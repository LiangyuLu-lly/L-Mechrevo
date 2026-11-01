using System;
using MechrevoLite.Gpu;

namespace MechrevoLite.Hardware;

internal static class ColorCalibrationSwitchPolicy
{
	/// <summary>GCU 最多上报 4 个校色档（默认 / sRGB / P3 / AdobeRGB）。</summary>
	internal const int MaxLevel = 4;

	/// <summary>
	/// 校色档位数的上报口径：① GCU `Customize/Info` 的 `SupportColorCalibration`（整数，= 档位数）
	/// 落到 <see cref="MechrevoHw.MqttColorCalibrationLevel"/>，优先；② 没有该上报时用注册表 ItemSupport 的
	/// `SupportColorCalibration`/`ColorCalibrationSupport`/`DisplayColorCalibrationSupport`/`ColorCalibration`
	/// （MechrevoDeviceCapabilities.ColorCalibrationLevel）；③ 都没有才退回屏幕色域判断（sRGB 屏按 1 档）。有效范围 1..4。
	/// </summary>
	internal static int ResolveLevel(int? mqttLevel, int capabilityLevel, bool? srgbDisplay)
	{
		int level = mqttLevel ?? capabilityLevel;
		if (level <= 0)
		{
			level = ((srgbDisplay == true) ? 1 : 2);
		}
		return Math.Clamp(level, 1, MaxLevel);
	}

	/// <summary>
	/// 二分设备：GCU 只上报 1 个校色档，档位只有「校准 / 不校准」两态
	/// （用户口径：40 系 + sRGB 屏；40 系 GCU 二进制里也只有裸 `COLOR_CALIBRATION_ON` / `COLOR_CALIBRATION_OFF`）。
	/// UI 用开关表达档位（开 = 校准、关 = 不校准），不显示档位下拉。
	/// </summary>
	internal static bool IsBinaryDevice(int? mqttLevel, int capabilityLevel, bool? srgbDisplay)
	{
		return ResolveLevel(mqttLevel, capabilityLevel, srgbDisplay) <= 1;
	}

	/// <summary>
	/// 档位回读同步能力。用户口径：「40 系 GCU 不支持回读，50 系支持回读开关」。
	/// 故只有 50 系、且 GCU 真的上报过档位字段（`CurrentColorCalibration`/`ColorCalibrationMode`
	/// -> <see cref="MechrevoHw.ColorCalibrationModeSeen"/>）才允许回读覆盖 UI；
	/// 40 系一律不回读同步（这同时是"用户选完被回读拉回"的根治手段）。
	/// </summary>
	internal static bool SupportsReadback(DgpuGenerationKind generation, bool calibrationModeSeen)
	{
		return calibrationModeSeen & (generation == DgpuGenerationKind.Gen50);
	}

	/// <summary>
	/// 状态是否已达目标。<paramref name="binaryDevice"/> = 二分设备（40 系 + sRGB 屏）时档位没有可比性：
	/// 只有「校准 / 不校准」，开关状态即为全部状态。
	/// </summary>
	internal static bool StateMatches(bool expectedOn, bool actualOn, int actualMode, int targetMode, bool binaryDevice)
	{
		if (!expectedOn)
		{
			return actualOn == expectedOn;
		}
		// 开关优先：预期开启但实际开关未开，必然尚未应用（防止模式残留导致误判 AlreadyApplied）
		if (!actualOn)
		{
			return false;
		}
		if (binaryDevice)
		{
			return true;
		}
		return actualMode == targetMode;
	}

	internal static ColorCalibrationDecision Decide(bool connectedAndSupported, bool expectedOn, bool hdrEnabled, bool currentOn, int currentMode, int targetMode, bool binaryDevice)
	{
		if (!connectedAndSupported)
		{
			return ColorCalibrationDecision.Unavailable;
		}
		if (expectedOn & hdrEnabled)
		{
			return ColorCalibrationDecision.HdrBlocked;
		}
		if (!StateMatches(expectedOn, currentOn, currentMode, targetMode, binaryDevice))
		{
			return ColorCalibrationDecision.Proceed;
		}
		return ColorCalibrationDecision.AlreadyApplied;
	}
}
