using System;
using System.Runtime.InteropServices;
using MechrevoLite.Helpers;

namespace MechrevoLite.Gpu;

internal static class UefiDisplayModeDiagnostics
{
	private struct Luid
	{
		public uint LowPart;

		public int HighPart;
	}

	private struct TokenPrivileges
	{
		public uint PrivilegeCount;

		public Luid Luid;

		public uint Attributes;
	}

	private const string VariableName = "UniWillVariable";

	private const string VariableGuid = "{9f33f85c-13ca-4fd1-9c4a-96217722c593}";

	private const int DisplayModeOffset = 98;

	private const int ErrorEnvVarNotFound = 203;

	private const uint TokenAdjustPrivileges = 32u;

	private const uint TokenQuery = 8u;

	private const uint SePrivilegeEnabled = 2u;

	private static GpuRoute? _pendingRouteCache;

	private static long _pendingRouteCacheAt;

	// 2026-10-13：读取失败标志独立成位。旧实现失败时把时间戳存成 -now-1，
	// 使 `now - cacheAt < 5000` 永远不成立 ⇒ 失败情形下缓存永不命中、UI 每次刷新都读固件。
	private static bool _pendingRouteCacheReadFailed;

	/// <summary>
	/// 读取 NVRAM 里「待生效」的显示路由（与 Describe 同一变量/偏移，只是返回结构化结果）。
	/// 需要管理员权限；失败返回 false（route 为 null 表示读到了但解码不出）。
	/// </summary>
	internal static bool TryReadRoute(bool isAmd, out GpuRoute? route)
	{
		route = null;
		try
		{
			if (!ProcessHelper.IsUserAdministrator() || !EnableSystemEnvironmentPrivilege())
			{
				return false;
			}
			byte[] buffer = new byte[4096];
			uint read = GetFirmwareEnvironmentVariableW("UniWillVariable", "{9f33f85c-13ca-4fd1-9c4a-96217722c593}", buffer, (uint)buffer.Length);
			if (read <= 98)
			{
				return false;
			}
			route = GpuRouteInference.DecodeOemDisplayMode(buffer[98], isAmd);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("TryReadRoute failed: " + ex.Message);
			return false;
		}
	}

	/// <summary>带 5 秒缓存的待生效路由（UI 每次刷新都会问，不能每次都调固件）。</summary>
	internal static bool TryGetPendingRoute(bool isAmd, out GpuRoute? route)
	{
		long now = Environment.TickCount64;
		if (_pendingRouteCacheAt != 0 && now - _pendingRouteCacheAt < 5000)
		{
			route = _pendingRouteCache;
			return !_pendingRouteCacheReadFailed;
		}
		bool ok = TryReadRoute(isAmd, out route);
		_pendingRouteCache = route;
		// 时间戳一律存正数（成功/失败都从此刻起算 5s 缓存），成功与否由标志位表达；
		// 成功路径行为与旧实现完全一致（缓存命中即返回 true + 缓存的 route）。
		_pendingRouteCacheAt = now;
		_pendingRouteCacheReadFailed = !ok;
		return ok;
	}

	/// <summary>
	/// 2026-10-13：清空 5 秒缓存，让下一次 UI 刷新真的去读固件。
	/// 用途：刚下发一次显卡模式切换时，缓存里很可能是「下发之前」的旧读数；
	/// 用它判定「待生效已满足」会让按钮高亮与「重启以应用」标签互相打架。
	/// </summary>
	internal static void InvalidatePendingRouteCache()
	{
		_pendingRouteCacheAt = 0L;
		_pendingRouteCache = null;
		_pendingRouteCacheReadFailed = false;
	}

	/// <summary>兼容旧签名（默认按 Intel 解码）。</summary>
	internal static string Describe()
	{
		return Describe(isAmd: false);
	}

	/// <summary>按真实平台解码 NVRAM 显示模式字节。</summary>
	internal static string Describe(bool isAmd)
	{
		try
		{
			if (!ProcessHelper.IsUserAdministrator())
			{
				return "NVRAM display byte: not elevated";
			}
			if (!EnableSystemEnvironmentPrivilege())
			{
				return "NVRAM display byte: privilege unavailable";
			}
			byte[] array = new byte[4096];
			uint firmwareEnvironmentVariableW = GetFirmwareEnvironmentVariableW("UniWillVariable", "{9f33f85c-13ca-4fd1-9c4a-96217722c593}", array, (uint)array.Length);
			switch (firmwareEnvironmentVariableW)
			{
			case 0u:
			{
				int lastWin32Error = Marshal.GetLastWin32Error();
				return (lastWin32Error == 203) ? $"NVRAM display byte: variable absent ({lastWin32Error})" : $"NVRAM display byte: read failed ({lastWin32Error})";
			}
			case 1u:
			case 2u:
			case 3u:
			case 4u:
			case 5u:
			case 6u:
			case 7u:
			case 8u:
			case 9u:
			case 10u:
			case 11u:
			case 12u:
			case 13u:
			case 14u:
			case 15u:
			case 16u:
			case 17u:
			case 18u:
			case 19u:
			case 20u:
			case 21u:
			case 22u:
			case 23u:
			case 24u:
			case 25u:
			case 26u:
			case 27u:
			case 28u:
			case 29u:
			case 30u:
			case 31u:
			case 32u:
			case 33u:
			case 34u:
			case 35u:
			case 36u:
			case 37u:
			case 38u:
			case 39u:
			case 40u:
			case 41u:
			case 42u:
			case 43u:
			case 44u:
			case 45u:
			case 46u:
			case 47u:
			case 48u:
			case 49u:
			case 50u:
			case 51u:
			case 52u:
			case 53u:
			case 54u:
			case 55u:
			case 56u:
			case 57u:
			case 58u:
			case 59u:
			case 60u:
			case 61u:
			case 62u:
			case 63u:
			case 64u:
			case 65u:
			case 66u:
			case 67u:
			case 68u:
			case 69u:
			case 70u:
			case 71u:
			case 72u:
			case 73u:
			case 74u:
			case 75u:
			case 76u:
			case 77u:
			case 78u:
			case 79u:
			case 80u:
			case 81u:
			case 82u:
			case 83u:
			case 84u:
			case 85u:
			case 86u:
			case 87u:
			case 88u:
			case 89u:
			case 90u:
			case 91u:
			case 92u:
			case 93u:
			case 94u:
			case 95u:
			case 96u:
			case 97u:
			case 98u:
				return $"NVRAM display byte: variable too short ({firmwareEnvironmentVariableW} B)";
			default:
			{
				byte value = array[98];
				// 2026-10-05 fix: 平台原先被写死成 Intel，导致 AMD 机器上
				// Direct(1) 被误标成 IgpuOnly、Hybrid(0) 被标成 unrecognized。
				GpuRoute? gpuRoute = GpuRouteInference.DecodeOemDisplayMode(value, isAmd);
				return $"NVRAM display byte @0x{98:X}={value} (len={firmwareEnvironmentVariableW}, {(isAmd ? "amd" : "intel")}-decoded={gpuRoute?.ToString() ?? "unrecognized"}; offset unverified)";
			}
			}
		}
		catch (Exception ex)
		{
			return "NVRAM display byte: " + ex.Message;
		}
	}

	private static bool EnableSystemEnvironmentPrivilege()
	{
		if (!OpenProcessToken(GetCurrentProcess(), 40u, out var token))
		{
			return false;
		}
		try
		{
			if (!LookupPrivilegeValueW(null, "SeSystemEnvironmentPrivilege", out var luid))
			{
				return false;
			}
			TokenPrivileges newState = new TokenPrivileges
			{
				PrivilegeCount = 1u,
				Luid = luid,
				Attributes = 2u
			};
			if (!AdjustTokenPrivileges(token, disableAll: false, ref newState, 0u, IntPtr.Zero, IntPtr.Zero))
			{
				return false;
			}
			return Marshal.GetLastWin32Error() == 0;
		}
		finally
		{
			CloseHandle(token);
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetFirmwareEnvironmentVariableW(string name, string guid, [Out] byte[] buffer, uint size);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool OpenProcessToken(nint process, uint access, out nint token);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool AdjustTokenPrivileges(nint token, bool disableAll, ref TokenPrivileges newState, uint bufferLength, nint previousState, nint returnLength);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint handle);

	[DllImport("kernel32.dll")]
	private static extern nint GetCurrentProcess();
}
