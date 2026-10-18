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
		CheckPlannerEmitsGpuOverclockOnFirmwareSlotForBaolongda();
		CheckPlannerKeepsGcuStepsUnchanged();
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
