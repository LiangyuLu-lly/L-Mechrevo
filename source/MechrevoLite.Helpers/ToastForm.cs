using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Forms.Automation;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;
using MechrevoLite.UI;

namespace MechrevoLite.Helpers;

public class ToastForm : OSDNativeForm
{
	protected static string toastText = "Balanced";

	protected static ToastIcon? toastIcon = null;

	protected static Timer timer = new Timer();

	private static readonly Font _toastFont = UiVisualStyle.Font(36f, FontStyle.Bold, GraphicsUnit.Pixel);

	private static readonly SolidBrush _toastBrush = new SolidBrush(Color.FromArgb(150, Color.Black));

	private static readonly SolidBrush _toastTextBrush = new SolidBrush(Color.White);

	private static readonly StringFormat _toastFormat = new StringFormat
	{
		LineAlignment = StringAlignment.Center,
		Alignment = StringAlignment.Center
	};

	internal static Action<string>? FailureCallback;

	internal static Action<string>? NoticeCallback;

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · OSD 提示 · 本地方案）：⭐ 本机是不是**宝龙达候选** ✅（⭐ 只看板号 ✅）。
	/// <para>⚠️ 用 <see cref="Lazy{T}" /> 缓存：⭐ 板号判据要读注册表 ⚠️ ⇒ ⭐ 每条 toast 都读就是浪费 ✅；
	/// ⭐ 而主板型号在**一次运行内不会变** ✅ ⇒ ⭐ 缓存安全 ✅。⭐ 读异常 ⇒ ⭐ 按"不是宝龙达"⭐ 处理
	/// （⭐ 安全方向 ✅）。</para>
	/// <para>⭐ 非宝龙达 ⇒ 恒 <c>false</c> ⇒ ⭐ <see cref="RunToast" /> **一行行为都不变** ✅。</para>
	/// </summary>
	private static readonly Lazy<bool> _isBaolongdaBoard = new Lazy<bool>(() =>
	{
		try
		{
			return BaolongdaBackend.IsBaolongdaModelWhitelisted(out _);
		}
		catch
		{
			return false;
		}
	});

	public ToastForm()
	{
		timer.Tick += timer_Tick;
		timer.Enabled = false;
		timer.Interval = 2000;
	}

	protected override void PerformPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.FillRoundedRectangle(_toastBrush, Bound, 10);
		using Bitmap bitmap = toastIcon switch
		{
			ToastIcon.BrightnessUp => Resources.brightness_up, 
			ToastIcon.BrightnessDown => Resources.brightness_down, 
			ToastIcon.BacklightUp => Resources.backlight_up, 
			ToastIcon.BacklightDown => Resources.backlight_down, 
			ToastIcon.Microphone => Resources.icons8_microphone_96, 
			ToastIcon.MicrophoneMute => Resources.icons8_mute_unmute_96, 
			ToastIcon.Touchpad => Resources.icons8_touchpad_96, 
			ToastIcon.FnLock => Resources.icons8_function, 
			ToastIcon.Battery => Resources.icons8_charged_battery_96, 
			ToastIcon.Charger => Resources.icons8_charging_battery_96, 
			ToastIcon.Controller => Resources.icons8_controller_96, 
			_ => null, 
		};
		int num = 0;
		if (bitmap != null)
		{
			e.Graphics.DrawImage(bitmap, 18, 18, 64, 64);
			num = 40;
		}
		e.Graphics.DrawString(toastText, _toastFont, _toastTextBrush, new PointF(Bound.Width / 2 + num, Bound.Height / 2), _toastFormat);
	}

	internal static void ShowFailure(string message)
	{
		FailureCallback?.Invoke(message);
		try
		{
			Program.toast?.RunToast(message, null, essential: true);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Failure toast failed: " + ex.Message);
		}
	}

	internal static void ShowNotice(string message)
	{
		NoticeCallback?.Invoke(message);
		try
		{
			Program.toast?.RunToast(message, null, essential: true);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Notice toast failed: " + ex.Message);
		}
	}

	/// <summary>
	/// ⭐ 弹一条 LM 自己的 OSD 提示（⭐ <c>OSDNativeForm</c> 派生 ✅）。
	/// <para>⭐⭐ 2026-10-16（宝龙达 · OSD 提示 · 本地方案）：⭐ 快捷开关里的「⭐ OSD」在**宝龙达**上改为
	/// **管这条 OSD** ✅ —— ⭐ <b>机主原话</b>「⭐ OSD显示开关不可用（⭐ <i>LM好像有自己的OSD部分</i> ✅）」
	/// ⇒ ⭐ 后半句就是修法 ✅。</para>
	/// <para>⭐ 口径与 <c>QuickSwitches["osd"]</c> 一致（⭐ ⛔ 不翻转）：⭐ 开 ⇒ ⭐ **显示** ✅ ·
	/// ⭐ 关 ⇒ ⭐ **隐藏** ✅（⭐ 原语义是"⭐ 隐藏**厂商** OSD"，⭐ 而那只在 GCU 机型上存在 ⚠️）。</para>
	/// <para>⚠️ <paramref name="essential" /> ＝ <c>true</c>（⭐ 出错/通知 ✅）⭐ **不受开关影响** ✅ ——
	/// ⭐ 否则用户会把「⭐ 出错了」⭐ 一起静音掉 ⚠️ ⇒ ⭐ 那是有害的 ❌。</para>
	/// <para>⭐ 非宝龙达 ⇒ ⭐ **一行行为都不变** ✅（⭐ 逐位一致 ✅）。</para>
	/// </summary>
	/// <param name="text">⭐ 提示文字。</param>
	/// <param name="icon">⭐ 可选图标。</param>
	/// <param name="essential">⭐ <c>true</c> ＝ **必须显示**（⭐ 错误/通知类 ✅）；⭐ 默认 <c>false</c> ＝ 受 OSD 开关约束 ✅。</param>
	public void RunToast(string text, ToastIcon? icon = null, bool essential = false)
	{
		// ⭐ 宝龙达 + 开关关（`osd_hidden=1`）⇒ ⭐ 静默跳过 ✅
		//   ⚠️ 刻意**不记日志**：⭐ 这是**正常**行为 ⚠️ ⇒ ⭐ 记了就是刷屏 ❌
		if (!essential && _isBaolongdaBoard.Value && AppConfig.Get("osd_hidden", 0) != 0)
		{
			return;
		}
		Program.settingsForm.Invoke(() =>
		{
			timer.Stop();
			toastText = text;
			toastIcon = icon;
			nint threadDpiAwarenessContext = User32.SetThreadDpiAwarenessContext(-4);
			Screen screen = Screen.FromHandle(Handle);
			int _textW = TextRenderer.MeasureText(toastText, _toastFont).Width;
			Width = Math.Max(300, 100 + _textW + (icon != null ? 90 : 0));
			Height = 100;
			X = (screen.Bounds.Width - Width) / 2;
			Y = screen.Bounds.Height - 300 - Height;
			Show();
			User32.SetThreadDpiAwarenessContext(threadDpiAwarenessContext);
			timer.Start();
			Program.settingsForm.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent, text);
		});
	}

	private void timer_Tick(object? sender, EventArgs e)
	{
		Hide();
		timer.Stop();
	}
}
