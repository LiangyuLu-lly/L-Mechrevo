namespace MechrevoLite.Diagnostics;

internal sealed record VerifyResult(string Group, string Name, VerifyOutcome Outcome, string Detail);
