namespace MechrevoLite.Gpu;

public readonly record struct GpuRouteReadback(PanelAdapterKind Panel, DgpuPresence Dgpu)
{
	public GpuRoute Route => GpuRouteInference.Infer(Panel, Dgpu);

	public static readonly GpuRouteReadback Unavailable = new GpuRouteReadback(PanelAdapterKind.Unknown, DgpuPresence.Unknown);

	public override string ToString()
	{
		return $"panel={Panel} dgpu={Dgpu} route={Route}";
	}
}
