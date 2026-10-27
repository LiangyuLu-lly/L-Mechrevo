using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite.Helpers;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;

namespace MechrevoLite.Gpu.NVidia;

public class NvidiaGpuControl : IGpuControl, IDisposable, IGpuOverclockControl
{
	private enum GpuState
	{
		Active,
		Asleep,
		Off
	}

	public static int MinClockLimit = AppConfig.Get("min_gpu_clock", 400);

	public const int MaxClockLimit = 3000;

	private PhysicalGPU? _internalGpu;

	private bool _writeAccessDenied;

	private readonly Func<bool> _isElevated;

	public int? _lastTemp;

	public int _lastTempTime;

	private static bool verboseLog = false;

	private GpuState _lastState = GpuState.Off;

	private long _lastStateTime = -500L;

	private const int StateCacheMs = 500;

	private Task<int?>? _readTask;

	private static readonly HashSet<string> _systemProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"dwm", "csrss", "winlogon", "services", "lsass", "smss", "wininit", "svchost", "fontdrvhost", "igfxem",
		"igfxhk", "igfxext", "nvcontainer", "nvdisplay.container", "nvsettings", "nvspcaps64", "nvsphelper64", "nvwmi64", "nvcplui", "atieclxx",
		"atiesrxx", "explorer", "taskhostw", "sihost", "runtimebroker", "shellexperiencehost", "searchhost", "startmenuexperiencehost", "textinputhost", "applicationframehost",
		"systemsettings", "dllhost", "conhost", "audiodg", "ctfloader", "spoolsv", "wlanext", "msdtc"
	};

	public GpuClockOffsetRange CoreOffset { get; private set; } = new GpuClockOffsetRange(0, 0, 0);

	public GpuClockOffsetRange MemoryOffset { get; private set; } = new GpuClockOffsetRange(0, 0, 0);

	public bool IsAvailable
	{
		get
		{
			if (!_writeAccessDenied && IsValid)
			{
				if (!CoreOffset.IsAdjustable)
				{
					return MemoryOffset.IsAdjustable;
				}
				return true;
			}
			return false;
		}
	}

	public bool WritesRequireElevation => !_isElevated();

	public string Name
	{
		get
		{
			if (!IsValid)
			{
				return "NVIDIA GPU";
			}
			return FullName;
		}
	}

	public bool IsValid => _internalGpu != null;

	public bool IsNvidia => IsValid;

	public string FullName => _internalGpu.FullName;

	public NvidiaGpuControl()
		: this(ProcessHelper.IsUserAdministrator)
	{
	}

	internal NvidiaGpuControl(Func<bool> isElevated)
	{
		_isElevated = isElevated;
		_internalGpu = GetInternalDiscreteGpu();
	}

	public static IGpuOverclockControl? TryCreateOverclockControl()
	{
		return TryCreateOverclockControl(ProcessHelper.IsUserAdministrator);
	}

	internal static IGpuOverclockControl? TryCreateOverclockControl(Func<bool> isElevated)
	{
		NvidiaGpuControl nvidiaGpuControl = new NvidiaGpuControl(isElevated);
		if (nvidiaGpuControl.Refresh())
		{
			return nvidiaGpuControl;
		}
		nvidiaGpuControl.Dispose();
		return null;
	}

	internal static bool TryGetActiveDgpuApplications(out IReadOnlyList<DgpuApplication> applications)
	{
		PhysicalGPU internalDiscreteGpu = GetInternalDiscreteGpu();
		if ((object)internalDiscreteGpu == null)
		{
			applications = Array.Empty<DgpuApplication>();
			return false;
		}
		try
		{
			int num = 0;
			Process[] activeApplications;
			while (true)
			{
				try
				{
					activeApplications = internalDiscreteGpu.GetActiveApplications();
				}
				catch (Exception ex)
				{
					num++;
					if (num >= 2)
					{
						throw;
					}
					Logger.WriteLine("dGPU application enumeration hit an exiting process, retrying: " + ex.Message);
					Thread.Sleep(200);
					continue;
				}
				break;
			}
			List<DgpuApplication> list = new List<DgpuApplication>(activeApplications.Length);
			bool flag = false;
			Process[] array = activeApplications;
			foreach (Process process in array)
			{
				using (process)
				{
					try
					{
						DateTime value;
						try
						{
							value = process.StartTime.ToUniversalTime();
						}
						catch (Exception ex2)
						{
							Logger.WriteLine("dGPU application enumeration blocked because its start time is unavailable: " + ex2.Message);
							flag = true;
							goto end_IL_006f;
						}
						list.Add(new DgpuApplication(process.Id, process.ProcessName, process.SessionId, value, process.MainWindowHandle != IntPtr.Zero));
						end_IL_006f:;
					}
					catch (Exception ex3)
					{
						Logger.WriteLine("dGPU application query failed: " + ex3.Message);
					}
				}
			}
			MergeComputeApplications(list);
			if (flag)
			{
				applications = Array.Empty<DgpuApplication>();
				return false;
			}
			applications = list;
			return true;
		}
		catch (Exception ex4)
		{
			Logger.WriteLine("dGPU application enumeration unavailable: " + ex4.Message);
			applications = Array.Empty<DgpuApplication>();
			return false;
		}
	}

	private static void MergeComputeApplications(List<DgpuApplication> result)
	{
		try
		{
			IReadOnlyList<(int, string)> readOnlyList = NvidiaSmi.QueryComputeApplications();
			if (readOnlyList == null || readOnlyList.Count == 0)
			{
				return;
			}
			HashSet<int> hashSet = new HashSet<int>(result.Select((DgpuApplication a) => a.ProcessId));
			foreach (var item2 in readOnlyList)
			{
				int item = item2.Item1;
				if (!hashSet.Add(item))
				{
					continue;
				}
				try
				{
					using Process process = Process.GetProcessById(item);
					result.Add(new DgpuApplication(item, process.ProcessName, process.SessionId, process.StartTime.ToUniversalTime(), process.MainWindowHandle != IntPtr.Zero));
				}
				catch (Exception ex)
				{
					Logger.WriteLineThrottled("dgpu-compute-enum", $"Compute app {item} exited before identity read: {ex.Message}");
				}
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLineThrottled("dgpu-compute-enum", "Compute application query failed: " + ex2.Message);
		}
	}

	private GpuState GetGpuState()
	{
		if (!IsValid)
		{
			return GpuState.Off;
		}
		if (Environment.TickCount64 - _lastStateTime < 500)
		{
			return _lastState;
		}
		try
		{
			PerformanceStateId currentPerformanceState = GPUApi.GetCurrentPerformanceState(_internalGpu.Handle);
			if (verboseLog)
			{
				Logger.WriteLine($"GPU: {currentPerformanceState}");
			}
			_lastState = GpuState.Active;
		}
		catch (Exception ex)
		{
			if (verboseLog)
			{
				Logger.WriteLine("GPU: " + ex.Message);
			}
			_lastState = ((ex.Message == "NVAPI_GPU_NOT_POWERED") ? GpuState.Asleep : GpuState.Off);
		}
		_lastStateTime = Environment.TickCount64;
		return _lastState;
	}

	public int? ReadCurrentTemperature(bool log = false)
	{
		if (!IsValid)
		{
			return null;
		}
		IThermalSettings thermalSettings = GPUApi.GetThermalSettings(_internalGpu.Handle);
		if (thermalSettings.Sensors == null)
		{
			return null;
		}
		IThermalSensor thermalSensor = thermalSettings.Sensors.FirstOrDefault((IThermalSensor s) => s.Target == ThermalSettingsTarget.GPU);
		if (log || verboseLog)
		{
			Logger.WriteLine($"GPU Temp: {thermalSensor?.CurrentTemperature}C");
		}
		return thermalSensor?.CurrentTemperature;
	}

	public int? GetCurrentTemperature()
	{
		if (!IsValid)
		{
			return null;
		}
		GpuState gpuState = GetGpuState();
		if (gpuState == GpuState.Off)
		{
			return null;
		}
		Task<int?>? readTask = _readTask;
		if ((readTask == null || readTask.IsCompleted) && (gpuState == GpuState.Active || ShouldRefresh()))
		{
			_readTask = Task.Run(() =>
			{
				int? num = ReadCurrentTemperature();
				if (num.HasValue)
				{
					_lastTemp = num;
					_lastTempTime = Environment.TickCount;
				}
				return num;
			});
		}
		_readTask?.Wait(500);
		return _lastTemp;
	}

	private bool ShouldRefresh()
	{
		int? lastTemp = _lastTemp;
		if (!lastTemp.HasValue)
		{
			return true;
		}
		float num = HardwareControl.GetCPUTemp();
		float num2 = (float)_lastTemp.Value - num;
		if (num2 < 5f)
		{
			return false;
		}
		float num3 = Math.Clamp((num2 - 5f) / 15f, 0f, 1f);
		int num4 = (int)(120000f - num3 * 115000f);
		bool flag = Environment.TickCount > _lastTempTime + num4;
		if (verboseLog)
		{
			Logger.WriteLine($"GPU Temp Refresh Interval: {num4}ms {flag}");
		}
		return flag;
	}

	public void Dispose()
	{
		_internalGpu = null;
	}

	public void KillGPUApps()
	{
		if (!IsValid)
		{
			return;
		}
		PhysicalGPU internalGpu = _internalGpu;
		int id = Process.GetCurrentProcess().Id;
		try
		{
			Process[] activeApplications = internalGpu.GetActiveApplications();
			foreach (Process process in activeApplications)
			{
				try
				{
					if (process.Id != id && process.SessionId != 0 && !_systemProcessNames.Contains(process.ProcessName))
					{
						Logger.WriteLine("Kill:" + process.ProcessName);
						ProcessHelper.KillByProcess(process);
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine(ex.Message);
				}
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLine(ex2.Message);
		}
	}

	public bool Refresh()
	{
		if (!IsValid)
		{
			return false;
		}
		try
		{
			IPerformanceStates20Info performanceStates = GPUApi.GetPerformanceStates20(_internalGpu.Handle);
			if (!performanceStates.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out var value))
			{
				return false;
			}
			CoreOffset = ReadOffsetRange(value, PublicClockDomain.Graphics);
			MemoryOffset = ReadOffsetRange(value, PublicClockDomain.Memory);
			int num;
			int num2;
			if (!_writeAccessDenied && performanceStates.IsEditable)
			{
				if (!CoreOffset.IsAdjustable)
				{
					num = (MemoryOffset.IsAdjustable ? 1 : 0);
					if (num == 0)
					{
						goto IL_008b;
					}
				}
				else
				{
					num = 1;
				}
				num2 = (_isElevated() ? 1 : 0);
				goto IL_008c;
			}
			num = 0;
			goto IL_008b;
			IL_008b:
			num2 = 0;
			goto IL_008c;
			IL_008c:
			bool value2 = (byte)num2 != 0;
			Logger.WriteLineThrottled("nvidia-oc-capability", $"NVIDIA OC capability: {FullName}, editable={performanceStates.IsEditable}, writable={value2}, core={CoreOffset.Minimum}..{CoreOffset.Maximum} current={CoreOffset.Current}, memory={MemoryOffset.Minimum}..{MemoryOffset.Maximum} current={MemoryOffset.Current}", 10000);
			return (byte)num != 0;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("NVIDIA OC capability unavailable: " + ex.Message);
			GpuClockOffsetRange coreOffset = (MemoryOffset = new GpuClockOffsetRange(0, 0, 0));
			CoreOffset = coreOffset;
			return false;
		}
	}

	private static GpuClockOffsetRange ReadOffsetRange(IEnumerable<IPerformanceStates20ClockEntry> clocks, PublicClockDomain domain)
	{
		IPerformanceStates20ClockEntry performanceStates20ClockEntry = clocks.FirstOrDefault((IPerformanceStates20ClockEntry item) => item.DomainId == domain && item.IsEditable);
		if (performanceStates20ClockEntry == null)
		{
			return new GpuClockOffsetRange(0, 0, 0);
		}
		PerformanceStates20ParameterDelta frequencyDeltaInkHz = performanceStates20ClockEntry.FrequencyDeltaInkHz;
		int minimum = (int)Math.Ceiling((double)frequencyDeltaInkHz.DeltaRange.Minimum / 1000.0);
		int maximum = (int)Math.Floor((double)frequencyDeltaInkHz.DeltaRange.Maximum / 1000.0);
		int current = (int)Math.Round((double)frequencyDeltaInkHz.DeltaValue / 1000.0, MidpointRounding.AwayFromZero);
		return new GpuClockOffsetRange(minimum, maximum, current);
	}

	public bool SetCoreOffset(int value)
	{
		return SetClockOffset(PublicClockDomain.Graphics, value);
	}

	public bool SetMemoryOffset(int value)
	{
		return SetClockOffset(PublicClockDomain.Memory, value);
	}

	private bool SetClockOffset(PublicClockDomain domain, int value)
	{
		if (!Refresh())
		{
			return false;
		}
		GpuClockOffsetRange gpuClockOffsetRange = ((domain == PublicClockDomain.Graphics) ? CoreOffset : MemoryOffset);
		if (!gpuClockOffsetRange.Contains(value))
		{
			Logger.WriteLine($"NVIDIA OC rejected outside driver range: domain={domain}, value={value}, range={gpuClockOffsetRange.Minimum}..{gpuClockOffsetRange.Maximum}");
			return false;
		}
		if (gpuClockOffsetRange.Current == value)
		{
			return true;
		}
		if (!_isElevated())
		{
			Logger.WriteLineThrottled("nvidia-oc-needs-elevation", $"NVIDIA OC write skipped (requires administrator): domain={domain}, value={value}");
			return false;
		}
		try
		{
			PerformanceStates20ClockEntryV1 performanceStates20ClockEntryV = new PerformanceStates20ClockEntryV1(domain, new PerformanceStates20ParameterDelta(value * 1000));
			PerformanceStates20InfoV1.PerformanceState20[] performanceStates = new PerformanceStates20InfoV1.PerformanceState20[1]
			{
				new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, new PerformanceStates20ClockEntryV1[1] { performanceStates20ClockEntryV }, Array.Empty<PerformanceStates20BaseVoltageEntryV1>())
			};
			GPUApi.SetPerformanceStates20(performanceStates20Info: new PerformanceStates20InfoV1(performanceStates, 1u, 0u), physicalGPUHandle: _internalGpu.Handle);
			int num = gpuClockOffsetRange.Current;
			bool flag = false;
			int[] array = new int[4] { 60, 120, 240, 400 };
			for (int i = 0; i < array.Length; i++)
			{
				Thread.Sleep(array[i]);
				bool flag2 = Refresh();
				num = ((domain == PublicClockDomain.Graphics) ? CoreOffset.Current : MemoryOffset.Current);
				if (flag2 && num == value)
				{
					flag = true;
					break;
				}
			}
			Logger.WriteLine($"NVIDIA OC set: domain={domain}, requested={value}, actual={num}, confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			if (ex.Message.Contains("NVAPI_INVALID_USER_PRIVILEGE", StringComparison.OrdinalIgnoreCase))
			{
				_writeAccessDenied = true;
				Logger.WriteLine("NVIDIA OC write access denied; disabling the direct backend for this session.");
			}
			Logger.WriteLine($"NVIDIA OC set failed: domain={domain}, value={value}, error={ex.Message}");
			return false;
		}
	}

	private static bool RunPowershellCommand(string script, int timeoutMs = 0)
	{
		try
		{
			ProcessHelper.RunCMD("powershell", script, null, timeoutMs);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine(ex.ToString());
			return false;
		}
	}

	public int GetMaxGPUCLock()
	{
		PhysicalGPU internalGpu = _internalGpu;
		try
		{
			int result = (int)GPUApi.GetClockBoostLock(internalGpu.Handle).ClockBoostLocks[0].VoltageInMicroV / 1000;
			Logger.WriteLine("GET CLOCK LIMIT: " + result);
			return result;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GET CLOCK LIMIT: " + ex.Message);
			return -1;
		}
	}

	public int SetMaxGPUClock(int clock)
	{
		if (clock < MinClockLimit || clock >= 3000)
		{
			clock = 0;
		}
		if (GetMaxGPUCLock() == clock)
		{
			return 0;
		}
		if (clock > 0)
		{
			RunPowershellCommand($"nvidia-smi -lgc 0,{clock}");
		}
		else
		{
			RunPowershellCommand("nvidia-smi -rgc");
		}
		return 1;
	}

	public static void RestartNvContainer()
	{
		if (ProcessHelper.IsUserAdministrator())
		{
			RunPowershellCommand("Restart-Service -Name 'NvContainerLocalSystem' -Force", 30000);
		}
	}

	public static void RestartNVService()
	{
		if (ProcessHelper.IsUserAdministrator())
		{
			RunPowershellCommand("Restart-Service -Name 'NVDisplay.ContainerLocalSystem' -Force", 30000);
			RunPowershellCommand("Restart-Service -Name 'NvContainerLocalSystem' -Force", 30000);
		}
	}

	public static void StopNVService()
	{
		if (ProcessHelper.IsUserAdministrator())
		{
			RunPowershellCommand("Stop-Service -Name 'NvContainerLocalSystem' -Force", 30000);
			RunPowershellCommand("Stop-Service -Name 'NVDisplay.ContainerLocalSystem' -Force", 30000);
		}
	}

	public int SetClocks(int core, int memory)
	{
		if (!Refresh())
		{
			return -1;
		}
		if (!CoreOffset.Contains(core) || !MemoryOffset.Contains(memory))
		{
			return 0;
		}
		bool flag = CoreOffset.Current != core;
		bool flag2 = MemoryOffset.Current != memory;
		if (!flag && !flag2)
		{
			return 0;
		}
		if (flag && !SetCoreOffset(core))
		{
			return -1;
		}
		if (flag2 && !SetMemoryOffset(memory))
		{
			return -1;
		}
		return 1;
	}

	private static PhysicalGPU? GetInternalDiscreteGpu()
	{
		try
		{
			PhysicalGPU[] physicalGPUs = PhysicalGPU.GetPhysicalGPUs();
			return physicalGPUs.FirstOrDefault((PhysicalGPU gpu) => gpu.SystemType == SystemType.Laptop) ?? physicalGPUs.FirstOrDefault();
		}
		catch (Exception)
		{
			return null;
		}
	}

	public int? GetGpuUse()
	{
		if (!IsValid)
		{
			return null;
		}
		if (GetGpuState() != GpuState.Active)
		{
			return null;
		}
		return (int?)GPUApi.GetUsages(_internalGpu.Handle).GPU?.Percentage;
	}

	public float? GetGpuPower()
	{
		if (!IsValid)
		{
			return null;
		}
		switch (GetGpuState())
		{
		case GpuState.Off:
			NvmlHelper.Shutdown();
			return null;
		default:
			return 0f;
		case GpuState.Active:
			return NvmlHelper.GetGpuPower().GetValueOrDefault();
		}
	}

	public (long usedMb, long totalMb)? GetVramInfo()
	{
		if (!IsValid)
		{
			return null;
		}
		if (GetGpuState() != GpuState.Active)
		{
			return null;
		}
		return NvmlHelper.GetMemoryInfo();
	}
}
