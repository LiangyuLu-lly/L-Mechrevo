using System.Collections.Generic;

namespace MechrevoLite.Gpu;

internal sealed record ElevatedKillResult(bool Success, IReadOnlyList<ElevatedKillTargetResult> Results);
