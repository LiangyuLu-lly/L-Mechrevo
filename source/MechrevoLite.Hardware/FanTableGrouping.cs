using System.Collections.Generic;

namespace MechrevoLite.Hardware;

public sealed record FanTableGrouping(string Id, IReadOnlyList<string> Keys, IReadOnlyList<string> Models);
