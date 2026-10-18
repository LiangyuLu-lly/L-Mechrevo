// 2026-10-18（宝龙达 · 充电阈值）：⭐⭐ **限充 EC 只读探针** `--battery-limit-probe` ⭐⭐
//
// ⭐⭐ 阶段 1（先只读，零写）。地址语义按参考实现（JiaoLong16Pro-Battery-threshold，
//    `bm5235_native.py` + `native_enable.jsonl` / `native_disable.jsonl`，作者在 **MRID6 (BM5235)** 实测）：
//    · 0x83A1 = 阈值**高**（停充 %；实测日志 0x50 = 80%）
//    · 0x83A2 = 阈值**低**（起充 %；实测日志 0x3C = 60%）
//    · 0x83C0 = 模式（0x00 = 正常；0x48 = 限充生效；允许值 0/2/0x48）
//    · 0x8151 = 命令字（正常 0x0F；0x0D = 启用；0x0E = 准备）
//    · 0x8150 = 触发（写完 0x10 后应回 0x00）
//    · 0x8152 = 结果（0x55 = 上次事务成功）
//    · 0x8153/0x8154 = 写阈值时用的 **mailbox 参数**（⚠️ 不是常驻显示值；常驻看 0x83A1/0x83A2）
//    ⚠️ 以上只取**协议事实**，⛔ 未复制参考代码；本轮**一个字节都不写**。
//
// ⭐ 本命令只读：上述 8 个主地址 + 6 个旁证地址（0x8370/0x8394/0x83A0/0x8410/0x8411/0x8460）
//    + 当前电池状态（AC/电量/是否充电）；⛔ 不写 EC、⛔ 不发 WMI 写。
//    结果写桌面 `lm-battery-probe-*.txt` + `log.txt`（前缀 `[battery-probe]`）。
//
// ⭐ 用法：`L-Mechrevo.exe --battery-limit-probe`（需管理员）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ 宝龙达限充 EC **只读**探针（<c>--battery-limit-probe</c>）✅。</summary>
internal static class BatteryLimitProbe
{
	// ⭐ 主地址（参考实现的协议事实）
	private const int TriggerRegister = 0x8150;   // 触发：写 0x10 提交；完成后应回 0x00
	private const int CommandRegister = 0x8151;   // 命令字：0x0D 启用 / 0x0E 准备 / 0x0F 关闭
	private const int ResultRegister = 0x8152;    // 结果：0x55 = 上次事务成功
	private const int MailboxHighParam = 0x8153;  // 写阈值时的 mailbox 参数（非常驻显示）
	private const int MailboxLowParam = 0x8154;   // 写阈值时的 mailbox 参数（非常驻显示）
	private const int HighRegister = 0x83A1;      // 阈值高（停充 %）—— 常驻显示值
	private const int LowRegister = 0x83A2;       // 阈值低（起充 %）—— 常驻显示值
	private const int ModeRegister = 0x83C0;      // 模式：0x00 正常 / 0x48 限充生效

	// ⭐ 可选旁证地址（参考日志/实现里出现，仅记录原始值）
	private static readonly int[] EvidenceAddresses = { 0x8370, 0x8394, 0x83A0, 0x8410, 0x8411, 0x8460 };

	private const string ReferenceBoard = "MRID6";

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
			L("⛔ 本命令**不写任何 EC 字节**（只读主地址 + 旁证 + 电池状态）");
			L("⭐ 地址语义来源：JiaoLong16Pro-Battery-threshold（MRID6/BM5235 实测日志；只取协议事实，未复制代码）");
			L("");

			string board = ReadBaseBoardProduct();
			// ⭐ 板号代际判据与 `MechrevoHw.IsBaolongda30Series` 同源：裸 MRID6 = 30 系；MRID6-* = 40 系 ✅
			bool is30Series = string.Equals(board, "MRID6", StringComparison.OrdinalIgnoreCase);
			bool is40Series = board.StartsWith("MRID6-", StringComparison.OrdinalIgnoreCase);
			L("── ⓪ 机型对照 ──");
			L("  参考机型 = " + ReferenceBoard + "（蛟龙16 Pro 2023 · 30 系 · BM5235）");
			L("  本机板号 = " + (board.Length == 0 ? "（读不到）" : board));
			L("  本机代际（板号判据） = " + (is30Series ? "30 系" : (is40Series ? "40 系" : "未知 / 非宝龙达"))
				+ "（IsBaolongda30Series=" + is30Series + "）");
			L("  ⚠️ 参考实现只在 **MRID6（30 系）** 实测过；本机若是 `MRID6-23`（40 系）⇒ 地址/语义**可能不同**，结论不能直接沿用 ✅");
			if (is30Series)
			{
				L("  ✅ 板号 = MRID6（与参考机型一致，可沿用参考语义）");
			}
			else if (is40Series)
			{
				L("  ⚠️ 板号 = MRID6-*（40 系不同机型）⇒ 先验证 0x83A1/0x83A2 是否也是 1–100 的合理值");
			}
			else
			{
				L("  ⚠️ 板号不是 MRID6/MRID6-* ⇒ 下列地址/语义仅供对照，⛔ 不据此开放功能");
			}
			L("");

			L("── ① 主地址（⭐ 只读；来源=ACPI-EC / LPC）──");
			int high = ReadEc(HighRegister, "0x83A1 阈值高 / 停充 %（常驻）", L);
			int low = ReadEc(LowRegister, "0x83A2 阈值低 / 起充 %（常驻）", L);
			int mode = ReadEc(ModeRegister, "0x83C0 模式（0x00 正常 / 0x48 限充生效）", L);
			int command = ReadEc(CommandRegister, "0x8151 命令字（0x0D/0x0E/0x0F）", L);
			int trigger = ReadEc(TriggerRegister, "0x8150 触发（应回 0x00）", L);
			int result = ReadEc(ResultRegister, "0x8152 结果（0x55=上次成功）", L);
			int mailboxHigh = ReadEc(MailboxHighParam, "0x8153 mailbox 参数-高（非常驻）", L);
			int mailboxLow = ReadEc(MailboxLowParam, "0x8154 mailbox 参数-低（非常驻）", L);
			L("");

			L("── ①b 旁证地址（⭐ 只读；仅记录，不参与阶段 1 结论）──");
			foreach (int address in EvidenceAddresses)
			{
				ReadEc(address, "0x" + address.ToString("X4") + " 旁证", L);
			}
			L("");

			L("── ② 语义对照（按 MRID6 实测日志）──");
			L("  阈值高 0x83A1 = " + DescribePercent(high) + "（停充 %，1–100；日志 0x50=80%）");
			L("  阈值低 0x83A2 = " + DescribePercent(low) + "（起充 %，1–100；日志 0x3C=60%）");
			L("  模式   0x83C0 = " + DescribeMode(mode) + "（0x00 正常 / 0x48 限充生效；允许 0/2/0x48）");
			L("  命令字 0x8151 = " + DescribeCommand(command) + "（正常 0x0F；启用后 0x0D；0x0E=准备）");
			L("  触发   0x8150 = " + DescribeHex(trigger) + "（空闲应为 0x00；非 0 ⇒ 可能有未完成事务）");
			L("  结果   0x8152 = " + DescribeHex(result) + "（0x55 = 上次事务成功）");
			L("  mailbox 参数 0x8153/0x8154 = " + DescribeHex(mailboxHigh) + " / " + DescribeHex(mailboxLow)
				+ "（⚠️ 写阈值时临时值，非常驻；常驻看 0x83A1/0x83A2）");
			L("");

			L("── ③ 当前电池状态（系统口径，只读）──");
			try
			{
				PowerStatus power = SystemInformation.PowerStatus;
				bool online = power.PowerLineStatus == PowerLineStatus.Online;
				int percent = (int)Math.Round(power.BatteryLifePercent * 100.0);
				string charging = power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging) ? "充电中"
					: (power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.High) ? "已充满/高电量"
						: (power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery) ? "无电池" : "未充电/放电"));
				L("  AC = " + (online ? "已接电源（Online）" : "电池供电（Offline/Unknown）"));
				L("  电量 = " + (percent >= 0 ? percent + "%" : "读不到"));
				L("  充电状态 = " + charging + "（BatteryChargeStatus=" + power.BatteryChargeStatus + "）");
				L("  ⚠️ 系统 API 不直接暴露“充电阈值”；请与 BIOS/官方控制台显示的阈值对照：");
				L("     若官方设 80% 停充 / 60% 起充 ⇒ 期望 0x83A1≈0x50、0x83A2≈0x3C、0x83C0=0x48。");
			}
			catch (Exception ex)
			{
				L("  电池状态读取异常：" + ex.GetType().Name + " " + ex.Message);
			}
			L("");

			L("── ④ 阶段 1 结论（⭐ 按板号/代际分别判定）──");
			bool highLooksPercent = high >= 1 && high <= 100;
			bool lowLooksPercent = low >= 1 && low <= 100;
			bool highGreaterThanLow = high > 0 && low > 0 && high > low;
			bool modeKnown = mode == 0x00 || mode == 0x02 || mode == 0x48;
			bool commandKnown = command == 0x0D || command == 0x0E || command == 0x0F;
			bool triggerIdle = trigger == 0x00;
			bool resultOk = result == 0x55;
			L("  阈值高像 1–100 = " + highLooksPercent + " · 阈值低像 1–100 = " + lowLooksPercent
				+ " · 高>低 = " + highGreaterThanLow);
			L("  模式值已知 = " + modeKnown + " · 命令字已知 = " + commandKnown
				+ " · 触发=0（空闲）= " + triggerIdle + " · 结果=0x55 = " + resultOk + "（仅参考）");
			bool addressLooksRight = highLooksPercent && lowLooksPercent && highGreaterThanLow && modeKnown && commandKnown && triggerIdle;
			if (is30Series)
			{
				L(addressLooksRight
					? "  ⇒ ✅ 30 系（MRID6）：地址语义看起来正确 ⇒ 可进入阶段 2（白名单写测试，需机主批准）"
					: "  ⇒ ⚠️ 30 系（MRID6）：地址语义未能确认（读不到/值不合理）⇒ ⛔ 不进入阶段 2，先把报告发回");
				if (addressLooksRight && !resultOk)
				{
					L("  ⚠️ 结果寄存器不是 0x55：可能本机 EC 尚未执行过限充事务（不代表地址错误），阶段 2 首次写后再复核。");
				}
			}
			else if (is40Series)
			{
				bool plausible = highLooksPercent && lowLooksPercent && highGreaterThanLow;
				L("  40 系判据：只要 0x83A1/0x83A2 读出 1–100 且高>低 ⇒ 说明同一布局存在，可继续；否则判本机不可用。");
				L(plausible
					? "  ⇒ ✅ 40 系：0x83A1/0x83A2 也读出合理百分比（高=0x" + (high < 0 ? "??" : high.ToString("X2"))
						+ " / 低=0x" + (low < 0 ? "??" : low.ToString("X2")) + "）⇒ 同一布局可能存在 ⇒ 可继续（需机主确认后再做阶段 2）"
					: "  ⇒ ⛔ 40 系：**该功能本机不可用**（0x83A1=" + DescribeHex(high) + " · 0x83A2=" + DescribeHex(low)
						+ "，不是可用的 1–100 停充/起充值；官方控制台也没有该功能）⇒ ⛔ 不做阶段 2、⛔ 不显示 UI");
			}
			else
			{
				L("  ⇒ ⚠️ 非 MRID6/MRID6-* 机型：⛔ 不据此进入阶段 2、不开放任何 UI。");
			}
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
			0x0D => "0x0D 启用",
			0x0E => "0x0E 准备",
			0x0F => "0x0F 关闭/正常",
			_ => "未知命令"
		};
		return "0x" + value.ToString("X2") + "（" + name + "）";
	}

	private static string DescribeMode(int value)
	{
		if (value < 0)
		{
			return "读不到";
		}
		return "0x" + value.ToString("X2") + (value == 0x48 ? "（限充生效）" : (value == 0x00 ? "（正常）" : ""));
	}

	private static string ReadBaseBoardProduct()
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
}
