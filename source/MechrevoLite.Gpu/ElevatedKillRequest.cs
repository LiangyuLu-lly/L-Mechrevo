using System.Collections.Generic;

namespace MechrevoLite.Gpu;

internal sealed record ElevatedKillRequest(string Token, IReadOnlyList<ElevatedKillTarget> Targets);
