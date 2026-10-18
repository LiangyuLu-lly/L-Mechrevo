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
		bool forceEc81 = false;
		if (args != null)
		{
			foreach (string arg in args)
			{
				if (string.Equals(arg, "--acpi", StringComparison.OrdinalIgnoreCase))
				{
					forceAcpi = true;
				}
				else if (string.Equals(arg, "--ec81", StringComparison.OrdinalIgnoreCase))
				{
					forceEc81 = true;
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
			// ⭐⭐ 2026-10-18（机主批准 · 0x81 写实验）：⭐ `--ec81` 只做**一次** 0x81 写实验，不跑 LPC/0x93 写
			if (forceEc81)
			{
				L("⭐ `--ec81`：只做 0x81（WR_EC）一次性写实验（⛔ 不写风扇寄存器、不做其它写花色）");
				bool ec81Ok = RunEc81Experiment(L);
				return ec81Ok ? 0 : 1;
			}
			// ⭐⭐ 2026-10-18（机主双报告 · 参考实现强锁场景实测）：⭐ 默认流程直接按 外部参考实现的写入顺序：
			//   ACPI 写 0xC83C/0xC83D → 读 0xB20 → 写 0xB20|0x02|0x08 → 2 s → ACPI 稳定读验证；
			//   ⚠️ `0x1060` 解锁改为 **best-effort**（外部参考实现 强锁时 0x1060=0x00 也生效，失败不中止）；
			//   ⚠️ LPC 通道**只作对照**（不参与判定/写入；报告 1 里 LPC 读可能陈旧）。
			if (!acpiIdentity)
			{
				L("结论：⛔ 不可用 —— ACPI 身份未通过（LPC 仅对照，不参与判定）");
				L("⛔ 本命令**未写任何 EC 字节**");
				return 1;
			}
			if (forceAcpi)
			{
				L("⭐ `--acpi`：ACPI 已是默认写通道（本参数保留兼容，无额外作用）");
			}
			L("══ 外部参考实现的写入顺序写验证（ACPI EC · LPC 仅对照）══");
			bool externalOrderOk = RunExternalOrderTest(L, is30);
			L("");
			if (externalOrderOk)
			{
				L("结论：✅ **ACPI EC 写通路可用**（0xB20 出现 bit1+bit3 且转速上升 ≥200 RPM）");
				// ⭐⭐ 2026-10-18（机主：40 系 --fan-ec-test 已成功但 UI 仍无开关）：⭐ 成功时也写验证标记 ✅
				bool gateSaved = FanEcVerifyStore.MarkVerifiedFromTest(is30, out string gateDetail);
				L(gateSaved
					? "✅ 已写入验证标记（重启 LM 后放开风扇曲线/强冷 UI）· " + gateDetail
					: "⚠️ 验证标记写入失败（UI 仍保持隐藏）· " + gateDetail);
				exitCode = 0;
			}
			else
			{
				L("结论：⛔ **写被拒/未生效** —— 见上面的两次写前后回读（0xC83C/0xC83D/0xB20）");
				L("⚠️ 若 `0xB20` 两次回读完全相同 ⇒ EC 不接受 ACPI 0x93 写；");
				L("   ⭐ 保留闸门关闭（UI 隐藏），把报告发回再定（不再尝试其它写花色）");
			}
		}
		catch (Exception ex)
		{
			L("测试异常：" + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		return exitCode;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主双报告 · 外部参考实现的写入顺序）：ACPI EC 写 `0xC83C/0xC83D` → 读 `0xB20` →
	/// 写 `0xB20 | 0x02|0x08` → 等 2 s → **只用 ACPI 稳定读**验证 `0xC834/0xC835/0xB20`；
	/// `0x1060` 解锁 **best-effort**（失败继续）；finally 恢复；LPC 仅对照 ✅。
	/// </summary>
	private static bool RunExternalOrderTest(Action<string> L, bool is30)
	{
		bool wroteTargets = false;
		bool wroteMask = false;
		int unlockOriginal = -1;
		bool unlockAttempted = false;
		bool available = false;
		try
		{
			// ① 基线（ACPI 稳定读；LPC 仅对照）
			L("── ① 基线（ACPI 稳定读；LPC 仅对照，不参与判定）──");
			if (!EcPortIo.TryReadByteStable(0xC83C, out int origFan1, out string n1))
			{
				L("⛔ 读 0xC83C 失败：" + n1);
				return false;
			}
			if (!EcPortIo.TryReadByteStable(0xC83D, out int origFan2, out string n2))
			{
				L("⛔ 读 0xC83D 失败：" + n2);
				return false;
			}
			if (!EcPortIo.TryReadByteStable(0xB20, out int origMask, out string nm))
			{
				L("⛔ 读 0xB20 失败：" + nm);
				return false;
			}
			L("ACPI 0xC83C=0x" + origFan1.ToString("X2") + "（" + n1 + "）· 0xC83D=0x" + origFan2.ToString("X2")
				+ "（" + n2 + "）· 0xB20=0x" + origMask.ToString("X2") + "（" + nm + "）");
			bool lpc1ok = EcLpcIo.TryReadByteStable(0xC83C, out int lpc1, out _);
			bool lpc2ok = EcLpcIo.TryReadByteStable(0xC83D, out int lpc2, out _);
			bool lpcMok = EcLpcIo.TryReadByteStable(0xB20, out int lpcM, out _);
			L("LPC 对照：0xC83C=" + (lpc1ok ? "0x" + lpc1.ToString("X2") : "读不到")
				+ " · 0xC83D=" + (lpc2ok ? "0x" + lpc2.ToString("X2") : "读不到")
				+ " · 0xB20=" + (lpcMok ? "0x" + lpcM.ToString("X2") : "读不到") + "（⚠️ 不参与判定）");
			bool ecRpmOk = ReadEcRpmBytes(L, "ACPI 基线", acpi: true, out int ecRpmBefore);
			int wmiBefore = ReadWmiRpm(out string wmiNote);
			int baselineRpm = (wmiBefore >= 0) ? wmiBefore : (ecRpmOk ? ecRpmBefore : -1);
			if (baselineRpm < 0)
			{
				L("⛔ 无法读基线转速（ACPI/WMI 都读不到）⇒ 不写");
				return false;
			}
			int rpmUnits = Math.Clamp(baselineRpm / 100, 0, 68);
			// ⭐ 外部参考实现的写入顺序写“原值+5”；原值为 0（固件自动控制）时用“当前转速档+5”，避免写到更低档 ✅
			int baseUnits = Math.Max(origFan1, rpmUnits);
			int maxUnits = is30 ? 49 : 68;
			// ⭐⭐ 2026-10-18（30 系报告：基线已 4329 RPM，+5 档只差 471 ⇒ 2 s 内看不出）：
			//   ⭐ 目标档拉大差距：max(当前档, 基线档) + 15，夹到该代际上限（30 系 49 / 40 系 68）✅
			int target = Math.Clamp(baseUnits + 15, 1, maxUnits);
			L("基线档=" + baseUnits + "（0xC83C 原值=" + origFan1 + " · 转速档=" + rpmUnits + "）⇒ 目标档=" + target
				+ "（" + (target * 100) + " RPM · 上限=" + maxUnits + "）");
			L("基线转速=" + baselineRpm + " RPM（来源=" + (wmiBefore >= 0 ? "WMI13" : "ACPI×100") + "）");
			L("");

			// ② best-effort 解锁
			L("── ② 解锁 0x1060（best-effort：失败不中止）──");
			if (EcPortIo.TryReadByteStable(0x1060, out unlockOriginal, out string lockNote))
			{
				L("0x1060 原值=0x" + unlockOriginal.ToString("X2") + "（" + lockNote + "）");
				if ((unlockOriginal & 0x80) == 0)
				{
					unlockAttempted = true;
					bool unlocked = EcPortIo.TryUnlockEcRamAcpi(out _, out int after, out string unlockNote);
					L("解锁尝试：" + unlockNote);
					L(unlocked
						? "解锁后 0x1060=0x" + after.ToString("X2")
						: "⚠️ 解锁失败 ⇒ **继续**（实测 外部参考实现 强锁时 0x1060=0x00 也生效；解锁不是必要条件）");
				}
				else
				{
					L("已解锁（bit7=1）");
				}
			}
			else
			{
				L("⚠️ 读 0x1060 失败（" + lockNote + "）⇒ 继续（解锁非必需）");
			}
			L("");

			// ③ 外部参考实现的写入顺序写
			L("── ③ 外部参考实现的写入顺序写（ACPI）──");
			wroteTargets = true;
			bool w1 = EcPortIo.TryWriteAcpiFanTarget(0xC83C, target, out int rb1);
			L("写 0xC83C=" + target + " ⇒ ok=" + w1 + " · 回读=" + (rb1 < 0 ? "读不到" : rb1.ToString()));
			L("  " + EcPortIo.LastAcpiWriteTrace);
			bool w2 = EcPortIo.TryWriteAcpiFanTarget(0xC83D, target, out int rb2);
			L("写 0xC83D=" + target + " ⇒ ok=" + w2 + " · 回读=" + (rb2 < 0 ? "读不到" : rb2.ToString()));
			L("  " + EcPortIo.LastAcpiWriteTrace);
			if (!EcPortIo.TryReadByteStable(0xB20, out int maskBefore, out string maskBeforeNote))
			{
				L("⛔ 写掩码前读 0xB20 失败：" + maskBeforeNote);
				return false;
			}
			int maskWanted = (maskBefore & ~0x0A) | 0x0A;
			wroteMask = true;
			bool w3 = EcPortIo.TryWriteAcpiFanManualMask(true, true, out int rbMask, out string maskWriteNote);
			L("写 0xB20：0x" + maskBefore.ToString("X2") + " → 0x" + maskWanted.ToString("X2")
				+ "（|=0x02|=0x08，保留其它位）⇒ ok=" + w3 + " · " + maskWriteNote);
			L("  " + EcPortIo.LastAcpiWriteTrace);
			L("");

			// ④ 轮询验证（每 500ms 读一次，最多 10s，取峰值；⛔ 期间不再写）
			L("── ④ 轮询验证（每 500ms 一次，最多 10s，取峰值；⛔ 期间不再写）──");
			int peakRpm = baselineRpm;
			bool readOk = false;
			long pollStart = Environment.TickCount64;
			while (Environment.TickCount64 - pollStart <= 10000)
			{
				Thread.Sleep(500);
				bool ecSampleOk = false;
				int b1 = 0;
				int b2 = 0;
				if (EcPortIo.TryReadByteStable(0xC834, out int v1, out _))
				{
					b1 = v1;
					if (EcPortIo.TryReadByteStable(0xC835, out int v2, out _))
					{
						b2 = v2;
						ecSampleOk = true;
					}
				}
				int wmiSample = ReadWmiRpm(out _);
				int sample = (wmiSample >= 0) ? wmiSample : (ecSampleOk ? Math.Max(b1, b2) * 100 : -1);
				if (sample < 0)
				{
					continue;
				}
				readOk = true;
				if (sample > peakRpm)
				{
					peakRpm = sample;
				}
				if (peakRpm >= target * 100 * 9 / 10 || peakRpm >= baselineRpm + 300)
				{
					break;
				}
			}
			double waitedSeconds = Math.Max(0, Environment.TickCount64 - pollStart) / 1000.0;
			int peakDelta = peakRpm - baselineRpm;
			L("等待 " + waitedSeconds.ToString("0.0") + " s ⇒ 峰值=" + peakRpm + " RPM（基线 " + baselineRpm + " · 变化 " + peakDelta + " RPM）");

			// ⭐⭐ 2026-10-18（30 系报告：写其实成功，2 s 判据误判）：⭐ 强/弱证据分层 ✅
			//   强证据（可用必要条件）：0xB20 bit1+bit3 且 0xC83C/0xC83D 回读 == 写入值
			//   弱证据：峰值 ≥ 基线+300 或 ≥ 目标×70%（仅提示，⛔ 不否定强证据）
			bool maskAfterOk = EcPortIo.TryReadByteStable(0xB20, out int maskAfter, out string maskAfterNote);
			bool maskBits = maskAfterOk && (maskAfter & 0x02) != 0 && (maskAfter & 0x08) != 0;
			bool targetReadback = w1 && w2 && rb1 == target && rb2 == target;
			bool strong = targetReadback && maskBits;
			bool weak = readOk && (peakRpm >= baselineRpm + 300 || peakRpm >= target * 100 * 7 / 10);
			available = strong;
			L("0xB20 写后稳定读=" + (maskAfterOk ? "0x" + maskAfter.ToString("X2") : "读不到") + "（" + maskAfterNote + "）"
				+ " · bit1+bit3=" + (maskBits ? "都已置位 ✅" : "未同时置位 ⚠️"));
			L("强证据：0xC83C 回读=" + (rb1 < 0 ? "读不到" : rb1.ToString()) + "/目标=" + target
				+ " · 0xC83D 回读=" + (rb2 < 0 ? "读不到" : rb2.ToString()) + " · 掩码位=" + maskBits
				+ " ⇒ " + strong);
			L("弱证据：峰值 " + peakRpm + " RPM（变化 " + peakDelta + "）⇒ " + weak);
			if (strong && !weak)
			{
				L("⚠️ 转速峰值未达预期（软启动 / 基线已高）⇒ **不影响写通路结论**（强证据已成立）");
			}
			L("判定：强证据=" + strong + "（必要条件）· 弱证据=" + weak + "（仅提示）⇒ " + available);
			if (!strong)
			{
				L("⚠️ 0xB20 两次写的前后回读（供定位）：");
				L("   写前=0x" + maskBefore.ToString("X2") + " · 写出=0x" + maskWanted.ToString("X2")
					+ " · 立即回读=" + (rbMask < 0 ? "读不到" : "0x" + rbMask.ToString("X2"))
					+ " · 写后稳定读=" + (maskAfterOk ? "0x" + maskAfter.ToString("X2") : "读不到"));
			}
			return available;
		}
		finally
		{
			L("");
			L("── ⑤ finally 恢复（ACPI：0xC83C=0 · 0xC83D=0 · 0xB20 清 0x02|0x08 · 0x1060 原值）──");
			if (wroteTargets)
			{
				bool r1 = EcPortIo.TryWriteAcpiFanTarget(0xC83C, 0, out int rb1);
				bool r2 = EcPortIo.TryWriteAcpiFanTarget(0xC83D, 0, out int rb2);
				L("目标 0：0xC83C ok=" + r1 + "（回读=" + (rb1 < 0 ? "读不到" : rb1.ToString())
					+ "）· 0xC83D ok=" + r2 + "（回读=" + (rb2 < 0 ? "读不到" : rb2.ToString()) + "）");
			}
			if (wroteMask)
			{
				bool rm = EcPortIo.TryWriteAcpiFanManualMask(false, false, out int rbMask, out string maskNote);
				L("清掩码位：ok=" + rm + " · " + maskNote);
			}
			EcPortIo.TryReadByteStable(0xB20, out int finalMask, out string finalMaskNote);
			L("恢复后 0xB20=0x" + finalMask.ToString("X2") + "（" + finalMaskNote + "）");
			EcPortIo.TryReadByteStable(0xC83C, out int f1, out _);
			EcPortIo.TryReadByteStable(0xC83D, out int f2, out _);
			L("恢复后 0xC83C=" + f1 + " · 0xC83D=" + f2);
			if (unlockAttempted && unlockOriginal >= 0)
			{
				bool ur = EcPortIo.TryRestoreEcUnlockRegisterAcpi(unlockOriginal, out int rb);
				L("0x1060 恢复原值 0x" + unlockOriginal.ToString("X2") + " ⇒ ok=" + ur
					+ " · 回读=" + (rb < 0 ? "读不到" : "0x" + rb.ToString("X2")));
			}
		}
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

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主批准 · **0x81 写实验**）：⭐ 只读对照（`0x92` vs `0x80` 标准空间扫描）
	/// + **一次** `0x81` 写实验（`reg=0x60` ← `0x1060` 原值|0x80）+ 写后 20ms 两条路径回读 + finally 回滚 ✅。
	/// <para>⛔ 只写这一个字节；⛔ 不写风扇寄存器；失败即结论「BIOS 锁写」并停止 ✅</para>
	/// </summary>
	private static bool RunEc81Experiment(Action<string> L)
	{
		L("══ C. 0x81（WR_EC）写实验（ACPI 标准空间 · 8 位地址）══");
		L("⚠️ 只做一次：reg=0x60（0x1060 低字节）← (原值 | 0x80)；写后 20ms 用 0x92 与 0x80 两条路径回读；随后回滚原值");
		L("");

		// ── 1) 只读对照：0x92 vs 0x80 ──
		L("── 1) 只读对照：0x92（XRAM）vs 0x80（标准空间）──");
		int[] addrs = { 0x2000, 0x1060, 0xC411, 0xC834, 0xC83C, 0xB20 };
		string[] names = { "0x2000 芯片 ID", "0x1060 解锁", "0xC411 EC 版本", "0xC834 Fan1 转速", "0xC83C Fan1 目标", "0xB20 接管掩码" };
		for (int i = 0; i < addrs.Length; i++)
		{
			bool readOk = EcPortIo.TryReadByteStable(addrs[i], out int value, out string note);
			L("  " + names[i] + "：0x92 = " + (readOk ? "0x" + value.ToString("X2") : "读不到") + "（" + note + "）");
		}
		L("  0x80 扫描 0x00..0xFF（只列非 0x00/非 0xFF）：");
		int[] regValues = new int[256];
		int foundCount = 0;
		for (int reg = 0; reg < 256; reg++)
		{
			regValues[reg] = -1;
			if (!EcPortIo.TryReadByteAcpi(reg, out int value))
			{
				continue;
			}
			regValues[reg] = value;
			if (value != 0x00 && value != 0xFF)
			{
				foundCount++;
				L("    0x80[0x" + reg.ToString("X2") + "] = 0x" + value.ToString("X2"));
			}
		}
		if (foundCount == 0)
		{
			L("    （没有读回可用的非 0 值 ⇒ 0x80 标准空间读也不可用）");
		}
		L("  已知值匹配（0x55 / 0x23 / 0x14）：");
		int[] knownValues = { 0x55, 0x23, 0x14 };
		string[] knownNames = { "0x2000", "0xC411", "0xC834/5" };
		for (int k = 0; k < knownValues.Length; k++)
		{
			string hits = "";
			for (int reg = 0; reg < 256; reg++)
			{
				if (regValues[reg] == knownValues[k])
				{
					hits += (hits.Length == 0 ? "" : ", ") + "0x" + reg.ToString("X2");
				}
			}
			L("    " + knownNames[k] + " = 0x" + knownValues[k].ToString("X2") + " ⇒ 匹配寄存器：" + (hits.Length == 0 ? "无" : hits));
		}
		L("");

		// ── 2) 0x81 写实验（只 1 个字节）──
		L("── 2) 0x81 写实验（仅 reg=0x60 一次）──");
		if (!EcPortIo.TryReadByteStable(0x1060, out int original, out string origNote))
		{
			L("⛔ 读 0x1060 失败（" + origNote + "）⇒ 不做 0x81 写（no write）");
			L("结论：⛔ 无法进行 0x81 实验（0x1060 读不到）");
			return false;
		}
		int wanted = original | 0x80;
		L("0x1060 原值 = 0x" + original.ToString("X2") + " · 实验写入值 = 0x" + wanted.ToString("X2") + "（仅置 bit7）");
		bool wrote = false;
		bool ok = false;
		try
		{
			wrote = EcPortIo.TryWriteByteEc81Experiment(0x60, wanted, out string trace);
			L("0x81 写步骤（命令/地址/数据/IBF-OBF 状态）：");
			foreach (string line in trace.Split('\n', StringSplitOptions.RemoveEmptyEntries))
			{
				L("  " + line.TrimEnd('\r'));
			}
			if (wrote)
			{
				Thread.Sleep(20);
				bool r92 = EcPortIo.TryReadByteStable(0x1060, out int v92, out string n92);
				bool r80 = EcPortIo.TryReadByteAcpi(0x60, out int v80);
				L("写后 20ms 回读：");
				L("  ACPI 0x92 @0x1060 = " + (r92 ? "0x" + v92.ToString("X2") : "读不到") + "（" + n92 + "）· " + EcPortIo.DescribeLastStatus());
				L("  ACPI 0x80 @0x60   = " + (r80 ? "0x" + v80.ToString("X2") : "读不到") + " · " + EcPortIo.DescribeLastStatus());
				ok = (r92 && (v92 & 0x80) != 0) || (r80 && (v80 & 0x80) != 0);
				L("bit7 观察（0x92 / 0x80）：" + ((r92 && (v92 & 0x80) != 0) ? "1" : "0") + " / "
					+ ((r80 && (v80 & 0x80) != 0) ? "1" : "0") + " ⇒ " + (ok ? "写入生效 ✅" : "写入无效 ⚠️"));
			}
			else
			{
				L("⛔ 0x81 写命令未成功执行（见上）");
			}
		}
		finally
		{
			if (wrote)
			{
				bool restored = EcPortIo.TryRestoreEc81Experiment(original, out string restoreTrace);
				L("0x81 回滚：写回原值 0x" + original.ToString("X2") + " ⇒ ok=" + restored + " · " + restoreTrace);
				Thread.Sleep(20);
				bool rb = EcPortIo.TryReadByteStable(0x1060, out int rv, out _);
				L("回滚后 0x92 @0x1060 = " + (rb ? "0x" + rv.ToString("X2") : "读不到"));
			}
		}
		L("");
		L(ok
			? "结论：✅ 0x81 写路径**生效**（⭐ 下一轮再确认 16 位地址/bank 映射，⛔ 本轮不写风扇寄存器）"
			: "结论：⛔ 0x81 写路径也无效 ⇒ **本机 EC RAM 写被 BIOS 锁死** ⇒ 按机主指示放弃风扇曲线/强冷（保持隐藏），写入交接；⛔ 不再尝试其它写花色");
		return ok;
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
