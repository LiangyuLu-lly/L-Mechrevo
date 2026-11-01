using System;
using System.Collections.Generic;
using System.Linq;

namespace MechrevoLite.Mode;

public static class PerfModeResolver
{
	public static PerfModeResolution? Resolve(IReadOnlyList<PerfModeDefinition> modes, IReadOnlyList<FirmwareSlotState> slots, int operatingMode, int customProfileIndex, bool silentTurbo, string? lastCustomId)
	{
		ArgumentNullException.ThrowIfNull(modes, "modes");
		ArgumentNullException.ThrowIfNull(slots, "slots");
		if ((operatingMode < 0 || operatingMode > 3) ? true : false)
		{
			return null;
		}
		if (operatingMode == 3)
		{
			string owner = slots.FirstOrDefault((FirmwareSlotState s) => s.Index == customProfileIndex)?.OwnerModeId;
			PerfModeDefinition perfModeDefinition = ((owner == null) ? null : modes.FirstOrDefault((PerfModeDefinition m) => m.Id == owner && m.Route == PerfModeRoute.FirmwareSlot));
			if ((object)perfModeDefinition != null)
			{
				return new PerfModeResolution(perfModeDefinition.Id, Redirect: false, $"custom slot {customProfileIndex} belongs to {perfModeDefinition.Id}");
			}
			PerfModeDefinition perfModeDefinition2 = modes.FirstOrDefault((PerfModeDefinition m) => m.IsCustom && m.Id == lastCustomId) ?? modes.FirstOrDefault((PerfModeDefinition m) => m.IsCustom);
			if ((object)perfModeDefinition2 != null)
			{
				return new PerfModeResolution(perfModeDefinition2.Id, Redirect: true, $"custom slot {customProfileIndex} has no owner");
			}
			return null;
		}
		PerfModeKind kind = PerfModeMapping.FromFirmware(operatingMode, silentTurbo);
		PerfModeDefinition perfModeDefinition3 = modes.FirstOrDefault((PerfModeDefinition m) => m.Kind == kind);
		if ((object)perfModeDefinition3 == null)
		{
			return null;
		}
		if (perfModeDefinition3.Route != PerfModeRoute.FirmwareSlot)
		{
			return new PerfModeResolution(perfModeDefinition3.Id, Redirect: false, "built-in mode");
		}
		return new PerfModeResolution(perfModeDefinition3.Id, Redirect: true, perfModeDefinition3.Id + " is customized and runs on a firmware slot");
	}

	public static bool IsFirmwareOn(PerfModeDefinition mode, IReadOnlyList<FirmwareSlotState> slots, int operatingMode, int customProfileIndex, bool silentTurbo, bool turboSubModeSupported)
	{
		ArgumentNullException.ThrowIfNull(mode, "mode");
		ArgumentNullException.ThrowIfNull(slots, "slots");
		if (mode.Route == PerfModeRoute.FirmwareSlot)
		{
			if (operatingMode != 3)
			{
				return false;
			}
			return slots.Any((FirmwareSlotState s) => s.Index == customProfileIndex && string.Equals(s.OwnerModeId, mode.Id, StringComparison.Ordinal));
		}
		if (operatingMode != PerfModeMapping.ToFirmwareOperatingMode(mode.Kind))
		{
			return false;
		}
		bool flag = !turboSubModeSupported;
		if (!flag)
		{
			PerfModeKind kind = mode.Kind;
			bool flag2 = (uint)(kind - 2) <= 1u;
			flag = !flag2;
		}
		if (flag)
		{
			return true;
		}
		return silentTurbo == (mode.Kind == PerfModeKind.SilentTurbo);
	}

	public static PerfModeSettings Delta(PerfModeSettings before, PerfModeSettings after)
	{
		ArgumentNullException.ThrowIfNull(before, "before");
		ArgumentNullException.ThrowIfNull(after, "after");
		bool flag = !Nullable.Equals(before.GpuOverclockOn, after.GpuOverclockOn) || !Nullable.Equals(before.GpuCoreOffset, after.GpuCoreOffset) || !Nullable.Equals(before.GpuMemoryOffset, after.GpuMemoryOffset);
		return new PerfModeSettings
		{
			Pl1 = Pick<int>(before.Pl1, after.Pl1),
			Pl2 = Pick<int>(before.Pl2, after.Pl2),
			Pl4 = Pick<int>(before.Pl4, after.Pl4),
			GpuTgp = Pick<int>(before.GpuTgp, after.GpuTgp),
			GpuDynamicBoostOn = Pick<bool>(before.GpuDynamicBoostOn, after.GpuDynamicBoostOn),
			GpuDynamicBoost = Pick<int>(before.GpuDynamicBoost, after.GpuDynamicBoost),
			CpuFanDuty = PickCurve(before.CpuFanDuty, after.CpuFanDuty),
			GpuFanDuty = PickCurve(before.GpuFanDuty, after.GpuFanDuty),
			TccOn = Pick<bool>(before.TccOn, after.TccOn),
			TccTarget = Pick<int>(before.TccTarget, after.TccTarget),
			GpuOverclockOn = (flag ? after.GpuOverclockOn : ((bool?)null)),
			GpuCoreOffset = (flag ? after.GpuCoreOffset : ((int?)null)),
			GpuMemoryOffset = (flag ? after.GpuMemoryOffset : ((int?)null)),
			FanBoost = Pick<bool>(before.FanBoost, after.FanBoost),
			FanSwitchSpeedOn = Pick<bool>(before.FanSwitchSpeedOn, after.FanSwitchSpeedOn),
			FanSwitchSpeedMs = Pick<int>(before.FanSwitchSpeedMs, after.FanSwitchSpeedMs),
			WindowsPowerMode = Pick<int>(before.WindowsPowerMode, after.WindowsPowerMode),
			CpuBoost = Pick<int>(before.CpuBoost, after.CpuBoost),
			PowerPlanGuid = (string.Equals(before.PowerPlanGuid, after.PowerPlanGuid, StringComparison.OrdinalIgnoreCase) ? null : after.PowerPlanGuid),
			RefreshHz = Pick<int>(before.RefreshHz, after.RefreshHz)
		};
		static T? Pick<T>(T? a, T? b) where T : struct
		{
			if (!Nullable.Equals(a, b))
			{
				return b;
			}
			return null;
		}
		static int[]? PickCurve(int[]? a, int[]? b)
		{
			if (a != null || b != null)
			{
				if (a == null || b == null || !a.SequenceEqual(b))
				{
					return b;
				}
				return null;
			}
			return null;
		}
	}

	public static PerfModeSettings Materialize(PerfModeSettings user, PerfModeSettings vendorSeed, PerfLiveSnapshot? live)
	{
		ArgumentNullException.ThrowIfNull(user, "user");
		ArgumentNullException.ThrowIfNull(vendorSeed, "vendorSeed");
		PerfModeSettings perfModeSettings = user with
		{
			Pl1 = (user.Pl1 ?? vendorSeed.Pl1),
			Pl2 = (user.Pl2 ?? vendorSeed.Pl2),
			Pl4 = (user.Pl4 ?? vendorSeed.Pl4),
			GpuTgp = (user.GpuTgp ?? vendorSeed.GpuTgp),
			GpuDynamicBoostOn = (user.GpuDynamicBoostOn ?? vendorSeed.GpuDynamicBoostOn),
			GpuDynamicBoost = (user.GpuDynamicBoost ?? vendorSeed.GpuDynamicBoost),
			CpuFanDuty = (user.CpuFanDuty ?? vendorSeed.CpuFanDuty),
			GpuFanDuty = (user.GpuFanDuty ?? vendorSeed.GpuFanDuty),
			TccOn = (user.TccOn ?? vendorSeed.TccOn),
			TccTarget = (user.TccTarget ?? vendorSeed.TccTarget),
			GpuOverclockOn = (user.GpuOverclockOn ?? vendorSeed.GpuOverclockOn),
			GpuCoreOffset = (user.GpuCoreOffset ?? vendorSeed.GpuCoreOffset),
			GpuMemoryOffset = (user.GpuMemoryOffset ?? vendorSeed.GpuMemoryOffset),
			FanSwitchSpeedOn = (user.FanSwitchSpeedOn ?? vendorSeed.FanSwitchSpeedOn),
			FanSwitchSpeedMs = (user.FanSwitchSpeedMs ?? vendorSeed.FanSwitchSpeedMs)
		};
		if ((object)live != null)
		{
			return live.FillNulls(perfModeSettings);
		}
		return perfModeSettings;
	}
}
