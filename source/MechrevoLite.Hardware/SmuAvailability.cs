// （30 系 · SMU 功耗控制）：**SMU 通路可用性**的唯一探测点（进程内缓存一次）。
using System;
using System.Threading;

namespace MechrevoLite.Hardware;

internal static class SmuAvailability
{
	private static readonly object Gate = new object();

	// -1 = 还没探测；0 = 不可用；1 = 可用（**成功/失败都缓存**，避免每次开窗都探测）
	private static int _readyCache = -1;

	private static string _reason = "尚未探测";

	/// <summary>SMU 通路是否可用（首次访问时探测并缓存；⛔ 不会每次都读 SMU）。</summary>
	internal static bool IsReady
	{
		get
		{
			return EnsureProbed(force: false);
		}
	}
	// **只读缓存值**（未探测过 = false， **不触发探测**）—— 给"开设置窗/构建行/算能力位"这类
	internal static bool IsReadyCached
	{
		get
		{
			return Volatile.Read(ref _readyCache) == 1;
		}
	}

	/// <summary>不可用时的原因（给日志/报告用；可用时是"SMU 可用（代号/家族/版本）"）。</summary>
	internal static string Reason
	{
		get
		{
			EnsureProbed(force: false);
			return _reason;
		}
	}

	/// <summary>
	/// 探测一次（默认走缓存）。<paramref name="force" /> = true 时强制重探（排障用；正常 UI/下发路径**不要**用）。
	/// </summary>
	internal static bool EnsureProbed(bool force = false)
	{
		if (!force)
		{
			int cached = Volatile.Read(ref _readyCache);
			if (cached >= 0)
			{
				return cached == 1;
			}
		}
		lock (Gate)
		{
			if (!force)
			{
				int cached2 = Volatile.Read(ref _readyCache);
				if (cached2 >= 0)
				{
					return cached2 == 1;
				}
			}
			bool ready = false;
			string reason;
			try
			{
				if (!PawnIO.RyzenSmuService.IsPawnInstalled())
				{
					reason = "PawnIO 未安装（设备不存在）";
				}
				else
				{
					// SharedSmu = 进程内共享单例（未初始化时初始化一次；失败返回 null）。
					// ⚠️ 它需要管理员权限（主程序清单本身是 requireAdministrator）。
					PawnIO.RyzenSmuService smu = MechrevoLite.Mode.ModeControl.SharedSmu;
					if (smu == null)
					{
						reason = "RyzenSMU 初始化失败（需要管理员权限 / 非 AMD 平台 / 代号或 SMU 版本读不到）";
					}
					else
					{
						ready = true;
						reason = $"SMU 可用（代号 {smu.CpuCodeName} · 家族 {smu.Family} · SMU 0x{smu.SmuVersion:X}）";
					}
				}
			}
			catch (Exception ex)
			{
				reason = "SMU 探测异常: " + ex.GetType().Name + " " + ex.Message;
			}
			_reason = reason;
			Volatile.Write(ref _readyCache, ready ? 1 : 0);
			try
			{
				Logger.WriteLine("[smu] 通路探测（进程内只一次，已缓存）: " + reason);
			}
			catch
			{
			}
			return ready;
		}
	}
}
