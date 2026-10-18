// 2026-10-18（宝龙达 · 充电阈值）：⭐⭐ **限充 EC 只读探针** `--battery-limit-probe` ⭐⭐
//
// ⭐⭐ 阶段 1（先只读，零写）：核对参考实现（JiaoLong16Pro-Battery-threshold）给出的 ACPI EC 地址语义：
//    · 0x8153 = 阈值**高**（停充 %，1–100）
//    · 0x8154 = 阈值**低**（起充 %，1–100）
//    · 0x8151 = 命令字（0x0D 读 / 0x0E 准备·启用 / 0x0F 关闭 / 0x10 触发）
//    · 0x8150 = **触发**（写 0x10 提交；完成后应回 0）
//    · 0x8152 = **结果**（0x55 = 成功）
//    · 0x83C0 = 当前模式（参考实现里合理值 0 / 2 / 0x48）
//    ⚠️ 以上地址/值均来自参考实现的**协议事实**，⛔ 未复制其代码；本轮**一个字节都不写**。
//
// ⭐ 本命令只读：上述 6 个 EC 地址（ACPI EC 0x92 优先、LPC 兜底）＋ 当前电池状态（AC/电量/是否在充电）。
//    ⛔ 不写 EC、⛔ 不发 WMI 写；结果写桌面 `lm-battery-probe-*.txt` + `log.txt`（前缀 `[battery-probe]`）。
//
// ⭐ 用法：`L-Mechrevo.exe --battery-limit-probe`（需管理员；与风扇探针同一套 PawnIO 通道）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ 宝龙达限充 EC **只读**探针（<c>--battery-limit-probe</c>）✅。</summary>
internal static class BatteryLimitProbe
{
	private const int TriggerRegister = 0x8150;   // 触发（写 0x10 提交；完成后应回 0）
	private const int CommandRegister = 0x8151;   // 命令字 0x0D/0x0E/0x0F/0x10
	private const int ResultRegister = 0x8152;    // 结果：0x55 = 成功
	private const int HighRegister = 0x8153;      // 阈值高（停充 %）
	private const int LowRegister = 0x8154;       // 阈值低（起充 %）
	private const int ModeRegister = 0x83C0;      // 当前模式（0 / 2 / 0x48）

	internal static int Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-battery-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-battery-limit-probe 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch
		{
			return 1;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[battery-probe] " + s); } catch { }
		}

		int exitCode = 1;
		try
		{
			L("=== 宝龙达充电阈值 EC 只读探针（阶段 1 · --battery-limit-probe）===");
			L("⛔ 本命令**不写任何 EC 字节**（只读 6 个地址 + 电池状态）");
			L("⭐ 协议事实来源：JiaoLong16Pro-Battery-threshold（只取地址/命令字事实，未复制代码）");
			L("");

			L("── ① EC 原始值（⭐ 只读；来源=ACPI-EC / LPC）──");
			int high = ReadEc(HighRegister, "0x8153 阈值高（停充 %）", L);
			int low = ReadEc(LowRegister, "0x8154 阈值低（起充 %）", L);
			int command = ReadEc(CommandRegister, "0x8151 命令字", L);
			int trigger = ReadEc(TriggerRegister, "0x8150 触发（应回 0）", L);
			int result = ReadEc(ResultRegister, "0x8152 结果（0x55=成功）", L);
			int mode = ReadEc(ModeRegister, "0x83C0 当前模式（0/2/0x48）", L);
			L("");

			L("── ② 语义对照（按参考实现的协议事实）──");
			L("  阈值高 0x8153 = " + DescribePercent(high) + "（参考：停充 %，1–100；系统若设 80% ⇒ 期望 0x50=80）");
			L("  阈值低 0x8154 = " + DescribePercent(low) + "（参考：起充 %，1–100；通常 低于高值，例如 40）");
			L("  命令字 0x8151 = " + DescribeCommand(command) + "（只读展示，⛔ 诊断不写）");
			L("  触发   0x8150 = " + DescribeHex(trigger) + "（空闲时应为 0x00；非 0 ⇒ 可能有未完成事务）");
			L("  结果   0x8152 = " + DescribeHex(result) + "（0x55=成功；其它值需对照参考实现）");
			L("  模式   0x83C0 = " + DescribeMode(mode) + "（参考实现合理值 0 / 2 / 0x48）");
			L("");

			L("── ③ 当前电池状态（系统口径，只读）──");
			try
			{
				PowerStatus power = SystemInformation.PowerStatus;
				bool online = power.PowerLineStatus == PowerLineStatus.Online;
				int percent = (int)Math.Round(power.BatteryLifePercent * 100.0);
				string charging = power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging) ? "充电中"
					: (power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery) ? "无电池" : "未充电/放电");
				L("  AC = " + (online ? "已接电源（Online）" : "电池供电（Offline/Unknown）"));
				L("  电量 = " + (percent >= 0 ? percent + "%" : "读不到"));
				L("  充电状态 = " + charging + "（BatteryChargeStatus=" + power.BatteryChargeStatus + "）");
				L("  ⚠️ 系统 API 不直接暴露“充电阈值”；请把本报告与 BIOS/官方控制台显示的阈值对照：");
				L("     若官方已设 80% 停充/40% 起充 ⇒ 期望 0x8153≈0x50、0x8154≈0x28，且当前电量会停在 ≤80%。");
			}
			catch (Exception ex)
			{
				L("  电池状态读取异常：" + ex.GetType().Name + " " + ex.Message);
			}
			L("");

			L("── ④ 结论（阶段 1 只判断“地址是否像限充语义”）──");
			bool highLooksPercent = high >= 1 && high <= 100;
			bool lowLooksPercent = low >= 1 && low <= 100;
			bool triggerIdle = trigger == 0x00;
			bool resultOk = result == 0x55;
			bool modeKnown = mode == 0x00 || mode == 0x02 || mode == 0x48;
			L("  阈值高像 1–100 的百分比 = " + highLooksPercent + " · 阈值低像 1–100 的百分比 = " + lowLooksPercent);
			L("  触发=0（空闲）= " + triggerIdle + " · 结果=0x55 = " + resultOk + " · 模式值已知 = " + modeKnown);
			L(highLooksPercent && lowLooksPercent && triggerIdle
				? "  ⇒ ✅ 地址语义**看起来正确**（阈值在 1–100、触发空闲）⇒ 可进入阶段 2（白名单写测试，需机主批准）"
				: "  ⇒ ⚠️ 地址语义**未能确认**（见上；读不到/值不合理）⇒ ⛔ 不进入阶段 2，先把报告发回");
			L("  ⛔ 本命令未写任何 EC 字节、未发 WMI 写");
			exitCode = 0;
		}
		catch (Exception ex)
		{
			L("探针异常：" + ex.GetType().Name + " " + ex.Message);
		}
		return exitCode;
	}

	private static int ReadEc(int address, string label, Action<string> log)
	{
		try
		{
			if (EcPortIo.TryReadByte(address, out int value))
			{
				log("  " + label + " = 0x" + value.ToString("X2") + " · 来源=ACPI-EC");
				return value;
			}
			string acpiReason = EcPortIo.LastReadFailure;
			if (EcLpcIo.TryReadByte(address, out int lpcValue))
			{
				log("  " + label + " = 0x" + lpcValue.ToString("X2") + " · 来源=LPC");
				return lpcValue;
			}
			log("  " + label + " = 读不到（ACPI-EC: " + acpiReason + " · LPC: " + EcLpcIo.LastReadFailure + "）");
		}
		catch (Exception ex)
		{
			log("  " + label + " = 读不到（异常：" + ex.GetType().Name + "）");
		}
		return -1;
	}

	private static string DescribePercent(int value)
	{
		return value < 0 ? "读不到" : "0x" + value.ToString("X2") + "（" + value + "%）";
	}

	private static string DescribeHex(int value)
	{
		return value < 0 ? "读不到" : "0x" + value.ToString("X2");
	}

	private static string DescribeCommand(int value)
	{
		if (value < 0)
		{
			return "读不到";
		}
		string name = value switch
		{
			0x0D => "0x0D 读",
			0x0E => "0x0E 准备/启用",
			0x0F => "0x0F 关闭",
			0x10 => "0x10 触发",
			_ => "未知命令"
		};
		return "0x" + value.ToString("X2") + "（" + name + "）";
	}

	private static string DescribeMode(int value)
	{
		return value < 0 ? "读不到" : "0x" + value.ToString("X2");
	}
}
