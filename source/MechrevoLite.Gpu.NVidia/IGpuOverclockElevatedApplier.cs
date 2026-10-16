using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Gpu.NVidia;

public interface IGpuOverclockElevatedApplier
{
	Task<GpuOverclockApplyResult> ApplyAsync(GpuOverclockApplyRequest request, CancellationToken cancellationToken);
}
