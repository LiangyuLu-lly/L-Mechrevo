// 2026-10-18（宝龙达 · 充电阈值）：⭐⭐ **平台校验（CLI 入口第一行）** ⭐⭐
//
// ⚠️ 机主强调：开发机（GM5HG0A · 同方）**不是宝龙达** ⇒ 所有 `--battery-limit-*` 命令
//    必须在入口**先做平台校验**：非宝龙达 ⇒ 打印“非宝龙达，未做任何操作”并 return，
//    **0 读 0 写**（⛔ 不读 EC、⛔ 不装载 PawnIO、⛔ 不尝试提权）。
//
// ⭐ 判据 = `BoardWhitelist.Matches(板号)`（宝龙达模具白名单：MRID6 / MRID6-23 / MRID6-*）；
//    ⛔ 不用 `UsesBaolongda`（CLI 进程里没有 hw 对象），板号白名单是同一判据的纯函数形式 ✅

using System;
using Microsoft.Win32;

namespace MechrevoLite.Hardware;

/// <summary>⭐ CLI 平台校验（纯注册表读取 · 不碰 EC）✅。</summary>
internal static class BatteryLimitPlatform
{
	/// <summary>⭐ 本机板号（读注册表 BIOS/BaseBoardProduct；失败返回空）✅。</summary>
	internal static string BoardProduct()
	{
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS", writable: false);
			return (key?.GetValue("BaseBoardProduct") as string ?? string.Empty).Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	/// <summary>⭐ 是否宝龙达模具（板号白名单）；⚠️ 非宝龙达 ⇒ 命令必须 0 读 0 写 ✅。</summary>
	internal static bool IsBaolongdaMachine()
	{
		return BoardWhitelist.Matches(BoardProduct());
	}

	/// <summary>⭐ 非宝龙达的统一拒绝日志（0 读 0 写）✅。</summary>
	internal static void LogRejected(string command)
	{
		string board = BoardProduct();
		string text = "[" + command + "] ⛔ 非宝龙达（board=" + (board.Length == 0 ? "(读不到)" : board)
			+ "）⇒ **未做任何操作**（0 读 0 写；不碰 EC/PawnIO/驱动）";
		try { Console.WriteLine(text); } catch { }
		try { Logger.WriteLine(text); } catch { }
	}
}
