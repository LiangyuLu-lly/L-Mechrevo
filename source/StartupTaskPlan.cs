using System;

internal readonly record struct StartupTaskPlan(TimeSpan TriggerDelay, int RestartCount, TimeSpan RestartInterval);
