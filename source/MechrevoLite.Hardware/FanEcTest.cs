// 2026-10-18（宝龙达 · 风扇接管）：⭐⭐ **EC 写通路最小验证** `--fan-ec-test [--acpi]` ⭐⭐
//
// ⭐⭐ 背景：WMI 功能号 20=1 会卡死；LPC/SuperIO（`EcLpcIo`）**读成功、写被 EC 拒绝**（ioctl 全 OK、回读不变）。
//    ⭐ 机主实测：ACPI EC（`EcPortIo`，0x62/0x66）也能读到同一批值（0x2000=0x55 / 0xC834=0x14…）
//    ⇒ ⭐ 写改走 ACPI EC 的 XRAM mailbox（命令 0x93；读是 0x92）✅
//
// ⭐ 流程（先 LPC，失败自动改试 ACPI；⭐ `--acpi` 可强制只测 ACPI）：
//    0. ACPI EC 只读对照（两通道读数并列）
//    ① 身份（LPC / ACPI 各判一次：0x2000==0x55 且 0xC411 非 0/0xFF；**稳定读 3 次取多数**）
//    ② LPC 写测试（若身份通过）：解锁 0x1060|0x80 → Fan1 目标+5 + 0xB20 bit1 → 2 s → 读回
//       失败 ⇒ 自动进入 ③
//    ③ ACPI 写测试：解锁 → Fan1 目标+5 + 0xB20 bit1 → 2 s → ACPI 读 0xC834/0xC835 + WMI 13
//    ④ **finally 恢复**（两条通道各自）：目标 0 · 清 0x02|0x08 · 0x1060 写回原值
//
// ⛔ 安全边界（硬性）：
//    · ⛔ 只写白名单（3+1）：0xC83C / 0xC83D / 0xB20（仅 bit1/bit3）· 0x1060（仅 `|0x80`）；
//    · ⛔ 不写 WMI 20/21（20=1 已知卡死）；WMI 只发 Get；
//    · ⭐ 每次写后**立刻回读比对**（ACPI 侧用稳定读）；失败立即停止，不做无判据的写；
//    · ⛔ 任何异常不抛出；⭐ 全程 try/finally，**一定执行恢复**；⛔ 不打包、不 kill LM。
//
// ⭐ 输出：桌面 `lm-fan-ec-test-<时间戳>.txt` + `log.txt`（前缀 `[fan-ec-test]`）。
// ⭐ 用法：`L-Mechrevo.exe --fan-ec-test`（需管理员）· `L-Mechrevo.exe --fan-ec-test --acpi`
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ 宝龙达风扇 **EC 写通路最小验证**（<c>--fan-ec-test</c>）：LPC 失败自动改试 ACPI EC ✅。
/// </summary>
internal static class FanEcTest
{
	private const byte UnlockBit = 0x80;

	internal static int Run(string[] args)
	{
		bool forceAcpi = false;
		if (args != null)
		{
			foreach (string arg in args)
			{
				if (string.Equals(arg, "--acpi", StringComparison.OrdinalIgnoreCase))
				{
					forceAcpi = true;
				}
			}
		}
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

		int exitCode = 1;
		try
		{
			L("=== 宝龙达风扇 EC 写通路验证（--fan-ec-test · 先 LPC 后 ACPI）===");
			L("⚠️ 只写白名单 3+1 地址（0xC83C/0xC83D/0xB20/0x1060；0x1060 仅 |0x80）；每次写后回读；finally 恢复");
			L("⚠️ 不写 WMI 20/21（20=1 真机卡死）；WMI 只发 Get");
			L("");

			string board = ReadBoardProduct();
			bool is30 = string.Equals(board, "MRID6", StringComparison.OrdinalIgnoreCase);
			L("主板 = " + (board.Length == 0 ? "(空/读不到)" : board) + " ⇒ " + (is30 ? "30 系" : "40 系"));
			L("");

			// ── 0. 双通道只读对照 ──
			EcChannelCompare.LogAcpiReadOnly(L);
			L("");

			// ── ① 身份（稳定读）──
			L("── ① EC 身份（稳定读：3 次取多数）──");
			bool lpcIdentity = EcLpcIo.TryReadEcIdentity(out _, out _, out string lpcIdNote);
			L("LPC 身份：" + lpcIdNote + " ⇒ " + (lpcIdentity ? "通过 ✅" : "未通过 ⚠️"));
			bool acpiIdentity = EcPortIo.TryReadEcIdentityAcpi(out _, out _, out string acpiIdNote);
			L("ACPI 身份：" + acpiIdNote + " ⇒ " + (acpiIdentity ? "通过 ✅" : "未通过 ⚠️"));
			L("");
			if (!lpcIdentity && !acpiIdentity)
			{
				L("结论：⛔ 不可用 —— 两条通道身份均未通过");
				L("⛔ 本命令**未写任何 EC 字节**");
				return 1;
			}
			if (forceAcpi)
			{
				L("⭐ `--acpi`：跳过 LPC 写测试，只测 ACPI EC 写");
			}

			// ── ② LPC 写测试（身份通过且未强制 ACPI）──
			if (lpcIdentity && !forceAcpi)
			{
				L("══ A. LPC/SuperIO 写测试（原通道）══");
				bool lpcAvailable = RunLpcWriteTest(L);
				L("");
				if (lpcAvailable)
				{
					L("结论：✅ **LPC 写通路可用**");
					exitCode = 0;
				}
				else
				{
					L("⚠️ LPC 写通路失败（ioctl 成功但 EC 不认 / 或解锁失败）⇒ **自动改试 ACPI EC 写**");
					L("");
				}
			}

			// ── ③ ACPI 写测试（LPC 失败 / 被 --acpi 强制 / LPC 身份未通过）──
			if (exitCode != 0)
			{
				if (!acpiIdentity)
				{
					L("结论：⛔ 不可用 —— ACPI 身份未通过，不写任何 EC 字节");
					return 1;
				}
				L("══ B. ACPI EC 写测试（0x93 mailbox）══");
				bool acpiAvailable = RunAcpiWriteTest(L);
				L("");
				if (acpiAvailable)
				{
					L("结论：✅ **ACPI EC 写通路可用**（LPC 写被 EC 拒绝，但 ACPI EC 接受）");
					L("⭐ 下一步：机主回传本报告 ⇒ 再验 Fan2（0xC83D + bit3），然后接产品闭环 ✅");
					exitCode = 0;
				}
				else
				{
					L("结论：⛔ **两条写通道都被拒** ⇒ 判定「本机 EC RAM 写被 BIOS 锁死」");
					L("⇒ 按机主指示：**放弃风扇曲线/强冷**（保持 UI 隐藏），并写入交接说明 ⚠️");
				}
			}
		}
		catch (Exception ex)
		{
			L("测试异常：" + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		return exitCode;
	}

	/// <summary>⭐ LPC 通道写测试（Fan1 + 0xB20 bit1 + 解锁），内部 try/finally 恢复 ✅。</summary>
	private static bool RunLpcWriteTest(Action<string> L)
	{
		bool wroteFan = false;
		bool unlockAttempted = false;
		int unlockOriginal = -1;
		try
		{
			// ②a 解锁
			L("── ②a 解锁寄存器 0x1060（LPC）──");
			if (!EcLpcIo.TryReadByteStable(EcLpcIo.EcUnlockAddress, out unlockOriginal, out string lockNote))
			{
				L("读 0x1060 失败（" + lockNote + "）⇒ ⛔ 不写");
				return false;
			}
			L("解锁前 0x1060 " + lockNote);
			int unlockAfter = unlockOriginal;
			if ((unlockOriginal & UnlockBit) == 0)
			{
				unlockAttempted = true;
				bool unlockOk = EcLpcIo.TryUnlockEcRam(out _, out int ubAfter, out string unlockNote);
				unlockAfter = ubAfter;
				L("解锁：" + unlockNote);
				L("解锁后 0x1060 = 0x" + (unlockAfter < 0 ? "读不到" : unlockAfter.ToString("X2")));
				LogLpcTrace(L, "解锁写 0x1060 端口序列");
				if (!unlockOk)
				{
					L("⛔ LPC 解锁失败 ⇒ 不继续写风扇");
					return false;
				}
			}
			else
			{
				L("LPC 侧已解锁（bit7=1），无需写 0x1060");
			}

			// ②b 基线
			L("── ②b 只读基线（LPC）──");
			bool ecBeforeOk = ReadEcRpmBytes(L, "LPC 基线", acpi: false, out int ecRpmBefore);
			L("LPC 0xC83C=" + ReadHexLpc(EcLpcIo.Fan1TargetAddress)
				+ " · 0xC83D=" + ReadHexLpc(EcLpcIo.Fan2TargetAddress)
				+ " · 0xB20=" + ReadHexLpc(EcLpcIo.FanManualMaskAddress));
			int wmiBefore = ReadWmiRpm(out string wmiNote);
			L("WMI 13 基线：" + (wmiBefore >= 0 ? wmiBefore + " RPM（" + wmiNote + "）" : "读不到（" + wmiNote + "）"));
			int baselineRpm = (wmiBefore >= 0) ? wmiBefore : (ecBeforeOk ? ecRpmBefore : -1);
			if (baselineRpm < 0)
			{
				L("⛔ 无法读基线转速，不写");
				return false;
			}
			L("基线转速（判定用）= " + baselineRpm + " RPM（来源=" + (wmiBefore >= 0 ? "WMI13" : "EC×100") + "）");

			// ②c 写 Fan1 + 置 bit1
			int currentUnits = Math.Clamp(baselineRpm / 100, 0, EcLpcIo.FanTargetMaxUnits100Rpm);
			int targetUnits = Math.Min(currentUnits + 5, EcLpcIo.FanTargetMaxUnits100Rpm);
			L("── ②c 写 Fan1 目标 = " + targetUnits + "（" + (targetUnits * 100) + " RPM）+ 0xB20 |= 0x02 ──");
			wroteFan = true;
			bool w1 = EcLpcIo.TryWriteFanTarget(EcLpcIo.Fan1TargetAddress, targetUnits, out int rb1);
			L("写 0xC83C=" + targetUnits + " ⇒ ok=" + w1 + " · 回读=" + (rb1 < 0 ? "读不到" : rb1.ToString()));
			LogLpcTrace(L, "写 0xC83C 端口序列");
			bool w2 = EcLpcIo.TryWriteFanManualMask(true, false, out int rbMask);
			L("写 0xB20（bit1=1）⇒ ok=" + w2 + " · 回读=" + (rbMask < 0 ? "读不到" : "0x" + rbMask.ToString("X2")));
			LogLpcTrace(L, "写 0xB20 端口序列");

			// ②d 等 2 s → 读回
			Thread.Sleep(2000);
			bool ecAfterOk = ReadEcRpmBytes(L, "LPC 写后", acpi: false, out int ecRpmAfter);
			int wmiAfter = ReadWmiRpm(out string wmiAfterNote);
			int afterRpm = (wmiAfter >= 0) ? wmiAfter : (ecAfterOk ? ecRpmAfter : -1);
			L("写后转速（判定用）= " + (afterRpm >= 0 ? afterRpm + " RPM（来源=" + (wmiAfter >= 0 ? "WMI13" : "EC×100") + "）" : "读不到"));
			int delta = (afterRpm >= 0) ? afterRpm - baselineRpm : int.MinValue;
			bool available = w1 && w2 && afterRpm >= 0 && delta >= 200;
			L("LPC 转速变化 = " + (delta == int.MinValue ? "无法读取" : delta + " RPM") + "（判据 ≥+200）⇒ " + available);
			return available;
		}
		finally
		{
			// ⭐ finally 恢复：目标 0 + 清位 + 0x1060 原值
			if (wroteFan)
			{
				try
				{
					bool restored = EcLpcIo.TryRestoreFanFirmware(out string detail);
					L("LPC 风扇恢复 = " + restored + " · " + detail);
					LogLpcTrace(L, "风扇恢复 端口序列");
				}
				catch (Exception ex)
				{
					L("⚠️ LPC 风扇恢复异常：" + ex.GetType().Name + " " + ex.Message);
				}
			}
			if (unlockAttempted && unlockOriginal >= 0)
			{
				try
				{
					bool ok = EcLpcIo.TryRestoreEcUnlockRegister(unlockOriginal, out int rb);
					L("LPC 解锁恢复：0x1060 写回 0x" + unlockOriginal.ToString("X2")
						+ " ⇒ 回读 0x" + (rb < 0 ? "读不到" : rb.ToString("X2")) + " ⇒ ok=" + ok);
					LogLpcTrace(L, "解锁恢复 端口序列");
				}
				catch (Exception ex)
				{
					L("⚠️ LPC 解锁恢复异常：" + ex.GetType().Name + " " + ex.Message);
				}
			}
		}
	}

	/// <summary>⭐ ACPI EC 通道写测试（Fan1 + 0xB20 bit1 + 解锁），内部 try/finally 恢复 ✅。</summary>
	private static bool RunAcpiWriteTest(Action<string> L)
	{
		bool wroteFan = false;
		bool unlockAttempted = false;
		int unlockOriginal = -1;
		try
		{
			// ③a 解锁
			L("── ③a 解锁寄存器 0x1060（ACPI EC）──");
			if (!EcPortIo.TryReadByteStable(EcPortIo.AcpiEcUnlockAddress, out unlockOriginal, out string lockNote))
			{
				L("读 0x1060 失败（" + lockNote + "）⇒ ⛔ 不写");
				return false;
			}
			L("解锁前 0x1060 " + lockNote);
			int unlockAfter = unlockOriginal;
			if ((unlockOriginal & UnlockBit) == 0)
			{
				unlockAttempted = true;
				bool unlockOk = EcPortIo.TryUnlockEcRamAcpi(out _, out int ubAfter, out string unlockNote);
				unlockAfter = ubAfter;
				L("解锁：" + unlockNote);
				L("解锁后 0x1060 = 0x" + (unlockAfter < 0 ? "读不到" : unlockAfter.ToString("X2")));
				L("ACPI 写细节：" + EcPortIo.LastAcpiWriteTrace);
				if (!unlockOk)
				{
					L("⛔ ACPI 解锁失败 ⇒ 不继续写风扇");
					return false;
				}
			}
			else
			{
				L("ACPI 侧已解锁（bit7=1），无需写 0x1060");
			}

			// ③b 基线
			L("── ③b 只读基线（ACPI EC）──");
			bool ecBeforeOk = ReadEcRpmBytes(L, "ACPI 基线", acpi: true, out int ecRpmBefore);
			L("ACPI 0xC83C=" + ReadHexAcpi(EcPortIo.AcpiFan1TargetAddress)
				+ " · 0xC83D=" + ReadHexAcpi(EcPortIo.AcpiFan2TargetAddress)
				+ " · 0xB20=" + ReadHexAcpi(EcPortIo.AcpiFanManualMaskAddress));
			int wmiBefore = ReadWmiRpm(out string wmiNote);
			L("WMI 13 基线：" + (wmiBefore >= 0 ? wmiBefore + " RPM（" + wmiNote + "）" : "读不到（" + wmiNote + "）"));
			int baselineRpm = (wmiBefore >= 0) ? wmiBefore : (ecBeforeOk ? ecRpmBefore : -1);
			if (baselineRpm < 0)
			{
				L("⛔ 无法读基线转速，不写");
				return false;
			}
			L("基线转速（判定用）= " + baselineRpm + " RPM（来源=" + (wmiBefore >= 0 ? "WMI13" : "EC×100") + "）");

			// ③c 写 Fan1 + 置 bit1（全部 ACPI EC）
			int currentUnits = Math.Clamp(baselineRpm / 100, 0, EcPortIo.AcpiFanTargetMaxUnits100Rpm);
			int targetUnits = Math.Min(currentUnits + 5, EcPortIo.AcpiFanTargetMaxUnits100Rpm);
			L("── ③c 写 Fan1 目标 = " + targetUnits + "（" + (targetUnits * 100) + " RPM）+ 0xB20 |= 0x02（ACPI）──");
			wroteFan = true;
			bool w1 = EcPortIo.TryWriteAcpiFanTarget(EcPortIo.AcpiFan1TargetAddress, targetUnits, out int rb1);
			L("写 0xC83C=" + targetUnits + " ⇒ ok=" + w1 + " · 回读=" + (rb1 < 0 ? "读不到" : rb1.ToString())
				+ " · " + EcPortIo.LastAcpiWriteTrace);
			bool w2 = EcPortIo.TryWriteAcpiFanManualMask(true, false, out int rbMask, out string maskNote);
			L("写 0xB20（bit1=1）⇒ ok=" + w2 + " · " + maskNote + " · " + EcPortIo.LastAcpiWriteTrace);

			// ③d 等 2 s → ACPI + WMI 读回
			Thread.Sleep(2000);
			bool ecAfterOk = ReadEcRpmBytes(L, "ACPI 写后", acpi: true, out int ecRpmAfter);
			int wmiAfter = ReadWmiRpm(out string wmiAfterNote);
			L("WMI 13 写后：" + (wmiAfter >= 0 ? wmiAfter + " RPM（" + wmiAfterNote + "）" : "读不到（" + wmiAfterNote + "）"));
			int afterRpm = (wmiAfter >= 0) ? wmiAfter : (ecAfterOk ? ecRpmAfter : -1);
			L("写后转速（判定用）= " + (afterRpm >= 0 ? afterRpm + " RPM（来源=" + (wmiAfter >= 0 ? "WMI13" : "EC×100") + "）" : "读不到"));
			L("ACPI 写后 0xC83C=" + ReadHexAcpi(EcPortIo.AcpiFan1TargetAddress)
				+ " · 0xB20=" + ReadHexAcpi(EcPortIo.AcpiFanManualMaskAddress));
			int delta = (afterRpm >= 0) ? afterRpm - baselineRpm : int.MinValue;
			bool available = w1 && w2 && afterRpm >= 0 && delta >= 200;
			L("ACPI 转速变化 = " + (delta == int.MinValue ? "无法读取" : delta + " RPM") + "（判据 ≥+200）⇒ " + available);
			return available;
		}
		finally
		{
			if (wroteFan)
			{
				try
				{
					bool restored = EcPortIo.TryRestoreFanFirmwareAcpi(out string detail);
					L("ACPI 风扇恢复 = " + restored + " · " + detail);
					L("ACPI 恢复写细节：" + EcPortIo.LastAcpiWriteTrace);
				}
				catch (Exception ex)
				{
					L("⚠️ ACPI 风扇恢复异常：" + ex.GetType().Name + " " + ex.Message);
				}
			}
			if (unlockAttempted && unlockOriginal >= 0)
			{
				try
				{
					bool ok = EcPortIo.TryRestoreEcUnlockRegisterAcpi(unlockOriginal, out int rb);
					L("ACPI 解锁恢复：0x1060 写回 0x" + unlockOriginal.ToString("X2")
						+ " ⇒ 回读 0x" + (rb < 0 ? "读不到" : rb.ToString("X2")) + " ⇒ ok=" + ok
						+ " · " + EcPortIo.LastAcpiWriteTrace);
				}
				catch (Exception ex)
				{
					L("⚠️ ACPI 解锁恢复异常：" + ex.GetType().Name + " " + ex.Message);
				}
			}
		}
	}

	/// <summary>⭐ 读 EC 0xC834/0xC835（≈RPM/100，稳定读）并换算成 RPM；<paramref name="acpi" /> 选通道 ✅。</summary>
	private static bool ReadEcRpmBytes(Action<string> log, string tag, bool acpi, out int rpm)
	{
		rpm = -1;
		bool r1;
		bool r2;
		int fan1Byte;
		int fan2Byte;
		string note1;
		string note2;
		if (acpi)
		{
			r1 = EcPortIo.TryReadByteStable(0xC834, out fan1Byte, out note1);
			r2 = EcPortIo.TryReadByteStable(0xC835, out fan2Byte, out note2);
		}
		else
		{
			r1 = EcLpcIo.TryReadByteStable(0xC834, out fan1Byte, out note1);
			r2 = EcLpcIo.TryReadByteStable(0xC835, out fan2Byte, out note2);
		}
		string ch = acpi ? "ACPI" : "LPC";
		log(tag + "：" + ch + " 0xC834=" + (r1 ? "0x" + fan1Byte.ToString("X2") + "（" + note1 + "）" : "读不到（" + note1 + "）")
			+ " · 0xC835=" + (r2 ? "0x" + fan2Byte.ToString("X2") + "（" + note2 + "）" : "读不到（" + note2 + "）")
			+ (r1 && r2 ? " ⇒ ≈ " + (Math.Max(fan1Byte, fan2Byte) * 100) + " RPM" : ""));
		if (r1 && r2)
		{
			rpm = Math.Max(fan1Byte, fan2Byte) * 100;
			return true;
		}
		return false;
	}

	/// <summary>⭐ 只读 WMI 13 转速（⭐ 不写任何 WMI 风扇命令）✅。</summary>
	private static int ReadWmiRpm(out string detail)
	{
		detail = "WMI 不可用";
		int rpm = -1;
		try
		{
			using BaolongdaWmi wmi = BaolongdaWmi.TryCreate();
			if (wmi == null)
			{
				detail = "没有 MICommonInterface 实例";
				return -1;
			}
			if (wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds))
			{
				rpm = Math.Max(speeds.First, speeds.Second);
				detail = "WMI13=" + speeds.First + "/" + speeds.Second;
			}
			else
			{
				detail = "WMI13 读不到";
			}
		}
		catch (Exception ex)
		{
			detail = "WMI 异常：" + ex.GetType().Name;
		}
		return rpm;
	}

	private static string ReadHexLpc(int address)
	{
		return EcLpcIo.TryReadByteStable(address, out int v, out _) ? "0x" + v.ToString("X2") : "读不到";
	}

	private static string ReadHexAcpi(int address)
	{
		return EcPortIo.TryReadByteStable(address, out int v, out _) ? "0x" + v.ToString("X2") : "读不到";
	}

	/// <summary>⭐ 打印 LPC 写的完整端口序列 + ioctl 返回码 ✅。</summary>
	private static void LogLpcTrace(Action<string> log, string title)
	{
		log(title + "（ioctl 最后一条：ok=" + EcLpcIo.LastIoctlOk + " · Win32=" + EcLpcIo.LastIoctlError + "）");
		string trace = EcLpcIo.LastWriteTrace;
		if (string.IsNullOrWhiteSpace(trace))
		{
			log("  （无端口序列记录 —— 可能在模块装载/互斥体阶段就失败）");
			return;
		}
		foreach (string line in trace.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			log("  " + line.TrimEnd('\r'));
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
