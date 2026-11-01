namespace MechrevoLite.Mode;

public sealed record FirmwareSlotState(int Index, string? OwnerModeId, string? Signature, long LastUsedTicks)
{
	public bool IsFree => string.IsNullOrEmpty(OwnerModeId);
}
