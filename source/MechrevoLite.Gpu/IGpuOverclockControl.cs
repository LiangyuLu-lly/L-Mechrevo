using System;

namespace MechrevoLite.Gpu;

public interface IGpuOverclockControl : IDisposable
{
	string Name { get; }

	bool IsAvailable { get; }

	GpuClockOffsetRange CoreOffset { get; }

	GpuClockOffsetRange MemoryOffset { get; }

	bool WritesRequireElevation => false;

	bool Refresh();

	bool SetCoreOffset(int value);

	bool SetMemoryOffset(int value);
}
