using System;
using System.Collections.Generic;
using System.Linq;
using MechrevoLite.Mode;

internal static class Program
{
	private static int _fail;

	private static int Main()
	{
		CheckPl1LoweredWhenPl2Lowered();
		CheckPl2RaisedWhenPl1Raised();
		CheckPl4Cascade();
		CheckPl1PinnedByPl2Max();
		CheckPl4NotLoweredWhenAlreadyAbove();
		CheckNoChangeWhenValid();
		CheckUnknownIgnored();
		// ⭐⭐ 2026-10-19（机主：「是持续功耗时间（STAPM time）」）：
		//   C1 = 显示/下发**同一判据**：宝龙达 && 30 系 && SMU 可用；
		//   C2 = 规划器只在判据为真时生成 `SmuStapmTimeSeconds`（未设置/不可用 ⇒ 不生成，旧行为）。
		CheckSmuPowerRoutePredicate();
		CheckPlannerEmitsStapmTimeWindowOnlyWhenSmuUsable();
		// ⭐⭐ 2026-10-19（机主 UI 顺序 + FPPT 结论）：
		//   C3a = 步骤顺序与 UI 行顺序一致：PL1 → PL2 → SmuStapmTimeSeconds → CpuTccOffset；
		//   C3b = **没有**独立 FPPT 字段（fast limit 就是 fPPT，PL2 已在写它）⇒ 不重复加行。
		CheckSmuPowerRowOrderAndNoIndependentFppt();
		CheckPlannerEmitsGpuOverclockOnFirmwareSlotForBaolongda();
		CheckPlannerKeepsGcuStepsUnchanged();
		// ⭐⭐ 2026-10-19（机主：「显卡超频设置了但频率没变化」· 根因②）：
		//   D = "应用"是否走硬件下发的纯判据：宝龙达自定义档**不是当前激活档也要下发**；
		//       非宝龙达保持旧口径（只有当前激活档才下发）⇒ GCU 行为逐位不变。
		CheckLocalApplyGateForBaolongdaCustom();
		Console.WriteLine(_fail == 0 ? "ALL OK" : $"{_fail} FAILED");
		return (_fail == 0) ? 0 : 1;
	}

	private static void CheckPl1LoweredWhenPl2Lowered()
	{
		// 用户把 PL2 从 120 降到 100 ⇒ 联动把 PL1 从 120 降到 100
		bool ok = PowerLimitOrder.TryLink(PowerLimitOrder.Field.Pl2, 120, 100, null,
			R(5, 120), R(5, 150), R(5, 200),
			out int? p1, out int? p2, out int? p4, out IReadOnlyList<PowerLimitOrder.Change> changes, out string reason);
		Expect("A1 ok", ok);
		Expect("A1 PL1", p1 == 100, p1);
		Expect("A1 PL2", p2 == 100, p2);
		Expect("A1 PL4", p4 == null, p4);
		Expect("A1 changes", changes.Count == 1 && changes[0].Field == PowerLimitOrder.Field.Pl1 && changes[0].From == 120 && changes[0].To == 100);
		string log = PowerLimitOrder.Describe(changes, reason);
		Console.WriteLine("  A1 log: " + log);
		Expect("A1 log contains", log.Contains("PL1 120→100") && log.Contains("因 PL2=100"));
	}

	private static void CheckPl2RaisedWhenPl1Raised()
	{
		// 用户把 PL1 抬到 130（> PL2=100）⇒ PL1 被硬件上限夹到 120、联动抬 PL2=120、PL4 110→120
		bool ok = PowerLimitOrder.TryLink(PowerLimitOrder.Field.Pl1, 130, 100, 110,
			R(5, 120), R(5, 150), R(5, 200),
			out int? p1, out int? p2, out int? p4, out IReadOnlyList<PowerLimitOrder.Change> changes, out string reason);
		Expect("A2 ok", ok);
		Expect("A2 PL1", p1 == 120, p1);
		Expect("A2 PL2", p2 == 120, p2);
		Expect("A2 PL4", p4 == 120, p4);
		Console.WriteLine("  A2 log: " + PowerLimitOrder.Describe(changes, reason));
	}

	private static void CheckPl4Cascade()
	{
		// 用户把 PL4 降到 90（< PL2=100）⇒ PL2 90、PL1 110→90
		bool ok = PowerLimitOrder.TryLink(PowerLimitOrder.Field.Pl4, 110, 100, 90,
			R(5, 120), R(5, 150), R(5, 200),
			out int? p1, out int? p2, out int? p4, out IReadOnlyList<PowerLimitOrder.Change> changes, out string reason);
		Expect("A3 ok", ok);
		Expect("A3 PL1", p1 == 90, p1);
		Expect("A3 PL2", p2 == 90, p2);
		Expect("A3 PL4", p4 == 90, p4);
	}

	private static void CheckPl1PinnedByPl2Max()
	{
		// PL1 想抬到 170，但 PL2 上限 150 ⇒ 最终 150/150（仍满足关系）
		bool ok = PowerLimitOrder.TryLink(PowerLimitOrder.Field.Pl1, 170, 100, null,
			R(5, 200), R(5, 150), R(5, 200),
			out int? p1, out int? p2, out int? p4, out IReadOnlyList<PowerLimitOrder.Change> changes, out string reason);
		Expect("A4 ok", ok);
		Expect("A4 PL1", p1 == 150, p1);
		Expect("A4 PL2", p2 == 150, p2);
	}

	private static void CheckPl4NotLoweredWhenAlreadyAbove()
	{
		// PL1 抬到 130，PL2=100，PL4 本来 180 ⇒ PL4 必须保持 180（只抬 PL2）
		bool ok = PowerLimitOrder.TryLink(PowerLimitOrder.Field.Pl1, 130, 100, 180,
			R(5, 120), R(5, 150), R(5, 200),
			out int? p1, out int? p2, out int? p4, out IReadOnlyList<PowerLimitOrder.Change> changes, out string reason);
		Expect("A5 ok", ok);
		Expect("A5 PL4 stays", p4 == 180, p4);
		Expect("A5 PL2 raised", p2 == 120, p2);
	}

	private static void CheckNoChangeWhenValid()
	{
		bool ok = PowerLimitOrder.TryLink(PowerLimitOrder.Field.Pl2, 100, 120, 150,
			R(5, 120), R(5, 150), R(5, 200),
			out int? p1, out int? p2, out int? p4, out IReadOnlyList<PowerLimitOrder.Change> changes, out string reason);
		Expect("A6 ok", ok);
		Expect("A6 no changes", changes.Count == 0);
	}

	private static void CheckUnknownIgnored()
	{
		// PL2 未设置（null）⇒ 抬 PL1 不应凭空造出 PL2
		bool ok = PowerLimitOrder.TryLink(PowerLimitOrder.Field.Pl1, 120, null, null,
			R(5, 120), R(5, 150), R(5, 200),
			out int? p1, out int? p2, out int? p4, out IReadOnlyList<PowerLimitOrder.Change> changes, out string reason);
		Expect("A7 ok", ok);
		Expect("A7 PL1", p1 == 120, p1);
		Expect("A7 PL2 unknown", p2 == null, p2);
		Expect("A7 no changes", changes.Count == 0);
	}

	private static void CheckSmuPowerRoutePredicate()
	{
		// ⭐⭐ 2026-10-19（机主：显示条件 =「当前自定义档实际走 SMU 下发」，不是"机型是 Gen30"）：
		//   唯一下发/显示判据 = `PerfModeCapabilities.UsesSmuForCustomPower`。
		//   C1a：宝龙达 + 30 系 + SMU 可用 ⇒ true（UI 建「功耗时间窗（STAPM，秒）」、下发走 SMU）。
		bool ready = PerfModeCapabilities.UsesSmuForCustomPower(usesBaolongda: true, isBaolongda30Series: true, smuReady: true);
		Expect("C1a SMU route enabled (baolongda+30+smu ok)", ready);
		//   C1b：PawnIO 未装 / RyzenSMU 初始化失败（smuReady=false）⇒ false（不建行、不生成步骤）。
		bool notReady = PerfModeCapabilities.UsesSmuForCustomPower(usesBaolongda: true, isBaolongda30Series: true, smuReady: false);
		Expect("C1b SMU route disabled when smu unavailable", !notReady);
		//   C1c：非 30 系（40 系有 WMI 23）⇒ 不走 SMU。
		bool gen40 = PerfModeCapabilities.UsesSmuForCustomPower(usesBaolongda: true, isBaolongda30Series: false, smuReady: true);
		Expect("C1c SMU route disabled for non-30-series", !gen40);
	}

	private static void CheckPlannerEmitsStapmTimeWindowOnlyWhenSmuUsable()
	{
		// ⭐⭐ 2026-10-19（机主：「功耗持续（时间）实际能不能放进去」）：
		//   判据为真 + 已设置 StapmTimeSeconds ⇒ 生成 `SmuStapmTimeSeconds`（MP1 0x18）这一步；
		//   判据为假 ⇒ **不生成**（旧行为：步骤列表不变、不写时间窗）。
		PerfModeSettings settings = new PerfModeSettings
		{
			Pl1 = 65,
			Pl2 = 100,
			StapmTimeSeconds = 30
		};
		PerfModeDefinition mode = new PerfModeDefinition("custom1", PerfModeKind.Custom, null, settings);
		FirmwareSlotPlan slotPlan = new FirmwareSlotPlan(0, NeedsParameterWrite: true, "test");
		PerfModeCapabilities smuCaps = new PerfModeCapabilities
		{
			SupportsFirmwareSlot = false,
			SupportsWmiPowerFields = true,
			SupportsSmuPowerControls = true
		};
		IReadOnlyList<PerfApplyStep> smuSteps = PerfModeApplyPlanner.Build(mode, slotPlan, smuCaps, includeSwitch: false);
		PerfApplyStep? timeStep = smuSteps.FirstOrDefault((PerfApplyStep s) => s.Kind == PerfApplyStepKind.WriteFirmwareField && s.WireKey == "SmuStapmTimeSeconds");
		Expect("C2a stapm-time step exists when SMU usable", (object?)timeStep != null);
		if ((object?)timeStep != null)
		{
			Expect("C2a stapm-time value", timeStep.Value == "30", timeStep.Value);
		}
		// 不可用 ⇒ 不生成（同一份 settings）
		PerfModeCapabilities noSmuCaps = new PerfModeCapabilities
		{
			SupportsFirmwareSlot = false,
			SupportsWmiPowerFields = true,
			SupportsSmuPowerControls = false
		};
		IReadOnlyList<PerfApplyStep> noSmuSteps = PerfModeApplyPlanner.Build(mode, slotPlan, noSmuCaps, includeSwitch: false);
		Expect("C2b no stapm-time step when SMU unavailable",
			noSmuSteps.All((PerfApplyStep s) => !(s.Kind == PerfApplyStepKind.WriteFirmwareField && s.WireKey == "SmuStapmTimeSeconds")));
		// 未设置 StapmTimeSeconds（旧配置缺省 = null）⇒ 即使 SMU 可用也不生成（不写、旧行为）
		PerfModeSettings unset = settings with { StapmTimeSeconds = null };
		IReadOnlyList<PerfApplyStep> unsetSteps = PerfModeApplyPlanner.Build(mode.With(unset), slotPlan, smuCaps, includeSwitch: false);
		Expect("C2c no stapm-time step when unset (old behaviour)",
			unsetSteps.All((PerfApplyStep s) => !(s.Kind == PerfApplyStepKind.WriteFirmwareField && s.WireKey == "SmuStapmTimeSeconds")));
		Console.WriteLine("  C2 steps: " + string.Join("  ", smuSteps.Select((PerfApplyStep s) => s.ToString())));
	}

	private static void CheckSmuPowerRowOrderAndNoIndependentFppt()
	{
		// ⭐⭐ 2026-10-19（机主 UI 调整）：行顺序 = PL1 → PL2 → 功耗时间窗（STAPM，秒）→ 温度墙；
		//   规划器步骤顺序与 UI 行顺序保持一致（日志可逐行对照）。
		PerfModeSettings settings = new PerfModeSettings
		{
			Pl1 = 65,
			Pl2 = 100,
			TccTarget = 95,
			StapmTimeSeconds = 30
		};
		PerfModeDefinition mode = new PerfModeDefinition("custom1", PerfModeKind.Custom, null, settings);
		PerfModeCapabilities caps = new PerfModeCapabilities
		{
			SupportsFirmwareSlot = false,
			SupportsWmiPowerFields = true,
			SupportsSmuPowerControls = true
		};
		IReadOnlyList<PerfApplyStep> steps = PerfModeApplyPlanner.Build(mode, new FirmwareSlotPlan(0, NeedsParameterWrite: true, "test"), caps, includeSwitch: false);
		int iPl1 = IndexOfWire(steps, "PL1");
		int iPl2 = IndexOfWire(steps, "PL2");
		int iTime = IndexOfWire(steps, "SmuStapmTimeSeconds");
		int iTcc = IndexOfWire(steps, "CpuTccOffset");
		Expect("C3a order: PL1 before PL2", iPl1 >= 0 && iPl2 > iPl1, $"{iPl1},{iPl2}");
		Expect("C3a order: PL2 before STAPM time", iPl2 >= 0 && iTime > iPl2, $"{iPl2},{iTime}");
		Expect("C3a order: STAPM time before temp wall", iTime >= 0 && iTcc > iTime, $"{iTime},{iTcc}");
		// ⭐⭐ C3b：**FPPT 不是独立字段** —— SMU 的 `fast limit`（MP1 0x15）就是 fPPT，PL2 已经写它
		//   ⇒ 机主要求的"若 FPPT 独立存在才加行"不成立，**不新增 FpptLimitW**（防将来重复加行）。
		bool hasFpptField = typeof(PerfModeSettings).GetProperty("FpptLimitW") != null
			|| typeof(PerfModeSettings).GetProperty("FpptLimit") != null;
		Expect("C3b no independent FPPT field (PL2 is fPPT)", !hasFpptField);
		// 缺省 = 未设置（⛔ 不编默认值）
		Expect("C3b StapmTimeSeconds default null", new PerfModeSettings().StapmTimeSeconds == null);
		Console.WriteLine("  C3 steps: " + string.Join("  ", steps.Select((PerfApplyStep s) => s.ToString())));
	}

	private static int IndexOfWire(IReadOnlyList<PerfApplyStep> steps, string wireKey)
	{
		for (int i = 0; i < steps.Count; i++)
		{
			if (steps[i].Kind == PerfApplyStepKind.WriteFirmwareField && steps[i].WireKey == wireKey)
			{
				return i;
			}
		}
		return -1;
	}

	private static void CheckLocalApplyGateForBaolongdaCustom()
	{
		// D：宝龙达自定义档即使不是"当前激活档"也允许本机下发（GPU 超频/PL/曲线才能真正执行）；
		//    非宝龙达（GCU/老接口）保持旧口径：只有当前激活档才下发 ⇒ 逐位不变。
		Expect("D1 baolongda custom inactive => apply", PerfApplyGate.ShouldApplyLocalCustom(usesBaolongda: true, isCustom: true, isActive: false));
		Expect("D2 baolongda custom active => apply", PerfApplyGate.ShouldApplyLocalCustom(usesBaolongda: true, isCustom: true, isActive: true));
		Expect("D3 non-baolongda inactive => no apply (old gate)", !PerfApplyGate.ShouldApplyLocalCustom(usesBaolongda: false, isCustom: true, isActive: false));
		Expect("D4 non-baolongda active => apply (old gate)", PerfApplyGate.ShouldApplyLocalCustom(usesBaolongda: false, isCustom: true, isActive: true));
		Expect("D5 baolongda builtin inactive => no apply (custom only)", !PerfApplyGate.ShouldApplyLocalCustom(usesBaolongda: true, isCustom: false, isActive: false));
	}

	private static void CheckPlannerEmitsGpuOverclockOnFirmwareSlotForBaolongda()
	{
		// B：宝龙达（自定义档 = FirmwareSlot 路由、无固件槽、有 WMI 功耗墙 + 本机 GPU 直写）
		PerfModeCapabilities caps = new PerfModeCapabilities
		{
			SupportsFirmwareSlot = false,
			SupportsWmiPowerFields = true,
			SupportsDirectGpuOverclock = true
		};
		PerfModeSettings settings = new PerfModeSettings
		{
			Pl1 = 100,
			Pl2 = 120,
			GpuOverclockOn = true,
			GpuCoreOffset = 100,
			GpuMemoryOffset = 200
		};
		PerfModeDefinition mode = new PerfModeDefinition("custom1", PerfModeKind.Custom, null, settings);
		IReadOnlyList<PerfApplyStep> steps = PerfModeApplyPlanner.Build(mode, new FirmwareSlotPlan(0, NeedsParameterWrite: true, "test"), caps, includeSwitch: false);
		PerfApplyStep? oc = steps.FirstOrDefault((PerfApplyStep s) => s.Kind == PerfApplyStepKind.ApplyGpuOverclock);
		Expect("B1 step exists", (object?)oc != null);
		if ((object?)oc != null)
		{
			Expect("B1 number", oc.Number == 1, oc.Number);
			Expect("B1 core", oc.GpuOffsets.HasValue && oc.GpuOffsets.Value.Core == 100, oc.GpuOffsets);
			Expect("B1 mem", oc.GpuOffsets.HasValue && oc.GpuOffsets.Value.Memory == 200, oc.GpuOffsets);
			Expect("B1 verify", oc.Verify == PerfApplyVerify.GpuClockOffset, oc.Verify);
		}
		Console.WriteLine("  B1 steps: " + string.Join("  ", steps.Select((PerfApplyStep s) => s.ToString())));
		// 关闭超频 ⇒ 步骤仍然生成（把偏移归零），数值 = 0/0
		PerfModeSettings offSettings = settings with { GpuOverclockOn = false };
		IReadOnlyList<PerfApplyStep> offSteps = PerfModeApplyPlanner.Build(mode.With(offSettings), new FirmwareSlotPlan(0, NeedsParameterWrite: true, "test"), caps, includeSwitch: false);
		PerfApplyStep? offOc = offSteps.FirstOrDefault((PerfApplyStep s) => s.Kind == PerfApplyStepKind.ApplyGpuOverclock);
		Expect("B1 off step exists", (object?)offOc != null);
		if ((object?)offOc != null)
		{
			Expect("B1 off number", offOc.Number == 0, offOc.Number);
			Expect("B1 off offsets zero", offOc.GpuOffsets.HasValue && offOc.GpuOffsets.Value.Core == 0 && offOc.GpuOffsets.Value.Memory == 0, offOc.GpuOffsets);
		}
	}

	private static void CheckPlannerKeepsGcuStepsUnchanged()
	{
		// B：非宝龙达（GCU）⇒ SupportsDirectGpuOverclock 默认 false ⇒ FirmwareSlot 路由下**不**生成新步骤
		PerfModeCapabilities caps = new PerfModeCapabilities
		{
			SupportsFirmwareSlot = true,
			SupportsWmiPowerFields = false
		};
		PerfModeSettings settings = new PerfModeSettings
		{
			Pl1 = 100,
			Pl2 = 120,
			GpuOverclockOn = true,
			GpuCoreOffset = 100,
			GpuMemoryOffset = 200
		};
		PerfModeDefinition mode = new PerfModeDefinition("custom1", PerfModeKind.Custom, null, settings);
		IReadOnlyList<PerfApplyStep> steps = PerfModeApplyPlanner.Build(mode, new FirmwareSlotPlan(0, NeedsParameterWrite: true, "test"), caps, includeSwitch: false);
		Expect("B2 no direct GPU OC step for GCU", steps.All((PerfApplyStep s) => s.Kind != PerfApplyStepKind.ApplyGpuOverclock));
		Expect("B2 legacy fields kept", steps.Any((PerfApplyStep s) => s.Kind == PerfApplyStepKind.WriteFirmwareField && s.WireKey == "GpuCoreClockOffsetOC"));
	}

	private static PowerLimitOrder.Range R(int min, int max)
	{
		return new PowerLimitOrder.Range(min, max);
	}

	private static void Expect(string name, bool condition, object? actual = null)
	{
		if (!condition)
		{
			_fail++;
			Console.WriteLine("FAIL " + name + (actual == null ? "" : (" (actual=" + actual + ")")));
		}
		else
		{
			Console.WriteLine("ok   " + name);
		}
	}
}
