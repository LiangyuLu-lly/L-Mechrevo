// ⭐⭐ 2026-10-19（宝龙达键盘灯 · 运行时自校准 · 第 1 步：只放结果与纯函数）：
// 「本机键盘灯到底能写几档亮度 / 彩虹能不能用」不再靠 GPU 代数猜常量，
// 而是**打开键盘灯设置时探测一次**，结果缓存全进程复用。
//
// ⚠️ 为什么必须自校准（机主真机反馈）：
//   · 「最大亮度不能用」—— 40 系曾被判成 0~4，UI 能拖到 4，但固件只认 0~3
//     ⇒ 拖到最大反而被夹回/灭 ⚠️；
//   · 「彩虹没效果」—— 第三方 7.3 用 `RGBKeyboardMode = 1`，但**本机固件认不认**只能实测。
//   ⇒ 这两件事都是**每台机器固件能力**，⛔ 不是能从代数推出来的事实。
//
// ⚠️ 本文件的纪律（与 `BaolongdaProfile.cs` 同款）：
//   ① ⛔ **不碰硬件**（不读 WMI、不打日志、不写硬件）—— 纯数据 + 纯函数 ⇒ 可离线自检；
//   ② 真正探测（写 WMI / 回读 / try-finally 恢复）在
//      `MechrevoHw.ProbeKeyboardLightingCapabilities()`；
//   ③ 结果**只读缓存**在 `MechrevoHw.KeyboardLightingCaps`，⛔ 不每次刷新重探。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐ 宝龙达键盘灯**运行时自校准结果**（不可变）。
/// <para>⭐ 由 <c>MechrevoHw.EnsureKeyboardLightingCalibration()</c> 探测一次后缓存；
/// ⭐ UI（亮度滑块范围 / 效果下拉的彩虹项）只读本对象 ✅。</para>
/// <para>⚠️ 语义**如实**：<see cref="BrightnessProbed" /> 为 <c>false</c> 表示"没测出来"
/// （⛔ 不是"本机只有 1 档"）⇒ 此时 <see cref="BrightnessMax" /> 退回**声明值**（保守）✅。</para>
/// </summary>
internal sealed class KeyboardLightingCalibration
{
	private KeyboardLightingCalibration(bool brightnessProbed, int brightnessMax, bool rainbowProbed, bool rainbowSupported, string detail)
	{
		BrightnessProbed = brightnessProbed;
		BrightnessMax = brightnessMax;
		RainbowProbed = rainbowProbed;
		RainbowSupported = rainbowSupported;
		Detail = detail;
	}

	/// <summary>⭐ 亮度上限是否**实测**出来了（<c>false</c> = 探测失败 ⇒ 用声明值兜底）。</summary>
	internal bool BrightnessProbed { get; }

	/// <summary>⭐ 本机键盘灯亮度上限（硬件级；⛔ 不是界面档位）。探测失败时 = 声明值。</summary>
	internal int BrightnessMax { get; }

	/// <summary>⭐ Rainbow 能力是否**实测**过（模式 16 写 1 后回读得到结论）。</summary>
	internal bool RainbowProbed { get; }

	/// <summary>⭐ 彩虹（模式 16 = 1）本机是否真的能保持住。⛔ 未探测/失败一律 <c>false</c>。</summary>
	internal bool RainbowSupported { get; }

	/// <summary>⭐ 人读摘要（写日志 / 诊断用；⛔ 不参与判断）。</summary>
	internal string Detail { get; }

	/// <summary>
	/// ⭐ **保守回退**：探测器不可用 / 读不到 / 写不进时用 ——
	/// 亮度上限保持**声明值**（⛔ 不凭探测失败就缩小可调范围）、Rainbow 一律**隐藏**（⛔ 不给无效项）。
	/// </summary>
	internal static KeyboardLightingCalibration Conservative(int declaredBrightnessMax, string reason)
	{
		int brightnessMax = Math.Max(1, declaredBrightnessMax);
		return new KeyboardLightingCalibration(
			brightnessProbed: false,
			brightnessMax: brightnessMax,
			rainbowProbed: false,
			rainbowSupported: false,
			detail: "保守回退（" + reason + "）：亮度上限=" + brightnessMax + "（声明值）· Rainbow 隐藏");
	}

	/// <summary>
	/// ⭐ 由探测原始读数生成结果（⭐ **纯函数**，离线自检直接调它 ✅）。
	/// <para>⭐ 判据：⭐ <paramref name="probedBrightnessMax" /> = 「写 k 后回读 == k」成立的最大 k；
	/// 它 &lt; 1 ⇒ 视为亮度探测失败 ⇒ 用 <paramref name="declaredBrightnessMax" /> 兜底 ✅。</para>
	/// <para>⚠️ 亮度上限**不会超过声明值**：探测写超范围被夹回时不得把上限抬高（⛔ 不信任夹回值）✅。</para>
	/// </summary>
	internal static KeyboardLightingCalibration FromProbe(
		int declaredBrightnessMax,
		int probedBrightnessMax,
		bool rainbowProbed,
		bool rainbowSupported,
		string detail)
	{
		int declared = Math.Max(1, declaredBrightnessMax);
		bool brightnessProbed = probedBrightnessMax >= 1;
		int brightnessMax = brightnessProbed ? Math.Clamp(probedBrightnessMax, 1, declared) : declared;
		bool rainbow = rainbowProbed && rainbowSupported;
		return new KeyboardLightingCalibration(brightnessProbed, brightnessMax, rainbowProbed, rainbow, detail);
	}
}
