using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MechrevoLite.Mode;

public static class PerfModeCollection
{
	public static readonly PerfModeKind[] BuiltInOrder = new PerfModeKind[4]
	{
		PerfModeKind.Silent,
		PerfModeKind.Balanced,
		PerfModeKind.SilentTurbo,
		PerfModeKind.Turbo
	};

	public const int MaxCustomModes = 12;

	public const int MaxUserNameLength = 12;

	public static int NextCustomOrdinal(IEnumerable<PerfModeDefinition> existing)
	{
		ArgumentNullException.ThrowIfNull(existing, "existing");
		HashSet<int> hashSet = new HashSet<int>();
		foreach (PerfModeDefinition item in existing)
		{
			if (item.IsCustom && TryParseCustomOrdinal(item.Id, out var ordinal))
			{
				hashSet.Add(ordinal);
			}
		}
		for (int i = 1; i <= 12; i++)
		{
			if (!hashSet.Contains(i))
			{
				return i;
			}
		}
		return -1;
	}

	internal static bool TryParseCustomOrdinal(string id, out int ordinal)
	{
		ordinal = -1;
		if (string.IsNullOrEmpty(id) || !id.StartsWith("custom", StringComparison.Ordinal))
		{
			return false;
		}
		return int.TryParse(id.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out ordinal);
	}

	public static bool CanAdd(IReadOnlyCollection<PerfModeDefinition> existing)
	{
		ArgumentNullException.ThrowIfNull(existing, "existing");
		return existing.Count((PerfModeDefinition m) => m.IsCustom) < 12;
	}

	public static bool CanRemove(IReadOnlyCollection<PerfModeDefinition> existing, string id)
	{
		ArgumentNullException.ThrowIfNull(existing, "existing");
		PerfModeDefinition perfModeDefinition = existing.FirstOrDefault((PerfModeDefinition m) => m.Id == id);
		if ((object)perfModeDefinition == null || !perfModeDefinition.IsCustom)
		{
			return false;
		}
		return existing.Count((PerfModeDefinition m) => m.IsCustom) > 1;
	}

	public static string? SanitizeUserName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		string text = name.Trim();
		if (text.Length > 16)
		{
			text = text.Substring(0, 16);
		}
		return text;
	}
}
