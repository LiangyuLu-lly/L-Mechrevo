// 2026-10-18（宝龙达 · 风扇接管）：⭐⭐ **EC 写通路最小验证** `--fan-ec-test` ⭐⭐
//
// ⭐⭐ 背景（机主 0.291.9.14 真机）：WMI 功能号 **20=1 会卡死**（连 try/finally 都跑不完）
//    ⇒ 机主批准改走 **EC 直写**（与开源蛟龙 `Blding64` 同语义，⛔ 未复制代码）✅
//    ⭐ 本命令只做**最小验证**：先只验 Fan1（CPU）一个通道，通过后再验 Fan2。
//
// ⭐ 步骤（⭐ 白名单写：只碰 0xC83C/0xC83D/0xB20）：
//    ① 只读基线：EC 0xC83C/0xC83D/0xB20（＋0x2000/0x1060/0xC411/0xC834/0xC835 对照）· WMI 13 转速
//    ② 写 Fan1 目标 = 当前档位 + 5（≤68）→ 0xB20 |= 0x02（bit1 = Fan1 手动接管）
//    ③ 等 2 s → 读 WMI 13 实测转速
//    ④ 若转速 ≥ 基线 + 200 RPM ⇒ 判定 EC 通路**可用**；否则**不可用**
//    ⑤ **finally 恢复**：目标 0、0xB20 清 0x02|0x08 → 再读一次基线确认
//
// ⛔ 安全边界（硬性）：
//    · ⛔ 只写 3 个白名单地址（`EcLpcIo.TryWriteFanTarget` / `TryWriteFanManualMask` 内部再校验一次）；
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
		int exitCode = 1;
		try
		{
			L("=== 宝龙达风扇 EC 写通路最小验证（--fan-ec-test · Fan1 单通道）===");
			L("⚠️ 安全：只写 0xC83C/0xC83D/0xB20 三个白名单地址；try/finally 一定恢复（目标 0 + 清 0x02|0x08）");
			L("⚠️ 不写 WMI 20/21（20=1 真机卡死）；WMI 只发 Get");
			L("");

			string board = ReadBoardProduct();
			bool is30 = string.Equals(board, "MRID6", StringComparison.OrdinalIgnoreCase);
			L("主板 = " + (board.Length == 0 ? "(空/读不到)" : board) + " ⇒ " + (is30 ? "30 系" : "40 系"));
			L("");

			// ── ① 只读基线 ──
			L("── ① 只读基线 ──");
			bool ecOk = ReadEcRegisters(L, "基线", out int fan1, out int fan2, out int mask);
			L("EC 0x2000（芯片 ID）= " + ReadHex(0x2000) + " · 0x1060（解锁寄存器）= " + ReadHex(0x1060)
				+ " · 0xC411（EC 版本）= " + ReadHex(0xC411));
			L("EC 0xC834 / 0xC835（上游转速字节，仅对照）= " + ReadHex(0xC834) + " / " + ReadHex(0xC835));
			L("");
			try
			{
				wmi = BaolongdaWmi.TryCreate();
			}
			catch
			{
				wmi = null;
			}
			int baselineRpm = -1;
			if (wmi != null)
			{
				if (wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds))
				{
					baselineRpm = Math.Max(speeds.First, speeds.Second);
				}
				L("WMI 13 基线转速 = " + (baselineRpm >= 0 ? baselineRpm + " RPM（通道 " + speeds2String(wmi) + "）" : "读不到"));
				L("WMI 20/21 只读 hex：" + RawHex(wmi, BaolongdaWmi.Function.MaxFanSpeedSwitch, 0)
					+ " / " + RawHex(wmi, BaolongdaWmi.Function.MaxFanSpeed, 0));
			}
			else
			{
				L("WMI：⚠️ 没有 MICommonInterface 实例 ⇒ **无法读 13 判定转速变化**");
			}
			L("");

			// ── 前置判据：EC 可读 + WMI 13 可读，否则**不写** ──
			if (!ecOk)
			{
				L("结论：⛔ 不可用 —— EC 三地址未全部读到合理值（" + EcLpcIo.DescribeChannel() + "）");
				L("⛔ 本命令**未写任何 EC 字节**");
				return 1;
			}
			if (wmi == null || baselineRpm < 0)
			{
				L("结论：⛔ 不可用 —— 无法读 WMI 13 转速，**无法判定**写入是否生效（⛔ 不做无判据的写）");
				L("⛔ 本命令**未写任何 EC 字节**");
				return 1;
			}
			if (fan1 < 0 || fan1 > EcLpcIo.FanTargetMaxUnits100Rpm)
			{
				L("结论：⛔ 不可用 —— 基线 0xC83C=" + fan1 + " 超出 0..68（地址语义可能不对）");
				return 1;
			}

			// ── ② 写 Fan1 目标 + 置 bit1 ──
			int currentUnits = Math.Clamp(baselineRpm / 100, 0, EcLpcIo.FanTargetMaxUnits100Rpm);
			int targetUnits = Math.Min(currentUnits + 5, EcLpcIo.FanTargetMaxUnits100Rpm);
			L("── ② 写测试（Fan1 单通道）──");
			L("目标：0xC83C ← " + targetUnits + "（" + (targetUnits * 100) + " RPM）· 0xB20 |= 0x02（Fan1 手动接管）");
			wroteEc = true; // ⭐ 先置恢复标志：即使下面异常，finally 也会恢复 ✅
			bool w1 = EcLpcIo.TryWriteFanTarget(EcLpcIo.Fan1TargetAddress, targetUnits, out int rb1);
			L("写 0xC83C ⇒ ok=" + w1 + " · 回读=" + (rb1 < 0 ? "读不到" : rb1.ToString()));
			bool w2 = EcLpcIo.TryWriteFanManualMask(fan1Manual: true, fan2Manual: false, out int rbMask);
			L("写 0xB20（bit1=1，保留其它位）⇒ ok=" + w2 + " · 回读=0x"
				+ (rbMask < 0 ? "读不到" : rbMask.ToString("X2"))
				+ "（bit1=" + (((rbMask & 0x02) != 0) ? "1" : "0") + " · bit3=" + (((rbMask & 0x08) != 0) ? "1" : "0") + "）");
			L("");

			// ── ③ 等起转 → 读 13 ──
			L("── ③ 等待 2 s 后读 WMI 13 ──");
			Thread.Sleep(2000);
			int afterRpm = -1;
			if (wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds afterSpeeds))
			{
				afterRpm = Math.Max(afterSpeeds.First, afterSpeeds.Second);
			}
			L("WMI 13 写后转速 = " + (afterRpm >= 0 ? afterRpm + " RPM（" + afterSpeeds.First + "/" + afterSpeeds.Second + "）" : "读不到"));
			ReadEcRegisters(L, "写后", out int afterFan1, out int afterFan2, out int afterMask);
			L("");

			// ── ④ 判定 ──
			int delta = (afterRpm >= 0) ? (afterRpm - baselineRpm) : int.MinValue;
			bool available = w1 && w2 && afterRpm >= 0 && delta >= 200;
			L("── ④ 结论 ──");
			L("转速变化 = " + (delta == int.MinValue ? "无法读取" : delta + " RPM") + "（判据：≥ +200 RPM）");
			if (available)
			{
				L("结论：✅ **EC 写通路可用**（Fan1 目标写入 + 0xB20 bit1 置位 ⇒ 转速上升 " + delta + " RPM）");
				L("⭐ 下一步：机主回传本报告 ⇒ 再验 Fan2（0xC83D + 0xB20 bit3），然后接产品闭环 ✅");
				exitCode = 0;
			}
			else
			{
				L("结论：⛔ **EC 写通路不可用/未确认** —— 写入未使转速明显上升（≥+200 RPM）");
				L("⚠️ 可能原因：地址语义不同 / 需先解锁（0x1060 bit7）/ 风扇当前已在高转速 / EC 写被拒");
				L("⭐ 请把本报告发回，并保留 0x2000/0x1060 的读数供判断");
			}
		}
		catch (Exception ex)
		{
			L("测试异常：" + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		finally
		{
			// ⭐⭐ 硬性要求：无论成功/失败/异常，都**恢复**（目标 0 + 清 0x02|0x08）✅
			if (wroteEc)
			{
				L("");
				L("── ⑤ finally 恢复（目标 0 · 0xB20 清 0x02|0x08）──");
				try
				{
					bool restored = EcLpcIo.TryRestoreFanFirmware(out string restoreDetail);
					L("恢复 = " + restored + " · " + restoreDetail);
					if (!restored)
					{
						L("⚠️ 恢复未完全成功 —— 请立即把本报告发回；必要时重启机器交回固件 ⚠️");
					}
				}
				catch (Exception ex)
				{
					L("⚠️ finally 恢复异常（请把报告发回）：" + ex.GetType().Name + " " + ex.Message);
				}
				Thread.Sleep(300);
				ReadEcRegisters(L, "恢复后", out _, out _, out _);
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

	private static string speeds2String(BaolongdaWmi wmi)
	{
		return wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds s) ? s.First + "/" + s.Second : "读不到";
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
