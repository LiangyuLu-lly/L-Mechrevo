using System;

namespace MechrevoLite.Mode;

/// <summary>
/// 30 系「模式 × 配置」绑定：三个模式（办公 / 游戏 / 狂飙）各记一个固件配置号（ProfileIndex）。
///
/// 协议证据（2026-10-04 GCU MQTT 抓包 + 官方控制台真机文件）：
///   Fan/Control {"Action":"OPERATING_OFFICE_MODE","ProfileIndex":"3"}
///   Fan/Control {"Action":"OPERATING_GAMING_MODE","ProfileIndex":"2"}
///   Fan/Control {"Action":"OPERATING_TURBO_MODE","ProfileIndex":"2"}
///   官方 UserPofiles\MainOption.json 里对应
///   OfficeProfileIndex / GamingProfileIndex / TurboProfileIndex（本机 3 / 2 / 2，与抓包逐条吻合）
///
/// LM 原先只在「自定义模式」下发 ProfileIndex，办公/游戏/狂飙恒发 0；
/// 本类补上"每个模式各自记住并下发自己的配置号"。
/// 仅 30 系启用（GcuVariant=="30" 或显卡代际 Gen30），见 <see cref="MechrevoLite.Hardware.MechrevoHw.SupportsModeProfileConfig"/>。
///
/// 编号约定 —— 本类一律用 **LM 视觉模式号**（与 UI / SwitchPerformanceModeAsync 同号）：
///   0 = 平衡/游戏(Gaming) / 1 = 狂暴/狂飙(Turbo) / 2 = 静音/办公(Office) / 3 = 自定义
///
/// ⚠️ 不要与控制台 OperatingMode / MQTT 动作编号混用，那是另一套：
///   0 = 办公(Office) / 1 = 游戏(Gaming) / 2 = 狂飙(Turbo) / 3 = 自定义
///   二者换算：PowerModeMapping.FromVisualMode（0->1, 1->2, 2->0）
/// 也不要与 ProfileName 的 Mode{N} 混用（1=游戏 / 2=办公 / 3=狂飙）。
/// </summary>
internal static class ModeProfileConfig
{
	/// <summary>配置数量 = 固件槽位数（官方每模式 5 个配置，部分机型 3 个）。</summary>
	internal static int Count
	{
		get
		{
			try
			{
				int slotCount = PerfModeService.SlotCount;
				return (slotCount > 0) ? slotCount : 1;
			}
			catch
			{
				return 1;
			}
		}
	}

	/// <summary>该**视觉模式**是否适用「模式 × 配置」绑定（排除自定义模式 3）。</summary>
	internal static bool IsBindable(int visualMode)
	{
		return visualMode == 0 || visualMode == 1 || visualMode == 2;
	}

	internal static string? KeyFor(int visualMode)
	{
		return visualMode switch
		{
			2 => "mode_cfg_office", 
			0 => "mode_cfg_gaming", 
			1 => "mode_cfg_turbo", 
			_ => null, 
		};
	}

	/// <summary>读取该模式绑定的配置号（0 起）。未设置时返回 0 —— 即保持 LM 原有行为。</summary>
	internal static int Get(int visualMode)
	{
		string? text = KeyFor(visualMode);
		if (text == null)
		{
			return 0;
		}
		// 0.293.5.1 基线：未设置时下发 ProfileIndex=0。
		return Math.Clamp(AppConfig.Get(text, 0), 0, Count - 1);
	}

	/// <summary>设置该模式绑定的配置号。下次切到该模式时随 Action 一起下发。</summary>
	internal static void Set(int visualMode, int profileIndex)
	{
		string? text = KeyFor(visualMode);
		if (text == null)
		{
			return;
		}
		int num = Math.Clamp(profileIndex, 0, Count - 1);
		AppConfig.Set(text, num);
		Logger.WriteLine($"ModeProfileConfig: visualMode={visualMode} profile={num} count={Count}");
	}

	/// <summary>
	/// 2026-10-05: per-config custom name, stored locally by LM.
	/// The GCU has no name for *preset* profiles (SET_CUSTOM_PROFILE_OSD_STRING does not
	/// persist one -- verified by capture: the official console's own names never reach
	/// MQTT, the profile JSON, the registry or the package data), so LM keeps its own.
	/// </summary>
	internal static string? GetName(int visualMode, int profileIndex)
	{
		string? text = NameKeyFor(visualMode, profileIndex);
		if (text == null)
		{
			return null;
		}
		string? text2 = AppConfig.GetString(text, null);
		return string.IsNullOrWhiteSpace(text2) ? null : text2;
	}

	/// <summary>Save the custom name for this config; null/blank clears it.</summary>
	internal static void SetName(int visualMode, int profileIndex, string? name)
	{
		string? text = NameKeyFor(visualMode, profileIndex);
		if (text == null)
		{
			return;
		}
		string text2 = (name ?? "").Trim();
		AppConfig.Set(text, text2);
		Logger.WriteLine($"ModeProfileConfig: name visualMode={visualMode} profile={profileIndex} name={text2}");
	}

	private static string? NameKeyFor(int visualMode, int profileIndex)
	{
		string? text = KeyFor(visualMode);
		if (text == null)
		{
			return null;
		}
		return text.Replace("mode_cfg_", "mode_name_") + "_" + Math.Clamp(profileIndex, 0, Count - 1);
	}
}
