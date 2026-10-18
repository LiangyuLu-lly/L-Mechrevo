// 2026-10-18（宝龙达 · 风扇曲线 / 强冷「不可用」）：⭐⭐ **风扇协议探针** `--fan-probe` ⭐⭐
//
// ⭐⭐ 要钉死的三件事（机主反馈「风扇曲线不可用」「风扇强冷不可用」）：
//    ① 功能号 20（`MaxFanSpeedSwitch`）到底是**按通道手动接管使能**、还是"最大转速/强冷"开关？
//    ② 功能号 21（`MaxFanSpeed`）的单位是不是 x100 RPM？写入是否**必须**先 20=1 才生效？
//    ③ 正确顺序：**先 20=1 再 21**，还是 **先 21 再 20=1**？
//
// ⭐ 做法（⭐ 只读基线 + **单次写** + **每步回读**）：
//    0. 只读：GET 13（两路 RPM）· GET 20（无参 / [4]=0 / [4]=1）· GET 21（[4]=0/1）⇒ 打印**完整 OutData 十六进制**
//    a. 写 20=0（两通道）⇒ 回读 20、13
//    b. 写 21 两通道 = 当前档位（同值）⇒ 回读 13
//    c. 写 21 两通道 = 档位 +5（⛔ 不超过本机满转）⇒ 回读 13
//    d. 写 20=1（两通道）⇒ 回读 20、13（看是否直接"满转"）
//    d2.（仅当 d 既没满转、也没落到 21 目标时）再写一次 21 ⇒ 看 21 是否"必须在 20=1 之后写"
//    e. **恢复**：20=0（两通道）→ 回读确认 → 21=0（两通道）→ 回读 13/20 确认
//
// ⚠️⚠️ 安全边界（硬性）：
//    · ⛔ 目标上限 ≤ **本机满转**（30 系 4900 / 40 系 6000 RPM，按板号判定）；
//    · ⛔ 恢复顺序 = **先关 20 再清 21**（⭐ 顺序反过来：手动仍开时写 21=0 会真的把风扇停掉 ⚠️）；
//    · ⛔ 全程 `try/finally`：即使中途异常 / 提前返回也一定执行恢复，**绝不把风扇留在满转**；
//    · ⛔ 本探针**不打包、不碰安装目录、不 kill LM**。
//
// ⭐ 输出：
//    · 桌面 `lm-fan-probe-<时间戳>.txt`（主报告，⭐ 每步一行：写入 / 回读 / 结论）；
//    · `%APPDATA%\MechrevoLite\log.txt`（⭐ 每行前缀 `[fan-probe]`，与 `--kbd-probe` / `--gpu-oc-probe` 同款）。
//
// ⭐ 用法：`L-Mechrevo.exe --fan-probe`（⭐ WinExe 无控制台 ⇒ 看桌面文件 / log.txt）。
// ⚠️ 非宝龙达机型（⭐ 探不到 `root\WMI:MICommonInterface`）⇒ ⭐ 只写"没有接口"，**一个字节都不写** ✅。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ 宝龙达风扇协议探针（<c>--fan-probe</c>）：⭐ 只读基线 + 单次写 + 每步回读 + <c>try/finally</c> 恢复 ✅。
/// </summary>
internal static class FanProbe
{
	internal static string Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-fan-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-fan-probe 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			return "ERR 桌面文件写不了: " + ex.Message;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[fan-probe] " + s); } catch { }
		}

		BaolongdaWmi wmi = null;
		bool restoreNeeded = false;
		bool restoreDone = false;
		try
		{
			L("=== 宝龙达风扇协议探针（--fan-probe）===");
			L("协议背景：13=读转速（RPM，小端 [4..5]/[6..7]）· 20=按通道手动接管开关（写 [4]=fanType,[5]=0/1）"
				+ " · 21=按通道目标（写 [4]=fanType,[5]=x100RPM）");
			L("⚠️ 安全：目标 ≤ 本机满转 · 每步后回读 · try/finally 恢复 20=0 + 21=0（⛔ 不会把风扇留在满转）");
			L("");

			string board = ReadBoardProduct();
			// ⭐ 满转按板号：裸 `MRID6` = 30 系 4900 RPM（机主新基准）；`MRID6-*`（含 MRID6-23）= 40 系 6000 RPM ✅
			int fullRpm = string.Equals(board, "MRID6", StringComparison.OrdinalIgnoreCase)
				? BaolongdaFanCurveSettings.FullRpm30Series
				: BaolongdaFanCurveSettings.FullRpm40Series;
			int fullUnits = fullRpm / 100;

			wmi = BaolongdaWmi.TryCreate();
			if (wmi == null)
			{
				L("探针结果：⚠️ **没有 MICommonInterface 实例**（`root\\WMI:MICommonInterface` / `ACPI\\PNP0C14\\MIFS*` 都探不到）");
				L("⇒ 本机不是宝龙达机型（⭐ 例：开发机 GM5HG0A 是同方模具）⇒ ⛔ 探针**一个写命令都没发** ✅");
				L("⇒ ⭐ 请在宝龙达机型（蛟龙16Pro 30/40 系）上运行 `L-Mechrevo.exe --fan-probe` 才能得到每步回读结果。");
				return path;
			}
			L("实例 = " + wmi.InstancePath + " · 类GUID = " + (wmi.ClassGuid.Length == 0 ? "(无)" : wmi.ClassGuid));
			L("主板 = " + (board.Length == 0 ? "(空/读不到)" : board)
				+ " ⇒ 本机满转按 **" + fullRpm + " RPM** 处理（协议上限 " + fullUnits + " x100RPM）");
			L("");

			// ────────────────────────────────────────────────────────────────
			// 0. 只读基线：⭐ 把原始 OutData 全打出来（⭐ 20/21 的响应布局靠它钉死）
			// ────────────────────────────────────────────────────────────────
			L("── 0. 只读基线（⛔ 不写任何字节）──");
			if (wmi.TryInvokeRawGet(BaolongdaWmi.Function.CpuGpuFanSpeed, null, out byte[] raw13))
			{
				L("GET 13 ⇒ " + Hex(raw13));
			}
			else
			{
				L("GET 13 ⇒ ⚠️ 读失败");
			}
			bool baselineRpmOk = ReadRpm(wmi, out int baseFirst, out int baseSecond);
			L("13 转速 = " + (baselineRpmOk ? (baseFirst + " / " + baseSecond + " RPM") : "⚠️ 读不到"));
			if (wmi.TryInvokeRawGet(BaolongdaWmi.Function.MaxFanSpeedSwitch, null, out byte[] raw20Plain))
			{
				L("GET 20（无 payload，⭐ 旧代码就是这么读的）⇒ " + Hex(raw20Plain));
			}
			else
			{
				L("GET 20（无 payload）⇒ ⚠️ 读失败");
			}
			if (wmi.TryInvokeRawGet(BaolongdaWmi.Function.MaxFanSpeedSwitch, new byte[1], out byte[] raw20Fan0))
			{
				L("GET 20（[4]=0 ⇒ 通道0）⇒ " + Hex(raw20Fan0));
			}
			else
			{
				L("GET 20（[4]=0）⇒ ⚠️ 读失败");
			}
			if (wmi.TryInvokeRawGet(BaolongdaWmi.Function.MaxFanSpeedSwitch, new byte[1] { 1 }, out byte[] raw20Fan1))
			{
				L("GET 20（[4]=1 ⇒ 通道1）⇒ " + Hex(raw20Fan1));
			}
			else
			{
				L("GET 20（[4]=1）⇒ ⚠️ 读失败");
			}
			bool baselineSwitchOk = wmi.TryReadFanManualSwitches(out bool baseCh0On, out bool baseCh1On);
			L("20 手动接管 = " + (baselineSwitchOk
				? (baseCh0On ? "1" : "0") + "/" + (baseCh1On ? "1" : "0") + "（按 [4]=fanType ⇒ OutData[5] 解析）"
				: "⚠️ 读不到"));
			bool speed0Ok = wmi.TryReadMaxFanSpeed(0, out byte baseUnits0);
			bool speed1Ok = wmi.TryReadMaxFanSpeed(1, out byte baseUnits1);
			L("21 手动目标 = " + ((speed0Ok || speed1Ok)
				? (speed0Ok ? (baseUnits0 + "（" + (baseUnits0 * 100) + " RPM）") : "读不到") + " / "
					+ (speed1Ok ? (baseUnits1 + "（" + (baseUnits1 * 100) + " RPM）") : "读不到") + "（x100RPM）"
				: "⚠️ 读不到"));
			L("");

			// 基准档位 = 两通道平均（四舍五入到 x100RPM）；⭐ 加档 = +5 档（最多到满转）
			int currentUnits;
			if (baselineRpmOk)
			{
				currentUnits = (int)Math.Round((Math.Max(baseFirst, 0) + Math.Max(baseSecond, 0)) / 200.0);
			}
			else
			{
				currentUnits = Math.Min(30, fullUnits); // ⭐ 读不到转速 ⇒ 用 3000 RPM 作为保守基准
			}
			currentUnits = Math.Clamp(currentUnits, 0, Math.Max(0, fullUnits - 5));
			int stepUnits = Math.Min(currentUnits + 5, fullUnits);
			L("基准档位 = " + currentUnits + "（" + (currentUnits * 100) + " RPM）· 测试加档 = "
				+ stepUnits + "（" + (stepUnits * 100) + " RPM）· 满转 = " + fullRpm + " RPM");
			L("");

			// ⭐⭐ 从这里开始会写 ⇒ 先置恢复标志（finally 一定执行恢复）✅
			restoreNeeded = true;

			// ────────────────────────────────────────────────────────────────
			// a. 写 20=0（两通道）
			// ────────────────────────────────────────────────────────────────
			L("── a. 写 20=0（关两通道手动接管）──");
			string wa0 = WriteSwitch(wmi, 0, false);
			string wa1 = WriteSwitch(wmi, 1, false);
			Thread.Sleep(400);
			bool aReadOk = wmi.TryReadFanManualSwitches(out bool aCh0, out bool aCh1);
			bool aRpmOk = ReadRpm(wmi, out int aFirst, out int aSecond);
			L("写 20=(0,0)/(1,0) ⇒ 通道0 " + wa0 + " · 通道1 " + wa1
				+ " ⇒ 回读 20=" + (aReadOk ? (aCh0 ? "1" : "0") + "/" + (aCh1 ? "1" : "0") : "读不到")
				+ " · 13=" + (aRpmOk ? aFirst + "/" + aSecond : "读不到"));
			L("结论：20=0 时两通道回到固件自动控制（⭐ 转速随负载/曲线走，而不是停在我们写的值上）");
			L("");

			// ────────────────────────────────────────────────────────────────
			// b. 写 21 = 当前档位（两通道同值）
			// ────────────────────────────────────────────────────────────────
			L("── b. 写 21 两通道 = 当前档位 " + currentUnits + "（" + (currentUnits * 100) + " RPM）──");
			string wb0 = WriteSpeed(wmi, 0, currentUnits);
			string wb1 = WriteSpeed(wmi, 1, currentUnits);
			Thread.Sleep(1500);
			bool bRpmOk = ReadRpm(wmi, out int bFirst, out int bSecond);
			L("写 21=(0," + currentUnits + ")/(1," + currentUnits + ") ⇒ 通道0 " + wb0 + " · 通道1 " + wb1
				+ " ⇒ 13=" + (bRpmOk ? bFirst + "/" + bSecond : "读不到"));
			L("结论：20=0 时写 21 —— 若 13 基本不变 ⇒ 21 被固件忽略（⭐ 说明必须先开 20）");
			L("");

			// ────────────────────────────────────────────────────────────────
			// c. 写 21 = 档位 +5
			// ────────────────────────────────────────────────────────────────
			L("── c. 写 21 两通道 = 档位+5 = " + stepUnits + "（" + (stepUnits * 100) + " RPM）──");
			string wc0 = WriteSpeed(wmi, 0, stepUnits);
			string wc1 = WriteSpeed(wmi, 1, stepUnits);
			Thread.Sleep(2500);
			bool cRpmOk = ReadRpm(wmi, out int cFirst, out int cSecond);
			L("写 21=(0," + stepUnits + ")/(1," + stepUnits + ") ⇒ 通道0 " + wc0 + " · 通道1 " + wc1
				+ " ⇒ 13=" + (cRpmOk ? cFirst + "/" + cSecond : "读不到"));
			int rpmBeforeEnable = (aRpmOk && bRpmOk) ? Math.Max(bFirst, bSecond) : (aRpmOk ? Math.Max(aFirst, aSecond) : 0);
			int rpmAfterStepWrite = (cRpmOk) ? Math.Max(cFirst, cSecond) : 0;
			bool standalone21Works = rpmAfterStepWrite >= rpmBeforeEnable + 250;
			L("结论：21 在 20=0 时" + (standalone21Works
				? "**能**改转速 ⇒ 21 可独立生效（⭐ 20 不是「手动使能」而是独立开关）"
				: "**不能**改转速（13 没涨 ≥250 RPM）⇒ 21 需要先 20=1（⭐ 符合参考实现）"));
			L("");

			// ────────────────────────────────────────────────────────────────
			// d. 写 20=1（两通道）⇒ 看是否"满转"
			// ────────────────────────────────────────────────────────────────
			L("── d. 写 20=1（开两通道手动接管）→ 观察 13 是否出现「满转」──");
			string wd0 = WriteSwitch(wmi, 0, true);
			string wd1 = WriteSwitch(wmi, 1, true);
			Thread.Sleep(300);
			bool dReadOk = wmi.TryReadFanManualSwitches(out bool dCh0, out bool dCh1);
			int dFirst = -1;
			int dSecond = -1;
			string dSamples = SampleRpm(wmi, 5, 700, ref dFirst, ref dSecond);
			int rpmAfterEnable = Math.Max(dFirst, dSecond);
			L("写 20=(0,1)/(1,1) ⇒ 通道0 " + wd0 + " · 通道1 " + wd1
				+ " ⇒ 回读 20=" + (dReadOk ? (dCh0 ? "1" : "0") + "/" + (dCh1 ? "1" : "0") : "读不到")
				+ " · 13 采样=" + dSamples);
			bool forcesMax = rpmAfterEnable >= fullRpm * 9 / 10;
			bool appliesPending21 = Math.Abs(rpmAfterEnable - (stepUnits * 100)) <= 700;
			string dVerdict;
			if (forcesMax)
			{
				dVerdict = "20=1 = **强制满转**（" + rpmAfterEnable + " RPM ≥ 满转的 90%）⇒ 20 是「最大转速/强冷」开关";
			}
			else if (appliesPending21)
			{
				dVerdict = "20=1 = **手动接管使能**，并立即执行之前写的 21=" + stepUnits
					+ "（13=" + rpmAfterEnable + " RPM ≈ 目标 " + (stepUnits * 100) + "）";
			}
			else if (standalone21Works)
			{
				dVerdict = "20=1 后 13=" + rpmAfterEnable + " RPM（既非满转、也非 21 目标）⇒ 20 与 21 的关系仍不明确，见 d2";
			}
			else
			{
				dVerdict = "写 20=1 后 13 没有明显变化（" + rpmAfterEnable + " RPM）⇒ 21 可能**必须后写**，见 d2";
			}
			L("结论：" + dVerdict);
			L("");

			// ────────────────────────────────────────────────────────────────
			// d2. 只有在 d 没出现"满转 / 目标生效"时才做：20=1 后再写 21
			// ────────────────────────────────────────────────────────────────
			bool d2Applied = false;
			bool needD2 = !forcesMax && !appliesPending21;
			if (needD2)
			{
				L("── d2. 20=1 之后**再写一次** 21=" + stepUnits + "（验证「先 20=1 再 21」的顺序）──");
				string we0 = WriteSpeed(wmi, 0, stepUnits);
				string we1 = WriteSpeed(wmi, 1, stepUnits);
				int eFirst = -1;
				int eSecond = -1;
				string eSamples = SampleRpm(wmi, 5, 700, ref eFirst, ref eSecond);
				int rpmAfterLate21 = Math.Max(eFirst, eSecond);
				d2Applied = Math.Abs(rpmAfterLate21 - (stepUnits * 100)) <= 700 || rpmAfterLate21 >= fullRpm * 9 / 10;
				L("写 21=(0," + stepUnits + ")/(1," + stepUnits + ")（20 已开）⇒ 通道0 " + we0 + " · 通道1 " + we1
					+ " ⇒ 13 采样=" + eSamples + " ⇒ 生效=" + d2Applied);
				L("结论：" + (d2Applied
					? "顺序必须是 **先 20=1 → 再 21**（⭐ 与参考实现 BellatorFanControl 一致）"
					: "⚠️ 先 20=1 再 21 也没看到变化 ⇒ 需要用原始字节进一步判断（也许 20 的第二个参数不是 0/1 / 通道号不同）"));
				L("");
			}

			// ────────────────────────────────────────────────────────────────
			// e. 恢复
			// ────────────────────────────────────────────────────────────────
			L("── e. 恢复（20=0 两通道 → 回读 → 21=0 两通道 → 回到固件控制）──");
			restoreDone = RestoreForProbe(wmi, L);
			restoreNeeded = !restoreDone;
			L("恢复确认 = " + restoreDone + (restoreDone ? " ✅" : " ⚠️（见上面回读值）"));
			L("");

			// ────────────────────────────────────────────────────────────────
			// 总结论（⭐ 只按实测写，⛔ 不硬套参考实现）
			// ────────────────────────────────────────────────────────────────
			L("══ 总结论（⭐ 本次实测）══");
			L("① 20 语义 = " + (forcesMax
				? "**强制满转/最大转速开关**（写 [fanType,1] 后 13 直接接近满转）"
				: (appliesPending21 || d2Applied)
					? "**按通道手动接管使能**（写 [fanType,1] 后先前的 21 目标开始生效）"
					: "⚠️ 未明确（见各步原始回读）"));
			L("② 21 单位 = " + (appliesPending21 || d2Applied || standalone21Works
				? "**x100 RPM**（值 ×100 = RPM；⭐ 本次目标 " + stepUnits + " ⇒ " + (stepUnits * 100) + " RPM 与 13 相符/接近）"
				: "⚠️ 未能从 13 反推（见原始回读）"));
			L("③ 正确顺序 = " + (forcesMax
				? "强冷可**只写 20=1**；曲线仍需 20=1 + 21（⭐ 若 20=1 会满转，则曲线路径必须 20=0 + 21，⭐ 见下一条）"
				: d2Applied
					? "**先 20=1（按通道）→ 再 21（按通道）**"
					: "⚠️ 未确认"));
			L("④ 曲线建议 = " + (forcesMax && !d2Applied
				? "写 20=0 + 21（⭐ 20=1 会强制满转，不能用于曲线）"
				: "写 20=1 + 21（⭐ 20=1 是手动使能，21 是目标）"));
			L("");
			L("⚠️ 若本报告与参考实现（三份开源：JiaolongControl / BellatorFanControl / DOUZHANZHE-Control）不一致，"
				+ "⭐ **以本报告的真机回读为准**，并把报告发回 ✅");
		}
		catch (Exception ex)
		{
			L("探针异常：" + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		finally
		{
			if (wmi != null)
			{
				if (restoreNeeded && !restoreDone)
				{
					// ⭐⭐ 硬性要求：异常/提前返回也一定恢复（⛔ 绝不把风扇留在满转）⚠️
					try
					{
						RestoreForProbe(wmi, L);
					}
					catch (Exception ex)
					{
						L("⚠️ finally 恢复失败（⭐ 请手工运行 `--fan-probe` 再确认一次 / 或重启机器交回固件）：" + ex.Message);
					}
				}
				try
				{
					wmi.Dispose();
				}
				catch
				{
				}
			}
		}
		return path;
	}

	/// <summary>
	/// ⭐ 恢复：⭐ ① 先关两通道手动接管（20=[fanType,0]）→ ② 回读确认 → ③ 确认关掉后才把 21 归零 →
	/// ④ 再回读 13/20 确认 ✅。
	/// <para>⚠️ 顺序**不可反**：手动仍开时写 21=0 会真的把风扇停掉 ⚠️</para>
	/// </summary>
	private static bool RestoreForProbe(BaolongdaWmi wmi, Action<string> log)
	{
		bool off0 = false;
		bool off1 = false;
		try
		{
			off0 = wmi.TrySetFanManualSwitch(0, false);
			off1 = wmi.TrySetFanManualSwitch(1, false);
		}
		catch
		{
		}
		Thread.Sleep(300);
		bool readOk = false;
		bool ch0On = true;
		bool ch1On = true;
		try
		{
			readOk = wmi.TryReadFanManualSwitches(out ch0On, out ch1On);
		}
		catch
		{
		}
		bool zero0 = false;
		bool zero1 = false;
		bool confirmed = readOk && !ch0On && !ch1On;
		if (confirmed)
		{
			try
			{
				zero0 = wmi.TrySetMaxFanSpeed(0, 0);
				zero1 = wmi.TrySetMaxFanSpeed(1, 0);
			}
			catch
			{
			}
			Thread.Sleep(200);
		}
		bool rpmOk = ReadRpm(wmi, out int first, out int second);
		log("恢复：写 20=(0,0)/(1,0) ⇒ ok=" + (off0 && off1)
			+ " ⇒ 回读 20=" + (readOk ? (ch0On ? "1" : "0") + "/" + (ch1On ? "1" : "0") : "读不到")
			+ " ⇒ " + (confirmed
				? "写 21=(0,0)/(1,0) ⇒ ok=" + (zero0 && zero1)
				: "⚠️ 未确认关掉 ⇒ **不写** 21（避免误停扇）")
			+ " · 回读 13=" + (rpmOk ? first + "/" + second : "读不到"));
		return confirmed;
	}

	private static bool ReadRpm(BaolongdaWmi wmi, out int firstChannelRpm, out int secondChannelRpm)
	{
		firstChannelRpm = -1;
		secondChannelRpm = -1;
		try
		{
			if (wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds))
			{
				firstChannelRpm = speeds.First;
				secondChannelRpm = speeds.Second;
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	/// <summary>⭐ 连续采样若干次 13（⭐ 风扇有机械加速延迟 ⇒ 单次读容易误判）✅。</summary>
	private static string SampleRpm(BaolongdaWmi wmi, int samples, int delayMs, ref int bestFirst, ref int bestSecond)
	{
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < samples; i++)
		{
			Thread.Sleep(delayMs);
			if (ReadRpm(wmi, out int first, out int second))
			{
				if (first > bestFirst)
				{
					bestFirst = first;
				}
				if (second > bestSecond)
				{
					bestSecond = second;
				}
				if (sb.Length > 0)
				{
					sb.Append(" → ");
				}
				sb.Append(first).Append('/').Append(second);
			}
		}
		return (sb.Length == 0) ? "读不到" : sb.ToString();
	}

	/// <summary>⭐ 写一个通道的手动接管开关（功能号 20 = <c>[fanType, 0/1]</c>），返回写入结果 + OutData 回显 ✅。</summary>
	private static string WriteSwitch(BaolongdaWmi wmi, int fanType, bool on)
	{
		if (wmi.TryInvokeRawSet(BaolongdaWmi.Function.MaxFanSpeedSwitch,
			new byte[2] { (byte)fanType, on ? (byte)1 : (byte)0 }, out byte[] response))
		{
			return "写成功（OutData=" + Hex(response) + "）";
		}
		return "⚠️ 写失败";
	}

	/// <summary>⭐ 写一个通道的手动目标转速（功能号 21 = <c>[fanType, x100RPM]</c>），返回写入结果 + OutData 回显 ✅。</summary>
	private static string WriteSpeed(BaolongdaWmi wmi, int fanType, int units100Rpm)
	{
		if (wmi.TryInvokeRawSet(BaolongdaWmi.Function.MaxFanSpeed,
			new byte[2] { (byte)fanType, (byte)units100Rpm }, out byte[] response))
		{
			return "写成功（OutData=" + Hex(response) + "）";
		}
		return "⚠️ 写失败";
	}

	private static string Hex(byte[] data)
	{
		if (data == null)
		{
			return "null";
		}
		StringBuilder sb = new StringBuilder(data.Length * 3);
		for (int i = 0; i < data.Length; i++)
		{
			if (i > 0)
			{
				sb.Append(' ');
			}
			sb.Append(data[i].ToString("X2"));
		}
		return sb.ToString();
	}

	/// <summary>⭐ 只读主板型号（⭐ 只为了算"本机满转"：裸 `MRID6` = 30 系 4900 / `MRID6-*` = 40 系 6000）✅。</summary>
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
