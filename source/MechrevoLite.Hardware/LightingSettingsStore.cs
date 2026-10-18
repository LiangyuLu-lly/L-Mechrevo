using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace MechrevoLite.Hardware;

internal static class LightingSettingsStore
{
	internal const string ConfigDirectoryOverrideVariable = "LMECHREVO_LIGHT_CFG_DIR";

	// 2026-10-10（①「安装后灯被熄灭」）：用户"明确设置过灯光"的标记文件。
	// 安装程序会删掉整个 %APPDATA%\MechrevoLite（含键盘/灯带/Logo 三个 .cfg），卸载期也默认删；
	// 文件一旦缺失，旧逻辑就用「默认值 PowerOn=true」冒充用户意图 ⇒ 启动/唤醒时无条件重发一次灯光
	// （键盘 SetPower + 灯带/Logo/Hinge/Sync 的 SetPower/SetEffectALL），单分区机上就表现成
	// 「发了却不亮 / 被熄灭」。本标记用来把「配置缺失（=从未配置或被安装删掉）」与「用户明确选择」
	// 区分开：**只有用户真的做过灯光操作时才写它**，不做时间戳推断、不做内容猜测。
	// ⚠️ 文件名与三个 .cfg 同目录（同样支持 LMECHREVO_LIGHT_CFG_DIR 覆盖），便于安装/卸载一起带走。
	private const string UserIntentMarkerFileName = "lighting.user-configured";

	private static volatile bool _userLightingIntent;

	/// <summary>
	/// 2026-10-10（① 安装后灯被熄灭）：用户是否**明确设置过**灯光（情况 A / C 的前提）。
	/// <para>
	/// 情况 B（返回 false）：配置从来不存在，或被安装程序删掉 ⇒ 调用方**必须什么都不下发**
	/// —— 此时硬件上的灯光状态可能是官方控制台或 EC/固件设的，⛔ LM 不去覆盖才是对的。
	/// </para>
	/// <para>
	/// 迁移：老版本没有这个标记文件，但三个通道 .cfg **只可能由用户操作写入**
	/// （<see cref="Save"/> 的全部调用点都是 UI 事件）⇒ 存在即视为用户配置过，一次性补写标记，
	/// 保证既有用户仍是情况 A/C，行为不变。
	/// </para>
	/// </summary>
	internal static bool HasUserLightingIntent()
	{
		if (_userLightingIntent)
		{
			return true;
		}
		string markerPath = GetUserIntentMarkerPath();
		try
		{
			if (File.Exists(markerPath))
			{
				_userLightingIntent = true;
				return true;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("灯效用户意图标记读取失败: " + ex.Message);
		}
		if (HasLegacySavedChannelConfig())
		{
			MarkUserLightingIntent("旧版本遗留的灯效配置文件（迁移）");
			return true;
		}
		return false;
	}

	/// <summary>
	/// 2026-10-10（① 安装后灯被熄灭）：记录「用户明确做过灯光操作」。⛔ 只在用户真的点了/拖了灯光控件时调用，
	/// ⛔ 不在配置加载、启动恢复、空闲/离电熄灭等自动路径里调用。
	/// </summary>
	internal static void MarkUserLightingIntent(string reason)
	{
		if (_userLightingIntent)
		{
			return;
		}
		_userLightingIntent = true;
		try
		{
			string markerPath = GetUserIntentMarkerPath();
			Directory.CreateDirectory(Path.GetDirectoryName(markerPath));
			File.WriteAllText(markerPath, "configured=1" + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			Logger.WriteLine("灯效用户意图标记已写入（" + reason + "）：后续启动/唤醒恢复灯效");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("灯效用户意图标记写入失败（" + reason + "）: " + ex.Message);
		}
	}

	private static bool HasLegacySavedChannelConfig()
	{
		// 只查三个"仅由用户操作写入"的通道配置；⛔ 刻意不看 KeyboardRgb 的 rgb.cfg
		// —— 它在进程退出时会被无条件 SaveConfig() 写出来（Dispose），不能代表用户意图。
		string directory = GetConfigDirectory();
		return File.Exists(Path.Combine(directory, "keyboard.cfg"))
			|| File.Exists(Path.Combine(directory, "lightbar.cfg"))
			|| File.Exists(Path.Combine(directory, "logolight.cfg"));
	}

	internal static bool TryLoad(string topic, out LightChannelSettings settings)
	{
		settings = default;
		string path = GetPath(topic);
		if (!File.Exists(path))
		{
			return false;
		}
		try
		{
			settings = ParseLines(File.ReadLines(path), DefaultEffect(topic));
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Lighting settings read failed (" + topic + "): " + ex.Message);
			return false;
		}
	}

	internal static LightChannelSettings Load(string topic, string defaultEffect)
	{
		if (!TryLoad(topic, out var settings))
		{
			return new LightChannelSettings(defaultEffect, 4, 1, Color.White.ToArgb(), PowerOn: false);
		}
		return settings;
	}

	internal static void Save(string topic, LightChannelSettings settings)
	{
		string path = GetPath(topic);
		string text = path + ".tmp";
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(text, Serialize(settings), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			File.Move(text, path, overwrite: true);
			// 2026-10-10（① 安装后灯被熄灭）：Save 的全部调用点都是用户的灯光操作
			// （RgbForm / SettingsForm / LightForm 的点击、拖拽）⇒ 这里顺手记下"用户明确设置过"。
			MarkUserLightingIntent("Save " + topic);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Lighting settings write failed (" + topic + "): " + ex.Message);
		}
	}

	internal static void SavePower(string topic, bool powerOn)
	{
		LightChannelSettings lightChannelSettings = Load(topic, DefaultEffect(topic));
		Save(topic, lightChannelSettings with
		{
			PowerOn = powerOn,
			PowerExplicit = true
		});
	}

	internal static LightChannelSettings ParseLines(IEnumerable<string> lines, string defaultEffect)
	{
		string effect = defaultEffect;
		int light = 4;
		int speed = 1;
		int colorArgb = Color.White.ToArgb();
		bool powerOn = false;
		bool explicitPower = false;
		int[]? manualIndexes = null;
		int breathingIndex = 1;
		int monoIndex = 4;
		bool usePreset = false;
		foreach (string line in lines)
		{
			int num = line.IndexOf('=');
			if (num <= 0)
			{
				continue;
			}
			string text = line.Substring(0, num);
			string text2 = line.Substring(num + 1);
			switch (text)
			{
			case "effect":
				if (!string.IsNullOrWhiteSpace(text2))
				{
					effect = text2;
				}
				break;
			case "light":
			{
				if (int.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result3))
				{
					light = Math.Clamp(result3, 0, 4);
				}
				break;
			}
			case "speed":
			{
				if (int.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result4))
				{
					speed = Math.Clamp(result4, 1, 10);
				}
				break;
			}
			case "color":
			{
				if (int.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
				{
					colorArgb = result2;
				}
				break;
			}
			case "power":
			{
				if (int.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
				{
					powerOn = result != 0;
				}
				break;
			}
			case "powerexplicit":
			{
				// ⭐⭐ 2026-10-19（机主：启动自动点亮 Logo 灯）：
				//   ⭐ 只有用户真的点过开关才有 powerexplicit=1；旧配置没有该键 ⇒ 视为"没设过"
				//   ⇒ 启动恢复**不点灯**（⛔ 不再因为 power=1 就无条件点亮）✅
				explicitPower = int.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ef) && ef != 0;
				break;
			}
			case "manualindexes":
			{
				if (!string.IsNullOrWhiteSpace(text2))
				{
					string[] parts = text2.Split(',');
					if (parts.Length >= 6)
					{
						int[] arr = new int[6];
						bool ok = true;
						for (int i = 0; i < 6; i++)
						{
							if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out arr[i]))
							{
								ok = false;
								break;
							}
						}
						if (ok)
						{
							manualIndexes = arr;
						}
					}
				}
				break;
			}
			case "breathingindex":
			{
				if (int.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var breathingResult))
				{
					breathingIndex = Math.Clamp(breathingResult, 1, 4);
				}
				break;
			}
			case "usepreset":
			{
				usePreset = (text2 == "1");
				break;
			}
			case "monoindex":
			{
				if (int.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var monoResult))
				{
					monoIndex = Math.Clamp(monoResult, 1, 29);
				}
				break;
			}
		}
		}
		return new LightChannelSettings(effect, light, speed, colorArgb, powerOn && explicitPower, manualIndexes, breathingIndex, monoIndex, usePreset);
	}

	private static string Serialize(LightChannelSettings settings)
	{
		StringBuilder stringBuilder = new StringBuilder("manualindexes=");
		int[] colors = settings.ManualIndexes ?? LightChannelSettings.DefaultManualIndexes;
		for (int i = 0; i < 6; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(',');
			}
			stringBuilder.Append(colors[i].ToString(CultureInfo.InvariantCulture));
		}
		return string.Join('\n', new string[11]
		{
			"effect=" + settings.Effect,
			"light=" + settings.Light.ToString(CultureInfo.InvariantCulture),
			"speed=" + settings.Speed.ToString(CultureInfo.InvariantCulture),
			"color=" + settings.ColorArgb.ToString(CultureInfo.InvariantCulture),
			"power=" + (settings.PowerOn ? "1" : "0"),
			"powerexplicit=" + (settings.PowerExplicit ? "1" : "0"),
			"breathingindex=" + settings.BreathingIndex.ToString(CultureInfo.InvariantCulture),
			"monoindex=" + settings.MonochromeIndex.ToString(CultureInfo.InvariantCulture),
			"usepreset=" + (settings.UsePresetColor ? "1" : "0"),
			stringBuilder.ToString(),
			string.Empty
		});
	}

	private static string DefaultEffect(string topic)
	{
		if (!topic.Contains("Logo", StringComparison.OrdinalIgnoreCase))
		{
			return "Single";
		}
		return "Single";
	}

	private static string GetPath(string topic)
	{
		string path;
		if (topic.Contains("Logo", StringComparison.OrdinalIgnoreCase))
		{
			path = "logolight.cfg";
		}
		else
		{
			path = (topic.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) ? "keyboard.cfg" : "lightbar.cfg");
		}
		return Path.Combine(GetConfigDirectory(), path);
	}

	private static string GetConfigDirectory()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("LMECHREVO_LIGHT_CFG_DIR");
		return string.IsNullOrWhiteSpace(environmentVariable) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MechrevoLite") : environmentVariable;
	}

	private static string GetUserIntentMarkerPath()
	{
		return Path.Combine(GetConfigDirectory(), UserIntentMarkerFileName);
	}
}
