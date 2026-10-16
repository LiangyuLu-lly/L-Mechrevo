namespace MechrevoLite.Mode;

public sealed record PerfApplyStep(PerfApplyStepKind Kind, string? WireKey = null, string? Value = null, int Number = 0, bool IsCpuCurve = false, int[]? Curve = null, PerfApplyVerify Verify = PerfApplyVerify.ServiceEcho, (int Core, int Memory)? GpuOffsets = null)
{
	public override string ToString()
	{
		return Kind switch
		{
			PerfApplyStepKind.WriteFirmwareField => $"{Kind}:{WireKey}={Value}", 
			PerfApplyStepKind.WriteFanCurve => $"{Kind}:{(IsCpuCurve ? "CPU" : "GPU")}", 
			PerfApplyStepKind.ApplyGpuOverclock => $"{Kind}:{Number}:{GpuOffsets?.Core}/{GpuOffsets?.Memory}", 
			_ => $"{Kind}:{Number}", 
		};
	}
}
