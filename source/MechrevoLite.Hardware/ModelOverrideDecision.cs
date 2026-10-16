namespace MechrevoLite.Hardware;

public sealed record ModelOverrideDecision(ModelOverrideMode Mode, string? EffectiveModel, bool ManualApplied, ModelOverrideReason Reason);
