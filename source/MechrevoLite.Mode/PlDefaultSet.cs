namespace MechrevoLite.Mode;

public sealed record PlDefaultSet(int? Pl1, int? Pl2, int? Pl4, int? TccOffset)
{
	public bool IsAvailable
	{
		get
		{
			if (Pl1.HasValue && Pl2.HasValue && Pl4.HasValue)
			{
				return TccOffset.HasValue;
			}
			return false;
		}
	}

	public static readonly PlDefaultSet Unavailable = new PlDefaultSet(null, null, null, null);
}
