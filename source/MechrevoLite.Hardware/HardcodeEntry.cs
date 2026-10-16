using System.Collections.Generic;

namespace MechrevoLite.Hardware;

public sealed record HardcodeEntry(string Item, string Assumption, HardcodeDisposition Disposition, IReadOnlyList<int> Lines, string Note);
