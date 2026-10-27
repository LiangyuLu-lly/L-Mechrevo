using System;
using System.Collections.Generic;
using Probe;

namespace MechrevoLite.Hardware;

public static class ModelSupport
{
	private static readonly Lazy<ModelRegistryData> RegistryData = new Lazy<ModelRegistryData>(ModelRegistryData.Load);

	public static SupportDecision Determine(IEcReadTransport transport, bool serviceServed)
	{
		return Determine(ModelRegistry.Read(transport), RegistryData.Value.PlatformCodeSet, serviceServed);
	}

	public static SupportDecision Determine(ModelIdentity identity, IReadOnlySet<string> supportedCodes, string? corroboratingBiosProjectId, bool serviceServed)
	{
		return Determine(identity, supportedCodes, serviceServed);
	}

	public static SupportDecision Determine(ModelIdentity identity, IReadOnlySet<string> supportedCodes, bool serviceServed)
	{
		ArgumentNullException.ThrowIfNull(identity, "identity");
		ArgumentNullException.ThrowIfNull(supportedCodes, "supportedCodes");
		if (!identity.IsParsed)
		{
			if (!serviceServed)
			{
				return SupportDecision.Unparsable();
			}
			string biosProjectId = identity.BiosProjectId;
			return SupportDecision.Supported((biosProjectId != null && biosProjectId.Length > 0 && biosProjectId != "Unknown") ? biosProjectId : "GCU");
		}
		if (!serviceServed)
		{
			return SupportDecision.NotInSet(identity.ProjectId);
		}
		return SupportDecision.Supported(identity.ProjectId);
	}
}
