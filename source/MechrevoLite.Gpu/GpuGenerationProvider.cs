using System;
using System.Collections.Generic;

namespace MechrevoLite.Gpu;

public static class GpuGenerationProvider
{
	private static readonly object Sync = new object();

	private static DgpuIdentity? _current;

	internal static Func<DgpuIdentity>? Override { get; set; }

	internal static Func<IReadOnlyList<GpuAdapter>>? AdapterOverride { get; set; }

	internal static IDgpuGenerationStorage Storage { get; set; } = new AppConfigDgpuGenerationStorage();

	public static DgpuIdentity Current()
	{
		lock (Sync)
		{
			Func<DgpuIdentity> func = Override;
			if (func != null)
			{
				return func();
			}
			return _current ?? (_current = Resolve());
		}
	}

	public static void Invalidate()
	{
		lock (Sync)
		{
			_current = null;
		}
	}

	private static DgpuIdentity Resolve()
	{
		Func<IReadOnlyList<GpuAdapter>> adapterOverride = AdapterOverride;
		IReadOnlyList<GpuAdapter> adapters;
		bool enumerationAvailable;
		if (adapterOverride != null)
		{
			adapters = adapterOverride();
			enumerationAvailable = true;
		}
		else
		{
			adapters = GpuAdapterReader.ReadFromRegistry(out enumerationAvailable);
		}
		string text = DgpuGenerationStore.Fingerprint(adapters);
		DgpuIdentity dgpuIdentity = DgpuGenerationStore.Load(Storage);
		if ((object)dgpuIdentity != null && !DgpuGenerationStore.ShouldReResolve(Storage.Get("dgpu_generation_fingerprint"), text))
		{
			return dgpuIdentity;
		}
		DgpuIdentity dgpuIdentity2 = DgpuGenerationProbe.Resolve(adapters, enumerationAvailable);
		DgpuGenerationStore.Save(Storage, dgpuIdentity2, text);
		Logger.WriteLine($"dGPU generation resolved: {dgpuIdentity2.Generation} source={dgpuIdentity2.Source} hasDgpu={dgpuIdentity2.HasDgpu}");
		return dgpuIdentity2;
	}
}
