// 2026-10-18（宝龙达 · 充电阈值）：⭐⭐ **限充启用/关闭测试** `--battery-limit-test` ⭐⭐
//
// ⭐ 阶段 2（白名单写）：严格按参考实现在 BM5235/2023 款上的协议事实（⛔ 未复制代码）：
//    启用：身份 OK → 0x8150==0 → AC 在线 → 0<low<high<=100 → SOC<high
//          → 0x8153=high → 0x8154=low → 0x8151=0x0D → 0x8150=0x10 → 轮询回 0
//          → 校验 0x8152==0x55 且 0x83C0==0x48
//    关闭：0x8151=0x0F → 0x8150=0x10 → 轮询回 0 → 校验 0x8152==0x55 且 0x83C0==0x00
// ⛔ 失败不重试、不做清理写；⛔ 只写 4 个白名单地址（0x8153/0x8154/0x8151/0x8150）。
//
// ⭐ 用法：
//    L-Mechrevo.exe --battery-limit-test --low 60 --high 80   启用 80/60
//    L-Mechrevo.exe --battery-limit-test --disable            关闭（恢复默认充电）
//    结果写桌面 `lm-battery-limit-test-*.txt` + `log.txt`（前缀 `[battery-limit-test]`）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ 限充启用/关闭测试（<c>--battery-limit-test</c>）✅。</summary>
internal static class BatteryLimitTest
{
	internal static int Run(string[] args)
	{
		// ⭐⭐ 机主强调：开发机（同方 GM5HG0A）不是宝龙达 ⇒ 入口第一行平台校验，非宝龙达 0 读 0 写 ✅
		if (!BatteryLimitPlatform.IsBaolongdaMachine())
		{
			BatteryLimitPlatform.LogRejected("battery-limit-test");
			return 0;
		}
		int low = 60;
		int high = 80;
		bool disable = false;
		if (args != null)
		{
			for (int i = 0; i < args.Length; i++)
			{
				string arg = args[i] ?? "";
				if (string.Equals(arg, "--disable", StringComparison.OrdinalIgnoreCase))
				{
					disable = true;
				}
				else if (string.Equals(arg, "--low", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
				{
					int.TryParse(args[++i], out low);
				}
				else if (string.Equals(arg, "--high", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
				{
					int.TryParse(args[++i], out high);
				}
			}
		}

		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-battery-limit-test-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-battery-limit-test 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch
		{
			return 1;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[battery-limit-test] " + s); } catch { }
		}

		int exitCode = 1;
		try
		{
			L("=== 宝龙达充电阈值测试（阶段 2 · --battery-limit-test）===");
			L("⛔ 失败不重试、不做清理写；只写 0x8153/0x8154/0x8151/0x8150 四个白名单地址");
			if (disable)
			{
				L("模式：关闭限充（恢复默认充电）");
				bool ok = BatteryLimitWriter.TryDisable(out string detail);
				L(detail);
				L("结论：" + (ok ? "✅ 已关闭（mode=0x00）" : "⛔ 关闭失败/未确认（见上）"));
				exitCode = ok ? 0 : 1;
			}
			else
			{
				L("模式：启用限充（high=" + high + "% · low=" + low + "%）");
				bool stateOk = BatteryLimitWriter.TryReadState(out int curHigh, out int curLow, out int curMode, out string state);
				L("当前状态：" + state + (stateOk ? "" : "（⚠️ 读取不完整）"));
				bool ok = BatteryLimitWriter.TryEnable(low, high, out string detail);
				L(detail);
				BatteryLimitWriter.TryReadState(out int afterHigh, out int afterLow, out int afterMode, out string after);
				L("写后状态：" + after);
				L("结论：" + (ok ? "✅ 限充已启用（mode=0x48）" : "⛔ 启用失败/未确认（见上）"));
				exitCode = ok ? 0 : 1;
			}
			L("");
			L("⚠️ 如需关闭：L-Mechrevo.exe --battery-limit-test --disable");
		}
		catch (Exception ex)
		{
			L("测试异常：" + ex.GetType().Name + " " + ex.Message);
		}
		return exitCode;
	}
}
