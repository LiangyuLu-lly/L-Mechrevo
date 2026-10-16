using System;
using System.Collections.Generic;

namespace MechrevoLite.Hardware;

public static class ModelOverrideStateMachine
{
	public const string ModeKey = "model_override_mode";

	public const string ModelKey = "model_override";

	public const string OverrideVariable = "LMECHREVO_MODEL_OVERRIDE";

	private static readonly Lazy<IReadOnlySet<string>> SupportedCodes = new Lazy<IReadOnlySet<string>>(() => ModelRegistryData.Load().PlatformCodeSet);

	public static SupportDecision ValidateManual(string? model)
	{
		if (string.IsNullOrWhiteSpace(model))
		{
			return SupportDecision.Unparsable();
		}
		string text = model.Trim();
		if (!SupportedCodes.Value.Contains(text) && !ModelRegistry.IsKnownProjectName(text))
		{
			return SupportDecision.NotInSet(text);
		}
		return SupportDecision.Supported(text);
	}

	public static ModelOverrideMode ParseMode(string? value)
	{
		switch (value?.Trim().ToLowerInvariant())
		{
		case "manualpinned":
		case "pinned":
			return ModelOverrideMode.ManualPinned;
		case "manualfallback":
		case "fallback":
			return ModelOverrideMode.ManualFallback;
		default:
			return ModelOverrideMode.Auto;
		}
	}

	public static ModelOverrideDecision Decide(ModelOverrideMode mode, SupportDecision auto, SupportDecision manual)
	{
		ArgumentNullException.ThrowIfNull(auto, "auto");
		ArgumentNullException.ThrowIfNull(manual, "manual");
		if (mode == ModelOverrideMode.Auto)
		{
			return FollowAuto(mode, auto);
		}
		if (!manual.IsSupported)
		{
			return FollowAuto(mode, auto)with
			{
				Reason = ModelOverrideReason.ManualRejected
			};
		}
		if (mode == ModelOverrideMode.ManualPinned)
		{
			return new ModelOverrideDecision(mode, manual.ProjectId, ManualApplied: true, ModelOverrideReason.ManualAccepted);
		}
		if (!auto.IsSupported)
		{
			return new ModelOverrideDecision(mode, manual.ProjectId, ManualApplied: true, ModelOverrideReason.ManualAccepted);
		}
		return FollowAuto(mode, auto);
	}

	public static ModelOverrideDecision EvaluateConfigured(SupportDecision auto)
	{
		return Decide(ParseMode(AppConfig.GetString("model_override_mode")), auto, ValidateManual(ReadConfiguredManualModel()));
	}

	public static ModelOverrideDecision Evaluate(SupportDecision auto)
	{
		string text = EnvironmentOverride();
		if (text != null)
		{
			return Decide(ModelOverrideMode.ManualPinned, auto, ValidateManual(text));
		}
		return EvaluateConfigured(auto);
	}

	public static string? EnvironmentOverride()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("LMECHREVO_MODEL_OVERRIDE");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			return environmentVariable.Trim();
		}
		return null;
	}

	public static string? ReadConfiguredManualModel()
	{
		SupportDecision supportDecision = ValidateManual(AppConfig.GetString("model_override"));
		if (!supportDecision.IsSupported)
		{
			return null;
		}
		return supportDecision.ProjectId;
	}

	public static ModelOverrideMode ReadConfiguredMode()
	{
		return ParseMode(AppConfig.GetString("model_override_mode"));
	}

	public static void SetMode(ModelOverrideMode mode)
	{
		AppConfig.Set("model_override_mode", mode.ToString());
	}

	public static bool TrySetManual(string? model, out SupportDecision decision)
	{
		decision = ValidateManual(model);
		if (!decision.IsSupported)
		{
			return false;
		}
		AppConfig.Set("model_override", decision.ProjectId);
		return true;
	}

	private static ModelOverrideDecision FollowAuto(ModelOverrideMode mode, SupportDecision auto)
	{
		if (!auto.IsSupported)
		{
			return new ModelOverrideDecision(mode, null, ManualApplied: false, ModelOverrideReason.AutoUnsupported);
		}
		return new ModelOverrideDecision(mode, auto.ProjectId, ManualApplied: false, ModelOverrideReason.AutoOk);
	}
}
