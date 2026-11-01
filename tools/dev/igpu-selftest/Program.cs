using System;
using System.Collections.Generic;
using MechrevoLite.Gpu;
using MechrevoLite.Hardware;

/// <summary>
/// 2026-10-13 离线夹具：断言「核显省电」这条路的**决策逻辑**（⭐ 会动硬件的代码，逻辑必须先离线全绿）。
///
/// <para>被测代码 = 产品源码（⭐ 直接 Link 编译，⛔ 不是复制一份逻辑）：
/// <c>source\MechrevoLite.Hardware\IgpuOnlySaveModeDecision.cs</c> ·
/// <c>source\MechrevoLite.Gpu\GpuScenarioGuard.cs</c> ·
/// <c>source\MechrevoLite.Gpu\DgpuApplicationSafety.cs</c>。</para>
///
/// <para>四组判据（对应任务书 ①②③）：
/// ① 确认判据 <c>IgpuOnlySaveMode == 期望值</c>（⛔ 不再看显示路由）；
/// ② 保护名单过滤（安全门 (a)）；
/// ③ 「独显唯一驱动显示 ⇒ 拒绝强制」（安全门 (b)）；
/// ④ 回滚触发条件（安全门 (c)）。</para>
///
/// <para>用法：<c>dotnet run -c Release --project tools\dev\igpu-selftest</c>（退出码 0 = 全绿）。
/// ⚠️ 本夹具**不碰硬件**：不发 EC/GCU 命令、不结束任何进程、不读注册表。</para>
/// </summary>
internal static class Program
{
	private static int _fail;

	private static int _total;

	private static void Check(string name, bool actual, bool expected)
	{
		_total++;
		if (actual != expected)
		{
			_fail++;
		}
		Console.WriteLine($"[{((actual == expected) ? "PASS" : "FAIL")}] {name,-72} actual={actual,-5} expected={expected}");
	}

	private static void CheckEq<T>(string name, T actual, T expected)
	{
		_total++;
		bool ok = EqualityComparer<T>.Default.Equals(actual, expected);
		if (!ok)
		{
			_fail++;
		}
		Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name,-72} actual={actual,-5} expected={expected}");
	}

	private static DisplayAdapterSnapshot Nv(int width)
	{
		return new DisplayAdapterSnapshot("NVIDIA GeForce RTX", "PCI\\VEN_10DE&DEV_0000", width);
	}

	private static DisplayAdapterSnapshot Igpu(int width)
	{
		return new DisplayAdapterSnapshot("AMD Radeon Graphics", "PCI\\VEN_1002&DEV_0000", width);
	}

	private static DisplayRouteSnapshot Route(params DisplayAdapterSnapshot[] adapters)
	{
		return new DisplayRouteSnapshot(adapters);
	}

	private static int Main()
	{
		Console.WriteLine("== ① 确认判据：IgpuOnlySaveMode == 期望值（新谓词，⛔ 不看显示路由）==");
		Check("ON(0) 对 ON(0) ⇒ 达成", IgpuOnlySaveModeDecision.IsTargetReached(0, 0), true);
		Check("OFF(1) 对 OFF(1) ⇒ 达成", IgpuOnlySaveModeDecision.IsTargetReached(1, 1), true);
		Check("AUTO(3) 对 AUTO(3) ⇒ 达成", IgpuOnlySaveModeDecision.IsTargetReached(3, 3), true);
		// ⚠️ 旧实现无条件 return true ⇒ 下面这几条是"这次必须能报失败"的核心。
		Check("粘滞初值 OFF(1) 对 ON(0) ⇒ ⛔ 未达成（旧实现会误报成功）", IgpuOnlySaveModeDecision.IsTargetReached(1, 0), false);
		Check("粘滞初值 OFF(1) 对 AUTO(3) ⇒ ⛔ 未达成", IgpuOnlySaveModeDecision.IsTargetReached(1, 3), false);
		Check("读不到(-1) 对 ON(0) ⇒ ⛔ 未达成（⛔ 不拿初值当确认）", IgpuOnlySaveModeDecision.IsTargetReached(-1, 0), false);
		Check("读不到(-1) 对 读不到(-1) ⇒ ⛔ 未达成", IgpuOnlySaveModeDecision.IsTargetReached(-1, -1), false);
		Check("OFF(1) 对 非法期望(-1) ⇒ ⛔ 未达成", IgpuOnlySaveModeDecision.IsTargetReached(1, -1), false);
		Check("AUTO(3) 对 ON(0) ⇒ 未达成", IgpuOnlySaveModeDecision.IsTargetReached(3, 0), false);

		Console.WriteLine();
		Console.WriteLine("== ①b 模式串 ⇄ 命令 / 期望值 ==");
		Check("TryResolveTargetMode(ON) = true/0", IgpuOnlySaveModeDecision.TryResolveTargetMode("ON", out int onMode) && onMode == 0, true);
		Check("TryResolveTargetMode(OFF) = true/1", IgpuOnlySaveModeDecision.TryResolveTargetMode("OFF", out int offMode) && offMode == 1, true);
		Check("TryResolveTargetMode(AUTO) = true/3", IgpuOnlySaveModeDecision.TryResolveTargetMode("AUTO", out int autoMode) && autoMode == 3, true);
		Check("TryResolveTargetMode(on) 小写 ⇒ ⛔ 拒绝（与既有实现一字不差）", IgpuOnlySaveModeDecision.TryResolveTargetMode("on", out _), false);
		Check("TryResolveTargetMode(空串) ⇒ ⛔ 拒绝", IgpuOnlySaveModeDecision.TryResolveTargetMode("", out _), false);
		Check("TryResolveTargetMode(null) ⇒ ⛔ 拒绝", IgpuOnlySaveModeDecision.TryResolveTargetMode(null, out _), false);
		CheckEq("Action(ON) = IGPU_ONLY_CONNECT_RB_ON", IgpuOnlySaveModeDecision.ResolveActionName("ON"), "IGPU_ONLY_CONNECT_RB_ON");
		CheckEq("Action(AUTO) = IGPU_ONLY_CONNECT_RB_AUTO", IgpuOnlySaveModeDecision.ResolveActionName("AUTO"), "IGPU_ONLY_CONNECT_RB_AUTO");
		CheckEq("Action(非法) = null", IgpuOnlySaveModeDecision.ResolveActionName("NOPE"), null);
		CheckEq("Suffix(3) = AUTO（回滚要用它把整数翻译回命令）", IgpuOnlySaveModeDecision.ActionSuffix(3), "AUTO");
		CheckEq("Suffix(2) = null", IgpuOnlySaveModeDecision.ActionSuffix(2), null);
		Check("补查重发：第 1/3/5 次", IgpuOnlySaveModeDecision.ShouldResendOnAttempt(1) && IgpuOnlySaveModeDecision.ShouldResendOnAttempt(3) && IgpuOnlySaveModeDecision.ShouldResendOnAttempt(5), true);
		Check("补查重发：第 0/2/4 次不发", IgpuOnlySaveModeDecision.ShouldResendOnAttempt(0) || IgpuOnlySaveModeDecision.ShouldResendOnAttempt(2) || IgpuOnlySaveModeDecision.ShouldResendOnAttempt(4), false);

		Console.WriteLine();
		Console.WriteLine("== ③ 独显唯一驱动显示 ⇒ 拒绝强制（安全门 (b)）==");
		DisplayRouteSnapshot dgpuOnly = Route(Nv(3840));
		DisplayRouteSnapshot hybrid = Route(Nv(3840), Igpu(1920));
		DisplayRouteSnapshot igpuOnlyRoute = Route(Igpu(1920));
		CheckEq("独显独家 ⇒ 判定 DgpuExclusive", GpuScenarioGuard.ClassifyRoute(dgpuOnly), DisplayRouteKind.DgpuExclusive);
		CheckEq("独显+核显都在驱动 ⇒ Hybrid", GpuScenarioGuard.ClassifyRoute(hybrid), DisplayRouteKind.Hybrid);
		CheckEq("只有核显在驱动 ⇒ IgpuOnly", GpuScenarioGuard.ClassifyRoute(igpuOnlyRoute), DisplayRouteKind.IgpuOnly);
		CheckEq("没有任何活动适配器 ⇒ Unavailable", GpuScenarioGuard.ClassifyRoute(Route(Nv(0), Igpu(0))), DisplayRouteKind.Unavailable);
		CheckEq("空快照 ⇒ Unavailable", GpuScenarioGuard.ClassifyRoute(Route()), DisplayRouteKind.Unavailable);
		CheckEq("适配器列表为 null ⇒ Unavailable（WMI 读不到）", GpuScenarioGuard.ClassifyRoute(DisplayRouteSnapshot.Unavailable), DisplayRouteKind.Unavailable);
		CheckEq("只有 PNPDeviceID 命中 VEN_10DE 也算独显", GpuScenarioGuard.ClassifyRoute(Route(new DisplayAdapterSnapshot("Generic Display", "PCI\\VEN_10DE&DEV_1C82", 1920))), DisplayRouteKind.DgpuExclusive);

		GpuScenarioVerdict refused = GpuScenarioGuard.Evaluate(dgpuOnly, occupancySnapshotAvailable: true, occupied: true, forceConfirmed: true, userOverrideDgpuOwnsDisplay: false);
		Check("独显独家 + 已确认 + 无 override ⇒ ⛔ 拒绝", refused.Refused, true);
		CheckEq("拒绝理由 = dgpu-owns-only-active-display", refused.RefusalReason, "dgpu-owns-only-active-display");
		Check("独显独家 ⇒ 命中黑屏风险标记", refused.DgpuOwnsOnlyActiveDisplay, true);
		Check("独显独家 ⇒ 即使已确认也不放行", refused.ForceAllowed, false);
		GpuScenarioVerdict overridden = GpuScenarioGuard.Evaluate(dgpuOnly, occupancySnapshotAvailable: true, occupied: true, forceConfirmed: true, userOverrideDgpuOwnsDisplay: true);
		Check("独显独家 + 明确 override + 已确认 ⇒ 放行", overridden.ForceAllowed && !overridden.Refused, true);
		GpuScenarioVerdict overriddenUnconfirmed = GpuScenarioGuard.Evaluate(dgpuOnly, occupancySnapshotAvailable: true, occupied: true, forceConfirmed: false, userOverrideDgpuOwnsDisplay: true);
		Check("有 override 但用户没做第二次动作 ⇒ ⛔ 仍不放行", overriddenUnconfirmed.ForceAllowed, false);
		Check("有 override 但没确认 ⇒ 要求确认", overriddenUnconfirmed.NeedsForceConfirmation, true);

		Console.WriteLine();
		Console.WriteLine("== ②④ 混合模式下的正常路 / 占用路 / 快照不可用 ==");
		GpuScenarioVerdict clean = GpuScenarioGuard.Evaluate(hybrid, occupancySnapshotAvailable: true, occupied: false, forceConfirmed: false, userOverrideDgpuOwnsDisplay: false);
		Check("混合 + 无占用 ⇒ 不拒、不需强制确认（走普通路）", !clean.Refused && !clean.NeedsForceConfirmation && !clean.ForceAllowed, true);
		GpuScenarioVerdict occupied = GpuScenarioGuard.Evaluate(hybrid, occupancySnapshotAvailable: true, occupied: true, forceConfirmed: false, userOverrideDgpuOwnsDisplay: false);
		Check("混合 + 有占用 + 未确认 ⇒ 要求用户主动确认", occupied.NeedsForceConfirmation && !occupied.Refused, true);
		Check("混合 + 有占用 + 未确认 ⇒ ⛔ 还不放行", occupied.ForceAllowed, false);
		GpuScenarioVerdict occupiedConfirmed = GpuScenarioGuard.Evaluate(hybrid, occupancySnapshotAvailable: true, occupied: true, forceConfirmed: true, userOverrideDgpuOwnsDisplay: false);
		Check("混合 + 有占用 + 已确认 ⇒ 放行强制", occupiedConfirmed.ForceAllowed && !occupiedConfirmed.Refused, true);
		GpuScenarioVerdict noSnapshot = GpuScenarioGuard.Evaluate(hybrid, occupancySnapshotAvailable: false, occupied: false, forceConfirmed: true, userOverrideDgpuOwnsDisplay: false);
		CheckEq("快照不可用 ⇒ 拒绝（理由 occupancy-snapshot-unavailable）", noSnapshot.RefusalReason, "occupancy-snapshot-unavailable");
		GpuScenarioVerdict unknownRoute = GpuScenarioGuard.Evaluate(DisplayRouteSnapshot.Unavailable, occupancySnapshotAvailable: true, occupied: false, forceConfirmed: true, userOverrideDgpuOwnsDisplay: true);
		CheckEq("显示通路读不到 ⇒ 拒绝（理由 display-route-unavailable）", unknownRoute.RefusalReason, "display-route-unavailable");

		Console.WriteLine();
		Console.WriteLine("== ② 保护名单过滤 + %SystemRoot% 闸门（安全门 (a)(d)）==");
		Check("名单内进程（explorer）⇒ 不是可结束候选", DgpuApplicationSafety.IsSafeCandidate(App(5000, "explorer", 1), 100, 1), false);
		Check("名单内进程（nvcontainer）⇒ 不是可结束候选", DgpuApplicationSafety.IsSafeCandidate(App(5001, "nvcontainer", 1), 100, 1), false);
		Check("controlcenter 前缀 ⇒ 不是可结束候选", DgpuApplicationSafety.IsSafeCandidate(App(5002, "ControlCenter3", 1), 100, 1), false);
		Check("LM 自己（l-mechrevo）⇒ 不是可结束候选", DgpuApplicationSafety.IsSafeCandidate(App(5003, "L-Mechrevo", 1), 100, 1), false);
		Check("本进程 PID ⇒ 不是可结束候选", DgpuApplicationSafety.IsSafeCandidate(App(100, "game", 1), 100, 1), false);
		Check("别的会话 ⇒ 不是可结束候选", DgpuApplicationSafety.IsSafeCandidate(App(5004, "game", 2), 100, 1), false);
		Check("没有启动时间（可能已退出）⇒ 不是可结束候选", DgpuApplicationSafety.IsSafeCandidate(new DgpuApplication(5005, "game", 1, null, false), 100, 1), false);
		Check("普通用户程序 ⇒ 是可结束候选", DgpuApplicationSafety.IsSafeCandidate(App(5006, "game", 1), 100, 1), true);
		Check("有窗口的普通程序 ⇒ 是可结束候选", DgpuApplicationSafety.IsSafeCandidate(new DgpuApplication(5007, "blender", 1, DateTime.UtcNow, true), 100, 1), true);
		CheckEq("SystemRoot 之下 ⇒ true", DgpuApplicationSafety.IsUnderSystemRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\System32\\notepad.exe"), true);
		CheckEq("SystemRoot 之下（大小写不同）⇒ true", DgpuApplicationSafety.IsUnderSystemRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToUpperInvariant() + "\\system32\\cmd.exe"), true);
		CheckEq("普通程序目录 ⇒ false", DgpuApplicationSafety.IsUnderSystemRoot("D:\\Games\\game.exe"), false);
		CheckEq("路径为空 ⇒ null（上层按拒绝结束处理）", DgpuApplicationSafety.IsUnderSystemRoot(""), null);
		CheckEq("路径读不到 ⇒ null", DgpuApplicationSafety.IsUnderSystemRoot(null), null);

		Console.WriteLine();
		Console.WriteLine("== ② 强制结束名单的二次过滤（安全门 (a)(d)）==");
		string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		List<DgpuApplication> candidates = new List<DgpuApplication>
		{
			App(6001, "game", 1),
			App(6002, "explorer", 1),
			App(100, "L-Mechrevo", 1),
			App(6003, "systemtool", 1),
			App(6004, "nodpath", 1),
			new DgpuApplication(6005, "gone", 1, null, false)
		};
		Dictionary<int, string?> paths = new Dictionary<int, string?>
		{
			[6001] = "D:\\Games\\game.exe",
			[6002] = systemRoot + "\\explorer.exe",
			[100] = "D:\\openLM\\L-Mechrevo.exe",
			[6003] = systemRoot + "\\System32\\systemtool.exe",
			[6004] = null
		};
		GpuForceKillPlan plan = DgpuForceKillPlanBuilder.TryPlan(candidates, 100, 1, forceConfirmed: true, app => paths.TryGetValue(app.ProcessId, out string? p) ? p : null);
		Check("过滤后只剩 1 个（普通用户程序）", plan.SafeToKill.Count == 1, true);
		CheckEq("留下来的就是 game", plan.SafeToKill.Count == 1 ? plan.SafeToKill[0].ProcessName : "-", "game");
		Check("跳过 5 个（名单内/本进程/SystemRoot/路径读不到/已退出）", plan.Skipped.Count == 5, true);
		Check("有确认过 ⇒ 不算被拒", plan.Refused, false);
		Check("名单+跳过 = 输入全集（互斥且不漏）", plan.SafeToKill.Count + plan.Skipped.Count == candidates.Count, true);
		GpuForceKillPlan unconfirmed = DgpuForceKillPlanBuilder.TryPlan(candidates, 100, 1, forceConfirmed: false, app => paths.TryGetValue(app.ProcessId, out string? p) ? p : null);
		Check("⛔ 没确认 ⇒ 可结束名单必须是空的（绝不静默结束）", unconfirmed.SafeToKill.Count == 0, true);
		Check("没确认 ⇒ 跳过名单也是空的（还没到列名单那一步）", unconfirmed.Skipped.Count == 0, true);
		CheckEq("没确认 ⇒ 理由 force-not-confirmed", unconfirmed.RefusedReason, DgpuForceKillPlanBuilder.ReasonNotConfirmed);
		GpuForceKillPlan empty = DgpuForceKillPlanBuilder.TryPlan(new List<DgpuApplication>(), 100, 1, forceConfirmed: true, _ => "D:\\x.exe");
		Check("空名单 + 已确认 ⇒ 不拒、无事可做", !empty.Refused && empty.SafeToKill.Count == 0, true);
		GpuForceKillPlan nullList = DgpuForceKillPlanBuilder.TryPlan(null, 100, 1, forceConfirmed: true, _ => "D:\\x.exe");
		Check("快照为 null ⇒ 不崩、无事可做", !nullList.Refused && nullList.SafeToKill.Count == 0, true);
		Check("读路径抛异常 ⇒ 该进程被跳过（不结束）", DgpuForceKillPlanBuilder.TryPlan(new List<DgpuApplication> { App(7001, "boom", 1) }, 100, 1, true, _ => throw new InvalidOperationException("nope")).SafeToKill.Count == 0, true);
		CheckEq("跳过理由：本进程", DgpuForceKillPlanBuilder.DescribeSkipReason(App(100, "L-Mechrevo", 1), 100, 1, null), DgpuForceKillPlanBuilder.ReasonIsLocalProcess);
		CheckEq("跳过理由：保护名单", DgpuForceKillPlanBuilder.DescribeSkipReason(App(6002, "explorer", 1), 100, 1, systemRoot + "\\explorer.exe"), DgpuForceKillPlanBuilder.ReasonProtectedProcess);
		CheckEq("跳过理由：SystemRoot", DgpuForceKillPlanBuilder.DescribeSkipReason(App(6003, "systemtool", 1), 100, 1, systemRoot + "\\System32\\systemtool.exe"), DgpuForceKillPlanBuilder.ReasonUnderSystemRoot);
		CheckEq("跳过理由：路径读不到", DgpuForceKillPlanBuilder.DescribeSkipReason(App(6004, "nodpath", 1), 100, 1, null), DgpuForceKillPlanBuilder.ReasonImagePathUnavailable);

		Console.WriteLine();
		Console.WriteLine("== ④ 回滚触发条件（安全门 (c)）==");
		Check("下发了 + 未确认 + 之前是已知值 ⇒ ⭐ 回滚", IgpuOnlySaveModeDecision.ShouldRollback(commandSent: true, confirmed: false, previousMode: 1), true);
		Check("下发了 + 未确认 + 之前是 AUTO(3) ⇒ 回滚", IgpuOnlySaveModeDecision.ShouldRollback(commandSent: true, confirmed: false, previousMode: 3), true);
		Check("下发了 + 已确认 ⇒ ⛔ 不回滚", IgpuOnlySaveModeDecision.ShouldRollback(commandSent: true, confirmed: true, previousMode: 1), false);
		Check("没下发（拿不到锁/非法模式）⇒ ⛔ 不回滚", IgpuOnlySaveModeDecision.ShouldRollback(commandSent: false, confirmed: false, previousMode: 1), false);
		Check("之前取值未知(-1) ⇒ ⛔ 不回滚（⛔ 不把「不知道」瞎写成 OFF）", IgpuOnlySaveModeDecision.ShouldRollback(commandSent: true, confirmed: false, previousMode: -1), false);

		Console.WriteLine();
		Console.WriteLine($"== 结果：{_total - _fail}/{_total} 通过，失败 {_fail} 条 ==");
		return (_fail == 0) ? 0 : 1;
	}

	private static DgpuApplication App(int pid, string name, int session)
	{
		return new DgpuApplication(pid, name, session, DateTime.UtcNow.AddMinutes(-5), false);
	}
}
