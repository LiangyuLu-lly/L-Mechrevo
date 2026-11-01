namespace PawnIO;

public sealed record PowerLimits(float Stapm, float Fast, float Slow, float TctlTemp, float? ApuSlow = null);
