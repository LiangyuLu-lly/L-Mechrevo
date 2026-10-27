// 2026-10-18（宝龙达 · 充电阈值）：⭐⭐ **只读状态命令** `--battery-limit-status` ⭐⭐
//
// ⭐ 打印：EC 常驻值（0x83C0 / 0x83A1 / 0x83A2）+ 本地配置（battery_limit_enabled/start/stop）
//    + 是否一致；⛔ 0 写（只读，不触发恢复）。
// ⭐ 用法：L-Mechrevo.exe --battery-limit-status（需管理员；非宝龙达直接拒绝、0 读 0 写）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ 充电阈值只读状态（<c>--battery-limit-status</c>）✅。</summary>
internal static class BatteryLimitStatus
{
	internal static int Run()
	{
		if (!BatteryLimitPlatform.IsBaolongdaMachine())
		{
			BatteryLimitPlatform.LogRejected("battery-limit-status");
			return 0;
		}
		bool hasConfig = BatteryLimitRestore.TryLoad(out bool enabled, out int low, out int high);
		Console.WriteLine("[battery-limit-status] 平台=宝龙达（只读）");
		if (!BatteryLimitWriter.TryReadState(out int ecHigh, out int ecLow, out int mode, out string stateDetail))
		{
			Console.WriteLine("  EC 状态 = 读不到（" + stateDetail + "）");
			return 0;
		}
		Console.WriteLine("  EC：" + stateDetail);
		// ⭐ 机主要求：无论配置是否存在，都多打印一行（便于机主验证“允许初次放电”状态）✅
		Console.WriteLine("  允许初次放电=" + BatteryLimitRestore.AllowInitialDischarge);
		if (!hasConfig)
		{
			Console.WriteLine("  本地配置 = 不存在（enabled/start/stop 未设置）⇒ 不参与恢复");
			Console.WriteLine("  一致 = 无法判断");
			return 0;
		}
		bool ecActive = mode == BatteryLimitWriter.ModeActive;
		bool consistent = enabled
			? (ecActive && ecHigh == high && ecLow == low)
			: (!ecActive);
		Console.WriteLine("  本地配置：enabled=" + enabled + " high=" + high + " low=" + low);
		Console.WriteLine("  一致 = " + consistent + (consistent ? "（⛔ 不需要恢复）" : "（⚠️ 启动时会被恢复）"));
		try
		{
			Logger.WriteLine("[battery-limit-status] EC=" + stateDetail + " · 配置 enabled=" + enabled
				+ " high=" + high + " low=" + low + " · 一致=" + consistent);
		}
		catch
		{
		}
		return 0;
	}
}
