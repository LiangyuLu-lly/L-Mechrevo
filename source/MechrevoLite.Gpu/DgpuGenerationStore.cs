using System;
using System.Collections.Generic;
using System.Linq;

namespace MechrevoLite.Gpu;

public static class DgpuGenerationStore
{
	public const string GenerationKey = "dgpu_generation";

	public const string SourceKey = "dgpu_generation_source";

	public const string HasDgpuKey = "dgpu_has_dgpu";

	public const string FingerprintKey = "dgpu_generation_fingerprint";

	public static string Fingerprint(IReadOnlyList<GpuAdapter> adapters)
	{
		ArgumentNullException.ThrowIfNull(adapters, "adapters");
		return string.Join(";", adapters.Select((GpuAdapter adapter) => $"{adapter.Name}|{adapter.VendorId}|{adapter.DeviceId}").OrderBy((string entry) => entry, StringComparer.Ordinal));
	}

	public static bool ShouldReResolve(string? storedFingerprint, string currentFingerprint)
	{
		return !string.Equals(storedFingerprint ?? "", currentFingerprint ?? "", StringComparison.Ordinal);
	}

	public static DgpuIdentity? Load(IDgpuGenerationStorage storage)
	{
		ArgumentNullException.ThrowIfNull(storage, "storage");
		if (!Enum.TryParse<DgpuGenerationKind>(storage.Get("dgpu_generation"), out var result))
		{
			return null;
		}
		if ((uint)(result - 2) > 2u)
		{
			return null;
		}
		DgpuProbeSource source = (Enum.TryParse<DgpuProbeSource>(storage.Get("dgpu_generation_source"), out var result2) ? result2 : DgpuProbeSource.None);
		bool flag;
		switch (storage.Get("dgpu_has_dgpu"))
		{
		case "1":
		case "true":
		case "True":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool hasDgpu = flag;
		return new DgpuIdentity(result, source, hasDgpu, null, null);
	}

	public static void Save(IDgpuGenerationStorage storage, DgpuIdentity identity, string fingerprint)
	{
		ArgumentNullException.ThrowIfNull(storage, "storage");
		ArgumentNullException.ThrowIfNull(identity, "identity");
		storage.Set("dgpu_generation", identity.Generation.ToString());
		storage.Set("dgpu_generation_source", identity.Source.ToString());
		storage.Set("dgpu_has_dgpu", identity.HasDgpu ? "1" : "0");
		storage.Set("dgpu_generation_fingerprint", fingerprint ?? "");
	}
}
