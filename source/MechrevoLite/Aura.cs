using System.Drawing;
using MechrevoLite.Helpers;
using MechrevoLite.Properties;

namespace MechrevoLite;

public static class Aura
{
	public static class CustomRGB
	{
		public static void ApplyGPUColor(int gpuMode = -1)
		{
			_ = IsMechrevoConnected;
		}
	}

	// 2026-10-15（C-1 第 2 步分类 ①）：Aura 是灯效（GCU 键盘/灯带通道）⇒ 真正需要 GCU。
	internal static bool IsMechrevoConnected => Program.hw?.IsGcuConnected ?? false;

	public static Color Color1 { get; set; } = Color.White;

	public static Color Color2 { get; set; } = Color.Black;

	public static AuraMode Mode { get; set; }

	public static bool sessionLock { get; set; }

	public static void ApplyAura()
	{
		_ = IsMechrevoConnected;
	}

	internal static void NotifyApplyOutcome(bool success)
	{
		if (!success)
		{
			ToastForm.ShowFailure(Strings.AuraFailed);
		}
	}

	public static void ApplyBrightness()
	{
		_ = IsMechrevoConnected;
	}

	public static void ApplyBrightness(int brightness, string log = "Backlight")
	{
		_ = IsMechrevoConnected;
	}

	public static void SleepBrightness()
	{
		_ = IsMechrevoConnected;
	}

	public static bool HasRandomColor()
	{
		return false;
	}

	public static bool HasSecondColor()
	{
		return false;
	}
}
