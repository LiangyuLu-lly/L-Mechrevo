using System;
using System.Collections.Generic;
using MechrevoLite.Gpu;

namespace MechrevoLite.Hardware;

public static class GpuRouteCommandLayer
{
	public const int RestartDelayMilliseconds = 800;

	public const int RetryIntervalMilliseconds = 2000;

	public const int RetryEveryPolls = 4;

	public static IReadOnlyList<GpuRouteCommand> BuildRestartCommands(int mode, bool supportsDgpuDirect, DgpuGenerationKind generation)
	{
		IReadOnlyList<Dictionary<string, object>> readOnlyList = MechrevoService.CreateGpuRestartTargetPayloads(mode, supportsDgpuDirect, generation);
		if (readOnlyList.Count == 0)
		{
			return Array.Empty<GpuRouteCommand>();
		}
		List<GpuRouteCommand> list = new List<GpuRouteCommand>(readOnlyList.Count + 1);
		foreach (Dictionary<string, object> item in readOnlyList)
		{
			list.Add(new GpuRouteCommand("Setting/Control", ActionOf(item), item, 0));
		}
		list.Add(new GpuRouteCommand("Setting/Control", "DGPU_DIRECT_CONNECT_RESTART", new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_RESTART" }, 800));
		return list;
	}

	public static GpuRouteCommand? BuildSwitchCommand(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly, DgpuGenerationKind generation)
	{
		Dictionary<string, object> payload = MechrevoService.CreateGpuSwitchPayload(mode, supportsDgpuDirect, supportsIgpuOnly);
		string action = ActionOf(payload);
		if (!IsAllowedByGeneration(generation, action, supportsIgpuOnly))
		{
			return null;
		}
		return new GpuRouteCommand("Setting/Control", action, payload, 0);
	}

	public static bool IsAllowedByGeneration(DgpuGenerationKind generation, string action)
	{
		return DisplayRoutePolicy.AllowsAction(generation, action);
	}

	public static bool IsAllowedByGeneration(DgpuGenerationKind generation, string action, bool threeMode)
	{
		return DisplayRoutePolicy.AllowsAction(generation, action, threeMode);
	}

	private static string ActionOf(Dictionary<string, object> payload)
	{
		if (!payload.TryGetValue("Action", out object value))
		{
			return "";
		}
		return value?.ToString() ?? "";
	}
}
