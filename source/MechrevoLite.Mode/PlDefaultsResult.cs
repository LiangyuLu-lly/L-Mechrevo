namespace MechrevoLite.Mode;

public sealed record PlDefaultsResult(PlDefaultMode? Mode, PlDefaultSet Values, bool Editable, string Reason)
{
	public static PlDefaultsResult Unavailable(PlDefaultMode? mode, string reason)
	{
		return new PlDefaultsResult(mode, PlDefaultSet.Unavailable, Editable: false, reason);
	}
}
