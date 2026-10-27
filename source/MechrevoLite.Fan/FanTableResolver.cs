using System;
using System.Collections.Generic;
using System.IO;
using MechrevoLite.Hardware;

namespace MechrevoLite.Fan;

public static class FanTableResolver
{
	public const string RootFolderName = "UserFanTables";

	public static FanTableResolution Resolve(string fanTablesRoot, ModelIdentity identity, IReadOnlySet<string> platformCodes)
	{
		ArgumentNullException.ThrowIfNull(fanTablesRoot, "fanTablesRoot");
		ArgumentNullException.ThrowIfNull(identity, "identity");
		ArgumentNullException.ThrowIfNull(platformCodes, "platformCodes");
		if (!identity.IsParsed)
		{
			return new FanTableResolution(FanTableResolutionKind.Unparsable, identity.ProjectId, null, InSupportedSet: false, "model identity is not parseable from the EC");
		}
		string projectId = identity.ProjectId;
		bool flag = platformCodes.Contains(projectId);
		string text = Path.Combine(fanTablesRoot, projectId);
		if (Directory.Exists(text))
		{
			return new FanTableResolution(FanTableResolutionKind.Directory, projectId, text, flag, "resolved to the model's own fan-table directory");
		}
		if (HasGenericFlatTables(fanTablesRoot))
		{
			return new FanTableResolution(FanTableResolutionKind.GenericFlatTables, projectId, fanTablesRoot, flag, "model has no fan-table directory; using the generic flat fan tables");
		}
		return new FanTableResolution(FanTableResolutionKind.EcdDefaults, projectId, null, flag, flag ? "model directory is missing; read the default fan table from the EC" : "model has no fan-table directory; read the default fan table from the EC");
	}

	internal static bool HasGenericFlatTables(string fanTablesRoot)
	{
		if (string.IsNullOrWhiteSpace(fanTablesRoot) || !Directory.Exists(fanTablesRoot))
		{
			return false;
		}
		if (File.Exists(Path.Combine(fanTablesRoot, "DefaultFanTable_Gaming.json")))
		{
			return File.Exists(Path.Combine(fanTablesRoot, "M1T1.json"));
		}
		return false;
	}
}
