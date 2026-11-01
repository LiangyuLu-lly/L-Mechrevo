namespace MechrevoLite.Mode;

public sealed record FirmwareSlotPlan(int SlotIndex, bool NeedsParameterWrite, string Reason);
