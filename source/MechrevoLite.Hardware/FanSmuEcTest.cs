// 2026-10-18（宝龙达 · 风扇接管）：⭐⭐ **风扇 EC 写入验证入口** `--fan-smu-ec-test` ⭐⭐
//
// ⭐⭐ 目的（机主批准）：一次性把写入链路做完，但**默认不开放**。本命令是唯一的“验证/放行”入口：
//    ① 只读对照：ACPI EC（`EcPortIo`）vs LPC（`EcLpcIo`）vs `SmuEcAccess`（7.3 `RyzenAccess` 等价，
//       ⚠️ 实为 LPC 端口 I/O，**没有 SMU 邮箱命令**，见 SmuEcAccess.cs 顶部来源）；
//    ② 最小写验证：先写两路目标 = 当前档位（不改变转速），再 `0xB20` 置 bit1 → 回读确认；
//       finally 恢复：`0xB20` 写回原值 + 目标 0（交回固件）；
//    ③ 只有全部写入都**回读确认**才判定“可用”：写 `%APPDATA%\MechrevoLite\fan_ec_verified.cfg`
//       （`fan_ec_verified_30=1` / `fan_ec_verified_40=1`）⇒ **下次启动**放开 UI 曲线/强冷 ✅
//    ④ 不可用 ⇒ 不写配置、日志说明、UI 保持隐藏（⛔ 不写任何字节）✅
//
// ⛔ 安全：只写白名单 0xC83C/0xC83D/0xB20（仅 bit1/bit3）；写后必回读；finally 恢复；失败即停；
//    ⛔ 不写 0x1060（本命令不碰解锁；如报告需要解锁，先跑 `--fan-ec-test`）；
//    ⛔ 不循环重试；⛔ 不抛出。
//
// ⭐ 输出：桌面 `lm-fan-smu-ec-test-<时间戳>.txt` + `log.txt`（前缀 `[fan-smu-ec-test]`）。
// ⭐ 用法：`L-Mechrevo.exe --fan-smu-ec-test`（需管理员）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ 风扇 EC 写入验证入口（<c>--fan-smu-ec-test</c>）：只读对照 + 最小写 + 闸门配置 ✅。</summary>
internal static class FanSmuEcTest
{
	internal static int Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-fan-smu-ec-test-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-fan-smu-ec-test 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch
		{
			return 1;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[fan-smu-ec-test] " + s); } catch { }
		}

		int exitCode = 1;
		try
		{
			L("=== 风扇 EC 写入验证（--fan-smu-ec-test）===");
			L("⚠️ 只写白名单 0xC83C/0xC83D/0xB20（bit1/bit3）；写后回读；finally 恢复；失败即停");
			L("⚠️ 本命令不写 0x1060；也不写 WMI 20/21（20=1 真机卡死）");
			L("");

			string board = ReadBoardProduct();
			bool is30 = string.Equals(board, "MRID6", StringComparison.OrdinalIgnoreCase);
			L("主板 = " + (board.Length == 0 ? "(空/读不到)" : board) + " ⇒ " + (is30 ? "30 系" : "40 系"));
			L("");

			// ── 1. 只读对照（ACPI vs LPC/SMU-EC）──
			L("── 1. 只读对照（稳定读 3 次取多数）──");
			int[] addresses = { 0x2000, 0xC411, 0xC834, 0xC835, 0x1060, 0xC83C, 0xC83D, 0xB20 };
			string[] names =
			{
				"0x2000 芯片 ID（期望 0x55）", "0xC411 EC 版本", "0xC834 Fan1 转速字节", "0xC835 Fan2 转速字节",
				"0x1060 解锁寄存器", "0xC83C Fan1 目标", "0xC83D Fan2 目标", "0xB20 接管掩码"
			};
			bool acpiIdentity = EcPortIo.TryReadEcIdentityAcpi(out _, out _, out string acpiIdNote);
			bool lpcIdentity = EcLpcIo.TryReadEcIdentity(out _, out _, out string lpcIdNote);
			L("ACPI 身份：" + acpiIdNote + " ⇒ " + (acpiIdentity ? "可用 ✅" : "不可用 ⚠️"));
			L("LPC 身份：" + lpcIdNote + " ⇒ " + (lpcIdentity ? "可用 ✅" : "不可用 ⚠️"));
			L("SMU-EC（SmuEcAccess）：⚠️ 无 SMU 邮箱命令（7.3 的 EC_EX 实为 LPC 端口 I/O）⇒ 与 LPC 同一通道");
			for (int i = 0; i < addresses.Length; i++)
			{
				bool acpiOk = EcPortIo.TryReadByteStable(addresses[i], out int acpiValue, out string acpiNote);
				bool lpcOk = EcLpcIo.TryReadByteStable(addresses[i], out int lpcValue, out string lpcNote);
				L("  " + names[i] + "：");
				L("    ACPI = " + (acpiOk ? "0x" + acpiValue.ToString("X2") : "读不到") + "（" + acpiNote + "）");
				L("    LPC  = " + (lpcOk ? "0x" + lpcValue.ToString("X2") : "读不到") + "（" + lpcNote + "）");
				L("    一致 = " + (acpiOk && lpcOk && acpiValue == lpcValue));
			}
			L("");

			// ── 2. 最小写验证（0xB20）──
			L("── 2. 最小写验证（两路目标 = 当前档位 → 0xB20 置 bit1 → 回读；finally 恢复）──");
			if (!acpiIdentity && !lpcIdentity)
			{
				L("结论：⛔ 不可用 —— ACPI/LPC 身份均不通过，⛔ 未写任何 EC 字节");
				return 1;
			}
			if (!EcPortIo.TryReadByteStable(0xB20, out int originalMask, out string maskNote))
			{
				bool lpcMaskOk = EcLpcIo.TryReadByteStable(0xB20, out originalMask, out string lpcMaskNote);
				maskNote = lpcMaskOk ? lpcMaskNote : maskNote + " / LPC: " + lpcMaskNote;
				if (!lpcMaskOk)
				{
					L("⛔ 读 0xB20 失败（" + maskNote + "）⇒ 不做写验证（no write）");
					L("结论：⛔ 不可用 —— 0xB20 读不到");
					return 1;
				}
			}
			L("0xB20 原值 = 0x" + originalMask.ToString("X2") + "（" + maskNote + "）");
			int targetUnits = 20;
			if (FanWriter.TryReadRpmUnits(out int rpmUnits1, out int rpmUnits2, out string rpmUnitsDetail))
			{
				targetUnits = Math.Clamp(Math.Max(rpmUnits1, rpmUnits2), 0, 68);
				L("当前转速字节：" + rpmUnitsDetail + " ⇒ 目标档 = " + targetUnits + "（" + (targetUnits * 100) + " RPM，尽量不改变转速）");
			}
			else
			{
				L("⚠️ 读转速字节失败（" + rpmUnitsDetail + "）⇒ 用保守目标档 20（2000 RPM）");
			}
			bool targetsOk = false;
			bool maskOk = false;
			bool verified = false;
			try
			{
				targetsOk = FanWriter.TrySetFanTargets(targetUnits, targetUnits, out string targetDetail);
				L("写两路目标 = " + targetUnits + " ⇒ ok=" + targetsOk + " · " + targetDetail);
				if (!targetsOk)
				{
					L("⛔ 目标写入失败 ⇒ ⛔ 不写 0xB20（避免在目标 0 时开手动接管）");
				}
				else
				{
					maskOk = FanWriter.TrySetManualMask(true, false, out string maskDetail);
					L("写 0xB20（bit1=1，保留 bit3）⇒ ok=" + maskOk + " · " + maskDetail);
					bool readMaskOk = EcPortIo.TryReadByteStable(0xB20, out int readMask, out string readNote);
					if (!readMaskOk)
					{
						readMaskOk = EcLpcIo.TryReadByteStable(0xB20, out readMask, out readNote);
					}
					L("回读 0xB20 = " + (readMaskOk ? "0x" + readMask.ToString("X2") : "读不到") + "（" + readNote + "）");
					verified = targetsOk && maskOk && readMaskOk && (readMask & 0x02) != 0;
					L("bit1 回读 = " + (readMaskOk ? (((readMask & 0x02) != 0) ? "1 ✅" : "0 ⚠️") : "读不到"));
				}
			}
			finally
			{
				// ⭐ 恢复：0xB20 写回原值（保留原 bit3）+ 目标 0（交回固件）
				bool maskRestored = FanWriter.TrySetManualMask((originalMask & 0x02) != 0, (originalMask & 0x08) != 0, out string maskRestoreDetail);
				bool targetRestored = FanWriter.TrySetFanTargets(0, 0, out string targetRestoreDetail);
				L("恢复：0xB20 ⇒ " + maskRestored + " · " + maskRestoreDetail);
				L("恢复：目标 0 ⇒ " + targetRestored + " · " + targetRestoreDetail);
			}
			L("");

			// ── 3. 判定 + 闸门配置 ──
			L("── 3. 判定与闸门 ──");
			if (!verified)
			{
				L("结论：⛔ **不可用** —— 写入/回读未确认 ⇒ 不写配置，UI 保持隐藏（风扇曲线/强冷）");
				L("⭐ 请把本报告发回；⛔ 不再尝试其它写花色");
			}
			else
			{
				L("结论：✅ **可用** —— 写入与回读均确认（通道=" + FanWriter.ChannelName(FanWriter.PreferredChannel) + "）");
				// ⭐⭐ 2026-10-18：⭐ 与 `--fan-ec-test` 共用同一个“写标记”函数（避免两套逻辑）✅
				bool saved = FanEcVerifyStore.MarkVerifiedFromTest(is30, out string cfgDetail);
				L(saved
					? "✅ 已写入验证标记（重启 LM 后放开风扇曲线/强冷 UI）· " + cfgDetail
					: "⚠️ 验证标记写入失败（UI 仍保持隐藏）· " + cfgDetail);
				exitCode = 0;
			}
		}
		catch (Exception ex)
		{
			L("测试异常：" + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		return exitCode;
	}

	private static string ReadBoardProduct()
	{
		try
		{
			using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine
				.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS", writable: false);
			return (key?.GetValue("BaseBoardProduct") as string ?? string.Empty).Trim();
		}
		catch
		{
			return string.Empty;
		}
	}
}
