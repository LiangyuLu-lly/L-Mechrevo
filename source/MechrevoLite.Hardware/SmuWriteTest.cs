// ⭐⭐ 2026-10-19（30 系 · SMU **保守写 + 回读** 实测工具）：
//
// 用途：在没有 30 系真机时，用本机（AMD 8845H / HawkPoint，与 30 系 6800H 同属 APU 命令组）
//       验证我们新接的 SMU 写通路到底能不能写、写完能不能读回：
//         * PL1 → slow limit（`SetSlowLimit`，Renoir/APU 组 MP1 0x16）
//         * PL2 → fast limit（`SetFastLimit`，MP1 0x15）
//         * 温度墙 → tctl（`SetTctlTemp`，MP1 0x19）
//         * 功耗时间窗 → stapm_time（`SetStapmTimeSeconds`，MP1 0x18）
//
// ⚠️⚠️ **边界（机主/Lead 明确要求，代码里写死，不给参数覆盖）**：
//   1. **先读后写**：写之前必须能读到 PM 表快照（PL1/PL2/SPL/tctl/stapm_time），读不到 ⇒ 一条写命令都不发；
//   2. **只动一项、改完立刻回读**；
//   3. 温度墙**只往 ≥85℃ 方向**改（例：当前 95 ⇒ 试 90，立刻回读，再还原）；
//   4. slow/fast（PL1/PL2）**只在当前值往下 5W 内**动，⛔ 绝不加高；
//   5. stapm_time **只写回当前值**（验证写入通路，⛔ 不改大/改小）；
//   6. `finally` 里**必须**把 PL1/PL2/SPL/tctl/stapm_time **全部写回快照**，然后回读确认（容差 ±1）；
//      恢复失败 ⇒ 立刻再试一次，并在报告里用 ERROR 级别写明（这比测试结果重要）。
//
// ⛔ 不带 `--yes` 时**只读**（只打印快照，不写任何东西）。
//
// 输出：桌面 `lm-smu-write-test-<时间戳>.txt` + `%APPDATA%\MechrevoLite\log.txt`（前缀 `[smu-write-test]`）。
// 用法：`L-Mechrevo.exe --smu-write-test [--yes]`（需要管理员；主程序清单本身就是 requireAdministrator）
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using MechrevoLite.Helpers;
using PawnIO;

namespace MechrevoLite.Hardware;

internal static class SmuWriteTest
{
	internal static int Run(string[] args)
	{
		bool write = args != null && args.Any((string a) => string.Equals(a, "--yes", StringComparison.OrdinalIgnoreCase));
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-smu-write-test-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		int exitCode = 2;
		try
		{
			File.WriteAllText(path, "lm-smu-write-test 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · write=" + write + "\r\n", Encoding.UTF8);
		}
		catch (Exception)
		{
			return 2;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[smu-write-test] " + s); } catch { }
		}
		void E(string s)
		{
			try { File.AppendAllText(path, "ERROR " + s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteError("[smu-write-test] ERROR " + s); } catch { }
		}
		try
		{
			L("边界: 先读后写 · 只动一项并立刻回读 · 温度墙只往 >=85℃ 方向 · PL 只往下 5W 内 · stapm_time 只写回当前值 · finally 必须恢复快照并回读确认");
			L("管理员: " + (ProcessHelper.IsUserAdministrator() ? "是" : "否（PawnIO 写会 AccessDenied）"));
			RyzenSmuService smu = new RyzenSmuService();
			using (smu)
			{
				bool init = smu.Initialize(Assembly.GetExecutingAssembly());
				if (!init)
				{
					E("SMU 初始化失败（PawnIO/RyzenSMU/代号/SMU 版本任一步没过）⇒ 不做任何写入");
					L("结论: 环境不可用，未写入任何东西。报告: " + path);
					return 2;
				}
				string codeName = smu.CpuCodeName.ToString();
				L($"SMU: 代号={codeName} · 家族={smu.Family} · SMU 版本=0x{smu.SmuVersion:X}");
				if (!RyzenSmuService.LegacyFamilyCommandsApproved(smu.CpuCodeName))
				{
					E("本代号不在族驱动命令白名单（LegacyFamilyCommandsApproved）⇒ 一条写命令都不发");
					L("结论: 未写入任何东西。报告: " + path);
					return 2;
				}
				SmuPowerSnapshot before = smu.GetPowerSnapshot();
				if (before == null)
				{
					E("PM 表快照读不到 ⇒ **写前必须先能读**，本次一条写命令都不发");
					L("结论: 未写入任何东西。报告: " + path);
					return 2;
				}
				int pl1 = R(before.Slow), pl2 = R(before.Fast), spl = R(before.Stapm), tctl = R(before.TctlTemp);
				int stapmTime = before.StapmTimeSeconds ?? 0;
				L($"快照（写前）: SPL={spl}W · sPPT/PL1={pl1}W · fPPT/PL2={pl2}W · tctl={tctl}℃ · "
					+ $"stapm_time={(stapmTime > 0 ? stapmTime + "s" : "读不到")} · slow_time={(before.SlowTimeSeconds.HasValue ? before.SlowTimeSeconds.Value + "s" : "读不到")} "
					+ $"(PM 表 0x{before.TableVersion:X6})");
				// 时间窗稳定性预检（只读）：stapm_time 必须连续两次读数在 ±1 内，才允许"写回当前值"测试。
				// ⚠️ 本机教训（Phoenix / 0x4C0009）：该字段会 25~67 来回跳 ⇒ 不稳定的字段**不能**用来验证写入。
				int stapmTime2 = 0;
				bool stapmTimeStable = false;
				try
				{
					Thread.Sleep(300);
					SmuPowerSnapshot second = smu.GetPowerSnapshot();
					stapmTime2 = second != null ? (second.StapmTimeSeconds ?? 0) : 0;
					stapmTimeStable = stapmTime > 0 && Math.Abs(stapmTime2 - stapmTime) <= 1;
				}
				catch (Exception exPre)
				{
					L("stapm_time 稳定性预检异常: " + exPre.GetType().Name + " " + exPre.Message);
				}
				if (stapmTime > 0)
				{
					L($"stapm_time 稳定性预检: {stapmTime}s → {stapmTime2}s ⇒ 稳定={stapmTimeStable}");
				}
				if (!write)
				{
					L("未带 --yes ⇒ **只读模式**：到此结束，未写任何东西。");
					L("报告: " + path);
					return 0;
				}
				// ---- 以下开始写；所有写入都包在 try/finally 里（finally 一定恢复快照）----
				int pass = 0;
				int fail = 0;
				bool skipped = false;
				bool wroteTimeTest = false;
				try
				{
					// ① 温度墙 tctl：只往 ≥85℃ 方向改（默认往下 5℃，但不低于 85）
					int tctlTarget = Math.Max(85, tctl - 5);
					if (tctlTarget == tctl)
					{
						L($"① 温度墙: 当前 {tctl}℃ ⇒ 目标 {tctlTarget}℃ 与当前相同 ⇒ 跳过（不写）");
						skipped = true;
					}
					else
					{
						L($"① 温度墙: 当前 {tctl}℃ ⇒ 写 {tctlTarget}℃（方向 ≥85℃；不升高）");
						SmuStatus st = smu.SetTctlTemp(tctlTarget);
						SmuPowerSnapshot rb = smu.GetPowerSnapshot();
						bool ok = rb != null && Math.Abs(R(rb.TctlTemp) - tctlTarget) <= 1;
						L($"   写入结果={st}(0x{(uint)st:X2}) · 回读={(rb == null ? "读不到" : R(rb.TctlTemp) + "℃")} ⇒ 回读一致={ok}");
						if (st == SmuStatus.OK && ok) { pass++; } else { fail++; }
						RestoreItem(smu, "温度墙", () => smu.SetTctlTemp(tctl), tctl, L);
					}
					// ② PL1 → slow limit：只在当前值往下 5W 内
					int pl1Target = Math.Max(5, pl1 - 5);
					if (pl1Target == pl1)
					{
						L($"② PL1(slow): 当前 {pl1}W ⇒ 往下 5W 会低于下限 5W ⇒ 跳过（不写）");
					}
					else
					{
						L($"② PL1(slow): 当前 {pl1}W ⇒ 写 {pl1Target}W（只降不升，幅度 5W）");
						SmuStatus st = smu.SetSlowLimit(pl1Target);
						SmuPowerSnapshot rb = smu.GetPowerSnapshot();
						bool ok = rb != null && Math.Abs(R(rb.Slow) - pl1Target) <= 1;
						L($"   写入结果={st}(0x{(uint)st:X2}) · 回读={(rb == null ? "读不到" : R(rb.Slow) + "W")} ⇒ 回读一致={ok}");
						if (st == SmuStatus.OK && ok) { pass++; } else { fail++; }
						RestoreItem(smu, "PL1(slow)", () => smu.SetSlowLimit(pl1), pl1, L);
					}
					// ③ PL2 → fast limit：只在当前值往下 5W 内
					int pl2Target = Math.Max(5, pl2 - 5);
					if (pl2Target == pl2)
					{
						L($"③ PL2(fast): 当前 {pl2}W ⇒ 往下 5W 会低于下限 5W ⇒ 跳过（不写）");
					}
					else
					{
						L($"③ PL2(fast): 当前 {pl2}W ⇒ 写 {pl2Target}W（只降不升，幅度 5W）");
						SmuStatus st = smu.SetFastLimit(pl2Target);
						SmuPowerSnapshot rb = smu.GetPowerSnapshot();
						bool ok = rb != null && Math.Abs(R(rb.Fast) - pl2Target) <= 1;
						L($"   写入结果={st}(0x{(uint)st:X2}) · 回读={(rb == null ? "读不到" : R(rb.Fast) + "W")} ⇒ 回读一致={ok}");
						if (st == SmuStatus.OK && ok) { pass++; } else { fail++; }
						RestoreItem(smu, "PL2(fast)", () => smu.SetFastLimit(pl2), pl2, L);
					}
					// ④ STAPM 时间窗：只写回当前值（验证 0x18 通路；⛔ 不改大/改小）
					if (stapmTime <= 0)
					{
						L("④ stapm_time: 当前值读不到 ⇒ 跳过（不猜值去写）");
						skipped = true;
					}
					else if (!stapmTimeStable)
					{
						// ⚠️ 字段读数不稳定 ⇒ 不能"写回当前值"（那个当前值本身不可信），也不能拿它做回读验证
						L($"④ stapm_time: 读数不稳定（{stapmTime}s → {stapmTime2}s）⇒ **跳过写测试**（⛔ 不拿不可信的当前值去写）");
						skipped = true;
					}
					else
					{
						L($"④ stapm_time: 写回当前值 {stapmTime}s（只验证通路，⛔ 不改值）");
						SmuStatus st = smu.SetStapmTimeSeconds(stapmTime);
						wroteTimeTest = true;
						SmuPowerSnapshot rb = smu.GetPowerSnapshot();
						bool ok = rb != null && rb.StapmTimeSeconds.HasValue && Math.Abs(rb.StapmTimeSeconds.Value - stapmTime) <= 1;
						L($"   写入结果={st}(0x{(uint)st:X2}) · 回读={(rb == null || !rb.StapmTimeSeconds.HasValue ? "读不到" : rb.StapmTimeSeconds.Value + "s")} ⇒ 回读一致={ok}");
						if (st == SmuStatus.OK && ok) { pass++; } else { fail++; }
					}
					exitCode = (fail == 0) ? 0 : 1;
				}
				finally
				{
					// ---- 硬性要求：恢复快照（PL1/PL2/SPL/tctl，以及**本次真的写过**的 stapm_time）+ 回读确认（容差 ±1）----
					L("恢复: 把 PL1/PL2/SPL/tctl" + (wroteTimeTest ? "/stapm_time" : "") + " 写回写前快照 …");
					if (!wroteTimeTest && stapmTime > 0)
					{
						L($"   说明: stapm_time 本次**未写入**（读数不稳定 / 读不到）⇒ 不写它、也不拿它做恢复校验（{stapmTime}s → {stapmTime2}s）");
					}
					bool restored = TryRestoreAll(smu, spl, pl1, pl2, tctl, wroteTimeTest ? stapmTime : 0, L);
					SmuPowerSnapshot after = smu.GetPowerSnapshot();
					bool verified = VerifyRestored(after, spl, pl1, pl2, tctl, wroteTimeTest, wroteTimeTest ? stapmTime : 0);
					L($"恢复回读: {(after == null ? "读不到" : $"SPL={R(after.Stapm)}W · sPPT={R(after.Slow)}W · fPPT={R(after.Fast)}W · tctl={R(after.TctlTemp)}℃ · stapm_time={(after.StapmTimeSeconds.HasValue ? after.StapmTimeSeconds.Value + "s" : "读不到")}")} ⇒ 与快照一致={verified}");
					if (!restored || !verified)
					{
						// 立即再试一次（Lead 要求）
						L("⚠️ 恢复第一次未确认 ⇒ 立刻再试一次 …");
						restored = TryRestoreAll(smu, spl, pl1, pl2, tctl, wroteTimeTest ? stapmTime : 0, L);
						SmuPowerSnapshot after2 = smu.GetPowerSnapshot();
						verified = VerifyRestored(after2, spl, pl1, pl2, tctl, wroteTimeTest, wroteTimeTest ? stapmTime : 0);
						L($"恢复重试回读: {(after2 == null ? "读不到" : $"SPL={R(after2.Stapm)}W · sPPT={R(after2.Slow)}W · fPPT={R(after2.Fast)}W · tctl={R(after2.TctlTemp)}℃ · stapm_time={(after2.StapmTimeSeconds.HasValue ? after2.StapmTimeSeconds.Value + "s" : "读不到")}")} ⇒ 与快照一致={verified}");
						if (!verified)
						{
							E("**恢复失败：机器可能未回到写前值！** 请立即人工核对/手工恢复。" + path);
							exitCode = 3;
						}
					}
					L($"测试统计: 通过={pass} 失败={fail} 跳过项={(skipped ? "有" : "无")} · 最终恢复确认={verified}");
					L("最终快照: " + (after == null ? "读不到" : $"SPL={R(after.Stapm)}W · sPPT/PL1={R(after.Slow)}W · fPPT/PL2={R(after.Fast)}W · tctl={R(after.TctlTemp)}℃ · stapm_time={(after.StapmTimeSeconds.HasValue ? after.StapmTimeSeconds.Value + "s" : "读不到")}"));
					L("报告: " + path);
				}
				return exitCode;
			}
		}
		catch (Exception ex)
		{
			E("工具异常: " + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
			return 2;
		}
	}

	private static void RestoreItem(RyzenSmuService smu, string name, Func<SmuStatus> restore, int original, Action<string> log)
	{
		try
		{
			SmuStatus st = restore();
			log($"   还原 {name} → {original}: {st}(0x{(uint)st:X2})");
		}
		catch (Exception ex)
		{
			log($"   还原 {name} 异常（最终会整体再恢复一次）: {ex.GetType().Name} {ex.Message}");
		}
	}

	private static bool TryRestoreAll(RyzenSmuService smu, int spl, int pl1, int pl2, int tctl, int stapmTimeToRestore, Action<string> log)
	{
		bool ok = true;
		// 三项功率：一次按"stapm/fast/slow"发（与正常下发路径同序；这里 stapm 用**快照里的 SPL**，
		// 不是 PL1 —— 否则会把 SPL 顺手改成 PL1，那就不是"恢复原状"了）。
		smu.SetAllLimits(spl, pl2, pl1, out SmuStatus sStapm, out SmuStatus sFast, out SmuStatus sSlow);
		log($"   SetAllLimits(SPL={spl}, fast={pl2}, slow={pl1}): stapm={sStapm} fast={sFast} slow={sSlow}");
		ok &= sStapm == SmuStatus.OK && sFast == SmuStatus.OK && sSlow == SmuStatus.OK;
		if (tctl > 0)
		{
			SmuStatus sTctl = smu.SetTctlTemp(tctl);
			log($"   SetTctlTemp({tctl}): {sTctl}");
			ok &= sTctl == SmuStatus.OK;
		}
		// ⚠️ 只有本次**真的写过** stapm_time 才恢复它（读数不稳定的字段不参与：写回一个不可信的值没有意义）
		if (stapmTimeToRestore > 0)
		{
			SmuStatus sTime = smu.SetStapmTimeSeconds(stapmTimeToRestore);
			log($"   SetStapmTimeSeconds({stapmTimeToRestore}): {sTime}");
			ok &= sTime == SmuStatus.OK;
		}
		return ok;
	}

	private static bool VerifyRestored(SmuPowerSnapshot after, int spl, int pl1, int pl2, int tctl, bool checkStapmTime, int stapmTime)
	{
		if (after == null)
		{
			return false;
		}
		bool ok = Math.Abs(R(after.Stapm) - spl) <= 1
			&& Math.Abs(R(after.Slow) - pl1) <= 1
			&& Math.Abs(R(after.Fast) - pl2) <= 1
			&& Math.Abs(R(after.TctlTemp) - tctl) <= 1;
		if (checkStapmTime && stapmTime > 0)
		{
			ok &= after.StapmTimeSeconds.HasValue && Math.Abs(after.StapmTimeSeconds.Value - stapmTime) <= 1;
		}
		return ok;
	}

	private static int R(float value)
	{
		return (int)Math.Round(value, MidpointRounding.AwayFromZero);
	}
}
