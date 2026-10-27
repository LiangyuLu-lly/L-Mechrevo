using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-09（kbdctrl）：<see cref="KeyboardCtrlPayload"/> 的**离线**自测。三段：
/// <list type="number">
/// <item><b>官方 7 段帧</b>：与 2026-10-09 蛟龙17 / Gen30 / 官方 30 系控制台抓包的首个 <c>SetEffectALL</c>
/// 实测帧**逐字段**对照（含对象键顺序与 ColorBuffer 段顺序）—— 这是**默认路径**，必须逐字节一致。</item>
/// <item><b>实验注入 1 段</b>：⚠️ 没有任何实测帧可对照（归档里 0 个 <c>ColorBlocks:1</c> 样本），
/// 所以只断言"我们约定的 1 段规则"（<c>ColorBlocks:1</c> + ColorBuffer 仅 <c>ID:0</c> + <c>ManualIndex1..6=null</c>）
/// 并印出样例 JSON。<b>「单分区该不该改段数」仍是待真机验证项，不是结论。</b></item>
/// <item><b>块数注入开关解析</b>：未设/<c>official</c>/<c>1</c>/<c>7</c>/乱填 → 各自的段数与来源。</item>
/// </list>
/// <para>✅ 不联网、不连 13688、不发任何 MQTT 帧、不读写设置、不碰硬件；只做内存里的字典组装与字符串比较。
/// 入口：<c>L-Mechrevo.exe --kbd-ctrl-payload [输出.txt]</c>。</para>
/// </summary>
internal static class KeyboardCtrlPayloadSelfTest
{
	/// <summary>实测帧原文（2026-10-09 抓包，首个 SetEffectALL，未改一字；ID2..6 按抓包展开为同红）。</summary>
	internal const string MeasuredSetEffectAll = """{"MqttID":null,"function":"SetEffectALL","mode":"Lighting","effect":"Single","light":"3","speed":"2","direction":null,"nv_save":null,"color":{"isCircular":true,"ColorBlocks":7,"ColorBuffer":[{"ID":0,"R":151,"G":243,"B":0},{"ID":1,"R":255,"G":0,"B":0},{"ID":2,"R":255,"G":0,"B":0},{"ID":3,"R":255,"G":0,"B":0},{"ID":4,"R":255,"G":0,"B":0},{"ID":5,"R":255,"G":0,"B":0},{"ID":6,"R":255,"G":0,"B":0}]},"backgroundcolor":0,"alphabet":null,"solution":0,"level":0,"powerstatus":1,"type":0,"MonochromeIndex":"2","ManualIndex1":"1","ManualIndex2":"5","ManualIndex3":"9","ManualIndex4":"13","ManualIndex5":"21","ManualIndex6":"25","ManualInterval":"6","BreathingIndex":"1","DefaultData":"","AuroraSwitch":false}""";

	/// <summary>
	/// 复现实测帧的输入：effect=Single / light=3 / speed=2 / powerstatus=1 / MonochromeIndex=2 /
	/// ManualIndex1..6=1,5,9,13,21,25 / ManualInterval=6 / BreathingIndex=1 / ColorBuffer[0]=151,243,0。
	/// </summary>
	internal static Dictionary<string, object> BuildMeasuredEquivalent(int colorBlocks)
	{
		int first = unchecked((int)0xFF97F300);
		return KeyboardCtrlPayload.BuildSetEffectAll(
			effect: "Single",
			light: 3,
			speed: 2,
			powerstatus: 1,
			monochromeIndex: 2,
			manualIndexes: KeyboardCtrlPayload.DefaultManualIndexes,
			manualInterval: 6,
			breathingIndex: 1,
			colorBufferArgb: KeyboardCtrlPayload.SingleColorBuffer(first, colorBlocks),
			colorBlocks: colorBlocks);
	}

	/// <summary>跑自测，返回可整段写进日志/文件的多行报告（首行是 OK/FAIL 汇总）。</summary>
	internal static string Run()
	{
		List<string> pass = new List<string>();
		List<string> fail = new List<string>();

		// ---- ① 默认路径：official 7 段，与实测帧逐字段对照 ----
		string official = KeyboardCtrlPayload.Serialize(BuildMeasuredEquivalent(KeyboardCtrlPayload.KeyboardColorBlocks));
		CompareJson("$", MeasuredSetEffectAll, official, pass, fail);
		Check(official == MeasuredSetEffectAll, "$ 逐字节：序列化文本 == 实测帧原文（不止逐字段）", pass, fail);

		// ---- ② 实验注入 1 段：约定规则断言（无实测帧可对照）----
		string oneBlock = KeyboardCtrlPayload.Serialize(BuildMeasuredEquivalent(KeyboardCtrlPayload.SingleZoneExperimentalColorBlocks));
		using (JsonDocument doc = JsonDocument.Parse(oneBlock))
		{
			JsonElement root = doc.RootElement;
			JsonElement color = root.GetProperty("color");
			Check(color.GetProperty("ColorBlocks").GetInt32() == 1, "[1段] color.ColorBlocks == 1", pass, fail);
			Check(color.GetProperty("ColorBuffer").GetArrayLength() == 1, "[1段] ColorBuffer 长度 == 1", pass, fail);
			JsonElement only = color.GetProperty("ColorBuffer")[0];
			Check(only.GetProperty("ID").GetInt32() == 0, "[1段] ColorBuffer[0].ID == 0", pass, fail);
			Check(only.GetProperty("R").GetInt32() == 151 && only.GetProperty("G").GetInt32() == 243 && only.GetProperty("B").GetInt32() == 0, "[1段] ColorBuffer[0] = 151,243,0（自定义色仍走该段 RGB）", pass, fail);
			Check(root.GetProperty("MonochromeIndex").GetString() == "2", "[1段] MonochromeIndex 仍为字符串 \"2\"（单分区颜色主驱动字段不变）", pass, fail);
			bool manualsNull = true;
			for (int i = 1; i <= 6; i++)
			{
				if (root.GetProperty("ManualIndex" + i).ValueKind != JsonValueKind.Null)
				{
					manualsNull = false;
				}
			}
			Check(manualsNull, "[1段] ManualIndex1..6 全为 null（多分区字段对单分区无意义）", pass, fail);
			Check(root.GetProperty("ManualInterval").GetString() == "6" && root.GetProperty("BreathingIndex").GetString() == "1", "[1段] ManualInterval/BreathingIndex 保持字符串原值（未改语义）", pass, fail);
			Check(string.Join(",", KeysOf(MeasuredSetEffectAll)) == string.Join(",", KeysOf(oneBlock)), "[1段] 顶层键集合与顺序与官方帧相同（只改 color 段数与 ManualIndex 取值）", pass, fail);
		}

		// ---- ③ 块数注入开关解析 ----
		(int BlockCount, string Source) d = KeyboardCtrlBlockCountSwitch.Resolve(null, null);
		Check(d.BlockCount == 7 && d.Source.Contains("默认"), "[开关] 未设 → 7 段（official 默认）", pass, fail);
		Check(KeyboardCtrlBlockCountSwitch.Resolve("official", null).BlockCount == 7, "[开关] official → 7 段", pass, fail);
		Check(KeyboardCtrlBlockCountSwitch.Resolve("1", null).BlockCount == 1, "[开关] 1 → 1 段（实验）", pass, fail);
		Check(KeyboardCtrlBlockCountSwitch.Resolve("7", null).BlockCount == 7, "[开关] 7 → 7 段", pass, fail);
		Check(KeyboardCtrlBlockCountSwitch.Resolve("banana", null).BlockCount == 7, "[开关] 乱填 → 回落 7 段", pass, fail);
		Check(KeyboardCtrlBlockCountSwitch.Resolve(null, "1").BlockCount == 1, "[开关] 用户级变量也认（进程级未设时）", pass, fail);
		Check(KeyboardCtrlBlockCountSwitch.Resolve("official", "1").BlockCount == 7, "[开关] 进程级优先于用户级", pass, fail);

		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"Keyboard/Ctrl 自测（离线，不发帧）：OK={pass.Count} FAIL={fail.Count}");
		sb.AppendLine("--- ① 默认路径 逐字段对照：官方实测帧 vs 本实现组装（official=7 段）---");
		foreach (string line in pass)
		{
			sb.AppendLine("[=] " + line);
		}
		foreach (string line in fail)
		{
			sb.AppendLine("[X] " + line);
		}
		sb.AppendLine("--- ① 组装结果（SetEffectALL，official 7 段 = 默认路径）---");
		sb.AppendLine(official);
		sb.AppendLine("--- ② 实验注入 1 段（LMECHREVO_KB_BLOCKS=1 才用；⚠️ 无实测对照，仅约定规则，待真机验证）---");
		sb.AppendLine("规则：ColorBlocks=1、ColorBuffer 仅 ID:0、颜色走该段 RGB 或 MonochromeIndex、ManualIndex1..6=null；其余字段与官方帧同值同序。");
		sb.AppendLine(oneBlock);
		sb.AppendLine("--- ③ 另外两条（无实测对照，只印形状）---");
		sb.AppendLine("SetPower(on)          = " + KeyboardCtrlPayload.Serialize(KeyboardCtrlPayload.BuildSetPower(on: true)));
		sb.AppendLine("SetLightingLevel(...) = " + KeyboardCtrlPayload.Serialize(KeyboardCtrlPayload.BuildSetLightingLevel("Single", 3, 2, 2, KeyboardCtrlPayload.DefaultManualIndexes, 6, 1, KeyboardCtrlPayload.SingleColorBuffer(unchecked((int)0xFF97F300), KeyboardCtrlPayload.KeyboardColorBlocks))));
		return sb.ToString();
	}

	private static void Check(bool ok, string description, List<string> pass, List<string> fail)
	{
		if (ok)
		{
			pass.Add(description);
		}
		else
		{
			fail.Add(description);
		}
	}

	private static List<string> KeysOf(string json)
	{
		List<string> keys = new List<string>();
		using JsonDocument doc = JsonDocument.Parse(json);
		foreach (JsonProperty p in doc.RootElement.EnumerateObject())
		{
			keys.Add(p.Name);
		}
		return keys;
	}

	/// <summary>递归比较两个 JSON 文本：对象比键顺序 + 逐键递归，数组比长度 + 逐项递归，值比原文。</summary>
	private static void CompareJson(string path, string expected, string actual, List<string> pass, List<string> fail)
	{
		using JsonDocument expectedDoc = JsonDocument.Parse(expected);
		using JsonDocument actualDoc = JsonDocument.Parse(actual);
		CompareElement(path, expectedDoc.RootElement, actualDoc.RootElement, pass, fail);
	}

	private static void CompareElement(string path, JsonElement expected, JsonElement actual, List<string> pass, List<string> fail)
	{
		if (expected.ValueKind == JsonValueKind.Object || actual.ValueKind == JsonValueKind.Object)
		{
			if (expected.ValueKind != JsonValueKind.Object || actual.ValueKind != JsonValueKind.Object)
			{
				fail.Add($"{path}: 类型不同 expected={expected.ValueKind} actual={actual.ValueKind}");
				return;
			}
			List<string> expectedKeys = new List<string>();
			foreach (JsonProperty p in expected.EnumerateObject())
			{
				expectedKeys.Add(p.Name);
			}
			List<string> actualKeys = new List<string>();
			foreach (JsonProperty p in actual.EnumerateObject())
			{
				actualKeys.Add(p.Name);
			}
			if (string.Join(",", expectedKeys) != string.Join(",", actualKeys))
			{
				fail.Add($"{path}: 键集合/顺序不同 expected=[{string.Join(",", expectedKeys)}] actual=[{string.Join(",", actualKeys)}]");
			}
			else
			{
				pass.Add($"{path}: 键集合与顺序一致（{expectedKeys.Count} 个键）");
			}
			foreach (JsonProperty p in expected.EnumerateObject())
			{
				if (actual.TryGetProperty(p.Name, out JsonElement child))
				{
					CompareElement(path + "." + p.Name, p.Value, child, pass, fail);
				}
			}
			return;
		}
		if (expected.ValueKind == JsonValueKind.Array || actual.ValueKind == JsonValueKind.Array)
		{
			if (expected.ValueKind != JsonValueKind.Array || actual.ValueKind != JsonValueKind.Array)
			{
				fail.Add($"{path}: 类型不同 expected={expected.ValueKind} actual={actual.ValueKind}");
				return;
			}
			if (expected.GetArrayLength() != actual.GetArrayLength())
			{
				fail.Add($"{path}: 数组长度不同 expected={expected.GetArrayLength()} actual={actual.GetArrayLength()}");
				return;
			}
			pass.Add($"{path}: 数组长度一致（{expected.GetArrayLength()}）");
			for (int i = 0; i < expected.GetArrayLength(); i++)
			{
				CompareElement(path + "[" + i + "]", expected[i], actual[i], pass, fail);
			}
			return;
		}
		string e = expected.GetRawText();
		string a = actual.GetRawText();
		if (e == a)
		{
			pass.Add($"{path} = {e}");
		}
		else
		{
			fail.Add($"{path}: expected={e} actual={a}");
		}
	}
}
