using System.Collections.Generic;
using MechrevoLite.Properties;

namespace MechrevoLite.Mode;

internal class Modes
{
	private static Dictionary<string, string> settings = new Dictionary<string, string>
	{
		{ "mode_base", "_" },
		{ "mode_name", "_" },
		{ "powermode", "string" },
		// 2026-10-09（修 B2）：PowerNative.SetPowerMode 一直按「该档的 scheme_N」读方案，但这个键从未进过
		// schema、也没有任何写入来源 ⇒ GetModeString("scheme") 恒为 null ⇒ 每次切档都落进「null 切平衡」兜底。
		// 这里把它注册成按档字符串键（scheme_0 / scheme_1 / …），既是那句代码的真正来源，也让用户能显式指定。
		// 不写默认值：键不存在 ⇒ GetModeString 返回 null ⇒ 不碰系统电源方案。
		{ "scheme", "string" },
		{ "limit_total", "int" },
		{ "limit_slow", "int" },
		{ "limit_fast", "int" },
		{ "limit_cpu", "int" },
		{ "fan_profile_cpu", "string" },
		{ "fan_profile_gpu", "string" },
		{ "fan_profile_mid", "string" },
		{ "gpu_power", "int" },
		{ "gpu_boost", "int" },
		{ "gpu_temp", "int" },
		{ "gpu_core", "int" },
		{ "gpu_memory", "int" },
		{ "gpu_clock_limit", "int" },
		{ "cpu_temp", "_" },
		{ "cpu_uv", "_" },
		{ "igpu_uv", "_" },
		{ "auto_boost", "int" },
		{ "auto_apply", "int" },
		{ "auto_apply_power", "int" },
		{ "auto_uv", "_" },
		{ "hysteresis_up", "int" },
		{ "hysteresis_down", "int" }
	};

	private const int maxModes = 20;

	public static Dictionary<int, string> GetDictonary()
	{
		Dictionary<int, string> dictionary = new Dictionary<int, string>
		{
			{
				2,
				Strings.Silent
			},
			{
				0,
				Strings.Balanced
			},
			{
				1,
				Strings.Turbo
			}
		};
		for (int i = 3; i < 20; i++)
		{
			if (Exists(i))
			{
				dictionary.Add(i, GetName(i));
			}
		}
		return dictionary;
	}

	public static List<int> GetList()
	{
		List<int> list = new List<int> { 2, 0, 1 };
		for (int i = 3; i < 20; i++)
		{
			if (Exists(i))
			{
				list.Add(i);
			}
		}
		return list;
	}

	public static void Remove(int mode)
	{
		foreach (string key in settings.Keys)
		{
			AppConfig.Remove(key + "_" + mode);
		}
	}

	public static int Add()
	{
		int current = GetCurrent();
		for (int i = 3; i < 20; i++)
		{
			if (Exists(i))
			{
				continue;
			}
			AppConfig.Set("mode_base_" + i, GetCurrentBase());
			AppConfig.Set("mode_name_" + i, "Custom " + (i - 2));
			if (Exists(current))
			{
				foreach (KeyValuePair<string, string> setting in settings)
				{
					if (setting.Value == "_")
					{
						continue;
					}
					string name = setting.Key + "_" + current;
					string name2 = setting.Key + "_" + i;
					if (AppConfig.Exists(name))
					{
						if (setting.Value == "int")
						{
							AppConfig.Set(name2, AppConfig.Get(name));
						}
						else
						{
							AppConfig.Set(name2, AppConfig.GetString(name) ?? "");
						}
					}
				}
			}
			return i;
		}
		return -1;
	}

	public static int GetCurrent()
	{
		return AppConfig.Get("performance_mode");
	}

	public static void SetCurrent(int mode)
	{
		AppConfig.Set("performance_" + Program.PerformanceKey(), mode);
		AppConfig.Set("performance_mode", mode);
	}

	public static int GetCurrentBase()
	{
		return GetBase(GetCurrent());
	}

	public static string GetCurrentName()
	{
		return GetName(GetCurrent());
	}

	public static bool Exists(int mode)
	{
		return GetBase(mode) >= 0;
	}

	public static int GetBase(int mode)
	{
		if (mode >= 0 && mode <= 2)
		{
			return mode;
		}
		return AppConfig.Get("mode_base_" + mode);
	}

	public static string GetName(int mode)
	{
		return mode switch
		{
			0 => Strings.Balanced, 
			1 => Strings.Turbo, 
			2 => Strings.Silent, 
			_ => AppConfig.GetString("mode_name_" + mode) ?? "", 
		};
	}

	public static int GetNext(bool back = false)
	{
		List<int> list = GetList();
		int num = list.IndexOf(GetCurrent());
		if (back)
		{
			num--;
			if (num < 0)
			{
				num = list.Count - 1;
			}
			return list[num];
		}
		num++;
		if (num > list.Count - 1)
		{
			num = 0;
		}
		return list[num];
	}
}
