using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MechrevoLite.Hardware;

public sealed class ModelRegistryData
{
	public const string ResourceName = "LMechrevo.model-registry.json";

	public const int SupportedSchemaVersion = 1;

	public const int PlatformCodeCount = 24;

	private static readonly IReadOnlyDictionary<string, int> ExpectedVendorListCounts = new Dictionary<string, int>(StringComparer.Ordinal)
	{
		["commercial"] = 28,
		["commercialHave20Db"] = 14,
		["singleColorKeyboard"] = 9,
		["nonNumpad"] = 3,
		["idpIdy"] = 3
	};

	private static readonly IReadOnlyDictionary<string, int> ExpectedGpuSkuCounts = new Dictionary<string, int>(StringComparer.Ordinal)
	{
		["gn20"] = 10,
		["gn21"] = 19
	};

	private static readonly HashSet<string> ForbiddenFieldNames = new HashSet<string>(StringComparer.Ordinal) { "capabilities", "capability", "capabilitybits", "generation", "dgpugeneration", "generationsource" };

	private static readonly int[] ExpectedDgpuGenerations = new int[3] { 30, 40, 50 };

	public IReadOnlyList<PlatformCodeIdentity> PlatformCodes { get; }

	public IReadOnlyDictionary<int, string> BiosProjectBytes { get; }

	public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorLists { get; }

	public IReadOnlyDictionary<string, IReadOnlyList<string>> GpuSkus { get; }

	public IReadOnlyList<FanTableGrouping> FanTableGroups { get; }

	public IReadOnlyList<int> DgpuGenerations { get; }

	public IReadOnlySet<string> PlatformCodeSet { get; }

	private ModelRegistryData(IReadOnlyList<PlatformCodeIdentity> platformCodes, IReadOnlyDictionary<int, string> biosProjectBytes, IReadOnlyDictionary<string, IReadOnlyList<string>> vendorLists, IReadOnlyDictionary<string, IReadOnlyList<string>> gpuSkus, IReadOnlyList<FanTableGrouping> fanTableGroups, IReadOnlyList<int> dgpuGenerations)
	{
		PlatformCodes = platformCodes;
		BiosProjectBytes = biosProjectBytes;
		VendorLists = vendorLists;
		GpuSkus = gpuSkus;
		FanTableGroups = fanTableGroups;
		DgpuGenerations = dgpuGenerations;
		PlatformCodeSet = new HashSet<string>(platformCodes.Select((PlatformCodeIdentity code) => code.Code), StringComparer.Ordinal);
	}

	public static ModelRegistryData Load()
	{
		using Stream stream = typeof(ModelRegistryData).Assembly.GetManifestResourceStream("LMechrevo.model-registry.json");
		if (stream == null)
		{
			throw new ModelRegistryDataException("embedded resource 'LMechrevo.model-registry.json' is missing");
		}
		using StreamReader streamReader = new StreamReader(stream);
		return Parse(streamReader.ReadToEnd());
	}

	public static ModelRegistryData Parse(string json)
	{
		JsonDocument jsonDocument;
		try
		{
			jsonDocument = JsonDocument.Parse(json);
		}
		catch (JsonException ex)
		{
			throw new ModelRegistryDataException("registry is not valid JSON: " + ex.Message);
		}
		using (jsonDocument)
		{
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object)
			{
				throw new ModelRegistryDataException("registry root must be an object");
			}
			RejectForbiddenFields(rootElement, "$");
			int num = RequireInt(rootElement, "schemaVersion");
			if (num != 1)
			{
				throw new ModelRegistryDataException($"unsupported schemaVersion {num} (want {1})");
			}
			return new ModelRegistryData(ReadPlatformCodes(rootElement), ReadBiosProjectBytes(rootElement), ReadStringLists(rootElement, "vendorLists", ExpectedVendorListCounts), ReadStringLists(rootElement, "gpuSkus", ExpectedGpuSkuCounts), ReadFanTableGroups(rootElement), ReadDgpuGenerations(rootElement));
		}
	}

	public bool IsPlatformCode(string code)
	{
		return PlatformCodeSet.Contains(code);
	}

	private static void RejectForbiddenFields(JsonElement element, string path)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item in element.EnumerateObject())
			{
				string text = item.Name.ToLowerInvariant();
				bool flag = text.Contains("bios") && text.Contains("generation");
				if (ForbiddenFieldNames.Contains(text) | flag)
				{
					throw new ModelRegistryDataException($"forbidden field '{item.Name}' at {path}: the identity registry carries no capability bits and no per-code generation");
				}
				RejectForbiddenFields(item.Value, path + "." + item.Name);
			}
			return;
		}
		if (element.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item2 in element.EnumerateArray())
		{
			RejectForbiddenFields(item2, path + "[]");
		}
	}

	private static List<PlatformCodeIdentity> ReadPlatformCodes(JsonElement root)
	{
		JsonElement jsonElement = Require(root, "platformCodes", JsonValueKind.Array);
		List<PlatformCodeIdentity> list = new List<PlatformCodeIdentity>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		HashSet<int> hashSet2 = new HashSet<int>();
		foreach (JsonElement item in jsonElement.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				throw new ModelRegistryDataException("platformCodes entries must be objects");
			}
			string text = ((item.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String) ? value.GetString() : null);
			if (string.IsNullOrWhiteSpace(text))
			{
				throw new ModelRegistryDataException("platformCodes entry has no 'code'");
			}
			if (!hashSet.Add(text))
			{
				throw new ModelRegistryDataException("duplicate platform code '" + text + "'");
			}
			int? projectId = null;
			if (item.TryGetProperty("projectId", out var value2) && value2.ValueKind != JsonValueKind.Null)
			{
				if (value2.ValueKind != JsonValueKind.Number || !value2.TryGetInt32(out var value3))
				{
					throw new ModelRegistryDataException("platformCodes '" + text + "' has a non-integer projectId");
				}
				if (!hashSet2.Add(value3))
				{
					throw new ModelRegistryDataException($"duplicate projectId {value3}");
				}
				projectId = value3;
			}
			list.Add(new PlatformCodeIdentity(text, projectId));
		}
		if (list.Count != 24)
		{
			throw new ModelRegistryDataException($"platformCodes must cover the {24} fan-table codes, got {list.Count}");
		}
		return list;
	}

	private static IReadOnlyDictionary<int, string> ReadBiosProjectBytes(JsonElement root)
	{
		JsonElement jsonElement = Require(root, "biosProjectBytes", JsonValueKind.Array);
		Dictionary<int, string> dictionary = new Dictionary<int, string>();
		foreach (JsonElement item in jsonElement.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				throw new ModelRegistryDataException("biosProjectBytes entries must be objects");
			}
			if (!item.TryGetProperty("masked", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var value2))
			{
				throw new ModelRegistryDataException("biosProjectBytes entry has no integer 'masked'");
			}
			string value3 = ((item.TryGetProperty("name", out var value4) && value4.ValueKind == JsonValueKind.String) ? value4.GetString() : null);
			if (string.IsNullOrWhiteSpace(value3))
			{
				throw new ModelRegistryDataException($"biosProjectBytes entry {value2} has no 'name'");
			}
			if (!dictionary.TryAdd(value2, value3))
			{
				throw new ModelRegistryDataException($"duplicate biosProjectBytes masked value {value2}");
			}
		}
		if (dictionary.Count == 0)
		{
			throw new ModelRegistryDataException("biosProjectBytes is empty");
		}
		return dictionary;
	}

	private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadStringLists(JsonElement root, string name, IReadOnlyDictionary<string, int> expectedCounts)
	{
		JsonElement parent = Require(root, name, JsonValueKind.Object);
		Dictionary<string, IReadOnlyList<string>> dictionary = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
		foreach (var (text2, num2) in expectedCounts)
		{
			if (!parent.TryGetProperty(text2, out var _))
			{
				throw new ModelRegistryDataException(name + "." + text2 + " is missing");
			}
			List<string> list = ReadStringArray(parent, text2, name);
			if (list.Count != num2)
			{
				throw new ModelRegistryDataException($"{name}.{text2} must have {num2} members, got {list.Count}");
			}
			dictionary[text2] = list;
		}
		return dictionary;
	}

	private static List<FanTableGrouping> ReadFanTableGroups(JsonElement root)
	{
		JsonElement jsonElement = Require(root, "fanTableGroups", JsonValueKind.Array);
		List<FanTableGrouping> list = new List<FanTableGrouping>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		int num = 0;
		foreach (JsonElement item in jsonElement.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				throw new ModelRegistryDataException("fanTableGroups entries must be objects");
			}
			string text = ((item.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String) ? value.GetString() : null);
			if (string.IsNullOrWhiteSpace(text))
			{
				throw new ModelRegistryDataException("fanTableGroups entry has no 'id'");
			}
			if (!hashSet.Add(text))
			{
				throw new ModelRegistryDataException("duplicate fanTableGroups id '" + text + "'");
			}
			List<string> list2 = ReadStringArray(item, "keys", "fanTableGroups[" + text + "]");
			List<string> list3 = ReadStringArray(item, "models", "fanTableGroups[" + text + "]");
			if (list2.Count == 0 || list3.Count == 0)
			{
				throw new ModelRegistryDataException("fanTableGroups[" + text + "] must have keys and models");
			}
			if (list2.Distinct(StringComparer.Ordinal).Count() != list2.Count)
			{
				throw new ModelRegistryDataException("fanTableGroups[" + text + "] has duplicate keys");
			}
			num += list3.Count;
			list.Add(new FanTableGrouping(text, list2, list3));
		}
		if (list.Count == 0)
		{
			throw new ModelRegistryDataException("fanTableGroups is empty");
		}
		if (num != 24)
		{
			throw new ModelRegistryDataException($"fanTableGroups models must sum to {24}, got {num}");
		}
		return list;
	}

	private static IReadOnlyList<int> ReadDgpuGenerations(JsonElement root)
	{
		return ReadIntArray(root, "dgpuGenerations");
	}

	private static IReadOnlyList<int> ReadIntArray(JsonElement root, string name)
	{
		JsonElement jsonElement = Require(root, name, JsonValueKind.Array);
		List<int> list = new List<int>();
		foreach (JsonElement item in jsonElement.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var value))
			{
				throw new ModelRegistryDataException(name + " must contain only integers");
			}
			list.Add(value);
		}
		if (!list.OrderBy((int result) => result).SequenceEqual(ExpectedDgpuGenerations))
		{
			throw new ModelRegistryDataException($"{name} must be the axis-2 domain {{{string.Join(",", ExpectedDgpuGenerations)}}}, got {{{string.Join(",", list)}}}");
		}
		return list;
	}

	private static List<string> ReadStringArray(JsonElement parent, string name, string context)
	{
		JsonElement jsonElement = Require(parent, name, JsonValueKind.Array);
		List<string> list = new List<string>();
		foreach (JsonElement item in jsonElement.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.String)
			{
				throw new ModelRegistryDataException(context + "." + name + " must contain only strings");
			}
			string text = item.GetString();
			if (string.IsNullOrWhiteSpace(text))
			{
				throw new ModelRegistryDataException(context + "." + name + " contains an empty entry");
			}
			list.Add(text);
		}
		return list;
	}

	private static int RequireInt(JsonElement parent, string name)
	{
		if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var value2))
		{
			throw new ModelRegistryDataException("'" + name + "' is missing or not an integer");
		}
		return value2;
	}

	private static JsonElement Require(JsonElement parent, string name, JsonValueKind kind)
	{
		if (!parent.TryGetProperty(name, out var value))
		{
			throw new ModelRegistryDataException("'" + name + "' is missing");
		}
		if (value.ValueKind != kind)
		{
			throw new ModelRegistryDataException($"'{name}' must be a {kind}, got {value.ValueKind}");
		}
		return value;
	}
}
