using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Mode;

public interface IPerfModeBackend
{
	Task<bool> SwitchBuiltInAsync(PerfModeKind kind, CancellationToken ct);

	Task<bool> SwitchTurboSubModeAsync(bool silent, CancellationToken ct);

	Task<bool> SwitchFirmwareSlotAsync(int slotIndex, CancellationToken ct);

	Task<bool> WriteDetailFieldAsync(string wireKey, string value, CancellationToken ct);

	Task<bool> WriteFanCurveAsync(bool cpu, int[] duty, CancellationToken ct);

	Task<bool> ApplyGpuOverclockAsync(bool on, int core, int memory, CancellationToken ct);

	Task<bool> SetSlotNameAsync(string name, CancellationToken ct);

	Task<bool> SetFanBoostAsync(bool on, CancellationToken ct);

	Task<bool> SetRefreshRateAsync(int hz, CancellationToken ct);

	bool SetPowerPlan(string guid);

	bool SetCpuBoost(int index);

	bool SetPowerOverlay(int mode);

	Task DelayAsync(int milliseconds, CancellationToken ct);

	Task<bool?> VerifyAsync(PerfApplyStep step, CancellationToken ct);
}
