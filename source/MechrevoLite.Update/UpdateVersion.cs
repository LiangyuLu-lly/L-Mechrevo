using System;
using System.Collections.Generic;
using System.Text;

namespace MechrevoLite.Update;

internal static class UpdateVersion
{
	internal static IReadOnlyList<int> NumberSegments(string? version)
	{
		List<int> list = new List<int>();
		if (string.IsNullOrWhiteSpace(version))
		{
			return list;
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (char c in version)
		{
			if (char.IsAsciiDigit(c))
			{
				stringBuilder.Append(c);
			}
			else if (stringBuilder.Length != 0)
			{
				list.Add(ParseSegment(stringBuilder.ToString()));
				stringBuilder.Clear();
			}
		}
		if (stringBuilder.Length > 0)
		{
			list.Add(ParseSegment(stringBuilder.ToString()));
		}
		return list;
	}

	private static int ParseSegment(string digits)
	{
		if (!int.TryParse(digits, out var result))
		{
			return int.MaxValue;
		}
		return result;
	}

	internal static int Compare(string? left, string? right)
	{
		IReadOnlyList<int> readOnlyList = NumberSegments(left);
		IReadOnlyList<int> readOnlyList2 = NumberSegments(right);
		int num = Math.Min(readOnlyList.Count, readOnlyList2.Count);
		for (int i = 0; i < num; i++)
		{
			int num2 = readOnlyList[i].CompareTo(readOnlyList2[i]);
			if (num2 != 0)
			{
				return num2;
			}
		}
		if (readOnlyList.Count == readOnlyList2.Count)
		{
			return 0;
		}
		IReadOnlyList<int> readOnlyList3 = ((readOnlyList.Count > readOnlyList2.Count) ? readOnlyList : readOnlyList2);
		for (int j = num; j < readOnlyList3.Count; j++)
		{
			if (readOnlyList3[j] != 0)
			{
				if (readOnlyList.Count <= readOnlyList2.Count)
				{
					return -1;
				}
				return 1;
			}
		}
		return 0;
	}

	internal static bool IsNewer(string? candidate, string? current)
	{
		return Compare(candidate, current) > 0;
	}
}
