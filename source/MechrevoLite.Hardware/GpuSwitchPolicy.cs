namespace MechrevoLite.Hardware;

internal static class GpuSwitchPolicy
{
	internal static bool ShouldDeferUntilGcuConnection(bool hardwareCreated, bool gcuConnected, bool serviceAvailable)
	{
		if (hardwareCreated)
		{
			if (gcuConnected)
			{
				return !serviceAvailable;
			}
			return true;
		}
		return false;
	}

	internal static GpuSwitchPlan Resolve(int reportedMode, int targetMode, bool currentStateFresh = true)
	{
		// 2026-10-05：删除「自动切换显卡模式」(mode 3/AUTO) —— 原先 mode 3 需按自动运行时刻
		// 反推成 0/1，该映射（ResolveCurrentMode）只服务 AUTO，已整段删除；
		// 现在直接采用上报的三档模式（0 核显 / 1 混合 / 2 独显直连）。
		if ((reportedMode == targetMode) & currentStateFresh)
		{
			return new GpuSwitchPlan(GpuSwitchRoute.NoChange, RequiresDgpuProcessPreflight: false);
		}
		if (reportedMode == 2 || targetMode == 2 || (reportedMode == 0 && targetMode != 0))
		{
			return new GpuSwitchPlan(GpuSwitchRoute.Restart, RequiresDgpuProcessPreflight: false);
		}
		if (reportedMode == 1 && targetMode == 0)
		{
			// 2026-10-03：能切集显的机型（三模/50 系，CanOfferIgpuOnly）统一走重启后应用；
			// 判断依据与核显省电行一致，不再重复传参
			return new GpuSwitchPlan(GpuSwitchRoute.Restart, RequiresDgpuProcessPreflight: false);
		}
		// 2026-10-03：对齐 beta21 删除 Direct 路由——集显→混合(0→1)同样走重启后应用
		return new GpuSwitchPlan(GpuSwitchRoute.Restart, RequiresDgpuProcessPreflight: false);
	}
}
