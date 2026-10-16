namespace MechrevoLite.Fan;

public sealed record FanTableResolution(FanTableResolutionKind Kind, string Model, string? Directory, bool InSupportedSet, string Reason)
{
	public bool IsDirectory => Kind == FanTableResolutionKind.Directory;
}
