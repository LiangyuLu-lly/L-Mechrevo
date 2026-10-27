namespace MechrevoLite.Mode;

public sealed record PerfApplyStepOutcome(PerfApplyStep Step, PerfApplyResult Result, string Detail = "")
{
	public override string ToString()
	{
		return Step?.ToString() + " -> " + Result.ToString() + (string.IsNullOrEmpty(Detail) ? "" : (" (" + Detail + ")"));
	}
}
