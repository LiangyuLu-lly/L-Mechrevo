using System.Collections.Generic;

namespace MechrevoLite.Hardware;

public sealed record GpuRouteCommand(string Topic, string Action, Dictionary<string, object> Payload, int DelayBeforeMilliseconds);
