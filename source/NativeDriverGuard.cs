using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// 2026-10-09（本地功耗采样 NVML 原生崩溃修复）：进程内原生驱动模块（nvml.dll / nvapi64.dll）存活守卫。
///
/// 事故依据（用户提供的 .NET Runtime 1026 / Application Error 1000）：
///   异常代码 0xc0000005，出错模块 coreclr.dll，栈顶为
///   LibreHardwareMonitor.Interop.NvidiaML.NvmlDeviceGetPowerUsage(...) ← Gpu.NvidiaGpu.Update()
///   ← MechrevoLite.Hardware.LhmMonitor.Update() ← HardwareControl.RefreshLocalMonitoring()
///   ← SampleLocalPower() ← CustomModeForm.UpdatePowerWallVerdict() ← 构造函数里挂的 2s Timer。
///
/// 原理：CoreCLR 的 P/Invoke 桩在首次调用后会**缓存导出函数地址**（调用点缓存），
/// 一旦 nvml.dll 被驱动更新/TDR/卸载流程 unmap（或被重载到别的基址），那个缓存地址就指向未映射内存；
/// 下一次调用会在桩内（coreclr.dll）的间接 call 指令上触发 0xc0000005 —— 正好解释了「出错模块 coreclr.dll」。
/// 这类 AV 在 .NET 上通常不可捕获，会直接终止进程（所以没有 crash.txt）。
///
/// 因此必须在**发原生调用之前**用廉价且安全的 kernel32 查询确认模块还在、且基址没变：
/// GetModuleHandleW 只查本进程加载器链表，不进入任何驱动，不会触发原生 AV。
/// 判定为失效时调用方必须完全跳过该原生库（缓存地址已不可信，无法在本进程内安全重解析）。
/// </summary>
internal static class NativeDriverGuard
{
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
	private static extern nint GetModuleHandleW(string lpModuleName);

	private static readonly object _sync = new object();

	private static readonly Dictionary<string, nint> _baseline = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);

	/// <summary>查询模块当前加载基址；0 = 未加载/已被卸载。只走 kernel32，不进入驱动。</summary>
	internal static nint Query(string moduleName)
	{
		if (string.IsNullOrEmpty(moduleName))
		{
			return 0;
		}
		try
		{
			return GetModuleHandleW(moduleName);
		}
		catch
		{
			return 0;
		}
	}

	/// <summary>记录基线（模块名 → 初始化成功时的基址）。基址为 0 表示这次拿不到句柄，记 0（等价于「无法守卫」）。</summary>
	internal static void Remember(string moduleName, nint baseAddress)
	{
		if (string.IsNullOrEmpty(moduleName))
		{
			return;
		}
		lock (_sync)
		{
			_baseline[moduleName] = baseAddress;
		}
	}

	internal static void Forget(string moduleName)
	{
		if (string.IsNullOrEmpty(moduleName))
		{
			return;
		}
		lock (_sync)
		{
			_baseline.Remove(moduleName);
		}
	}

	internal static bool TryGetBaseline(string moduleName, out nint baseline)
	{
		baseline = 0;
		if (string.IsNullOrEmpty(moduleName))
		{
			return false;
		}
		lock (_sync)
		{
			return _baseline.TryGetValue(moduleName, out baseline);
		}
	}

	/// <summary>
	/// 已记录非 0 基线，而当前基址为 0（模块已被卸载）或与基线不同（被重载到别的基址）
	/// ⇒ 之前缓存的导出函数地址/设备句柄全部失效，绝不能再调用。
	/// </summary>
	internal static bool IsStale(string moduleName, out nint current)
	{
		current = Query(moduleName);
		if (!TryGetBaseline(moduleName, out nint baseline) || baseline == 0)
		{
			return false;
		}
		return current != baseline;
	}
}
