using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MechrevoLite.Gpu;

/// <summary>
/// 2026-10-19（宝龙达 · 无 GCU 的 GPU 频率偏移）：NVAPI P-States 20 的 P0 时钟偏移封装。
/// <para>⭐ 为什么不用厂商 GCU：宝龙达机型没有 GCU，厂商的 <c>LCHWOC</c> / <c>GpuCoreClockOffsetOC</c>
/// 字段下发必然失败。NVAPI 的 <c>NvAPI_GPU_SetPstates20</c> 是驱动级接口，**不依赖 GCU**。</para>
/// <para>⭐ 单位：NVAPI 结构里的 <c>freqDelta_kHz</c> 是 **kHz**；本类对外全部用 **MHz**
/// （与 UI 滑块、厂商 7.3 参考实现的 <c>ChangePstatesLevel0Settings</c> 口径一致）。</para>
/// <para>⭐ 安全：所有入口都 try/catch，失败只返回 false 并写 <see cref="LastError" />，⛔ 不把异常抛给 UI。</para>
/// </summary>
internal static class NvapiPStates
{
	// ⭐⭐ 2026-10-19（UI 范围）：宝龙达下 UI 取「驱动读到的范围」与这个区间的**交集**。
	// 来源（只取数值语义，⛔ 未复制任何官方组件行为实现）：第三方 7.3 的 MinCoreOffset=-900 / MaxCoreOffset=2000。
	internal const int UiOffsetMinimumMhz = -900;

	internal const int UiOffsetMaximumMhz = 2000;

	private const int MaxPstates = 16;

	private const int MaxClocks = 8;

	private const int MaxBaseVoltages = 4;

	private const uint ClockDomainGraphics = 0u;

	private const uint ClockDomainMemory = 4u;

	private const uint PstateIdP0 = 0u;

	private const int NvapiMaxPhysicalGpus = 64;

	// NVAPI 的函数不是 nvapi64.dll 的直接导出，必须走 nvapi_QueryInterface(functionId)。
	// 下面这些 ID 是 NVAPI 公开接口编号（与官方头文件 / NvAPIWrapper 的 FunctionId 表一致）：
	//   NvAPI_Initialize            0x0150E828
	//   NvAPI_GetErrorMessage       0x6C2D048C
	//   NvAPI_EnumPhysicalGPUs      0xE5AC921F
	//   NvAPI_GPU_GetPstates20      0x6FF81213
	//   NvAPI_GPU_SetPstates20      0x0F4DAE6B
	//
	// ⛔⛔ 2026-10-18（本机实测崩溃，**别再改回去**）：**正常路径绝不调用 NvAPI_Unload**。
	//   LM 进程里 NvAPIWrapper（`NvidiaGpuControl`）也调 P-States20，它把 `nvapi_QueryInterface`
	//   返回的函数指针**缓存**起来且**从不 Initialize**。只要本类 Unload 一次，wrapper 下一次
	//   `GPUApi.EnumPhysicalGPUs()` 就直接 0xC0000005（本机 `nvapi-p0-probe --interop` 已复现，
	//   dump：crash at NvAPIWrapper.Native.GPUApi.EnumPhysicalGPUs）。
	//   ⇒ 本类改成「进程内只 Initialize 一次（静态标志、失败可重试）+ 永不 Unload」；
	//     进程退出由系统回收 NVAPI 状态。互操作实测 10/10 一致、无崩溃（同一探针的 --interop）。
	private const uint FunctionIdInitialize = 0x0150E828u;

	private const uint FunctionIdGetErrorMessage = 0x6C2D048Cu;

	private const uint FunctionIdEnumPhysicalGpus = 0xE5AC921Fu;

	private const uint FunctionIdGpuGetPstates20 = 0x6FF81213u;

	private const uint FunctionIdGpuSetPstates20 = 0x0F4DAE6Bu;

	private static readonly object Gate = new object();

	// ⭐ 进程内只 Initialize 一次：0=未初始化 / 1=已初始化。失败不置 1（下次调用可重试）。
	//   ⛔ 配套纪律：绝不 Unload（原因见文件上方 FunctionId 注释里的崩溃实测）。
	private static int _initialized;

	// ⚠️ ThreadStatic：UI 线程（读范围）与后台应用线程（写偏移）可能同时调用 NVAPI；
	//    用线程本地错误串，避免 A 线程刚失败、B 线程读到 A 的原因（只影响日志，不影响控制流）。
	[ThreadStatic]
	private static string _lastError;

	/// <summary>最近一次失败的中文原因（成功时为空串）。⛔ 只用于日志/探针，不参与控制流。</summary>
	internal static string LastError => _lastError ?? string.Empty;

	/// <summary>
	/// ⭐ 读 P0 的 core / memory 频率偏移（MHz）。P0 缺 core 或 memory 时钟域、NVAPI 不可用、无管理员权限时返回 false。
	/// </summary>
	internal static bool TryGetP0Offsets(out int coreMhz, out int memMhz)
	{
		coreMhz = 0;
		memMhz = 0;
		lock (Gate)
		{
			_lastError = string.Empty;
			NvapiApi api = null;
			try
			{
				if (!TryOpen(out api, out IntPtr gpu, out string error))
				{
					_lastError = error;
					return false;
				}
				if (!TryReadP0(api, gpu, out _, out NvapiPstate p0, out int coreIndex, out int memIndex, out error))
				{
					_lastError = error;
					return false;
				}
				coreMhz = ToMhz(p0.Clocks[coreIndex].FreqDelta.DeltaValue);
				memMhz = ToMhz(p0.Clocks[memIndex].FreqDelta.DeltaValue);
				return true;
			}
			catch (Exception ex)
			{
				_lastError = "NVAPI 读取 P0 偏移异常: " + ex.GetType().Name + ": " + ex.Message;
				return false;
			}
			finally
			{
				Close(api);
			}
		}
	}

	/// <summary>
	/// ⭐ 读 P0 的 core / memory 偏移范围（MHz，已按 kHz→MHz 取整：min 向上、max 向下）。
	/// 返回 false 表示范围不可用（P0 缺失 / 时钟域缺失 / 驱动给的范围无效）。
	/// </summary>
	internal static bool TryGetP0OffsetRange(out int coreMinMhz, out int coreMaxMhz, out int memMinMhz, out int memMaxMhz)
	{
		coreMinMhz = 0;
		coreMaxMhz = 0;
		memMinMhz = 0;
		memMaxMhz = 0;
		lock (Gate)
		{
			_lastError = string.Empty;
			NvapiApi api = null;
			try
			{
				if (!TryOpen(out api, out IntPtr gpu, out string error))
				{
					_lastError = error;
					return false;
				}
				if (!TryReadP0(api, gpu, out _, out NvapiPstate p0, out int coreIndex, out int memIndex, out error))
				{
					_lastError = error;
					return false;
				}
				NvapiParamDelta coreDelta = p0.Clocks[coreIndex].FreqDelta;
				NvapiParamDelta memDelta = p0.Clocks[memIndex].FreqDelta;
				coreMinMhz = ToMhzCeiling(coreDelta.RangeMinimum);
				coreMaxMhz = ToMhzFloor(coreDelta.RangeMaximum);
				memMinMhz = ToMhzCeiling(memDelta.RangeMinimum);
				memMaxMhz = ToMhzFloor(memDelta.RangeMaximum);
				if (coreMaxMhz <= coreMinMhz && memMaxMhz <= memMinMhz)
				{
					_lastError = $"P0 偏移范围无效: core={coreMinMhz}..{coreMaxMhz} mem={memMinMhz}..{memMaxMhz}";
					return false;
				}
				return true;
			}
			catch (Exception ex)
			{
				_lastError = "NVAPI 读取 P0 范围异常: " + ex.GetType().Name + ": " + ex.Message;
				return false;
			}
			finally
			{
				Close(api);
			}
		}
	}

	/// <summary>
	/// ⭐ 一次写入 P0 的 core + memory 偏移（MHz），随后**回读**；只有两者都与请求一致时才返回 true。
	/// <para>⚠️ 请求超出驱动范围 ⇒ 直接拒绝（不夹取、不硬写）。</para>
	/// <para>⚠️ 写入已被驱动接受但回读不一致 ⇒ 返回 false，并通过 <paramref name="appliedCoreMhz" /> /
	/// <paramref name="appliedMemMhz" /> 如实带回实际读到的值（⛔ 不假装成功）。</para>
	/// </summary>
	internal static bool TrySetP0Offsets(int coreMhz, int memMhz, out int appliedCoreMhz, out int appliedMemMhz)
	{
		appliedCoreMhz = 0;
		appliedMemMhz = 0;
		lock (Gate)
		{
			_lastError = string.Empty;
			NvapiApi api = null;
			try
			{
				if (!TryOpen(out api, out IntPtr gpu, out string error))
				{
					_lastError = error;
					return false;
				}
				if (!TryReadP0(api, gpu, out NvapiPstatesInfo info, out NvapiPstate p0, out int coreIndex, out int memIndex, out error))
				{
					_lastError = error;
					return false;
				}
				NvapiClockEntry coreEntry = p0.Clocks[coreIndex];
				NvapiClockEntry memEntry = p0.Clocks[memIndex];
				int currentCoreMhz = ToMhz(coreEntry.FreqDelta.DeltaValue);
				int currentMemMhz = ToMhz(memEntry.FreqDelta.DeltaValue);
				appliedCoreMhz = currentCoreMhz;
				appliedMemMhz = currentMemMhz;
				if (p0.IsEditable == 0 || coreEntry.IsEditable == 0 || memEntry.IsEditable == 0)
				{
					_lastError = $"P0 不可编辑: pstateEditable={p0.IsEditable} coreEditable={coreEntry.IsEditable} memEditable={memEntry.IsEditable}";
					return false;
				}
				if (!TryCheckRange("核心", coreMhz, coreEntry.FreqDelta, out error))
				{
					_lastError = error;
					return false;
				}
				if (!TryCheckRange("显存", memMhz, memEntry.FreqDelta, out error))
				{
					_lastError = error;
					return false;
				}
				// 0/0（关闭超频）或本来就是要写的值：不产生无意义的驱动写。
				if (coreMhz == currentCoreMhz && memMhz == currentMemMhz)
				{
					return true;
				}
				NvapiPstatesInfo request = NewInfo();
				request.IsEditable = info.IsEditable;
				request.NumPstates = 1u;
				request.NumClocks = 2u;
				request.NumBaseVoltages = 0u;
				request.Pstates[0].PstateId = PstateIdP0;
				request.Pstates[0].IsEditable = p0.IsEditable;
				// ⭐ 只改 freqDelta，其余字段（clockType / 频率/电压 data / 可编辑位）按驱动读回的原样带回，
				//    避免把 P0 的固定频率或电压信息一起改掉。
				request.Pstates[0].Clocks[0] = coreEntry;
				request.Pstates[0].Clocks[0].FreqDelta.DeltaValue = ToKhz(coreMhz);
				request.Pstates[0].Clocks[1] = memEntry;
				request.Pstates[0].Clocks[1].FreqDelta.DeltaValue = ToKhz(memMhz);
				int status = api.SetPstates20(gpu, ref request);
				if (status != 0)
				{
					_lastError = "NvAPI_GPU_SetPstates20 返回 " + api.Error(status);
					return false;
				}
				// ⭐ 驱动回读有滞后（实测通常 <120ms，偶发到秒级）：短重试，但**只认完全一致**。
				for (int attempt = 0; attempt < 6; attempt++)
				{
					Thread.Sleep((attempt == 0) ? 80 : 250);
					if (!TryReadP0(api, gpu, out _, out NvapiPstate readP0, out int readCoreIndex, out int readMemIndex, out error))
					{
						_lastError = "写入后回读失败: " + error;
						continue;
					}
					appliedCoreMhz = ToMhz(readP0.Clocks[readCoreIndex].FreqDelta.DeltaValue);
					appliedMemMhz = ToMhz(readP0.Clocks[readMemIndex].FreqDelta.DeltaValue);
					if (appliedCoreMhz == coreMhz && appliedMemMhz == memMhz)
					{
						_lastError = string.Empty;
						return true;
					}
				}
				_lastError = $"写入已被驱动接受，但回读不一致：请求 core={coreMhz} mem={memMhz} MHz，回读 core={appliedCoreMhz} mem={appliedMemMhz} MHz";
				return false;
			}
			catch (Exception ex)
			{
				_lastError = "NVAPI 写 P0 偏移异常: " + ex.GetType().Name + ": " + ex.Message;
				return false;
			}
			finally
			{
				Close(api);
			}
		}
	}

	private static bool TryOpen(out NvapiApi api, out IntPtr gpu, out string error)
	{
		api = null;
		gpu = IntPtr.Zero;
		error = null;
		try
		{
			api = NvapiApi.Load();
			if (!EnsureInitialized(api, out error))
			{
				api.Dispose();
				api = null;
				return false;
			}
			IntPtr[] handles = new IntPtr[NvapiMaxPhysicalGpus];
			int status = api.EnumPhysicalGPUs(handles, out uint count);
			if (status != 0)
			{
				error = "NvAPI_EnumPhysicalGPUs 返回 " + api.Error(status);
				api.Dispose();
				api = null;
				return false;
			}
			if (count == 0 || handles[0] == IntPtr.Zero)
			{
				error = "NVAPI 没有枚举到物理 GPU";
				api.Dispose();
				api = null;
				return false;
			}
			gpu = handles[0];
			return true;
		}
		catch (Exception ex)
		{
			error = "加载 NVAPI 失败: " + ex.GetType().Name + ": " + ex.Message;
			api?.Dispose();
			api = null;
			return false;
		}
	}

	/// <summary>
	/// 进程内只调一次 <c>NvAPI_Initialize</c>；成功后永久置位，**永不 Unload**
	/// （与 NvAPIWrapper 共用同一份 nvapi64 状态，Unload 会让 wrapper 后续调用 0xC0000005，已复现）。
	/// <para>初始化失败时不置位，下一次调用可重试；错误通过 <paramref name="error" /> 如实返回。</para>
	/// </summary>
	private static bool EnsureInitialized(NvapiApi api, out string error)
	{
		error = null;
		if (Volatile.Read(in _initialized) != 0)
		{
			return true;
		}
		int status = api.Initialize();
		if (status != 0)
		{
			error = "NvAPI_Initialize 返回 " + api.Error(status);
			return false;
		}
		Interlocked.Exchange(ref _initialized, 1);
		return true;
	}

	/// <summary>
	/// ⚠️ 只是释放本类的托管句柄，**不做 Unload**（见 <see cref="EnsureInitialized" /> 的说明）。
	/// </summary>
	private static void Close(NvapiApi api)
	{
		api?.Dispose();
	}

	private static bool TryReadP0(NvapiApi api, IntPtr gpu, out NvapiPstatesInfo info, out NvapiPstate p0, out int coreIndex, out int memIndex, out string error)
	{
		info = NewInfo();
		p0 = default(NvapiPstate);
		coreIndex = -1;
		memIndex = -1;
		int status = api.GetPstates20(gpu, ref info);
		if (status != 0)
		{
			error = "NvAPI_GPU_GetPstates20 返回 " + api.Error(status);
			return false;
		}
		int pstateCount = (int)Math.Min(info.NumPstates, (uint)MaxPstates);
		int clockCount = (int)Math.Min(info.NumClocks, (uint)MaxClocks);
		for (int i = 0; i < pstateCount; i++)
		{
			if (info.Pstates[i].PstateId != PstateIdP0)
			{
				continue;
			}
			p0 = info.Pstates[i];
			for (int clock = 0; clock < clockCount; clock++)
			{
				uint domain = p0.Clocks[clock].DomainId;
				if (domain == ClockDomainGraphics)
				{
					coreIndex = clock;
				}
				else if (domain == ClockDomainMemory)
				{
					memIndex = clock;
				}
			}
			if (coreIndex < 0 || memIndex < 0)
			{
				error = $"P0 时钟域不完整: numClocks={info.NumClocks} coreIndex={coreIndex} memIndex={memIndex}";
				return false;
			}
			error = null;
			return true;
		}
		error = $"NVAPI 未返回 P0（numPstates={info.NumPstates}）";
		return false;
	}

	private static bool TryCheckRange(string label, int mhz, NvapiParamDelta delta, out string error)
	{
		// 0 是"关闭/归零"的默认值：即使该时钟域的范围读不到，也允许写 0（不依赖驱动范围）。
		if (mhz == 0)
		{
			error = null;
			return true;
		}
		int minMhz = ToMhzCeiling(delta.RangeMinimum);
		int maxMhz = ToMhzFloor(delta.RangeMaximum);
		if (maxMhz <= minMhz)
		{
			error = $"{label}偏移范围不可用（驱动返回 {delta.RangeMinimum}..{delta.RangeMaximum} kHz）";
			return false;
		}
		if (mhz < minMhz || mhz > maxMhz)
		{
			error = $"{label}偏移 {mhz} MHz 超出驱动范围 {minMhz}..{maxMhz} MHz";
			return false;
		}
		error = null;
		return true;
	}

	private static NvapiPstatesInfo NewInfo()
	{
		NvapiPstatesInfo info = default(NvapiPstatesInfo);
		info.Version = PstatesInfoVersion;
		info.Pstates = new NvapiPstate[MaxPstates];
		for (int i = 0; i < MaxPstates; i++)
		{
			info.Pstates[i].Clocks = new NvapiClockEntry[MaxClocks];
			info.Pstates[i].BaseVoltages = new NvapiBaseVoltageEntry[MaxBaseVoltages];
			for (int clock = 0; clock < MaxClocks; clock++)
			{
				info.Pstates[i].Clocks[clock].Data = new uint[5];
			}
		}
		return info;
	}

	// NVAPI 的 MAKE_NVAPI_VERSION(type, 1) = sizeof(type) | (1 << 16)。结构体大小 7316 = 0x1C94，
	// 所以版本字是 0x00011C94；⛔ 不是常见的 0x00010001（那会让驱动报 INCOMPATIBLE_STRUCT_VERSION）。
	private static readonly uint PstatesInfoVersion = (uint)Marshal.SizeOf<NvapiPstatesInfo>() | (1u << 16);

	private static int ToMhz(int khz)
	{
		return (int)Math.Round(khz / 1000.0, MidpointRounding.AwayFromZero);
	}

	private static int ToMhzFloor(int khz)
	{
		return (int)Math.Floor(khz / 1000.0);
	}

	private static int ToMhzCeiling(int khz)
	{
		return (int)Math.Ceiling(khz / 1000.0);
	}

	private static int ToKhz(int mhz)
	{
		return checked(mhz * 1000);
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int NvapiInitializeDelegate();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int NvapiGetErrorMessageDelegate(int status, StringBuilder message);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int NvapiEnumPhysicalGpusDelegate([Out] IntPtr[] handles, out uint count);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int NvapiGpuGetPstates20Delegate(IntPtr gpu, ref NvapiPstatesInfo info);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int NvapiGpuSetPstates20Delegate(IntPtr gpu, ref NvapiPstatesInfo info);

	private sealed class NvapiApi : IDisposable
	{
		internal NvapiInitializeDelegate Initialize;

		internal NvapiGetErrorMessageDelegate GetErrorMessage;

		internal NvapiEnumPhysicalGpusDelegate EnumPhysicalGPUs;

		internal NvapiGpuGetPstates20Delegate GetPstates20;

		internal NvapiGpuSetPstates20Delegate SetPstates20;

		internal static NvapiApi Load()
		{
			NvapiApi api = new NvapiApi();
			api.Initialize = GetDelegate<NvapiInitializeDelegate>(FunctionIdInitialize);
			api.GetErrorMessage = GetDelegate<NvapiGetErrorMessageDelegate>(FunctionIdGetErrorMessage);
			api.EnumPhysicalGPUs = GetDelegate<NvapiEnumPhysicalGpusDelegate>(FunctionIdEnumPhysicalGpus);
			api.GetPstates20 = GetDelegate<NvapiGpuGetPstates20Delegate>(FunctionIdGpuGetPstates20);
			api.SetPstates20 = GetDelegate<NvapiGpuSetPstates20Delegate>(FunctionIdGpuSetPstates20);
			return api;
		}

		internal string Error(int status)
		{
			try
			{
				StringBuilder message = new StringBuilder(128);
				if (GetErrorMessage(status, message) == 0 && message.Length > 0)
				{
					return status.ToString() + " (0x" + ((uint)status).ToString("X8") + ") " + message;
				}
			}
			catch
			{
			}
			return status.ToString() + " (0x" + ((uint)status).ToString("X8") + ")";
		}

		public void Dispose()
		{
		}
	}

	private static T GetDelegate<T>(uint functionId) where T : Delegate
	{
		IntPtr pointer = NvapiQueryInterface(functionId);
		if (pointer == IntPtr.Zero)
		{
			throw new EntryPointNotFoundException("nvapi_QueryInterface(0x" + functionId.ToString("X8") + ") 返回空指针");
		}
		return Marshal.GetDelegateForFunctionPointer<T>(pointer);
	}

	[DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr NvapiQueryInterface(uint functionId);

	// ⚠️ 结构体顺序必须严格按官方 nvapi.h 的 P-States 20 定义（Pack=8）：
	//   NV_GPU_PERF_PSTATES20_INFO_V1
	//     version, bIsEditable, numPstates, numClocks, numBaseVoltages
	//     pstates[16]
	//   NV_GPU_PERF_PSTATES20_PSTATE
	//     pstateId, bIsEditable, clocks[8], baseVoltages[4]
	//   NV_GPU_PSTATE20_CLOCK_ENTRY_V1
	//     domainId, typeId, bIsEditable, freqDelta_kHz{value,min,max}, data[20 bytes]
	//   NV_GPU_PERF_PSTATE20_BASE_VOLTAGE_ENTRY_V1
	//     domainId, bIsEditable, volt_uV, voltDelta_uV{value,min,max}
	// ⛔ 不要凭记忆改字段顺序/大小；版本字由 Marshal.SizeOf 计算，结构体一旦改动会立刻在驱动侧报版本不兼容。
	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	private struct NvapiParamDelta
	{
		public int DeltaValue;

		public int RangeMinimum;

		public int RangeMaximum;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	private struct NvapiClockEntry
	{
		public uint DomainId;

		public uint ClockType;

		public uint IsEditable;

		public NvapiParamDelta FreqDelta;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
		public uint[] Data;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	private struct NvapiBaseVoltageEntry
	{
		public uint DomainId;

		public uint IsEditable;

		public uint Value;

		public NvapiParamDelta ValueDelta;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	private struct NvapiPstate
	{
		public uint PstateId;

		public uint IsEditable;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxClocks)]
		public NvapiClockEntry[] Clocks;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxBaseVoltages)]
		public NvapiBaseVoltageEntry[] BaseVoltages;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	private struct NvapiPstatesInfo
	{
		public uint Version;

		public uint IsEditable;

		public uint NumPstates;

		public uint NumClocks;

		public uint NumBaseVoltages;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxPstates)]
		public NvapiPstate[] Pstates;
	}
}
