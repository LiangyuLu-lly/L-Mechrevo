using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-09（kbdctrl，用户原话「只搁置用户模式」⇒「给 LM 接通键盘灯」这一条开工）：
/// 往 <c>Keyboard/Ctrl</c> 发键盘灯帧的**纯** payload 组装器 —— 不碰硬件、不碰 UI、不读设置、不发包，
/// 只把字典按实测帧的字段与顺序摆好；真正的 PUBLISH 在 <c>MechrevoService.PublishKeyboardCtrl*</c>。
///
/// <para><b>为什么单开一份而不是复用 <c>SetLightEffect("Keyboard/Ctrl")</c></b>：老那条路径的
/// 键盘 payload 会按 <c>IsGcu40Keyboard</c> / <c>IsSingleZoneRgb</c> / <c>IsGcu50</c> 分支增删字段
/// （40 系把 <c>nv_save</c>/<c>direction</c> 改成 null、50 系把 <c>powerstatus</c> 拔到 1……），
/// 落到"既非 40 也非单分区"的通用分支时甚至**没有** <c>MonochromeIndex</c>/<c>ManualIndex*</c>/
/// <c>DefaultData</c>/<c>AuroraSwitch</c>。本轮要的是「与 2026-10-09 蛟龙17 / Gen30 / 官方 30 系控制台
/// 抓包的首个 SetEffectALL **逐字段同构**」的固定产物，所以这里写一份**无条件全字段**的组装器：
/// 不管机型判据取到什么值，产出的键集合与顺序都恒等于实测帧。</para>
///
/// <para>⚠️ 老路径（灯带 <c>HidLightbar/Ctrl</c> 与键盘的各判据分支）**一行未动**：本文件只被
/// <c>MechrevoService</c> 里新增的 <c>PublishKeyboardCtrl*</c> 用到，而那些方法默认（开关关闭）**直接返回**。</para>
/// </summary>
internal static class KeyboardCtrlPayload
{
	/// <summary>键盘灯主题（与 <see cref="MqttTopics.KeyboardCtrl"/> 同值，这里单列一份是为了本文件零依赖、可单独编译自测）。</summary>
	internal const string Topic = "Keyboard/Ctrl";

	/// <summary>
	/// 键盘侧 ColorBlocks 的**官方固定值 = 7**（一个固定 7 槽格式）。
	/// ⚠️ 2026-10-09（用户更正）：「【7 分区】的蛟龙17也是单分区的」⇒ <c>ColorBlocks:7</c> **不代表 7 分区键盘**，
	/// 官方控制台对**单分区**键盘（KeyboardType=1 / Keyboard/Status.type=SingleZone）也照样发 7 段。
	/// 所以默认路径恒用本值；「单分区是否该只发 1 段」＝**尚无证据、待真机验证**的实验项，
	/// 由 <see cref="KeyboardCtrlBlockCountSwitch"/> 显式注入（默认不注入）。
	/// ⚠️ 灯带侧是 126，那是另一条已搁置的线，本文件不涉及。
	/// </summary>
	internal const int KeyboardColorBlocks = 7;

	/// <summary>实验注入用的"单分区 1 段"值（<c>LMECHREVO_KB_BLOCKS=1</c> 时才用；默认不用）。</summary>
	internal const int SingleZoneExperimentalColorBlocks = 1;

	/// <summary>自定义色（颜色走 ColorBuffer）时 GCU 认的色板索引，沿用老路径 0.291.x 的取值。</summary>
	internal const int CustomColorMonochromeIndex = 33;

	/// <summary>手动分区默认色板索引 1/5/9/13/21/25（与实测帧 ManualIndex1..6 逐值相同）。</summary>
	internal static readonly int[] DefaultManualIndexes = new int[6] { 1, 5, 9, 13, 21, 25 };

	/// <summary>默认色 0xFFFF0000（纯红）—— 实测帧里 ID1..6 与"缺省段"的取值。</summary>
	private const int DefaultColorArgb = unchecked((int)0xFFFF0000);

	/// <summary>
	/// <c>color</c> 子对象：<c>{"isCircular":true,"ColorBlocks":N,"ColorBuffer":[{"ID":i,"R":..,"G":..,"B":..},…]}</c>。
	/// 每项**必带 ID**（ID 是 GCU 认这一条 SetEffectALL 的必要字段，见 diagnostics\30系键盘灯GCU协议_抓包分析.md）。
	/// </summary>
	internal static Dictionary<string, object> BuildColor(int[] colorBufferArgb, int colorBlocks)
	{
		if (colorBlocks <= 0)
		{
			colorBlocks = KeyboardColorBlocks;
		}
		object[] buffer = new object[colorBlocks];
		for (int i = 0; i < colorBlocks; i++)
		{
			int argb = (colorBufferArgb != null && i < colorBufferArgb.Length) ? colorBufferArgb[i] : DefaultColorArgb;
			buffer[i] = new Dictionary<string, object>
			{
				["ID"] = i,
				["R"] = (argb >> 16) & 255,
				["G"] = (argb >> 8) & 255,
				["B"] = argb & 255
			};
		}
		return new Dictionary<string, object>
		{
			["isCircular"] = true,
			["ColorBlocks"] = colorBlocks,
			["ColorBuffer"] = buffer
		};
	}

	/// <summary>
	/// <c>SetEffectALL</c>。键顺序与实测帧**逐字相同**：
	/// MqttID, function, mode, effect, light, speed, direction, nv_save, color, backgroundcolor, alphabet,
	/// solution, level, powerstatus, type, MonochromeIndex, ManualIndex1..6, ManualInterval, BreathingIndex,
	/// DefaultData, AuroraSwitch。
	/// <para>字符串化的数值（light/speed/MonochromeIndex/ManualIndex*/ManualInterval/BreathingIndex）
	/// 是官方控制台的写法，必须保持字符串 —— 实测帧里它们是 <c>"3"</c> 而不是 <c>3</c>。</para>
	/// </summary>
	internal static Dictionary<string, object> BuildSetEffectAll(
		string effect,
		int light,
		int speed,
		int powerstatus = 0,
		int monochromeIndex = CustomColorMonochromeIndex,
		int[] manualIndexes = null,
		int manualInterval = 6,
		int breathingIndex = 1,
		int[] colorBufferArgb = null,
		int colorBlocks = KeyboardColorBlocks)
	{
		int[] indexes = (manualIndexes != null && manualIndexes.Length >= 6) ? manualIndexes : DefaultManualIndexes;
		return new Dictionary<string, object>
		{
			["MqttID"] = null,
			["function"] = "SetEffectALL",
			["mode"] = "Lighting",
			["effect"] = effect,
			["light"] = light.ToString(CultureInfo.InvariantCulture),
			["speed"] = speed.ToString(CultureInfo.InvariantCulture),
			["direction"] = null,
			["nv_save"] = null,
			["color"] = BuildColor(colorBufferArgb, colorBlocks),
			["backgroundcolor"] = 0,
			["alphabet"] = null,
			["solution"] = 0,
			["level"] = 0,
			["powerstatus"] = powerstatus,
			["type"] = 0,
			["MonochromeIndex"] = monochromeIndex.ToString(CultureInfo.InvariantCulture),
			["ManualIndex1"] = M(0),
			["ManualIndex2"] = M(1),
			["ManualIndex3"] = M(2),
			["ManualIndex4"] = M(3),
			["ManualIndex5"] = M(4),
			["ManualIndex6"] = M(5),
			["ManualInterval"] = manualInterval.ToString(CultureInfo.InvariantCulture),
			["BreathingIndex"] = breathingIndex.ToString(CultureInfo.InvariantCulture),
			["DefaultData"] = "",
			["AuroraSwitch"] = false
		};
		// 2026-10-09（kbdctrl②）：**仅**当调用方显式注入 blockCount=1（诊断/实验路径）时，把
		// ManualIndex1..6 这类"多分区"字段发 null；默认 official（7）时逐字与实测帧一致。
		// ⚠️ 这一步不表达任何结论 —— 「单分区到底要不要改块数」仍待真机验证（见 KeyboardCtrlBlockCountSwitch）。
		object M(int i)
		{
			return (colorBlocks > 1) ? indexes[i].ToString(CultureInfo.InvariantCulture) : null;
		}
	}

	/// <summary><c>SetPower</c>：实测与老路径同形，只有 function + powerstatus 两个键（powerstatus 为数字 1/0）。</summary>
	internal static Dictionary<string, object> BuildSetPower(bool on)
	{
		return new Dictionary<string, object>
		{
			["function"] = "SetPower",
			["powerstatus"] = (on ? 1 : 0)
		};
	}

	/// <summary>2026-10-21：<c>SetEffectLight</c> —— 50 GCU 单分区键盘的亮度入口（只读 mode + light 两个字段）。</summary>
	internal static Dictionary<string, object> BuildSetEffectLight(int light)
	{
		return new Dictionary<string, object>
		{
			["function"] = "SetEffectLight",
			["mode"] = "Lighting",
			["light"] = light.ToString(CultureInfo.InvariantCulture)
		};
	}

	/// <summary>
	/// <c>SetLightingLevel</c>（亮度）：字段集合与老路径 <c>MechrevoService.SetLightingLevelAsync</c> 相同，
	/// 但同样无条件带全 <c>MonochromeIndex</c>/<c>ManualIndex*</c>/<c>DefaultData</c>/<c>AuroraSwitch</c>。
	/// </summary>
	internal static Dictionary<string, object> BuildSetLightingLevel(
		string effect,
		int light,
		int speed,
		int monochromeIndex = CustomColorMonochromeIndex,
		int[] manualIndexes = null,
		int manualInterval = 6,
		int breathingIndex = 1,
		int[] colorBufferArgb = null,
		int colorBlocks = KeyboardColorBlocks)
	{
		int[] indexes = (manualIndexes != null && manualIndexes.Length >= 6) ? manualIndexes : DefaultManualIndexes;
		return new Dictionary<string, object>
		{
			["function"] = "SetLightingLevel",
			["mode"] = "Lighting",
			["effect"] = effect,
			["light"] = light.ToString(CultureInfo.InvariantCulture),
			["speed"] = speed.ToString(CultureInfo.InvariantCulture),
			["direction"] = "None",
			["nv_save"] = "SAVE",
			["color"] = BuildColor(colorBufferArgb, colorBlocks),
			["MqttID"] = null,
			["backgroundcolor"] = 0,
			["alphabet"] = null,
			["solution"] = 0,
			["level"] = 0,
			["powerstatus"] = 0,
			["type"] = 0,
			["MonochromeIndex"] = monochromeIndex.ToString(CultureInfo.InvariantCulture),
			["ManualIndex1"] = M(0),
			["ManualIndex2"] = M(1),
			["ManualIndex3"] = M(2),
			["ManualIndex4"] = M(3),
			["ManualIndex5"] = M(4),
			["ManualIndex6"] = M(5),
			["ManualInterval"] = manualInterval.ToString(CultureInfo.InvariantCulture),
			["BreathingIndex"] = breathingIndex.ToString(CultureInfo.InvariantCulture),
			["DefaultData"] = "",
			["AuroraSwitch"] = false
		};
		// 2026-10-09（kbdctrl②）：**仅**当调用方显式注入 blockCount=1（诊断/实验路径）时，把
		// ManualIndex1..6 这类"多分区"字段发 null；默认 official（7）时逐字与实测帧一致。
		// ⚠️ 这一步不表达任何结论 —— 「单分区到底要不要改块数」仍待真机验证（见 KeyboardCtrlBlockCountSwitch）。
		object M(int i)
		{
			return (colorBlocks > 1) ? indexes[i].ToString(CultureInfo.InvariantCulture) : null;
		}
	}

	/// <summary>日志/自测用：与其它灯效日志同口径（<c>System.Text.Json</c>），字段顺序即插入顺序。</summary>
	internal static string Serialize(object payload)
	{
		try
		{
			return JsonSerializer.Serialize(payload);
		}
		catch (Exception ex)
		{
			return "<serialize failed: " + ex.Message + ">";
		}
	}

	/// <summary>
	/// 单色（Single）/自定义色用的 7 段 ColorBuffer：**第 0 段 = 调用方给的颜色，其余 6 段保持默认红**。
	/// 依据是实测帧的首个 SetEffectALL —— <c>[{151,243,0}, 红×6]</c>，以及实测口径「换自定义色 = 只改写
	/// <c>color.ColorBuffer[0]</c> 的 RGB（其余段维持控制台缺省红）」；因此这里不做"整条同色"的猜测。
	/// </summary>
	internal static int[] SingleColorBuffer(int firstColorArgb, int colorBlocks)
	{
		int count = (colorBlocks > 0) ? colorBlocks : KeyboardColorBlocks;
		int[] buffer = new int[count];
		for (int i = 0; i < count; i++)
		{
			buffer[i] = (i == 0) ? firstColorArgb : DefaultColorArgb;
		}
		return buffer;
	}
}
