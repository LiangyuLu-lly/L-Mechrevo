// NVIDIA 机型（有 dGPU）不会调用本类（调用点已按 CpuVendor.IsAmd + NoDgpu 门控）。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MechrevoLite.Hardware;

namespace MechrevoLite.Hardware;

internal static class AdlPowerReader
{
	private const string Dll = "atiadlxx.dll";

	private const int MaxPmLogSensors = 256;

	private static readonly object Gate = new object();

	private static bool _initialized;

	private static bool _available;

	private static IntPtr _context = IntPtr.Zero;

	private static int _adapterCount;

	private static readonly Dictionary<int, string> SensorNames = new Dictionary<int, string>
	{
		[1] = "CORE_CLOCK",
		[2] = "MEMORY_CLOCK",
		[3] = "ASIC_POWER",
		[4] = "GPU_TEMPERATURE",
		[5] = "FAN_SPEED",
		[6] = "GPU_LOAD",
		[7] = "GPU_LOAD_ALT",
		[8] = "GPU_VOLTAGE",
		[9] = "ASIC_POWER_ALT",
		[10] = "MEMORY_USAGE",
		[11] = "GPU_POWER",
		[12] = "SOC_POWER",
		[13] = "GFX_POWER",
		[14] = "GPU_CLOCK",
		[15] = "MEMORY_CLOCK_ALT"
	};

	private static float? _lastValue;
	private static int _stableCount;
	private static bool _frozen;
	private static int _changeCount;

	private delegate IntPtr AllocCallback(int size);

	private static readonly AllocCallback Alloc = (int size) => Marshal.AllocHGlobal(size);

	[StructLayout(LayoutKind.Sequential)]
	private struct PmLogSensor
	{
		public int Supported;
		public int Value;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct PmLogOutput
	{
		public int Size;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxPmLogSensors)]
		public PmLogSensor[] Sensors;
	}

	[DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
	private static extern int ADL2_Main_Control_Create(AllocCallback callback, int enumConnectedAdapters, out IntPtr context);

	[DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
	private static extern int ADL2_Main_Control_Destroy(IntPtr context);

	[DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
	private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int numAdapters);

	[DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
	private static extern int ADL2_New_QueryPMLogData_Get(IntPtr context, int adapterIndex, ref PmLogOutput data);

	/// <summary>⭐ ADL 是否可用（初始化成功；只读）✅</summary>
	internal static bool Available => EnsureInitialized();

	/// <summary>⭐ 最近一次读数是否被冻结判据判定为「疑似冻结」✅</summary>
	internal static bool Frozen => _frozen;

	/// <summary>⭐ 只读读取一次 AMD 核显功耗（W）；不可用/读不到返回 false。⛔ 不做任何写操作。</summary>
	internal static bool TryRead(out float watts, out string source)
	{
		watts = 0f;
		source = "";
		if (!EnsureInitialized())
		{
			return false;
		}
		PmLogOutput output = new PmLogOutput
		{
			Size = Marshal.SizeOf<PmLogOutput>(),
			Sensors = new PmLogSensor[MaxPmLogSensors]
		};
		int adapterCount = Math.Max(1, _adapterCount);
		for (int adapterIndex = 0; adapterIndex < adapterCount; adapterIndex++)
		{
			if (ADL2_New_QueryPMLogData_Get(_context, adapterIndex, ref output) != 0)
			{
				continue;
			}
			List<string> all = new List<string>();
			List<(int Id, int Value, string Name)> powerSensors = new List<(int, int, string)>();
			for (int i = 0; i < MaxPmLogSensors; i++)
			{
				PmLogSensor sensor = output.Sensors[i];
				if (sensor.Supported == 0)
				{
					continue;
				}
				string name = SensorNames.TryGetValue(i, out string known) ? known : ("PM" + i);
				all.Add(name + "(0x" + i.ToString("X2") + ")=" + sensor.Value);
				if (name.IndexOf("POWER", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					powerSensors.Add((i, sensor.Value, name));
				}
			}
			if (all.Count == 0)
			{
				continue;
			}
			string allLog = "[adl-power] adapter=" + adapterIndex + " PMLog 传感器：" + string.Join(" · ", all);
			if (Logger.TryMarkChanged("adl-power-sensors-" + adapterIndex, allLog))
			{
				Logger.WriteError(allLog);
			}
			if (powerSensors.Count == 0)
			{
				continue;
			}
			int bestId = -1;
			int bestValue = 0;
			string bestName = "";
			int bestPriority = int.MaxValue;
			foreach ((int Id, int Value, string Name) candidate in powerSensors)
			{
				int priority = candidate.Name switch
				{
					"GPU_POWER" => 0,
					"ASIC_POWER" => 1,
					"ASIC_POWER_ALT" => 2,
					"SOC_POWER" => 3,
					"GFX_POWER" => 4,
					_ => 9
				};
				if (candidate.Value > 0 && priority < bestPriority)
				{
					bestPriority = priority;
					bestId = candidate.Id;
					bestValue = candidate.Value;
					bestName = candidate.Name;
				}
			}
			if (bestId < 0)
			{
				continue;
			}
			// ADL PMLog 不同代际单位不同：>1000 视为 mW，否则视为 W（两种都打印原始值便于机主核对）✅
			float raw = bestValue;
			float normalized = (raw > 1000f) ? (raw / 1000f) : raw;
			watts = normalized;
			source = "ADL PMLog " + bestName + "(0x" + bestId.ToString("X2") + ")";
			float rounded = (float)Math.Round(normalized, 1);
			if (!_lastValue.HasValue || Math.Abs(_lastValue.Value - rounded) > 0.05f)
			{
				_changeCount++;
				_stableCount = 0;
				_frozen = false;
				_lastValue = rounded;
			}
			else
			{
				_stableCount++;
				if (_stableCount >= 6 && !_frozen)
				{
					_frozen = true;
					Logger.WriteError("[gpu-power] ⚠️ ADL 读数疑似冻结（恒 " + rounded.ToString("F1") + "W）⇒ 悬浮窗显示「—」");
				}
			}
			string selected = "[adl-power] 选中源=" + source + " ⇒ 原始=" + bestValue + " · 换算=" + normalized.ToString("F1")
				+ "W（变化次数=" + _changeCount + "）";
			if (Logger.TryMarkChanged("adl-power-selected", selected))
			{
				Logger.WriteError(selected);
			}
			if (_frozen)
			{
				return false;
			}
			return true;
		}
		return false;
	}

	/// <summary>只读 PMLog：GPU 核心负载 / 核心频率 / 显存频率 / 显存占用（AMD 核显）。</summary>
	internal static bool TryReadTelemetry(out int loadPercent, out int coreMhz, out int memoryMhz, out int memoryUsedMb, out int temperatureC)
	{
		loadPercent = -1;
		coreMhz = -1;
		memoryMhz = -1;
		memoryUsedMb = -1;
		temperatureC = -1;
		if (!EnsureInitialized())
		{
			return false;
		}
		PmLogOutput output = new PmLogOutput
		{
			Size = Marshal.SizeOf<PmLogOutput>(),
			Sensors = new PmLogSensor[MaxPmLogSensors]
		};
		int adapterCount = Math.Max(1, _adapterCount);
		bool any = false;
		for (int adapterIndex = 0; adapterIndex < adapterCount; adapterIndex++)
		{
			if (ADL2_New_QueryPMLogData_Get(_context, adapterIndex, ref output) != 0)
			{
				continue;
			}
			for (int i = 0; i < MaxPmLogSensors; i++)
			{
				PmLogSensor sensor = output.Sensors[i];
				if (sensor.Supported == 0 || sensor.Value <= 0)
				{
					continue;
				}
				switch (i)
				{
					case 4:
						if (temperatureC < 0 && sensor.Value > 0 && sensor.Value < 150) { temperatureC = sensor.Value; any = true; }
						break;
					case 6:
					case 7:
						if (loadPercent < 0)
						{
							int normalized = sensor.Value > 100 ? (int)Math.Round(sensor.Value / 100.0) : sensor.Value;
							if (normalized >= 0 && normalized <= 100) { loadPercent = normalized; any = true; }
						}
						break;
					case 1:
					case 14:
						if (coreMhz < 0) { coreMhz = sensor.Value; any = true; }
						break;
					case 2:
					case 15:
						if (memoryMhz < 0) { memoryMhz = sensor.Value; any = true; }
						break;
					case 10:
						if (memoryUsedMb < 0)
						{
							memoryUsedMb = sensor.Value > 1000000 ? (int)Math.Round(sensor.Value / 1024.0 / 1024.0) : sensor.Value;
							any = true;
						}
						break;
				}
			}
			if (any)
			{
				break;
			}
		}
		return any;
	}

	private static bool EnsureInitialized()
	{
		lock (Gate)
		{
			if (_initialized)
			{
				return _available;
			}
			_initialized = true;
			try
			{
				int hr = ADL2_Main_Control_Create(Alloc, 1, out _context);
				if (hr != 0 || _context == IntPtr.Zero)
				{
					Logger.WriteLine("[adl-power] ADL2 初始化失败 hr=0x" + hr.ToString("X8") + "（AMD 核显功耗不可用）");
					return _available = false;
				}
				if (ADL2_Adapter_NumberOfAdapters_Get(_context, out _adapterCount) != 0 || _adapterCount <= 0)
				{
					_adapterCount = 1;
				}
				Logger.WriteError("[adl-power] ADL2 初始化成功 · adapterCount=" + _adapterCount + "（只读 PMLog 查询）");
				return _available = true;
			}
			catch (DllNotFoundException)
			{
				Logger.WriteLine("[adl-power] atiadlxx.dll 不存在 ⇒ ADL 核显功耗不可用");
				return _available = false;
			}
			catch (EntryPointNotFoundException)
			{
				Logger.WriteLine("[adl-power] atiadlxx.dll 缺 ADL2 PMLog 入口 ⇒ ADL 核显功耗不可用");
				return _available = false;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[adl-power] ADL 初始化异常（已吞掉）：" + ex.Message);
				return _available = false;
			}
		}
	}
}
