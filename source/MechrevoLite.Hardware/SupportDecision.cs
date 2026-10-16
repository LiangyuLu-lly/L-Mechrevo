namespace MechrevoLite.Hardware;

public sealed record SupportDecision(bool IsSupported, SupportReason Reason, string ProjectId)
{
	public static SupportDecision Supported(string projectId)
	{
		return new SupportDecision(IsSupported: true, SupportReason.Ok, projectId);
	}

	public static SupportDecision Unparsable()
	{
		return new SupportDecision(IsSupported: false, SupportReason.Unparsable, "Unknown");
	}

	public static SupportDecision NotInSet(string projectId)
	{
		return new SupportDecision(IsSupported: false, SupportReason.NotInSet, projectId);
	}
}
