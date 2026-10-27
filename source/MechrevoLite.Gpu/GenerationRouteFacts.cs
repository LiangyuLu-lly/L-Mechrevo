using System.Collections.Generic;

namespace MechrevoLite.Gpu;

public sealed record GenerationRouteFacts(DgpuGenerationKind Generation, RouteCell ConsoleProtocol, RouteCell ServiceWritePath, RouteCell IgpuOnly, RouteCell Restart, RouteCell HotSwap, IReadOnlyList<string> ConsoleActions, IReadOnlyList<OemDisplayModeEncoding> OemDisplayMode)
{
	public bool? ThreeMode { get; init; }

	public IReadOnlyList<string> Actions => ConsoleActions;
}
