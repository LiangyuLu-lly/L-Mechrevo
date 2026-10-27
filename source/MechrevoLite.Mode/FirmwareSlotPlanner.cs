using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MechrevoLite.Mode;

public static class FirmwareSlotPlanner
{
	public const int DefaultSlotCount = 5;

	public const int ReducedSlotCount = 3;

	public const int MaxSlotCount = 5;

	public static int SlotCountFor(int customizeId)
	{
		if (customizeId != 9)
		{
			return 5;
		}
		return 3;
	}

	public static FirmwareSlotPlan Plan(string modeId, string signature, IReadOnlyList<FirmwareSlotState> slots, string? runningModeId = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(modeId, "modeId");
		ArgumentNullException.ThrowIfNull(slots, "slots");
		if (slots.Count == 0)
		{
			throw new ArgumentException("该机型没有固件自定义档，调用方必须先判断可用性", "slots");
		}
		foreach (FirmwareSlotState slot in slots)
		{
			if (string.Equals(slot.OwnerModeId, modeId, StringComparison.Ordinal))
			{
				bool flag = !string.Equals(slot.Signature, signature, StringComparison.Ordinal);
				return new FirmwareSlotPlan(slot.Index, flag, flag ? "slot owned but parameters changed" : "slot hit");
			}
		}
		FirmwareSlotState firmwareSlotState = (from s in slots
			where s.IsFree
			orderby s.Index
			select s).FirstOrDefault();
		if ((object)firmwareSlotState != null)
		{
			return new FirmwareSlotPlan(firmwareSlotState.Index, NeedsParameterWrite: true, "free slot");
		}
		List<FirmwareSlotState> list = slots.Where((FirmwareSlotState s) => runningModeId == null || !string.Equals(s.OwnerModeId, runningModeId, StringComparison.Ordinal)).ToList();
		if (list.Count == 0)
		{
			list = slots.ToList();
		}
		return new FirmwareSlotPlan((from s in list
			orderby s.LastUsedTicks, s.Index
			select s).First().Index, NeedsParameterWrite: true, "evicted least-recently-used slot");
	}

	public static IReadOnlyList<FirmwareSlotState> Assign(IReadOnlyList<FirmwareSlotState> slots, int slotIndex, string modeId, string? signature, long nowTicks)
	{
		ArgumentNullException.ThrowIfNull(slots, "slots");
		ArgumentException.ThrowIfNullOrEmpty(modeId, "modeId");
		List<FirmwareSlotState> list = new List<FirmwareSlotState>(slots.Count);
		foreach (FirmwareSlotState slot in slots)
		{
			if (slot.Index == slotIndex)
			{
				list.Add(new FirmwareSlotState(slot.Index, modeId, signature, nowTicks));
			}
			else if (string.Equals(slot.OwnerModeId, modeId, StringComparison.Ordinal))
			{
				list.Add(new FirmwareSlotState(slot.Index, null, null, slot.LastUsedTicks));
			}
			else
			{
				list.Add(slot);
			}
		}
		return list;
	}

	public static IReadOnlyList<FirmwareSlotState> Release(IReadOnlyList<FirmwareSlotState> slots, string modeId)
	{
		ArgumentNullException.ThrowIfNull(slots, "slots");
		return slots.Select((FirmwareSlotState s) => (!string.Equals(s.OwnerModeId, modeId, StringComparison.Ordinal)) ? s : new FirmwareSlotState(s.Index, null, null, s.LastUsedTicks)).ToList();
	}

	internal static string OwnerKey(int slotIndex)
	{
		return "perf_slot_owner_" + slotIndex.ToString(CultureInfo.InvariantCulture);
	}

	internal static string SignatureKey(int slotIndex)
	{
		return "perf_slot_sig_" + slotIndex.ToString(CultureInfo.InvariantCulture);
	}

	internal static string LastUsedKey(int slotIndex)
	{
		return "perf_slot_used_" + slotIndex.ToString(CultureInfo.InvariantCulture);
	}
}
