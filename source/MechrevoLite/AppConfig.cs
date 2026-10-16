using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using System.Windows.Forms;
using MechrevoLite.Helpers;
using MechrevoLite.Mode;

namespace MechrevoLite;

public static class AppConfig
{
	private static string configFile;

	private static string fallbackConfigFile;

	private static bool configFileOverridden;

	private static Dictionary<string, object> config;

	private static System.Timers.Timer timer;

	private static readonly object configLock;

	private static readonly object writeLock;

	internal static int FlushCount;

	private static readonly JsonSerializerOptions LenientOptions;

	private static readonly Regex KeyValueRegex;

	private static readonly Lazy<string> _model;

	private static readonly Lazy<(string Bios, string ModelShort)> _biosData;

	static AppConfig()
	{
		config = new Dictionary<string, object>();
		timer = new System.Timers.Timer(2000.0)
		{
			AutoReset = false
		};
		configLock = new object();
		writeLock = new object();
		LenientOptions = new JsonSerializerOptions
		{
			AllowTrailingCommas = true,
			ReadCommentHandling = JsonCommentHandling.Skip
		};
		KeyValueRegex = new Regex("\"((?:\\\\.|[^\"\\\\])*)\"\\s*:\\s*(\"(?:\\\\.|[^\"\\\\])*\"|-?\\d+(?:\\.\\d+)?|true|false|null)");
		_model = new Lazy<string>(LoadModel, LazyThreadSafetyMode.ExecutionAndPublication);
		_biosData = new Lazy<(string, string)>(LoadBios, LazyThreadSafetyMode.ExecutionAndPublication);
		string text = "config.json";
		string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MechrevoLite");
		string startupConfig = Path.Combine(Application.StartupPath.Trim('\\'), text);
		fallbackConfigFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MechrevoLite", text);
		string environmentVariable = Environment.GetEnvironmentVariable("LMECHREVO_CONFIG_FILE");
		configFile = (string.IsNullOrWhiteSpace(environmentVariable) ? ResolveConfigPath(startupConfig, fallbackConfigFile, Path.Combine(text2, text), ProcessHelper.IsRunningAsSystem(), File.Exists) : environmentVariable);
		configFileOverridden = !string.IsNullOrWhiteSpace(environmentVariable);
		Directory.CreateDirectory(Path.GetDirectoryName(configFile) ?? text2);
		Directory.CreateDirectory(text2);
		if (!TryLoadConfig(configFile) && !TryRecoverConfig(configFile) && !TryLoadConfig(configFile + ".bak") && (configFileOverridden || !TryLoadConfig(fallbackConfigFile)))
		{
			Init();
		}
		MigrateLogLevelDefault();
		timer.Elapsed += Timer_Elapsed;
	}

	internal static string ResolveConfigPath(string startupConfig, string fallbackConfig, string appDataConfig, bool runningAsSystem, Func<string, bool> fileExists)
	{
		if (fileExists(startupConfig))
		{
			return startupConfig;
		}
		if (runningAsSystem && fileExists(fallbackConfig))
		{
			return fallbackConfig;
		}
		return appDataConfig;
	}

	internal static IReadOnlyList<(string SourcePath, string FileName, string Description)> ExportableConfigFiles()
	{
		List<(string, string, string)> files = new List<(string, string, string)>();
		Add(configFile, Path.GetFileName(configFile), "主配置：性能/显卡模式、灯效、快捷开关、窗口外观等设置项");
		Add(configFile + ".bak", Path.GetFileName(configFile) + ".bak", "主配置上一次成功写入的备份（原子替换留下）");
		if (!configFileOverridden)
		{
			Add(fallbackConfigFile, "config.fallback.json", "机器级回退配置（ProgramData，供 SYSTEM/计划任务身份读取）");
		}
		return files;
		void Add(string path, string fileName, string description)
		{
			if (!string.IsNullOrWhiteSpace(path) && !files.Any(((string, string, string) file) => string.Equals(file.Item1, path, StringComparison.OrdinalIgnoreCase)))
			{
				files.Add((path, fileName, description));
			}
		}
	}

	internal static Dictionary<string, object>? DeserializeConfig(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<Dictionary<string, object>>(json, LenientOptions);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	internal static Dictionary<string, string> RecoverKeyValuePairs(string? json)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		if (string.IsNullOrEmpty(json))
		{
			return dictionary;
		}
		foreach (Match item in KeyValueRegex.Matches(json))
		{
			dictionary["\"" + item.Groups[1].Value + "\""] = item.Groups[2].Value;
		}
		return dictionary;
	}

	internal static string RebuildJson(IDictionary<string, string> pairs)
	{
		return "{" + string.Join(",", pairs.Select((KeyValuePair<string, string> p) => p.Key + ":" + p.Value)) + "}";
	}

	internal static Dictionary<string, object>? RecoverConfig(string? json)
	{
		Dictionary<string, string> dictionary = RecoverKeyValuePairs(json);
		if (dictionary.Count == 0)
		{
			return null;
		}
		return DeserializeConfig(RebuildJson(dictionary));
	}

	private static bool TryLoadConfig(string path)
	{
		if (!File.Exists(path))
		{
			return false;
		}
		try
		{
			Dictionary<string, object> dictionary = DeserializeConfig(File.ReadAllText(path));
			if (dictionary == null)
			{
				Logger.WriteLine("Config at " + path + " deserialized to null; treating it as unreadable.");
				return false;
			}
			config = dictionary;
			Logger.WriteLine("Config loaded from " + path);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Broken config " + path + ": " + ex.Message);
			return false;
		}
	}

	private static bool TryRecoverConfig(string path)
	{
		if (!File.Exists(path))
		{
			return false;
		}
		try
		{
			Dictionary<string, object> dictionary = RecoverConfig(File.ReadAllText(path));
			if (dictionary == null)
			{
				return false;
			}
			config = dictionary;
			Logger.WriteLine($"Recovered {dictionary.Count} values from broken config {path}");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Config recovery failed " + path + ": " + ex.Message);
			return false;
		}
	}

	private static void Timer_Elapsed(object? sender, ElapsedEventArgs e)
	{
		timer.Stop();
		Persist();
	}

	private static bool Persist()
	{
		bool result;
		lock (writeLock)
		{
			try
			{
				string content;
				lock (configLock)
				{
					content = JsonSerializer.Serialize(config, new JsonSerializerOptions
					{
						WriteIndented = true
					});
				}
				WriteAtomic(configFile, content);
				SyncFallbackConfig();
				result = true;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Config write failed: " + ex.Message);
				result = false;
			}
		}
		return result;
	}

	public static void Flush()
	{
		TryFlush();
	}

	internal static bool TryFlush()
	{
		FlushCount++;
		timer.Stop();
		return Persist();
	}

	public static void Shutdown()
	{
		Flush();
		timer.Dispose();
	}

	internal static void WriteAtomic(string path, string content)
	{
		string text = path + ".tmp";
		File.WriteAllText(text, content);
		using (FileStream fileStream = new FileStream(text, FileMode.Open, FileAccess.Write))
		{
			fileStream.Flush(flushToDisk: true);
		}
		if (File.Exists(path))
		{
			File.Replace(text, path, path + ".bak");
		}
		else
		{
			File.Move(text, path);
		}
	}

	internal static bool ShouldSyncFallback(string? fallbackConfig, string? currentConfig)
	{
		if (string.IsNullOrEmpty(fallbackConfig))
		{
			return false;
		}
		if (string.Equals(fallbackConfig, currentConfig, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return !string.IsNullOrEmpty(Path.GetDirectoryName(fallbackConfig));
	}

	private static void SyncFallbackConfig()
	{
		if (configFileOverridden || !ShouldSyncFallback(fallbackConfigFile, configFile))
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(fallbackConfigFile));
			File.Copy(configFile, fallbackConfigFile, overwrite: true);
		}
		catch (Exception)
		{
		}
	}

	private static string LoadModel()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("Select * from Win32_ComputerSystem");
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectSearcher.Get().GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementBaseObject current = managementObjectEnumerator.Current;
				using (current)
				{
					return current["Model"]?.ToString() ?? string.Empty;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine(ex.Message);
		}
		return string.Empty;
	}

	internal static (string Bios, string ModelShort) ParseBiosVersion(string? raw)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return (Bios: string.Empty, ModelShort: string.Empty);
		}
		string[] array = text.Split('.');
		if (array.Length != 2)
		{
			return (Bios: text, ModelShort: string.Empty);
		}
		string text2 = array[0].Trim();
		string text3 = array[1].Trim();
		if (text3.Length == 0 || text2.Length < 4 || !text2.Any(char.IsLetter))
		{
			return (Bios: text, ModelShort: string.Empty);
		}
		return (Bios: text3, ModelShort: text2);
	}

	private static (string Bios, string ModelShort) LoadBios()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectSearcher.Get().GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementBaseObject current = managementObjectEnumerator.Current;
				using (current)
				{
					return ParseBiosVersion(current["SMBIOSBIOSVersion"]?.ToString());
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine(ex.Message);
		}
		return (Bios: string.Empty, ModelShort: string.Empty);
	}

	public static string GetModel()
	{
		return _model.Value;
	}

	public static (string, string) GetBiosAndModel()
	{
		return (_biosData.Value.Bios, _biosData.Value.ModelShort);
	}

	public static bool ContainsModel(string contains)
	{
		return _model.Value.Contains(contains, StringComparison.OrdinalIgnoreCase);
	}

	private static void Init()
	{
		config = new Dictionary<string, object>();
		config["performance_mode"] = 0;
		// 2026-10-13 logdefault-error：出厂默认日志级别 = 仅错误（"error"）。诊断包是**事后**导出的，
		// off 时包里只剩内存环形缓冲那几十行；all 又太吵。仅错误兼顾「有历史可查」与「不刷屏」。
		config["log_level"] = "error";
		string contents = JsonSerializer.Serialize(config);
		File.WriteAllText(configFile, contents);
	}

	/// <summary>
	/// 2026-10-12 logfix2（用户原话「改log」）：把**已有安装**的默认日志级别从 off 提到 all（见 <see cref="Init" />）。
	/// 只改 Init() 救不了老用户 —— 他们的 config.json 里早就写着 "off"，那是历史默认值、不是机主的选择。
	/// 一次性迁移：没有迁移标记、且当前值仍是 off/缺失时才改成 all 并**立刻落盘**；
	/// 机主之后在「设置 → 日志级别」手动选回「关闭」不会被再覆盖（标记已持久化）。
	/// 任何失败只记 log.txt，绝不抛 —— 配置这边出问题也不能挡住启动。
	/// 2026-10-13 logdefault-error：出厂默认改成「仅错误」后，**本条迁移特意保持原样**（不做 v2 回滚）：
	/// 配置里无法区分「被 v1 迁到 all」与「机主自己选了 all」—— 两者都是 log_level=all + 标记已存在，
	/// 强行回退会覆盖机主的真实选择，所以已迁到 all 的安装保持 all。
	/// </summary>
	private static void MigrateLogLevelDefault()
	{
		const string key = "log_level_default_migrated";
		try
		{
			if (config.ContainsKey(key))
			{
				return;
			}
			string current = GetString("log_level");
			if (string.IsNullOrWhiteSpace(current) || string.Equals(current.Trim(), "off", StringComparison.OrdinalIgnoreCase))
			{
				config["log_level"] = "all";
				Logger.WriteLine("log_level default migrated: off -> all (one-time; 可在 设置 → 日志级别 改回「关闭」)");
			}
			config[key] = 1;
			Persist();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("log_level default migration failed: " + ex.Message);
		}
	}

	public static bool Exists(string name)
	{
		lock (configLock)
		{
			return config.ContainsKey(name);
		}
	}

	public static int Get(string name, int empty = -1)
	{
		lock (configLock)
		{
			object value;
			int result;
			return (config.TryGetValue(name, out value) && int.TryParse(value?.ToString(), out result)) ? result : empty;
		}
	}

	public static bool Is(string name)
	{
		return Get(name) == 1;
	}

	public static bool IsNotFalse(string name)
	{
		return Get(name) != 0;
	}

	public static string? GetString(string name, string? empty = null)
	{
		lock (configLock)
		{
			object value;
			return (!config.TryGetValue(name, out value)) ? empty : value?.ToString();
		}
	}

	private static void Write()
	{
		timer.Stop();
		timer.Start();
	}

	public static void Set(string name, int value)
	{
		lock (configLock)
		{
			config[name] = value;
		}
		Write();
	}

	public static void Set(string name, string value)
	{
		lock (configLock)
		{
			config[name] = value;
		}
		Write();
	}

	public static void Remove(string name)
	{
		lock (configLock)
		{
			config.Remove(name);
		}
		Write();
	}

	internal static IReadOnlyDictionary<string, object> Snapshot()
	{
		lock (configLock)
		{
			return new Dictionary<string, object>(config);
		}
	}

	public static void RemoveMode(string name)
	{
		Remove(name + "_" + Modes.GetCurrent());
	}

	public static string GgetParamName(AsusFan device, string paramName = "fan_profile")
	{
		int current = Modes.GetCurrent();
		string text = device switch
		{
			AsusFan.GPU => "gpu", 
			AsusFan.Mid => "mid", 
			_ => "cpu", 
		};
		return paramName + "_" + text + "_" + current;
	}

	public static byte[] StringToBytes(string str)
	{
		string[] array = str.Split('-');
		byte[] array2 = new byte[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array2[i] = Convert.ToByte(array[i], 16);
		}
		return array2;
	}

	public static string? GetModeString(string name)
	{
		return GetString(name + "_" + Modes.GetCurrent());
	}

	public static int GetMode(string name, int empty = -1)
	{
		return Get(name + "_" + Modes.GetCurrent(), empty);
	}

	public static bool IsMode(string name)
	{
		return Get(name + "_" + Modes.GetCurrent()) == 1;
	}

	public static void SetMode(string name, int value)
	{
		Set(name + "_" + Modes.GetCurrent(), value);
	}

	public static void SetMode(string name, string value)
	{
		Set(name + "_" + Modes.GetCurrent(), value);
	}

	public static bool IsOLED()
	{
		return Is("oled");
	}

	public static bool IsNoOverdrive()
	{
		return Is("no_overdrive");
	}

	public static bool IsApplyPower()
	{
		return IsMode("auto_apply_power");
	}

	public static bool IsApplyFans()
	{
		return IsMode("auto_apply");
	}

	public static bool IsApplyUV()
	{
		return IsMode("auto_uv");
	}

	public static bool IsManualModeRequired()
	{
		if (!IsApplyPower())
		{
			return false;
		}
		return Is("manual_mode");
	}

	public static bool IsFanRequired()
	{
		return Is("fan_required");
	}

	public static bool IsModeReapplyRequired()
	{
		return Is("mode_reapply");
	}

	public static bool IsStandardModeFix()
	{
		return Is("shutdown_gpu");
	}

	public static bool IsNVPlatform()
	{
		return Is("nv_platform");
	}

	public static bool IsForceSetGPUMode()
	{
		return Is("gpu_mode_force_set");
	}

	public static bool NoGpu()
	{
		return Is("no_gpu");
	}

	public static bool IsOverlay()
	{
		return Is("overlay");
	}

	public static bool IsOverlayGameOnly()
	{
		return Is("overlay_game_only");
	}

	public static bool IsDynamicLighting()
	{
		return Is("dynamic_lighting");
	}

	public static bool IsDynamicLightingOnly()
	{
		return Is("dynamic_lighting_only");
	}

	public static bool IsDynamicLightingInit()
	{
		return Is("lighting_init");
	}

	public static bool IsAutoASPM()
	{
		return IsNotFalse("aspm");
	}
}
