using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using System.Windows.Forms;
using MechrevoLite.Helpers;
using Microsoft.Win32;
using PawnIO;

namespace MechrevoLite.Overlay;

public class HardwareOverlay : OSDNativeForm
{
	private delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

	private enum OverlayMode
	{
		Default,
		Light,
		Full,
		Complete
	}

	private const uint EVENT_SYSTEM_FOREGROUND = 3u;

	private const uint WINEVENT_OUTOFCONTEXT = 0u;

	private const uint WINEVENT_SKIPOWNPROCESS = 2u;

	private const int GWL_EXSTYLE = -20;

	private const int WS_EX_TRANSPARENT_FLAG = 32;

	private const int WM_LBUTTONDOWN = 513;

	private const int WM_MOUSEMOVE = 512;

	private const int WM_LBUTTONUP = 514;

	private const int WM_MOUSEWHEEL = 522;

	private const int WM_MBUTTONDOWN = 519;

	private const int VK_CONTROL = 17;

	private const int VK_SHIFT = 16;

	private const int VK_MENU = 18;

	private const int MinScalePercent = 35;

	private const int MaxScalePercent = 300;

	private const int ScaleStepPercent = 10;

	private int _scalePercent = 100;

	private const float BaseScale = 2f;

	private bool _dragging;

	private Point _dragCursorStart;

	private Point _dragWindowStart;

	private bool _dragModeActive;

	private OverlayMode _mode;

	private const float BaseFontSize = 13f;

	private const float BaseRpmFontSize = 8.5f;

	private const int BaseLineHeight = 18;

	private const int BaseLineSpacing = 1;

	private const int BasePadX = 8;

	private const int BasePadY = 4;

	private const int BaseFpsColWidth = 52;

	private const int BaseLeftColWidth = 144;

	private const int BaseChartColWidth = 120;

	private const int BasePowerGap = 4;

	private const int BasePowerExtraGap = 16;

	private const int BasePowerColWidth = 46;

	private const int BaseColGap = 8;

	private const int CornerRadius = 3;

	private const int MarginFromEdge = 10;

	private const int BaseLightLeftColWidth = 96;

	private const int BaseUsageBarGap = 11;

	private const int BaseUsageBarWidth = 5;

	private const int BaseUsageNumGap = 4;

	private const int BaseUsageNumColWidth = 30;

	private const int BaseFullPadRight = 4;

	private const int BaseUsageBarHeight = 15;

	private const int BaseUsageBarYNudge = 1;

	private const int BaseNameColWidth = 90;

	private const int BaseMemBarGap = 8;

	private const int BaseMemNumColWidth = 54;

	private const int BaseBatColGap = 8;

	private const int BaseBatColWidth = 38;

	private const int BaseBatIconGap = 2;

	private const int BaseBatIconWidth = 8;

	private const int BaseBatIconHeight = 15;

	private const int BaseBatNubWidth = 4;

	private const int BaseBatNubHeight = 2;

	private const int BaseLightWidth = 222;

	private const int BaseWidth = 398;

	private const int BaseFullWidth = 444;

	private const int BaseCompleteWidth = 515;

	private static readonly Color DefaultGpuColor = Color.FromArgb(255, 0, 255, 80);

	private static readonly Color DefaultCpuColor = Color.FromArgb(255, 60, 220, 255);

	private static readonly SolidBrush _batBrush = new SolidBrush(Color.FromArgb(255, 235, 235, 235));

	private static readonly SolidBrush _batDimBrush = new SolidBrush(Color.FromArgb(128, 78, 78, 78));

	private const int DragMinAlpha = 110;

	private static readonly SolidBrush _dragBgBrush = new SolidBrush(Color.FromArgb(110, 0, 0, 0));

	private int _bgAlpha = 128;

	private SolidBrush _bgBrush = new SolidBrush(Color.FromArgb(128, 0, 0, 0));

	private SolidBrush _gpuBrush = new SolidBrush(DefaultGpuColor);

	private SolidBrush _cpuBrush = new SolidBrush(DefaultCpuColor);

	private Pen _gpuLinePen = new Pen(DefaultGpuColor, 1.5f);

	private Pen _cpuLinePen = new Pen(DefaultCpuColor, 1.5f);

	private SolidBrush _gpuFillBrush = new SolidBrush(Color.FromArgb(128, 0, 85, 27));

	private SolidBrush _cpuFillBrush = new SolidBrush(Color.FromArgb(128, 20, 73, 85));

	private float _lastScale;

	private float _charW;

	private Font? _font;

	private Font? _rpmFont;

	private Pen? _totalPen;

	private Pen? _axPen;

	private readonly PointF[] _basePts = new PointF[60];

	private readonly PointF[] _cpuPts = new PointF[60];

	private readonly PointF[] _gpuPts = new PointF[60];

	private readonly PointF[] _polyPts = new PointF[120];

	private string _gpuTempStr = "";

	private string _cpuTempStr = "";

	private string _gpuFanNum = "";

	private string _cpuFanNum = "";

	private string _gpuPow = "";

	private string _cpuPow = "";

	private int? _gpuUsage;

	private int? _cpuUsage;

	private int? _vramUsage;

	private int? _ramUsage;

	private int? _vramUsedMb;

	private int? _ramUsedMb;

	private string _gpuShortName = "";

	private string _cpuShortName = "";

	private string _batLevel = "";

	private string _batRate = "";

	private int _batPercent;

	private bool _batCharging;

	private static readonly PointF[] _boltShape = new PointF[6]
	{
		new PointF(0.6f, 0f),
		new PointF(0.15f, 0.55f),
		new PointF(0.45f, 0.55f),
		new PointF(0.35f, 1f),
		new PointF(0.85f, 0.45f),
		new PointF(0.5f, 0.45f)
	};

	private static readonly PointF[] _boltPts = new PointF[6];

	private static readonly SolidBrush _batBoltBrush = new SolidBrush(Color.FromArgb(255, 25, 25, 25));

	private const int HistoryLength = 60;

	private readonly float[] _cpuHistory = new float[60];

	private readonly float[] _gpuHistory = new float[60];

	private int _historyHead;

	private readonly System.Timers.Timer _timer = new System.Timers.Timer(1000.0)
	{
		AutoReset = true
	};

	private int _resourcesDisposed;

	private int _lastFgPid;

	private bool _active;

	private bool _gameOnly;

	private bool _showNames;

	private bool _showFps;

	private bool _showTemp;

	private bool _showFans;

	private bool _showChart;

	private bool _showPower;

	private bool _showUsage;

	private bool _showRam;

	private bool _showBattery;

	private bool _onBattery;

	private int _overlayBattery = -1;

	private bool _hidden;

	private int _shownPid;

	private bool _fgDesktop;

	private nint _fgHook;

	private WinEventProc? _fgHookProc;

	private int _gameTicks;

	private string? _lastPaintedFingerprint;

	/// <summary>
	/// ⭐ 2026-10-18（机主：悬浮窗 CPU 温度停在开机时的温度）：⭐ 上一帧 CPU 温度是否被判「过期」✅
	/// （⭐ 只在**状态翻转**时各记一行，⛔ 不重复刷屏 ✅）。
	/// </summary>
	private bool _cpuTempStale;

	private static readonly HashSet<string> DesktopApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "iexplore", "chromium", "librewolf", "arc",
		"waterfox", "thorium", "WindowsTerminal", "conhost", "cmd", "powershell", "pwsh", "alacritty", "wezterm-gui", "mintty",
		"discord", "slack", "Teams", "ms-teams", "Spotify", "WhatsApp", "Signal", "Telegram", "Code", "Notion",
		"obsidian", "zoom", "Skype", "Element", "Viber", "LINE", "WeChat", "notepad", "notepad++", "sublime_text",
		"devenv", "rider64", "idea64", "pycharm64", "webstorm64", "steam", "steamwebhelper", "EpicGamesLauncher", "Battle.net", "GalaxyClient",
		"EADesktop", "UbisoftConnect", "vlc", "mpv", "mpc-hc64", "mpc-be64", "PotPlayerMini64", "wmplayer", "smplayer", "foobar2000",
		"aimp", "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "Acrobat", "AcroRd32", "SumatraPDF", "thunderbird", "Mailspring",
		"OneNote", "GitHubDesktop", "7zFM", "WinRAR", "SnippingTool", "explorer", "ShellExperienceHost", "ShellHost", "SearchHost", "StartMenuExperienceHost",
		"ApplicationFrameHost", "SystemSettings", "Taskmgr", "PowerToys.PowerLauncher", "PowerToys.Settings", "PowerToys.ColorPickerUI", "PowerToys.FancyZonesEditor", "PowerToys.Peek.UI", "PowerToys.PowerOCR", "PowerToys.MeasureToolUI",
		"PowerToys.ImageResizer", "PowerToys.PowerRename", "PowerToys.AdvancedPaste", "Microsoft.CmdPal.UI"
	};

	// ⭐ 2026-10-18（机主：悬浮窗没有置顶）：⭐ 置顶改由基类 `OSDNativeForm.TopMost` / `AssertTopMost()` 统一实现
	//   （⚠️ 原来这里的 `SetWindowPos(..., HWND_TOPMOST, ..., 19)` 只在 WS_EX_TOPMOST 位"丢失"时才跑 ⇒ 实际从未跑过 ❌）。

	private const int WM_NCDESTROY = 130;

	private const int WM_SETCURSOR = 32;

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

	[DllImport("user32.dll")]
	private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

	[DllImport("user32.dll")]
	private static extern bool UnhookWinEvent(nint hWinEventHook);

	[DllImport("user32.dll")]
	private static extern int GetWindowLong(nint hWnd, int nIndex);

	[DllImport("user32.dll")]
	private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll")]
	private static extern bool SetCapture(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	public HardwareOverlay()
	{
		Alpha = byte.MaxValue;
		_timer.Elapsed += (object? _, ElapsedEventArgs _) =>
		{
			Tick();
		};
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 130)
		{
			base.WndProc(ref m);
			if (_active)
			{
				Program.settingsForm?.BeginInvoke(() =>
				{
					RestorePosition();
					base.Show();
					ApplyDragMode(_dragModeActive);
				});
			}
		}
		else if (m.Msg == 32)
		{
			Cursor.Current = (_dragging ? Cursors.SizeAll : Cursors.Hand);
			m.Result = 1;
		}
		else if (m.Msg == 522 && AreDragKeysDown())
		{
			int num = (short)((((IntPtr)m.WParam).ToInt64() >> 16) & 0xFFFF);
			ApplyScale(_scalePercent + ((num > 0) ? 10 : (-10)));
			m.Result = IntPtr.Zero;
		}
		else if (m.Msg == 519 && AreDragKeysDown())
		{
			ApplyScale(100);
			m.Result = IntPtr.Zero;
		}
		else if (m.Msg == 513 && _dragModeActive)
		{
			_dragging = true;
			_dragCursorStart = Cursor.Position;
			_dragWindowStart = Location;
			SetCapture(Handle);
			m.Result = IntPtr.Zero;
		}
		else if (m.Msg == 512 && _dragging)
		{
			Point position = Cursor.Position;
			int value = _dragWindowStart.X + position.X - _dragCursorStart.X;
			int value2 = _dragWindowStart.Y + position.Y - _dragCursorStart.Y;
			Screen screen = Screen.FromPoint(position);
			value = Math.Clamp(value, screen.Bounds.Left + 5, screen.Bounds.Right - Width - 5);
			value2 = Math.Clamp(value2, screen.Bounds.Top + 5, screen.Bounds.Bottom - Height - 5);
			Location = new Point(value, value2);
			m.Result = IntPtr.Zero;
		}
		else if (m.Msg == 514 && _dragging)
		{
			_dragging = false;
			ReleaseCapture();
			Point position2 = Cursor.Position;
			if (Math.Abs(position2.X - _dragCursorStart.X) <= 5 && Math.Abs(position2.Y - _dragCursorStart.Y) <= 5)
			{
				Point point = new Point(Location.X + Width / 2, Location.Y + Height / 2);
				Screen screen2 = Screen.FromPoint(point);
				bool flag = point.X > screen2.Bounds.X + screen2.Bounds.Width / 2;
				int num2 = Location.X + Width;
				_mode = _mode switch
				{
					OverlayMode.Light => OverlayMode.Default, 
					OverlayMode.Default => OverlayMode.Full, 
					OverlayMode.Full => OverlayMode.Complete, 
					_ => OverlayMode.Light, 
				};
				AppConfig.Set("overlay_mode", (int)_mode);
				ApplyPreset(_mode);
				ApplySensorFlags();
				UpdateOverlaySize();
				Invalidate();
				if (flag)
				{
					Location = new Point(num2 - Width, Location.Y);
					SavePosition();
				}
			}
			else
			{
				SavePosition();
			}
			m.Result = IntPtr.Zero;
		}
		else
		{
			base.WndProc(ref m);
		}
	}

	private float GetScale()
	{
		return 2f * ((float)_scalePercent / 100f);
	}

	private static int S(float sc, int v)
	{
		return (int)((float)v * sc);
	}

	private static double D(object? v)
	{
		try
		{
			return (v == null) ? 0.0 : Convert.ToDouble(v);
		}
		catch
		{
			return 0.0;
		}
	}

	private static string FmtTemp(double t)
	{
		if (!(t > 0.0))
		{
			return " -- ";
		}
		return ((int)Math.Round(t) + "°").PadLeft(4);
	}

	internal static string FormatPowerText(float? power)
	{
		if (!power.HasValue || !(power.GetValueOrDefault() > 0f))
		{
			return "";
		}
		return Math.Round(power.Value, 1).ToString("F1") + "W";
	}

	private static string ShortGpuName(string? full)
	{
		if (string.IsNullOrEmpty(full))
		{
			return "";
		}
		string[] array = new string[4] { "RTX", "GTX", "RX", "Arc" };
		foreach (string value in array)
		{
			int num = full.IndexOf(value, StringComparison.OrdinalIgnoreCase);
			if (num >= 0)
			{
				string[] array2 = full.Substring(num).Split(' ', StringSplitOptions.RemoveEmptyEntries);
				string text = ((array2.Length >= 2) ? (array2[0] + " " + array2[1]) : array2[0]);
				if (array2.Length >= 3 && array2[2] == "Ti")
				{
					text += " Ti";
				}
				return text;
			}
		}
		return full;
	}

	private static string ShortCpuName(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return "";
		}
		Match match = Regex.Match(name, "i[3579]-\\w+");
		if (match.Success)
		{
			return match.Value;
		}
		match = Regex.Match(name, "Ultra\\s+\\d+\\s+(\\w*\\d\\w*)");
		if (match.Success)
		{
			return "Ultra " + match.Groups[1].Value;
		}
		if (name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
		{
			match = Regex.Match(name, "(?:[A-Z]{2,}\\s+)?\\d{3,}\\w*");
			if (!match.Success)
			{
				return "Ryzen";
			}
			return "Ryzen " + match.Value;
		}
		string[] array = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (array == null || array.Length <= 0)
		{
			return "";
		}
		return array[0];
	}

	private static string FormatFan(int? fan)
	{
		if (!fan.HasValue || fan < 0)
		{
			return "";
		}
		return fan.ToString();
	}

	private void Tick()
	{
		bool flag = true;
		if (flag != _dragModeActive && !_dragging)
		{
			ApplyDragMode(flag);
		}
		if (Handle != IntPtr.Zero && OverlayZOrderPolicy.ShouldReassertTopmost((GetWindowLong(Handle, -20) & 8) != 0))
		{
			// ⭐⭐ 2026-10-18（机主反馈「悬浮窗没有置顶、会被其他窗口盖住」）：
			//   ⚠️ 以前这句只在 `WS_EX_TOPMOST` 位**丢失**时才 `SetWindowPos(HWND_TOPMOST, …)` ——
			//      而那个位在窗口创建时就写在 `CreateParams.ExStyle` 里 ⚠️ ⇒ 条件恒假 ⇒ **一次都没执行过** ❌。
			//   ⭐ 现在策略改成「每帧重申」（见 `OverlayZOrderPolicy`），并统一走基类的
			//      `OSDNativeForm.AssertTopMost()`（⭐ `SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE` ⇒ ⛔ 不抢焦点 ✅）。
			AssertTopMost();
		}
		GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
		int num = (int)processId;
		bool flag2 = num == 0 || num == Environment.ProcessId;
		_lastFgPid = num;
		_fgDesktop = flag2 || IsDesktopApp(num);
		if (_gameOnly)
		{
			UpdateGameVisibility(num);
			if (_hidden)
			{
				return;
			}
		}
		HardwareControl.ReadSensorsOverlay();
		double num2 = D(HardwareControl.gpuTemp);
		// ⭐⭐ 2026-10-18（机主反馈「开机很久了，悬浮窗 CPU 温度还停在开机时的温度」）：
		//   ⚠️ 原来这里直接把 `HardwareControl.cpuTemp` 当实时值画出来、**没有任何新鲜度判据** ❌
		//   ⇒ ⭐ 采样链一旦不再刷新它（真机根因：LHM 闸门失败 ⇒ `ApplySensorPriority` 整段不跑），
		//   ⭐ 开机时那个值就会被**永远**当成实时温度显示 ⚠️。
		//   ⭐ 修法：⭐ 先查时间戳（`HardwareControl.CpuTempUpdatedAt` 体系）⇒ ⭐ 超 10 s 未更新 ⇒
		//   ⭐ 显示「—」、⛔ **不显示陈旧值**（⭐ 与主界面 `TelemetryTemp` 的「不新鲜 ⇒ —」同口径 ✅）。
		bool cpuTempFresh = HardwareControl.CpuTempIsFresh(HardwareControl.CpuTempFreshAge);
		LogCpuTempFreshness(cpuTempFresh);
		double t = (cpuTempFresh ? D(HardwareControl.cpuTemp) : 0.0);
		bool flag3 = num2 > 0.0;
		_gpuTempStr = "GPU:" + FmtTemp(num2) + fmtFreq(HardwareControl.gpuCoreFreq) + " ";
		_cpuTempStr = "CPU:" + (cpuTempFresh ? FmtTemp(t) : " — ") + fmtFreq(HardwareControl.cpuFrequency) + " ";
		_gpuFanNum = FormatFan(HardwareControl.gpuFanRPM);
		_cpuFanNum = FormatFan(HardwareControl.cpuFanRPM);
		_gpuPow = FormatPowerText(HardwareControl.gpuPower);
		_cpuPow = FormatPowerText(HardwareControl.cpuPower);
		_cpuHistory[_historyHead] = (float)Math.Max(0.0, D(HardwareControl.cpuPower));
		_gpuHistory[_historyHead] = (flag3 ? ((float)Math.Max(0.0, D(HardwareControl.gpuPower))) : 0f);
		_historyHead = (_historyHead + 1) % 60;
		_cpuUsage = HardwareControl.cpuUsage;
		_gpuUsage = (flag3 ? new int?(HardwareControl.gpuUsage.GetValueOrDefault()) : ((int?)null));
		_vramUsage = (flag3 ? new int?(HardwareControl.vramUsage.GetValueOrDefault()) : ((int?)null));
		_vramUsedMb = (flag3 ? new int?(HardwareControl.vramUsedMb.GetValueOrDefault()) : ((int?)null));
		_ramUsage = HardwareControl.ramUsage;
		_ramUsedMb = HardwareControl.ramUsedMb;
		if (_showBattery)
		{
			bool flag4 = _overlayBattery == 1 || SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;
			if (flag4 != _onBattery)
			{
				int num3 = Location.X + Width;
				Point point = new Point(Location.X + Width / 2, Location.Y + Height / 2);
				Screen screen = Screen.FromPoint(point);
				bool flag5 = point.X > screen.Bounds.X + screen.Bounds.Width / 2;
				_onBattery = flag4;
				ApplySensorFlags();
				UpdateOverlaySize();
				if (flag5)
				{
					Location = new Point(num3 - Width, Location.Y);
				}
			}
			if (_onBattery)
			{
				double num4 = HardwareControl.GetBatteryChargePercentage();
				_batPercent = (int)Math.Round(num4);
				_batLevel = ((num4 > 0.0) ? (_batPercent + "%") : "--");
				decimal valueOrDefault = HardwareControl.batteryRate.GetValueOrDefault();
				_batRate = ((valueOrDefault == 0m) ? "" : (((valueOrDefault > 0m) ? "+" : "-") + Math.Abs(valueOrDefault).ToString("F1")));
				_batCharging = valueOrDefault > 0m;
			}
		}
		if (_showNames)
		{
			if (_cpuShortName.Length == 0)
			{
				_cpuShortName = ShortCpuName(CpuInfo.Name);
			}
			if (_gpuShortName.Length == 0)
			{
				_gpuShortName = ShortGpuName(HardwareControl.GpuControl?.FullName);
			}
		}
		_003C_003Ey__InlineArray19<object> buffer = default;
		buffer[0] = _gpuTempStr;
		buffer[1] = _cpuTempStr;
		buffer[2] = _gpuFanNum;
		buffer[3] = _cpuFanNum;
		buffer[4] = _gpuPow;
		buffer[5] = _cpuPow;
		buffer[6] = _gpuUsage;
		buffer[7] = _cpuUsage;
		buffer[8] = _vramUsage;
		buffer[9] = _vramUsedMb;
		buffer[10] = _ramUsage;
		buffer[11] = _ramUsedMb;
		buffer[12] = _gpuShortName;
		buffer[13] = _cpuShortName;
		buffer[14] = _batLevel;
		buffer[15] = _batRate;
		buffer[16] = _batPercent;
		buffer[17] = _batCharging;
		buffer[18] = _onBattery;
		string text = string.Join('\n', (ReadOnlySpan<object?>)buffer);
		if (OverlayPaintPolicy.ShouldInvalidate(_lastPaintedFingerprint, text))
		{
			_lastPaintedFingerprint = text;
			Invalidate();
		}
		static string fmtFreq(int mhz)
		{
			if (mhz <= 0)
			{
				return "";
			}
			return " " + ((double)mhz / 1000.0).ToString("F1") + "G";
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主反馈「开机很久了，悬浮窗 CPU 温度还停在开机时的温度」）：
	/// ⭐ <b>CPU 温度新鲜度日志</b> ✅ —— ⭐ 过期/恢复时各记一行，⭐ 过期期间**每秒最多一条**
	/// <c>[float] cpu temp stale age=…s</c>（⭐ 判据每秒都在跑 ⇒ 用 1 s 限流，⛔ 不刷屏 ✅）。
	/// <para>⚠️ 与显示判据**同一套时间戳**（<see cref="HardwareControl.CpuTempIsFresh" /> /
	/// <see cref="HardwareControl.CpuTempAgeMs" />）⇒ ⭐ 日志里看到的 age 就是"为什么显示「—」"的直接证据 ✅。</para>
	/// </summary>
	private void LogCpuTempFreshness(bool fresh)
	{
		long ageMs = HardwareControl.CpuTempAgeMs;
		string ageText = ((ageMs < 0L) ? "never" : (ageMs / 1000.0).ToString("F1") + "s");
		if (!fresh)
		{
			if (!_cpuTempStale)
			{
				// ⭐ 状态翻转：⭐ 用 `WriteError` ⇒ ⭐ 默认「仅错误」级别下也可见（诊断包能直接定位）✅
				_cpuTempStale = true;
				Logger.WriteError("[float] cpu temp stale age=" + ageText
					+ " ⇒ 悬浮窗 CPU 温度显示「—」（⛔ 不再拿陈旧值冒充实时值；⭐ 超过 "
					+ HardwareControl.CpuTempFreshAge.TotalSeconds.ToString("F0") + " s 未更新即判过期）");
			}
			else
			{
				Logger.WriteLineThrottled("float-cpu-temp-stale", "[float] cpu temp stale age=" + ageText, 1000);
			}
		}
		else if (_cpuTempStale)
		{
			_cpuTempStale = false;
			Logger.WriteLine("[float] cpu temp fresh again age=" + ageText + "（⭐ 恢复实时显示）");
		}
	}

	private void UpdateGameVisibility(int fgPid)
	{
		_gameTicks = ((!_fgDesktop) ? (_gameTicks + 1) : 0);
		if (_gameTicks >= 2)
		{
			_shownPid = fgPid;
		}
		bool flag = fgPid == _shownPid;
		if (flag == _hidden)
		{
			_hidden = !flag;
			if (Handle != IntPtr.Zero)
			{
				User32.ShowWindow(Handle, (short)((!_hidden) ? 4 : 0));
			}
		}
	}

	private void OnForegroundChanged()
	{
		if (!_active || !_gameOnly)
		{
			return;
		}
		GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
		int num = (int)processId;
		if (num == 0 || num == Environment.ProcessId)
		{
			return;
		}
		bool flag = num == _shownPid;
		if (flag == _hidden)
		{
			_hidden = !flag;
			if (Handle != IntPtr.Zero)
			{
				User32.ShowWindow(Handle, (short)((!_hidden) ? 4 : 0));
			}
		}
	}

	private static bool IsDesktopApp(int pid)
	{
		try
		{
			using Process process = Process.GetProcessById(pid);
			return DesktopApps.Contains(process.ProcessName);
		}
		catch
		{
			return false;
		}
	}

	protected override void PerformPaint(PaintEventArgs e)
	{
		float scale = GetScale();
		int num = S(scale, 8);
		int num2 = S(scale, 4);
		int num3 = S(scale, 18);
		int num4 = S(scale, 1);
		int cornerRadius = S(scale, 3);
		int num5 = S(scale, 52);
		int num6 = S(scale, 120);
		int num7 = S(scale, 46);
		int num8 = S(scale, 8);
		int num9 = S(scale, 4);
		int num10 = num3 * 2 + num4;
		bool showFps = _showFps;
		bool showTemp = _showTemp;
		bool showFans = _showFans;
		bool showChart = _showChart;
		bool showPower = _showPower;
		bool showUsage = _showUsage;
		bool showRam = _showRam;
		bool showNames = _showNames;
		bool flag = _showBattery && _onBattery;
		bool flag2 = _showUsage || _showRam;
		int num11 = (showNames ? S(scale, 90) : 0);
		int num12 = S(scale, 30);
		int num13 = S(scale, 5);
		int num14 = S(scale, 54);
		int num15 = S(scale, 38);
		int num16 = num;
		if (showFps)
		{
			num16 += num5 + num8;
		}
		int num17 = num16;
		if (showNames)
		{
			num16 += num11 + num8;
		}
		int num18 = num16;
		num16 += S(scale, (showTemp & showFans) ? 144 : ((showTemp | showFans) ? 96 : 0));
		int x = num16;
		if (showChart)
		{
			num16 += num6;
		}
		int num19 = num16;
		if (showPower)
		{
			num19 = num16 + num9 + S(scale, 16);
			num16 = num19 + num7;
		}
		int num20 = num16;
		int num21 = num16;
		if (showUsage)
		{
			num20 = num16 + S(scale, 11);
			num21 = num20 + num12 + S(scale, 4);
			num16 = num21 + num13;
		}
		int num22 = num16;
		int num23 = num16;
		if (showRam)
		{
			num22 = num16 + S(scale, 8);
			num23 = num22 + num14 + S(scale, 4);
			num16 = num23 + num13;
		}
		int num24 = num16;
		int num25 = num16;
		if (flag)
		{
			num24 = num16 + S(scale, 8);
			num25 = num24 + num15 + S(scale, 2);
			num16 = num25 + S(scale, 8);
		}
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		// 2026-10-09（P3「统一不设 PixelOffsetMode」）：这里原来显式设 `PixelOffsetMode.HighQuality`
		// —— 它会把**采样网格整体偏移半个像素**（把整张覆盖层的绘制挪半像素），与外接层所有
		// `(int)Math.Round(...)` 出来的整数坐标/整数宽度笔画相冲：直线段会从"落在整像素列"
		// 变成"跨两列各 50%"，观感就是**发虚**（热键标记 `_totalPen` 的点虚线尤其明显）。
		// 全仓统一口径 = **不设**（GDI+ 默认 `PixelOffsetMode.None`，即"不平移、按原样取样"），
		// 与 RButton / RComboBox / 全部自绘控件一致 ⇒ 覆盖层的边框/点虚线不再被半像素摊薄。
		// ⚠️ 删除本行只影响"取样的相位"，不改任何坐标、颜色、字号、线宽与布局。
		graphics.TextRenderingHint = ((_scalePercent <= 75) ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAliasGridFit);
		graphics.FillRoundedRectangle((_dragModeActive && _bgAlpha < 110) ? _dragBgBrush : _bgBrush, Bound, cornerRadius);
		if (scale != _lastScale)
		{
			_lastScale = scale;
			_font?.Dispose();
			_font = new Font("Consolas", 13f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
			_rpmFont?.Dispose();
			_rpmFont = new Font("Consolas", 8.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
			_totalPen?.Dispose();
			_totalPen = new Pen(Color.FromArgb(255, 200, 200, 200), scale * 0.75f)
			{
				DashStyle = DashStyle.Dot
			};
			_axPen?.Dispose();
			_axPen = new Pen(Color.FromArgb(255, 80, 80, 80), scale * 0.5f);
			_charW = graphics.MeasureString("XX", _font).Width - graphics.MeasureString("X", _font).Width;
		}
		Font font = _font;
		Font rpmFont = _rpmFont;
		float charW = _charW;
		int num26 = num2;
		int num27 = num26 + (int)Math.Round(scale);
		if (showNames)
		{
			GraphicsState gstate = graphics.Save();
			graphics.SetClip(new RectangleF(num17, num26, num11, num10));
			graphics.DrawString(_gpuShortName, font, _gpuBrush, new PointF(num17, num27));
			graphics.DrawString(_cpuShortName, font, _cpuBrush, new PointF(num17, num27 + num3 + num4));
			graphics.Restore(gstate);
		}
		DrawTempFan(graphics, font, rpmFont, charW, scale, num18, num27, showTemp ? _gpuTempStr : "", showFans ? _gpuFanNum : "", _gpuBrush);
		DrawTempFan(graphics, font, rpmFont, charW, scale, num18, num27 + num3 + num4, showTemp ? _cpuTempStr : "", showFans ? _cpuFanNum : "", _cpuBrush);
		if (showChart)
		{
			DrawStackedChart(graphics, x, num26, num6, num10, scale);
		}
		if (showPower && _gpuPow.Length > 0)
		{
			graphics.DrawString(_gpuPow, font, _gpuBrush, new PointF((float)(num19 + num7) - graphics.MeasureString(_gpuPow, font).Width, num27));
		}
		if (showPower && _cpuPow.Length > 0)
		{
			graphics.DrawString(_cpuPow, font, _cpuBrush, new PointF((float)(num19 + num7) - graphics.MeasureString(_cpuPow, font).Width, num27 + num3 + num4));
		}
		if (flag2)
		{
			int num28 = Math.Max(1, (int)Math.Floor(scale));
			int num29 = 1;
			int num30 = S(scale, 15);
			int num31 = num28 + num29;
			int num32 = Math.Max(2, (num30 + num29) / num31);
			int num33 = num32 * num28 + (num32 - 1) * num29;
			int num34 = (num3 - num33) / 2 - S(scale, 1);
			int num35 = num3 + num4;
			if (showUsage)
			{
				DrawUsageBar(graphics, num21, num26 + num34, num13, num28, num29, num32, _gpuUsage.GetValueOrDefault(), _gpuBrush, _gpuFillBrush);
				DrawUsageBar(graphics, num21, num26 + num35 + num34, num13, num28, num29, num32, _cpuUsage.GetValueOrDefault(), _cpuBrush, _cpuFillBrush);
				DrawUsagePercent(graphics, font, num20, num12, num27, _gpuUsage, _gpuBrush);
				DrawUsagePercent(graphics, font, num20, num12, num27 + num35, _cpuUsage, _cpuBrush);
			}
			if (showRam)
			{
				DrawMemGb(graphics, font, num22, num14, num27, _vramUsedMb, _gpuBrush);
				DrawMemGb(graphics, font, num22, num14, num27 + num35, _ramUsedMb, _cpuBrush);
				DrawUsageBar(graphics, num23, num26 + num34, num13, num28, num29, num32, _vramUsage.GetValueOrDefault(), _gpuBrush, _gpuFillBrush);
				DrawUsageBar(graphics, num23, num26 + num35 + num34, num13, num28, num29, num32, _ramUsage.GetValueOrDefault(), _cpuBrush, _cpuFillBrush);
			}
		}
		if (flag)
		{
			if (_batLevel.Length > 0)
			{
				graphics.DrawString(_batLevel, font, _batBrush, new PointF((float)(num24 + num15) - graphics.MeasureString(_batLevel, font).Width, num27));
			}
			if (_batRate.Length > 0)
			{
				graphics.DrawString(_batRate, font, _batBrush, new PointF((float)(num24 + num15) - graphics.MeasureString(_batRate, font).Width, num27 + num3 + num4));
				graphics.DrawString("W", font, _batBrush, new PointF((float)num25 + ((float)S(scale, 8) - graphics.MeasureString("W", font).Width) / 2f, num27 + num3 + num4));
			}
			int num36 = S(scale, 15);
			DrawBatteryIcon(graphics, num25, num26 + (num3 - num36) / 2 - S(scale, 1), num36, scale, _batPercent, _batCharging);
		}
	}

	private static void DrawUsagePercent(Graphics g, Font font, int x, int colW, int y, int? usage, SolidBrush brush)
	{
		if (usage.HasValue)
		{
			string text = usage.Value + "%";
			g.DrawString(text, font, brush, new PointF((float)(x + colW) - g.MeasureString(text, font).Width, y));
		}
	}

	private static void DrawMemGb(Graphics g, Font font, int x, int colW, int y, int? usedMb, SolidBrush brush)
	{
		if (usedMb.HasValue)
		{
			string text = ((double)usedMb.Value / 1024.0).ToString("F1") + "GB";
			g.DrawString(text, font, brush, new PointF((float)(x + colW) - g.MeasureString(text, font).Width, y));
		}
	}

	private static void DrawBatteryIcon(Graphics g, int x, int y, int h, float sc, int level, bool charging)
	{
		SmoothingMode smoothingMode = g.SmoothingMode;
		g.SmoothingMode = SmoothingMode.None;
		int num = S(sc, 8);
		int num2 = Math.Max(2, S(sc, 4));
		int num3 = Math.Max(1, S(sc, 2));
		int num4 = Math.Max(1, (int)sc);
		int num5 = y + num3;
		int num6 = h - num3;
		g.FillRectangle(_batBrush, x + (num - num2) / 2, y, num2, num3);
		g.FillRectangle(_batBrush, x, num5, num, num4);
		g.FillRectangle(_batBrush, x, num5 + num6 - num4, num, num4);
		g.FillRectangle(_batBrush, x, num5, num4, num6);
		g.FillRectangle(_batBrush, x + num - num4, num5, num4, num6);
		int x2 = x + num4;
		int width = num - 2 * num4;
		int num7 = num5 + num4;
		int num8 = num6 - 2 * num4;
		int num9 = (int)Math.Round((double)(num8 * Math.Clamp(level, 0, 100)) / 100.0);
		if (num9 > 0)
		{
			g.FillRectangle(_batBrush, x2, num7 + num8 - num9, width, num9);
		}
		if (num9 < num8)
		{
			g.FillRectangle(_batDimBrush, x2, num7, width, num8 - num9);
		}
		if (charging)
		{
			g.SmoothingMode = SmoothingMode.AntiAlias;
			float num10 = (float)num * 0.9f;
			float num11 = (float)num6 * 0.64f;
			float num12 = (float)x + ((float)num - num10) / 2f;
			float num13 = (float)num5 + ((float)num6 - num11) / 2f;
			for (int i = 0; i < _boltShape.Length; i++)
			{
				_boltPts[i] = new PointF(num12 + _boltShape[i].X * num10, num13 + _boltShape[i].Y * num11);
			}
			g.FillPolygon(_batBoltBrush, _boltPts);
		}
		g.SmoothingMode = smoothingMode;
	}

	private static void DrawUsageBar(Graphics g, int x, int y, int w, int cellH, int sepH, int numCells, int usage, SolidBrush litBrush, SolidBrush dimBrush)
	{
		SmoothingMode smoothingMode = g.SmoothingMode;
		g.SmoothingMode = SmoothingMode.None;
		int num = Math.Clamp((int)Math.Ceiling((float)(usage * numCells) / 100f), 0, numCells);
		int num2 = cellH + sepH;
		for (int i = 0; i < numCells; i++)
		{
			bool flag = i >= numCells - num;
			g.FillRectangle(flag ? litBrush : dimBrush, x, y + i * num2, w, cellH);
		}
		g.SmoothingMode = smoothingMode;
	}

	private static void DrawTempFan(Graphics g, Font font, Font rpmFont, float charW, float sc, float x, float y, string tempStr, string fanNum, SolidBrush brush)
	{
		g.DrawString(tempStr, font, brush, new PointF(x, y));
		if (fanNum.Length != 0)
		{
			float num = x + charW * (float)tempStr.Length;
			g.DrawString(fanNum, font, brush, new PointF(num, y));
			float x2 = num + charW * (float)fanNum.Length + 2f * sc;
			g.DrawString("RPM", rpmFont, brush, new PointF(x2, y));
		}
	}

	private void DrawStackedChart(Graphics g, int x, int y, int w, int h, float sc)
	{
		float num = 10f;
		for (int i = 0; i < 60; i++)
		{
			float num2 = _cpuHistory[i] + _gpuHistory[i];
			if (num2 > num)
			{
				num = num2;
			}
		}
		float num3 = (float)w / 59f;
		for (int j = 0; j < 60; j++)
		{
			int num4 = Idx(j);
			float x2 = (float)x + (float)j * num3;
			float num5 = _cpuHistory[num4] / num * (float)h;
			float num6 = _gpuHistory[num4] / num * (float)h;
			_basePts[j] = new PointF(x2, y + h);
			_cpuPts[j] = new PointF(x2, (float)(y + h) - num5);
			_gpuPts[j] = new PointF(x2, (float)(y + h) - num5 - num6);
		}
		GraphicsState gstate = g.Save();
		g.SetClip(new RectangleF(x, y, w, h));
		FillArea(g, _cpuPts, _basePts, _cpuFillBrush);
		g.DrawLines(_cpuLinePen, _cpuPts);
		FillArea(g, _gpuPts, _cpuPts, _gpuFillBrush);
		g.DrawLines(_gpuLinePen, _gpuPts);
		g.DrawLines(_totalPen, _gpuPts);
		g.Restore(gstate);
		g.DrawLine(_axPen, x, y + h, x + w, y + h);
		int Idx(int num7)
		{
			return (_historyHead + num7) % 60;
		}
	}

	private void FillArea(Graphics g, PointF[] topLine, PointF[] bottomLine, SolidBrush brush)
	{
		int num = topLine.Length;
		topLine.CopyTo(_polyPts, 0);
		for (int i = 0; i < num; i++)
		{
			_polyPts[num + i] = bottomLine[num - 1 - i];
		}
		g.FillPolygon(brush, _polyPts);
	}

	private void PositionAtTopLeft()
	{
		Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
		Location = new Point(screen.Bounds.X + 10, screen.Bounds.Y + 10);
	}

	private static Screen TargetScreen()
	{
		string text = AppConfig.GetString("overlay_screen");
		if (!string.IsNullOrEmpty(text))
		{
			Screen[] allScreens = Screen.AllScreens;
			foreach (Screen screen in allScreens)
			{
				if (screen.DeviceName == text)
				{
					return screen;
				}
			}
		}
		return Screen.PrimaryScreen ?? Screen.AllScreens[0];
	}

	private bool AreDragKeysDown()
	{
		if ((GetAsyncKeyState(17) & 0x8000) != 0 && (GetAsyncKeyState(16) & 0x8000) != 0)
		{
			return (GetAsyncKeyState(18) & 0x8000) != 0;
		}
		return false;
	}

	private void ApplyDragMode(bool on)
	{
		_dragModeActive = on;
		SetTransparentStyle(!on);
		Cursor.Current = (on ? Cursors.Hand : Cursors.Default);
		if (_bgAlpha < 110)
		{
			Invalidate();
		}
	}

	private void SetTransparentStyle(bool transparent)
	{
		if (Handle != IntPtr.Zero)
		{
			int windowLong = GetWindowLong(Handle, -20);
			windowLong = (transparent ? (windowLong | 0x20) : (windowLong & -33));
			SetWindowLong(Handle, -20, windowLong);
		}
	}

	private void ApplyScale(int next)
	{
		next = Math.Clamp(next, 35, 300);
		if (next != _scalePercent)
		{
			Point point = new Point(Location.X + Width / 2, Location.Y + Height / 2);
			Screen screen = Screen.FromPoint(point);
			bool flag = point.X > screen.Bounds.X + screen.Bounds.Width / 2;
			bool flag2 = point.Y > screen.Bounds.Y + screen.Bounds.Height / 2;
			int num = Location.X + Width;
			int num2 = Location.Y + Height;
			_scalePercent = next;
			AppConfig.Set("overlay_scale_percent", _scalePercent);
			UpdateOverlaySize();
			Invalidate();
			int num3 = (flag ? (num - Width) : Location.X);
			int num4 = (flag2 ? (num2 - Height) : Location.Y);
			if (num3 != Location.X || num4 != Location.Y)
			{
				Location = new Point(num3, num4);
			}
			SavePosition();
		}
	}

	private void SavePosition()
	{
		Point point = new Point(Location.X + Width / 2, Location.Y + Height / 2);
		Screen screen = Screen.FromPoint(point);
		bool flag = point.X > screen.Bounds.X + screen.Bounds.Width / 2;
		bool flag2 = point.Y > screen.Bounds.Y + screen.Bounds.Height / 2;
		int value = (flag2 ? 2 : 0) | (flag ? 1 : 0);
		AppConfig.Set("overlay_screen", screen.Primary ? "" : screen.DeviceName);
		int value2 = (flag ? (screen.Bounds.Right - Location.X - Width) : (Location.X - screen.Bounds.X));
		int value3 = (flag2 ? (screen.Bounds.Bottom - Location.Y - Height) : (Location.Y - screen.Bounds.Y));
		AppConfig.Set("overlay_anchor", value);
		AppConfig.Set("overlay_offset_x", value2);
		AppConfig.Set("overlay_offset_y", value3);
	}

	private static Color ParseColor(string key, Color fallback)
	{
		string text = AppConfig.GetString(key);
		if (string.IsNullOrEmpty(text))
		{
			return fallback;
		}
		try
		{
			return ColorTranslator.FromHtml(text.StartsWith("#") ? text : ("#" + text));
		}
		catch
		{
			return fallback;
		}
	}

	private void ApplyColors()
	{
		SettingsForm settingsForm = Program.settingsForm;
		if (settingsForm != null && !settingsForm.IsDisposed && Program.settingsForm.InvokeRequired)
		{
			try
			{
				Program.settingsForm.BeginInvoke(ApplyColors);
				return;
			}
			catch
			{
				return;
			}
		}
		Color color = ParseColor("overlay_color_gpu", DefaultGpuColor);
		Color color2 = ParseColor("overlay_color_cpu", DefaultCpuColor);
		_bgAlpha = Math.Clamp(AppConfig.Get("overlay_alpha", 128), 0, 255);
		lock (PaintSync)
		{
			_gpuBrush.Dispose();
			_gpuBrush = new SolidBrush(color);
			_cpuBrush.Dispose();
			_cpuBrush = new SolidBrush(color2);
			_gpuLinePen.Dispose();
			_gpuLinePen = new Pen(color, 1.5f);
			_cpuLinePen.Dispose();
			_cpuLinePen = new Pen(color2, 1.5f);
			_gpuFillBrush.Dispose();
			_gpuFillBrush = new SolidBrush(Color.FromArgb(128, color.R / 3, color.G / 3, color.B / 3));
			_cpuFillBrush.Dispose();
			_cpuFillBrush = new SolidBrush(Color.FromArgb(128, color2.R / 3, color2.G / 3, color2.B / 3));
			_bgBrush.Dispose();
			_bgBrush = new SolidBrush(Color.FromArgb(_bgAlpha, 0, 0, 0));
		}
	}

	private void ApplyPreset(OverlayMode mode)
	{
		bool flag = mode == OverlayMode.Complete;
		bool flag2 = mode != OverlayMode.Light;
		_showFps = false;
		_showTemp = !flag || AppConfig.IsNotFalse("overlay_show_temp");
		_showFans = (flag ? AppConfig.IsNotFalse("overlay_show_fans") : flag2);
		_showChart = false;
		_showPower = !flag || AppConfig.IsNotFalse("overlay_show_power");
		_showUsage = (flag ? AppConfig.IsNotFalse("overlay_show_usage") : (mode == OverlayMode.Full));
		_showRam = flag && AppConfig.IsNotFalse("overlay_show_ram");
		_overlayBattery = (flag ? AppConfig.Get("overlay_show_battery") : (-1));
		_showBattery = (flag ? (_overlayBattery != 0) : flag2);
		_showNames = flag && AppConfig.Is("overlay_names");
	}

	private void ApplySensorFlags()
	{
		HardwareControl.readFans = _showFans;
		HardwareControl.readUsage = _showUsage;
		HardwareControl.readMemory = _showRam;
		HardwareControl.readPower = _showPower;
		HardwareControl.readBattery = _showBattery && _onBattery;
	}

	private void UpdateOverlaySize()
	{
		float scale = GetScale();
		int num = S(scale, 8);
		if (_showFps)
		{
			num += S(scale, 60);
		}
		if (_showNames)
		{
			num += S(scale, 98);
		}
		if (_showTemp || _showFans)
		{
			num += S(scale, (_showTemp && _showFans) ? 144 : 96);
		}
		if (_showChart)
		{
			num += S(scale, 120);
		}
		if (_showPower)
		{
			num += S(scale, 66);
		}
		if (_showUsage)
		{
			num += S(scale, 50);
		}
		if (_showRam)
		{
			num += S(scale, 71);
		}
		if (_showBattery && _onBattery)
		{
			num += S(scale, 56);
		}
		int height = S(scale, 4) * 2 + S(scale, 18) * 2 + S(scale, 1);
		Size size = new Size(num + S(scale, 8), height);
		if (Size != size)
		{
			Size = size;
		}
	}

	private void OnDisplaySettingsChanged(object? sender, EventArgs e)
	{
		if (!_active)
		{
			return;
		}
		Program.settingsForm?.BeginInvoke(() =>
		{
			if (_active)
			{
				RestorePosition();
			}
		});
	}

	private void RestorePosition()
	{
		int num = AppConfig.Get("overlay_anchor");
		if (num < 0)
		{
			PositionAtTopLeft();
			return;
		}
		int num2 = AppConfig.Get("overlay_offset_x", 10);
		int num3 = AppConfig.Get("overlay_offset_y", 10);
		Screen screen = TargetScreen();
		bool flag = (num & 1) != 0;
		bool flag2 = (num & 2) != 0;
		int val = (flag ? (screen.Bounds.Right - Width - num2) : (screen.Bounds.X + num2));
		int val2 = (flag2 ? (screen.Bounds.Bottom - Height - num3) : (screen.Bounds.Y + num3));
		val = Math.Max(screen.Bounds.Left + 5, Math.Min(val, screen.Bounds.Right - Width - 5));
		val2 = Math.Max(screen.Bounds.Top + 5, Math.Min(val2, screen.Bounds.Bottom - Height - 5));
		Location = new Point(val, val2);
	}

	public void StartOverlay()
	{
		if (_active)
		{
			return;
		}
		_active = true;
		_lastPaintedFingerprint = null;
		HardwareControl.EnableLocalMonitoring();
		_lastFgPid = 0;
		_gameOnly = AppConfig.IsOverlayGameOnly();
		_hidden = false;
		_shownPid = 0;
		_fgDesktop = false;
		_mode = (AppConfig.Exists("overlay_mode") ? AppConfig.Get("overlay_mode", 0) : AppConfig.Get("overlay_light_mode", 0)) switch
		{
			3 => OverlayMode.Complete, 
			2 => OverlayMode.Full, 
			1 => OverlayMode.Light, 
			_ => OverlayMode.Default, 
		};
		_scalePercent = Math.Clamp(AppConfig.Get("overlay_scale_percent", 100), 35, 300);
		ApplyColors();
		ApplyPreset(_mode);
		_onBattery = _overlayBattery == 1 || SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;
		ApplySensorFlags();
		SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
		HardwareControl.ResetCPUPowerCounter();
		UpdateOverlaySize();
		RestorePosition();
		// ⭐⭐ 2026-10-18（机主反馈「悬浮窗没有置顶、会被其他窗口盖住」）：
		//   ⭐ 每次打开（含 ⭐ 从托盘重新打开 / ⭐ 息屏恢复后 `ResumeForDisplayOn` 重开 / ⭐ 句柄重建）都显式置顶 ✅
		//   ⚠️ 不抢焦点：`base.Show()` 走 `SW_SHOWNOACTIVATE`、置顶动作带 `SWP_NOACTIVATE`、
		//      窗口本身带 `WS_EX_NOACTIVATE` ⇒ ⭐ 焦点行为与改动前一致 ✅
		TopMost = true;
		base.Show();
		ApplyDragMode(on: true);
		if (_gameOnly)
		{
			_hidden = true;
			User32.ShowWindow(Handle, 0);
		}
		Tick();
		RestorePosition();
		_timer.Start();
		if (_gameOnly && _fgHook == IntPtr.Zero)
		{
			_fgHookProc = (nint _, uint _, nint _, int _, int _, uint _, uint _) =>
			{
				OnForegroundChanged();
			};
			_fgHook = SetWinEventHook(3u, 3u, IntPtr.Zero, _fgHookProc, 0u, 0u, 2u);
		}
	}

	public void StopOverlay()
	{
		SettingsForm settingsForm = Program.settingsForm;
		if (settingsForm != null && !settingsForm.IsDisposed && Program.settingsForm.InvokeRequired)
		{
			try
			{
				Program.settingsForm.BeginInvoke(StopOverlay);
				return;
			}
			catch
			{
				return;
			}
		}
		_active = false;
		if (_fgHook != IntPtr.Zero)
		{
			UnhookWinEvent(_fgHook);
			_fgHook = IntPtr.Zero;
		}
		_fgHookProc = null;
		HardwareControl.readUsage = false;
		HardwareControl.readFans = false;
		HardwareControl.readMemory = false;
		HardwareControl.readPower = false;
		HardwareControl.readBattery = false;
		SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
		_timer.Stop();
		_dragModeActive = false;
		_dragging = false;
		lock (PaintSync)
		{
			_font?.Dispose();
			_font = null;
			_rpmFont?.Dispose();
			_rpmFont = null;
			_totalPen?.Dispose();
			_totalPen = null;
			_axPen?.Dispose();
			_axPen = null;
		}
		_lastScale = 0f;
		base.Hide();
	}

	public void SuspendForDisplayOff()
	{
		if (_active)
		{
			StopOverlay();
		}
	}

	public void ResumeForDisplayOn()
	{
		if (!_active && AppConfig.IsOverlay())
		{
			StartOverlay();
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && Interlocked.Exchange(ref _resourcesDisposed, 1) == 0)
		{
			_timer.Stop();
			_timer.Dispose();
			lock (PaintSync)
			{
				_font?.Dispose();
				_font = null;
				_rpmFont?.Dispose();
				_rpmFont = null;
				_totalPen?.Dispose();
				_totalPen = null;
				_axPen?.Dispose();
				_axPen = null;
				_bgBrush.Dispose();
				_gpuBrush.Dispose();
				_cpuBrush.Dispose();
				_gpuLinePen.Dispose();
				_cpuLinePen.Dispose();
				_gpuFillBrush.Dispose();
				_cpuFillBrush.Dispose();
			}
		}
		base.Dispose(disposing);
	}
}
