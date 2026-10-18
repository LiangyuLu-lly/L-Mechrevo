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

	// ⭐ 参考机型：2023 款（40 系 · 模具 MRID6/BM5235 · 23 款 = MRID6-23）
	private const string ReferenceModel = "蛟龙16 Pro 2023（宝龙达 · ITE IT5570 · 40 系 · 模具 MRID6/BM5235 · 23 款 = MRID6-23）";

	// ⭐ “与参考机型同款”的板号前缀（裸 MRID6 与 MRID6-23 都算同款：作者只是没写后缀）
	private const string ReferenceBoardPrefix = "MRID6";

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
			// ⭐⭐ 2026-10-18（机主澄清）：参考机型就是 2023 款（40 系 · 模具 MRID6/BM5235 · 23 款 = MRID6-23）；
			//   作者写的 `MRID6` 只是没写后缀。⭐ 是否支持仍只看「EC 是否含该布局」，⛔ 不看 30/40 标签 ✅
			bool sameModel = board.StartsWith(ReferenceBoardPrefix, StringComparison.OrdinalIgnoreCase);
			L("── ⓪ 参考机型对照 ──");
			L("  参考机型 = " + ReferenceModel);
			L("  本机板号 = " + (board.Length == 0 ? "（读不到）" : board));
			L("  本机与参考机型是否同款（板号以 " + ReferenceBoardPrefix + " 开头） = " + sameModel);
			L("  ⚠️ 参考作者写的 `MRID6/BM5235` = 2023 款（40 系 · 23 款 = `MRID6-23`）；裸 `MRID6` 只是同款未写后缀 ✅");
			L("  ⭐ 是否支持**只看 EC 是否含该布局**（0x83A1/0x83A2 是否为合理阈值），⛔ 不按 30/40 系标签判断 ✅");
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

			L("── ①c 参考 EC 版本对照（参考日志 [0,33,0,0]；十进制 33 = 0x21）──");
			int version0 = ReadEc(0x8410, "0x8410 EC 版本字节 0", L);
			int version1 = ReadEc(0x8411, "0x8411 EC 版本字节 1", L);
			int version2 = ReadEc(0x8412, "0x8412 EC 版本字节 2", L);
			int version3 = ReadEc(0x8413, "0x8413 EC 版本字节 3", L);
			bool versionNear = version0 == 0x00 && version1 == 0x21;
			L("  参考 EC 版本 = [0,33,0,0] · 本机前两字节匹配 = " + versionNear
				+ "（本机 = " + DescribeHex(version0) + "," + DescribeHex(version1) + ","
				+ DescribeHex(version2) + "," + DescribeHex(version3) + "）");
			L("  ⚠️ 版本仅作旁证：⛔ 不用它判断是否支持（支持与否只看 0x83A1/0x83A2 布局）✅");
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

			L("── ④ 阶段 1 结论（⭐ 按「EC 是否含该布局」判断；⛔ 不按 30/40 系）──");
			bool highLooksPercent = high >= 1 && high <= 100;
			bool lowLooksPercent = low >= 1 && low <= 100;
			bool highGreaterThanLow = high > 0 && low > 0 && high > low;
			bool modeKnown = mode == 0x00 || mode == 0x02 || mode == 0x48;
			bool commandKnown = command == 0x0D || command == 0x0E || command == 0x0F;
			bool triggerIdle = trigger == 0x00;
			bool resultOk = result == 0x55;
			bool layoutPlausible = highLooksPercent && lowLooksPercent && highGreaterThanLow;
			L("  阈值高像 1–100 = " + highLooksPercent + " · 阈值低像 1–100 = " + lowLooksPercent
				+ " · 高>低 = " + highGreaterThanLow + " ⇒ 布局合理 = " + layoutPlausible);
			L("  模式值已知 = " + modeKnown + " · 命令字已知 = " + commandKnown
				+ " · 触发=0（空闲）= " + triggerIdle + " · 结果=0x55 = " + resultOk + "（仅参考）");
			L(layoutPlausible
				? "  ⇒ ✅ 本机 EC **含该布局**（0x83A1=" + DescribeHex(high) + " · 0x83A2=" + DescribeHex(low)
					+ "）⇒ 可进入阶段 2（白名单写测试，需机主批准）"
				: "  ⇒ ⛔ 本机 EC **不含该布局**（0x83A1=" + DescribeHex(high) + " · 0x83A2=" + DescribeHex(low)
					+ "，不是合理的 1–100 停充/起充值）⇒ ⛔ 不做阶段 2、⛔ 不显示 UI");
			if (layoutPlausible && !resultOk)
			{
				L("  ⚠️ 结果寄存器不是 0x55：可能本机尚未执行过限充事务（不代表布局错误），阶段 2 首次写后再复核。");
			}
			L("  旁证（仅参考，⛔ 不作支持判据）：与参考机型同款=" + sameModel + " · EC 版本接近=" + versionNear);
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
