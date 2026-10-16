using System;

namespace MechrevoLite;

internal static class UiLanguage
{
	public const string Chinese = "zh-CN";

	public const string English = "en";

	public const string ConfigKey = "language";

	public static string Normalize(string? stored, string? uiCultureName)
	{
		if (!string.IsNullOrWhiteSpace(stored))
		{
			if (stored.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
			{
				return "zh-CN";
			}
			if (stored.StartsWith("en", StringComparison.OrdinalIgnoreCase))
			{
				return "en";
			}
		}
		if (!string.IsNullOrWhiteSpace(uiCultureName) && uiCultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
		{
			return "zh-CN";
		}
		return "en";
	}

	public static int IndexOf(string code)
	{
		return string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
	}

	public static string CodeFromIndex(int index)
	{
		if (index != 1)
		{
			return "zh-CN";
		}
		return "en";
	}
}
