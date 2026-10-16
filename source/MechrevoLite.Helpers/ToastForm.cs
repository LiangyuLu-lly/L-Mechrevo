using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Forms.Automation;
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
			Program.toast?.RunToast(message);
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
			Program.toast?.RunToast(message);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Notice toast failed: " + ex.Message);
		}
	}

	public void RunToast(string text, ToastIcon? icon = null)
	{
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
