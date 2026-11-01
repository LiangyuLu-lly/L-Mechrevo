namespace MechrevoLite.Gpu;

public sealed record DgpuIdentity(DgpuGenerationKind Generation, DgpuProbeSource Source, bool HasDgpu, string? MarketingName, string? DeviceId)
{
	public bool IsResolved
	{
		get
		{
			DgpuGenerationKind generation = Generation;
			if ((uint)(generation - 2) <= 2u)
			{
				return true;
			}
			return false;
		}
	}

	public static readonly DgpuIdentity Unknown = new DgpuIdentity(DgpuGenerationKind.Unknown, DgpuProbeSource.None, HasDgpu: false, null, null);

	public static readonly DgpuIdentity NoDgpu = new DgpuIdentity(DgpuGenerationKind.NoDgpu, DgpuProbeSource.None, HasDgpu: false, null, null);
}
