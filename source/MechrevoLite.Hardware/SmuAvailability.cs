// ⭐⭐ 2026-10-19（30 系 · SMU 功耗控制）：**SMU 通路可用性**的唯一探测点（进程内缓存一次）。
//
// 为什么单独一个文件：机主明确要求「『持续功耗 STAPM』这一行只在**当前自定义档实际走 SMU 下发**时显示」，
// 而不是按"机型是 30 系"这种静态判据。真实下发分支（MechrevoService.ApplyPowerLimitViaSmu）最先检查
// `ModeControl.SharedSmu != null`；界面若只按机型判，就会出现"显示了但 SMU 不可用 ⇒ 点了必然失败"的
// 错位（违反 §9.14「能力位 || 本机实现」与"⛔ 不给能点但没用的控件"）。
//
// ⚠️ 纪律：
//   * **只探测一次**（成功/失败都缓存，进程内）—— ⛔ 每次开自定义模式窗口都去读 SMU（那会每次 ~0.4 s）；
//   * 探测本身是**轻量**的：`RyzenSmuService.IsPawnInstalled()`（打开 PawnIO 设备判断"装没装/有没有权限"）
//     + `ModeControl.SharedSmu` 一次初始化（已初始化则直接复用；与 UV/FMax 通路同一个单例）；
//   * ⛔ 不在这里读写 PL/温度墙，也不读 PM 表 —— 只回答"SMU 通路在不在"。
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
