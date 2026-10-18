// 2026-10-18（宝龙达 · 风扇接管）：⭐⭐ **EC 写通路最小验证** `--fan-ec-test` ⭐⭐
//
// ⭐⭐ 背景（机主 0.291.9.14 真机）：WMI 功能号 **20=1 会卡死**（连 try/finally 都跑不完）
//    ⇒ 机主批准改走 **EC 直写**（与开源蛟龙 `Blding64` 同语义，⛔ 未复制代码）✅
//    ⭐ 2026-10-18 机主实测：EC 读原语已修正（0x2000=0x55 · 0xC411=0x23 · 0xC834/0xC835=0x14 ≈ 2000 RPM，
//      与 WMI 13 吻合）⇒ ⭐ 现在验证**写通路**；先只验 Fan1（CPU）一个通道。
//
// ⭐ 步骤（⭐ 白名单写：只碰 0xC83C/0xC83D/0xB20 + 0x1060 的 bit7 解锁）：
//    ① 读身份：0x2000 == 0x55 且 0xC411 非 0/0xFF（⛔ 不通过 ⇒ 0 写、结论不可用）
//    ② 读 0x1060 解锁寄存器；bit7=0 ⇒ 写 `当前值 | 0x80`（只置 bit7、保留其它位）→ 回读确认 bit7=1
//    ③ 只读基线：EC 0xC834/0xC835（≈ RPM/100）+ WMI 13 转速
//    ④ 写 Fan1 目标 = 当前档 + 5（≤68）→ 0xB20 |= 0x02 → 等 2 s
//    ⑤ 读 0xC834/0xC835 + WMI 13 ⇒ 转速涨 ≥ +200 RPM 判**可用**
//    ⑥ **finally 恢复**：目标 0、清 0x02|0x08，再把 0x1060 写回**测试前读到的原值**
//
// ⛔ 安全边界（硬性）：
//    · ⛔ 只写白名单地址（3 个风扇地址 + 0x1060 解锁位）；地址/值域在 `EcLpcIo` 内**二次校验**；
//    · ⛔ 不写 WMI 20/21（⭐ 20=1 已知卡死）；WMI 只发 Get；
//    · ⛔ 任何异常不抛出；⭐ 全程 try/finally，**一定执行恢复**；
//    · ⛔ 不打包、不碰安装目录、不 kill LM。
//
// ⭐ 输出：桌面 `lm-fan-ec-test-<时间戳>.txt` + `%APPDATA%\MechrevoLite\log.txt`（前缀 `[fan-ec-test]`）。
// ⭐ 用法：`L-Mechrevo.exe --fan-ec-test`（⭐ 需管理员：PawnIO 驱动访问）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ 宝龙达风扇 **EC 写通路最小验证**（<c>--fan-ec-test</c>）：Fan1 单通道 · 白名单写 · finally 恢复 ✅。
/// </summary>
internal static class FanEcTest
{
	private const byte UnlockBit = 0x80;

	internal static int Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-fan-ec-test-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-fan-ec-test 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch
		{
			return 1;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[fan-ec-test] " + s); } catch { }
		}

		BaolongdaWmi wmi = null;
		bool wroteEc = false;
		bool unlockAttempted = false;
		int unlockOriginal = -1;
		int exitCode = 1;
		try
		{
			L("=== 宝龙达风扇 EC 写通路最小验证（--fan-ec-test · Fan1 单通道）===");
			L("⚠️ 安全：只写 0xC83C/0xC83D/0xB20（风扇）+ 0x1060 的 bit7（解锁）；try/finally 一定恢复");
			L("⚠️ 不写 WMI 20/21（20=1 真机卡死）；WMI 只发 Get");
			L("");

			string board = ReadBoardProduct();
			bool is30 = string.Equals(board, "MRID6", StringComparison.OrdinalIgnoreCase);
			L("主板 = " + (board.Length == 0 ? "(空/读不到)" : board) + " ⇒ " + (is30 ? "30 系" : "40 系"));
			L("");

			// ── ① 身份（闸门只认身份：0x2000 == 0x55 且 0xC411 非 0/0xFF）──
			L("── ① EC 身份 ──");
			bool identityOk = EcLpcIo.TryReadEcIdentity(out _, out _, out string idNote);
			L("EC 身份：" + idNote + " ⇒ " + (identityOk ? "可访问 ✅" : "⛔ 不可访问（判据：0x2000==0x55 且 0xC411 非 0/0xFF）"));
			if (!identityOk)
			{
				L("");
				L("结论：⛔ 不可用 —— EC 身份不通过");
				L("⛔ 本命令**未写任何 EC 字节**（身份不通过 = 禁止任何写）");
				return 1;
			}
			L("");

			// ── ② 解锁（0x1060 |= 0x80；只置 bit7，保留其它位）──
			L("── ② 解锁寄存器 0x1060 ──");
			if (!EcLpcIo.TryReadByte(EcLpcIo.EcUnlockAddress, out unlockOriginal))
			{
				L("读 0x1060 失败 ⇒ ⛔ 不可用（无法安全解锁/恢复），不写任何 EC 字节");
				return 1;
			}
			L("解锁前 0x1060 = 0x" + unlockOriginal.ToString("X2") + "（bit7=" + (((unlockOriginal & UnlockBit) != 0) ? "1" : "0") + "）");
			int unlockAfter = unlockOriginal;
			if ((unlockOriginal & UnlockBit) == 0)
			{
				unlockAttempted = true;
				bool unlockOk = EcLpcIo.TryUnlockEcRam(out _, out int ubAfter, out string unlockNote);
				unlockAfter = ubAfter;
				L("解锁：" + unlockNote);
				L("解锁后 0x1060 = 0x" + (unlockAfter < 0 ? "读不到" : unlockAfter.ToString("X2"))
					+ "（bit7=" + (((unlockAfter & UnlockBit) != 0) ? "1" : "0") + "）");
				if (!unlockOk)
				{
					L("");
					L("结论：⛔ 不可用 —— 解锁失败，⛔ 不继续写风扇（finally 会尝试恢复 0x1060）");
					return 1;
				}
			}
			else
			{
				L("已解锁（bit7=1）⇒ 无需写 0x1060 ✅");
			}
			L("");

			// ── ③ 只读基线：EC 0xC834/0xC835（≈RPM/100）+ WMI 13 ──
			L("── ③ 只读基线 ──");
			bool ecRpmOkBefore = ReadEcRpmBytes(L, "基线", out int ecRpmBefore);
			L("EC 0xC83C（Fan1 目标）= " + ReadHex(EcLpcIo.Fan1TargetAddress)
				+ " · 0xC83D（Fan2 目标）= " + ReadHex(EcLpcIo.Fan2TargetAddress)
				+ " · 0xB20（接管掩码）= " + ReadHex(EcLpcIo.FanManualMaskAddress));
			try
			{
				wmi = BaolongdaWmi.TryCreate();
			}
			catch
			{
				wmi = null;
			}
			int wmiRpmBefore = -1;
			BaolongdaWmi.FanSpeeds speedsBefore = default;
			if (wmi != null)
			{
				if (wmi.TryReadFanSpeeds(out speedsBefore))
				{
					wmiRpmBefore = Math.Max(speedsBefore.First, speedsBefore.Second);
				}
				L("WMI 13 基线转速 = " + (wmiRpmBefore >= 0 ? wmiRpmBefore + " RPM（" + speedsBefore.First + "/" + speedsBefore.Second + "）" : "读不到"));
				L("WMI 20/21 只读 hex：" + RawHex(wmi, BaolongdaWmi.Function.MaxFanSpeedSwitch, 0)
					+ " / " + RawHex(wmi, BaolongdaWmi.Function.MaxFanSpeed, 0));
			}
			else
			{
				L("WMI：⚠️ 没有 MICommonInterface 实例 ⇒ 用 EC 0xC834/0xC835 估算转速");
			}
			int baselineRpm = (wmiRpmBefore >= 0) ? wmiRpmBefore : (ecRpmOkBefore ? ecRpmBefore : -1);
			L("基线转速（用于判定）= " + (baselineRpm >= 0 ? baselineRpm + " RPM（来源=" + (wmiRpmBefore >= 0 ? "WMI13" : "EC 0xC834/5×100") + "）" : "读不到"));
			if (baselineRpm < 0)
			{
				L("");
				L("结论：⛔ 不可用 —— 无法读基线转速（WMI 13 与 EC 0xC834/0xC835 都读不到），不写");
				return 1;
			}
			L("");

			// ── ④ 写 Fan1 目标 + 置 bit1 ──
			int currentUnits = Math.Clamp(baselineRpm / 100, 0, EcLpcIo.FanTargetMaxUnits100Rpm);
			int targetUnits = Math.Min(currentUnits + 5, EcLpcIo.FanTargetMaxUnits100Rpm);
			L("── ④ 写测试（Fan1 单通道）──");
			L("目标：0xC83C ← " + targetUnits + "（" + (targetUnits * 100) + " RPM）· 0xB20 |= 0x02（Fan1 手动接管）");
			wroteEc = true; // ⭐ 先置恢复标志：即使下面异常，finally 也会恢复 ✅
			bool w1 = EcLpcIo.TryWriteFanTarget(EcLpcIo.Fan1TargetAddress, targetUnits, out int rb1);
			L("写 0xC83C=" + targetUnits + " ⇒ ok=" + w1 + " · 回读=" + (rb1 < 0 ? "读不到" : rb1.ToString()));
			bool w2 = EcLpcIo.TryWriteFanManualMask(fan1Manual: true, fan2Manual: false, out int rbMask);
			L("写 0xB20（bit1=1，保留其它位）⇒ ok=" + w2 + " · 回读=0x"
				+ (rbMask < 0 ? "读不到" : rbMask.ToString("X2"))
				+ "（bit1=" + (((rbMask & 0x02) != 0) ? "1" : "0") + " · bit3=" + (((rbMask & 0x08) != 0) ? "1" : "0") + "）");
			L("");

			// ── ⑤ 等起转 → 读 EC + WMI 13 ──
			L("── ⑤ 等待 2 s 后读转速 ──");
			Thread.Sleep(2000);
			bool ecRpmOkAfter = ReadEcRpmBytes(L, "写后", out int ecRpmAfter);
			int wmiRpmAfter = -1;
			BaolongdaWmi.FanSpeeds speedsAfter = default;
			if (wmi != null && wmi.TryReadFanSpeeds(out speedsAfter))
			{
				wmiRpmAfter = Math.Max(speedsAfter.First, speedsAfter.Second);
				L("WMI 13 写后转速 = " + wmiRpmAfter + " RPM（" + speedsAfter.First + "/" + speedsAfter.Second + "）");
			}
			else
			{
				L("WMI 13 写后转速 = 读不到");
			}
			int afterRpm = (wmiRpmAfter >= 0) ? wmiRpmAfter : (ecRpmOkAfter ? ecRpmAfter : -1);
			L("写后转速（用于判定）= " + (afterRpm >= 0 ? afterRpm + " RPM（来源=" + (wmiRpmAfter >= 0 ? "WMI13" : "EC 0xC834/5×100") + "）" : "读不到"));
			ReadEcRegisters(L, "写后寄存器", out _, out _, out _);
			L("");

			// ── ⑥ 判定 ──
			int delta = (afterRpm >= 0) ? (afterRpm - baselineRpm) : int.MinValue;
			bool available = w1 && w2 && afterRpm >= 0 && delta >= 200;
			L("── ⑥ 结论 ──");
			L("转速变化 = " + (delta == int.MinValue ? "无法读取" : delta + " RPM") + "（判据：≥ +200 RPM）");
			if (available)
			{
				L("结论：✅ **EC 写通路可用**（Fan1 目标写入 + 0xB20 bit1 置位 ⇒ 转速上升 " + delta + " RPM）");
				L("⭐ 下一步：机主回传本报告 ⇒ 再验 Fan2（0xC83D + bit3），然后接产品闭环 ✅");
				exitCode = 0;
			}
			else
			{
				L("结论：⛔ **EC 写通路不可用/未确认** —— 写入未使转速明显上升（<+200 RPM）");
				L("⚠️ 可能原因：0x1060 解锁未真正生效 / 0xB20 写被拒 / 目标地址语义不同 / 风扇已在高转速");
				L("⭐ 请把本报告发回（含解锁前后 0x1060 与写入回读）");
			}
		}
		catch (Exception ex)
		{
			L("测试异常：" + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		finally
		{
			// ⭐⭐ 硬性要求：无论成功/失败/异常，都**恢复**（目标 0 + 清 0x02|0x08）；解锁位写回原值 ✅
			if (wroteEc)
			{
				L("");
				L("── ⑦ finally 恢复（目标 0 · 清 0x02|0x08 · 0x1060 写回原值）──");
				try
				{
					bool restored = EcLpcIo.TryRestoreFanFirmware(out string restoreDetail);
					L("风扇恢复 = " + restored + " · " + restoreDetail);
					if (!restored)
					{
						L("⚠️ 风扇恢复未完全成功 —— 请立即把本报告发回；必要时重启机器 ⚠️");
					}
				}
				catch (Exception ex)
				{
					L("⚠️ finally 风扇恢复异常（请把报告发回）：" + ex.GetType().Name + " " + ex.Message);
				}
			}
			if (unlockAttempted && unlockOriginal >= 0)
			{
				try
				{
					bool unlockRestored = EcLpcIo.TryRestoreEcUnlockRegister(unlockOriginal, out int unlockReadBack);
					L("解锁恢复：0x1060 写回 0x" + unlockOriginal.ToString("X2")
						+ " ⇒ 回读=" + (unlockReadBack < 0 ? "读不到" : "0x" + unlockReadBack.ToString("X2"))
						+ " ⇒ ok=" + unlockRestored
						+ (unlockRestored ? " ✅" : " ⚠️（请把报告发回）"));
				}
				catch (Exception ex)
				{
					L("⚠️ finally 解锁恢复异常（请把报告发回）：" + ex.GetType().Name + " " + ex.Message);
				}
			}
			if (wroteEc || unlockAttempted)
			{
				Thread.Sleep(300);
				L("恢复后 0x1060 = " + ReadHex(EcLpcIo.EcUnlockAddress));
				ReadEcRegisters(L, "恢复后寄存器", out _, out _, out _);
			}
			if (wmi != null)
			{
				try
				{
					wmi.Dispose();
				}
				catch
				{
				}
			}
			L("");
			L("⚠️ 验证通过前，产品**不开放**风扇曲线/强冷（UI 已隐藏）；⭐ 请把本报告发回 ✅");
		}
		return exitCode;
	}

	/// <summary>⭐ 读 EC 0xC834/0xC835（上游口径 ≈ RPM/100）并换算成 RPM ✅。</summary>
	private static bool ReadEcRpmBytes(Action<string> log, string tag, out int rpm)
	{
		rpm = -1;
		bool r1 = EcLpcIo.TryReadByte(0xC834, out int fan1Byte);
		bool r2 = EcLpcIo.TryReadByte(0xC835, out int fan2Byte);
		log(tag + "：0xC834=" + (r1 ? "0x" + fan1Byte.ToString("X2") : "读不到")
			+ " · 0xC835=" + (r2 ? "0x" + fan2Byte.ToString("X2") : "读不到")
			+ (r1 && r2 ? " ⇒ ≈ " + (Math.Max(fan1Byte, fan2Byte) * 100) + " RPM（×100 口径）" : ""));
		if (r1 && r2)
		{
			rpm = Math.Max(fan1Byte, fan2Byte) * 100;
			return true;
		}
		return false;
	}

	private static bool ReadEcRegisters(Action<string> log, string tag, out int fan1, out int fan2, out int mask)
	{
		bool r1 = EcLpcIo.TryReadByte(EcLpcIo.Fan1TargetAddress, out fan1);
		bool r2 = EcLpcIo.TryReadByte(EcLpcIo.Fan2TargetAddress, out fan2);
		bool r3 = EcLpcIo.TryReadByte(EcLpcIo.FanManualMaskAddress, out mask);
		log(tag + "：0xC83C=" + (r1 ? "0x" + fan1.ToString("X2") : "读不到")
			+ " · 0xC83D=" + (r2 ? "0x" + fan2.ToString("X2") : "读不到")
			+ " · 0xB20=" + (r3 ? "0x" + mask.ToString("X2")
				+ "（bit1=" + (((mask & 0x02) != 0) ? "1" : "0") + " · bit3=" + (((mask & 0x08) != 0) ? "1" : "0") + "）" : "读不到"));
		return r1 && r2 && r3;
	}

	private static string ReadHex(int address)
	{
		return EcLpcIo.TryReadByte(address, out int v) ? "0x" + v.ToString("X2") : "读不到";
	}

	private static string RawHex(BaolongdaWmi wmi, BaolongdaWmi.Function function, byte fanType)
	{
		try
		{
			if (!wmi.TryInvokeRawGet(function, new byte[1] { fanType }, out byte[] raw) || raw == null)
			{
				return "读不到";
			}
			int count = Math.Min(8, raw.Length);
			return "[" + (count > 0 ? BitConverter.ToString(raw, 0, count).Replace('-', ' ') : "空") + "]";
		}
		catch
		{
			return "读不到";
		}
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
