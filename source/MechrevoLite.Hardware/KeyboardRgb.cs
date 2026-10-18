using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

public class KeyboardRgb : IDisposable
{
	private record KeyEntry(string Name, int Offset, int Row, int Col);

	private enum ConnectOutcome
	{
		Success,
		NoDevice,
		Failed
	}

	public const int BufSize = 512;

	private const int VendorId = 1165;

	private const int ProductId = 24587;

	private const int OffsetShiftLeft = 5;

	private const int OffsetShiftLeftAux = 29;

	private const int OffsetEnter = 349;

	private const int OffsetEnterAux = 345;

	private static readonly KeyEntry[] Keymap = new KeyEntry[102]
	{
		new KeyEntry("Esc", 21, 0, 0),
		new KeyEntry("F1", 45, 0, 1),
		new KeyEntry("F2", 69, 0, 2),
		new KeyEntry("F3", 93, 0, 3),
		new KeyEntry("F4", 117, 0, 4),
		new KeyEntry("F5", 141, 0, 5),
		new KeyEntry("F6", 165, 0, 6),
		new KeyEntry("F7", 189, 0, 7),
		new KeyEntry("F8", 213, 0, 8),
		new KeyEntry("F9", 237, 0, 9),
		new KeyEntry("F10", 261, 0, 10),
		new KeyEntry("F11", 285, 0, 11),
		new KeyEntry("F12", 309, 0, 12),
		new KeyEntry("FnLockCam", 333, 0, 13),
		new KeyEntry("PrtSc", 357, 0, 14),
		new KeyEntry("Del", 381, 0, 15),
		new KeyEntry("Home", 405, 0, 16),
		new KeyEntry("PgUp", 429, 0, 17),
		new KeyEntry("PgDn", 453, 0, 18),
		new KeyEntry("End", 477, 0, 19),
		new KeyEntry("Grave", 17, 1, 0),
		new KeyEntry("1", 41, 1, 1),
		new KeyEntry("2", 65, 1, 2),
		new KeyEntry("3", 89, 1, 3),
		new KeyEntry("4", 113, 1, 4),
		new KeyEntry("5", 137, 1, 5),
		new KeyEntry("6", 161, 1, 6),
		new KeyEntry("7", 185, 1, 7),
		new KeyEntry("8", 209, 1, 8),
		new KeyEntry("9", 233, 1, 9),
		new KeyEntry("0", 257, 1, 10),
		new KeyEntry("Minus", 281, 1, 11),
		new KeyEntry("Equals", 305, 1, 12),
		new KeyEntry("Backspace", 353, 1, 13),
		new KeyEntry("NumLock", 377, 1, 14),
		new KeyEntry("NumpadDiv", 401, 1, 15),
		new KeyEntry("NumpadMul", 425, 1, 16),
		new KeyEntry("NumpadMinus", 449, 1, 17),
		new KeyEntry("Tab", 13, 2, 0),
		new KeyEntry("Q", 61, 2, 1),
		new KeyEntry("W", 85, 2, 2),
		new KeyEntry("E", 109, 2, 3),
		new KeyEntry("R", 133, 2, 4),
		new KeyEntry("T", 157, 2, 5),
		new KeyEntry("Y", 181, 2, 6),
		new KeyEntry("U", 205, 2, 7),
		new KeyEntry("I", 229, 2, 8),
		new KeyEntry("O", 253, 2, 9),
		new KeyEntry("P", 277, 2, 10),
		new KeyEntry("BracketL", 301, 2, 11),
		new KeyEntry("BracketR", 325, 2, 12),
		new KeyEntry("Enter", 349, 2, 13),
		new KeyEntry("Numpad7", 373, 2, 14),
		new KeyEntry("Numpad8", 397, 2, 15),
		new KeyEntry("Numpad9", 421, 2, 16),
		new KeyEntry("NumpadPlus", 445, 2, 17),
		new KeyEntry("CapsLock", 9, 3, 0),
		new KeyEntry("A", 57, 3, 1),
		new KeyEntry("S", 81, 3, 2),
		new KeyEntry("D", 105, 3, 3),
		new KeyEntry("F", 129, 3, 4),
		new KeyEntry("G", 153, 3, 5),
		new KeyEntry("H", 177, 3, 6),
		new KeyEntry("J", 201, 3, 7),
		new KeyEntry("K", 225, 3, 8),
		new KeyEntry("L", 249, 3, 9),
		new KeyEntry("Semicolon", 273, 3, 10),
		new KeyEntry("Quote", 297, 3, 11),
		new KeyEntry("Backslash", 321, 3, 12),
		new KeyEntry("Numpad4", 369, 3, 13),
		new KeyEntry("Numpad5", 393, 3, 14),
		new KeyEntry("Numpad6", 417, 3, 15),
		new KeyEntry("ShiftL", 5, 4, 0),
		new KeyEntry("ISO_Backslash", 53, 4, 1),
		new KeyEntry("Z", 77, 4, 2),
		new KeyEntry("X", 101, 4, 3),
		new KeyEntry("C", 125, 4, 4),
		new KeyEntry("V", 149, 4, 5),
		new KeyEntry("B", 173, 4, 6),
		new KeyEntry("N", 197, 4, 7),
		new KeyEntry("M", 221, 4, 8),
		new KeyEntry("Comma", 245, 4, 9),
		new KeyEntry("Period", 269, 4, 10),
		new KeyEntry("Slash", 293, 4, 11),
		new KeyEntry("ShiftR", 341, 4, 12),
		new KeyEntry("Numpad1", 365, 4, 13),
		new KeyEntry("Numpad2", 389, 4, 14),
		new KeyEntry("Numpad3", 413, 4, 15),
		new KeyEntry("NumpadEnter", 437, 4, 16),
		new KeyEntry("CtrlL", 1, 5, 0),
		new KeyEntry("Fn", 49, 5, 1),
		new KeyEntry("Windows", 73, 5, 2),
		new KeyEntry("AltL", 97, 5, 3),
		new KeyEntry("Space", 169, 5, 4),
		new KeyEntry("AltGr", 241, 5, 5),
		new KeyEntry("CopilotKey", 289, 5, 6),
		new KeyEntry("ArrowUp", 337, 5, 7),
		new KeyEntry("Numpad0", 385, 5, 8),
		new KeyEntry("NumpadDecimal", 409, 5, 9),
		new KeyEntry("ArrowLeft", 313, 6, 6),
		new KeyEntry("ArrowDown", 433, 6, 7),
		new KeyEntry("ArrowRight", 361, 6, 8)
	};

	public const int ModeBreath = 0;

	public const int ModeWave = 1;

	public const int ModeSparkle = 2;

	public const int ModeReactive = 3;

	public const int ModeWheel = 4;

	public const int ModeLightning = 5;

	public const int ModeFlame = 6;

	public const int ModeRain = 7;

	public const int ModeMatrix = 8;

	public const int ModeStatic = 9;

	private HidDeviceWin? _stream;

	private readonly object _lock = new object();

	private Thread? _effectThread;

	private volatile bool _stop;

	private volatile int _activeMode = -1;

	private int _generation;

	private int _customModeReinitCount;

	private readonly object _startLock = new object();

	private readonly object _configLock = new object();

	private Timer? _configSaveTimer;

	private readonly string? _configPathOverride;

	internal Func<bool>? ReconnectProbe;

	internal Func<HidDeviceWin?>? ResolveDeviceProbe;

	private readonly object _reconnectLock = new object();

	private volatile bool _reconnecting;

	private volatile bool _disposed;

	private long _reconnectStartedTicks;

	private const int ReconnectTimeoutMs = 3000;

	public volatile int Brightness = 100;

	/// <summary>
	/// 2026-10-12（机主原话「亮度是0，所以打开关闭没有变化，把亮度拉高就好了」）：**上一次非 0 的亮度**
	/// （`brightness=` 这个既有配置键的历史值；见 <see cref="MechrevoLite.Hardware.LightingState.NormalizeKeyboardBrightness" />）。
	/// <para>用途：亮度被（GCU 回读 / 固件接管）压成 0 时抬回**用户自己选过的值**。⛔ 只认 <c>1..100</c>，
	/// <c>0</c>（坏读数）与 <c>-1</c>（未知）都⛔ 不 latch。</para>
	/// </summary>
	private volatile int _kbLastNonZeroBrightness = MechrevoLite.Hardware.LightingState.NoKeyboardBrightnessHistory;

	/// <summary>上一次非 0 的亮度（<c>1..100</c>；无历史 = <c>-1</c>）。</summary>
	public int KbLastNonZeroBrightness => _kbLastNonZeroBrightness;

	/// <summary>
	/// 2026-10-12：把一条亮度读数 latch 成"上一次非 0 的亮度"。⛔ 非 <c>1..100</c> 一律忽略（幂等）。
	/// </summary>
	public void LatchLastNonZeroBrightness(int brightness)
	{
		if (LightingState.ShouldLatchLastNonZeroBrightness(brightness))
		{
			_kbLastNonZeroBrightness = brightness;
		}
	}

	/// <summary>
	/// 2026-10-12（亮度 0 归一化）：把 <see cref="Brightness" /> 抬到"看得见"的值。
	/// <para>语义全部收在 <see cref="LightingState.NormalizeKeyboardBrightness" />（真值表可离线断言）：
	/// 有效电源不 On ⇒ 不动；单分区机型（<paramref name="brightnessZeroIsOff" />）⇒ 不动；
	/// 亮度 ≥ 1 ⇒ 不动；亮度 = 0 ⇒ 抬回上一次非 0 的亮度（没历史则 50%）。</para>
	/// ⛔ 不改 <c>KbPowerOn</c> / ⛔ 不碰 <c>KbPowerExplicitOff</c> / ⛔ 不发任何硬件命令（调用方决定怎么发）。
	/// </summary>
	/// <returns>原本的亮度（调用方据此判断"到底改了没有"）。</returns>
	public int NormalizeBrightness(int lastNonZeroBrightness, bool effectivePowerOn, bool brightnessZeroIsOff)
	{
		int num = Brightness;
		int num2 = LightingState.NormalizeKeyboardBrightness(effectivePowerOn, num, lastNonZeroBrightness, brightnessZeroIsOff);
		if (num2 != num)
		{
			Brightness = num2;
			Logger.WriteLine($"键盘灯亮度 0 归一化：{num}% -> {num2}%（电源有效为 On；历史非 0 亮度={lastNonZeroBrightness}）");
		}
		return num;
	}

	public volatile int TargetFps = 30;

	public int CloseTimerMinutes;

	public int KbHidMode = 1;

	private bool _kbPowerOn = true;

	/// <summary>
	/// 2026-10-12（键盘灯「有时候控制不了」根因①：门控背离）：用户是否**明确把键盘灯关掉过**
	/// （UI 开关拨到关 / 亮度拨到 0 / rgb.cfg 里明确写着 <c>kbPower=0</c>）。
	/// <para>
	/// 它是 <see cref="MechrevoLite.Hardware.LightingState.ResolveEffectiveKbPower"/> 的
	/// 「硬件不能赢」那一票 —— 这一条正是"用户明确关灯仍然生效"的保证。
	/// ⚠️ **但它只在硬件/GCU 也报 Off 时才算数**（2026-10-12 用户拍板「键盘灯光也修复」）：
	/// 硬件报 On 而它还留着，说明这个标记**陈旧**（机主后来用 Fn 把灯点亮了 / 硬件自己开了）
	/// ⇒ 由 <see cref="ClearKbPowerExplicitOffFromHardware" /> 清掉，⛔ 不许再压住用户的灯。
	/// </para>
	/// <para>⛔ 与"配置缺省"无关：默认值 <c>true</c> 只是"rgb.cfg 没有 kbPower 键"的兜底，
	/// ⛔ 也⛔绝不因硬件回灌（<see cref="KbPowerSyncFromHardware"/>）而置位。</para>
	/// </summary>
	public bool KbPowerExplicitOff { get; private set; }

	private bool _loadingConfig;

	/// <summary>
	/// 2026-10-10（①「安装后灯被熄灭」）：这是 HID 链路上唯一的"用户要不要键盘灯"信号。
	/// <para>
	/// ⚠️ 缺省值 true 只是"rgb.cfg 里没有 kbPower 键"的兜底，**不再**等于"用户开了灯" ——
	/// 「配置缺失时该不该下发」统一由 <see cref="LightingSettingsStore.HasUserLightingIntent"/> 判定：
	/// 配置缺失（= 从未配置 / 被安装程序删掉）⇒ 一律不下发，硬件上的状态可能是官方控制台或 EC/固件设的，
	/// LM 不去覆盖才是对的。
	/// </para>
	/// <para>
	/// setter 里顺手写"已配置"标记：能走到赋值的只有用户的开关/效果操作
	/// （SettingsForm / RgbForm 的点击）。⚠️ 从 rgb.cfg **加载**时不写 —— 那个文件在进程退出时会被
	/// 无条件 SaveConfig() 写出来（Dispose），存在本身不能代表用户意图。
	/// </para>
	/// </summary>
	public bool KbPowerOn
	{
		get
		{
			return _kbPowerOn;
		}
		set
		{
			_kbPowerOn = value;
			if (!_loadingConfig)
			{
				LightingSettingsStore.MarkUserLightingIntent("KbPowerOn=" + (value ? "1" : "0"));
				// 2026-10-12（根因①）：能走到这里的就是用户在 UI 上拨了「键盘」开关
				// （SettingsForm 的 CheckedChanged）⇒ 值本身就是用户意图，**两个方向都记**。
				KbPowerExplicitOff = !value;
				// ⭐⭐ 2026-10-19（机主：关灯后又自己开）：⭐ 用户意图必须**立刻落盘**
				//   ⇒ rgb.cfg 的 `kbPower=0/1` 马上写出去（不再只等窗口关闭/进程退出）✅
				QueueSaveConfig();
			}
		}
	}

	/// <summary>
	/// 2026-10-12（根因① 的后半句）：硬件/GCU 报键盘电源 = On 时，把它**同步回 LM 状态**，
	/// 让 UI 反映真实、也让下次恢复按"开"走。
	/// <para>
	/// ⚠️ 与 <see cref="KbPowerOn"/> 的 setter 严格区分：这里**不**写"用户意图"标记、
	/// **不**碰 <see cref="KbPowerExplicitOff"/> —— 它不是用户动作，是硬件回灌；否则会把
	/// <c>HasUserLightingIntent</c> 的语义污染成"硬件开过灯"。</para>
	/// </summary>
	internal void KbPowerSyncFromHardware(bool on)
	{
		if (_kbPowerOn == on && !(on && KbPowerExplicitOff))
		{
			return;
		}
		_kbPowerOn = on;
		// 2026-10-12（用户拍板「键盘灯光也修复」）：硬件报 On 时**顺手清掉**那条"用户明确关过"标记
		// —— 硬件说它现在是 On，那条标记就是陈旧的（见 KbPowerExplicitOff 的说明）。
		// ⚠️ 幂等：标记本来就没置位时这一行是空操作，所以 `_kbPowerOn` 已为 true 而只有标记陈旧时
		// 也一定会走到这里把标记清掉（这正是"陈旧标记"那一幕）。
		if (on)
		{
			KbPowerExplicitOff = false;
		}
		QueueSaveConfig();
	}

	/// <summary>
	/// 2026-10-12（用户拍板「键盘灯光也修复」）：**只**清掉"用户明确关过"那条陈旧标记。
	/// 用于"LM 缓存已经是 On、只有标记还在"这一种背离 —— 此时
	/// <see cref="KbPowerSyncFromHardware"/> 那条路会因为 <c>_kbPowerOn == on</c> 而无事可做。
	/// <para>⛔ 不写"用户意图"标记（<c>HasUserLightingIntent</c> 只认用户动作）；⛔ 不改 <c>_kbPowerOn</c>；
	/// 只有真的从 true 变 false 时才 <see cref="QueueSaveConfig" />（把 rgb.cfg 里那个 <c>kbPower=0</c>
	/// 落成 1，避免下次启动又被 <c>LoadConfig</c> 重新置位）。</para>
	/// </summary>
	/// <returns>本次是否真的清掉了标记。</returns>
	internal bool ClearKbPowerExplicitOffFromHardware()
	{
		if (!KbPowerExplicitOff)
		{
			return false;
		}
		KbPowerExplicitOff = false;
		QueueSaveConfig();
		return true;
	}

	public Color BreathColor = Color.FromArgb(0, 120, 255);

	public volatile int BreathSpeed = 12;

	public volatile int BreathSmooth = 60;

	public volatile int WaveAngle = 30;

	public volatile int WaveSpeed = 4;

	public volatile int SparkleSpeed = 5;

	public volatile int SparkleDensity = 8;

	public Color ReactiveColor = Color.White;

	public volatile bool ReactiveRandom = true;

	public volatile int ReactiveDuration = 20;

	public volatile bool WheelReverse;

	public volatile int WheelSpeed = 4;

	public volatile int LightningSpeed = 5;

	public volatile int LightningSmooth = 10;

	public volatile int LightningWidth = 2;

	public volatile int LightningConcurrent = 1;

	public volatile int FlameSpeed = 5;

	public volatile int FlameSmooth = 8;

	public volatile int RainSpeed = 5;

	public volatile int RainDensity = 3;

	public volatile int RainColorCount = 1;

	public Color[] RainColors = new Color[5]
	{
		Color.FromArgb(80, 140, 255),
		Color.FromArgb(120, 80, 255),
		Color.FromArgb(80, 220, 255),
		Color.White,
		Color.FromArgb(80, 255, 180)
	};

	public volatile int MatrixSpeed = 5;

	public volatile int MatrixStyle;

	public volatile int MatrixDensity = 2;

	public volatile int MatrixColorCount = 1;

	public Color[] MatrixColors = new Color[3]
	{
		Color.FromArgb(0, 255, 60),
		Color.FromArgb(0, 180, 255),
		Color.FromArgb(255, 0, 120)
	};

	public volatile int StaticLayout;

	public volatile int StaticZones = 1;

	public Color[] StaticColors = new Color[10]
	{
		Color.White,
		Color.Red,
		Color.Lime,
		Color.Blue,
		Color.Yellow,
		Color.Magenta,
		Color.Cyan,
		Color.FromArgb(255, 140, 0),
		Color.FromArgb(140, 0, 255),
		Color.FromArgb(0, 140, 140)
	};

	private DateTime _lastSendFailLog = DateTime.MinValue;

	private long _successFrames;

	private DateTime _lastHeartbeat = DateTime.MinValue;

	private long _heartbeatBase;

	private int _failCount;

	private static readonly byte[] Step3 = new byte[9] { 0, 18, 0, 0, 8, 0, 0, 0, 0 };

	private readonly byte[] _scaledFrame = new byte[512];

	private readonly byte[][] _frameChunks = (from _ in Enumerable.Range(0, 8)
		select new byte[65]).ToArray();

	private readonly FramePacer _pacer = new FramePacer(new SystemFrameClock());

	private static readonly byte[] Step1 = new byte[9] { 0, 18, 0, 3, 0, 0, 0, 0, 0 };

	private static readonly byte[] Step2 = new byte[9] { 0, 8, 2, 51, 0, 50, 0, 0, 0 };

	private const int ProbeTimeoutMs = 3000;

	private const int ProbeRetryDelayMs = 100;

	private readonly object _probeLock = new object();

	private int _controllerProbeCount;

	private static readonly object _timerPeriodLock = new object();

	private static int _timerPeriodUsers;

	private static bool _timerPeriodActive;

	private double BreathState;

	private double WaveState;

	private double WheelState;

	private const int MaxStrikes = 4;

	private const int MaxRainDrops = 20;

	private const int MatrixStreaks = 5;

	private const int MatrixColumns = 24;

	private const int MaxLasers = 3;

	private const int MaxRipples = 3;

	private int[]? _xRank;

	private int _lastSavedMode = -1;

	public Color StaticColor
	{
		get
		{
			return StaticColors[0];
		}
		set
		{
			StaticColors[0] = value;
		}
	}

	public bool IsConnected => _stream != null;

	public string DeviceInfo { get; private set; } = "未连接";

	public string LastError { get; private set; } = "";

	public int ActiveMode => _activeMode;

	internal int EffectGeneration => Volatile.Read(in _generation);

	internal int CustomModeReinitCount => Volatile.Read(in _customModeReinitCount);

	internal FeatureAvailability ControllerAvailability { get; private set; }

	internal int ControllerProbeCount => Volatile.Read(in _controllerProbeCount);

	public string ConfigPath => _configPathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MechrevoLite", "rgb.cfg");

	public event Action? DeviceLost;

	public KeyboardRgb()
	{
	}

	internal KeyboardRgb(string configPathOverride)
	{
		_configPathOverride = configPathOverride;
	}

	internal KeyboardRgb(string configPathOverride, HidDeviceWin stream)
		: this(configPathOverride)
	{
		_stream = stream;
	}

	private void LogSendFail(string why)
	{
		if (!((DateTime.Now - _lastSendFailLog).TotalSeconds < 5.0))
		{
			_lastSendFailLog = DateTime.Now;
			Logger.WriteLine("RGB send_frame FAIL (" + why + ") dev=" + DeviceInfo);
		}
	}

	private void Heartbeat()
	{
		if (!((DateTime.Now - _lastHeartbeat).TotalSeconds < 60.0))
		{
			_lastHeartbeat = DateTime.Now;
			Logger.WriteLine($"RGB heartbeat: {_successFrames - _heartbeatBase} 帧/60s");
			_heartbeatBase = _successFrames;
		}
	}

	private void PaceFrame(int fps)
	{
		_pacer.Pace(fps);
	}

	private HidDeviceWin? ResolveDevice()
	{
		if (ResolveDeviceProbe != null)
		{
			return ResolveDeviceProbe();
		}
		HidDeviceWin result = null;
		int num = 0;
		foreach (HidDeviceWin item in HidDeviceWin.Enumerate(1165))
		{
			int num2 = ScoreCandidate(item.ProductID, item.UsagePage, item.InterfaceNumber, item.OutputReportLength, item.FeatureReportLength);
			Logger.WriteLine($"RGB HID candidate: {item.DeviceInfo} out={item.OutputReportLength} feature={item.FeatureReportLength} score={num2}");
			if (num2 > num)
			{
				num = num2;
				result = item;
			}
		}
		return result;
	}

	internal static int ScoreCandidate(ushort productId, ushort usagePage, int interfaceNumber, ushort outputReportLength, ushort featureReportLength)
	{
		int num = ((usagePage == 65283) ? 30 : ((interfaceNumber == 1) ? 20 : 0));
		if (num == 0)
		{
			return 0;
		}
		if (productId == 24587)
		{
			num += 4;
		}
		if (outputReportLength == 65)
		{
			num += 2;
		}
		if (featureReportLength == 9)
		{
			num += 2;
		}
		return num;
	}

	internal static int MapHardwareBrightnessLevel(int level)
	{
		return Math.Clamp(level, 0, 4) * 25;
	}

	internal static int MapSoftwareBrightnessToHardwareLevel(int brightness)
	{
		return Math.Clamp((brightness + 12) / 25, 0, 4);
	}

	internal static int MapReportedHardwareBrightness(int brightness, int legacyLevel)
	{
		if (brightness < 0)
		{
			if (legacyLevel >= 0)
			{
				if (legacyLevel > 4)
				{
					return Math.Clamp(legacyLevel, 0, 100);
				}
				return MapHardwareBrightnessLevel(legacyLevel);
			}
			return -1;
		}
		if (brightness > 4)
		{
			return Math.Clamp(brightness, 0, 100);
		}
		return MapHardwareBrightnessLevel(brightness);
	}

	public bool Connect()
	{
		lock (_lock)
		{
			try
			{
				return ConnectCoreLocked() == ConnectOutcome.Success;
			}
			catch (Exception ex)
			{
				_stream?.Dispose();
				_stream = null;
				LastError = "连接异常: " + ex.Message;
				Logger.WriteLine("RGB connect FAIL: " + LastError);
				return false;
			}
		}
	}

	private ConnectOutcome ConnectCoreLocked()
	{
		_stream?.Dispose();
		_stream = null;
		HidDeviceWin hidDeviceWin = ResolveDevice();
		if (hidDeviceWin == null)
		{
			LastError = "未找到兼容的 ITE8291/BetterRGB 接口（VID 048D，FF03/MI_01）";
			return ConnectOutcome.NoDevice;
		}
		if (!hidDeviceWin.Open())
		{
			LastError = "打开 HID 失败（设备被占用或权限不足）";
			return ConnectOutcome.Failed;
		}
		_stream = hidDeviceWin;
		DeviceInfo = hidDeviceWin.DeviceInfo;
		Thread.Sleep(20);
		if (!EnterCustomMode())
		{
			_stream.Dispose();
			_stream = null;
			LastError = "初始化 RGB 模式失败（feature report 被拒）";
			Logger.WriteLine("RGB connect FAIL: " + LastError);
			return ConnectOutcome.Failed;
		}
		LastError = "";
		Logger.WriteLine("RGB connect OK: " + DeviceInfo);
		return ConnectOutcome.Success;
	}

	private bool EnterCustomMode()
	{
		if (!_stream.SetFeature(Step1))
		{
			return false;
		}
		if (!_stream.Write(new byte[65]))
		{
			return false;
		}
		if (!_stream.SetFeature(Step2))
		{
			return false;
		}
		return true;
	}

	public async Task<bool> EnsureHidReadyAsync()
	{
		if (ControllerAvailability != FeatureAvailability.Unknown)
		{
			return ControllerAvailability == FeatureAvailability.Supported;
		}
		FeatureAvailability featureAvailability;
		try
		{
			featureAvailability = await Task.Run((Func<FeatureAvailability>)ProbeController).WaitAsync(TimeSpan.FromMilliseconds(3000L)).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RGB probe 超时或异常: " + ex.Message);
			featureAvailability = FeatureAvailability.Unknown;
		}
		if (featureAvailability != FeatureAvailability.Supported)
		{
			lock (_lock)
			{
				_stream?.Dispose();
				_stream = null;
			}
		}
		if (featureAvailability != FeatureAvailability.Unknown)
		{
			ControllerAvailability = featureAvailability;
		}
		return featureAvailability == FeatureAvailability.Supported;
	}

	private FeatureAvailability ProbeController()
	{
		lock (_probeLock)
		{
			if (ControllerAvailability != FeatureAvailability.Unknown)
			{
				return ControllerAvailability;
			}
			Interlocked.Increment(ref _controllerProbeCount);
			try
			{
				ConnectOutcome connectOutcome;
				lock (_lock)
				{
					connectOutcome = ConnectCoreLocked();
				}
				switch (connectOutcome)
				{
				case ConnectOutcome.Success:
					return FeatureAvailability.Supported;
				case ConnectOutcome.NoDevice:
					return FeatureAvailability.Unsupported;
				default:
					Thread.Sleep(100);
					lock (_lock)
					{
						connectOutcome = ConnectCoreLocked();
					}
					return (connectOutcome != ConnectOutcome.Success) ? FeatureAvailability.Unsupported : FeatureAvailability.Supported;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("RGB probe FAIL: " + ex.Message);
				return FeatureAvailability.Unknown;
			}
		}
	}

	private bool Reconnect()
	{
		if (ReconnectProbe != null)
		{
			return ReconnectProbe();
		}
		lock (_lock)
		{
			_stream?.Dispose();
			_stream = null;
		}
		Thread.Sleep(100);
		HidDeviceWin hidDeviceWin = ResolveDevice();
		if (hidDeviceWin == null || !hidDeviceWin.Open())
		{
			return false;
		}
		Thread.Sleep(20);
		lock (_lock)
		{
			if (_disposed)
			{
				hidDeviceWin.Dispose();
				return false;
			}
			_stream?.Dispose();
			_stream = hidDeviceWin;
			return EnterCustomMode();
		}
	}

	private void RequestReconnect()
	{
		lock (_reconnectLock)
		{
			if (_disposed)
			{
				return;
			}
			if (_reconnecting)
			{
				if (Stopwatch.GetElapsedTime(_reconnectStartedTicks).TotalMilliseconds < 3000.0)
				{
					return;
				}
				Logger.WriteLine($"RGB reconnect 超时（>{3000}ms），重置单飞标志后重试");
			}
			_reconnecting = true;
			_reconnectStartedTicks = Stopwatch.GetTimestamp();
		}
		Task.Run(() =>
		{
			bool flag = false;
			try
			{
				flag = Reconnect();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("RGB reconnect fail: " + ex.Message);
			}
			finally
			{
				_reconnecting = false;
			}
			if (!flag)
			{
				RegisterFail();
			}
		});
	}

	public bool SendFrame(byte[] buf)
	{
		bool flag;
		lock (_lock)
		{
			if (_disposed || _reconnecting || _stream == null)
			{
				return false;
			}
			flag = WriteFrameLocked(buf);
			if (flag)
			{
				_failCount = 0;
				_successFrames++;
				Heartbeat();
			}
		}
		if (flag)
		{
			return true;
		}
		RegisterFail();
		RequestReconnect();
		return false;
	}

	private bool WriteFrameLocked(byte[] buf)
	{
		if (_stream == null)
		{
			return false;
		}
		byte[] array = buf;
		int brightness = Brightness;
		if (brightness < 100)
		{
			array = _scaledFrame;
			for (int i = 0; i < 512; i++)
			{
				array[i] = (byte)(buf[i] * brightness / 100);
			}
		}
		if (!_stream.SetFeature(Step3))
		{
			LogSendFail("step3");
			return false;
		}
		for (int j = 0; j < 8; j++)
		{
			byte[] array2 = _frameChunks[j];
			Array.Copy(array, j * 64, array2, 1, 64);
			if (!_stream.Write(array2))
			{
				LogSendFail($"chunk{j}");
				return false;
			}
		}
		return true;
	}

	private void RegisterFail()
	{
		if (Interlocked.Increment(ref _failCount) >= 10)
		{
			_failCount = 0;
			_stop = true;
			LastError = "键盘 RGB 设备异常（连续发送失败），灯效已停止";
			Logger.WriteLine("RGB device lost: " + LastError);
			Task.Run(() =>
			{
				DeviceLost?.Invoke();
			});
		}
	}

	private static void SetKey(byte[] buf, int offset, byte r, byte g, byte b)
	{
		if (offset >= 0 && offset + 2 < 512)
		{
			buf[offset] = r;
			buf[offset + 1] = g;
			buf[offset + 2] = b;
			int num = offset switch
			{
				349 => 345, 
				5 => 29, 
				_ => -1, 
			};
			if (num >= 0 && num + 2 < 512)
			{
				buf[num] = r;
				buf[num + 1] = g;
				buf[num + 2] = b;
			}
		}
	}

	private static void HsvToRgb(double h, out byte r, out byte g, out byte b)
	{
		h %= 360.0;
		if (h < 0.0)
		{
			h += 360.0;
		}
		double num = 1.0 - Math.Abs(h / 60.0 % 2.0 - 1.0);
		(double, double, double) tuple;
		if (h < 60.0)
		{
			tuple = (1.0, num, 0.0);
		}
		else if (h < 120.0)
		{
			tuple = (num, 1.0, 0.0);
		}
		else if (h < 180.0)
		{
			tuple = (0.0, 1.0, num);
		}
		else if (h < 240.0)
		{
			tuple = (0.0, num, 1.0);
		}
		else
		{
			tuple = ((!(h < 300.0)) ? (1.0, 0.0, num) : (num, 0.0, 1.0));
		}
		(double, double, double) tuple2 = tuple;
		double item = tuple2.Item1;
		double item2 = tuple2.Item2;
		double item3 = tuple2.Item3;
		r = (byte)(item * 255.0);
		g = (byte)(item2 * 255.0);
		b = (byte)(item3 * 255.0);
	}

	private static double GetKeyX(KeyEntry k)
	{
		if (k.Name == "AltGr")
		{
			return 10.5;
		}
		if (k.Name == "CopilotKey")
		{
			return 11.5;
		}
		if (k.Name == "ArrowUp")
		{
			return 13.0;
		}
		if (k.Name == "ArrowLeft")
		{
			return 12.0;
		}
		if (k.Name == "ArrowDown")
		{
			return 13.0;
		}
		if (k.Name == "ArrowRight")
		{
			return 14.0;
		}
		if (k.Name == "Numpad0")
		{
			return 15.0;
		}
		if (k.Name == "NumpadDecimal")
		{
			return 16.0;
		}
		if (k.Name.StartsWith("Numpad") || k.Name == "NumLock")
		{
			return (double)k.Col + 1.5;
		}
		bool flag;
		switch (k.Name)
		{
		case "Del":
		case "End":
		case "PgDn":
		case "PgUp":
		case "Home":
		case "FnLockCam":
		case "PrtSc":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return (double)k.Col + 1.5;
		}
		return k.Col;
	}

	public void StartMode(int mode)
	{
		lock (_startLock)
		{
			if (ShouldRestartEffect(mode, _activeMode, _stop, _effectThread?.IsAlive ?? false))
			{
				StopCurrentEffect();
				int gen = Interlocked.Increment(ref _generation);
				_activeMode = mode;
				_stop = false;
				_effectThread = new Thread(() =>
				{
					RunEffect(mode, gen);
				})
				{
					IsBackground = true,
					Name = "L-Mechrevo RGB",
					Priority = ThreadPriority.Normal
				};
				_effectThread.Start();
			}
		}
	}

	internal static bool ShouldRestartEffect(int requestedMode, int activeMode, bool stopping, bool threadAlive)
	{
		if (!((requestedMode != activeMode) | stopping))
		{
			return !threadAlive;
		}
		return true;
	}

	public void StartModeForced(int mode)
	{
		StopCurrentEffect();
		StartMode(mode);
	}

	public bool ApplyBrightnessToDevice()
	{
		lock (_lock)
		{
			if (_stream == null)
			{
				return false;
			}
			return _stream.SetFeature(Step3);
		}
	}

	public bool ReInitCustomMode()
	{
		lock (_lock)
		{
			if (_stream == null || !EnterCustomMode())
			{
				return false;
			}
			Interlocked.Increment(ref _customModeReinitCount);
			return true;
		}
	}

	public void StopCurrentEffect()
	{
		_stop = true;
		_effectThread?.Join(500);
		_effectThread = null;
		_activeMode = -1;
		if (_stream != null)
		{
			SendFrame(new byte[512]);
		}
	}

	private void RunEffect(int mode, int generation)
	{
		AcquirePreciseTimer();
		try
		{
			switch (mode)
			{
			case 0:
				EffectBreath(generation);
				break;
			case 1:
				EffectWave(generation);
				break;
			case 2:
				EffectSparkle(generation);
				break;
			case 3:
				EffectReactive(generation);
				break;
			case 4:
				EffectWheel(generation);
				break;
			case 5:
				EffectLightning(generation);
				break;
			case 6:
				EffectFlame(generation);
				break;
			case 7:
				EffectRain(generation);
				break;
			case 8:
				EffectMatrix(generation);
				break;
			case 9:
				EffectStatic(generation);
				break;
			}
		}
		finally
		{
			ReleasePreciseTimer();
		}
	}

	[DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
	private static extern uint TimeBeginPeriod(uint period);

	[DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
	private static extern uint TimeEndPeriod(uint period);

	private static void AcquirePreciseTimer()
	{
		lock (_timerPeriodLock)
		{
			if (_timerPeriodUsers++ == 0)
			{
				_timerPeriodActive = TimeBeginPeriod(1u) == 0;
			}
		}
	}

	private static void ReleasePreciseTimer()
	{
		lock (_timerPeriodLock)
		{
			if (_timerPeriodUsers > 0 && --_timerPeriodUsers == 0 && _timerPeriodActive)
			{
				TimeEndPeriod(1u);
				_timerPeriodActive = false;
			}
		}
	}

	private void FrameLoop(int generation, Action<byte[]> fill, Func<double> tick)
	{
		double num = 0.0;
		byte[] array = new byte[512];
		while (!_stop && generation == _generation)
		{
			Array.Clear(array);
			fill(array);
			SendFrame(array);
			num += tick();
			PaceFrame(TargetFps);
		}
	}

	private void EffectBreath(int gen)
	{
		FrameLoop(gen, (byte[] buf) =>
		{
			double num = (Math.Sin(BreathState) + 1.0) / 2.0;
			int num2 = Math.Max(2, BreathSmooth);
			double num3 = Math.Floor(num * (double)num2) / (double)num2;
			byte r = (byte)((double)(int)BreathColor.R * num3);
			byte g = (byte)((double)(int)BreathColor.G * num3);
			byte b = (byte)((double)(int)BreathColor.B * num3);
			KeyEntry[] keymap = Keymap;
			foreach (KeyEntry keyEntry in keymap)
			{
				SetKey(buf, keyEntry.Offset, r, g, b);
			}
		}, () =>
		{
			BreathState += (double)BreathSpeed / 100.0;
			return 0.0;
		});
	}

	private void EffectWave(int gen)
	{
		FrameLoop(gen, (byte[] buf) =>
		{
			double num = (double)WaveAngle * Math.PI / 180.0;
			double num2 = Math.Cos(num);
			double num3 = Math.Sin(num);
			KeyEntry[] keymap = Keymap;
			foreach (KeyEntry keyEntry in keymap)
			{
				double num4 = GetKeyX(keyEntry) * num2 + (double)keyEntry.Row * num3;
				HsvToRgb(WaveState + num4 * 15.0, out var r, out var g, out var b);
				SetKey(buf, keyEntry.Offset, r, g, b);
			}
		}, () =>
		{
			WaveState += WaveSpeed;
			return 0.0;
		});
	}

	private void EffectWheel(int gen)
	{
		var (cx, cy) = GetWheelCenter();
		FrameLoop(gen, (byte[] buf) =>
		{
			double num = (WheelReverse ? (-1.0) : 1.0);
			KeyEntry[] keymap = Keymap;
			foreach (KeyEntry keyEntry in keymap)
			{
				double num2 = Math.Atan2((double)keyEntry.Row - cy, GetKeyX(keyEntry) - cx) * 180.0 / Math.PI;
				HsvToRgb(WheelState * num + num2, out var r, out var g, out var b);
				SetKey(buf, keyEntry.Offset, r, g, b);
			}
		}, () =>
		{
			WheelState += (double)WheelSpeed;
			return 0.0;
		});
	}

	internal static (double X, double Y) GetWheelCenter()
	{
		KeyEntry keyEntry = Keymap.First((KeyEntry key) => key.Name == "K");
		return (X: GetKeyX(keyEntry), Y: keyEntry.Row);
	}

	private void EffectSparkle(int gen)
	{
		int[] life = new int[Keymap.Length];
		int[] maxLife = new int[Keymap.Length];
		byte[] rr = new byte[Keymap.Length];
		byte[] gg = new byte[Keymap.Length];
		byte[] bb = new byte[Keymap.Length];
		FrameLoop(gen, (byte[] buf) =>
		{
			int num = Math.Clamp(SparkleDensity, 1, 20);
			int num2 = Math.Max(8, 50 - SparkleSpeed * 4);
			int num3 = life.Count((int l) => l > 0);
			for (int num4 = 0; num4 < num - num3; num4++)
			{
				int num5 = -1;
				for (int num6 = 0; num6 < 20; num6++)
				{
					int num7 = Random.Shared.Next(Keymap.Length);
					if (life[num7] == 0)
					{
						num5 = num7;
						break;
					}
				}
				if (num5 < 0)
				{
					break;
				}
				life[num5] = num2;
				maxLife[num5] = num2;
				rr[num5] = (byte)Random.Shared.Next(256);
				gg[num5] = (byte)Random.Shared.Next(256);
				bb[num5] = (byte)Random.Shared.Next(256);
			}
			for (int num8 = 0; num8 < Keymap.Length; num8++)
			{
				if (life[num8] > 0)
				{
					double num9 = (double)life[num8] / (double)maxLife[num8];
					SetKey(buf, Keymap[num8].Offset, (byte)((double)(int)rr[num8] * num9), (byte)((double)(int)gg[num8] * num9), (byte)((double)(int)bb[num8] * num9));
					life[num8]--;
				}
			}
		}, () => 1.0);
	}

	private void EffectReactive(int gen)
	{
		bool[] wasDown = new bool[Keymap.Length];
		int[] fadeLife = new int[Keymap.Length];
		byte[] rr = new byte[Keymap.Length];
		byte[] gg = new byte[Keymap.Length];
		byte[] bb = new byte[Keymap.Length];
		List<int> vkList = new List<int>();
		for (int i = 65; i <= 90; i++)
		{
			vkList.Add(i);
		}
		for (int j = 48; j <= 57; j++)
		{
			vkList.Add(j);
		}
		int[] collection = new int[61]
		{
			32, 13, 8, 9, 160, 161, 20, 162, 164, 165,
			37, 39, 38, 40, 226, 91, 219, 221, 186, 222,
			220, 188, 190, 191, 189, 187, 192, 27, 112, 113,
			114, 115, 116, 117, 118, 119, 120, 121, 122, 123,
			44, 46, 36, 33, 34, 35, 96, 97, 98, 99,
			100, 101, 102, 103, 104, 105, 110, 107, 109, 106,
			111
		};
		vkList.AddRange(collection);
		FrameLoop(gen, (byte[] buf) =>
		{
			for (int k = 0; k < vkList.Count; k++)
			{
				int num = VkToKeymapIndex(vkList[k]);
				if (num >= 0)
				{
					if ((GetAsyncKeyState(vkList[k]) & 0x8000) != 0)
					{
						if (!wasDown[num])
						{
							if (ReactiveRandom)
							{
								rr[num] = (byte)Random.Shared.Next(256);
								gg[num] = (byte)Random.Shared.Next(256);
								bb[num] = (byte)Random.Shared.Next(256);
							}
							else
							{
								rr[num] = ReactiveColor.R;
								gg[num] = ReactiveColor.G;
								bb[num] = ReactiveColor.B;
							}
						}
						wasDown[num] = true;
						fadeLife[num] = ReactiveDuration;
					}
					else
					{
						wasDown[num] = false;
					}
				}
			}
			for (int l = 0; l < Keymap.Length; l++)
			{
				if (wasDown[l])
				{
					SetKey(buf, Keymap[l].Offset, rr[l], gg[l], bb[l]);
				}
				else if (fadeLife[l] > 0)
				{
					double num2 = (double)fadeLife[l] / (double)Math.Max(1, ReactiveDuration);
					SetKey(buf, Keymap[l].Offset, (byte)((double)(int)rr[l] * num2), (byte)((double)(int)gg[l] * num2), (byte)((double)(int)bb[l] * num2));
					fadeLife[l]--;
				}
			}
		}, () => 1.0);
	}

	private void EffectLightning(int gen)
	{
		double[] headPos = new double[4];
		double[] boltX = new double[4];
		int[] countdown = new int[4];
		for (int i = 0; i < 4; i++)
		{
			headPos[i] = 100.0;
			boltX[i] = 10.0;
			countdown[i] = 10 + Random.Shared.Next(30);
		}
		FrameLoop(gen, (byte[] buf) =>
		{
			double num = 0.05 + (double)LightningSpeed * 0.08;
			double num2 = (0.4 + (double)LightningSmooth * 0.15) * 2.5;
			double num3 = 6.0 + num2 + 0.5;
			double num4 = 0.35 + ((double)LightningWidth - 1.0) * 0.383;
			int num5 = Math.Clamp(LightningConcurrent, 1, 4);
			double[] array = new double[Keymap.Length];
			for (int j = 0; j < num5; j++)
			{
				if (headPos[j] > num3 && --countdown[j] <= 0)
				{
					headPos[j] = -2.0;
					boltX[j] = Random.Shared.Next(20);
					countdown[j] = 20 + Random.Shared.Next(40);
				}
				if (headPos[j] <= num3)
				{
					for (int k = 0; k < Keymap.Length; k++)
					{
						double num6 = headPos[j] - (double)Keymap[k].Row;
						if (!(num6 < -0.3) && !(num6 > num2))
						{
							double num7 = Math.Abs(GetKeyX(Keymap[k]) - boltX[j]);
							if (!(num7 > num4))
							{
								double num8 = ((num6 < 0.0) ? 1.0 : (1.0 - num6 / num2));
								double num9 = 1.0 - num7 / num4;
								if (num8 < 0.0)
								{
									num8 = 0.0;
								}
								if (num9 < 0.0)
								{
									num9 = 0.0;
								}
								double num10 = num8 * num9;
								if (num10 > array[k])
								{
									array[k] = num10;
								}
							}
						}
					}
					headPos[j] += num;
				}
			}
			for (int l = 0; l < Keymap.Length; l++)
			{
				if (array[l] > 0.0)
				{
					SetKey(buf, Keymap[l].Offset, (byte)(array[l] * 255.0), (byte)(array[l] * 255.0), byte.MaxValue);
				}
				else
				{
					SetKey(buf, Keymap[l].Offset, 4, 4, 14);
				}
			}
		}, () => 1.0);
	}

	private void EffectFlame(int gen)
	{
		double[] heat = new double[Keymap.Length];
		for (int i = 0; i < Keymap.Length; i++)
		{
			heat[i] = 100.0 + (double)Random.Shared.Next(60);
		}
		int frameCounter = 0;
		FrameLoop(gen, (byte[] buf) =>
		{
			double num = 1.0 / (1.0 + (double)FlameSmooth * 0.6);
			double num2 = 40.0 + (double)FlameSpeed * 18.0;
			int num3 = Math.Max(1, 11 - FlameSpeed);
			if (++frameCounter % num3 == 0)
			{
				for (int j = 0; j < Keymap.Length; j++)
				{
					double num4 = (double)Keymap[j].Row / 6.0;
					double num5 = 40.0 * num4;
					double num6 = 90.0 + num5 + (Random.Shared.NextDouble() * num2 - num2 / 2.0);
					heat[j] = Math.Clamp(heat[j] * (1.0 - num) + num6 * num, 0.0, 255.0);
				}
			}
			for (int k = 0; k < Keymap.Length; k++)
			{
				int num7 = (int)heat[k];
				byte r;
				byte g;
				byte b;
				if (num7 < 85)
				{
					r = (byte)(num7 * 3);
					g = 0;
					b = 0;
				}
				else if (num7 < 170)
				{
					r = byte.MaxValue;
					g = (byte)((num7 - 85) * 3);
					b = 0;
				}
				else
				{
					r = byte.MaxValue;
					g = byte.MaxValue;
					b = (byte)((num7 - 170) * 3);
				}
				SetKey(buf, Keymap[k].Offset, r, g, b);
			}
		}, () => 1.0);
	}

	private void EffectRain(int gen)
	{
		double[] dropX = new double[20];
		double[] dropRow = new double[20];
		int[] dropColor = new int[20];
		for (int i = 0; i < 20; i++)
		{
			dropX[i] = Random.Shared.Next(20);
			dropRow[i] = -Random.Shared.Next(8);
			dropColor[i] = Random.Shared.Next(Math.Max(1, RainColorCount));
		}
		FrameLoop(gen, (byte[] buf) =>
		{
			double num = 0.05 + (double)RainSpeed * 0.03;
			int num2 = Math.Clamp(RainColorCount, 1, 5);
			int num3 = Math.Min(20, Math.Clamp(RainDensity, 1, 5) * 3);
			for (int j = 0; j < num3; j++)
			{
				Color color = RainColors[dropColor[j] % num2];
				for (int k = 0; k <= 6; k++)
				{
					double num4 = dropRow[j] - (double)k;
					if (!(num4 < -0.3) && !(num4 > 1.8))
					{
						double num5 = ((num4 < 0.0) ? 1.0 : (1.0 - num4 / 1.8));
						if (num5 < 0.0)
						{
							num5 = 0.0;
						}
						int num6 = -1;
						double num7 = 1000000000.0;
						for (int l = 0; l < Keymap.Length; l++)
						{
							if (Keymap[l].Row == k)
							{
								double num8 = Math.Abs(GetKeyX(Keymap[l]) - dropX[j]);
								if (num8 < num7)
								{
									num7 = num8;
									num6 = l;
								}
							}
						}
						if (num6 >= 0 && num7 < 1.2)
						{
							SetKey(buf, Keymap[num6].Offset, (byte)((double)(int)color.R * num5), (byte)((double)(int)color.G * num5), (byte)((double)(int)color.B * num5));
						}
					}
				}
				dropRow[j] += num;
				if (dropRow[j] > 8.0)
				{
					dropRow[j] = -Random.Shared.Next(5);
					dropX[j] = Random.Shared.Next(20);
					dropColor[j] = Random.Shared.Next(num2);
				}
			}
		}, () => 1.0);
	}

	private void EffectMatrix(int gen)
	{
		double[] angle = new double[5];
		double[] pos = new double[5];
		double[] spd = new double[5];
		int[] streakColor = new int[5];
		for (int i = 0; i < 5; i++)
		{
			angle[i] = (double)Random.Shared.Next(360) * Math.PI / 180.0;
			pos[i] = -30.0;
			spd[i] = 0.2 + (double)Random.Shared.Next(100) / 100.0;
			streakColor[i] = Random.Shared.Next(Math.Max(1, MatrixColorCount));
		}
		double[] colPos = new double[24];
		double[] colSpeed = new double[24];
		int[] colColor = new int[24];
		for (int j = 0; j < 24; j++)
		{
			colPos[j] = -Random.Shared.Next(8);
			colSpeed[j] = 0.05 + (double)Random.Shared.Next(100) / 300.0;
			colColor[j] = Random.Shared.Next(Math.Max(1, MatrixColorCount));
		}
		double[] laserAngle = new double[3];
		double[] laserPos = new double[3];
		double[] laserSpd = new double[3];
		int[] laserColor = new int[3];
		for (int k = 0; k < 3; k++)
		{
			laserAngle[k] = (double)Random.Shared.Next(4) * (Math.PI / 2.0);
			laserPos[k] = -30.0 - (double)k * 8.0;
			laserSpd[k] = 0.6 + (double)Random.Shared.Next(100) / 100.0;
			laserColor[k] = Random.Shared.Next(Math.Max(1, MatrixColorCount));
		}
		double[] rippleX = new double[3];
		double[] rippleY = new double[3];
		double[] rippleR = new double[3];
		int[] rippleColor = new int[3];
		for (int l = 0; l < 3; l++)
		{
			rippleX[l] = Random.Shared.Next(20);
			rippleY[l] = Random.Shared.Next(7);
			rippleR[l] = (double)l * 8.0;
			rippleColor[l] = Random.Shared.Next(Math.Max(1, MatrixColorCount));
		}
		int activeStreaks = Math.Clamp(MatrixDensity, 1, 5);
		int rerollCounter = 0;
		FrameLoop(gen, (byte[] buf) =>
		{
			int num = Math.Clamp(MatrixColorCount, 1, 3);
			int num2 = Math.Clamp(MatrixStyle, 0, 3);
			double num3 = 0.3 + (double)MatrixSpeed * 0.15;
			switch (num2)
			{
			case 0:
			{
				if (++rerollCounter >= 60)
				{
					activeStreaks = ComputeProbCount(Math.Clamp(MatrixDensity, 1, 5), 1, 5);
					rerollCounter = 0;
				}
				for (int num23 = 0; num23 < Keymap.Length; num23++)
				{
					double keyX3 = GetKeyX(Keymap[num23]);
					double num24 = Keymap[num23].Row;
					double num25 = 0.0;
					int num26 = 0;
					for (int num27 = 0; num27 < activeStreaks; num27++)
					{
						double num28 = Math.Cos(angle[num27]);
						double num29 = Math.Sin(angle[num27]);
						double num30 = Math.Abs(keyX3 * num28 + num24 * num29 - pos[num27]);
						if (num30 < 1.5)
						{
							double num31 = 1.0 - num30 / 1.5;
							if (num31 > num25)
							{
								num25 = num31;
								num26 = streakColor[num27] % num;
							}
						}
					}
					if (num25 > 1.0)
					{
						num25 = 1.0;
					}
					Color color3 = MatrixColors[num26];
					SetKey(buf, Keymap[num23].Offset, (byte)((double)(int)color3.R * num25), (byte)((double)(int)color3.G * num25), (byte)((double)(int)color3.B * num25));
				}
				for (int num32 = 0; num32 < activeStreaks; num32++)
				{
					pos[num32] += spd[num32] * num3;
					if (pos[num32] > 30.0)
					{
						pos[num32] = -30.0;
						angle[num32] = (double)Random.Shared.Next(360) * Math.PI / 180.0;
						spd[num32] = 0.2 + (double)Random.Shared.Next(100) / 100.0;
						streakColor[num32] = Random.Shared.Next(num);
					}
				}
				break;
			}
			case 1:
			{
				for (int num33 = 0; num33 < Keymap.Length; num33++)
				{
					int num34 = Math.Clamp((int)(GetKeyX(Keymap[num33]) + 0.5), 0, 23);
					double num35 = colPos[num34] - (double)Keymap[num33].Row;
					double num36 = 0.0;
					if (num35 >= -0.3 && num35 < 4.0)
					{
						num36 = ((num35 < 0.0) ? 1.0 : (1.0 - num35 / 4.0));
					}
					if (num36 < 0.0)
					{
						num36 = 0.0;
					}
					Color color4 = MatrixColors[colColor[num34] % num];
					SetKey(buf, Keymap[num33].Offset, (byte)((double)(int)color4.R * num36), (byte)((double)(int)color4.G * num36), (byte)((double)(int)color4.B * num36));
				}
				for (int num37 = 0; num37 < 24; num37++)
				{
					colPos[num37] += colSpeed[num37] * num3;
					if (colPos[num37] > 10.0)
					{
						colPos[num37] = -Random.Shared.Next(6);
						colSpeed[num37] = 0.05 + (double)Random.Shared.Next(100) / 300.0;
						colColor[num37] = Random.Shared.Next(num);
					}
				}
				break;
			}
			case 2:
			{
				int num12 = ((Math.Clamp(MatrixDensity, 1, 5) >= 4) ? 3 : 2);
				for (int num13 = 0; num13 < Keymap.Length; num13++)
				{
					double keyX2 = GetKeyX(Keymap[num13]);
					double num14 = Keymap[num13].Row;
					double num15 = 0.0;
					int num16 = 0;
					for (int num17 = 0; num17 < num12; num17++)
					{
						double num18 = Math.Cos(laserAngle[num17]);
						double num19 = Math.Sin(laserAngle[num17]);
						double num20 = Math.Abs(keyX2 * num18 + num14 * num19 - laserPos[num17]);
						if (num20 < 0.6)
						{
							double num21 = 1.0 - num20 / 0.6;
							if (num21 > num15)
							{
								num15 = num21;
								num16 = laserColor[num17] % num;
							}
						}
					}
					if (num15 > 1.0)
					{
						num15 = 1.0;
					}
					Color color2 = MatrixColors[num16];
					SetKey(buf, Keymap[num13].Offset, (byte)((double)(int)color2.R * num15), (byte)((double)(int)color2.G * num15), (byte)((double)(int)color2.B * num15));
				}
				for (int num22 = 0; num22 < num12; num22++)
				{
					laserPos[num22] += laserSpd[num22] * num3 * 1.5;
					if (laserPos[num22] > 30.0)
					{
						laserPos[num22] = -30.0 - (double)Random.Shared.Next(20);
						laserAngle[num22] = (double)Random.Shared.Next(4) * (Math.PI / 2.0);
						laserSpd[num22] = 0.6 + (double)Random.Shared.Next(100) / 100.0;
						laserColor[num22] = Random.Shared.Next(num);
					}
				}
				break;
			}
			default:
			{
				for (int m = 0; m < Keymap.Length; m++)
				{
					double keyX = GetKeyX(Keymap[m]);
					double num4 = Keymap[m].Row;
					double num5 = 0.0;
					int num6 = 0;
					for (int n = 0; n < 3; n++)
					{
						double num7 = keyX - rippleX[n];
						double num8 = num4 - rippleY[n];
						double num9 = Math.Abs(Math.Sqrt(num7 * num7 + num8 * num8) - rippleR[n]);
						if (num9 < 1.2)
						{
							double num10 = 1.0 - num9 / 1.2;
							if (num10 > num5)
							{
								num5 = num10;
								num6 = rippleColor[n] % num;
							}
						}
					}
					if (num5 > 1.0)
					{
						num5 = 1.0;
					}
					Color color = MatrixColors[num6];
					SetKey(buf, Keymap[m].Offset, (byte)((double)(int)color.R * num5), (byte)((double)(int)color.G * num5), (byte)((double)(int)color.B * num5));
				}
				for (int num11 = 0; num11 < 3; num11++)
				{
					rippleR[num11] += num3 * 0.25;
					if (rippleR[num11] > 24.0)
					{
						rippleX[num11] = Random.Shared.Next(20);
						rippleY[num11] = Random.Shared.Next(7);
						rippleR[num11] = 0.0;
						rippleColor[num11] = Random.Shared.Next(num);
					}
				}
				break;
			}
			}
		}, () => 1.0);
	}

	private static int ComputeProbCount(int target, int minCount, int maxCount)
	{
		if (target >= maxCount)
		{
			return maxCount;
		}
		int num = Random.Shared.Next(100);
		if (num < 25)
		{
			return Math.Max(minCount, target - 1);
		}
		if (num < 75)
		{
			return target;
		}
		return Math.Min(maxCount, target + 1);
	}

	private void EffectStatic(int gen)
	{
		if (_xRank == null)
		{
			List<(double, int)> list = (from t in Keymap.Select((KeyEntry k, int i) => (x: GetKeyX(k), i: i))
				orderby t.x
				select t).ToList();
			_xRank = new int[Keymap.Length];
			for (int num = 0; num < list.Count; num++)
			{
				_xRank[list[num].Item2] = num;
			}
		}
		FrameLoop(gen, (byte[] buf) =>
		{
			int max = ((StaticLayout == 0) ? 6 : 10);
			int num2 = Math.Clamp(StaticZones, 1, max);
			for (int i = 0; i < Keymap.Length; i++)
			{
				int num3 = 0;
				if (num2 > 1)
				{
					if (StaticLayout == 0)
					{
						num3 = HorizontalZoneForRow(Keymap[i].Row, num2);
					}
					else
					{
						num3 = _xRank[i] * num2 / Keymap.Length;
						if (num3 >= num2)
						{
							num3 = num2 - 1;
						}
					}
				}
				Color color = StaticColors[num3];
				SetKey(buf, Keymap[i].Offset, color.R, color.G, color.B);
			}
		}, () => 1.0);
	}

	private static int HorizontalZoneForRow(int row, int zoneCount)
	{
		return zoneCount switch
		{
			1 => 0, 
			2 => (row > 3) ? 1 : 0, 
			3 => (row > 1) ? ((row <= 4) ? 1 : 2) : 0, 
			4 => (row != 0) ? ((row <= 2) ? 1 : ((row <= 4) ? 2 : 3)) : 0, 
			5 => (row != 0) ? ((row == 1) ? 1 : ((row == 2) ? 2 : ((row <= 4) ? 3 : 4))) : 0, 
			6 => row switch
			{
				4 => 4, 
				3 => 3, 
				2 => 2, 
				1 => 1, 
				0 => 0, 
				_ => 5, 
			}, 
			_ => 0, 
		};
	}

	private static int VkToKeymapIndex(int vk)
	{
		for (int i = 0; i < Keymap.Length; i++)
		{
			string name = Keymap[i].Name;
			if (((vk >= 65 && vk <= 90) || (vk >= 48 && vk <= 57)) && name.Length == 1 && name[0] == (ushort)vk)
			{
				return i;
			}
		}
		(int, string)[] array = new (int, string)[61]
		{
			(32, "Space"),
			(13, "Enter"),
			(8, "Backspace"),
			(9, "Tab"),
			(160, "ShiftL"),
			(161, "ShiftR"),
			(20, "CapsLock"),
			(162, "CtrlL"),
			(164, "AltL"),
			(165, "AltGr"),
			(37, "ArrowLeft"),
			(39, "ArrowRight"),
			(38, "ArrowUp"),
			(40, "ArrowDown"),
			(226, "ISO_Backslash"),
			(91, "Windows"),
			(219, "BracketL"),
			(221, "BracketR"),
			(186, "Semicolon"),
			(222, "Quote"),
			(220, "Backslash"),
			(188, "Comma"),
			(190, "Period"),
			(191, "Slash"),
			(189, "Minus"),
			(187, "Equals"),
			(192, "Grave"),
			(27, "Esc"),
			(112, "F1"),
			(113, "F2"),
			(114, "F3"),
			(115, "F4"),
			(116, "F5"),
			(117, "F6"),
			(118, "F7"),
			(119, "F8"),
			(120, "F9"),
			(121, "F10"),
			(122, "F11"),
			(123, "F12"),
			(44, "PrtSc"),
			(46, "Del"),
			(36, "Home"),
			(33, "PgUp"),
			(34, "PgDn"),
			(35, "End"),
			(96, "Numpad0"),
			(97, "Numpad1"),
			(98, "Numpad2"),
			(99, "Numpad3"),
			(100, "Numpad4"),
			(101, "Numpad5"),
			(102, "Numpad6"),
			(103, "Numpad7"),
			(104, "Numpad8"),
			(105, "Numpad9"),
			(110, "NumpadDecimal"),
			(107, "NumpadPlus"),
			(109, "NumpadMinus"),
			(106, "NumpadMul"),
			(111, "NumpadDiv")
		};
		for (int j = 0; j < array.Length; j++)
		{
			var (num, text) = array[j];
			if (vk != num)
			{
				continue;
			}
			for (int k = 0; k < Keymap.Length; k++)
			{
				if (Keymap[k].Name == text)
				{
					return k;
				}
			}
		}
		return -1;
	}

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	internal string SerializeConfig()
	{
		StringBuilder stringBuilder2;
		StringBuilder stringBuilder = (stringBuilder2 = new StringBuilder());
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder2);
		handler.AppendLiteral("mode=");
		handler.AppendFormatted((_activeMode >= 0) ? _activeMode : Math.Max(0, _lastSavedMode));
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("brightness=");
		handler.AppendFormatted(Brightness);
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("targetFps=");
		handler.AppendFormatted(TargetFps);
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("breathColor=");
		handler.AppendFormatted(BreathColor.ToArgb());
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("breathSpeed=");
		handler.AppendFormatted(BreathSpeed);
		stringBuilder7.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("breathSmooth=");
		handler.AppendFormatted(BreathSmooth);
		stringBuilder8.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("waveAngle=");
		handler.AppendFormatted(WaveAngle);
		stringBuilder9.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("waveSpeed=");
		handler.AppendFormatted(WaveSpeed);
		stringBuilder10.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder11 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("sparkleSpeed=");
		handler.AppendFormatted(SparkleSpeed);
		stringBuilder11.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder12 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("sparkleDensity=");
		handler.AppendFormatted(SparkleDensity);
		stringBuilder12.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder13 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
		handler.AppendLiteral("reactiveColor=");
		handler.AppendFormatted(ReactiveColor.ToArgb());
		stringBuilder13.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder14 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("reactiveRandom=");
		handler.AppendFormatted(ReactiveRandom ? 1 : 0);
		stringBuilder14.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder15 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("reactiveDuration=");
		handler.AppendFormatted(ReactiveDuration);
		stringBuilder15.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder16 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("wheelReverse=");
		handler.AppendFormatted(WheelReverse ? 1 : 0);
		stringBuilder16.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder17 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("wheelSpeed=");
		handler.AppendFormatted(WheelSpeed);
		stringBuilder17.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder18 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("lightningSpeed=");
		handler.AppendFormatted(LightningSpeed);
		stringBuilder18.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder19 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("lightningSmooth=");
		handler.AppendFormatted(LightningSmooth);
		stringBuilder19.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder20 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("lightningWidth=");
		handler.AppendFormatted(LightningWidth);
		stringBuilder20.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder21 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("lightningConcurrent=");
		handler.AppendFormatted(LightningConcurrent);
		stringBuilder21.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder22 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("flameSpeed=");
		handler.AppendFormatted(FlameSpeed);
		stringBuilder22.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder23 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("flameSmooth=");
		handler.AppendFormatted(FlameSmooth);
		stringBuilder23.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder24 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("rainSpeed=");
		handler.AppendFormatted(RainSpeed);
		stringBuilder24.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder25 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("rainDensity=");
		handler.AppendFormatted(RainDensity);
		stringBuilder25.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder26 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("rainColorCount=");
		handler.AppendFormatted(RainColorCount);
		stringBuilder26.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder27 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("rainColors=");
		handler.AppendFormatted(Colors(RainColors));
		stringBuilder27.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder28 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("matrixSpeed=");
		handler.AppendFormatted(MatrixSpeed);
		stringBuilder28.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder29 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("matrixStyle=");
		handler.AppendFormatted(MatrixStyle);
		stringBuilder29.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder30 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
		handler.AppendLiteral("matrixDensity=");
		handler.AppendFormatted(MatrixDensity);
		stringBuilder30.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder31 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("matrixColorCount=");
		handler.AppendFormatted(MatrixColorCount);
		stringBuilder31.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder32 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("matrixColors=");
		handler.AppendFormatted(Colors(MatrixColors));
		stringBuilder32.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder33 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("staticLayout=");
		handler.AppendFormatted(StaticLayout);
		stringBuilder33.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder34 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("staticZones=");
		handler.AppendFormatted(StaticZones);
		stringBuilder34.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder35 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("staticColors=");
		handler.AppendFormatted(Colors(StaticColors));
		stringBuilder35.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder36 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("closeTimer=");
		handler.AppendFormatted(CloseTimerMinutes);
		stringBuilder36.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder37 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("kbHidMode=");
		handler.AppendFormatted(KbHidMode);
		stringBuilder37.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder38 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder2);
		handler.AppendLiteral("kbPower=");
		handler.AppendFormatted(KbPowerOn ? 1 : 0);
		stringBuilder38.AppendLine(ref handler);
		return stringBuilder.ToString();
		static string Colors(IEnumerable<Color> values)
		{
			return string.Join(',', values.Select((Color color) => color.ToArgb()));
		}
	}

	public void QueueSaveConfig()
	{
		// 2026-10-10（① 安装后灯被熄灭）：QueueSaveConfig 的全部调用点都是用户的灯光操作
		// （RgbForm/SettingsForm 的点击、拖拽），⛔ 与本类 Dispose() 里那次无条件 SaveConfig 不同，
		// 因此这里可以安全地记下"用户明确设置过灯效"（否则纯 HID 用户永远等不到标记）。
		LightingSettingsStore.MarkUserLightingIntent("QueueSaveConfig");
		lock (_configLock)
		{
			if (_configSaveTimer == null)
			{
				_configSaveTimer = new Timer((object? _) =>
				{
					SaveConfig();
				}, null, -1, -1);
			}
			_configSaveTimer.Change(250, -1);
		}
	}

	public void SaveConfig()
	{
		try
		{
			lock (_configLock)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
				string text = ConfigPath + ".tmp";
				File.WriteAllText(text, SerializeConfig(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				File.Move(text, ConfigPath, overwrite: true);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RGB save cfg fail: " + ex.Message);
		}
	}

	public void LoadConfig()
	{
		try
		{
			if (File.Exists(ConfigPath))
			{
				// 2026-10-10（① 安装后灯被熄灭）：加载阶段不写"用户已配置"标记
				//（rgb.cfg 会被退出时的 SaveConfig 无条件写出来，不能当作用户意图）。
				_loadingConfig = true;
				try
				{
					LoadConfigLines(File.ReadAllLines(ConfigPath));
				}
				finally
				{
					_loadingConfig = false;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RGB load cfg fail: " + ex.Message);
		}
	}

	internal void LoadConfigLines(IEnumerable<string> lines)
	{
		foreach (string line in lines)
		{
			int num = line.IndexOf('=');
			if (num <= 0)
			{
				continue;
			}
			string text = line.Substring(0, num);
			string text2 = line.Substring(num + 1).Trim();
			switch (text)
			{
			case "rainColors":
				LoadColors(text2, RainColors);
				continue;
			case "matrixColors":
				LoadColors(text2, MatrixColors);
				continue;
			case "staticColors":
				LoadColors(text2, StaticColors);
				continue;
			}
			if (int.TryParse(text2, out var result))
			{
				switch (text)
				{
				case "mode":
					_lastSavedMode = result;
					break;
				case "brightness":
					Brightness = Math.Clamp(result, 0, 100);
					// 2026-10-12（亮度 0 归一化）：这是"上一次非 0 的亮度"的**持久化来源** ——
					// rgb.cfg 里那个 brightness 键本来就是用户上次调的值（见 SerializeConfig），
					// 所以 ⛔ 不新造配置键，只在这里把非 0 的历史 latch 住。
					LatchLastNonZeroBrightness(Brightness);
					break;
				case "targetFps":
					TargetFps = Math.Clamp(result, 15, 60);
					break;
				case "breathColor":
					BreathColor = Color.FromArgb(result);
					break;
				case "breathSpeed":
					BreathSpeed = Math.Clamp(result, 1, 100);
					break;
				case "breathSmooth":
					BreathSmooth = Math.Clamp(result, 0, 100);
					break;
				case "waveAngle":
					WaveAngle = Math.Clamp(result, 0, 360);
					break;
				case "waveSpeed":
					WaveSpeed = Math.Clamp(result, 1, 20);
					break;
				case "sparkleSpeed":
					SparkleSpeed = Math.Clamp(result, 1, 20);
					break;
				case "sparkleDensity":
					SparkleDensity = Math.Clamp(result, 1, 20);
					break;
				case "reactiveColor":
					ReactiveColor = Color.FromArgb(result);
					break;
				case "reactiveRandom":
					ReactiveRandom = result != 0;
					break;
				case "reactiveDuration":
					ReactiveDuration = Math.Clamp(result, 5, 60);
					break;
				case "wheelReverse":
					WheelReverse = result != 0;
					break;
				case "wheelSpeed":
					WheelSpeed = Math.Clamp(result, 1, 20);
					break;
				case "lightningSpeed":
					LightningSpeed = Math.Clamp(result, 1, 20);
					break;
				case "lightningSmooth":
					LightningSmooth = Math.Clamp(result, 1, 40);
					break;
				case "lightningWidth":
					LightningWidth = Math.Clamp(result, 1, 4);
					break;
				case "lightningConcurrent":
					LightningConcurrent = Math.Clamp(result, 1, 4);
					break;
				case "flameSpeed":
					FlameSpeed = Math.Clamp(result, 1, 20);
					break;
				case "flameSmooth":
					FlameSmooth = Math.Clamp(result, 1, 40);
					break;
				case "rainSpeed":
					RainSpeed = Math.Clamp(result, 1, 20);
					break;
				case "rainDensity":
					RainDensity = Math.Clamp(result, 1, 5);
					break;
				case "rainColorCount":
					RainColorCount = Math.Clamp(result, 1, 5);
					break;
				case "matrixSpeed":
					MatrixSpeed = Math.Clamp(result, 1, 20);
					break;
				case "matrixStyle":
					MatrixStyle = Math.Clamp(result, 0, 3);
					break;
				case "matrixDensity":
					MatrixDensity = Math.Clamp(result, 1, 5);
					break;
				case "matrixColorCount":
					MatrixColorCount = Math.Clamp(result, 1, 3);
					break;
				case "staticLayout":
					StaticLayout = Math.Clamp(result, 0, 1);
					break;
				case "staticZones":
					StaticZones = Math.Clamp(result, 1, 10);
					break;
				case "staticColor":
					StaticColor = Color.FromArgb(result);
					break;
				case "closeTimer":
					CloseTimerMinutes = Math.Clamp(result, 0, 120);
					break;
				case "kbHidMode":
					KbHidMode = Math.Clamp(result, 0, 9);
					break;
				case "kbPower":
					KbPowerOn = result != 0;
					// 2026-10-12（根因①）：读到的 0 就是"用户明确关过"的另一条证据来源（与 setter 同一票），
					// 必须在 _loadingConfig 期间也记上 —— 它决定"硬件报 On 也不能自动开灯"。
					KbPowerExplicitOff = result == 0;
					// 2026-10-10（① 安装后灯被熄灭）：rgb.cfg 里**明确写着 kbPower=0** 只可能来自用户的关灯动作
					// （SerializeConfig 的缺省写出恒为 1，退出时那次无条件 SaveConfig 也只会写 1），
					// 因此这是"用户明确选择"的可靠证据，而不是"文件存在"这种猜测 ⇒ 情况 C：
					// 即使用户从没碰过其它灯光 UI，也必须按他的意愿恢复成"关"，⛔ 不能被当成"从未配置"。
					// ⚠️ 只在读到的值为 0 时写标记；值为 1 与默认值不可区分，故不作证据。
					if (result == 0)
					{
						LightingSettingsStore.MarkUserLightingIntent("rgb.cfg kbPower=0（用户明确关过键盘灯）");
					}
					break;
				}
			}
		}
		static void LoadColors(string text3, Color[] target)
		{
			string[] array = text3.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			for (int i = 0; i < target.Length && i < array.Length; i++)
			{
				if (int.TryParse(array[i], out var result2))
				{
					target[i] = Color.FromArgb(result2);
				}
			}
		}
	}

	public void Dispose()
	{
		StopCurrentEffect();
		_disposed = true;
		lock (_configLock)
		{
			_configSaveTimer?.Change(-1, -1);
			_configSaveTimer?.Dispose();
			_configSaveTimer = null;
		}
		SaveConfig();
		lock (_lock)
		{
			_stream?.Dispose();
			_stream = null;
		}
	}
}
