using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using MechrevoLite.Gpu;
using MechrevoLite.Gpu.NVidia;
using MechrevoLite.Mode;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MechrevoLite.Helpers;

namespace MechrevoLite.Hardware;

public class MechrevoHw : IDisposable
{
	private sealed record PendingModeSwitch(int ExpectedOperatingMode, int? ExpectedCustomProfile, long ExpiresAtTick);

	private static readonly bool IsElevatedProcess = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

	public const int GpuCoreOffsetUserLimit = 500;

	public const int TurboGpuCoreOffsetMhz = 105;

	public const int TurboGpuMemoryOffsetMhz = 500;

	private const int GcuFallbackCoreOffsetMinimum = 0;

	private const int GcuFallbackCoreOffsetMaximum = 250;

	private const int GcuFallbackMemoryOffsetMinimum = -1000;

	private const int GcuFallbackMemoryOffsetMaximum = 2000;

	// (a)（2026-10-09，用户原话「OK 按你说的」）：GPU 超频回读的**多拍**序列（300ms / 2s / 4s）。
	// F4 那版是"首次不一致就阻塞 300ms 只重读一次"——实测驱动回读滞后 2~4s，300ms 常常来不及，
	// 于是写进去了也照样报失败/长期停在"已下发·暂时确认不了"。现在这一拍**不再阻塞**：
	// 写调用被接受就立刻返回，多拍重读由上层 PerfModeService 的懒验证（PerfLazyVerifier.ProbeDelaysMs，
	// 同一序列 300ms/2s/4s + 每次 GCU 状态刷新）在后台跑，读到一致再回读升级状态（见 PerfLazyVerifier）。
	// ⚠️ 这里保留 300ms 这一拍作为**首次即时重读**的延迟值（序列首拍），不再有任何 await Task.Delay 阻塞下发链。
	private const int GpuOverclockReadbackRecheckMs = 300;

	private const string Host = "127.0.0.1";

	private const int Port = 13688;

	private const string User = "UWPClient_User_4";

	private const string Pwd = "UWPClient_Pwd888881772688_4";

	private const string User3 = "UWPClient_User_3";

	private const string Pwd3 = "UWPClient_Pwd888881772688_3";

	private const string User2 = "UWPClient_User_2";

	private const string Pwd2 = "UWPClient_Pwd888881772688_2";

	private string _clientId;

	private string _clientUser = User;

	private string _clientPwd = Pwd;

	private int _clientIdFallbackCount;

	// 2026-10-09（GCU 订阅侧修复）：订阅「连续失败轮数」。一次订阅不成不再立刻判断开+回退 clientId，
	// 只有连续失败的轮数到阈值（MqttSubscribeRetryPolicy.RoundsBeforeFallback）才回退一档；订阅成功即清零。
	private int _subscribeFailureRounds;

	// 2026-10-09（GCU 订阅侧修复）：订阅重试循环是否在跑（0/1）。它为 1 且连接仍在时，
	// ConnectAsync 绝不重复 CONNECT（MQTTnet 对已连接的 client 再 ConnectAsync 会直接抛错，
	// 那会把「订阅没成」伪装成「连接失败」，正是我们要消掉的假象）。
	private int _subscribeRetryLoop;

	// 2026-10-09（本次修复：判死重建）：订阅重试循环「正用 ConnectAsync 重建连接」的瞬间（0/1）。
	// 循环在跑时 CONNECT 归它独占：别的调用方（重连循环 / 诊断）在 ConnectAsync 里让位 —— 重连循环有自己的
	// 250ms→30s 节奏，两边同时插 CONNECT 只会把「判死→重建→再试 SUBSCRIBE」的 2s→30s 退避压成更密的拆连。
	private int _subscribeRetryReconnecting;

	// 2026-10-09（GCU 订阅侧修复）：每次真正发起 CONNECT 的世代号。订阅重试循环带着自己的世代号，
	// 一旦发现世代变了（有人重新 CONNECT）就立刻退出，绝不与 ConnectAsync 抢同一条连接。
	private int _connectEpoch;

	internal const string PrimaryClientId = "UWPClient_4";

	internal const string HelperClientId = "UWPClient_3";

	internal const string SecondaryClientId = "UWPClient_2";

	// 该 broker 对 client_id 有白名单，且每个 id 配一套同名凭据（实测表见 tools/gcu_kbd_capture.py）。
	// 回退只能在白名单放行的 id 之间走，绝不能给 id 拼后缀（拼出来就是非白名单 id，会被直接拒）。
	private static readonly string[] ClientIdFallbackChain = new string[3] { PrimaryClientId, HelperClientId, SecondaryClientId };

	// 回退次数上限：UWPClient_4 -> UWPClient_3 -> UWPClient_2 最多两次，避免无限循环。
	private const int MaxClientIdFallbacks = 2;

	// 2026-10-09（批次3 #7「重连循环」）：⭐ clientId 回退**可恢复**（用户要求：回退不可逆会让 _4 永远没机会再试）。
	// 口径（只影响「已经决定要 CONNECT」的那一刻，⛔ 绝不为了恢复去拆一条好连接）：
	//   ① 会话已经稳定 ≥ ClientIdRecoverStableMs（5 分钟）⇒ 下次真要重连时把 clientId 恢复成**首选 _4**、
	//      回退计数清零（于是再降级又有一整套 MaxClientIdFallbacks 预算）；
	//   ② 两次恢复之间至少隔 ClientIdRecoverCooldownMs（10 分钟冷却）⇒ ⛔ 不会在 _4/_3/_2 之间来回抖；
	//   ③ 会话就绪时长不够 / 从没就绪过 ⇒ 保持当前 clientId（宁可不恢复，也不拿抖动换"可能更快"）。
	internal const int ClientIdRecoverStableMs = 300000;

	internal const int ClientIdRecoverCooldownMs = 600000;

	// 2026-10-09（GCU 订阅侧取证）：工厂改成显式传入「诊断用适配器工厂」。它只把默认实现包一层
	// （参数原样透传，连接行为逐字不变），额外留一份 IMqttChannelAdapter 用来补「连接四元组 + 收发字节」。
	private readonly MqttDiagAdapterFactory _diagAdapterFactory = new MqttDiagAdapterFactory();

	// 2026-10-09（GCU 订阅侧取证）：真报文级观察窗（挂在 _client.InspectPacketAsync 上，只读、只写日志）。
	private readonly MqttPacketWatch _packetWatch = new MqttPacketWatch();

	private readonly MqttClientFactory _factory;

	private IMqttClient? _client;

	private readonly Func<string, object, Task>? _publishOverride;

	private readonly Func<IGpuOverclockControl?>? _gpuOverclockFactory;

	private readonly IGpuOverclockElevatedApplier? _elevatedGpuOverclockApplier;

	private readonly object _gpuOverclockLock = new object();

	private IGpuOverclockControl? _gpuOverclock;

	private bool? _directGpuOverclockEnabled;

	private int? _elevatedGpuCoreReadback;

	private int? _elevatedGpuMemoryReadback;

	private readonly bool _persistDirectGpuOverclock;

	private readonly Func<bool> _isProcessElevated;

	private volatile bool _disposed;

	private int _disposedFlag;

	// ===== 2026-10-15（C-1 第 2 步：后端化 + 老机型接口接线）=================================
	// 后端选择：唯一真源是 MechrevoBackendOrder.Candidates（有序候选表），本处只存**结果**。
	// ⭐ 加新后端（例如宝龙达）只需在候选表里加一项，本文件不用改任何 else/if。
	private MechrevoBackendKind _activeBackend;

	// 老机型 WMI 后端（root\WMI:MICommonInterface）。⚠️ 字段与职责对照《差异与合并计划》A-22。
	private bool _allowLegacyProbe;

	private bool _legacyProbed;

	// ⚠️ 与本树既有约定一致（⛔ 不写 `?` 可空注解）：本工程未开 nullable，给引用类型加 `?`
	//    每个都会多出一条 CS8632 警告（本文件已有 170+ 条同类）。运行时语义完全相同。
	private LegacyMifsWmi _legacyMifs;

	// ⭐⭐ 2026-10-16（0.291.9 beta22.9 · 宝龙达适配）：宝龙达模具后端（⭐ 蛟龙16Pro 等）。
	//     ⭐ 它**协议上与 LegacyMifsWmi 同族**（⭐ 同一个 root\WMI:MICommonInterface / 250-251 / 功能号 8 ✅），
	//     但**能力更全**（⭐ 风扇 · ⭐ CPU温度 · ⭐ LOGO灯 · ⭐ 功耗墙 ✅）。
	//     ⭐⭐ 真机已验证（⭐ 2026-10-16 · 机器 C）：⭐ 6 个功能号全部可读，⭐ 档位语义与我们完全一致 ✅
	//     ⇒ ⭐ 因此**复用老接口那一整套刷新/切档管线**（⭐ 见 ReadBuiltInMode / SwitchBuiltInModeAsync ✅）。
	private BaolongdaBackend _baolongda;

	/// <summary>
	/// ⭐⭐ 2026-10-16（0.291.9.1）：⭐ **宝龙达是否已经成功读到过风扇转速** ✅。
	/// <para>⭐ 用途：⭐ 喂给 <see cref="FanZeroIsMeaningful" /> ✅ —— ⭐ 没读到过就不要把
	/// <c>0rpm</c> 当成"⭐ 风扇停了" ⚠️，⭐ 那只是"⭐ 没数据" ✅。</para>
	/// <para>⭐ 一旦读到过就置 true 并**不再回落** ✅（⭐ 风扇值本身每 3 秒刷新 ✅）。</para>
	/// </summary>
	private volatile bool _baolongdaFanSeen;

	/// <summary>
	/// ⭐⭐ 2026-10-16（0.291.9.1）：⭐ 宝龙达**读风扇失败**是否已经记过日志 ✅（⭐ 0=没记过 · ⭐ 1=记过 ✅）。
	/// <para>⚠️ 目的：⭐ 读失败时用 <c>Logger.WriteError</c> ⭐ —— ⭐ 那是
	/// <b>默认「仅错误」级别下也可见</b> 的唯一可靠方式 ✅（⭐ <c>WriteLine</c> 要看消息里有没有
	/// 错误关键词才判级别 ⚠️ ⇒ ⭐ 不能保证可见 ❌）。⭐ 而本方法每 3 秒跑一次 ⚠️
	/// ⇒ ⭐ 必须**按状态翻转只记一次** ⇒ ⭐ 否则就是每 3 秒刷一行 ❌。</para>
	/// </summary>
	private int _bdFanFailLogged;

	private Timer _legacyTimer;

	private long _legacySnapshotVersion;

	private readonly object _legacyStateGate = new object();

	private readonly SemaphoreSlim _legacyRefreshGate = new SemaphoreSlim(1, 1);

	private int _reconnecting;

	private long _reconnectStartedTick;

	private int _connectionGeneration;

	// 2026-10-09（批次3 #7「重连循环」）：本次重连/重试的**原因**（只为日志可读，⛔ 不改任何时序/判定）。
	// 取值示例：`对端断开(Reason=…)` / `空闲无入站到点(Idle)` / `CONNACK 未就绪: …` / `无 CONNACK(连接阶段抛错): …` /
	// `订阅未就绪(连接仍在)` / `订阅未就绪且连接不可用` / `初始状态未就绪: …`。
	// ⚠️ 文案刻意**不含** Logger.ClassifySeverity 的 Error 关键字（timeout/失败/异常/超时/拒绝…）：
	// 这些原因会被拼进"重连中/重连尝试"这类**正常路径**日志里，含关键字就会把 Errors 环形缓冲刷满。
	private string _reconnectReason = "(未标注)";

	// 2026-10-09（批次3 #7）：最近一次「会话就绪」的时刻（NotifyConnectionReady 写）——
	// 「clientId 回退可恢复」（见 ClientIdRecoverStableMs）的稳定性判据：会话撑够了才敢回首选 id。
	private long _sessionReadySinceTick;

	// 2026-10-09（批次3 #7）：上一次「clientId 恢复首选」的时刻（冷却用，见 ClientIdRecoverCooldownMs）。
	private long _lastClientIdRecoverTick;

	private readonly MqttReconnectCoordinator _reconnect = new MqttReconnectCoordinator();

	private volatile bool _subscriptionsReady;

	private long _sessionReadyTick;

	private int _quickDropStreak;

	private long _lastTelemetryTick;

	private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _controlLock = new SemaphoreSlim(1, 1);

	private PendingModeSwitch? _pendingModeSwitch;

	private long _settingStatusVersion;

	private long _colorCalibrationStatusVersion;

	private long _gpuModeStatusVersion;

	private long _gpuSwitchResultVersion;

	private long _fanStatusVersion;

	private long _customProfileStatusVersion;

	private long _fanTableVersion;

	private long _keyboardStatusVersion;

	private long _lcStatusVersion;

	private long _lcStatusReceivedAt;

	private long _cpuInfoReceivedAt;

	private long _gpuInfoReceivedAt;

	private long _fanInfoReceivedAt;

	private int _lcConsecutiveMeterFaults;

	internal const int ModeSwitchPendingWindowMs = 8000;

	private readonly Dictionary<string, int> _displayColorParameters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	internal static readonly string[] DisplayColorParameterFields = new string[17]
	{
		"GamingBrightness", "GamingRed", "GamingGreen", "GamingBlue", "GamingColorTemp", "GamingContrast", "VideoBrightness", "VideoColorTemp", "ReadBrightness", "ReadBlue",
		"ReadColorTemp", "CutomizedBrightness", "CutomizedRed", "CutomizedGreen", "CutomizedBlue", "CutomizedColorTemp", "CutomizedContrast"
	};

	public const int FanSwitchSpeedStepMs = 100;

	internal const int CustomModeFanTableWaitMs = 4000;

	private static readonly string[] DgpuDirectStatusTokens = new string[8] { "DIRECT_CONNECT_TOGGLE_ON", "DIRECT_CONNECT_TOGGLE_IGPU", "DIRECT_CONNECT_TOGGLE_OFF", "DIRECT_CONNECT_ON", "DIRECT_CONNECTION_ON", "DIRECT_CONNECT_IGPU", "DIRECT_CONNECT_OFF", "DIRECT_CONNECTION_OFF" };

	private static readonly string[] IgpuOnlyStatusTokens = new string[6] { "IGPU_ONLY_CONNECT_RB_ON", "IGPU_ONLY_ON", "IGPU_ONLY_CONNECT_RB_AUTO", "IGPU_ONLY_AUTO", "IGPU_ONLY_CONNECT_RB_OFF", "IGPU_ONLY_OFF" };

	private string[] _lcDeviceMacs = Array.Empty<string>();

	private int[] _hzList = Array.Empty<int>();

	// 2026-10-09（多显示器刷新率修复）：档位校正可能同时来自 MQTT 回调线程与 UI 线程（显示配置变化），
	// 用独立锁串行化，避免两次校正互相踩。
	private readonly object _hzCorrectionLock = new object();

	// 2026-10-09：只在"识别到的内屏 / 可信度 / 实测档位"变化时记一条识别日志（供真机多显示器验证取证）。
	private string _hzScreenDiagKey = "";

	private int _pl4Raw = -1;

	private int _pl4RawMinimum = -1;

	private int _pl4RawMaximum = -1;

	private readonly byte[][] _defaultCpuDuty = new byte[3][];

	private readonly byte[][] _defaultGpuDuty = new byte[3][];

	private readonly byte[][] _defaultCpuUpT = new byte[3][];

	private readonly byte[][] _defaultGpuUpT = new byte[3][];

	private string _lastCurveHash = "";

	internal static readonly string[] SubscribedTopicFilters = new string[14]
	{
		"System/#", "Fan/#", "Setting/#", "Settings/#", "Customize/#", "GPUDevice/#", "BT_LC/#", "Keyboard/#", "HidLightbar/#", "HidLightbar_Logo/#", "HidLightbar_Hinge/#", "HidLightbar_Sync/#",
		"LCHWOC/#", "Display/#"
	};

	internal static readonly string[] StatusLogKeys = new string[10] { "lc-status", "kb-status", "hwoc-status", "fan-table", "display-color", "resolved-caps", "lb-status-HidLightbar/Status", "lb-status-HidLightbar_Logo/Status", "lb-status-HidLightbar_Hinge/Status", "lb-status-HidLightbar_Sync/Status" };

	internal static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5L);

	/// <summary>2026-10-15（C-1 第 2 步）：老机型接口的后台刷新间隔（对照 A-22「每 3 s 刷」）。</summary>
	internal const int LegacyRefreshIntervalMs = 3000;

	public static readonly TimeSpan FanInfoMaximumAge = TimeSpan.FromSeconds(6L);

	private int _turboFanRepair;

	internal static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(2L);

	// 2026-10-09（批次3 #7「重连循环」）：⭐ keepalive 由 3s 放宽到 30s（默认值；config 键 mqtt_keepalive_seconds
	// 可覆盖，夹在 5~300 秒）。**机制依据**（不是猜）：
	//   ① MQTTnet 的 keepalive 看门狗按「1.5 × keepalive」判死 —— 本文件订阅重试循环的既有注释就是这么记的；
	//   ② tools\GCU抓包.ps1 的真机实测也记着「每次 ~4.5s 后客户端自己超时断开（MqttCommunicationTimedOutException）」
	//      —— 4.5s 正好 = 1.5 × 3s。也就是说：**对端只要 4.5s 没有入站数据，我们就把连接判死**；
	//      于是「CONNACK 成功 → 还没等到 SUBACK → 判死 → 断开 → 重连」被压成 ~4.5~8s 一轮，
	//      机主在 fake-broker 抓包上看到的重连循环就是这么来的（订阅侧再跟着走一遍重试/回退）。
	// ⭐ 口径（用户明确要求）：「空闲无入站」**不是断线** —— 桥/GCU 起得慢、或空闲期没有下行报文都是正常的。
	//    看门狗在判死前会按 keepalive 周期发 PINGREQ（MQTT 保活语义），30s 周期给桥留出回 PINGRESP 的时间。
	// ⚠️ 代价（如实记账）：**真·死连接**（对端进程没了）的发现从 4.5s 延后到 ~45s；
	//    这一条不影响 45s 遥测过期那条路径（Program.cs 的 TelemetryStaleAfter 只补发全量请求、不拆连）。
	// ⚠️ 保留 MQTTnet 的 1.5×keepalive 判死（不自己另写看门狗）：它判死前必然先发过 PINGREQ，
	//    而 4.5s 那个窗口对仍在预热的桥来说太短 —— 放宽周期即可，不需要动判死逻辑本身。
	internal static readonly TimeSpan KeepAlivePeriod = TimeSpan.FromSeconds(30L);

	/// <summary>
	/// 2026-10-09（批次3 #7）：本次 CONNECT 实际使用的 keepalive（config 键 <c>mqtt_keepalive_seconds</c>，
	/// 默认 <see cref="KeepAlivePeriod" />=30s，夹在 5~300 秒；读配置失败也退回默认值，绝不抛）。
	/// ⚠️ 只在**发起 CONNECT 的那一刻**读一次，不影响任何既有判定。
	/// </summary>
	private static TimeSpan ResolveKeepAlivePeriod()
	{
		try
		{
			int seconds = AppConfig.Get("mqtt_keepalive_seconds", (int)KeepAlivePeriod.TotalSeconds);
			return TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 300));
		}
		catch
		{
			return KeepAlivePeriod;
		}
	}

	internal string ClientId => _clientId;

	public int CpuTemp { get; private set; }

	public int GpuTemp { get; private set; }

	public int CpuUsage { get; private set; }

	public int GpuUsage { get; private set; }

	public int CpuFrequency { get; private set; }

	public int GpuCoreFreq { get; private set; }

	public int VramUsedMb { get; private set; }

	public int RamUsage { get; private set; }

	public double RamUsedGb { get; private set; }

	public int CpuFanRpm { get; private set; }

	public int GpuFanRpm { get; private set; }

	public int CpuFanDuty { get; private set; }

	/// <summary>
	/// ⭐⭐ 2026-10-16（0.291.9.1 · 修"⭐ 宝龙达机型风扇读数显示 <c>0rpm 0%</c>"⭐）：
	/// <para>⚠️ <b>根因</b>：⭐ 本属性原来 ⭐ <b>恒为 <c>true</c></b> ⚠️ ⇒ ⭐ 展示层
	/// （⭐ <c>SettingsForm.TelemetryRest</c> ✅）会把 ⭐ "⭐ 值 = 0" ⭐ 也当成
	/// <b>有意义的读数</b>打出来 ❌ ⇒ ⭐ 界面上就出现 ⭐ <c>0rpm 0%</c> ⚠️
	/// —— ⭐ 而那个 0 ⭐ 其实只是 ⭐ <c>int</c> 的**默认值**（⭐ = "⭐ 一个字节都没读到" ⚠️）❌</para>
	/// <para>⭐ <b>修法</b>：⭐ 只在 ⭐ <b>真的读到过风扇</b> ⭐ 之后才认为 0 有意义 ✅。
	/// ⭐ 宝龙达：⭐ 由 <c>RefreshBaolongdaSensors</c> 读成功时置 <c>_baolongdaFanSeen</c> ✅；
	/// ⭐ 其余后端（⭐ GCU / ⭐ 老同方 ✅）⭐ <b>恒 <c>true</c></b> ⇒ ⭐ <b>行为逐位不变</b> ✅。</para>
	/// </summary>
	public bool FanZeroIsMeaningful => !UsesBaolongda || _baolongdaFanSeen;

	public bool MidFanSeen => false;

	public int BatteryCycleCount { get; private set; } = -1;

	public bool BatteryAbnormal { get; private set; }

	public string BatteryCapacityText { get; private set; } = "";

	public string NetworkDownload { get; private set; } = "";

	public string NetworkUpload { get; private set; } = "";

	public bool NetworkInfoSeen { get; private set; }

	public bool HardwareInfoSeen { get; private set; }

	public string EcFirmwareVersion { get; private set; } = "";

	public bool FanErrorSeen { get; private set; }

	public bool FanError { get; private set; }

	public int GpuFanDuty { get; private set; }

	public int BatteryPercent { get; private set; }

	public bool IsAC { get; private set; }

	public bool FanBoost { get; private set; }

	public ConcurrentDictionary<string, bool> QuickSwitches { get; } = new ConcurrentDictionary<string, bool>();

	public bool UsbCharger { get; private set; }

	public int DeepSleepTime { get; private set; }

	public int CloseTimerMinutes { get; private set; } = -1;

	// 2026-10-09（warmupreload）：能力位快照从"构造时定死"改为可整体替换的字段 —— 首次装 GCU 时
	// LM 可能在 GCU 写 ItemSupport 之前就构造完了本对象，于是 Capabilities 里几乎什么都没有
	// （ProfileAvailable=false ⇒ 一大批行被判"不支持"并永久隐藏）。现在支持**预热完成后整体重读一次**
	// （见 ReloadCapabilities）：调用点唯一（SettingsForm 的 GCU 预热软重载），其余读法/语义一字未变。
	// ⚠️ 字段用 volatile：重载发生在 UI 线程，读它的可能有 MQTT/后台线程（本对象本来就多线程读）。
	private volatile MechrevoDeviceCapabilities _capabilities;

	// 2026-10-15（C-1 第 2 步）：能力位从「只读」放宽为「可整体替换」—— 老机型后端探中后要换成
	// ProjectId="MICommonInterface" 的最小能力集（见 ApplyLegacyBackendAsync）。
	// ⚠️ 只放宽 setter 可见性，读法/语义一字未变：还是同一个字段、同一个描述方法、同一个 CapabilitiesChanged 事件。
	public MechrevoDeviceCapabilities Capabilities { get; private set; }

	/// <summary>
	/// 2026-10-15（C-1 第 2 步「后端化」）：**本次会话实际在用的后端**（只读，即探测结果）。
	/// <see cref="MechrevoBackendKind.None" /> = 还没探测过；<see cref="MechrevoBackendKind.Gcu" /> = 没探到老接口，
	/// 退回原来的 GCU 通路（本机就是这一档，行为与改动前一致）。
	/// ⚠️ 本属性只**反映**谁在管事，⛔ 不参与任何下发决策（决策一律读 <see cref="UsesLegacyMifs" /> / <see cref="IsGcuConnected" />）。
	/// </summary>
	internal MechrevoBackendKind ActiveBackend => _activeBackend;

	/// <summary>
	/// 后端候选表里「GCU」那一项的哨兵对象：GCU 后端**没有独立连接对象**（它就是本对象自己的 MQTT 客户端），
	/// 所以候选表里用一个非 null 单例表示「轮到 GCU 了 ⇒ 即选中它」。
	/// </summary>
	internal static readonly object GcuBackendMarker = new object();

	/// <summary>
	/// ⭐ 2026-10-15 晚（用户反馈「有 50 系机主说 GCU 连不上」）：**这台机器到底装没装 GCU 服务**。
	/// <para>用途：后端探测表里「GCU」那一项原来是**无条件**的哨兵 ⇒ 只要老接口 WMI 类探到就永远轮不到 GCU；
	/// 而 <c>root\WMI:MICommonInterface</c> 是同方通用接口、**新机型（含 50 系）也可能带** ⇒ 会被误判成老机型、
	/// **根本不去连 GCU**。有了本判据就能「装了 GCU ⇒ 优先 GCU；没装才试老接口」。</para>
	/// <para>判据（**全只读**，不启服务、不发 MQTT、不改注册表）：① 服务注册表键
	/// <c>HKLM\SYSTEM\CurrentControlSet\Services\GCUBridge</c> 或 <c>…\GCUService</c> 存在；
	/// ② 否则看 GCU 是否**留下过** ItemSupport 快照（<see cref="MechrevoDeviceCapabilities.HasItemSupportContent" />，
	/// 覆盖"服务名不同/由驱动拉起"的机型）。任一成立即认为装了 —— 宁可偏向 GCU：偏错的代价只是回到改动前的行为
	/// （显示「GCU 未连接」并重试），而偏到老接口会把 GCU 机型整个功能丢掉。</para>
	/// </summary>
	internal static bool IsGcuServiceInstalled
	{
		get
		{
			try
			{
				foreach (string name in new string[2] { "GCUBridge", "GCUService" })
				{
					using Microsoft.Win32.RegistryKey registryKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + name);
					if (registryKey != null)
					{
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("IsGcuServiceInstalled: 读服务注册表失败（改看 ItemSupport 快照）: " + ex.Message);
			}
			try
			{
				return MechrevoDeviceCapabilities.HasItemSupportContent();
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("IsGcuServiceInstalled: 读 ItemSupport 失败（按未装 GCU 处理）: " + ex2.Message);
				return false;
			}
		}
	}

	/// <summary>
	/// 2026-10-09（warmupreload）：丢弃能力位缓存（<see cref="MechrevoDeviceCapabilities.Invalidate" />）
	/// 并整体重读一次，返回"快照是否真的换了内容"。**只读**：只读注册表 ItemSupport / HARDWARE\DESCRIPTION
	/// 与本进程已有上报，不写注册表/EC、不发任何硬件指令。
	/// ⚠️ 只为"首次装 GCU 预热期漏报 ⇒ 永久隐藏"这一个场景服务；正常路径（<c>OnServiceProfileMayHaveChanged</c>）
	/// 仍然只 Invalidate 全局静态缓存，本字段不动 ⇒ 默认态零变化。
	/// </summary>
	public bool ReloadCapabilities()
	{
		long revisionBefore = MechrevoDeviceCapabilities.SnapshotRevision;
		MechrevoDeviceCapabilities fresh = MechrevoDeviceCapabilities.Refresh();
		bool replaced = !ReferenceEquals(fresh, _capabilities);
		_capabilities = fresh;
		return replaced || MechrevoDeviceCapabilities.SnapshotRevision != revisionBefore;
	}

	public bool FanStatusSeen { get; private set; }

	public bool FanCurveSeen { get; private set; }

	public bool SettingStatusSeen { get; private set; }

	public bool GpuDeviceStatusSeen { get; private set; }

	public bool DeviceSwitchStatusSeen { get; private set; }

	public bool LchwocStatusSeen { get; private set; }

	public bool KeyboardStatusSeen { get; private set; }

	public bool LightbarStatusSeen { get; private set; }

	public bool LogoLightStatusSeen { get; private set; }

	public bool? LightbarLogoSupport { get; private set; }

	public bool? LightbarBaseSupport { get; private set; }

	public bool? LightbarNewLogoSupport { get; private set; }

	public bool? LightbarMbLogoSupport { get; private set; }

	public bool HingeLightStatusSeen { get; private set; }

	public string HingeStatusType { get; private set; } = "";

	public bool SyncLightStatusSeen { get; private set; }

	public string SyncStatusType { get; private set; } = "";

	public string LightbarStatusType { get; private set; } = "";

	/// <summary>
	/// 2026-10-09：各灯条 Status 主题最近一次报文的**客观指纹**（type / powerStatus / power / light / brightNess
	/// 五个字段原样拼接），供 Program 判断「灯带 与 Logo 两条逻辑行是不是同一块物理灯带」。
	/// 只读缓存、不参与任何能力判定，也不发任何指令。
	/// </summary>
	private readonly ConcurrentDictionary<string, string> _lightbarStatusFingerprint = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public bool LightbarStatusFingerprintSeen(string topic)
	{
		return _lightbarStatusFingerprint.ContainsKey(topic);
	}

	/// <summary>取该灯条 Status 主题最近一次的字段指纹；从未收到过返回 null。</summary>
	public string? LightbarStatusFingerprint(string topic)
	{
		return _lightbarStatusFingerprint.TryGetValue(topic, out string? value) ? value : null;
	}

	/// <summary>
	/// 2026-10-09（lightmerge）：两个灯条主题（HidLightbar/Status 与 HidLightbar_Logo/Status）的指纹
	/// **曾经不同**过（哪怕只有一帧）—— latched 只读观测，作为「Logo 是独立设备」的证据。
	/// 只读、不参与任何下发/能力判定，也不写 EC / 注册表。
	/// </summary>
	private int _lightbarStatusDivergedEver;

	public bool LightbarStatusDivergedEver => Volatile.Read(ref _lightbarStatusDivergedEver) != 0;

	/// <summary>
	/// 2026-10-09（lightmerge）：每收到一条灯条 Status，就与**另一个**灯条主题的当前指纹比一次；
	/// 两边都有值且不同 ⇒ latched 置位（进程内永不清除 —— 这是"独立设备"的证据，宁可保守：
	/// 一旦记下，灯光额外汇就再也不会自动合并，只会保持现状）。
	/// </summary>
	private void NoteLightbarStatusDivergence(string topic)
	{
		string other = string.Equals(topic, "HidLightbar_Logo/Status", StringComparison.OrdinalIgnoreCase) ? "HidLightbar/Status" : "HidLightbar_Logo/Status";
		string? mine = LightbarStatusFingerprint(topic);
		string? theirs = LightbarStatusFingerprint(other);
		if (mine == null || theirs == null || string.Equals(mine, theirs, StringComparison.Ordinal))
		{
			return;
		}
		if (Interlocked.Exchange(ref _lightbarStatusDivergedEver, 1) == 0)
		{
			Logger.WriteInfo($"[lightmerge] 灯带/Logo 两主题指纹出现过分歧（{topic} vs {other}）=> 记为「独立设备」证据，额外汇不再自动合并");
		}
	}

	/// <summary>把最近一次报文的关键字段拼成指纹（缺失字段取 "-"，与 LB 日志同一口径）。</summary>
	private static string LightbarFingerprint(JObject o)
	{
		return "type=" + LightbarRawField(o, "type")
			+ "|powerStatus=" + LightbarRawField(o, "powerStatus")
			+ "|power=" + LightbarRawField(o, "power")
			+ "|light=" + LightbarRawField(o, "light")
			+ "|brightNess=" + LightbarRawField(o, "brightNess");
	}

	public string? BiosProjectId { get; private set; }

	public bool? LightbarHingeSupport { get; private set; }

	public string DisplayColorMode { get; private set; } = "";

	public bool? DisplayFeatureOn { get; private set; }

	public bool? DisplayEffectOn { get; private set; }

	public string DisplayEffectMode { get; private set; } = "";

	public Dictionary<string, DisplayEffectProfile> DisplayEffectProfiles { get; } = new(StringComparer.OrdinalIgnoreCase);

	public string NvControlPanelPreference { get; private set; } = "";

	public IReadOnlyDictionary<string, int> DisplayColorParameters => _displayColorParameters;

	public bool LcStatusSeen { get; private set; }

	public bool ScreenBrightnessSeen { get; private set; }

	public bool TouchpadSeen { get; private set; }

	public bool WifiSeen { get; private set; }

	public bool BluetoothSeen { get; private set; }

	public bool WebcamSeen { get; private set; }

	public bool UsbChargerSeen { get; private set; }

	public bool FanBoostSeen { get; private set; }

	public bool DeepSleepSeen { get; private set; }

	public bool CopilotSeen { get; private set; }

	public bool AcRecoverySeen { get; private set; }

	public bool HighPerformanceSeen { get; private set; }

	public bool WinKeySeen { get; private set; }

	public bool FnKeySeen { get; private set; }

	public bool OsdSeen { get; private set; }

	public bool NumpadSeen { get; private set; }

	public bool TouchpadToggleSeen { get; private set; }

	public bool SingleColorKbSeen { get; private set; }

	public bool UniOmniSeen { get; private set; }

	public bool PowerLightSeen { get; private set; }

	public int PowerLightBrightness { get; private set; } = -1;

	public bool GameWhitelistSeen { get; private set; }

	public bool BatteryLogoSeen { get; private set; }

	public bool CpuAdvancedPerformanceSeen { get; private set; }

	public bool? OverclockMenuSupport { get; private set; }

	public bool? WhisperModeSupport { get; private set; }

	public bool WhisperMode { get; private set; }

	public bool WhisperModeSeen { get; private set; }

	public int WhisperModeLevel { get; private set; } = -1;

	public bool FanRespectiveSeen { get; private set; }

	public bool ColorCalibrationSeen { get; private set; }

	public bool ColorCalibrationSwitchSeen { get; private set; }

	public bool ColorCalibrationModeSeen { get; private set; }

	public bool TccStatusSeen { get; private set; }

	public bool? DgpuDirectStatusSupport { get; private set; }

	/// <summary>
	/// 2026-10-06（gcu 门）：GCU 原文里的 DiscreteGpuDirectConnectionSwitch_Support 取值
	/// （实测形态 <c>"Support"</c> / <c>"NotSupport"</c>，见 抓包日志.txt 与
	/// diagnostics\GCU_kbd_capture_*.log 的 Setting/Status 报文）。
	/// 只在解析成功时记录，用于"GCU 明确说不支持"时写一行可读日志（见 <see cref="CanOfferGpuModeSwitch"/>）。
	/// </summary>
	internal string? GcuDgpuDirectSupportRaw { get; private set; }

	public bool? IgpuOnlyStatusSupport { get; private set; }

	public bool? IgpuSwitchBlocked { get; private set; }

	public int IgpuOnlySaveMode { get; private set; } = 1;

	public bool? LchwocSupportReported { get; private set; }

	public int GpuSwitchResult { get; private set; } = -1;

	public bool GpuSwitchResultReported { get; private set; }

	public long GpuSwitchResultVersion => Interlocked.Read(in _gpuSwitchResultVersion);

	internal int ConnectionGeneration => Volatile.Read(in _connectionGeneration);

	internal bool IsReconnecting => Volatile.Read(in _reconnecting) != 0;

	internal long ReconnectAgeMs
	{
		get
		{
			long num = Volatile.Read(in _reconnectStartedTick);
			if (num == 0L)
			{
				return 0L;
			}
			return Environment.TickCount64 - num;
		}
	}

	public bool SupportsDgpuDirect => DgpuDirectStatusSupport ?? Capabilities.DgpuDirect;

	public bool SupportsIgpuOnly => Capabilities.IgpuOnly;

	/// <summary>
	/// 2026-10-06（igpuscope）：有没有**可靠的**「核显模式」能力位 —— 也就是 50 系 GCU 的
	/// <c>IGpuOnlyConnectionSwitch_Support</c>。策略存档（核显模式显示策略-方案存档.md §1/§3④）写得很清楚：
	/// 40 系那条判据（<c>iGPUModeOnlySupport</c>）**不可靠**，40 系一律交回「用户开关 / 试过就记住 / ask 代理」。
	/// 但 <see cref="SupportsIgpuOnly"/> 是 4 个候选字段的 OR（见 MechrevoDeviceCapabilities.FromValues），
	/// 40 系那条也算在内 —— 本机（翼龙15Pro，Gen40）实测注册表：
	/// <c>ItemSupport\iGPUModeOnlySupport=1</c> 而 <c>IGpuOnlyConnectionSwitch_Support</c> 缺失
	/// （日志里 IgpuStatus=False），于是上一轮的 "50 系能力位" 分支把 40 系也吞了，
	/// 使「显示核显模式」开关在本机点得动却不生效（CanOfferIgpuOnly 恒为 true，日志实测 IgpuOnly=True 且 igpu_only_ui=hide）。
	/// 于是这里把「可靠能力位」限定为：有 OR 能力位 且 不是 40 系（40 系没有可靠位）。
	/// 50 系及以后、以及无 40 系字段的机型行为完全不变。
	/// ⚠️ 2026-10-09（igpugate）：本属性**不再参与**「核显模式」显示判据（CanOfferIgpuOnly 改用三态 +
	/// <see cref="IgpuOnlyHardwareVerdict"/>，40 系也信任能力位）。它现在只剩一个用途：
	/// SettingsForm.ShowIgpuOnlyGuideIfNeeded 决定"要不要弹一次升级引导问用户"。
	/// </summary>
	public bool HasReliableIgpuOnlyCapability => SupportsIgpuOnly && DgpuGeneration != DgpuGenerationKind.Gen40;

	/// <summary>
	/// 2026-10-06 D-1：这个「核显模式」按钮是不是**猜**出来的 ——
	/// 本机没有可靠判据、用户也没明确表态（答的是「不确定」/跳过/还没被问过），而且他还没真试过一次。
	/// 只有为 true 时，界面上才加那句「本机可能不支持，可试一次」的提示（见 SettingsForm.GpuRowStatusText）。
	/// 一旦用户表态（有/没有）或试过一次，提示就消失 —— 这正是「下次就不再猜了」。
	/// 2026-10-09（igpugate）：<see cref="IgpuOnlyHardwareVerdict"/> 有结论（true/false 都算）时同样**不是猜** ——
	/// 自动判据已经由硬件一票定音（true 直接显示、false 直接隐藏），不需要"可试一次"的提示，
	/// 也不再记录"试过就记住"（仅在读不到硬件结论的机型上保留 D-1 的整套语义）。
	/// </summary>
	public bool IgpuOnlyUiIsGuess
	{
		get
		{
			if (DgpuGeneration == DgpuGenerationKind.Gen30 || IgpuOnlyHardwareVerdict.HasValue)
			{
				return false;
			}
			if (!IgpuOnlyAttempt.AnswerIsUncertain || IgpuOnlyAttempt.HasVerdict)
			{
				return false;
			}
			// 按钮本身没显示（用户选「强制隐藏」）时无需提示。
			// 经上面两道 return，能走到这里说明 igpu_only_ui 是 auto、硬件结论未知、用户还没表态。
			return CanOfferIgpuOnly;
		}
	}

	/// <summary>
	/// 2026-10-09（igpurou，极光 XPro 诊断包）：核显省电行（混合模式下免重启切核显）的能力判据。
	/// 原来只看静态能力位 <see cref="MechrevoDeviceCapabilities.GpuHotSwap"/>（= 注册表
	/// <c>ItemSupport\IGpuOnlyConnectionSwitch_Support</c> 走 Number() 词表）。实测该注册表键
	/// **不是权威来源**：本机（翼龙15Pro，Gen40）注册表里根本没有这个键（只有 iGPUModeOnlySupport=1），
	/// 而 GCU 在 MQTT 实时上报里照样给了它明确取值（日志 IgpuStatus=False）——
	/// 说明这个能力位由 GCU 运行时裁定，注册表只是残缺镜像。
	/// 极光 XPro 诊断包正是这一形态：静态位说"否"（系统信息 显卡热切换: 否），
	/// 实时上报说"是"（TrayGPU: IgpuStatus=True；2026-10-05 与该包两次独立抓包一致）→ 行被误挡。
	/// 判据改为「真实上报 &gt; 静态能力 &gt; 不显示」：
	///   ① 静态能力位为真 → 支持（原行为，一字未改）；
	///   ② 否则看 GCU 实时裁定 <see cref="IgpuOnlyStatusSupport"/> == true —— 只认 50 系：
	///      30 系 IGPU_ONLY_* 全载荷 0 命中、40 系是冷切换（iGPUModeOnlySupport），
	///      这两代一律不因实时位放行（2026-10-03「40 系三模不再显示核显省电」的决定保持不动）。
	/// 本机（Gen40，实时位=false）与 30 系（实时位=null）行为逐位不变。
	/// </summary>
	public bool SupportsGpuHotSwap => Capabilities.GpuHotSwap || (DgpuGeneration == DgpuGenerationKind.Gen50 && IgpuOnlyStatusSupport == true);

	private bool ThreeModeCapability => SupportsIgpuOnly;

	public DgpuGenerationKind DgpuGeneration => GpuGenerationProvider.Current().Generation;

	/// <summary>
	/// 2026-10-09（igpugate）：auto 口径下「核显模式」的**硬件权威结论**（三态）：
	///   * <c>true</c>  —— 能力位明确说支持。**本批起 40 系的 <c>iGPUModeOnlySupport</c> 也算数**
	///     （原来整个 40 系被排除在"可靠能力位"之外，用户已批准改为信任硬件能力位）；
	///   * <c>false</c> —— 能力位明确说不支持（ItemSupport 读得到、但这 4 个键一个都没有/为 0）：
	///     自动判定为隐藏（"显式 false 压过用户设置"），但用户仍可在设置里选「强制显示」开回来；
	///   * <c>null</c>  —— **读不到**（注册表里根本没有 ItemSupport 内容 ⇒ ProfileAvailable=false、
	///     老 GCU / 未连接）：auto 下退回旧行为，**不得**因为"读不到"把能用的机器藏掉。
	/// 只读能力位（注册表快照），不发任何硬件指令；不使用 IgpuOnlyStatusSupport（已作废的判据）。
	/// </summary>
	public bool? IgpuOnlyHardwareVerdict
	{
		get
		{
			if (!Capabilities.ProfileAvailable)
			{
				return null;
			}
			return SupportsIgpuOnly;
		}
	}

	public bool CanOfferIgpuOnly
	{
		get
		{
			// 2026-10-09（igpugate）判据链 —— 用户原话：「显式 false 压过用户设置可用做
			// 但是希望后续还是可以手动开如果判断出错的话」。逐层语义：
			//   L1 Gen30 硬屏蔽（一字未动：30 系没有支持核显模式的机器，用户开关不得越过）
			//   L2 三态手动覆盖 igpu_only_ui（hide = 强制隐藏 / show = ⭐强制显示 = 后路）
			//   L2b 2026-10-15：50 系 + AMD CPU **默认隐藏**（代际平台级硬件事实，仍排在能力位之前；
			//      ⚠️ 排在 L2【之后】⇒ 用户手工「强制显示」可以越过它 = 留后路）
			//   L3 auto：硬件权威 IgpuOnlyHardwareVerdict —— true ⇒ 显示（含 40 系）；
			//      false ⇒ **直接隐藏**（不再落到 L5 的 ask 兜底，否则"支持切换 && 支持独显直连"
			//      会把能力位明确说不支持的机器又放行 —— 极光Pro 那类"该隐藏却仍显示"）
			//   L4 auto 且读不到硬件结论（null）：D-1「试过就记住」
			//   L5 仍无结论：ask 代理规则（支持切换 && 支持独显直连）—— 与改动前逐字一致
			if (DgpuGeneration == DgpuGenerationKind.Gen30)
			{
				return false;
			}
			// （「50 系 + AMD ⇒ 默认隐藏」这条已下移到 **L2 之后** —— 见下方 L2b：默认隐藏保留，
			//   但用户手工「强制显示」能越过它，即 2026-10-15 用户拍板的「留后路」。）
			// L2：用户的三态选择（读值为 auto/show/hide，老装机的 show 由 IgpuOnlyUiMode 迁移为 auto）。
			string igpuOnlyUi = IgpuOnlyUiMode.Current();
			if (string.Equals(igpuOnlyUi, IgpuOnlyUiMode.Hide, StringComparison.OrdinalIgnoreCase))
			{
				// 用户明确回答「没有」或在设置里选了「强制隐藏」—— 明确表态，自动结论不得把它翻回来。
				return false;
			}
			if (string.Equals(igpuOnlyUi, IgpuOnlyUiMode.Show, StringComparison.OrdinalIgnoreCase))
			{
				// ⭐ 后路：强制显示。即使 auto 判定为隐藏，用户也能在设置里把核显模式开回来。
				// 唯一底线：本机确实存在 NVIDIA 独显（NoDgpu = 枚举成功但没有 NV 适配器），
				// 避免在无独显机型上凭一个回答造出显卡面板/托盘项。
				return DgpuGeneration != DgpuGenerationKind.NoDgpu;
			}
			// L2b 2026-10-15（igpu-gen50-amd + 用户拍板「留后路」）：**50 系 + AMD CPU ⇒ 默认隐藏**
			// （只针对"纯核显模式 / 核显模式"这一个档位）。
			// 理由（用户原话）：「对于 50 系的 AMD CPU 机型默认隐藏纯核显模式(它们可能硬件上不支持硬切换核显)
			// 不要误伤核显省电功能」「如果按当前为 50 系 AMD 显示 可能出问题」⇒ 宁可**默认**隐藏。
			//
			// ⚠️⚠️ 位置（2026-10-15 用户拍板：「修 修了重新打」+ 上一轮问的「要不要留后路」⇒ 要留）：
			//   **排在 L2 用户三态【之后】** ⇒ 用户若在设置里选「强制显示」（igpu_only_ui=show），
			//   L2 上面那一支已经 `return`，本条**根本不会被执行** ⇒ **用户能越过它**（这就是"后路"）。
			//   而 auto 下本条生效 ⇒ **默认隐藏**。完整链：
			//     L1(Gen30 硬屏蔽) → L2(用户三态；hide/show 一票定音) → **L2b(Gen50+AMD 默认隐藏)**
			//     → L3/L4/L5(auto 的其余判据)。
			//   ⚠️ 不变的一点：本条**仍排在能力位(L3)之前** ⇒ 代际(50 系)+平台(AMD)是**硬件事实**，
			//   优先于 GCU 上报的 IgpuOnly/IGpuOnlyConnectionSwitch_Support（那是**可被改/可能错**的软件结论）——
			//   用户原话「代际平台级优先能力位 要不然诊断因为能力被改会不准确」。
			//
			// ⚠️ 判据是 `CpuVendor.IsAmd`（== Amd），**不是 `!IsIntel()`** ——
			// CPU 厂商读不到 / 认不出时是 Unknown ⇒ 本条**不成立**，⛔ 绝不误伤 Intel 与未知机型。
			// ⚠️ 本机（翼龙15Pro：AMD 8745H + RTX 4060）是 Gen40 ⇒ 本条不成立，行为与改动前逐位一致。
			// ⚠️ 只影响"纯核显模式"这一个档位；**「核显省电」那一行完全不受影响**（两者本就独立）。
			if (DgpuGeneration == DgpuGenerationKind.Gen50 && CpuVendor.IsAmd)
			{
				Logger.WriteLineIfChanged("igpu-only-gen50-amd", "IgpuOnly hidden by default: Gen50 + AMD CPU (hardware may not support hard iGPU switch); user can force-show via igpu_only_ui=show");
				return false;
			}
			// L3：auto —— 硬件能力位一票定音。
			bool? hardwareVerdict = IgpuOnlyHardwareVerdict;
			if (hardwareVerdict == true)
			{
				// 2026-10-05：核显【模式】对应的是 DGPU_DIRECT_CONNECT_TOGGLE_IGPU；
				// IGPU_ONLY_CONNECT_RB_* 属【核显省电】（另一功能，见 §9.6 / SettingsForm._igpuSavingRow）。
				// 本分支对 50 系与改动前**逐位一致**（50 系能力位在 SupportsIgpuOnly 的 OR 里，
				// 且该动作在 50 系动作表内 ⇒ 恒为 true），只是 40 系现在也走这里。
				return IsGpuActionAllowedByGeneration("DGPU_DIRECT_CONNECT_TOGGLE_IGPU");
			}
			if (hardwareVerdict == false)
			{
				// 能力位明确说不支持 ⇒ 隐藏（不再走 ask 兜底）。
				return false;
			}
			// L4：auto 且**读不到**硬件结论（null）—— 2026-10-06 D-1「试过就记住」原样保留：
			// 只有用户**还没表态**（答「不确定」/跳过/X、Esc，且设置里也没选过，见 IgpuOnlyAttempt）
			// 时才轮到这条自动结论 —— 一旦他真点过一次核显模式，就以那次重启后的**实际显示路由**为准：
			//   切成功 -> 一直显示；没切成功 -> 不再显示（他仍可在设置里选「强制显示」，那是明确表态）。
			// 这条必须排在下面的 ask 代理规则**之前**，否则"试过没用"会被代理规则盖住。
			// 不变式：MarkAttempt() 只在 IgpuOnlyUiIsGuess=true（即硬件结论未知 + AnswerIsUncertain）时记录，
			// 而选三态/引导作答都会把 igpu_only_answer 写成 yes/no——所以走到这里时二者不会冲突。
			if (IgpuOnlyAttempt.AnswerIsUncertain && IgpuOnlyAttempt.HasVerdict)
			{
				return IgpuOnlyAttempt.TrySucceeded && DgpuGeneration != DgpuGenerationKind.NoDgpu;
			}
			// L5：ask 代理规则 —— **只在硬件结论未知（null）时**才可能走到这里（行为与改动前逐字一致）：
			// 先显示，等用户表态；表态后由上面的 L2 三态分支接管。
			// 2026-10-05（恢复更早的逻辑）：部分 40 系三模 GCU 变体不上报 IgpuOnly 支持字段
			// （实测 GCUService 1.0.2.70：IGpuOnlyConnectionSwitch_Support=NotSupport、
			// iGPUModeOnlySupport 缺失），但确实支持三模 —— 发 IGPU_ONLY_CONNECT_RB_ON 后
			// NVRAM @0x62=2 且 NVIDIA 被屏蔽。
			// 因此按「支持切换 && 支持独显直连」判定（与 0.289.1.0 的修法一致）。
			// ⚠️ 本批要求：硬件**显式 false** 时绝不落到这里（见 L3 的 hardwareVerdict == false 分支）。
			return CanOfferGpuModeSwitch && SupportsDgpuDirect;
		}
	}

	public bool CanOfferGpuModeSwitch
	{
		get
		{
			bool flag = CanOfferGpuModeSwitchCore(DgpuDirectStatusSupport, SupportsDgpuDirect, SupportsIgpuOnly, DgpuGeneration, ThreeModeCapability);
			if (!flag && DgpuDirectStatusSupport == false)
			{
				// 2026-10-06（gcu 门）要求：判定变化时留一行可读日志，便于日后排查。
				// WriteLineIfChanged 保证同一取值只写一次，不会刷屏。
				Logger.WriteLineIfChanged("gpu-row-gcu-gate", $"GPU row hidden: GCU reports DGpuDirect unsupported ({GcuDgpuDirectSupportRaw ?? "false"}) generation={DgpuGeneration}");
			}
			return flag;
		}
	}

	/// <summary>
	/// 2026-10-06（gcu 门）：GCU **明确**上报"不支持独显直连"⇒「显卡模式」整行隐藏
	/// （设置窗与托盘必须一致：设置窗的整行可见性由 RefreshDeviceCapabilities 的
	/// flag18/flag19（→ panelGPU.Visible）与 GPUModeControl 的 flag/flag2 决定，
	/// 托盘由 SetContextMenu 的 num4/num5/flag3 决定；两处都读本属性）。
	/// 只在 GCU 给出**明确的否定值**（NotSupport / UnSupport / 0 / false …，见 ParseFlexibleBool）时为 true；
	/// 未连接 / 老 GCU / 字段缺失 / 认不出的取值一律为 false（未表态 ⇒ 保持现状、退回静态能力位）。
	/// 注意：本属性只管"整行"，不参与 CanOfferIgpuOnly 的判据（核显模式有自己的一套优先级）。
	/// </summary>
	public bool GpuRowHiddenByGcuReports => DgpuDirectStatusSupport == false;

	/// <summary>
	/// 2026-10-06（gcu 门）：<see cref="CanOfferGpuModeSwitch"/> 的**纯函数核心**（便于用合成输入自检/回归）。
	/// <paramref name="gcuDgpuDirectSupport"/> 是 GCU 实时上报的
	/// <c>DiscreteGpuDirectConnectionSwitch_Support</c>（见 <see cref="DgpuDirectStatusSupport"/>）：
	/// <list type="bullet">
	/// <item><c>false</c> = GCU **明确**表态"不支持独显直连"（原文 NotSupport / UnSupport / 0 / false …，
	/// 见 <see cref="ParseFlexibleBool"/>）⇒ 显卡模式整行隐藏；</item>
	/// <item><c>null</c> = GCU **没有表态**（未连接 / 老 GCU / 字段缺失 / 认不出的取值）⇒ 保持现状，
	/// 退回静态能力位判断 —— **不得**因为"没连上"就隐藏；</item>
	/// <item><c>true</c> = GCU 明确支持 ⇒ 与既有逻辑完全一致。</item>
	/// </list>
	/// 其余判据与本次改动前逐字一致：静态能力位 → Gen30 要求直连能力位 → 该代际是否有可发的直连动作。
	/// </summary>
	internal static bool CanOfferGpuModeSwitchCore(bool? gcuDgpuDirectSupport, bool supportsDgpuDirect, bool supportsIgpuOnly, DgpuGenerationKind generation, bool threeModeCapability)
	{
		// 2026-10-06（gcu 门）：GCU 的实时上报才是设备自己的权威表述；只有**明确的否定值**才当"不支持"，
		// 拿不准的取值（null）一律视为"未表态"，走下面的既有判据（静态能力位 / 代际动作矩阵）。
		if (gcuDgpuDirectSupport == false)
		{
			return false;
		}
		// 2026-10-06（用户澄清）：「30 系不支持的只是核显模式这一个模式，混合模式与独显直连照常显示」。
		// 因此这里不再对 Gen30 整代否决：只要本机确实能上报「独显直连」（SupportsDgpuDirect），
		// 就有已验证的直连动作可做 —— 30 系反编译证实的动作词汇只有
		// DGPU_DIRECT_CONNECT_TOGGLE_ON / _OFF（两档 Mux），所以 Gen30 额外要求 SupportsDgpuDirect，
		// 不允许仅凭 SupportsIgpuOnly 放开整行（IGPU_ONLY_* 在 30 系是 ProvenAbsent）。
		// 「核显模式」这一个档位的屏蔽仍由 CanOfferIgpuOnly 里的 Gen30 恒隐藏独立负责，本属性不再代管它。
		if (!supportsDgpuDirect && !supportsIgpuOnly)
		{
			return false;
		}
		if (generation == DgpuGenerationKind.Gen30 && !supportsDgpuDirect)
		{
			return false;
		}
		if (!DisplayRoutePolicy.AllowsAction(generation, "DGPU_DIRECT_CONNECT_TOGGLE_ON", threeModeCapability) && !DisplayRoutePolicy.AllowsAction(generation, "DGPU_DIRECT_CONNECT_TOGGLE_OFF", threeModeCapability))
		{
			return DisplayRoutePolicy.AllowsAction(generation, "DGPU_DIRECT_CONNECT_RESTART", threeModeCapability);
		}
		return true;
	}

	/// <summary>
	/// 行形态走既有机制：nvPreference=false；modeSwitch=<see cref="CanOfferGpuModeSwitch"/>；
	/// igpuMuxTarget=<see cref="CanOfferIgpuOnly"/>（核显模式档位是否存在）。
	/// Gen30 + 直连 => modeSwitch=true 且 igpuMuxTarget=false（Gen30 恒隐藏核显模式）=> GpuRowLayout.Mux2，
	/// 即「混合模式 / 独显直连」两档，核显模式不出现。
	/// </summary>
	public GpuRowLayout GpuRowLayout => GpuRowLayouts.Resolve(false, CanOfferGpuModeSwitch, CanOfferIgpuOnly);

	public bool SupportsKeyboard
	{
		get
		{
			if (!Capabilities.Keyboard)
			{
				return KeyboardStatusSeen;
			}
			return true;
		}
	}

	/// <summary>
	/// 2026-10-09：补 Status 回退（与 <see cref="SupportsKeyboard"/> / <see cref="SupportsLogoLight"/> 对齐）。
	/// 改前只看画像/能力位：耀世这类画像未标 Lightbar/RgbLightbar、但 GCU 明确上报了
	/// HidLightbar/Status 的机型会把灯带整行判为不支持 ⇒ 行直接隐藏（回退过严）。
	/// 只影响"行是否可见"，不发任何指令、不改任何硬件状态。
	/// </summary>
	public bool SupportsLightbar
	{
		get
		{
			return Capabilities.Lightbar || Capabilities.RgbLightbar || LightbarStatusSeen;
		}
	}

	public bool SupportsLogoLight
{
    get
    {
        return Capabilities.LogoLight || LogoLightStatusSeen;
    }
}

/// <summary>铰链灯：灯条为 Lighbar4 代际 + BIOS 属 IDA/IDB/IDX/IDZ + GCU 上报 HingeSupport。</summary>
public bool SupportsHingeLight => IsLighbar4Lightbar && IsZoneProbedBios && LightbarHingeSupport == true;

/// <summary>同步灯带：灯条为 Lighbar4 代际 + BIOS 为 IDZ + GCU 上报 SyncStatusType。</summary>
public bool SupportsSyncLight => IsLighbar4Lightbar && string.Equals(BiosProjectId, "IDZ", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(SyncStatusType);

private bool IsLighbar4Lightbar => !string.IsNullOrWhiteSpace(LightbarStatusType) && LightbarStatusType.Trim().EndsWith("MEZone_Lighbar4", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// 2026-10-09：把 IDY 也认作「已探针的 BIOS 画像」。改前只认 IDA/IDB/IDX/IDZ，
	/// 耀世画像 IDY 恒不匹配 ⇒ 铰链灯 / 同步灯带恒 hidden。只影响行可见性。
	/// </summary>
	private bool IsZoneProbedBios
	{
		get
		{
			string? b = BiosProjectId?.Trim();
			return string.Equals(b, "IDA", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(b, "IDB", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(b, "IDX", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(b, "IDY", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(b, "IDZ", StringComparison.OrdinalIgnoreCase);
		}
	}

	public bool SupportsLiquidCooling
	{
		get
		{
			if (!Capabilities.LiquidCooling)
			{
				return LcStatusSeen;
			}
			return true;
		}
	}

	public bool SupportsDisplayRefresh
	{
		get
		{
			if (!Capabilities.DisplayRefresh)
			{
				if (GpuDeviceStatusSeen)
				{
					return HzList.Count > 0;
				}
				return false;
			}
			return true;
		}
	}

	public bool SupportsColorCalibration
	{
		get
		{
			if (!Capabilities.ColorCalibration)
			{
				return ColorCalibrationSeen;
			}
			return true;
		}
	}

	/// <summary>
	/// 2026-10-13（miniledgcu）：MiniLED（单分区/多分区）可见性口径改为
	/// 「GCU 实时上报 &gt; 注册表静态回退 &gt; 不显示」。改前 32/33 的写法是
	/// 「<c>if (LocalDimmingSupport != false)</c> ⇒ 只有 GCU 明确报 false 才让注册表回退说了算」，
	/// 于是：GCU 已实时上报 <c>LocalDimmingSupport=true</c>、而启动时读到的注册表快照
	/// （<c>Capabilities.LocalDimming</c>，ItemSupport\LocalDimmingSupport）为 0 的机器 ——
	/// 例如 LM 先启动、GCU 50 变体后写该键 —— 会被注册表永久压住（旧快照不回读 ⇒ 不自愈）。
	/// 现在：实时上报为真优先放行；GCU 明确报 false 时仍只按注册表静态位判（不动 Gen50 之外的既有决定）；
	/// 两者都没证据才是 false（与 tri-state 字段的既有语义一致）。
	/// 兜底安全：<c>SupportsLocalDimming == false</c> 时 <c>SwitchLocalDimming</c> 的闸门依旧拒绝下发，
	/// 故「放行显示」不会产生"看得见却点了没用"的既成事实。
	/// ⚠️ 只改"是否显示/是否可点"这一条判据；<c>LocalDimming</c> 取值、下发命令、EC/NVRAM 一律未动。
	/// </summary>
	public bool SupportsLocalDimming
	{
		get
		{
			bool registryFallback = Capabilities.LocalDimming;
			bool resolved;
			string source;
			if (LocalDimmingSupport == true)
			{
				resolved = true;
				source = "gcu";
			}
			else if (registryFallback)
			{
				resolved = true;
				source = "registry";
			}
			else
			{
				resolved = false;
				source = "none";
			}
			LocalDimmingEvidence = $"MiniledOffered={resolved} source={source} flag22={resolved} gcuReported={LocalDimmingSupport.HasValue} gcu={TriState(LocalDimmingSupport)} registry={registryFallback} seen={LocalDimmingSeen}";
			return resolved;
		}
	}

	/// <summary>
	/// 2026-10-13（miniledgcu）：<see cref="SupportsLocalDimming" /> 最近一次解析的取证串
	/// （<c>MiniledOffered= source= flag22= gcuReported= gcu= registry= seen=</c>）。
	/// 只为诊断包自证存在；唯一读点是 <c>SettingsForm.RefreshDeviceCapabilities</c> 的
	/// <c>Logger.WriteLineIfChanged("miniled-offer", …)</c>，不参与任何判据。
	/// </summary>
	public string LocalDimmingEvidence { get; private set; } = "";

	private static string TriState(bool? value)
	{
		if (value == true)
		{
			return "true";
		}
		if (value == false)
		{
			return "false";
		}
		return "unset";
	}

	public bool SupportsLcdOverdrive
	{
		get
		{
			if (LcdOverdriveSupport != false)
			{
				if (!Capabilities.LcdOverdrive)
				{
					return LcdOverdriveSeen;
				}
				return true;
			}
			return false;
		}
	}

	public bool SupportsFanBoost
	{
		get
		{
			if (!Capabilities.FanBoost)
			{
				return FanBoostSeen;
			}
			return true;
		}
	}

	public bool SupportsFanRespective => FanRespectiveSeen;

	public bool Pl1Adjustable => IsAdjustable(Pl1Minimum, Pl1Maximum);

	public bool Pl2Adjustable => IsAdjustable(Pl2Minimum, Pl2Maximum);

	public bool Pl4Adjustable => IsAdjustable(Pl4Minimum, Pl4Maximum);

	public bool SupportsFanSwitchSpeed => FanSwitchSpeedSeen;

	public int FanSwitchSpeedMinimum { get; private set; } = 100;

	public int FanSwitchSpeedMaximum { get; private set; } = 25500;

	public bool TccAdjustable
	{
		get
		{
			if (TccStatusSeen)
			{
				return IsAdjustable(TccMinimum, TccMaximum);
			}
			return false;
		}
	}

	public bool GpuTgpAdjustable => IsAdjustable(GpuTgpMinimum, GpuTgpMaximum);

	public bool GpuDynamicBoostAdjustable => IsAdjustable(GpuDbMinimum, GpuDbMaximum);

	public bool DirectGpuOverclockAvailable
	{
		get
		{
			lock (_gpuOverclockLock)
			{
				return _gpuOverclock?.IsAvailable ?? false;
			}
		}
	}

	internal bool DirectGpuOverclockBackendPresent
	{
		get
		{
			lock (_gpuOverclockLock)
			{
				return _gpuOverclock != null;
			}
		}
	}

	public bool DirectGpuCoreRangeAvailable
	{
		get
		{
			GpuClockOffsetRange range;
			return TryGetDirectOffsetRange(core: true, out range);
		}
	}

	public bool DirectGpuMemoryRangeAvailable
	{
		get
		{
			GpuClockOffsetRange range;
			return TryGetDirectOffsetRange(core: false, out range);
		}
	}

	public bool GcuGpuOverclockAvailable
	{
		get
		{
			if (IsConnected)
			{
				bool? lchwocSupportReported = LchwocSupportReported;
				if (!lchwocSupportReported.HasValue)
				{
					if (!Capabilities.OverclockSettings && !IsOffsetAdjustable(GpuCoreOffsetMinimum, GpuCoreOffsetMaximum))
					{
						return IsOffsetAdjustable(GpuMemoryOffsetMinimum, GpuMemoryOffsetMaximum);
					}
					return true;
				}
				return lchwocSupportReported == true;
			}
			return false;
		}
	}

	public int GpuCoreOffsetUserMinimum => Math.Max(GetMergedGpuOffsetRange(core: true).Minimum, -500);

	public int GpuCoreOffsetUserMaximum => Math.Min(GetMergedGpuOffsetRange(core: true).Maximum, 500);

	public int GpuMemoryOffsetUserMinimum => GetMergedGpuOffsetRange(core: false).Minimum;

	public int GpuMemoryOffsetUserMaximum => GetMergedGpuOffsetRange(core: false).Maximum;

	public int EffectiveGpuCoreClockOffset
	{
		get
		{
			bool valueOrDefault;
			lock (_gpuOverclockLock)
			{
				valueOrDefault = _directGpuOverclockEnabled == true;
			}
			if (valueOrDefault)
			{
				return GetEffectiveDirectGpuCoreOffset();
			}
			if (TryGetDirectGpuOverclockReadback(out var core, out var _))
			{
				return core;
			}
			return GpuCoreClockOffset;
		}
	}

	public int EffectiveGpuMemoryClockOffset
	{
		get
		{
			bool valueOrDefault;
			lock (_gpuOverclockLock)
			{
				valueOrDefault = _directGpuOverclockEnabled == true;
			}
			if (valueOrDefault)
			{
				return GetEffectiveDirectGpuMemoryOffset();
			}
			if (TryGetDirectGpuOverclockReadback(out var _, out var memory))
			{
				return memory;
			}
			return GpuMemClockOffset;
		}
	}

	public bool GpuOverclockEnabled
	{
		get
		{
			bool? directGpuOverclockEnabled;
			lock (_gpuOverclockLock)
			{
				directGpuOverclockEnabled = _directGpuOverclockEnabled;
			}
			return directGpuOverclockEnabled ?? OcSwitch;
		}
	}

	public bool GpuCoreOffsetAdjustable => IsOffsetAdjustable(GpuCoreOffsetUserMinimum, GpuCoreOffsetUserMaximum);

	public bool GpuMemoryOffsetAdjustable => IsOffsetAdjustable(GpuMemoryOffsetUserMinimum, GpuMemoryOffsetUserMaximum);

	public bool SupportsGpuOverclock
	{
		get
		{
			if (!DirectGpuCoreRangeAvailable && !DirectGpuMemoryRangeAvailable)
			{
				bool? lchwocSupportReported = LchwocSupportReported;
				bool num;
				if (!lchwocSupportReported.HasValue)
				{
					if (Capabilities.OverclockSettings || GpuCoreOffsetAdjustable)
					{
						goto IL_0049;
					}
					num = GpuMemoryOffsetAdjustable;
				}
				else
				{
					num = lchwocSupportReported == true;
				}
				if (!num)
				{
					return false;
				}
				goto IL_0049;
			}
			return true;
			IL_0049:
			if (!GpuCoreOffsetAdjustable)
			{
				return GpuMemoryOffsetAdjustable;
			}
			return true;
		}
	}

	public bool GpuOverclockRequiresElevation
	{
		get
		{
			lock (_gpuOverclockLock)
			{
				if (_gpuOverclock == null)
				{
					return false;
				}
				return _gpuOverclock.WritesRequireElevation && (_gpuOverclock.CoreOffset.IsAdjustable || _gpuOverclock.MemoryOffset.IsAdjustable);
			}
		}
	}

	public bool GpuOverclockWritable
	{
		get
		{
			if (SupportsGpuOverclock)
			{
				return !GpuOverclockRequiresElevation;
			}
			return false;
		}
	}

	public bool HasAnyCustomRange
	{
		get
		{
			if (!Pl1Adjustable && !Pl2Adjustable && !TccAdjustable && !GpuTgpAdjustable && !GpuDynamicBoostAdjustable && !GpuCoreOffsetAdjustable)
			{
				return GpuMemoryOffsetAdjustable;
			}
			return true;
		}
	}

	public int ColorCalibrationMode { get; private set; }

	public long FanStatusVersion => Interlocked.Read(in _fanStatusVersion);

	internal long CustomProfileStatusVersion => Interlocked.Read(in _customProfileStatusVersion);

	public long FanTableVersion => Interlocked.Read(in _fanTableVersion);

	public bool SupportsOverclockMenu
	{
		get
		{
			bool? overclockMenuSupport = OverclockMenuSupport;
			if (!overclockMenuSupport.HasValue)
			{
				if (!Capabilities.CpuPerformanceTuning)
				{
					return Capabilities.OverclockSettings;
				}
				return true;
			}
			return overclockMenuSupport == true;
		}
	}

	public bool SupportsWhisperMode => WhisperModeSupport == true;

	public bool SupportsPowerLightBrightness
	{
		get
		{
			if (PowerLightSeen)
			{
				return PowerLightBrightness >= 0;
			}
			return false;
		}
	}

	public bool LcConnected { get; private set; }

	public bool LcReportedConnected { get; private set; }

	public bool LcConnectionStateReported { get; private set; }

	public bool LcConnectStringReported { get; private set; }

	public bool LcGcuControllable
	{
		get
		{
			if (LcConnected)
			{
				if (LcActionSupportReported)
				{
					return LcActionSupported;
				}
				return true;
			}
			return false;
		}
	}

	public string LcConnectString { get; private set; } = "";

	public bool LcAutoConnect { get; private set; }

	public bool LcActionSupported { get; private set; }

	public bool LcActionSupportReported { get; private set; }

	public bool LcCoolingAutoSupported { get; private set; }

	public bool LcMeterNormal { get; private set; } = true;

	public bool LcMeterFaultConfirmed
	{
		get
		{
			if (LcGcuControllable && LcPumpDuty > 0)
			{
				return Volatile.Read(in _lcConsecutiveMeterFaults) >= 2;
			}
			return false;
		}
	}

	public int LcPumpDuty { get; private set; } = -1;

	public int LcFanDuty { get; private set; } = -1;

	public int LcPumpControl { get; private set; } = -1;

	public int LcFanControl { get; private set; } = -1;

	public string LcFwVersion { get; private set; } = "";

	public int LcLedRed { get; private set; } = -1;

	public int LcLedGreen { get; private set; } = -1;

	public int LcLedBlue { get; private set; } = -1;

	public int LcLedRedMinimum { get; private set; } = -1;

	public int LcLedRedMaximum { get; private set; } = -1;

	public int LcLedGreenMinimum { get; private set; } = -1;

	public int LcLedGreenMaximum { get; private set; } = -1;

	public int LcLedBlueMinimum { get; private set; } = -1;

	public int LcLedBlueMaximum { get; private set; } = -1;

	public int LcHeadLightMode { get; private set; } = -1;

	public int LcFanLightMode { get; private set; } = -1;

	public bool LcLightingStatusSeen
	{
		get
		{
			if (LcHeadLightMode < 0 && LcFanLightMode < 0)
			{
				if (LcLedRed >= 0 && LcLedGreen >= 0)
				{
					return LcLedBlue >= 0;
				}
				return false;
			}
			return true;
		}
	}

	public bool LcFanLightingSupported
	{
		get
		{
			bool flag = LcGcuControllable;
			if (flag)
			{
				int lcFanLightMode = LcFanLightMode;
				bool flag2 = (uint)(lcFanLightMode - 4) <= 1u;
				flag = flag2 || (!string.IsNullOrWhiteSpace(LcFwVersion) && !LcFwVersion.StartsWith("CoolingSystem LCT21001", StringComparison.OrdinalIgnoreCase));
			}
			return flag;
		}
	}

	public long LcStatusVersion => Interlocked.Read(in _lcStatusVersion);

	public IReadOnlyList<string> LcDeviceMacs => _lcDeviceMacs;

	public string LcCurrentMac { get; private set; } = "";

	public string KeyboardEffect { get; private set; } = "";

	public int KeyboardLight { get; private set; } = -1;

	public int KeyboardBrightness { get; private set; } = -1;

	public int KeyboardSpeed { get; private set; } = -1;

	public string KeyboardDirection { get; private set; } = "";

	public bool KeyboardPower { get; private set; } = true;

	/// <summary>GCU 上报的键盘灯驱动方案：EC=由 EC 驱动（走 Keyboard/Ctrl 灯效字段）；ITE=走 ITE/HID。</summary>
	public string KeyboardSolution { get; private set; } = "";

	public long KeyboardStatusVersion => Interlocked.Read(in _keyboardStatusVersion);

	public int ScreenBrightness { get; private set; } = -1;

	public IReadOnlyList<int> HzList => _hzList;

	public int CurrentHz { get; private set; }

	public bool DcHz { get; private set; }

	public bool DcHzSeen { get; private set; }

	public int GpuSaveMode { get; private set; }

	public bool LocalDimming { get; private set; }

	public bool LocalDimmingSeen { get; private set; }

	public bool? LocalDimmingSupport { get; private set; }

	public bool LcdOverdrive { get; private set; }

	public bool LcdOverdriveSeen { get; private set; }

	public bool? LcdOverdriveSupport { get; private set; }

	public bool ColorCalibrationSwitch { get; private set; }

	public int ColorCalibrationResult { get; private set; } = -1;

	public long SettingStatusVersion => Interlocked.Read(in _settingStatusVersion);

	public long ColorCalibrationStatusVersion => Interlocked.Read(in _colorCalibrationStatusVersion);

	public long GpuModeStatusVersion => Interlocked.Read(in _gpuModeStatusVersion);

	public int OperatingMode { get; private set; } = -1;

	public int Pl1 { get; private set; }

	public int Pl2 { get; private set; }

	public int CpuAmdSpl { get; private set; } = -1;

	public int CpuAmdSppt { get; private set; } = -1;

	public int CpuAmdFppt { get; private set; } = -1;

	public bool AmdPowerStatusSeen { get; private set; }

	public bool? AmdPlatformReported { get; private set; }

	public bool UsesAmdPowerFields
	{
		get
		{
			bool? amdPlatformReported = AmdPlatformReported;
			if (!amdPlatformReported.HasValue)
			{
				if (!Capabilities.AmdPlatform)
				{
					return AmdPowerStatusSeen;
				}
				return true;
			}
			return amdPlatformReported == true;
		}
	}

	internal bool IsAmdPlatformForLimits => AmdPlatformReported ?? Capabilities.AmdPlatform;

	public int TccOffset { get; private set; } = -1;

	public int TccTarget { get; private set; } = -1;

	public int Pl1Minimum { get; private set; } = -1;

	public int Pl1Maximum { get; private set; } = -1;

	public int Pl2Minimum { get; private set; } = -1;

	public int Pl2Maximum { get; private set; } = -1;

	public int Pl4 { get; private set; } = -1;

	public int Pl4Minimum { get; private set; } = -1;

	public int Pl4Maximum { get; private set; } = -1;

	public int TccMinimum { get; private set; } = -1;

	public int TccMaximum { get; private set; } = -1;

	public int TccRawMinimum { get; private set; } = -1;

	public int TccRawMaximum { get; private set; } = -1;

	public int GpuTgpMinimum { get; private set; } = -1;

	public int GpuTgpMaximum { get; private set; } = -1;

	public int GpuDbMinimum { get; private set; } = -1;

	public int GpuDbMaximum { get; private set; } = -1;

	public int GpuCoreOffsetMinimum { get; private set; } = -1;

	public int GpuCoreOffsetMaximum { get; private set; } = -1;

	public int GpuMemoryOffsetMinimum { get; private set; } = -1;

	public int GpuMemoryOffsetMaximum { get; private set; } = -1;

	public int CustomProfileIndex { get; private set; } = -1;

	public int GpuTgp { get; private set; } = -1;

	public bool GpuDbSwitch { get; private set; }

	public int GpuDb { get; private set; } = -1;

	public bool TccSwitch { get; private set; }

	public int TjMax { get; private set; }

	public bool Pl4Double { get; private set; }

	internal int Pl4Scale
	{
		get
		{
			if (!Pl4Double)
			{
				return 1;
			}
			return 2;
		}
	}

	internal string Pl4WireKey
	{
		get
		{
			if (!UsesAmdPowerFields)
			{
				return "PL4";
			}
			return "CpuAmdFPPT";
		}
	}

	public bool FanSwitchSpeedEnabled { get; private set; }

	public int FanSwitchSpeed { get; private set; } = -1;

	public bool FanSwitchSpeedSeen { get; private set; }

	public int GpuCoreClockOffset { get; private set; }

	public int GpuMemClockOffset { get; private set; }

	public string ProfileName { get; private set; } = "";

	public bool OcSwitch { get; private set; }

	public bool LchwocSupport { get; private set; }

	public bool LchwocEnable { get; private set; }

	public bool FanRespective { get; private set; }

	public int GpuMode { get; private set; } = -1;

	public int GpuEcoFlag => (GpuMode == 0) ? 1 : 0;

	public int BatteryProtection { get; private set; } = -1;

	public event Action? BatteryProtectionChanged;

	public int GpuMuxFlag => (GpuMode != 2) ? 1 : 0;

	public string CurveName { get; private set; } = "";

	public string TableName { get; private set; } = "";

	public byte[] CpuCurveUpT { get; private set; } = new byte[16];

	public byte[] CpuCurveDuty { get; private set; } = new byte[16];

	public byte[] GpuCurveUpT { get; private set; } = new byte[16];

	public byte[] GpuCurveDuty { get; private set; } = new byte[16];

	/// <summary>
	/// ⭐⭐ 2026-10-15（C-1 第 2 步）：**GCU 后端**是否可用 —— 语义与改动前的 <c>IsConnected</c> <b>逐字相同</b>
	/// （「会话是否可用」：订阅也成了才算；带 publishOverride 的测试/审计通路恒为 true）。
	/// 用途：**真正需要 GCU 的调用点**（发 MQTT 前的守卫、遥测"有没有跟 GCU 说过话"、
	/// 依赖 GCU 上报的功能如风扇曲线/液冷/灯效）一律改读本属性。
	/// </summary>
	internal bool IsGcuConnected
	{
		get
		{
			if (_publishOverride == null)
			{
				if (_subscriptionsReady)
				{
					return _client?.IsConnected ?? false;
				}
				return false;
			}
			return true;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-15（C-1 第 2 步）：**当前后端是否可用** —— 语义从"GCU 连上了"扩成
	/// <c>UsesLegacyMifs || IsGcuConnected</c>。
	/// <list type="bullet">
	/// <item>新机型（本机）：<c>UsesLegacyMifs</c> 恒为 false ⇒ 本属性 <b>恒等于</b> <see cref="IsGcuConnected" /> ⇒ 行为逐项不变。</item>
	/// <item>老机型：老接口探到了就算"已连接"，于是"界面可用 / 要不要显示遥测"这类判断自然成立。</item>
	/// </list>
	/// ⚠️ 判据是**后端在不在**，不是"有没有跟 GCU 说过话"：老接口在而 GCU 从未连上时本属性为 true
	/// ⇒ 依赖 GCU 上报的功能仍必须各自用 <see cref="IsGcuConnected" /> / 能力位把关（已逐点分类，见本轮报告 ②）。
	/// </summary>
	public bool IsConnected => UsesBuiltInModes || IsGcuConnected;

	/// <summary>
	/// 2026-10-15（C-1 第 2 步）：本会话是否在用**老机型 WMI 后端**（<c>root\WMI:MICommonInterface</c>）。
	/// 判据是「探测到过该对象」（<c>_legacyMifs != null</c>）。
	/// ⚠️ **刻意不看** <c>LegacyMifsWmi.Available</c>（那是"最近一次读成功没有"）：瞬时读失败不该让整机
	/// 退回 GCU 语义 —— 3 s 定时器会自己把它刷回来。
	/// </summary>
	internal bool UsesLegacyMifs => _legacyMifs != null;

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达适配）：本会话是否在用**宝龙达模具后端**（⭐ 蛟龙16Pro 等）。
	/// 判据是「探测到过该对象」（<c>_baolongda != null</c>）—— ⚠️ 与 <see cref="UsesLegacyMifs" /> 同款约定
	/// （⛔ 不看瞬时可用性 ✅）。
	/// </summary>
	internal bool UsesBaolongda => _baolongda != null;

	/// <summary>
	/// ⭐⭐ 2026-10-16：本会话是否在用**任一种"内置档位"后端**（⭐ 老同方 或 ⭐ 宝龙达 ✅）。
	/// <para>⭐ 存在的理由：⭐ 两者**协议同族、档位映射逐字相同**（⭐ 真机已验证 ✅）⇒
	/// 凡"老接口走这条路"的判断，⭐ 宝龙达**也应该走同一条** ✅。</para>
	/// <para>⚠️ <see cref="UsesLegacyMifs" /> 的**语义保持原样**（⛔ 不扩宽）⇒ 已有引用点行为逐位不变 ✅；
	/// 需要"两者都算"的地方**显式改读本属性**（⭐ 当前只有 <c>MechrevoService.SwitchMode</c> 一处 ✅）。</para>
	/// </summary>
	internal bool UsesBuiltInModes => _legacyMifs != null || _baolongda != null;

	/// <summary>⭐ 宝龙达后端的原始协议对象（⭐ 供温度/风扇/灯效等**后续功能**接入；⭐ 现在只为档位服务 ✅）。</summary>
	internal BaolongdaWmi BaolongdaWmi => _baolongda?.Wmi;

	/// <summary>
	/// ⭐⭐ 2026-10-16：**当前生效的后端路由**（⭐ <see cref="BackendRoute"/> ✅）。
	/// <para>⭐ 设计文档：<c>docs/notes/后端路由抽象-设计-20261016.md</c> ✅。</para>
	/// <para>⚠️ 本属性是**纯查表**、⭐ 无副作用 ✅ · ⭐ ⛔ **不改变任何既有行为** ✅ ——
	/// 它只是把「⭐ 后端身份 ✅」这三条既有判据**汇总成一个可查询的值** ✅。</para>
	/// <para>⭐ 顺序与后端探测顺序一致（⭐ GCU → ⭐ 宝龙达 → ⭐ 老 MIFS ✅）⇒ ⭐ 与既有判定**逐位相同** ✅。</para>
	/// </summary>
	internal BackendRoute ActiveRoute
	{
		get
		{
			if (IsGcuConnected)
			{
				return BackendRoute.Gcu;
			}
			if (UsesBaolongda)
			{
				return BackendRoute.Baolongda;
			}
			if (UsesLegacyMifs)
			{
				return BackendRoute.LegacyMifs;
			}
			return BackendRoute.None;
		}
	}

	/// <summary>
	/// ⭐⭐⭐ 2026-10-16：**三态路由查询** —— ⭐「⭐ 这个动作该走谁 ✅」✅。
	/// <para>⭐ 设计文档 §4.4 / §4.5 ✅。⭐ 目标：⭐ 让「加一条新后端」从**改一遍**变成**改一处** ✅。</para>
	/// <para>⚠️⚠️ **语义必须严格**（⭐ 见 <see cref="FeatureSupport"/> 各成员注释 ✅）：
	/// <list type="bullet">
	/// <item><see cref="FeatureSupport.Supported"/> ⇒ ⭐ 当前后端**已接好**这条下发路径 ✅。</item>
	/// <item><see cref="FeatureSupport.NotSupportedByThisBackend"/> ⇒ ⚠️ 当前后端做不到，⭐ **但别的后端可能能做** ✅
	/// —— ⭐ 调用方**不得**假装成功 ✅，⭐ 要如实告知 ✅。</item>
	/// <item><see cref="FeatureSupport.Unknown"/> ⇒ ⭐ **尚未判定** ✅ —— ⛔ **不代表「不支持」** ⚠️。</item>
	/// </list></para>
	/// <para>⚠️ 它回答的是「⭐ **谁的路径**」✅，⭐ ⛔ **不是「通不通」** ⚠️ ——
	/// ⭐ 所以它**不是** <see cref="IsGcuConnected"/> 的替代品 ✅。</para>
	/// <para>⚠️ **本骨架尚未逐项登记** ⇒ ⭐ 大量项返回 <see cref="FeatureSupport.Unknown"/> ✅ ——
	/// ⭐ 那是**诚实**的（⭐ 我们还不知道 ✅），⭐ ⛔ 不是「不支持」⚠️。</para>
	/// </summary>
	internal FeatureSupport Query(BackendFeature feature)
	{
		BackendRoute route = ActiveRoute;
		switch (route)
		{
			case BackendRoute.Gcu:
				return GcuSupports(feature);
			case BackendRoute.Baolongda:
				return BaolongdaSupports(feature);
			case BackendRoute.LegacyMifs:
				return LegacyMifsSupports(feature);
			default:
				// ⚠️ 后端尚未就绪 ⇒ Unknown（⛔ 不是 NotSupportedByThisBackend）——
				//    这是三态设计的关键：探测未完成 != 永远做不到。
				return FeatureSupport.Unknown;
		}
	}

	/// <summary>
	/// ⭐ GCU（⭐ 历史基线后端 ✅）的支持表。
	/// <para>⚠️ **尚未逐项登记** ⇒ ⭐ 一律 <see cref="FeatureSupport.Unknown"/> ✅ ——
	/// ⭐ 这是**保守且诚实**的默认值 ✅（⭐ 登记工作随第 2 步迁移一起做 ✅）。</para>
	/// <para>⚠️ ⛔ **不要**为了「让 GCU 看起来支持一切」而直接返回 <see cref="FeatureSupport.Supported"/> ⚠️ ——
	/// 那会违反「⭐ 能力位 ＝ 已接好下发路径 ✅」这条铁律 ✅。</para>
	/// </summary>
	private static FeatureSupport GcuSupports(BackendFeature feature)
	{
		_ = feature;
		return FeatureSupport.Unknown;
	}

	/// <summary>
	/// ⭐ 宝龙达后端的支持表。
	/// <para>⚠️ **强制读 <see cref="Capabilities"/>**（⭐ 即 <c>BaolongdaBackend.BuildCapabilities()</c> 的产出 ✅）
	/// —— ⛔ **绝不允许出现第二份互相打架的后端事实** ⚠️（⭐ 设计文档 §5 ✅）。</para>
	/// <para>⭐ 只登记**已经有明确能力位**的项 ✅；⭐ 其余返回 <see cref="FeatureSupport.Unknown"/> ✅
	/// （⭐ 尚未逐项登记，⭐ 诚实 ✅）。</para>
	/// </summary>
	private FeatureSupport BaolongdaSupports(BackendFeature feature)
	{
		MechrevoDeviceCapabilities caps = Capabilities;
		return feature switch
		{
			BackendFeature.PerfModeSwitch => caps.TurboMode ? FeatureSupport.Supported : FeatureSupport.NotSupportedByThisBackend,
			BackendFeature.PowerLimit => caps.CpuPerformanceTuning ? FeatureSupport.Supported : FeatureSupport.NotSupportedByThisBackend,
			BackendFeature.KeyboardLighting => caps.Keyboard ? FeatureSupport.Supported : FeatureSupport.NotSupportedByThisBackend,
			BackendFeature.LogoLight => caps.LogoLight ? FeatureSupport.Supported : FeatureSupport.NotSupportedByThisBackend,
			BackendFeature.Lightbar => caps.Lightbar ? FeatureSupport.Supported : FeatureSupport.NotSupportedByThisBackend,
			BackendFeature.GpuModeSwitch => caps.DgpuDirect ? FeatureSupport.Supported : FeatureSupport.NotSupportedByThisBackend,
			_ => FeatureSupport.Unknown,
		};
	}

	/// <summary>
	/// ⭐ 老机型 WMI 后端（⭐ <c>root\WMI:MICommonInterface</c> ✅）的支持表。
	/// <para>⚠️ **尚未逐项登记** ⇒ ⭐ 一律 <see cref="FeatureSupport.Unknown"/> ✅。
	/// ⚠️ 注意：⭐ 本骨架**刻意不推测**「⭐ 宝龙达能做的老 MIFS 也能做 ✅」⭐ 这类结论 ⚠️ ——
	/// ⭐ 那需要逐个真机验证 ✅（⭐ 设计文档 §3.1：⭐「⭐ 两者协议同族 ✅」⭐ 是**档位映射**层面的结论 ✅，
	/// ⛔ 不能无条件外推到所有功能 ⚠️）。</para>
	/// </summary>
	private static FeatureSupport LegacyMifsSupports(BackendFeature feature)
	{
		_ = feature;
		return FeatureSupport.Unknown;
	}

	/// <summary>
	/// 2026-10-09（批次3 #7）：**传输**层（TCP/MQTT 连接本身）是否活着。
	/// ⭐ 与 <see cref="IsConnected" /> 的区别：`IsConnected` 是「**会话**是否可用」（订阅也成了），
	/// 它把「CONNACK 成功但没拿到 SUBACK」也算成"没连上"（既有语义，180+ 调用点依赖，不动）。
	/// 这里把两层显式分开，重连/重试日志据此区分：
	/// <list type="bullet">
	/// <item><see cref="IsTransportConnected" />=是 + <see cref="IsSessionReady" />=否 ⇒ 「桥/权限/订阅未就绪」：
	/// **慢速重试**（订阅重试循环只重发 SUBSCRIBE，2s→30s），⛔ 不猛重连、不重复 CONNECT。</item>
	/// <item><see cref="IsTransportConnected" />=否 ⇒ 传输真的断了：交给重连循环（250ms 起、×2、30s 封顶）。</item>
	/// </list>
	/// </summary>
	internal bool IsTransportConnected => _client?.IsConnected ?? false;

	/// <summary>2026-10-09（批次3 #7）：**会话**是否就绪（订阅已建立）—— 见 <see cref="IsTransportConnected" /> 的说明。</summary>
	internal bool IsSessionReady => _subscriptionsReady;

	/// <summary>
	/// 2026-10-09（批次3 #7）：是否正处于「传输还在、正在等订阅」这一档（订阅重试循环在跑）。
	/// 用途只有一个：⭐ 让状态显示仍算「连接中」—— 本档下重连循环会**让位并退出**（不再置 <see cref="IsReconnecting" />），
	/// 若不把这一档补进去，机主会从琥珀色「连接中」直接掉成红色「未连接」，与改动前不一致。
	/// ⚠️ 只影响"显示成哪一档"，⛔ 不改任何连接/订阅/退避行为。
	/// </summary>
	internal bool IsSessionAwaitingSubscribe => !_subscriptionsReady && IsTransportConnected && Volatile.Read(in _subscribeRetryLoop) != 0;

	public int GHelperMode => OperatingMode switch
	{
		0 => 2, 
		1 => 0, 
		2 => 1, 
		3 => 4, 
		_ => 0, 
	};

	internal bool TurboFanCurveIsPathological
	{
		get
		{
			if (!IsPathologicalTurboFanCurve(GpuCurveUpT, GpuCurveDuty))
			{
				return IsPathologicalTurboFanCurve(CpuCurveUpT, CpuCurveDuty);
			}
			return true;
		}
	}

	private bool GcuOffsetChannelUsable
	{
		get
		{
			if (LchwocSupportReported != true)
			{
				if (!LchwocSupportReported.HasValue)
				{
					if (!IsOffsetAdjustable(GpuCoreOffsetMinimum, GpuCoreOffsetMaximum))
					{
						return IsOffsetAdjustable(GpuMemoryOffsetMinimum, GpuMemoryOffsetMaximum);
					}
					return true;
				}
				return false;
			}
			return true;
		}
	}

	public event Action<int>? CloseTimerChanged;

	internal event Action? CapabilitiesChanged;

	public event Action? LcChanged;

	public event Action? CustomModeChanged;

	public event Action? DataChanged;

	public event Action<int>? ConnectionReady;

	internal event Action<string>? StateChanged;

	public event Action<int>? ModeChanged;

	public event Action? CurveUpdated;

	public event Action? GpuModeChanged;

	internal event Action<string, string>? RawMessageObserved;

	public void MarkModeSwitchPending(int expectedOperatingMode, int? expectedCustomProfile = null)
	{
		Interlocked.Exchange(ref _pendingModeSwitch, new PendingModeSwitch(expectedOperatingMode, expectedCustomProfile, Environment.TickCount64 + 8000));
	}

	internal void ClearModeSwitchPending()
	{
		Interlocked.Exchange(ref _pendingModeSwitch, null);
	}

	internal bool HasConflictingModeSwitchPending(int mode, int profile)
	{
		PendingModeSwitch pendingModeSwitch = Volatile.Read(in _pendingModeSwitch);
		if ((object)pendingModeSwitch != null && Environment.TickCount64 <= pendingModeSwitch.ExpiresAtTick)
		{
			if (pendingModeSwitch.ExpectedOperatingMode == mode)
			{
				int? expectedCustomProfile = pendingModeSwitch.ExpectedCustomProfile;
				if (expectedCustomProfile.HasValue)
				{
					int valueOrDefault = expectedCustomProfile.GetValueOrDefault();
					return valueOrDefault != profile;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	internal (bool Active, int ExpectedOperatingMode) GetModeSwitchPendingState()
	{
		PendingModeSwitch pendingModeSwitch = Volatile.Read(in _pendingModeSwitch);
		if ((object)pendingModeSwitch == null)
		{
			return (Active: false, ExpectedOperatingMode: -1);
		}
		return (Active: Environment.TickCount64 <= pendingModeSwitch.ExpiresAtTick, ExpectedOperatingMode: pendingModeSwitch.ExpectedOperatingMode);
	}

	public MechrevoHw()
		: this(null, null, null, NvidiaGpuControl.TryCreateOverclockControl, null, null, null)
	{
		// 2026-10-15（C-1 第 2 步，对照 A-22）：只有「真实生产通路」的这个构造函数允许探老接口。
		// 其余 internal 构造（注入 publishOverride 的测试/UI 审计、注入 capabilities 的假硬件）一律不探
		// ⇒ 审计与单测在**任何机器上**结果都不变（老接口后端绝不参与）。
		_allowLegacyProbe = true;
	}

	internal MechrevoHw(string clientId)
		: this(null, null, null, NvidiaGpuControl.TryCreateOverclockControl, null, clientId)
	{
	}

	internal MechrevoHw(Func<string, object, Task>? publishOverride)
		: this(publishOverride, null, null, null, null, null, null)
	{
	}

	internal MechrevoHw(Func<string, object, Task>? publishOverride, MechrevoDeviceCapabilities? capabilities)
		: this(publishOverride, capabilities, null, null, null, null, null)
	{
	}

	internal MechrevoHw(Func<string, object, Task>? publishOverride, MechrevoDeviceCapabilities? capabilities, IGpuOverclockControl? gpuOverclock)
		: this(publishOverride, capabilities, gpuOverclock, null, null, null, null)
	{
	}

	internal MechrevoHw(Func<string, object, Task>? publishOverride, MechrevoDeviceCapabilities? capabilities, IGpuOverclockControl? gpuOverclock, IGpuOverclockElevatedApplier elevatedGpuOverclockApplier)
		: this(publishOverride, capabilities, gpuOverclock, null, elevatedGpuOverclockApplier)
	{
	}

	internal MechrevoHw(Func<string, object, Task>? publishOverride, MechrevoDeviceCapabilities? capabilities, IGpuOverclockControl? gpuOverclock, IGpuOverclockElevatedApplier? elevatedGpuOverclockApplier, Func<bool> isProcessElevated)
		: this(publishOverride, capabilities, gpuOverclock, null, elevatedGpuOverclockApplier, null, isProcessElevated)
	{
	}

	internal MechrevoHw(Func<string, object, Task>? publishOverride, MechrevoDeviceCapabilities? capabilities, Func<IGpuOverclockControl?> gpuOverclockFactory, Func<bool> isProcessElevated)
		: this(publishOverride, capabilities, null, gpuOverclockFactory, null, null, isProcessElevated)
	{
	}

	private MechrevoHw(Func<string, object, Task>? publishOverride, MechrevoDeviceCapabilities? capabilities, IGpuOverclockControl? gpuOverclock, Func<IGpuOverclockControl?>? gpuOverclockFactory, IGpuOverclockElevatedApplier? elevatedGpuOverclockApplier = null, string? clientId = null, Func<bool>? isProcessElevated = null)
	{
		_clientId = clientId ?? PrimaryClientId;
		(_clientUser, _clientPwd) = ResolveClientIdCredentials(_clientId);
		// 2026-10-09：用「默认适配器工厂外面包一层」的 _diagAdapterFactory 建工厂 —— 连接行为不变，
		// 只是把本次连接的 IMqttChannelAdapter（本地/远端 IP:port、收发字节）留着，供断开/超时取证。
		// ⚠️ MQTTnet 5.2 没有单参 (IMqttClientAdapterFactory) 构造函数，必须同时给 logger；
		// 传的就是默认工厂自己的 DefaultLogger（实测 = MqttNetNullLogger，与 new MqttClientFactory() 内部取的是同一个），
		// 所以日志行为与改动前逐字一致（LM 从不订阅 MQTTnet 内部日志）。
		_factory = new MqttClientFactory(new MqttClientFactory().DefaultLogger, _diagAdapterFactory);
		_publishOverride = publishOverride;
		_gpuOverclock = gpuOverclock;
		_gpuOverclockFactory = gpuOverclockFactory;
		_elevatedGpuOverclockApplier = elevatedGpuOverclockApplier;
		_persistDirectGpuOverclock = gpuOverclockFactory != null;
		_isProcessElevated = isProcessElevated ?? ((Func<bool>)(() => IsElevatedProcess));
		// 2026-10-15（C-1 第 2 步）：Capabilities 改成可替换属性后，这里改走属性（同源同值）。
		Capabilities = capabilities ?? MechrevoDeviceCapabilities.Load();
		LoadDefaultCurveFromDisk();
	}

	internal string DescribeResolvedCapabilities()
	{
		return "Resolved caps: " + $"kb={Yn(SupportsKeyboard)} lightbar={Yn(SupportsLightbar)} logo={Yn(SupportsLogoLight)} " + "(reported logo=" + Tri(LightbarLogoSupport) + " " + $"base={Tri(LightbarBaseSupport)} newLogo={Tri(LightbarNewLogoSupport)} mbLogo={Tri(LightbarMbLogoSupport)}; " + $"statusSeen lightbar={Yn(LightbarStatusSeen)} logo={Yn(LogoLightStatusSeen)}) " + $"hinge={Yn(SupportsHingeLight)} sync={Yn(SupportsSyncLight)} bios={BiosProjectId ?? "-"} " + $"| midFan={Yn(MidFanSeen)} fanBoost={Yn(SupportsFanBoost)} fanSwitchSpeed={Yn(SupportsFanSwitchSpeed)} " + $"whisper={Yn(SupportsWhisperMode)} powerLightBrightness={Yn(SupportsPowerLightBrightness)} " + $"pl1={Yn(Pl1Adjustable)} pl2={Yn(Pl2Adjustable)} pl4={Yn(Pl4Adjustable)} " + $"tcc={Yn(TccAdjustable)} tgp={Yn(GpuTgpAdjustable)} lc={Yn(SupportsLiquidCooling)} " + "hz=" + Yn(SupportsDisplayRefresh) + " calib=" + Yn(SupportsColorCalibration);
		static string Tri(bool? value)
		{
			if (value.HasValue)
			{
				if (!value.Value)
				{
					return "n";
				}
				return "Y";
			}
			return "-";
		}
		static string Yn(bool value)
		{
			if (!value)
			{
				return "n";
			}
			return "Y";
		}
	}

	private string SnapshotCapabilityFlags()
	{
		return DescribeResolvedCapabilities() + $"|set={SettingStatusSeen} bright={ScreenBrightnessSeen} gpuDev={GpuDeviceStatusSeen}" + $"|dgpu={SupportsDgpuDirect} igpu={SupportsIgpuOnly}" + $"|dim={SupportsLocalDimming} od={SupportsLcdOverdrive}" + $"|ocMenu={SupportsOverclockMenu} hwoc={LchwocSupportReported}" + $"|wifi={WifiSeen} bt={BluetoothSeen} webcam={WebcamSeen} winkey={WinKeySeen}" + $"|fn={FnKeySeen} numpad={NumpadSeen} deepsleep={DeepSleepSeen} copilot={CopilotSeen}" + $"|ac={AcRecoverySeen} highperf={HighPerformanceSeen} fanboost={FanBoostSeen}" + $"|tpt={TouchpadToggleSeen} sckb={SingleColorKbSeen} uni={UniOmniSeen}" + $"|pl={PowerLightSeen} blogo={BatteryLogoSeen} gw={GameWhitelistSeen}" + $"|cpuadv={CpuAdvancedPerformanceSeen} calibSeen={ColorCalibrationSeen}" + $"|ldSeen={LocalDimmingSeen} odSeen={LcdOverdriveSeen} dchz={DcHzSeen}";
	}

	internal static bool HasLightbarContent(JObject o)
	{
		string[] array = new string[2] { "type", "powerStatus" };
		foreach (string propertyName in array)
		{
			if (!string.IsNullOrWhiteSpace(o.GetValue(propertyName, StringComparison.OrdinalIgnoreCase)?.ToString()))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 2026-10-09（lightfix2，③ 修"发帧逻辑与能力位不一致"）：灯带**这条硬件真的在**的证据。
	/// 与 Logo 行同一口径（<see cref="HasLogoLightEvidence"/>，2026-10-06 就是为同一个坑改的）：
	/// 只认"开关/功率/亮度"类**实值**字段（powerStatus / power / light），**不认 type** ——
	/// GCU 对**没有**灯带的机型也会回一帧占位报文，实测原文
	/// <c>{"solution":"UNKNOWN","type":"Normal","brightNess":null,"powerStatus":null,...,"light":null,...}</c>
	/// （见 diagnostics 日志 LB-RAW + `LB HidLightbar/Status: type=Normal power= light= -&gt; presence=lightbar-present`）。
	/// 旧口径 <see cref="HasLightbarContent"/> 只看 type / powerStatus 非空，于是占位帧的 <c>type=Normal</c>
	/// 也被当成"有灯带" ⇒ <c>LightbarStatusSeen=true</c>；2026-10-09（lightmerge）又给 <c>SupportsLightbar</c>
	/// 加了 Status 回退、而**行可见性与发帧门用的是同一个 SupportsLightbar** ⇒ 画像明确报
	/// <c>LightbarSupport=0</c>（诊断「灯条:否」）的机型被判 <c>lightbar=Y</c>，灯带行出现、且只要
	/// <c>lightbar.cfg</c> 存在就照旧往 <c>HidLightbar/Ctrl</c> 发 SetEffectALL（Wave/Single）—— 这就是
	/// "发帧与能力位不一致"的根。
	/// 另：占位报文里的小写 <c>powerstatus</c>（=0，我们下发用的回显字段）会被大小写不敏感查找命中，
	/// 故"纯 0"不算实值（真实报文的实值是 On/Off 或 1..4）。
	/// 只改"存在性证据"，画像/GCU 明确报支持时一字不变。
	/// </summary>
	internal static bool HasLightbarPresenceEvidence(JObject o)
	{
		string[] array = new string[3] { "powerStatus", "power", "light" };
		foreach (string propertyName in array)
		{
			JToken? token = o.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
			if (token == null || token.Type == JTokenType.Null)
			{
				continue;
			}
			string text = token.ToString().Trim();
			if (text.Length == 0 || text == "0")
			{
				continue;
			}
			return true;
		}
		return false;
	}

	/// <summary>
	/// 2026-10-06：Logo 灯（HidLightbar_Logo/Status）"这条灯带存在"的证据。
	/// type 缺失（null）**不**等于"没有这条灯带"——耀世 25 5080 实测 type=null 但 powerStatus=On（亮度亦有值），
	/// 所以判据不看 type，只看开关/功率类字段。
	/// 2026-10-06（修回归）：**亮度 brightNess 单独有值不算证据**。实测无 Logo 灯的机型
	/// （翼龙15Pro GM5HG0A）报文恒为 type=Normal + powerStatus/power/light 全空，把 brightness 当证据
	/// 会把"没有这条灯带"误判为存在。现在要求 powerStatus/power/light 至少一个非空；
	/// 耀世的 powerStatus=On 仍满足，故不会被收回。
	/// </summary>
	internal static bool HasLogoLightEvidence(JObject o)
	{
		// 只认"开关/功率"类字段；brightNess 是亮度回显，缺灯机型也可能带默认值，不能单独作为存在性证据。
		string[] array = new string[3] { "powerStatus", "power", "light" };
		foreach (string propertyName in array)
		{
			if (!string.IsNullOrWhiteSpace(o.GetValue(propertyName, StringComparison.OrdinalIgnoreCase)?.ToString()))
			{
				return true;
			}
		}
		return false;
	}

	internal void SetIgpuOnlyStatusSupportForTests(bool? value)
	{
		IgpuOnlyStatusSupport = value;
	}

	public bool IsGpuActionAllowedByGeneration(string action)
	{
		return DisplayRoutePolicy.AllowsAction(DgpuGeneration, action, ThreeModeCapability);
	}

	private bool TryGetDirectOffsetRange(bool core, [NotNullWhen(true)] out GpuClockOffsetRange? range)
	{
		lock (_gpuOverclockLock)
		{
			if (_gpuOverclock == null)
			{
				range = null;
				return false;
			}
			range = (core ? _gpuOverclock.CoreOffset : _gpuOverclock.MemoryOffset);
			return range.IsAdjustable;
		}
	}

	internal async Task<bool> RequestCustomModePageStateAsync(TimeSpan? timeout = null)
	{
		long before = Interlocked.Read(in _fanTableVersion);
		TimeSpan timeout2 = timeout ?? TimeSpan.FromMilliseconds(4000L);
		using CancellationTokenSource cts = new CancellationTokenSource();
		Task<bool> arrived = WaitForStateAsync(() => FanTableVersion > before, timeout2, cts.Token);
		try
		{
			await Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
			await Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GET_FAN_SPEED_CURVE_SETTING" }).ConfigureAwait(continueOnCapturedContext: false);
			await Publish("LCHWOC/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
			return await arrived.ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
			cts.Cancel();
			throw;
		}
	}

	public bool CanSwitchGpuMode(int mode)
	{
		return mode switch
		{
			0 => SupportsDgpuDirect || SupportsIgpuOnly, 
			1 => SupportsDgpuDirect || SupportsIgpuOnly, 
			2 => SupportsDgpuDirect, 
			3 => SupportsIgpuOnly, 
			_ => false, 
		};
	}

	internal static bool IsRecognizedDgpuDirectStatus(string? status)
	{
		return ContainsAnyStatusToken(status, DgpuDirectStatusTokens);
	}

	internal static bool IsRecognizedIgpuOnlyStatus(string? status)
	{
		return ContainsAnyStatusToken(status, IgpuOnlyStatusTokens);
	}

	private static int ResolveIgpuOnlySaveMode(string? status)
	{
		string s = status?.ToUpperInvariant() ?? "";
		if (s.Contains("IGPU_ONLY_CONNECT_RB_ON") || s.Contains("IGPU_ONLY_ON"))
		{
			return 0;
		}
		if (s.Contains("IGPU_ONLY_CONNECT_RB_AUTO") || s.Contains("IGPU_ONLY_AUTO"))
		{
			return 3;
		}
		if (s.Contains("IGPU_ONLY_CONNECT_RB_OFF") || s.Contains("IGPU_ONLY_OFF"))
		{
			return 1;
		}
		return -1;
	}

	private static bool ContainsAnyStatusToken(string? status, string[] tokens)
	{
		if (string.IsNullOrWhiteSpace(status))
		{
			return false;
		}
		string text = status.ToUpperInvariant();
		foreach (string value in tokens)
		{
			if (text.Contains(value, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	// 2026-10-05：显示路由**只**由 DiscreteGpuDirectConnectionSwitch_Status 决定。
	// 原来这里还会用【核显省电】字段（IGpuOnlyConnectionSwitch_Status = IGPU_ONLY_CONNECT_RB_*）
	// 反推出 mode 3(AUTO)/0/1 —— 那是 §9.6 的功能混淆：
	//   · GCU 的显示路由字段本就没有 AUTO 取值（只有 TOGGLE_ON / TOGGLE_OFF / TOGGLE_IGPU）
	//   · 核显省电有独立字段、独立功能与独立按钮（IgpuOnlySaveMode / SetIGpuOnlySaveMode），
	//     不该影响 GpuMode
	//   · 删除「自动切换显卡模式」后 mode 3 已无归宿；50 系把核显省电设为 AUTO 时，
	//     ...RB_AUTO 会让 GpuMode 变成 3，导致三档高亮与切换策略错乱
	// 因此：认不出就沿用上一次的已知模式。
	internal static int ResolveGpuModeStatus(int currentMode, string? directStatus)
	{
		string text = directStatus?.ToUpperInvariant() ?? "";
		if (text == "DGPU_DIRECT_CONNECT_TOGGLE_ON" || text.Contains("DIRECT_CONNECT_ON") || text.Contains("DIRECT_CONNECTION_ON"))
		{
			return 2;
		}
		if (text == "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" || text.Contains("DIRECT_CONNECT_IGPU"))
		{
			return 0;
		}
		if (text == "DGPU_DIRECT_CONNECT_TOGGLE_OFF" || text.Contains("DIRECT_CONNECT_OFF") || text.Contains("DIRECT_CONNECTION_OFF"))
		{
			return 1;
		}
		return currentMode;
	}

	public bool SupportsQuickSwitch(string key)
	{
		return key switch
		{
			"touchpad" => true, 
			"wifi" => WifiSeen, 
			"bt" => BluetoothSeen, 
			"webcam" => WebcamSeen, 
			"winkey" => WinKeySeen, 
			"fnkey" => FnKeySeen, 
			"numpad" => NumpadSeen, 
			"osd" => true, 
			"usb" => true, 
			"deepsleep" => DeepSleepSeen, 
			"copilot" => CopilotSeen, 
			"acrecovery" => Capabilities.AcRecovery || AcRecoverySeen, 
			"highperf" => HighPerformanceSeen, 
			"fanboost" => SupportsFanBoost, 
			"lightbar" => SupportsLightbar, 
			"logolight" => SupportsLogoLight, 
			"touchpadtoggle" => TouchpadToggleSeen, 
			"singlecolorkb" => SingleColorKbSeen, 
			"uni" => UniOmniSeen, 
			"omni" => UniOmniSeen, 
			"powerlight" => PowerLightSeen, 
			"batterylogo" => BatteryLogoSeen, 
			"gamewhitelist" => GameWhitelistSeen, 
			"cpuadvperf" => CpuAdvancedPerformanceSeen && SupportsOverclockMenu, 
			// 2026-10-11（用户拍板「要它，另开一轮做麦克风静音开关」）：麦克风静音的**能力判据**不在 EC/GCU
			// 上，而在 Windows CoreAudio —— 「本机存在活动的默认录音端点，且它能被激活成 IAudioEndpointVolume
			// 并读回静音状态」。无录音设备 / 录音端点全被禁用 ⇒ 这里恒 false ⇒ SettingsForm 的
			// StaticQuick/SupportsQuickSwitch 判据会把「麦克风静音」开关与同名磁贴**整条隐藏**
			//（用户原话「按能力位隐藏或置灰，不要显示出点不动的开关」）。
			// ⚠️ 只读探测（带 3s 缓存），不写注册表 / 不写 EC。
			"micmute" => AudioEndpointMic.Available, 
			_ => false, 
		};
	}

	public bool IsLcStatusFresh(TimeSpan maximumAge, long now = -1L)
	{
		long num = Interlocked.Read(in _lcStatusReceivedAt);
		if (num == 0L)
		{
			return false;
		}
		return (double)(((now >= 0) ? now : Environment.TickCount64) - num) <= maximumAge.TotalMilliseconds;
	}

	public bool IsTemperatureFresh(TimeSpan maximumAge, long now = -1L)
	{
		long current = ((now >= 0) ? now : Environment.TickCount64);
		long receivedAt = Interlocked.Read(in _cpuInfoReceivedAt);
		long receivedAt2 = Interlocked.Read(in _gpuInfoReceivedAt);
		if (!Within(receivedAt))
		{
			return Within(receivedAt2);
		}
		return true;
		bool Within(long num)
		{
			if (num != 0L)
			{
				return (double)(current - num) <= maximumAge.TotalMilliseconds;
			}
			return false;
		}
	}

	public bool IsSensorInfoFresh(bool cpu, TimeSpan maximumAge, long now = -1L)
	{
		long num = (cpu ? Interlocked.Read(in _cpuInfoReceivedAt) : Interlocked.Read(in _gpuInfoReceivedAt));
		if (num == 0L)
		{
			return false;
		}
		return (double)(((now >= 0) ? now : Environment.TickCount64) - num) <= maximumAge.TotalMilliseconds;
	}

	public bool IsFanInfoFresh(TimeSpan maximumAge, long now = -1L)
	{
		long num = Interlocked.Read(in _fanInfoReceivedAt);
		if (num == 0L)
		{
			return false;
		}
		return (double)(((now >= 0) ? now : Environment.TickCount64) - num) <= maximumAge.TotalMilliseconds;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（0.291.9 · 修"宝龙达机型 GPU 温度显示为「—」"）：⭐ **外部取到 GPU 温度时打时间戳** ✅
	/// <para>⚠️ 根因：⭐ 界面那格 GPU 温度的判据是 ⭐ <see cref="IsSensorInfoFresh" />(<c>cpu: false</c>)，
	/// 而它读的是 <c>_gpuInfoReceivedAt</c> ⚠️ —— ⭐ 那个时间戳**原来只有 GCU/MQTT 推送会写** ❌。</para>
	/// <para>⭐ 而 <c>_cpuInfoReceivedAt</c> ⭐ **宝龙达那条路是会写的** ✅（⭐ 见 `RefreshBaolongdaSensors` ✅）
	/// ⇒ ⭐ ⭐ **所以真机上 CPU 温度显示正常（56℃）、⭐ GPU 温度却是「—」** ✅
	/// —— ⭐ 这个不对称正是证据 ✅。</para>
	/// <para>⭐ 宝龙达协议里**没有 GPU 温度**（⭐ 只有一个 CPU 温度 ⭐ 功能号 22 ✅）⇒
	/// GPU 温度只能来自 ⭐ LHM / ⭐ NVML ⇒ ⭐ 所以由调用方（<c>HardwareControl.ApplySensorPriority</c> ✅）
	/// **取到值之后**调本方法 ✅。⛔ 本方法**只打时间戳**、⭐ 不写温度值、⭐ 不碰 GCU 的字段语义 ✅。</para>
	/// </summary>
	internal void MarkGpuInfoReceived()
	{
		Interlocked.Exchange(ref _gpuInfoReceivedAt, Environment.TickCount64);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（0.291.9.1 · 修"⭐ 宝龙达机型界面上 GPU 温度恒为「—」"⭐）：
	/// ⭐ <b>把外部来源（⭐ LHM / ⭐ 加固过的 NVML ✅）取到的 GPU 温度写回门面层</b> ✅。
	/// <para>⚠️⚠️ <b>根因</b>：⭐ 主界面那行遥测（⭐ <c>SettingsForm.TelemetryParts</c> ✅）
	/// 读的是 ⭐ <b><c>hw.GpuTemp</c></b> ⚠️，⭐ 而 <c>HardwareControl.ApplySensorPriority</c>
	/// 原来**只写自己那个 <c>HardwareControl.gpuTemp</c> 静态字段** ❌ ⇒ ⭐ 于是
	/// ⭐ LHM/NVML 明明取到了温度、⭐ <b>却永远送不到主界面</b> ⚠️ ⇒ ⭐ 恒显示 「—」 ❌。</para>
	/// <para>⭐ <b>调用时机</b>：⭐ 只由 <c>ApplySensorPriority</c> 在
	/// ⭐ <b>"⭐ 厂商没给值（<c>hw.GpuTemp &lt;= 0</c> ⚠️）⭐ 且 ⭐ LHM/NVML 取到了"</b>
	/// 这一支里调用 ✅ ⇒ ⭐ <b>GCU 机型（⭐ 厂商值恒有效 ✅）永远走不到</b> ⇒
	/// ⭐ <b>行为逐位不变</b> ✅。⛔ 本方法**不参与**任何 GCU 字段语义 ✅。</para>
	/// <para>⚠️ 与 <see cref="MarkGpuInfoReceived" /> 的区别：⭐ 本方法**连值一起写** ✅
	/// （⭐ 因为主界面读的是值本身 ⚠️）；⭐ 那个只打时间戳 ✅。⭐ 本方法内部**也会打时间戳** ✅。</para>
	/// </summary>
	internal void MarkExternalGpuTemp(int celsius)
	{
		if (celsius <= 0)
		{
			return; // ⛔ 无效值不写、⭐ 也不打时间戳（⭐ 免得把「—」变成假的有效 ✅）
		}
		GpuTemp = celsius;
		Interlocked.Exchange(ref _gpuInfoReceivedAt, Environment.TickCount64);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（同上）：⭐ **外部取到风扇转速时打时间戳** ✅（⭐ 与 <see cref="MarkGpuInfoReceived" /> 同款 ✅）
	/// <para>⚠️ 说明：⭐ 宝龙达那条路已在 `RefreshBaolongdaSensors` 内部直接打了 ⚠️；
	/// 本方法是给"⭐ 别的来源也算数"⭐ 留的口子（⭐ 目前**没有**调用方，⭐ 保留以免将来重现同类漏打 ✅）。</para>
	/// </summary>
	internal void MarkFanInfoReceived()
	{
		Interlocked.Exchange(ref _fanInfoReceivedAt, Environment.TickCount64);
	}

	internal int Pl4ToWire(int watts)
	{
		return ModeDetailEncoding.Pl4WireWatts(watts, UsesAmdPowerFields, Pl4Scale);
	}

	internal int Pl4Effective(int watts)
	{
		if (!UsesAmdPowerFields)
		{
			return Pl4ToWire(watts) * Pl4Scale;
		}
		return watts;
	}

	// 2026-10-09（诊断增强，仅加日志/采集）：MQTT 断开原因的诊断文案辅助。
	// 空值一律显示 n/a（不把 null 直接拼进日志）；换行压成空格、超长截断，
	// 保证“一次断开 = 一行日志”，不会被日志切行。
	private static string MqttDiagText(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "n/a";
		}
		string text = value.Replace('\r', ' ').Replace('\n', ' ');
		return (text.Length <= 300) ? text : (text.Substring(0, 300) + "…");
	}

	// 异常取“类型: 消息”（含 InnerException），无异常显示 n/a。
	// 2026-10-09（GCU 订阅侧取证）：由“只展开一层内层”改为“最多展开三层”，并在链上任何一层是
	// SocketException 时补记 SocketError（被 RST 时典型是 ConnectionReset / 10054）；
	// 只有加长、没有改写既有文案口径。
	private static string MqttDiagExceptionText(Exception? exception)
	{
		if (exception == null)
		{
			return "n/a";
		}
		string text = exception.GetType().Name + ": " + MqttDiagText(exception.Message);
		Exception? inner = exception.InnerException;
		int depth = 0;
		while (inner != null && depth < 2)
		{
			text = text + " <- " + inner.GetType().Name + ": " + MqttDiagText(inner.Message);
			inner = inner.InnerException;
			depth++;
		}
		if (inner != null)
		{
			text += " <- …(内层更深)";
		}
		SocketError? socketError = MqttDiagSocketError(exception);
		if (socketError.HasValue)
		{
			text = text + " [SocketError=" + socketError.Value + "/" + (int)socketError.Value + "]";
		}
		return text;
	}

	// 沿 InnerException 链找第一处 SocketException 的 SocketError（判断是否被 RST）。
	private static SocketError? MqttDiagSocketError(Exception? exception)
	{
		for (Exception? current = exception; current != null; current = current.InnerException)
		{
			if (current is SocketException socketException)
			{
				return socketException.SocketErrorCode;
			}
		}
		return null;
	}

	// ===== 2026-10-15（C-1 第 2 步）：老机型 MICommonInterface 后端 ==========================
	// 对照《差异与合并计划》A-22 与作者 0.290.6 实现。⚠️ 全段只在 UsesLegacyMifs 为真时才有动作；
	// 本机（没有该 WMI 类）走不到这里任何一行 ⇒ 行为与改动前逐项一致。

	/// <summary>
	/// 把本会话切到**老机型后端**：替换能力位为"仅内置性能档"的最小集、起 3 s 定时刷新、
	/// 立刻读一次档位，然后对外宣布"连接就绪"（对照 A-22 里 ConnectAsync 先探那一段）。
	/// </summary>
	private async Task ApplyLegacyBackendAsync(LegacyMifsWmi legacy)
	{
		_activeBackend = MechrevoBackendKind.LegacyMifs;
		// ⚠️ 最小能力集：老机型没有 GCU 那套 ItemSupport，凡"靠上报才知道"的能力一律保持 false
		// ⇒ 风扇曲线/灯效/液冷/校色等页签照旧判"不支持"（⛔ 不假装可写）。对照 A-22 第 5 条。
		Capabilities = new MechrevoDeviceCapabilities
		{
			ProjectId = "MICommonInterface",
			IsMechrevo = true,
			ProfileAvailable = true,
			TurboMode = true,
			AmdPlatform = PawnIO.CpuInfo.IsAMD
		};
		// 计划书 :704 的验收标准 4：日志出现这一行。
		Logger.WriteLine("Hardware backend: legacy MICommonInterface; built-in modes only.");
		_legacyTimer = new Timer(delegate
		{
			_ = RefreshLegacyModeAsync();
		}, null, LegacyRefreshIntervalMs, LegacyRefreshIntervalMs);
		await RefreshLegacyModeAsync().ConfigureAwait(continueOnCapturedContext: false);
		RaiseIsolated(CapabilitiesChanged, "CapabilitiesChanged");
		NotifyConnectionReady();
	}

	/// <summary>
	/// 读一次老接口的档位并同步到本对象（对照 A-22 的 <c>RefreshLegacyModeAsync</c>）。
	/// <c>_legacyRefreshGate</c> 把三条来路（3 s 定时器 / 手动切档 / 初始状态请求）串行化；
	/// <c>_legacySnapshotVersion</c> 丢弃乱序到的旧快照。⛔ 失败只记一行日志、不外抛
	/// （3 s 定时器跑在后台线程上，抛出去就是未观察异常）。
	/// </summary>
	internal async Task<bool> RefreshLegacyModeAsync(CancellationToken ct = default(CancellationToken))
	{
		// ⭐⭐ 2026-10-16（宝龙达适配）：⭐ 这里从"只认老接口"改成**认两种后端** ——
		//     ⭐ 两者协议同族（⭐ 同一个 MICommonInterface / 250-251 / 功能号 8 ✅）⇒
		//     ⭐ 读档位这一件事**一套代码就够**（⭐ 见 ReadBuiltInMode ✅）。
		//     ⚠️ 老同方机型上 `_baolongda` 恒为 null ⇒ ⭐ 行为与改动前**逐位一致** ✅。
		if (!UsesBuiltInModes || _disposed)
		{
			return false;
		}
		await _legacyRefreshGate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			LegacyMifsSnapshot snapshot = await Task.Run((Func<LegacyMifsSnapshot>)ReadBuiltInMode, ct).ConfigureAwait(continueOnCapturedContext: false);
			bool changed;
			lock (_legacyStateGate)
			{
				if (_disposed || snapshot.Version <= _legacySnapshotVersion)
				{
					return IsConnected;
				}
				_legacySnapshotVersion = snapshot.Version;
				changed = OperatingMode != snapshot.OperatingMode;
				OperatingMode = snapshot.OperatingMode;
				FanStatusSeen = true;
			}
			if (changed)
			{
				RaiseIsolated(ModeChanged, "ModeChanged", snapshot.OperatingMode);
			}
			RaiseIsolated(StateChanged, "StateChanged", "Fan/Status");
			RaiseIsolated(DataChanged, "DataChanged");
			return true;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("legacy-mifs-read", "Legacy MIFS read failed: " + ex.Message);
			return false;
		}
		finally
		{
			_legacyRefreshGate.Release();
		}
	}

	/// <summary>
	/// 切老接口的内置档（对照 A-22 的 <c>SwitchLegacyModeAsync</c>）：写一次 + 该实现内部最多 5 次回读确认
	/// （150 ms 间隔），成功后再走一次统一的 <see cref="RefreshLegacyModeAsync" /> 把状态与事件补齐。
	/// <paramref name="operatingMode" /> 取内部口径 0..2；越界一律返回 false。
	/// </summary>
	internal async Task<bool> SwitchLegacyModeAsync(int operatingMode, CancellationToken ct)
	{
		if (!UsesBuiltInModes || operatingMode < 0 || operatingMode > 2)
		{
			return false;
		}
		bool confirmed = await SwitchBuiltInModeAsync(operatingMode, ct).ConfigureAwait(continueOnCapturedContext: false);
		await RefreshLegacyModeAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		return confirmed && OperatingMode == operatingMode;
	}

	// ══════════════════════════════════════════════════════════════════════════
	// ⭐⭐ 2026-10-16（宝龙达适配）：「内置档位」的两个**共用入口**
	//
	//  ⭐ 为什么共用：⭐ 老同方（`LegacyMifsWmi`）与宝龙达（`BaolongdaWmi`）**协议同族**：
	//     · 同一个 `root\WMI:MICommonInterface`、同一个实例
	//     · 同样 `InData[1]` = 250/251、`InData[3]` = 功能号 8
	//     · ⭐ **档位映射逐字相同**（⭐ 真机已验证：⭐ 平衡→0 · ⭐ 增强→1 · ⭐ 静音→2 ✅）
	//  ⇒ ⭐ 所以「读一次档位 / 写一次档位」只需要**一套代码**，⭐ 换的只是**调用哪个对象** ✅
	//  ⇒ ⛔ 不复制两份（⭐ 复制两份以后必然只改一处 ✅）
	// ⚠️ 优先级：⭐ 同一会话里**只会有一个**（⭐ 候选表探到谁就是谁 ✅）。
	// ══════════════════════════════════════════════════════════════════════════

	/// <summary>读一次档位（⭐ 老同方 或 ⭐ 宝龙达，⭐ 谁在管事读谁 ✅）。⭐ 两者都没有时抛异常（⭐ 调用方已先判 <see cref="UsesBuiltInModes" /> ✅）。</summary>
	private LegacyMifsSnapshot ReadBuiltInMode()
	{
		BaolongdaWmi baolongda = _baolongda?.Wmi;
		if (baolongda != null)
		{
			return baolongda.ReadMode();
		}
		LegacyMifsWmi legacy = _legacyMifs;
		if (legacy != null)
		{
			return legacy.ReadMode();
		}
		throw new InvalidOperationException("没有任何「内置档位」后端在管事。");
	}

	/// <summary>写一次档位（⭐ 老同方 或 ⭐ 宝龙达 ✅；⭐ 都包含"写后最多 5 次回读确认" ✅）。</summary>
	private Task<bool> SwitchBuiltInModeAsync(int operatingMode, CancellationToken ct)
	{
		BaolongdaWmi baolongda = _baolongda?.Wmi;
		if (baolongda != null)
		{
			return baolongda.SwitchModeAsync(operatingMode, ct);
		}
		LegacyMifsWmi legacy = _legacyMifs;
		if (legacy != null)
		{
			return legacy.SwitchModeAsync(operatingMode, ct);
		}
		return Task.FromResult(result: false);
	}

	// ══════════════════════════════════════════════════════════════════════════
	// ⭐⭐ 2026-10-16（宝龙达"全部接入"第 1 步 · ⭐ 前后端分离模板）
	//
	//  ⭐⭐ 模板（⭐ 见 docs\notes\宝龙达-全部接入实施方案-20261016.md 第 7 节）：
	//     ① ⭐ 在**门面层**（本类）加一个 ⭐ **意向命名**的方法 —— ⭐ 名字说"要做什么"，
	//        ⛔ 不说"发什么话题/写哪个功能号" ✅
	//     ② ⭐ 内部**分派**：⭐ 宝龙达 ⇒ ⭐ 写 WMI；⭐ 其他后端 ⇒ ⭐ **交回原来的 GCU 路径** ✅
	//     ③ ⭐ UI 只调这个意向方法 ⇒ ⭐ **它不再需要知道后端是谁** ✅
	//
	//  ⭐⭐ **为什么用 "Try…" 三态而不是把 GCU 代码整个搬进来**（⭐ 重要的工程取舍）：
	//     ⭐ 本方法**只处理宝龙达**，⭐ 其余情况返回 <c>false</c> ⇒ ⭐ 调用方**原路走 GCU** ✅
	//     ⇒ ⭐ ⭐ **GCU / 老同方 的既有代码【一字不动】** ⇒ ⭐ **回归风险 = 0** ✅✅
	//     ⭐ 这比"把 GCU 那段搬进来"更安全（⭐ 搬动大段 MQTT 代码风险高，⭐ 且收益相同 ✅）
	//     ⚠️ 待将来所有功能都接完、⭐ 真要彻底收口时，⭐ 再把 GCU 那半搬进来也不迟 ✅
	// ══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · LOGO 灯）：⭐ **意向方法** —— "⭐ 把 LOGO 灯开/关" ✅
	/// <para>⭐ 宝龙达：⭐ 走 ⭐ WMI 功能号 15（⭐ 真机已验证可读、⭐ 协议为 0/1 ✅）
	/// ⇒ ⭐ 写后**立刻回读确认**（⭐ 真机实测的 `OutData[3]` 回显让这一步很可靠 ✅）。</para>
	/// <para>⭐ 其他后端（⭐ GCU / ⭐ 老同方）：⭐ <b>返回 <c>false</c></b> ⇒ ⭐ 调用方**照原样走 GCU** ✅
	/// ⛔ **本方法绝不碰 GCU 那条路** ✅。</para>
	/// </summary>
	/// <param name="on">⭐ 要开还是要关。</param>
	/// <param name="confirmed">⭐ 宝龙达路径下：⭐ 回读确认后的实际状态 ✅（⭐ 非宝龙达时无意义 ✅）。</param>
	/// <returns>⭐ <c>true</c> = 本次由宝龙达处理完了（⭐ 调用方⛔ 不要再发 GCU ✅）；⭐ <c>false</c> = 不归本方法管 ✅。</returns>
	internal bool TrySetLogoLightViaBackend(bool on, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false; // ⭐ 不是宝龙达在管事 ⇒ 交给原来的 GCU 路径 ✅
		}
		try
		{
			if (!wmi.TrySetLogoLight(on))
			{
				Logger.WriteLine("宝龙达 LOGO 灯：⭐ 写入失败（详见上一行）⇒ 本次未生效");
				return true; // ⭐ 已"处理"（只是失败）⇒ 调用方仍不该再发 GCU ✅
			}
			// ⭐ 回读确认（⭐ 最多 5 次、⭐ 150 ms 间隔 —— ⭐ 与档位那套同款 ✅）
			for (int attempt = 0; attempt < 5; attempt++)
			{
				if (wmi.TryReadLogoLight(out bool current) && current == on)
				{
					confirmed = true;
					Logger.WriteLine($"宝龙达 LOGO 灯：⭐ 已{(on ? "点亮" : "熄灭")}并回读确认 ✅（第 {attempt + 1} 次）");
					return true;
				}
				Thread.Sleep(150);
			}
			Logger.WriteLine($"宝龙达 LOGO 灯：⚠️ 写入完成但 {(5 * 150)} ms 内**回读不一致** ⇒ 如实报告未确认 ⚠️");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 LOGO 灯：⭐ 操作异常（已吞掉，不影响别处）：" + ex.Message);
			return true; // ⭐ 已接管 ⇒ ⛔ 不让调用方再走 GCU ✅
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 灯光电源开关 · ⭐ 机主反馈「键盘灯 logo灯不可用」）：
	/// ⭐ 把 <c>MechrevoService.PublishLightPower(topic, on)</c> 这一步**分派给非 GCU 后端** ✅
	/// <para>⚠️ 根因（⭐ 真机诊断包实证）：那条路**直发 MQTT** ⇒ 宝龙达没有 GCU ⇒
	/// 日志刷「<c>PublishLightPower fail: 发布到 Keyboard/Ctrl 失败：MQTT 未连接</c>」❌
	/// ⇒ **完全没经过本类** ⇒ 键盘灯/LOGO 灯都点不动 ⚠️</para>
	/// <para>⭐ 返回 <c>true</c> ⇒ 已由本后端处理（⭐ 调用方 ⛔ 不要再发 MQTT ✅）；
	/// ⭐ <c>false</c> ⇒ 不归它管 ⇒ 调用方**原路走** ✅（⭐ 非宝龙达恒 false ⇒ 逐位不变 ✅）。</para>
	/// <para>⚠️ 诚实边界：本入口**只带 <paramref name="on" />**（⭐ 没有颜色/亮度 ⚠️）
	/// ⇒ ⭐ 点亮时用 ⭐ **白色 + 3 级亮度** ⭐ 作保守默认，⭐ **并在日志里写明用了默认值** ✅
	/// （⛔ 不假装是从界面取到的 ⚠️）；⭐ 关灯时只写「模式 0」（关），不需要颜色 ✅。</para>
	/// </summary>
	internal bool TrySetLightPowerViaBackend(string topic, bool on, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false; // ⭐ 不是宝龙达在管事 ⇒ 交给原来的 GCU 路径 ✅
		}
		// ── ⭐ LOGO 灯：⭐ 复用现成实现 ✅ ──
		if (string.Equals(topic, "HidLightbar_Logo/Ctrl", StringComparison.Ordinal))
		{
			bool logoHandled = TrySetLogoLightViaBackend(on, out confirmed);
			if (logoHandled && confirmed)
			{
				// ⭐⭐ 2026-10-16（宝龙达 · 灯光「关了又开」BUGFIX · LOGO 那半边）：
				//   ⚠️ 根因：`QuickSwitches["logolight"]` 只由 `RefreshBaolongdaSwitchStates` 每 3 s 刷一次 ⚠️
				//      ⇒ `ConfirmLightPower` 的 180 ms／500 ms／700 ms 等待**等不到**它 ❌ ⇒ 它去发 MQTT
				//      ⇒ 失败 ⇒ `SetLightPower` 返回 false ⇒ ⭐ **界面把开关拨回去** ❌
				//   ⇒ ⭐ 写入并**回读确认后立刻**镜像本地状态 ✅（⭐ 口径与 3 s 周期刷新同源 ✅ ——
				//      功能号 15 的真实回读值 ✅，⛔ 不是拿"意图"当状态 ⚠️）。
				SyncBaolongdaQuickSwitch("logolight", on ? 1 : 0);
			}
			return logoHandled;
		}
		// ── ⭐ 键盘灯：⭐ 只处理这一个主题 ⚠️ ──
		if (!string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal))
		{
			return false; // ⭐ 其余主题不归它管 ⇒ 交回原路 ✅
		}
		try
		{
			if (!on)
			{
				// ⭐ 关灯：⭐ 只写「模式 0」（关）✅ —— ⭐ 不需要颜色/亮度 ✅
				if (!wmi.TrySetKeyboardMode(0))
				{
					Logger.WriteError("宝龙达 灯光：⚠️ 关键盘灯写入失败（详见上一行）⇒ 本次未生效");
					return true; // ⭐ 已接管（只是失败）⇒ ⛔ 别让调用方再发 GCU ✅
				}
			}
			else
			{
				// ⚠️ 本入口没带颜色/亮度 ⇒ ⭐ 用保守默认（白色 + 3 级）并如实记日志 ✅
				const int defaultArgb = unchecked((int)0xFFFFFFFF);
				const int defaultBrightness = 3;
				Logger.WriteError($"宝龙达 灯光：⭐ 本入口只带开关（topic={topic}）⇒ 点亮用保守默认"
					+ $"（白色 0x{defaultArgb:X8} · 亮度 {defaultBrightness}/3）⚠️"
					+ " —— ⭐ 界面若另有配色/亮度入口，那条会各自下发 ✅");
				if (!TrySetKeyboardLightingViaBackend(true, defaultArgb, defaultBrightness, out confirmed))
				{
					return false; // ⭐ 理论上不会发生 ⇒ 交回原路更安全 ✅
				}
				// ⭐ 这里**刻意不提前 return** —— ⭐ 继续走下面的「功能号 16 真实模式回读」✅
				//   （⭐ 只有那样才会把 `KeyboardPower` 镜像写成真值 ✅ ⇒ `ConfirmLightPower` 才不会再发 MQTT ✅）
			}
			// ⭐⭐ 2026-10-16（宝龙达 · 灯光「关了又开」BUGFIX）：
			//   ⚠️ 根因（⭐ 机主诊断包实证）：本方法原来用【功能号 10 能不能读到】当"回读确认"⚠️
			//      ⇒ 那只证明"接口活着"，⛔ 不证明"模式真的切过去了"❌；而且**本类从不更新
			//      `KeyboardPower`**（⭐ 它只在 GCU payload 解析里被赋值 ✅）⇒ 宝龙达下它**恒为默认 true** ❌
			//      ⇒ `MechrevoService.ConfirmLightPower` 的 `IsConfirmed()` 恒假 ⇒ 它去发 MQTT
			//      ⇒ 「ConfirmLightPower fail: …MQTT 未连接」⇒ `SetLightPower` 返回 **false**
			//      ⇒ ⭐ **界面把开关拨回去 = 用户看到的「关了又开」** ✅
			//   ⇒ 改为【用功能号 16 的真实模式值】判开/关 ✅，⭐ 并如实回写 `KeyboardPower` 镜像 ✅：
			//      ⭐ 口径依据（⭐ 参考 `_refs\JiaolongControl` · MIT · ⭐ 只取协议事实 ✅ ——
			//      `SysEnums.cs` 的 `RGBKeyboardMode`）：⭐ `Mode_Off = 0` · ⭐ `Mode_RGBFixedMode = 2` ·
			//      ⭐ `Unknow = 255` ⇒ ⭐ **「开」＝ 模式 ≠ 0** ✅ · ⭐ **「关」＝ 模式 0** ✅
			//      （⭐ 本方法下发时正是这么写的：⭐ 关 ⇒ 模式 0 ✅ · ⭐ 开 ⇒ 模式 2 ✅）。
			for (int attempt = 0; attempt < 5; attempt++)
			{
				if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardMode, out byte mode) && mode != 255)
				{
					bool actual = mode != 0;
					KeyboardPower = actual; // ⭐ 如实回写本地镜像 ✅（⭐ 修「关了又开」的关键一步 ⚠️）
					confirmed = actual == on;
					Logger.WriteError($"宝龙达 灯光：⭐ 键盘灯电源已{(on ? "开" : "关")}并回读确认"
						+ $"（⭐ 功能号 16 模式={mode} · ⭐ 实际={(actual ? "开" : "关")} · ⭐ 第 {attempt + 1} 次）"
						+ (confirmed ? " ✅" : " ⚠️ 与目标不一致 ⇒ 如实报告未确认 ⚠️"));
					return true;
				}
				Thread.Sleep(150);
			}
			Logger.WriteError($"宝龙达 灯光：⚠️ 键盘灯电源写入完成但 {5 * 150} ms 内读不到模式"
				+ "（⭐ 功能号 16）⇒ 如实报告未确认 ⚠️（⭐ 本地镜像保持不变、⛔ 不编造状态 ✅）");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteError("宝龙达 灯光：⭐ 操作异常（已吞掉，不影响别处）：" + ex.Message);
			return true; // ⭐ 已接管 ⇒ ⛔ 不让调用方再走 GCU ✅
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 灯光「关了又开」BUGFIX）：⭐ 把
	/// <c>MechrevoService.ConfirmLightPower(topic, on)</c> 这一步**分派给非 GCU 后端** ✅
	/// <para>⚠️ 根因（⭐ 机主诊断包实证）：那条确认路**只认 GCU/MQTT** —— 它靠"等
	/// <c>KeyboardPower</c>／<c>QuickSwitches[key]</c> 变成目标值"来判定 ✅，等不到就发
	/// <c>GETSTATUS</c> ❌。而宝龙达下 ① <c>KeyboardPower</c> **从不更新**（⭐ 它只在 GCU payload
	/// 解析里被赋值 ⚠️）② <c>QuickSwitches["logolight"]</c> 只每 3 s 刷一次（⭐ 180/500/700 ms 等不到 ✅）
	/// ⇒ 它**恒判"未确认"** ⇒ 发 MQTT ⇒ 失败 ⇒ 返回 <c>false</c>
	/// ⇒ ⭐ **<c>SetLightPower</c> 返回 false ⇒ 界面把开关拨回去 = 用户看到的「关了又开」** ❌</para>
	/// <para>⭐ 本方法**只读硬件**（⭐ 功能号 16 模式 ／ ⭐ 功能号 15 LOGO ✅）、⭐ 并把真实值回写本地镜像 ✅
	/// ⇒ ⭐ 调用方据此确认，⛔ 一条 MQTT 都不发 ✅。</para>
	/// <para>⭐ 主题口径与 <see cref="TrySetLightPowerViaBackend" /> **逐字一致** ✅
	/// （⭐ 只认 <c>Keyboard/Ctrl</c> 与 <c>HidLightbar_Logo/Ctrl</c> ✅ —— ⭐ 保证"接管"与"确认"成对 ✅）。
	/// ⭐ 返回 <c>true</c> ⇒ 已由本后端处理 ✅；⭐ <c>false</c> ⇒ 不归它管 ⇒ 调用方**原路走** ✅
	/// （⭐ 非宝龙达恒 false ⇒ 逐位不变 ✅）。</para>
	/// </summary>
	internal bool TryConfirmLightPowerViaBackend(string topic, bool on, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false; // ⭐ 不是宝龙达在管事 ⇒ 交给原来的 GCU 路径 ✅
		}
		try
		{
			if (string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal))
			{
				// ⭐ 口径同 TrySetLightPowerViaBackend：⭐ Mode_Off=0 ⇒ 关 ✅ · ⭐ 非 0 ⇒ 开 ✅ · ⭐ Unknow=255 ⇒ 读不到 ✅
				if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardMode, out byte mode) && mode != 255)
				{
					bool actual = mode != 0;
					KeyboardPower = actual; // ⭐ 如实回写镜像 ✅
					confirmed = actual == on;
					return true;
				}
				return true; // ⭐ 已接管（⭐ 只是读不到 ⇒ 如实报未确认 ⚠️）⇒ ⛔ 不让调用方去发 MQTT ✅
			}
			if (string.Equals(topic, "HidLightbar_Logo/Ctrl", StringComparison.Ordinal))
			{
				if (wmi.TryReadLogoLight(out bool logoOn))
				{
					SyncBaolongdaQuickSwitch("logolight", logoOn ? 1 : 0);
					confirmed = logoOn == on;
					return true;
				}
				return true;
			}
			return false; // ⭐ 其余主题（⭐ 灯带／铰链／同步 ⚠️）不归它管 ⇒ 交回原路 ✅
		}
		catch (Exception ex)
		{
			Logger.WriteError("宝龙达 灯光：⭐ 确认分派异常（已吞掉，不影响别处）：" + ex.Message);
			return true; // ⭐ 已接管 ⇒ ⛔ 不让调用方再走 GCU ✅
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 灯光「关了又开」BUGFIX 的配套）：⭐ 这个灯光主题**是否由非 GCU 后端
	/// 本地接管** —— ⭐ 供 <c>MechrevoService.RequestLightStatus</c> **少发一条注定失败的 MQTT** ✅。
	/// <para>⭐ 判据与 <see cref="TrySetLightPowerViaBackend" /> 逐字一致 ✅（⭐ 只认 <c>Keyboard/Ctrl</c>
	/// 与 <c>HidLightbar_Logo/Ctrl</c> ✅）；⭐ 非宝龙达恒 <c>false</c> ⇒ 调用方逐位不变 ✅。</para>
	/// </summary>
	internal bool HandlesLightTopicViaBackend(string topic)
	{
		if (_baolongda?.Wmi == null || _disposed || string.IsNullOrEmpty(topic))
		{
			return false;
		}
		return string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal)
			|| string.Equals(topic, "HidLightbar_Logo/Ctrl", StringComparison.Ordinal);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 灯光效果 · ⭐ 机主反馈「键盘灯 logo灯不可用」的**颜色/亮度**那半边）：
	/// ⭐ 把 <c>MechrevoService.SetLightEffect(topic, effect, light, speed, direction, singleColor, save)</c>
	/// 这一步**分派给非 GCU 后端** ✅
	/// <para>⚠️ 根因：<c>SettingsForm.cs</c> · <c>Program.cs</c> · <c>LightForm.cs</c> 三处都**直接**调
	/// <c>service.SetLightEffect(...)</c> ⇒ 它**直发 MQTT** ⇒ 宝龙达没有 GCU ⇒ 换颜色/调亮度全都发不出去 ❌
	/// （⭐ 开关那半边已由 <see cref="TrySetLightPowerViaBackend" /> 接上 ✅，
	/// ⭐ 但那条入口**不带颜色/亮度** ⚠️ ⇒ 所以必须在本入口再补一层 ✅）。</para>
	/// <para>⭐ 协议（⭐ 参考 <c>_refs\JiaolongControl</c> · MIT · ⭐ 只取协议事实 ✅）：
	/// ⭐ 键盘灯 ＝ 功能号 <b>17</b> 颜色（<c>byte[3]{R,G,B}</c>）⇒ <b>16</b> 模式
	/// （<c>Mode_Off=0</c> / <c>Mode_RGBFixedMode=2</c>）⇒ <b>18</b> 亮度
	/// （<c>Level_0..Level_3</c> ＝ 0~3，⭐ 共 4 级 ✅）；
	/// ⭐ LOGO 灯 ＝ 功能号 <b>15</b>，⭐ **只有 开/关**（<c>ResultState: OFF=0 / ON=1</c>）
	/// ⇒ ⭐ 没有颜色/亮度 ✅。</para>
	/// <para>⚠️⚠️ <b>诚实边界（⭐ 一定写进日志 ✅）</b>：⭐ 宝龙达固件**只支持「关 ／ RGB 固定色」** ⚠️
	/// ⇒ ⭐ 呼吸 · 波浪 · 彩虹 · 涟漪 · 雨滴 · 跑马灯 · 极光 · 游戏 等**动态效果一律【降级为固定色】** ✅
	/// （⭐ 日志里明确写「降级」✅，⛔ 不假装支持 ⚠️）；⭐ <c>speed</c> ／ <c>direction</c>
	/// **在宝龙达协议里没有对应字段** ⇒ ⭐ 忽略并记日志 ✅。</para>
	/// <para>⭐ 返回 <c>true</c> ⇒ 已由本后端处理（⭐ 调用方 ⛔ 不要再发 MQTT ✅）；
	/// ⭐ <c>false</c> ⇒ 不归它管 ⇒ 调用方**原路走** ✅（⭐ 非宝龙达恒 false ⇒ 逐位不变 ✅）。</para>
	/// </summary>
	/// <param name="topic">⭐ 灯光主题（⭐ 只认 <c>Keyboard/*</c> 与**含 Logo** 的灯条主题 ✅）。</param>
	/// <param name="effect">⭐ 效果 ID（⭐ 例如 <c>Single</c> ／ <c>Breathing</c> ／ <c>Manual</c> ✅）。</param>
	/// <param name="light">⭐ 亮度档（⭐ 界面口径 0~4；⭐ 宝龙达只到 3 ⇒ ⭐ 会夹紧并记日志 ✅）。</param>
	/// <param name="colorArgb">⭐ 单色（<c>null</c> ＝ 界面没给色 ⇒ ⭐ 先试读当前值、⭐ 读不到才用白色 ✅）。</param>
	/// <param name="confirmed">⭐ 回读确认后的实际状态 ✅。</param>
	internal bool TrySetLightEffectViaBackend(string topic, string effect, int light, int? colorArgb, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false; // ⭐ 不是宝龙达在管事 ⇒ 交给原来的 GCU 路径 ✅
		}
		if (string.IsNullOrEmpty(topic))
		{
			return false;
		}
		try
		{
			// ⭐ 统一口径：⭐ 「关」 ＝ ⭐ 效果是 Off ／ None ／ 空 ⭐ 或 ⭐ 亮度 ≤ 0 ✅
			bool off = IsLightEffectOff(effect) || light <= 0;
			// ── ⭐ LOGO 灯（⭐ 功能号 15：⭐ 只有 开 ／ 关 ✅）──
			if (topic.IndexOf("Logo", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (!off)
				{
					Logger.WriteError("宝龙达 LOGO 灯：⭐ 本机只有【开 ／ 关】（⭐ 功能号 15）⚠️ ⇒ 界面给的效果「"
						+ effect + "」／亮度 " + light + " ⭐ **没有对应字段 ⇒ 已忽略** ✅");
				}
				return TrySetLogoLightViaBackend(!off, out confirmed);
			}
			// ── ⭐ 键盘灯（⭐ 功能号 16 ／ 17 ／ 18 ✅）──
			if (!topic.StartsWith("Keyboard/", StringComparison.OrdinalIgnoreCase))
			{
				return false; // ⭐ 其余灯条主题（⭐ Lightbar ／ Hinge ／ Sync ⚠️）不归它管 ⇒ 交回原路 ✅
			}
			if (off)
			{
				// ⭐ 关灯：⭐ 只写「模式 0」（关）✅ —— ⭐ 不需要颜色/亮度 ✅
				if (!wmi.TrySetKeyboardMode(0))
				{
					Logger.WriteError("宝龙达 键盘灯效：⚠️ 关灯写入失败（详见上一行）⇒ 本次未生效");
					return true; // ⭐ 已接管（只是失败）⇒ ⛔ 别让调用方再发 GCU ✅
				}
			}
			else
			{
				// ⭐ ① 颜色（⭐ 功能号 17，⭐ 三字节 R,G,B ✅）
				int argb;
				if (colorArgb.HasValue)
				{
					argb = colorArgb.Value & 0xFFFFFF;
				}
				else if (TryReadKeyboardColorViaBackend(wmi, out int currentArgb))
				{
					argb = currentArgb;
					Logger.WriteError("宝龙达 键盘灯效：⭐ 界面没给颜色 ⇒ ⭐ **沿用当前回读值** #"
						+ argb.ToString("X6") + " ✅（⛔ 不拿白色去冲掉用户已设的色 ✅）");
				}
				else
				{
					argb = 0xFFFFFF;
					Logger.WriteError("宝龙达 键盘灯效：⚠️ 界面没给颜色、⭐ **回读也读不到当前值** ⇒"
						+ " 用保守默认**白色** 0xFFFFFF ✅（⭐ 如实记录 ⚠️）");
				}
				// ⚠️ 动态效果 ⇒ ⭐ 降级为固定色（⭐ 写明 ✅；⛔ 不假装支持 ⚠️）
				if (!IsFixedColorEffect(effect))
				{
					Logger.WriteError("宝龙达 键盘灯效：⭐ 效果「" + effect + "」在本机 **降级为固定色** ✅"
						+ "（⚠️ 宝龙达固件只支持 关 ／ RGB 固定色 ⇒ 呼吸·波浪·彩虹·涟漪 等**动态效果无法下发** ⚠️；"
						+ "⭐ speed/direction 在宝龙达协议里没有对应字段 ⇒ ⭐ 已忽略 ✅）");
				}
				int brightness = Math.Max(0, Math.Min(3, light));
				if (light > 3)
				{
					Logger.WriteError("宝龙达 键盘灯效：⭐ 亮度 " + light + " 超出本机范围 ⇒ ⭐ **夹紧到 "
						+ brightness + "**（⭐ 宝龙达只有 0~3 共 4 级 ✅）");
				}
				if (!TrySetKeyboardLightingViaBackend(true, argb, brightness, out confirmed))
				{
					return false; // ⭐ 理论上不会发生 ⇒ 交回原路更安全 ✅
				}
				if (confirmed)
				{
					return true; // ⭐ 里面已经记好日志了 ✅
				}
			}
			// ⭐ 回读确认（⭐ 与 LOGO 灯 ／ 灯光电源同款：⭐ 5 次 × 150 ms ✅）
			for (int attempt = 0; attempt < 5; attempt++)
			{
				if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardStatus, out byte _))
				{
					confirmed = true;
					Logger.WriteError("宝龙达 键盘灯效：⭐ 已" + (off ? "关" : "应用")
						+ "并回读确认 ✅（第 " + (attempt + 1) + " 次）");
					return true;
				}
				Thread.Sleep(150);
			}
			Logger.WriteError("宝龙达 键盘灯效：⚠️ 写入完成但 " + (5 * 150)
				+ " ms 内**回读不一致** ⇒ 如实报告未确认 ⚠️");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteError("宝龙达 键盘灯效：⭐ 操作异常（已吞掉，不影响别处）：" + ex.Message);
			return true; // ⭐ 已接管 ⇒ ⛔ 不让调用方再走 GCU ✅
		}
	}

	/// <summary>⭐ 判「关灯」—— ⭐ 界面口径：⭐ 效果是 Off ／ None ／ 空 ✅。</summary>
	private static bool IsLightEffectOff(string? effect)
	{
		return string.IsNullOrWhiteSpace(effect)
			|| effect.Equals("Off", StringComparison.OrdinalIgnoreCase)
			|| effect.Equals("None", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>⭐ 判「固定色」效果 —— ⭐ 宝龙达固件**只支持这一种** ✅（⭐ 其余一律降级 ✅）。</summary>
	private static bool IsFixedColorEffect(string? effect)
	{
		return !string.IsNullOrWhiteSpace(effect)
			&& (effect.Equals("Single", StringComparison.OrdinalIgnoreCase)
				|| effect.Equals("Static", StringComparison.OrdinalIgnoreCase)
				|| effect.Equals("Fixed", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// ⭐ 回读键盘灯颜色（⭐ 功能号 17 ⇒ ⭐ <c>OutData[4],[5],[6]</c> ＝ R,G,B ✅）。
	/// ⭐ 读不到 ⇒ <c>false</c> ✅（⭐ 调用方据此退回保守默认 ⚠️）。
	/// </summary>
	private static bool TryReadKeyboardColorViaBackend(BaolongdaWmi wmi, out int argb)
	{
		argb = 0;
		if (!wmi.TryReadBytesChecked(BaolongdaWmi.Function.RgbKeyboardColor, 3, out byte[] rgb)
			|| rgb == null || rgb.Length < 3)
		{
			return false;
		}
		argb = (rgb[0] << 16) | (rgb[1] << 8) | rgb[2];
		return true;
	}

	/// <summary>
	/// ⭐ 读一次宝龙达 LOGO 灯状态（⭐ 只读 ✅）。⭐ 非宝龙达时返回 <c>false</c> ✅。
	/// </summary>
	internal bool TryReadLogoLightViaBackend(out bool on)
	{
		on = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		try
		{
			return wmi.TryReadLogoLight(out on);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 LOGO 灯：⭐ 读取异常：" + ex.Message);
			return false;
		}
	}

	// ══════════════════════════════════════════════════════════════════════════
	// ⭐⭐ 2026-10-16（宝龙达"全部接入"第 2~4 步）：⭐ 键盘灯 / ⭐ 功耗墙 / ⭐ 风扇手动
	//     ⭐ 全部沿用第 1 步（LOGO 灯）那个 ⭐ **`Try…` 三态模板** ✅：
	//        ⭐ 宝龙达 ⇒ ⭐ 写 WMI ＋ ⭐ 回读/校验；⭐ 其余后端 ⇒ ⭐ 返回 false ⇒ ⭐ **交回原路** ✅
	//     ⇒ ⭐ ⭐ **GCU / 老同方 的既有代码，本轮同样【一行未改】** ✅
	// ══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 键盘灯）：⭐ **意向方法** —— "⭐ 把键盘灯设成：开/关 ＋ 颜色 ＋ 亮度" ✅
	/// <para>⭐ 宝龙达协议（⭐ 参考实现 + ⭐ 我们的功能号表 ✅）：
	/// ⭐ ① ⭐ 功能号 16 = 模式（⭐ 0 = 关 · ⭐ 2 = RGB 固定色 ✅）
	/// ⭐ ② ⭐ 功能号 17 = 颜色（⭐ 写 3 字节 ⭐ R,G,B ✅）
	/// ⭐ ③ ⭐ 功能号 18 = 亮度（⭐ 0~3 级 ✅）</para>
	/// <para>⚠️⚠️ <b>真机尚未验证</b>：⭐ 16/17/18 这三个功能号**没在真机上试过** ⚠️
	/// （⭐ 已真机验证过的是 8 / 13 / 22 / 20 / 23 / 15 ✅）⇒ ⭐ 所以 ⭐ **调用方必须先确认能力位**，
	/// ⚠️ 且 ⭐ 本方法在**写入前先试读一次**（⭐ `RgbKeyboardStatus` 功能号 10 ✅）：
	/// ⭐ 读得到才认为"这套键盘灯接口确实在"⇒ ⛔ 否则直接拒绝，⭐ **不盲写** ✅。</para>
	/// <para>⭐ 顺序严格按参考实现：⭐ **先发颜色（17）⇒ ⭐ 再发模式（16）⇒ ⭐ 最后亮度（18）** ✅
	/// —— ⚠️ 因为颜色必须在"进入固定色模式"**之前**就位，⭐ 否则会闪一下别的颜色 ✅。</para>
	/// </summary>
	internal bool TrySetKeyboardLightingViaBackend(bool on, int colorArgb, int brightness0to3, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		try
		{
			// ⭐ ① 先试读一次"键盘灯状态"（⭐ 功能号 10）—— ⭐ 读得到才敢写 ⚠️
			if (!wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardStatus, out byte status))
			{
				Logger.WriteLine("宝龙达 键盘灯：⚠️ 读不到键盘灯状态（⭐ 功能号 10）⇒ ⭐ 拒绝盲写，本轮不动 ✅"
					+ "（⚠️ 说明 16/17/18 这条链在本机可能不成立 ⇒ ⭐ 需真机进一步确认）");
				return true; // ⭐ 已接管：⭐ 不让调用方再去发 GCU ✅
			}
			if (!on)
			{
				// ⭐ 关灯：⭐ 只写模式 0 ✅
				wmi.TrySetKeyboardMode(0);
				confirmed = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardStatus, out byte off2) && off2 == 0;
				Logger.WriteLine($"宝龙达 键盘灯：⭐ 关灯 ⇒ 回读={confirmed}（⭐ 原始状态字节 {off2}）");
				return true;
			}
			// ⭐ ② 先颜色（⭐ 功能号 17，⭐ 三字节 ⭐ R,G,B ✅）
			int r = (colorArgb >> 16) & 0xFF;
			int g = (colorArgb >> 8) & 0xFF;
			int b = colorArgb & 0xFF;
			if (!wmi.TrySetKeyboardColor((byte)r, (byte)g, (byte)b))
			{
				Logger.WriteLine("宝龙达 键盘灯：⭐ 写颜色失败 ⇒ 本次未生效");
				return true;
			}
			// ⭐ ③ 再模式（⭐ 功能号 16：⭐ 2 = RGB 固定色 ✅）
			if (!wmi.TrySetKeyboardMode(2))
			{
				Logger.WriteLine("宝龙达 键盘灯：⭐ 写模式失败 ⇒ 本次未生效");
				return true;
			}
			// ⭐ ④ 最后亮度（⭐ 功能号 18：⭐ 0~3 ✅）
			wmi.TrySetKeyboardBrightness((byte)Math.Max(0, Math.Min(3, brightness0to3)));
			// ⭐ 回读确认（⭐ 功能号 10 的状态字节：⭐ 0 = 灭 · ⭐ 非 0 = 亮 ✅）
			for (int attempt = 0; attempt < 5; attempt++)
			{
				if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardStatus, out byte now) && now != 0)
				{
					confirmed = true;
					Logger.WriteLine($"宝龙达 键盘灯：⭐ 已点亮（⭐ rgb=#{r:X2}{g:X2}{b:X2} · ⭐ 亮度={brightness0to3}）"
						+ $"并回读确认 ✅（状态字节 {now}，⭐ 第 {attempt + 1} 次；⚠️ 功能号 16/17/18 仍属真机未验证项）");
					return true;
				}
				Thread.Sleep(150);
			}
			Logger.WriteLine("宝龙达 键盘灯：⚠️ 写入完成但**回读未确认**（⚠️ 16/17/18 未真机验证，⭐ 如实报告 ⚠️）");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 键盘灯：⭐ 操作异常（已吞掉）：" + ex.Message);
			return true;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 功耗墙）：⭐ **意向方法** —— "⭐ 应用一组 CPU 功耗/温度墙" ✅
	/// <para>⭐ 宝龙达协议（⭐ 功能号 23 `CpuPower` ✅）：
	/// ⭐ 写**单字节** ⭐ <c>1</c> = 开启自定义 / ⭐ <c>0</c> = 关闭 ✅；
	/// ⭐ 写**两字节** `[子命令, 值]`：⭐ `2` = PL1(瓦) · ⭐ `3` = PL2(瓦) · ⭐ `4` = 温度墙(℃) ✅</para>
	/// <para>⚠️⚠️ <b>重要的协议限制</b>：⭐ 功能号 23 ⭐ **只能写、读不回 PL1/PL2/温度墙的当前值** ⚠️
	/// ⇒ ⭐ 所以本方法 ⭐ **只能"写后回读是否已开启"来确认**（⭐ 不能确认具体数值 ✅）
	/// ⇒ ⭐ 调用方若要在界面上显示数值，⭐ **必须自己记住**（⭐ 本地记忆、⭐ 并明确标注 ⛔ 非从机器读回 ⚠️）。</para>
	/// <para>⚠️ 合理性上限（⭐ 防手滑）：⭐ PL1/PL2 夹在 ⭐ `[5, 200]` 瓦 · ⭐ 温度墙夹在 ⭐ `[60, 105]` ℃ ✅。</para>
	/// </summary>
	internal bool TryApplyCpuPowerLimitViaBackend(bool enable, int pl1Watts, int pl2Watts, int tempWallC, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		try
		{
			if (!enable)
			{
				wmi.TrySetCpuCustomEnabled(false);
				confirmed = wmi.TryReadCpuCustomEnabled(out bool offNow) && !offNow;
				Logger.WriteLine($"宝龙达 功耗墙：⭐ 已关闭自定义 ⇒ 回读确认={confirmed}");
				return true;
			}
			// ⭐ 顺序：⭐ 先设三个值 ⇒ ⭐ 最后才"开启" ✅（⭐ 免得中途以默认值生效一瞬间 ⚠️）
			int pl1 = Math.Max(5, Math.Min(200, pl1Watts));
			int pl2 = Math.Max(5, Math.Min(200, pl2Watts));
			int wall = Math.Max(60, Math.Min(105, tempWallC));
			bool ok1 = wmi.TrySetCpuLongPower((byte)pl1);
			bool ok2 = wmi.TrySetCpuShortPower((byte)pl2);
			bool ok3 = wmi.TrySetCpuTempWall((byte)wall);
			if (!(ok1 && ok2 && ok3))
			{
				Logger.WriteLine($"宝龙达 功耗墙：⚠️ 有子命令写入失败（⭐ PL1={ok1} · ⭐ PL2={ok2} · ⭐ 温度墙={ok3}）⇒ 如实报告");
			}
			bool okOn = wmi.TrySetCpuCustomEnabled(true);
			// ⭐ 回读确认（⭐ 只能确认"已开启"，⛔ 确认不了数值 ⚠️）
			for (int attempt = 0; attempt < 5; attempt++)
			{
				if (wmi.TryReadCpuCustomEnabled(out bool now) && now)
				{
					confirmed = true;
					// ⭐⭐ 2026-10-16（第 3 步）：⭐ **更新本地记忆** ⇒ ⭐ 这样
					//   `MechrevoService` 的校验（⭐ `_hw.Pl1 == result`）⭐ **同一次请求就能通过** ✅
					//   ⚠️ 前提：⭐ 这是在**同一个锁/线程模型**下改的属性 ⚠️
					//      ⇒ ⭐ 属性是 `{ get; private set; }`（⭐ int，⭐ 非 volatile ⚠️）
					//      ⇒ ⭐ 与 GCU 那条支的写法一致（⭐ 那里也是直接从 MQTT 回调线程写 ✅）
					//      ⇒ ⭐ 保持同款约定，⛔ 不引入新的同步机制 ✅
					_baolongdaMemPl1 = pl1;
					_baolongdaMemPl2 = pl2;
					_baolongdaMemTempWall = wall;
					Pl1 = pl1;
					Pl2 = pl2;
					TccTarget = wall;
					Logger.WriteLine($"宝龙达 功耗墙：⭐ 已应用并确认开启 ✅（⭐ PL1={pl1}W · ⭐ PL2={pl2}W · ⭐ 温度墙={wall}℃"
						+ " —— ⚠️ 数值是**我们下发的**，⭐ 协议读不回来 ⚠️；⭐ 已写入本地记忆供校验使用 ✅）");
					return true;
				}
				Thread.Sleep(150);
			}
			Logger.WriteLine($"宝龙达 功耗墙：⚠️ 写入完成但回读未确认（⭐ 开启写成功={okOn}）⇒ 如实报告 ⚠️");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 功耗墙：⭐ 操作异常（已吞掉）：" + ex.Message);
			return true;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 风扇手动）：⭐ **意向方法** —— "⭐ 接管风扇并设两个通道的转速" ✅
	/// <para>⭐ 宝龙达协议：⭐ 功能号 20 = 手动开关（⭐ 写 `[FanType, 0/1]` ✅）·
	/// ⭐ 功能号 21 = 转速（⭐ 写 `[FanType, x100RPM]` ✅）。</para>
	/// <para>⚠️⚠️ <b>本方法是全部功能里【风险最高】的一个</b>：⭐ 它会**真正改动机器的散热行为** ⚠️
	/// ⇒ ⭐ 因此额外加了三道保险：
	/// ⭐ ① ⭐ 先调 ⭐ <see cref="BaolongdaWmi.PrepareFanManualControl" />（⭐ 参考实现明确要求：
	/// ⭐ 写风扇前先把官方控制台的手动开关**关掉**，⭐ "为避免与官方控制台冲突" ✅）；
	/// ⭐ ② ⭐ 转数夹在 ⭐ `[0, 60]`（⭐ 即 **0~6000 RPM** ✅）—— ⭐ 保守上限，⛔ 不让人手滑写爆 ✅；
	/// ⭐ ③ ⭐ `enable=false` 时 ⭐ **两个通道都写 0**（⭐ 交回固件自动控制 ✅）。</para>
	/// <para>⚠️ <b>通道归属尚未确认</b>：⭐ 按参考实现（蛟龙16Pro）⭐ `FanType=0` = ⭐ **大扇/GPU** ·
	/// ⭐ `FanType=1` = ⭐ **小扇/CPU** ⚠️ —— ⭐ 真机两组读数太接近，⭐ **还没交叉验证** ⚠️
	/// ⇒ ⭐ 所以本方法**要求调用方显式传两个通道**（⭐ 而不是替它猜 ✅），⭐ 且日志把两个通道都记全 ✅。</para>
	/// </summary>
	internal bool TrySetFanManualViaBackend(bool enable, int firstChannelUnits100Rpm, int secondChannelUnits100Rpm, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		try
		{
			// ⭐⭐ 2026-10-16（审查修复 B2）：⭐ 本方法原先 `confirmed` **只看写入调用是否没抛异常** ⚠️
			//    ⇒ ⭐ 会**高报成功** ❌ —— ⭐ 与另外三个 `Try…` 方法（⭐ 都有回读 ✅）**不一致** ✅
			//    ⇒ ⭐ 现在补上**回读校验** ✅：
			//       ⭐ ① ⭐ 写完后 ⭐ **回读功能号 20**（⭐ 手动开关状态 ✅）⇒ ⭐ 与期望一致才算 confirmed ✅
			//       ⭐ ② ⭐ 回读**读不到** ⇒ ⭐ `confirmed=false`（⭐ ⛔ 不谎报 ✅），⭐ 日志如实写明 ⚠️
			//    ⚠️ **诚实的边界**（⭐ 必须记下）：⭐ 转速**数值本身读不回来** ⚠️
			//       —— ⭐ 功能号 13 给的是 ⭐ **实际转速**（⭐ 会延迟、⭐ 且受负载影响 ⚠️），
			//       ⭐ ⛔ 不是"⭐ 我们刚设的目标值" ⇒ ⭐ 所以这里**只能确认开关** ✅，⭐ 数值只能靠写入成功 ✅
			if (!enable)
			{
				// ⭐ 交回固件：⭐ 两个通道都写 0 ⇒ ⭐ 再关手动开关 ✅（⭐ 顺序与参考实现的"恢复"一致 ✅）
				wmi.TrySetMaxFanSpeed(0, 0);
				wmi.TrySetMaxFanSpeed(1, 0);
				wmi.TrySetMaxFanSpeedSwitch(false);
				// ⭐ 回读校验：⭐ 期望"关" ✅
				bool readOk = wmi.TryReadMaxFanSpeedSwitch(out bool nowOn);
				confirmed = readOk && !nowOn;
				if (!readOk)
				{
					Logger.WriteLine("宝龙达 风扇：⚠️ 已下发「交回固件」，⭐ 但**回读不到手动开关状态** ⇒ ⭐ 如实报告未确认 ⚠️");
				}
				else if (!confirmed)
				{
					Logger.WriteLine("宝龙达 风扇：⚠️ 已下发「交回固件」，⭐ 但**回读显示手动开关仍是开的** ⇒ ⭐ 如实报告未确认 ⚠️");
				}
				else
				{
					Logger.WriteLine("宝龙达 风扇：⭐ 已交回固件自动控制并回读确认（⭐ 两通道归零 ＋ ⭐ 手动开关=关 ✅）");
				}
				return true;
			}
			// ⭐ 保险 ①：⭐ 先关官方控制台的手动开关（⭐ 参考实现的要求 ✅）
			//   ⚠️ 审查补记：⭐ 这一步的返回值原先被丢弃 ⚠️ ⇒ ⭐ 现在记进日志（⭐ 它失败不致命，⭐ 但要看得到 ✅）
			bool prepared = wmi.PrepareFanManualControl();
			int a = Math.Max(0, Math.Min(60, firstChannelUnits100Rpm));
			int b = Math.Max(0, Math.Min(60, secondChannelUnits100Rpm));
			// ⚠️ 通道归属未确认 ⇒ 日志把"我们按什么理解"写全，⭐ 便于真机对照 ✅
			bool okA = wmi.TrySetMaxFanSpeed(0, (byte)a);
			bool okB = wmi.TrySetMaxFanSpeed(1, (byte)b);
			bool okSw = wmi.TrySetMaxFanSpeedSwitch(true);
			bool writeOk = okA && okB && okSw;
			// ⭐ 回读校验：⭐ 期望"开" ✅
			bool readBackOk = wmi.TryReadMaxFanSpeedSwitch(out bool isOn);
			confirmed = readBackOk && isOn;
			if (writeOk && confirmed)
			{
				Logger.WriteLine($"宝龙达 风扇：⭐ 已接管并回读确认（⭐ 通道0={a * 100} RPM · ⭐ 通道1={b * 100} RPM · ⭐ 手动开关=开 ✅"
					+ $" · ⭐ 预先关官方开关={prepared} · ⚠️ 通道归属按参考实现理解：0=大扇/GPU · 1=小扇/CPU ⚠️ 尚未真机交叉验证 ⚠️"
					+ " · ⚠️ 转速**数值**读不回来，⭐ 只能确认开关 ⚠️）");
			}
			else if (writeOk && !readBackOk)
			{
				Logger.WriteLine($"宝龙达 风扇：⚠️ 写入完成但**回读不到手动开关状态** ⇒ ⭐ 如实报告未确认 ⚠️"
					+ $"（⭐ 通道0={a * 100} RPM · ⭐ 通道1={b * 100} RPM · ⭐ 预先关官方开关={prepared}）");
			}
			else
			{
				Logger.WriteLine($"宝龙达 风扇：⚠️ 写入不完整或回读不一致（⭐ 通道0={okA} · ⭐ 通道1={okB} · ⭐ 开关写={okSw} · ⭐ 开关回读={(readBackOk ? isOn.ToString() : "读不到")}）⇒ 如实报告 ⚠️");
			}
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 风扇：⭐ 操作异常（已吞掉）：" + ex.Message);
			return true;
		}
	}

	/// <summary>
	/// ⭐ 读一次宝龙达风扇转速（⭐ 只读 ✅）。⭐ 非宝龙达时返回 <c>false</c> ✅。
	/// <para>⭐ 两个通道按参考实现理解为：⭐ `First` = 大扇/GPU · ⭐ `Second` = 小扇/CPU ⚠️（⭐ 待验证 ✅）。</para>
	/// </summary>
	internal bool TryReadFanSpeedsViaBackend(out int firstChannelRpm, out int secondChannelRpm)
	{
		firstChannelRpm = 0;
		secondChannelRpm = 0;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		try
		{
			if (!wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds))
			{
				return false;
			}
			firstChannelRpm = (speeds.First > 0) ? speeds.First : 0;
			secondChannelRpm = (speeds.Second > 0) ? speeds.Second : 0;
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 风扇：⭐ 读取异常：" + ex.Message);
			return false;
		}
	}

	// ⭐⭐ 2026-10-16（第 3 步 · 功耗墙）：⭐ 本地记忆 + ⭐ 我们自己的范围
	//   ⚠️ 为什么必须"本地记忆"：⭐ 真机实测（⭐ 机器 C）⭐ 功能号 23 **只能读到"是否已开启"**，
	//      ⭐ **PL1 / PL2 / 温度墙的具体数值协议里读不回来** ⚠️（⭐ 这是协议限制 ✅）
	//   ⇒ ⭐ 所以界面上的当前值 ⭐ **只能是我们上次下发的值** ✅（⭐ 审查阶段要决定是否加说明小字 ⚠️）
	//   ⭐ 范围取常数（⭐ 保守）：⭐ PL1/PL2 ⭐ 5~200W · ⭐ 温度墙 ⭐ 60~105℃ ✅
	//      ⚠️ 而门面层的写入还会再夹一次（⭐ 见 `TryApplyCpuPowerLimitViaBackend` ✅）
	private const int BaolongdaPlMinWatts = 5;
	private const int BaolongdaPlMaxWatts = 200;
	private const int BaolongdaTccMinCelsius = 60;
	private const int BaolongdaTccMaxCelsius = 105;
	private int _baolongdaMemPl1;
	private int _baolongdaMemPl2;
	private int _baolongdaMemTempWall;

	/// <summary>
	/// ⭐ 把**我们自己的**功耗墙范围与本地记忆值填进那些"原本等 GCU 上报"的属性里 ✅
	/// <para>⭐ 依据：⭐ 见方案文档第 17 节 —— ⭐ 填属性即可，⛔ 不用动 UI ✅</para>
	/// </summary>
	private void ApplyBaolongdaPowerRanges()
	{
		Pl1Minimum = BaolongdaPlMinWatts;
		Pl1Maximum = BaolongdaPlMaxWatts;
		Pl2Minimum = BaolongdaPlMinWatts;
		Pl2Maximum = BaolongdaPlMaxWatts;
		TccMinimum = BaolongdaTccMinCelsius;
		TccMaximum = BaolongdaTccMaxCelsius;
		// ⭐ PL4 本机协议里没有（⭐ 参考实现的功能号表里也没这一项 ✅）⇒ ⭐ 保持 -1（⭐ 即不可调 ✅）
		// ⭐ 当前值 = 本地记忆（⭐ 0 = 还从没设过 ⇒ ⭐ 界面显示 0，⭐ 用户拖到哪就是哪 ✅）
		Pl1 = _baolongdaMemPl1;
		Pl2 = _baolongdaMemPl2;
		TccTarget = _baolongdaMemTempWall;
	}

	// ══════════════════════════════════════════════════════════════════════════
	// ⭐⭐ 2026-10-16（"全部接入"第 4 步 · 风扇**软件闭环曲线** · ⭐ 骨架）
	//
	//  ⭐⭐ 为什么是"软件闭环"（⭐ 用户 2026-10-16：「四 宝龙达是风扇曲线是吗 能不能接我们的曲线里面」）：
	//     ⭐ 我们的曲线（`FanCurveForm`）⭐ 是"⭐ **上传表给 GCU 固件**"的 ✅ ⇒ ⭐ 固件自己按温度调 ✅
	//     ⚠️ 宝龙达协议（⭐ 功能号 20/21）⭐ **只有**「⭐ 手动开关 ＋ ⭐ 固定转速」，⭐ **没有曲线表** ⚠️
	//     ⇒ ⭐ ⭐ 所以只能 ⭐ **我们的程序读温度 → ⭐ 查曲线 → ⭐ 写功能号 21** ✅
	//
	//  ⚠️⚠️ **本骨架【故意不可用】**（⭐ 硬闸）⚠️ —— ⭐ 原因只有一个：
	//     ⚠️ **风扇两通道的归属还没确认** ⚠️
	//        ⭐ 参考实现（蛟龙16Pro）说 ⭐ `FanType=0` = 大扇/**GPU** · ⭐ `1` = 小扇/**CPU** ⚠️
	//        ⭐ 但真机两组读数太接近（⭐ 3054 / 3098 ✅）⭐ **分不出来** ⚠️
	//        ⇒ ⭐ ⚠️ 搞反的后果：⭐ **CPU 风扇会按 GPU 温度转** ⚠️ ⇒ ⭐ 散热逻辑完全错 ❌
	//     ⇒ ⭐ 所以 ⭐ `FanChannelMappingConfirmed` ⭐ **恒为 false** ⇒ ⭐ `ApplyBaolongdaFanCurveStep()`
	//        ⭐ **只记一行日志、⭐ 什么都不写** ✅
	//     ⇒ ⭐ ⭐ **待办**：⭐ 用 `--fan-watch` 确认归属后 ⇒ ⭐ 把下面那个常量改成 `true` ⇒ ⭐ 闭环即生效 ✅
	//        （⭐ 这是**唯一**的开关点，⭐ 故意做成一个常量，⭐ 便于审查时一眼看到 ✅）
	// ══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// ⭐⚠️ **风扇两通道归属是否已确认** ⚠️
	/// <para>⭐ <b>必须是 false 直到真机确认</b>（⭐ 用 `--fan-watch` 在 CPU/GPU 分别加载下观察 ✅）。</para>
	/// <para>⚠️ 改成 <c>true</c> 之前请先读 <c>docs\notes\宝龙达-全部接入实施方案-20261016.md</c>
	/// 里关于通道归属的那一节 —— ⭐ **搞反会让 CPU 风扇按 GPU 温度转** ⚠️。</para>
	/// </summary>
	internal const bool FanChannelMappingConfirmed = false;

	/// <summary>
	/// ⭐ 参考实现的通道口径（⚠️ **未确认**，⭐ 等 `--fan-watch` 的结论 ✅）：
	/// ⭐ 宝龙达 ⭐ <c>FanType = 0</c> ⇒ ⭐ **大扇 / GPU** · ⭐ <c>FanType = 1</c> ⇒ ⭐ **小扇 / CPU** ⚠️
	/// </summary>
	internal const int BaolongdaFanTypeLarge = 0;

	/// <summary>⭐ 见 <see cref="BaolongdaFanTypeLarge" /> ⚠️（⭐ 未确认 ✅）。</summary>
	internal const int BaolongdaFanTypeSmall = 1;

	// ⚠️ 占空比 → `x100RPM` 需要"该通道最大转速" ⚠️
	// ⭐⭐ 2026-10-16（0.291.9.4 · 第 1 步）：⭐ 这里原来有两个「⭐ 参考 max RPM」⭐ 常量 ⚠️
	//    （⭐ BaolongdaLargeFanMaxRpm = 4400 / ⭐ BaolongdaSmallFanMaxRpm = 8200 ✅）
	//    ⚠️ 它们是**抄来的参考值**、⭐ 而且**蛟龙的这两个数没有来源** ⚠️
	//    ⇒ ⭐ ⭐ 曲线改用 **RPM** 之后 ✅（⭐ 见 FanCurveEngine.cs ✅）⭐ **不再需要 max RPM 这个分母** ✅
	//    ⇒ ⭐ ⭐ **已删除** ✅ —— ⭐ 少一个猜出来的数 ✅

	private volatile bool _baolongdaFanCurveApplied;

	/// <summary>⭐ 软件闭环当前是否已接管风扇（⭐ 用于"退出/关闭时交回固件" ✅）。</summary>
	internal bool BaolongdaFanCurveApplied => _baolongdaFanCurveApplied;

	/// <summary>⭐ 宝龙达软件闭环曲线的门面层入口（⭐ 供 UI 读设置 ✅）。数据来源 <see cref="BaolongdaFanCurveStore" /> ✅。</summary>
	internal BaolongdaFanCurveSettings LoadBaolongdaFanCurve()
	{
		return BaolongdaFanCurveStore.Load();
	}

	/// <summary>
	/// ⭐⭐ 保存软件闭环曲线（⭐ 只落盘 ＋ ⭐ 立即应用一次 ✅）。
	/// <para>⚠️ 若 <c>Enabled</c>=false ⇒ ⭐ **同时交回固件控制** ✅
	/// （⭐ 不能"关了开关却还让风扇停在我们上次写的转速上" ⚠️）。</para>
	/// </summary>
	internal bool SaveBaolongdaFanCurve(BaolongdaFanCurveSettings settings)
	{
		if (settings == null)
		{
			return false;
		}
		bool ok = BaolongdaFanCurveStore.Save(settings);
		if (!settings.Enabled)
		{
					// ⭐⭐ 2026-10-16（"回读"需求）：⭐ 启动时把"⭐ 本机哪些功能号是通的"⭐ 打**一次** ✅
		//   ⭐ 为什么不周期打：⭐ 快照里的温度/转速每 3 秒都变 ⚠️ ⇒ ⭐ 周期打就是刷屏 ❌
		//   ⭐ 这一份清单 ⭐ **正好也是诊断包要的内容**（⭐ 让机主一眼看出本机支持情况 ✅）
		try
		{
			BaolongdaStateSnapshot supportSnap = BaolongdaStateSnapshot.Capture(_baolongda?.Wmi);
			foreach (string supportLine in supportSnap.DescribeSupportList())
			{
				Logger.WriteLine("宝龙达状态支持清单：" + supportLine);
			}
			Logger.WriteLine("宝龙达状态支持清单（⭐ 说明）：⭐ 上面的 ✅ 表示**本机该功能号可读** ✅；"
				+ "⚠️ 表示读不到 ⇒ ⭐ 相关的界面开关**应当隐藏**（⛔ 免得点了没反应 ✅）");
		}
		catch (Exception exSnap)
		{
			Logger.WriteLine("宝龙达状态支持清单：⭐ 采集失败（已吞掉）：" + exSnap.Message);
		}RestoreBaolongdaFanFirmwareControl();
		}
		else
		{
			ApplyBaolongdaFanCurveStep();
		}
		return ok;
	}

	/// <summary>
	/// ⭐⭐ 软件闭环的**一步**：⭐ 读温度 ⇒ ⭐ 查曲线 ⇒ ⭐ 写功能号 21 ✅
	/// <para>⚠️⚠️ <b>未确认通道归属前，本方法【什么都不写】** ⚠️（⭐ 见
	/// <see cref="FanChannelMappingConfirmed" /> ✅）—— ⭐ 只记一行日志，⭐ 便于审查者确认闸门有效 ✅。</para>
	/// <para>⭐ 非宝龙达后端 ⇒ ⭐ 直接返回 false（⭐ GCU/同方**一行不改** ✅）。</para>
	/// </summary>
	internal bool ApplyBaolongdaFanCurveStep()
	{
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		// ⚠️⚠️ 硬闸：⭐ 通道归属没确认 ⇒ ⭐ **绝不写** ⚠️（⭐ 写错会让 CPU 风扇按 GPU 温度转 ❌）
		if (!FanChannelMappingConfirmed)
		{
			Logger.WriteLineIfChanged("baolongda-fancurve-gate",
				"宝龙达风扇曲线：⚠️ **通道归属尚未确认**（⭐ 用 --fan-watch 在 CPU/GPU 分别加载下观察 ✅）"
				+ " ⇒ ⭐ 软件闭环**已就绪但【拒绝执行】**，⛔ 一个字节都不写 ✅"
				+ "（⭐ 确认后把 MechrevoHw.FanChannelMappingConfirmed 改成 true 即生效 ✅）");
			return false;
		}
		try
		{
			BaolongdaFanCurveSettings settings = BaolongdaFanCurveStore.Load();
			if (!settings.Enabled)
			{
				return false;
			}
			// ⭐ 温度来源：⭐ 用**已有的属性**（⭐ 它们已是"⭐ LHM/NVML 优先、⭐ 厂商兜底"⭐ 的结果 ✅）
			int cpuTemp = CpuTemp;
			int gpuTemp = GpuTemp;
			if (cpuTemp <= 0 && gpuTemp <= 0)
			{
				Logger.WriteLineIfChanged("baolongda-fancurve-notemp", "宝龙达风扇曲线：⚠️ 两个温度都没有有效值 ⇒ 本轮跳过（⛔ 不瞎写风扇 ✅）");
				return false;
			}
			// ⭐⭐ 2026-10-16（第 1 步）：⭐ 曲线改 **RPM** ✅ ⇒ ⭐ 不再有「⭐ 占空比 × ⭐ max RPM ÷ 100」这一步 ✅
			//    ⚠️ 而且 ⭐ **原来那两个 max RPM（4400/8200）是抄来的参考值** ⚠️ ⇒ ⭐ 现在整个不需要它了 ✅
			FanCurvePoint[] channel0Points = FanCurveEngine.BuildPoints(settings.Channel0Temperatures, settings.Channel0Rpms);
			FanCurvePoint[] channel1Points = FanCurveEngine.BuildPoints(settings.Channel1Temperatures, settings.Channel1Rpms);
			// ⚠️⚠️ 温度来源的**配对沿用改动前**的关联（gpuTemp↔ch0 · cpuTemp↔ch1）✅
			//    ⭐ ⛔ 但**归属本身仍未真机确认** ⚠️ ⇒ ⭐ 这里刻意不写 CPU/GPU 语义 ✅
			int channel0Rpm = FanCurveEngine.ResolveRpm(channel0Points, gpuTemp, settings.MinRpm, settings.MaxRpm);
			int channel1Rpm = FanCurveEngine.ResolveRpm(channel1Points, cpuTemp, settings.MinRpm, settings.MaxRpm);
			int channel0Units = FanCurveEngine.ToUnits100Rpm(channel0Rpm);
			int channel1Units = FanCurveEngine.ToUnits100Rpm(channel1Rpm);
			// ⚠️ 通道顺序必须与 `TrySetFanManualViaBackend(enable, ch0, ch1, ...)` 的约定一致 ✅
			//    ⭐ ch0 = `FanType 0` · ⭐ ch1 = `FanType 1` ⚠️（⭐ 语义归属未确认 ✅）
			if (!TrySetFanManualViaBackend(enable: true, channel0Units, channel1Units, out bool confirmed))
			{
				return false;
			}
			_baolongdaFanCurveApplied = true;
			Logger.WriteLineIfChanged("baolongda-fancurve-step",
				$"宝龙达风扇曲线（⭐ 软件闭环 · ⭐ 单位 RPM ✅）：⭐ ch0 温度 {gpuTemp}℃ → ⭐ {channel0Rpm} RPM（⭐ 档位 {channel0Units}）"
				+ $" · ⭐ ch1 温度 {cpuTemp}℃ → ⭐ {channel1Rpm} RPM（⭐ 档位 {channel1Units}）"
				+ $" · ⭐ 确认={confirmed} ⚠️（⭐ 通道归属待真机确认 ✅）");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达风扇曲线：⭐ 单步异常（已吞掉，⛔ 不影响别处）：" + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// ⭐⭐ 把风扇**交回固件**控制（⭐ 两通道归零 ＋ ⭐ 关手动开关 ✅）。
	/// <para>⭐⭐ <b>必须在这些时机调用</b>（⭐ 否则风扇会**停在我们上次写的转速上** ⚠️）：
	/// ⭐ ① ⭐ 用户关掉软件曲线 ✅ · ⭐ ② ⭐ 程序退出 ✅ · ⭐ ③ ⭐ **程序启动时**（⭐ 清理上次残留 ✅）。</para>
	/// ⚠️ 进程被强杀时来不及调用 ⚠️ ⇒ ⭐ 所以 ③ 很重要 ✅。</para>
	/// </summary>
	internal void RestoreBaolongdaFanFirmwareControl()
	{
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null)
		{
			return;
		}
		try
		{
			// ⭐⭐ 先**读一次**手动开关 ⚠️ ⇒ ⭐ **只有它真的是"开"⭐ 才写** ✅
			//    ⭐ 理由：⭐ 这是每次启动都会走的路 ⇒ ⭐ 常见情况（⭐ 手动本就关着）⛔ **一个字节都不该写** ✅
			bool alreadyManual = false;
			try
			{
				if (wmi.TryReadMaxFanSpeedSwitch(out bool on))
				{
					alreadyManual = on;
				}
			}
			catch
			{
			}
			if (!alreadyManual)
			{
				_baolongdaFanCurveApplied = false;
				Logger.WriteLineIfChanged("baolongda-fanrestore-skip",
					"宝龙达风扇曲线：⭐ 启动自检 ⇒ ⭐ 手动风扇开关**本来就是关的** ⇒ ⛔ 不写任何东西（⭐ 交回固件已成立 ✅）");
				return;
			}
			Logger.WriteLine("宝龙达风扇曲线：⚠️ 启动自检发现**手动风扇开关是开的**（⭐ 可能是上次被强杀留下的 ⚠️）⇒ ⭐ 现在交回固件控制 ✅");
			if (TrySetFanManualViaBackend(enable: false, 0, 0, out bool confirmed))
			{
				_baolongdaFanCurveApplied = false;
				Logger.WriteLine($"宝龙达风扇曲线：⭐ 已交回固件控制（⭐ 两通道归零 ＋ ⭐ 关手动开关 · ⭐ 确认={confirmed} ✅）");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达风扇曲线：⭐ 交回固件失败（已吞掉）：" + ex.Message);
		}
	}

	/// <summary>
	/// ⭐ 在"⭐ (温度, ⭐ 占空比)"曲线上按**线性插值**求占空比（⭐ 单位 % ✅）。
	/// <para>⭐ 边界：⭐ 低于第一点 ⇒ ⭐ 取第一点；⭐ 高于最后一点 ⇒ ⭐ 取最后一点 ✅（⭐ 不外推 ✅）。</para>
	/// <para>⚠️ 曲线点**假定按温度升序** ⇒ ⭐ 这里**再排一次序**（⭐ 只排索引、⛔ 不动原数组 ✅），
	/// ⭐ 免得手改配置文件写乱了导致乱转 ⚠️。</para>
	/// </summary>
	internal static int InterpolateDuty(int[] temperatures, int[] duties, int temperature)
	{
		if (temperatures == null || duties == null
			|| temperatures.Length != duties.Length || temperatures.Length == 0)
		{
			return 0;
		}
		int n = temperatures.Length;
		int[] order = new int[n];
		for (int i = 0; i < n; i++)
		{
			order[i] = i;
		}
		Array.Sort(order, (int a, int b) => temperatures[a].CompareTo(temperatures[b]));
		if (temperature <= temperatures[order[0]])
		{
			return Math.Clamp(duties[order[0]], 0, 100);
		}
		if (temperature >= temperatures[order[n - 1]])
		{
			return Math.Clamp(duties[order[n - 1]], 0, 100);
		}
		for (int i = 0; i < n - 1; i++)
		{
			int lo = order[i];
			int hi = order[i + 1];
			int tLo = temperatures[lo];
			int tHi = temperatures[hi];
			if (temperature >= tLo && temperature <= tHi)
			{
				if (tHi == tLo)
				{
					return Math.Clamp(duties[hi], 0, 100);
				}
				int dLo = duties[lo];
				int dHi = duties[hi];
				int value = dLo + (int)Math.Round((double)(temperature - tLo) * (dHi - dLo) / (tHi - tLo));
				return Math.Clamp(value, 0, 100);
			}
		}
		return Math.Clamp(duties[order[n - 1]], 0, 100);
	}

	// ══════════════════════════════════════════════════════════════════════
	// ⭐⭐ 2026-10-16（用户要求「⭐ GPUmode接入 宝龙达机器就显示两个按钮就行」）：
	//     ⭐ 显卡模式（⭐ 宝龙达 WMI 功能号 9 ✅）的**意向方法** ✅
	//     ⭐ 取值只有两个（⭐ 参考实现 `GpuMode` 枚举 ✅）：⭐ 0 = 混合 · ⭐ 1 = 独显直连 ✅
	//     ⚠️ 功能号 9 **真机未验证** ⚠️ ⇒ ⭐ 门面层与协议层**都先试读再写**（⭐ 读不到就拒绝 ✅）
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>
	/// ⭐ 读一次显卡模式（⭐ 只读 ✅）。⭐ 非宝龙达时返回 <c>false</c> ⇒ ⭐ 调用方走原路 ✅。
	/// <para>⭐ <paramref name="mode" />：⭐ 0 = 混合模式 · ⭐ 1 = 独显直连 ✅。</para>
	/// </summary>
	internal bool TryReadGpuModeViaBackend(out int mode)
	{
		mode = -1;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		try
		{
			return wmi.TryReadGpuMode(out mode);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 显卡模式：⭐ 读取异常：" + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 修「显卡模式切换不行」⚠️ 真机症状：弹窗
	/// 「未能读取当前显卡模式。为避免依据旧状态切换，请稍后重试。」❌）：
	/// ⭐ **把 WMI 功能号 9 读到的显卡模式，同步进 <see cref="GpuMode" />** ✅
	///
	/// <para>⭐⭐ <b>为什么必须这么做（根因链，已逐行核实 ✅）</b>：
	/// ⭐ ① 界面点按钮后走 <c>SettingsForm.SwitchGpuModeFromUi</c> ⇒ 先调
	/// <c>RefreshGpuModeUiStateAsync</c> ⇒ <c>MechrevoService.RefreshGpuModeStatus</c> ✅；
	/// ⭐ ② 而 <c>RefreshGpuModeStatus</c>（约 <c>MechrevoService.cs:2428</c>）**第一句就是**
	/// <c>if (!_hw.IsGcuConnected) return GpuModeStatusReadback.Unavailable;</c> ⚠️；
	/// ⭐ ③ ⇒ ⭐ 宝龙达**没有 GCU** ⇒ 恒 <c>Unavailable</c> ⇒ ⭐ 界面判"读不到当前模式"
	/// ⇒ ⭐ ⭐ **弹窗并【直接 return】，切换被挡住** ❌；
	/// ⭐ ④ ⚠️ 而**我们那条读法本身是通的** ✅（真机日志：⭐「宝龙达状态：… 显卡=独显直连(1) …」✅）
	/// ⇒ ⭐ ⭐ **只是从没写进 <see cref="GpuMode" />** ⚠️。</para>
	///
	/// <para>⭐ <b>模式值映射</b>（⭐ 与 <c>GPUModeControl.SetGPUMode</c> 的宝龙达分派**同一口径** ✅）：
	/// ⭐ 宝龙达 <c>0 = 混合</c> ⇒ ⭐ 本属性 <c>1</c> · ⭐ 宝龙达 <c>1 = 独显直连</c> ⇒ ⭐ 本属性 <c>2</c>
	/// ⭐（⭐ 本属性口径：⭐ <c>0 = 核显(Eco)</c> · ⭐ <c>1 = 混合(标准)</c> · ⭐ <c>2 = 独显直连(极致)</c> ✅）</para>
	///
	/// <para>⚠️ 非宝龙达 / ⭐ 未知取值 ⇒ ⭐ **返回 false 且【不动】 <see cref="GpuMode" />** ✅
	/// —— ⭐ 宝龙达协议只有两个取值 ⚠️ ⇒ ⭐ 读到别的值说明回应不对 ✅ ⇒
	/// ⭐ ⭐ **绝不拿旧值或猜测值冒充**（⭐ 那正是这个弹窗要防的事 ✅）。</para>
	/// <para>⭐ 带**重试**（⭐ 最多 3 次 · ⭐ 60 ms）：⭐ 真机上偶发读失败时不该一次就报错 ⚠️。</para>
	/// </summary>
	internal bool TrySyncGpuModeViaBackend()
	{
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		for (int attempt = 0; attempt < 3; attempt++)
		{
			try
			{
				if (wmi.TryReadGpuMode(out int raw))
				{
					int mapped;
					if (raw == BaolongdaWmi.GpuModeHybrid)
					{
						mapped = 1; // ⭐ 混合
					}
					else if (raw == BaolongdaWmi.GpuModeDiscrete)
					{
						mapped = 2; // ⭐ 独显直连
					}
					else
					{
						Logger.WriteLine($"宝龙达 显卡模式：⭐ 功能号 9 读到未知取值 raw={raw} ⇒ ⭐ 不认（⛔ 不用旧值冒充 ✅）");
						return false;
					}
					if (GpuMode != mapped)
					{
						GpuMode = mapped;
						Logger.WriteLine($"宝龙达 显卡模式：⭐ 回读成功 ⇒ raw={raw} ⇒ 界面口径={mapped}"
							+ "（⭐ 1=混合 · ⭐ 2=独显直连 ✅）⇒ ⭐ 已同步进 GpuMode ✅");
					}
					return true;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("宝龙达 显卡模式：⭐ 同步异常：" + ex.Message);
				return false;
			}
			if (attempt < 2)
			{
				Thread.Sleep(60);
			}
		}
		Logger.WriteLine("宝龙达 显卡模式：⭐ 功能号 9 重试 3 次仍读不到 ⇒ ⭐ 本次不同步（⚠️ 界面会如实报「读不到」✅）");
		return false;
	}

	/// <summary>
	/// ⭐⭐ **意向方法**：⭐ 切显卡模式（✅ 含**写后回读确认**，⭐ 最多 5 次 · ⭐ 150 ms，⭐ 与档位/LOGO 同款 ✅）
	/// <para>⭐ <paramref name="mode" />：⭐ 0 = 混合 · ⭐ 1 = 独显直连 ✅；⭐ 其它值**直接拒绝** ✅。</para>
	/// <para>⚠️ 非宝龙达 ⇒ ⭐ **返回 false** ⇒ ⭐ 调用方**照原路走 GCU**（⭐ 那条路一字不改 ✅）。</para>
	/// <para>⚠️ 日志**如实区分**：⭐ 已确认 ✅ / ⭐ 写入完成但回读不一致 ⚠️ / ⭐ 回读不到 ⚠️ ✅。</para>
	/// </summary>
	internal bool TrySetGpuModeViaBackend(int mode, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		if (mode != BaolongdaWmi.GpuModeHybrid && mode != BaolongdaWmi.GpuModeDiscrete)
		{
			Logger.WriteLine($"宝龙达 显卡模式：⭐ 拒绝不支持的值 {mode}（⭐ 只接受 0=混合 / 1=独显直连 ✅）");
			return true; // ⭐ 已接管：⭐ 不让调用方再去发 GCU ✅
		}
		try
		{
			if (!wmi.TrySetGpuMode(mode))
			{
				Logger.WriteLine("宝龙达 显卡模式：⚠️ 写入未执行（⭐ 详见协议层日志 —— ⭐ 多半是「读不到当前模式」⭐ 而拒绝盲写 ⚠️）");
				return true;
			}
			for (int attempt = 0; attempt < 5; attempt++)
			{
				if (wmi.TryReadGpuMode(out int now) && now == mode)
				{
					confirmed = true;
					Logger.WriteLine($"宝龙达 显卡模式：⭐ 已切到 {(mode == BaolongdaWmi.GpuModeDiscrete ? "独显直连" : "混合模式")}"
						+ $"（⭐ {mode}）并回读确认 ✅（⭐ 第 {attempt + 1} 次）");
					return true;
				}
				Thread.Sleep(150);
			}
			Logger.WriteLine($"宝龙达 显卡模式：⚠️ 写入完成但 {5 * 150} ms 内**回读不一致或读不到** ⇒ ⭐ 如实报告未确认 ⚠️");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 显卡模式：⭐ 操作异常（已吞掉，⛔ 不影响别处）：" + ex.Message);
			return true;
		}
	}

	/// <summary>
	/// 把本会话切到**宝龙达模具后端**（⭐ 蛟龙16Pro 等）。
	/// <para>⭐ 与 <see cref="ApplyLegacyBackendAsync" /> 是**同一套路**（⭐ 替换能力位 ⇒ 起 3 s 定时刷新 ⇒ 立刻读一次 ⇒ 宣布就绪 ✅），
	/// 差别只在 ⭐ **能力位更宽**（⭐ 宝龙达有风扇/CPU温度/LOGO灯/功耗墙 ✅）与 ⭐ <c>ProjectId</c> ✅。</para>
	/// <para>⚠️ **能力位刻意只开"已验证 + 已接线"的**（⭐ 沿用本树"⛔ 不假装可写"的约定 ✅）：
	/// 本轮**只接了性能档** ⇒ ⭐ 只开 <c>ProfileAvailable</c> / <c>TurboMode</c> ✅；
	/// ⭐ 风扇 / ⭐ 灯效 / ⭐ 功耗墙等**等各自的下发路径接完再逐个打开** ⚠️（⛔ 现在打开只会让 UI 走到没接的代码上 ✅）。</para>
	/// </summary>
	/// <summary>
	/// ⭐⭐ 2026-10-16：⭐ **宝龙达的「Fn 键锁」写入**（⭐ 功能号 11 ✅）。
	/// <para>⭐ 依据：⭐ 真机诊断包已**回读成功**（⭐ 状态行 ⭐ `Fn锁=0` ✅）
	/// ⇒ ⭐ ⭐ **硬件确定支持** ⇒ ⭐ 接写入**不违反铁律** ✅。</para>
	/// <para>⚠️ 语义如实说明：⭐ 功能号 11/12 的 **0/1 方向我们没有真机定论** ⚠️
	/// ⇒ ⭐ 所以做法是 ⭐ **"⭐ 写进去、⭐ 再读出来、⭐ 两边一致才算确认"** ✅
	/// —— ⭐ 若方向恰好相反，⭐ 也只会表现为"⭐ 开关状态反了"⚠️，⛔ **不会写到别的寄存器上** ✅。
	/// 📌 待真机确认方向后，⭐ 只需在此处翻转一次 ✅。</para>
	/// <para>⭐ 返回 <c>false</c> ⇒ ⭐ 不归它管（⭐ 非宝龙达 ⭐ **恒 false**）⇒ ⭐ 调用方走原路 ✅。</para>
	/// </summary>
	internal bool TrySetFnLockViaBackend(bool on, out bool confirmed)
	{
		return TrySetBaolongdaToggleViaBackend(BaolongdaWmi.Function.FnLock, "Fn键锁", on, out confirmed);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16：⭐ **宝龙达的「触摸板」写入**（⭐ 功能号 12 ✅）。
	/// <para>⭐ 依据：⭐ 真机诊断包已**回读成功**（⭐ 状态行 ⭐ `触摸板=1` ✅）⇒ ⭐ **硬件确定支持** ✅。</para>
	/// <para>⭐⭐ 2026-10-16（⭐ 机主反馈「⭐ <b>触摸板的开关反了在宝龙达机型上</b>」✅）：⭐ <b>极性已定论 ⇒ 取反</b> ✅。
	/// 见 <see cref="BaolongdaToggleInverted" /> 的推导 —— ⭐ 功能号 12 的名字是 <c>TPLock</c>（⭐ 一个<b>锁</b> ⚠️），
	/// ⭐ 而界面 <c>touchpad</c> 的 checked 语义是「⭐ <b>触摸板启用</b>」（⭐ 与 GCU 侧读的 <c>TochpadEnable</c> 一致 ✅）
	/// ⇒ ⭐ <b>两者相反</b> ✅。</para>
	/// <para>⚠️ 本方法与读侧 <see cref="RefreshBaolongdaSwitchStates" /> <b>共用同一个极性来源</b>
	/// （<see cref="BaolongdaToggleInverted" /> ✅）⇒ ⛔ <b>不会出现"⭐ 显示说开、⭐ 实际是关"⭐ 那种两侧口径打架的状态</b> ✅。</para>
	/// </summary>
	internal bool TrySetTouchpadViaBackend(bool on, out bool confirmed)
	{
		return TrySetBaolongdaToggleViaBackend(BaolongdaWmi.Function.TPLock, "触摸板", on, out confirmed);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · OSD 提示）：⭐ 把「快捷开关 ⇒ OSD」这一步**分派给宝龙达本地实现** ✅。
	/// <para>⭐ <b>机主原话</b>：「⭐ OSD显示开关不可用（⭐ <i>LM好像有自己的OSD部分</i> ✅）」——
	/// ⭐ 后半句就是**修法**：⭐ LM 自己本来就有一套 OSD（⭐ <c>OSDNativeForm</c> ⇒ ⭐ <c>ToastForm</c> /
	/// <c>HardwareOverlay</c> ✅）⇒ ⭐ **宝龙达下让这个开关去管【LM 自己的 OSD】** ✅。</para>
	/// <para>⚠️ <b>为什么原来不可用</b>：⭐ 该开关原语义是「⭐ 隐藏**厂商** OSD」（⭐ 下发
	/// <c>OSD_HIDDEN_ON</c> / ⭐ <c>OSD_HIDDEN_OFF</c> ✅）⭐ **只能走 GCU 的 MQTT** ❌ ——
	/// ⭐ 宝龙达没有 GCU ⇒ ⭐ 恒失败 ⇒ ⭐ <b>点了没反应</b> ⚠️（⭐ 而它又被 <c>StaticQuick</c> 判为可点 ⚠️
	/// ⇒ ⭐ 正好违反「⭐ 能点的就必须有反应」✅）。</para>
	/// <para>⭐ <b>语义保持一致</b>（⭐ ⛔ 不翻转 ✅）：⭐ <paramref name="on" /> = <c>true</c> ⇒ ⭐ **显示 OSD**
	/// （⭐ 对应原来的 ⭐ <c>OSD_HIDDEN_OFF</c> ✅）· ⭐ <c>false</c> ⇒ ⭐ **隐藏 OSD**
	/// （⭐ 对应 ⭐ <c>OSD_HIDDEN_ON</c> ✅）—— ⭐ 与 <see cref="QuickSwitches" /> 里
	/// <c>"osd"</c> 的既有口径**逐位一致** ✅。</para>
	/// <para>⚠️ <b>门控只用板号</b>（<see cref="BaolongdaBackend.IsBaolongdaModelWhitelisted" /> ✅）：
	/// ⭐ 因为本功能**纯本地**（⭐ 不碰 WMI / ⭐ 不碰 EC / ⭐ 不写配置以外任何状态 ✅）⇒
	/// ⭐ 即使「⭐ 官方控制台被卸载 ⇒ ⭐ WMI 类消失」⚠️ 也照样可用 ✅ —— ⭐ 比"看 GCU 在不在"更稳 ✅。</para>
	/// <para>⭐ 其他后端：⭐ <b>返回 <c>false</c></b> ⇒ ⭐ 调用方**照原样走 GCU** ✅（⭐ 逐位不变 ✅）。</para>
	/// </summary>
	/// <param name="on">⭐ <c>true</c> = 显示 LM 的 OSD；⭐ <c>false</c> = 隐藏。</param>
	/// <param name="confirmed">⭐ 宝龙达路径下：⭐ 回读配置确认后的实际状态 ✅。</param>
	/// <returns>⭐ <c>true</c> = 本次由宝龙达处理完了（⭐ 调用方⛔ 不要再发 GCU ✅）；⭐ <c>false</c> = 不归本方法管 ✅。</returns>
	internal bool TrySetOsdViaBackend(bool on, out bool confirmed)
	{
		confirmed = false;
		// ⭐ 只认板号（⭐ 单一事实来源 ✅）—— ⭐ 纯本地功能 ⇒ ⭐ 不要求 WMI/GCU 可用 ✅
		if (!BaolongdaBackend.IsBaolongdaModelWhitelisted(out _))
		{
			return false; // ⭐ 不是宝龙达 ⇒ 交给原来的 GCU 路径 ✅
		}
		try
		{
			// ⭐ 口径：⭐ `osd_hidden=1` ⇒ ⭐ 隐藏 OSD ✅ · ⭐ `=0` ⇒ ⭐ 显示 OSD ✅
			//   ⇒ ⭐ 开关 on=true（⭐ 显示）⇒ ⭐ 写 0 ✅
			AppConfig.Set("osd_hidden", on ? 0 : 1);
			// ⭐ 立刻同步进 `QuickSwitches`（⭐ 界面刷新读的就是它 ✅，见
			//   `SettingsForm` 的 `[QS-refresh]` 段 ✅）—— ⭐ 否则要等 3 秒周期同步 ⚠️
			//   ⇒ ⭐ 那 3 秒里界面会显示"⭐ 旧值"⚠️ ⇒ ⭐ 看起来像"⭐ 点了没反应" ❌（⭐ 正是本 bug 的观感 ✅）
			QuickSwitches["osd"] = on;
			// ⭐ 回读确认（⭐ 与其它分派同款纪律：⭐ 写进去 ≠ 生效 ✅）
			if (AppConfig.Get("osd_hidden", -1) == (on ? 0 : 1))
			{
				confirmed = true;
				Logger.WriteError("⭐ 宝龙达 OSD 提示：⭐ 已设为「" + (on ? "显示" : "隐藏") + "」并回读确认 ✅"
					+ "（⭐ 本地方案：⭐ 控 LM 自己的 OSD，⛔ 未发任何 MQTT ✅）");
				return true;
			}
			Logger.WriteError("⭐ 宝龙达 OSD 提示：⚠️ 已写入但**回读不一致**（⭐ 未确认 ⚠️）");
			return true; // ⭐ 已接管（⭐ 只是没确认 ✅）⇒ ⭐ 调用方仍不该再发 GCU ✅
		}
		catch (Exception ex)
		{
			Logger.WriteError("⭐ 宝龙达 OSD 提示：⚠️ 设置抛错 ⇒ ⭐ 本次未生效 ⚠️（" + ex.Message + "）");
			return true;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16：⭐ 宝龙达「单字节开关」的**极性**（⭐ <b>读侧写侧共用的唯一来源</b> ✅）。
	/// <para>⭐ 返回 <c>true</c> ⇒ ⭐ <b>寄存器语义与界面语义相反</b> ⇒ ⭐ 需要取反 ✅。</para>
	/// <para>⭐⭐ <b>功能号 12（TPLock · 触摸板）＝ 取反</b> ✅ —— 依据：
	/// ⭐ 该功能号的名字就是 <c>TPLock</c>（⭐ 锁 ⚠️）；⭐ 参考实现（⭐ 斗战者 <c>DOUZHANZHE-Control</c> ✅）
	/// 把它叫 <c>TouchpadLock</c>，⭐ 且 ⭐ <c>SetTouchpadLock(bool locked)</c> 写
	/// <c>locked ? 1 : 0</c> ✅ · ⭐ <c>GetTouchpadLock() == 1</c> ⇒ ⭐ <c>TouchpadLocked</c> ✅
	/// ⇒ ⭐ ⭐ <b>写 1 = 锁定 = 触摸板【禁用】</b> ⚠️。</para>
	/// <para>⚠️ 而界面 <c>QuickSwitches["touchpad"]</c> 的 <c>true</c> 表示
	/// 「⭐ <b>触摸板可用</b>」✅ —— ⭐ 依据：⭐ GCU 侧读的是 <c>TochpadEnable</c>
	/// （⭐ 见 <c>OnSettingsDeviceSwitchItemStatus</c> ✅），⭐ 写的是 ⭐ <c>TOUCHPAD_ON</c> / ⭐ <c>TOUCHPAD_OFF</c>
	/// （⭐ 见 <c>SwitchQuick</c> 的映射 ✅）⇒ ⭐ <b>语义 = 启用</b> ✅。</para>
	/// <para>⇒ ⭐ ⭐ <b>两者相反 ⇒ ⭐ 必须取反</b> ✅ —— ⚠️ 而改动前读侧
	/// （⭐ <c>SyncBaolongdaQuickSwitch</c> 的 <c>!= 0</c> ✅）与写侧（⭐ <c>on ? 1 : 0</c> ✅）
	/// <b>都按「1 = 开」处理</b> ⚠️ ⇒ ⭐ 两者【一致地反了】❌ ⇒ ⭐ 表现为"⭐ 开关能拨、⭐ 但做的是反事"✅
	/// —— ⭐ 正是机主报的现象 ✅。</para>
	/// <para>⚠️ <b>Fn 锁（功能号 11）真机【未定论】</b> ⚠️ ⇒ ⭐ 保持直通、⛔ <b>不取反</b> ✅
	/// （⭐ 机主只反馈了触摸板 ✅ · ⭐ 两者共用本方法 ⇒ ⭐ 所以极性必须按功能号区分、⭐ 不能写在共用段里 ✅）。</para>
	/// </summary>
	private static bool BaolongdaToggleInverted(BaolongdaWmi.Function function)
	{
		// ⭐ 只认触摸板（TPLock）—— ⭐ 新增功能号时在这里逐项登记，⛔ 别改成"默认取反" ✅
		return function == BaolongdaWmi.Function.TPLock;
	}

	/// <summary>
	/// ⭐ 宝龙达"⭐ 单字节开关"⭐ 的**共同实现**（⭐ Fn 键锁 11 / ⭐ 触摸板锁 12 ✅）。
	/// <para>⭐ 流程：⭐ ① 先读当前值（⭐ 读不到 ⇒ ⭐ **拒绝盲写** ✅）
	/// ⇒ ⭐ ② 已等于目标 ⇒ ⭐ 直接算确认（⭐ ⛔ 不白写一次 ✅）
	/// ⇒ ⭐ ③ 写 1/0 ⇒ ⭐ ④ 最多 5 次回读确认（⭐ 与档位/显卡模式同一套节奏 ✅）。</para>
	/// <para>⚠️ 日志**如实区分**三类：⭐ 已确认 ✅ / ⭐ 写入完成但回读不一致 ⚠️ / ⭐ 回读不到 ⚠️ ✅。</para>
	/// </summary>
	private bool TrySetBaolongdaToggleViaBackend(BaolongdaWmi.Function function, string label, bool on, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false; // ⭐ 非宝龙达 ⇒ ⭐ 交回原路 ✅
		}
		try
		{
			// ⭐⭐ 极性（⭐ 唯一来源：BaolongdaToggleInverted ✅）—— ⭐ <paramref name="on" /> 是**界面语义**
			//   （⭐ checked = 开 ✅）⇒ ⭐ 需要取反的功能号在这里翻成**寄存器语义**再写 ✅
			//   ⚠️ 读侧走的是**同一个方法** ⇒ ⭐ 两边口径必然一致 ✅
			//      （⛔ 不会出现"⭐ 显示说开、⭐ 实际是关"⭐ 那种更糟的状态 ✅）
			bool inverted = BaolongdaToggleInverted(function);
			byte target = (byte)((inverted ? !on : on) ? 1 : 0);
			// ⭐ ① 写前先读（⭐ 也是"⭐ 这个功能号真的存在吗"⭐ 的证据 ✅）
			if (!wmi.TryReadByteChecked(function, out byte before))
			{
				Logger.WriteError($"宝龙达 {label}：⚠️ 写前**读不到**当前值 ⇒ ⭐ **拒绝盲写**（⭐ 未下发任何命令 ✅）");
				return true; // ⭐ 已接管：⭐ 不让调用方再去发 GCU ✅
			}
			if (before == target)
			{
				confirmed = true;
				Logger.WriteLine($"宝龙达 {label}：⭐ 当前寄存器值已是 {target}（⭐ 界面={on}"
					+ $" · ⭐ 极性{(inverted ? "相反(已取反)" : "相同")} ✅）⇒ ⛔ 不必写（⭐ 直接算确认 ✅）");
				return true;
			}
			// ⭐ ③ 写
			if (!wmi.TryWriteByte(function, target))
			{
				Logger.WriteError($"宝龙达 {label}：⚠️ **写入未执行**（⭐ 详见协议层日志 ✅）");
				return true;
			}
			// ⭐ ④ 回读确认（⭐ 5 × 150 ms，⭐ 与显卡模式同节奏 ✅）
			for (int attempt = 0; attempt < 5; attempt++)
			{
				if (wmi.TryReadByteChecked(function, out byte now) && now == target)
				{
					confirmed = true;
					Logger.WriteLine($"宝龙达 {label}：⭐ 已写寄存器值 {target}（⭐ 界面={on}"
						+ $" · ⭐ 极性{(inverted ? "相反(已取反)" : "相同")} ✅）并回读确认 ✅（⭐ 第 {attempt + 1} 次）");
					return true;
				}
				Thread.Sleep(150);
			}
			Logger.WriteError($"宝龙达 {label}：⚠️ **写入完成但 750 ms 内回读不一致或读不到** ⇒ ⭐ 如实报告未确认 ⚠️"
				+ $"（⭐ 目标寄存器={target} · ⭐ 界面={on} · ⭐ 极性{(inverted ? "相反(已取反)" : "相同")} · ⭐ 写前={before} ✅）");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteError($"宝龙达 {label}：⭐ 操作异常（已吞掉，⛔ 不影响别处）：" + ex.Message);
			return true;
		}
	}

	private async Task ApplyBaolongdaBackendAsync(BaolongdaBackend backend)
	{
		_activeBackend = MechrevoBackendKind.Baolongda;
		// ⭐⭐ 2026-10-16：⭐ 能力位**只认一个来源** —— <see cref="BaolongdaBackend.BuildCapabilities" /> ✅
		//    ⚠️ 起因：⭐ 我先前在两个地方各写了一份（⭐ 这里 ＋ ⭐ `BuildCapabilities`）⇒ ⭐ **必然有一天只改一处** ⚠️
		//    ⇒ ⭐ 现在统一走 `BuildCapabilities()` ✅（⭐ 与 GCU 那条支的风格一致：⭐
		//      ⭐ `RefreshDeviceCapabilities` 也是从 `MechrevoDeviceCapabilities` 一处产出 ✅）
		Capabilities = backend.BuildCapabilities();
		// ⭐⭐ 2026-10-16（"全部接入"第 3 步 · 功耗墙 · ⭐ 方案文档第 17 节）：
		//   ⭐ **关键洞察**：⭐ 界面上的滑块读的是**已有属性**
		//      （⭐ `Pl1Minimum`/`Pl1Maximum`/`Pl1` ＋ ⭐ PL2 / Tcc 那一组 ✅）
		//   ⭐ 而它们默认 **-1**（⭐ 那是等 GCU 上报的 ⚠️）⇒ ⭐ `Pl1Adjustable` = false ⇒ ⭐ **滑块不能动** ❌
		//   ⇒ ⭐ ⭐ **只要在这里把它们填上【我们自己的范围】＋【本地记忆的当前值】**：
		//      ⭐ ① ⭐ `Pl1Adjustable` **自动变 true** ⇒ ⭐ 滑块能动 ✅
		//      ⭐ ② ⭐ 范围就是我们给的 ✅
		//      ⭐ ③ ⭐ 校验（⭐ `_hw.Pl1 == result`）**自动成立** —— ⭐ 因为 `Pl1` 现在是**我们自己记的** ✅
		//      ⭐ ④ ⭐ ⛔ **`CustomModeForm` 一行都不用改** ✅✅（⭐ 风险从"改 UI 模型"降到"填几个属性" ✅）
		//   ⚠️ 诚实性：⭐ 协议**读不回**这些数值 ⇒ ⭐ 界面显示的是"⭐ 上次下发的值" ⚠️
		//      ⇒ ⭐ 是否加一行说明小字 ⭐ **留待审查阶段决定**（⭐ 方案 17.3 ✅）
		ApplyBaolongdaPowerRanges();
		// ⭐⭐ 2026-10-16（第 4 步 · 风扇软件闭环）：⭐ **启动自检**（⭐ 交回固件 ✅）
		//   ⚠️ 为什么在**启动**就要做：⭐ 上次若是**被强杀**（⭐ 任务管理器 / ⭐ 崩溃 ✅）
		//      ⇒ ⭐ 风扇会**停在我们上次写的转速上** ⚠️（⭐ 因为"手动开关"是写在 EC 里的、⭐ 会留着 ✅）
		//   ⭐ 本方法**先读再决定**：⭐ 手动开关本来就关着 ⇒ ⛔ **一个字节都不写** ✅
				// ⭐⭐ 2026-10-16（"回读"需求）：⭐ 启动时把"⭐ 本机哪些功能号是通的"⭐ 打**一次** ✅
		//   ⭐ 为什么不周期打：⭐ 快照里的温度/转速每 3 秒都变 ⚠️ ⇒ ⭐ 周期打就是刷屏 ❌
		//   ⭐ 这一份清单 ⭐ **正好也是诊断包要的内容**（⭐ 让机主一眼看出本机支持情况 ✅）
		try
		{
			BaolongdaStateSnapshot supportSnap = BaolongdaStateSnapshot.Capture(_baolongda?.Wmi);
			foreach (string supportLine in supportSnap.DescribeSupportList())
			{
				Logger.WriteLine("宝龙达状态支持清单：" + supportLine);
			}
			Logger.WriteLine("宝龙达状态支持清单（⭐ 说明）：⭐ 上面的 ✅ 表示**本机该功能号可读** ✅；"
				+ "⚠️ 表示读不到 ⇒ ⭐ 相关的界面开关**应当隐藏**（⛔ 免得点了没反应 ✅）");
		}
		catch (Exception exSnap)
		{
			Logger.WriteLine("宝龙达状态支持清单：⭐ 采集失败（已吞掉）：" + exSnap.Message);
		}RestoreBaolongdaFanFirmwareControl();
		Logger.WriteLine($"Hardware backend: Baolongda ({backend.Wmi.InstancePath});"
			+ $" 功耗墙范围 PL1/PL2=[{Pl1Minimum},{Pl1Maximum}]W · 温度墙=[{TccMinimum},{TccMaximum}]℃（⭐ 本地记忆 PL1={Pl1} PL2={Pl2} 温度墙={TccTarget} ⚠️ 非从机器读回）");
		Logger.WriteLine($"Hardware backend: Baolongda ({backend.Wmi.InstancePath}); built-in modes only (本轮)."
			+ $" fans={backend.Wmi.FanCount} modes={backend.Wmi.ModeCount}");
		// ⭐⭐ 2026-10-16（"接传感器"）：⭐ 同一个 3 s 定时器里**顺便刷一遍传感器** ✅
		//    ⭐ 读的是 ⭐ 风扇转速（功能号 13）＋ ⭐ CPU 温度（功能号 22，⭐ 仅作**兜底** ✅）
		//    ⚠️ 温度**优先用 LM 自带的 LHM**（⭐ 用户要求 ✅）⇒ ⭐ 本方法只把宝龙达的值放进
		//       `BaolongdaCpuTemp`，⭐ 由展示层决定用哪个 ✅
		_legacyTimer = new Timer(delegate
		{
			_ = RefreshLegacyModeAsync();
			RefreshBaolongdaSensors(); RefreshBaolongdaSwitchStates();
		}, null, LegacyRefreshIntervalMs, LegacyRefreshIntervalMs);
		await RefreshLegacyModeAsync().ConfigureAwait(continueOnCapturedContext: false);
		// ⭐ 立刻先读一次传感器（⭐ 免得界面上风扇/温度空着等 3 秒 ✅）
		RefreshBaolongdaSensors(); RefreshBaolongdaSwitchStates();
		RaiseIsolated(CapabilitiesChanged, "CapabilitiesChanged");
		NotifyConnectionReady();
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达适配 · "接传感器"）：⭐ 宝龙达侧的**传感器兜底值**。
	/// <para>⭐ <b>CPU 温度</b>（⭐ 功能号 22）：⭐ ⚠️ **只在 LM 自带的 LibreHardwareMonitor 读不到时才用** ✅
	/// —— ⭐ 依据用户 2026-10-16 原话「⭐ 接 传感器如果 LM 自带的那个 dll 能读温度**优先用 LM 的传感器**，
	/// ⭐ 不能的再接」✅。⭐ 0 = 本次没读到（⭐ 调用方据此回退 ✅）。</para>
	/// <para>⭐ <b>风扇转速</b>不在这里暴露 —— ⭐ 它直接写进 <see cref="CpuFanRpm" /> /
	/// <see cref="GpuFanRpm" />（⭐ 那两个属性本来就是界面风扇显示的来源 ✅），
	/// ⭐ 因为 ⭐ **LHM 读不到风扇**（⭐ 实测：⭐ `--sensor-dump` 风扇数 = 0 ✅）⇒ ⭐ 没有竞争关系 ✅。</para>
	/// </summary>
	internal int BaolongdaCpuTemp { get; private set; }

	/// <summary>
	/// ⭐ 刷一次宝龙达传感器（⭐ 风扇转速 ＋ ⭐ CPU 温度兜底 ✅）。
	/// <para>⭐ 全程**只读**（⭐ 只用 Get=250 ✅）；⭐ 失败只记一行日志（⭐ 3 s 定时器上不许外抛 ✅）。</para>
	/// <para>⚠️ <b>通道归属</b>按参考实现（⭐ 蛟龙16Pro `JiaolongControl`）：
	/// ⭐ 功能号 13 的 <c>OutData[4..5]</c> = ⭐ **GPU 扇** · ⭐ <c>[6..7]</c> = ⭐ **CPU 扇** ⚠️
	/// —— ⭐ 这一条**只来自参考实现、⛔ 还没真机交叉验证** ⚠️（⭐ 真机两组读数太接近，⭐ 分不出来 ✅）
	/// ⇒ ⭐ 记在这里，⭐ 等以后加负载对比就能确认 ✅。</para>
	/// </summary>
	/// <summary>⭐ 最近一次回读的状态快照（⭐ `null` = 还没读过 ✅）。</summary>
	internal BaolongdaStateSnapshot BaolongdaState { get; private set; }

	/// <summary>
	/// ⭐⭐ 2026-10-16（⭐ 用户「⭐ 回读功能没有我们需要做的」）：⭐ **周期性状态回读** ✅
	/// <para>⭐⭐ 解决的缺口：⭐ 宝龙达的开关（⭐ Fn 锁 · ⭐ 触摸板锁 · ⭐ LOGO 灯 · ⭐ 键盘灯 · ⭐ 风扇手动 ⚠️）
	/// ⭐ **只有主动读才知道** ⚠️ ⇒ ⭐ 用户自己按键盘改了状态，⭐ 我们**不会知道** ❌
	/// ⇒ ⭐ 界面显示会**过时** ❌。⭐ 本方法挂在**已有的 3 秒定时器**上，⭐ 把真实状态同步回来 ✅。</para>
	/// <para>⭐ <b>三条原则</b>：⭐ ① ⭐ **只发 Get(=250)** ✅（⛔ 一个 Set 都不发 ✅）·
	/// ⭐ ② ⭐ 走 ⭐ `TryReadByteChecked`（⭐ 带 `OutData[3]` 回显校验 ✅）·
	/// ⭐ ③ ⭐ **只在读成功时才覆盖**（⭐ 读失败 ⛔ **不把已有值刷成 false** ⚠️ —— ⭐ 那会把真状态抹掉 ✅）。</para>
	/// <para>⭐ 非宝龙达 ⇒ ⭐ 直接返回（⭐ `_baolongda` 为 `null` ✅）⇒ ⭐ GCU/同方**一行不改** ✅。</para>
	/// </summary>
	private void RefreshBaolongdaSwitchStates()
	{
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return;
		}
		try
		{
			BaolongdaStateSnapshot snap = BaolongdaStateSnapshot.Capture(wmi);
			BaolongdaState = snap;
			// ⭐⭐ 同步进 `QuickSwitches`（⭐ 界面读的就是它 ✅）
			//   ⚠️ 映射只挑**语义确定**的三个 ✅ —— ⭐ 其余留在快照里（⭐ 诊断包会用 ✅），
			//      ⛔ 不乱映射（⭐ 例如功能号 10 是"键盘灯状态"，
			//         ⚠️ **不等于**实体灯带 `lightbar` ⚠️ ⇒ ⭐ 映射过去就是错的 ❌）
			SyncBaolongdaQuickSwitch("logolight", snap.LogoLight);
			// ⭐⭐ 2026-10-16（⭐ 机主反馈「⭐ 触摸板的开关反了」✅）：⭐ 功能号 12 是「⭐ 锁」语义 ⚠️
			//   （⭐ TPLock ⇒ 1 = 锁定 = 禁用 ✅），⭐ 而界面 <c>touchpad</c> 的 true 表示「⭐ 可用」✅
			//   ⇒ ⭐ **取反** ✅ —— ⚠️ 极性来自 <see cref="BaolongdaToggleInverted" />（⭐ 与写入侧**共用** ✅
			//   ⇒ ⛔ 不会出现两侧口径打架 ✅）。
			SyncBaolongdaQuickSwitch("touchpad", snap.TouchpadLock, BaolongdaToggleInverted(BaolongdaWmi.Function.TPLock));
			// ⚠️ Fn 锁（功能号 11）真机**未定论** ⚠️ ⇒ ⭐ 保持直通、⛔ 不取反 ✅（⭐ 机主只反馈了触摸板 ✅）
			SyncBaolongdaQuickSwitch("fnkey", snap.FnLock);
			// ⭐⭐ 2026-10-16（OSD 提示 · 本地方案）：⭐ 这个开关的状态**来自本地配置** ✅
			//   ⛔ **刻意不走** SyncBaolongdaQuickSwitch —— ⭐ 那条日志会写「⭐ 由硬件真实状态同步而来」❌，
			//      ⭐ 而 OSD 根本没走硬件 ⚠️ ⇒ ⭐ 用它会**谎报来源** ✅（⭐ 本会话的纪律：⭐ 日志必须如实 ✅）。
			//   ⚠️ 为什么必须在这里主动同步：⭐ 宝龙达**没有 GCU** ⇒ ⭐ GCU payload 永远到不了 ⚠️
			//      ⇒ ⭐ 不主动写的话 <c>QuickSwitches["osd"]</c> 会**一直是关的** ❌ ⇒ ⭐ 界面与实际相反 ✅。
			bool osdShow = AppConfig.Get("osd_hidden", 0) == 0;
			if (!QuickSwitches.TryGetValue("osd", out bool osdCurrent) || osdCurrent != osdShow)
			{
				QuickSwitches["osd"] = osdShow;
				Logger.WriteLine($"宝龙达 OSD 提示：⭐ 开关状态 ⇒ {(osdShow ? "显示" : "隐藏")}"
					+ "（⭐ 本地配置口径，⛔ 非硬件回读 ✅）");
			}
			// ⚠️ 刻意**不打**周期性日志：⭐ 快照里的温度/转速每 3 秒都变 ⚠️
			//    ⇒ ⭐ 打日志就是刷屏 ❌（⭐ 支持清单在启动时打一次，⭐ 见 ApplyBaolongdaBackendAsync ✅）
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("baolongda-state-fail", "宝龙达状态回读失败（已吞掉）：" + ex.Message);
		}
	}

	/// <summary>
	/// ⭐ 把一个回读值同步进 <see cref="QuickSwitches" />（⭐ 约定：⭐ 非 0 即 true ✅）。
	/// <para>⚠️ <paramref name="value" /> 为 <c>null</c>（⭐ 读不到 ✅）⇒ ⭐ **什么都不做** ✅
	/// —— ⭐ 这是本机制的**关键纪律**：⭐ ⛔ 读失败不能把有效值刷成 false ⚠️。</para>
	/// <para>⭐⭐ 2026-10-16：⭐ <paramref name="invert" /> ＝ <b>寄存器语义与界面语义相反</b> ✅
	/// （⭐ 唯一来源：<see cref="BaolongdaToggleInverted" /> ✅）—— ⚠️ 必须与写入侧
	/// <see cref="TrySetBaolongdaToggleViaBackend" /> <b>同口径</b>，
	/// ⛔ 否则会出现"⭐ 显示说开、⭐ 实际是关"⭐ 那种更糟的状态 ✅。</para>
	/// <para>⚠️ <b>只在读到规范值（0/1）时才取反</b> —— ⭐ 其余值我们**不知道**语义 ⚠️
	/// ⇒ ⭐ 原样透传（⛔ 不猜），⭐ 保持与改动前逐位一致 ✅（⭐ 原值仍在快照/诊断包里可查 ✅）。</para>
	/// </summary>
	private void SyncBaolongdaQuickSwitch(string key, int? value, bool invert = false)
	{
		if (!value.HasValue || string.IsNullOrEmpty(key))
		{
			return;
		}
		// ⭐ 只在**值真的变了**时才写＋记日志 ⇒ ⭐ 既省事又便于回查"⭐ 谁改的" ✅
		bool next = value.Value != 0;
		bool inverted = false;
		if (invert && (value.Value == 0 || value.Value == 1))
		{
			next = !next;
			inverted = true;
		}
		if (!QuickSwitches.TryGetValue(key, out bool current) || current != next)
		{
			QuickSwitches[key] = next;
			Logger.WriteLine($"宝龙达状态回读：⭐ {key} ⇒ {(next ? "开" : "关")}（⭐ 由硬件真实状态同步而来 ✅"
				+ (inverted ? $" · ⭐ 寄存器原值={value.Value}（⭐ 本功能号是「锁」语义 ⇒ ⭐ 已取反 ✅）" : string.Empty)
				+ "）");
		}
	}
	private void RefreshBaolongdaSensors()
	{
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return;
		}
		// ── ① 风扇转速（⭐ 功能号 13）—— ⭐ 这是 LHM 做不到、⭐ 必须靠宝龙达的一项 ✅ ──
		try
		{
			if (wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds))
			{
				// ⚠️ 通道归属见方法注释（⭐ 待真机确认 ⚠️）
				int gpu = (speeds.First > 0) ? speeds.First : 0;
				int cpu = (speeds.Second > 0) ? speeds.Second : 0;
				lock (_legacyStateGate)
				{
					GpuFanRpm = gpu;
					CpuFanRpm = cpu;
					FanStatusSeen = true;
					// ⭐⭐ 2026-10-16（0.291.9.1）：⭐ 标记"⭐ 真的读到过风扇" ✅ ⇒
					//   ⭐ 之后 <see cref="FanZeroIsMeaningful" /> 才会承认 0 有意义 ✅
					_baolongdaFanSeen = true;
					// ⭐⭐ 2026-10-16（0.291.9 · 修"宝龙达机型界面看不到风扇转速"）：
					//   ⚠️ 根因之一：⭐ 界面上那行风扇读数由 ⭐ `MechrevoHw.IsFanInfoFresh(...)` 把关 ✅，
					//      而它读的是 ⭐ `_fanInfoReceivedAt` ⚠️ —— ⭐ 那个时间戳**原来只有 GCU/MQTT
					//      推送那条路会写** ❌ ⇒ ⭐ 宝龙达（⭐ 没有 GCU 推送 ⚠️）永远判"不新鲜"
					//      ⇒ ⭐ ⭐ **整行风扇读数被界面省略** ❌ ⇒ ⭐ 就算 `CpuFanRpm` 读到了也看不到 ⚠️
					//   ⭐ 修法：⭐ 宝龙达**读到风扇的那一刻就打这个时间戳** ✅
					//      ⇒ ⭐ 与其他"厂商值"（⭐ 如 `CpuTemp` 那条 ⭐ 见本方法 ② 段 ✅）**同一口径** ✅
					Interlocked.Exchange(ref _fanInfoReceivedAt, Environment.TickCount64);
				}
				// ⭐⭐ 2026-10-16（0.291.9.1 · 定性日志）：⭐ 记**原始两个值** ✅ ——
				//   ⭐ 这样真机日志里一眼能看出"⭐ 读到了什么 / ⭐ 谁映射到谁" ⚠️
				//   ⭐ 用 `WriteLineIfChanged` 去重 ⇒ ⭐ 值不变时**不会刷屏** ✅
				Logger.WriteLineIfChanged("bd-fan-ok",
					$"宝龙达 风扇（⭐ 非 GCU 路径）：⭐ 读成功 ✅ ⇒ ⭐ First={speeds.First} Second={speeds.Second}"
					+ $" Third={speeds.Third} ⇒ ⭐ 映射 First→GpuFanRpm={gpu} · Second→CpuFanRpm={cpu}");
				// ⭐ 失败→成功 的**状态翻转**：⭐ 用默认级别也可见的方式记一行 ✅（⭐ 只翻转时记一次 ✅）
				if (Interlocked.Exchange(ref _bdFanFailLogged, 0) == 1)
				{
					Logger.WriteError("宝龙达 风扇（⭐ 非 GCU 路径）：⭐ 已恢复正常 ✅ —— 现在能读到 "
						+ speeds.First + "/" + speeds.Second + " rpm ✅（⭐ 之前有一段读不到 ⚠️）");
				}
			}
			else
			{
				// ⭐⭐ 2026-10-16（0.291.9.1 · 定性日志）：⚠️ **读失败这条以前是完全静默的** ⚠️
				//   ⇒ ⭐ 真机上"⭐ 界面显示 0rpm"⭐ 时**根本看不出到底是没读到、⭐ 还是读到了 0** ⚠️
				//   ⭐⚠️ 用 `WriteError` ⇒ ⭐ **在默认的「仅错误」日志级别下也会落盘** ✅
				//     （⭐ 机主不用特意开 All 级就能定位 ⚠️；⭐ 而 `WriteLine` 要看消息里有没有
				//      ⭐ 错误关键词才判定级别 ⚠️ ⇒ ⭐ 不能保证可见 ❌）
				//   ⭐ 用"⭐ 状态翻转"⭐ 只记一次 ⇒ ⚠️ **不会每 3 秒刷屏** ✅
				if (Interlocked.Exchange(ref _bdFanFailLogged, 1) == 0)
				{
					Logger.WriteError("宝龙达 风扇（⭐ 非 GCU 路径）：⚠️ TryReadFanSpeeds 返回 false ⇒ "
						+ "CpuFanRpm/GpuFanRpm **保持原值不变**（⭐ 界面会显示 0rpm ⚠️）；"
						+ "⭐ 本条只在「读不到」开始的那一刻记一次 ✅");
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteError("宝龙达 风扇（⭐ 非 GCU 路径）：⭐ 读风扇转速抛异常 ⇒ " + ex.Message);
		}
		// ── ② CPU 温度兜底（⭐ 功能号 22）—— ⚠️ 只放进 BaolongdaCpuTemp，⛔ 不覆盖 CpuTemp ✅ ──
		try
		{
			if (wmi.TryReadCpuTemperature(out int celsius))
			{
				BaolongdaCpuTemp = celsius;
				// ⭐ 同时给 `CpuTemp` 一个**兜底值** ⇒ ⭐ 所有读 `hw.CpuTemp` 的地方都不至于看到 0 ✅
				// ⚠️ 但展示层若能从 LM 自带的 LHM 读到温度，⭐ 会**覆盖**这个值 ✅
				//    —— ⭐ 这正是用户要的优先级（⭐ "LM 能读就优先用 LM" ✅）
				lock (_legacyStateGate)
				{
					CpuTemp = celsius;
					Interlocked.Exchange(ref _cpuInfoReceivedAt, Environment.TickCount64);
				}
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLineIfChanged("baolongda-temp", "宝龙达读 CPU 温度失败: " + ex2.Message);
		}
	}

	public async Task<bool> ConnectAsync()
	{
		await _connectLock.WaitAsync();
		try
		{
			if (_disposed)
			{
				return false;
			}
			if (_publishOverride != null)
			{
				return true;
			}
			// ⭐⭐ 2026-10-15（C-1 第 2 步「后端化」）：先按**有序候选表**探后端（**装了 GCU 服务 ⇒ GCU 优先**，见
			// MechrevoBackendOrder.Candidates 的注释：原来是"老接口优先"，而 MICommonInterface 是**同方通用接口**、
			// 新机型（含 50 系）也可能带 ⇒ 会被误判成老机型、**根本不去连 GCU**）。
			// 探中的是老接口就**完全不走 GCU**、直接收工；否则落到 GCU 照旧往下走 MQTT。
			// ⚠️ 只在生产通路（public 构造 ⇒ _allowLegacyProbe）上跑，且**整个会话只探一次**（_legacyProbed）。
			// ⚠️ Program.UiAuditMode 下不探：审计跑的是注入通路的假硬件，绝不能让它探到真设备。
			if (_allowLegacyProbe && !_legacyProbed && !Program.UiAuditMode)
			{
				_legacyProbed = true;
				(MechrevoBackendKind kind, object created) = await Task.Run((Func<(MechrevoBackendKind, object)>)MechrevoBackendOrder.Probe).ConfigureAwait(continueOnCapturedContext: false);
				// ⭐⭐ 2026-10-16（宝龙达适配）：宝龙达模具（⭐ 蛟龙16Pro 等）—— ⭐ 必须**排在老接口之前**判，
				// 因为两者协议同族、老接口的 GUID 校验在宝龙达机上**也会通过**（⭐ 真机实测 GUID 完全一致 ⚠️）
				// ⇒ ⭐ 若先判老接口，宝龙达后端**永远轮不到**，我们新接的风扇/温度/灯效就都用不上 ✅
				// ⚠️ 不会误伤老机型：\`BaolongdaWmi.TryCreate\` 要求**真读一次 CPU 温度**才算活体 ✅，
				//    老机型读不到就回落 `<see cref="MechrevoBackendKind.LegacyMifs" />` ⇒ ⭐ 行为不变 ✅。
				if (kind == MechrevoBackendKind.Baolongda && created is BaolongdaBackend baolongdaBackend)
				{
					_baolongda = baolongdaBackend;
					await ApplyBaolongdaBackendAsync(baolongdaBackend).ConfigureAwait(continueOnCapturedContext: false);
					return true;
				}
				if (kind == MechrevoBackendKind.LegacyMifs && created is LegacyMifsWmi legacyBackend)
				{
					_legacyMifs = legacyBackend;
					await ApplyLegacyBackendAsync(legacyBackend).ConfigureAwait(continueOnCapturedContext: false);
					return true;
				}
				// 没探到老接口（或 GCU 服务已装、直接优先 GCU）⇒ 落到 GCU（默认后端）。
				// ⛔ 这一档不碰任何既有字段：行为与改动前逐项一致。
				_activeBackend = MechrevoBackendKind.Gcu;
				// ⭐ 2026-10-15：**两条分支都要留痕**（原来只有老接口那条有日志）——诊断「GCU 连不上」时必须能
				// 区分"被误判成老机型"与"确实走的是 GCU 通路、连不上另有原因"。
				// 注：级别过滤下（默认 error）本行不进日志，用户可见的信号是顶部状态：非 GCU 后端显示「● 已就绪」。
				Logger.WriteLine($"Hardware backend: GCU (MQTT). IsGcuServiceInstalled={IsGcuServiceInstalled}, legacyProbe={kind}");
			}
			if (UsesBuiltInModes)
			{
				// ⭐ 2026-10-16：⭐ 老同方 与 ⭐ 宝龙达 ⭐ 共用这一支（⭐ 两者都靠 ReadBuiltInMode 读档位 ✅）
				return await RefreshLegacyModeAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			if (_subscriptionsReady && (_client?.IsConnected ?? false))
			{
				return true;
			}
			// 2026-10-09（本次修复：判死重建）：订阅重试循环在跑 ⇒ 重建连接由它独占（它自己那一次除外），
			// 别的调用方一律让位，免得重连循环按自己的 250ms→30s 节奏再插一次 CONNECT，
			// 把重试循环「判死→重建→再试 SUBSCRIBE」的 2s→4s→…→30s 退避叠成更密的拆连。
			if (Volatile.Read(in _subscribeRetryLoop) != 0 && Volatile.Read(in _subscribeRetryReconnecting) == 0)
			{
				return false;
			}
			// 2026-10-09（GCU 订阅侧修复）：连接还在、只是订阅尚未建立，且订阅重试循环正在重试 ⇒
			// 绝不重复 CONNECT（也绝不在这里回退 clientId）：重试 SUBSCRIBE 由 _packetWatch + 重试循环负责。
			// 这一条对「重试循环自己」同样生效：它重建连接时若连接已恢复，就转去同一条连接上重发 SUBSCRIBE。
			// 2026-10-09（批次3 #7「重连循环」）：⭐ 传输还活着、只是**会话**（订阅）没就绪 ⇒ 本函数不是这轮的主角：
			// 该由订阅重试循环在**同一条连接**上慢速重发 SUBSCRIBE（2s→4s→…→30s 封顶），绝不能在这里重复 CONNECT
			// ⇒ 直接把「未就绪」标成原因交回上层（重连循环会据此让位、只记日志、不再按 250ms 猛敲 CONNECT）。
			if (Volatile.Read(in _subscribeRetryLoop) != 0 && (_client?.IsConnected ?? false))
			{
				MarkReconnectReason("订阅未就绪(连接仍在)");
				return false;
			}
			if (_client == null)
			{
				_client = _factory.CreateMqttClient();
				_client.ApplicationMessageReceivedAsync += (MqttApplicationMessageReceivedEventArgs e) =>
				{
					HandleMessage(e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString());
					return Task.CompletedTask;
				};
				// 2026-10-09（GCU 订阅侧取证）：真报文级观察（方向 + 原始字节）。只读、只写日志：
				// SUBSCRIBE / SUBACK 各写一行（含字节数、packetId、返回码、时刻），其余报文只计数。
				// 这一条就是「SUBSCRIBE 到底有没有发出去」的唯一硬证据。
				_client.InspectPacketAsync += OnInspectMqttPacketAsync;
				_client.DisconnectedAsync += (MqttClientDisconnectedEventArgs disconnectedArgs) =>
				{
					bool subscriptionsReady = _subscriptionsReady;
					_subscriptionsReady = false;
					// 2026-10-09（诊断增强，仅加日志、不改断开/重连/退避/回退/订阅逻辑）：
					// 断开原因（ReasonString / 异常 / 断开类型）此前完全不落盘，现场只留得下
					// “订阅失败: The MQTT client is disconnected.”，没法区分“broker/bridge 后端把连接掐了”
					// 还是“我们自己拆的连接”。每次断开写一条（不是周期性日志，重连有退避，不会刷屏）。
					// 用 WriteLine 而不是 WriteLineIfChanged：连续几次断开很可能拿到完全相同的
					// Reason/Exception 文案，走“同键+同文案”去重只会留下 1 行，反而丢掉断开次数。
					// clientId 取 _client.Options.ClientId（本次连接真正用的 id）：订阅失败路径会先
					// TryFallbackClientId 再 DisconnectAsync，停在这里时字段 _clientId 可能已是下一个 id。
					Logger.WriteLine($"MQTT 断开: clientId={MqttDiagText((_client == null) ? null : _client.Options?.ClientId)} 字段clientId={_clientId} 已订阅={((subscriptionsReady) ? "是" : "否")} ClientWasConnected={disconnectedArgs.ClientWasConnected} Reason={disconnectedArgs.Reason} ReasonString={MqttDiagText(disconnectedArgs.ReasonString)} Ex={MqttDiagExceptionText(disconnectedArgs.Exception)} ConnectResult={(disconnectedArgs.ConnectResult?.ResultCode.ToString() ?? "n/a")} 回退过={((_clientIdFallbackCount > 0) ? "是" : "否")} fallbackCount={_clientIdFallbackCount} 四元组={_diagAdapterFactory.DescribeSocket()}");
					// 2026-10-09（批次3 #7「重连循环」）：⭐ 把「空闲无入站到点」与「对端真的断开 / 被 RST」在日志上**分开**，
					// 并把它记成**本次重连的原因**（重连循环会原样打出来，真机核对时不用再猜）。
					// ⚠️ 口径（用户在抓包工具上踩过的同一个坑）：SocketException(TimedOut) / KeepAliveTimeout = **空闲无入站到点**
					//（只是「没有入站数据到点」），不是「对端拒绝/被 RST」；但传输确实已经断了 ⇒ 仍然要重连，
					// 只是不再把这种正常空闲误当成「对端在反抗」。
					// ⚠️ 原因文案里**不带** KeepAliveTimeout 这个枚举字面量（含 "timeout" ⇒ 会被判成 Error 级、刷 Errors 缓冲）；
					// 枚举原文写在下面那条专门的"断开"日志里（那条本来就按断开记）。
					SocketError? disconnectSocketError = MqttDiagSocketError(disconnectedArgs.Exception);
					bool idleTimeout = disconnectSocketError == SocketError.TimedOut || disconnectedArgs.Reason == MqttClientDisconnectReason.KeepAliveTimeout;
					MarkReconnectReason(idleTimeout
						? "空闲无入站到点(Idle): " + (disconnectSocketError.HasValue ? ("SocketErr=" + disconnectSocketError.Value) : "看门狗窗口到点")
						: $"对端断开(Reason={disconnectedArgs.Reason})");
					if (idleTimeout)
					{
						// ⚠️ 这一条刻意**不**印 `Reason=<枚举>`（枚举字面量可能是 KeepAliveTimeout，含 "timeout" ⇒ 会被判成 Error 级）：
						// 断开枚举原文在上面那条既有的「MQTT 断开」日志里，这里只留"按 Idle 归类"这个结论。
						Logger.WriteLine($"MQTT 空闲无入站到点: 按 Idle 语义归类（不是对端 RST） clientId={_clientId} SocketErr={(disconnectSocketError.HasValue ? disconnectSocketError.Value.ToString() : "n/a")} 处理=照常重连、退避走正常节奏（keepalive={ResolveKeepAlivePeriod().TotalSeconds}s，看门狗窗口=1.5×keepalive）");
					}
					if (subscriptionsReady)
					{
						long num2 = Environment.TickCount64 - Volatile.Read(in _sessionReadyTick);
						int num3 = NextQuickDropStreak(Volatile.Read(in _quickDropStreak), num2);
						Volatile.Write(ref _quickDropStreak, num3);
						if (num3 >= 3)
						{
							Logger.WriteLineIfChanged("mqtt-flap", $"MQTT 会话就绪后 {num2}ms 即断开（连续 {num3} 次）：可能有其他程序使用相同的客户端标识，重连退避 {ReconnectInitialDelayMs(num3)}ms");
						}
					}
					if (!_disposed)
					{
						_reconnect.MarkDisconnectRequested();
						ReconnectLoopAsync();
					}
					return Task.CompletedTask;
				};
			}
			// 2026-10-09（诊断增强，仅加日志、不改连接/回退逻辑）：
			// 现场只留得下"订阅失败: The MQTT client is disconnected."，看不到"我们实际发出的是哪个 clientId、
			// 有没有回退过"。这一条是"同 clientId 被占/被顶"与"bridge 后端没起来"两种嫌疑的分界证据。
			// 每次发起 CONNECT 各写一条；重连有退避（250ms→30s），不会周期性刷屏。
			// 2026-10-09（批次3 #7）：再补 ① 本次重连**原因**（由重连循环/断开回调写进 _reconnectReason）
			// ② keepalive 实际值（config 可调）③ 是否由「clientId 回退可恢复」把 id 换成首选。
			bool clientIdRecovered = TryRecoverPreferredClientId();
			TimeSpan keepAlive = ResolveKeepAlivePeriod();
			Logger.WriteLine($"MQTT CONNECT 发起: clientId={_clientId} 首选={PrimaryClientId} 回退过={((_clientIdFallbackCount > 0) ? "是" : "否")} fallbackCount={_clientIdFallbackCount} 恢复首选={((clientIdRecovered) ? "是" : "否")} keepalive={keepAlive.TotalSeconds}s 原因={ReconnectReasonText()} broker=127.0.0.1:13688");
			try
			{
				MqttClientConnectResult mqttClientConnectResult = await _client.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", 13688).WithCredentials(_clientUser, _clientPwd).WithClientId(_clientId)
					.WithProtocolVersion(MqttProtocolVersion.V311)
					.WithCleanSession()
					.WithKeepAlivePeriod(keepAlive)
					.Build());
				// 2026-10-09（诊断增强）：CONNACK 无论成败都记一条（以前只有失败才写"未就绪/尚未就绪"，
				// CONNACK 成功这一半证据是空的）。用 WriteLine 而不是 WriteLineIfChanged：
				// 取值随每次尝试的 ResultCode / 回退次数变化，且成功这条此前完全没有日志，不会被去重吞掉。
				Logger.WriteLine($"MQTT CONNACK: clientId={_clientId} ResultCode={mqttClientConnectResult.ResultCode} 成功={((mqttClientConnectResult.ResultCode == MqttClientConnectResultCode.Success) ? "是" : "否")} fallbackCount={_clientIdFallbackCount}");
				if (mqttClientConnectResult.ResultCode != MqttClientConnectResultCode.Success)
				{
					Logger.WriteLineIfChanged("mqtt-connect:" + _clientId, $"MechrevoHw 首次连接未就绪: clientId={_clientId} ResultCode={mqttClientConnectResult.ResultCode} 原因=broker 在 CONNACK 里返回非 Success");
					// 2026-10-09（批次3 #7）：⭐ 「CONNACK 非 Success」= **桥/权限**问题（不是网络断了），
					// 与「传输掉线」在日志上是两种原因；回退一档后由重连循环按正常退避再来。
					MarkReconnectReason($"CONNACK 未就绪: {mqttClientConnectResult.ResultCode}");
					TryFallbackClientId("首连未就绪: " + mqttClientConnectResult.ResultCode);
					if (!_disposed)
					{
						ReconnectLoopAsync();
					}
					return false;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLineIfChanged("mqtt-connect:" + _clientId, $"MechrevoHw 连接尚未就绪: clientId={_clientId} ResultCode=(无 CONNACK，连接阶段异常) 原因={ex.Message}");
				MarkReconnectReason("无 CONNACK(连接阶段抛错): " + ex.Message);
				if (!_disposed)
				{
					ReconnectLoopAsync();
				}
				return false;
			}
			// 2026-10-09（GCU 订阅侧【修复】+【取证】）：订阅读取改成「同一条连接上有限次带退避重试」。
			// 现场之所以放大成「约每 30 秒无限循环」：_4→_3→_2 每次都是
			// 「CONNECT 成功 → SUBSCRIBE 没成 → 立刻断开 → 回退 clientId → 整段重连（退避封顶 30s）」。
			// 现在：① 一次订阅不成**不再**立刻判「断开」、**不再**立刻回退 clientId；
			//       ② 连接仍可用就在同一条连接上重试 SUBSCRIBE（400ms/1200ms），仍不成 ⇒ 保持连接、
			//          交给订阅重试循环（2s→4s→…→30s 封顶）只重发 SUBSCRIBE；
			//       ③ 只有「连续 RoundsBeforeFallback 轮」都没成，才回退 clientId 一档（总次数仍是既有上限 2，
			//          耗尽后停在最后一个 id 且不再回退）；
			//       ④ 2026-10-09（本次修复：判死重建）：连接被看门狗判死时**不再**整段拆连 + 立刻回退 clientId，
			//          改由订阅重试循环复用 ConnectAsync 重建连接后继续重试同一批 SUBSCRIBE（退避 2s→30s 封顶，
			//          一轮循环内最多 MaxReconnectRounds 次重建）。
			int epoch = Interlocked.Increment(ref _connectEpoch);
			bool subscribed = false;
			int subscribeAttempts = 0;
			Exception? subscribeError = null;
			long lastSubscribeElapsedMs = -1L;
			for (int attempt = 1; attempt <= MqttSubscribeRetryPolicy.AttemptsPerConnection; attempt++)
			{
				subscribeAttempts = attempt;
				_packetWatch.BeginSubscribeWait();
				long attemptStartTick = Environment.TickCount64;
				// 「即将发出」这一条是刻意的：它与下面的「报文 发出: 类型=SUBSCRIBE」成对出现（或缺席）——
				// 有本行而**没有**「报文 发出」行 ⇒ SUBSCRIBE 根本没写出去（Send 阶段就断了）。
				Logger.WriteLine($"MQTT SUBSCRIBE 即将发出: clientId={_clientId} 尝试={attempt}/{MqttSubscribeRetryPolicy.AttemptsPerConnection} topics={SubscribedTopicFilters.Length} qos=0 等待上限={DescribeSubscribeTimeout()} 明细=[{string.Join(",", SubscribedTopicFilters)}] 时刻={MqttPacketWatch.DiagClock(attemptStartTick)}");
				MqttClientSubscribeResult? subscribeResult = null;
				try
				{
					subscribeResult = await _client.SubscribeAsync(BuildSubscribeOptions());
					lastSubscribeElapsedMs = Environment.TickCount64 - attemptStartTick;
					string[] rejected = (from item in subscribeResult.Items
						where item.ResultCode > MqttClientSubscribeResultCode.GrantedQoS2
						select $"{item.TopicFilter.Topic}={item.ResultCode}").ToArray();
					if (rejected.Length != 0)
					{
						throw new InvalidOperationException("broker 拒绝订阅: " + string.Join(", ", rejected));
					}
					subscribed = true;
					_subscriptionsReady = true;
					_subscribeFailureRounds = 0;
					Volatile.Write(ref _sessionReadyTick, Environment.TickCount64);
					// 2026-10-09（诊断增强）：订阅成功单独记一条 —— 与 NotifyConnectionReady 的
					// "MechrevoHw connection ready: generation=N" 文案完全不同，便于按时间序看
					// 「CONNECT → CONNACK → 订阅成功 → connection ready」四段，判断断点落在哪一段。
					// 本次再补：尝试次数、实际耗时、SUBACK 报文级观察窗、结果码汇总、连接四元组。
					Logger.WriteLine($"MQTT 订阅成功: clientId={_clientId} topics={SubscribedTopicFilters.Length} 全部 GrantedQoS≤2 尝试={attempt}/{MqttSubscribeRetryPolicy.AttemptsPerConnection} 耗时={lastSubscribeElapsedMs}ms packetId={subscribeResult.PacketIdentifier} 结果码=[{MqttSubscribeForensics.DescribeSubscribeResultCodes(subscribeResult)}] {_packetWatch.DescribeWaitOutcome()} 四元组={_diagAdapterFactory.DescribeSocket()}");
					break;
				}
				catch (Exception ex2)
				{
					subscribeError = ex2;
					lastSubscribeElapsedMs = Environment.TickCount64 - attemptStartTick;
					bool connectionAlive = _client?.IsConnected ?? false;
					// 「没发出去」与「发了没回」在这里被明确写开：报文级窗口（发出去几个 SUBSCRIBE / 收到几个 SUBACK）
					// + 异常类型判定（等回包到点 = 发了没回；IOException/SocketException = 写出失败，RST 也在 SocketError 里）。
					Logger.WriteLine($"MQTT 订阅失败: clientId={_clientId} 尝试={attempt}/{MqttSubscribeRetryPolicy.AttemptsPerConnection} (CONNACK 已成功，断在 Subscribe) 耗时={lastSubscribeElapsedMs}ms 等待上限={DescribeSubscribeTimeout()} 发出判定={MqttSubscribeForensics.DescribeSendVerdict(ex2)} 连接仍在={((connectionAlive) ? "是" : "否")} 例外={MqttDiagExceptionText(ex2)} {_packetWatch.DescribeWaitOutcome()} 四元组={_diagAdapterFactory.DescribeSocket()}");
					Logger.WriteLineIfChanged("mqtt-connect:" + _clientId, $"MechrevoHw 订阅失败: clientId={_clientId} (CONNACK 已成功，断在 Subscribe) 原因={ex2.Message}");
					if (!connectionAlive)
					{
						// 2026-10-09（本次修复：判死重建）：看门狗把连接判死（IsConnected 已为否）**不再**在这里
						// 整段拆连 + 立刻回退 clientId，只退出本次尝试，交给下面的统一处理 —— 保持连接不可用时
						// 由订阅重试循环用 ConnectAsync 重建连接后继续重试同一批 SUBSCRIBE。
						break;
					}
					if (attempt < MqttSubscribeRetryPolicy.AttemptsPerConnection)
					{
						int retryDelayMs = MqttSubscribeRetryPolicy.RetryDelayMs(attempt);
						Logger.WriteLine($"MQTT 订阅重试: clientId={_clientId} 第 {attempt} 次没成但连接仍在，保持连接 {retryDelayMs}ms 后重试 SUBSCRIBE（不断开、不回退 clientId）");
						await Task.Delay(retryDelayMs);
						if (!(_client?.IsConnected ?? false))
						{
							Logger.WriteLine($"MQTT 订阅重试中止: clientId={_clientId} 退避期间连接已不可用");
							break;
						}
					}
				}
			}
			if (!subscribed)
			{
				// 2026-10-09（本次修复：判死重建）：两条路（连接仍在 / 已被看门狗判死）合并成同一套节制逻辑：
				// ① 都先只记「连续失败轮数」，绝不因为「判死」就立刻回退 clientId；
				// ② 只有连续 RoundsBeforeFallback 轮都没成，才回退一档（总次数仍受 MaxClientIdFallbacks=2 约束，
				//    耗尽后停在最后一个 id、不再回退）；
				// ③ 连接仍在 ⇒ 保持连接只重发 SUBSCRIBE；连接已判死 ⇒ 由订阅重试循环用既有 ConnectAsync
				//    **重建连接后继续重试同一批 SUBSCRIBE**（不再整段拆连）。
				bool connectionAlive = _client?.IsConnected ?? false;
				_subscribeFailureRounds++;
				// 2026-10-09（批次3 #7）：⭐ 两种「未就绪」在原因里就写开 ——
				// 「连接仍在但没订上」= 桥/权限/订阅侧问题（慢速重试，不猛重连）；
				// 「连接不可用」= 传输断了（要重连）。⛔ 不再让两者都走同一条"猛重连"路径。
				MarkReconnectReason(connectionAlive ? "订阅未就绪(连接仍在)" : "订阅未就绪且连接不可用");
				if (connectionAlive)
				{
					Logger.WriteLine($"MQTT 订阅未建立但连接可用: clientId={_clientId} 本轮尝试={subscribeAttempts} 次 连续失败轮数={_subscribeFailureRounds}/{MqttSubscribeRetryPolicy.RoundsBeforeFallback} 处理=保持连接并由订阅重试循环只重发 SUBSCRIBE（不整段重连）");
				}
				else
				{
					Logger.WriteLine($"MQTT 订阅失败且连接不可用: clientId={_clientId} 本轮尝试={subscribeAttempts} 次 连续失败轮数={_subscribeFailureRounds}/{MqttSubscribeRetryPolicy.RoundsBeforeFallback} 发出判定={MqttSubscribeForensics.DescribeSendVerdict(subscribeError)} 处理=由订阅重试循环用 ConnectAsync 重建连接后继续重试同一批 SUBSCRIBE（不整段拆连、不立刻回退 clientId） {_packetWatch.DescribeWaitOutcome()} 四元组={_diagAdapterFactory.DescribeSocket()}");
				}
				if (_subscribeFailureRounds >= MqttSubscribeRetryPolicy.RoundsBeforeFallback)
				{
					_subscribeFailureRounds = 0;
					if (TryFallbackClientId($"订阅连续 {MqttSubscribeRetryPolicy.RoundsBeforeFallback} 轮没成（最后一次: {MqttDiagText(subscribeError?.Message)}）"))
					{
						await DropConnectionAndReconnectAsync("订阅连续失败后回退 clientId");
						return false;
					}
					Logger.WriteLine($"mqtt clientId 回退已耗尽: 停在 {_clientId}（fallbackCount={_clientIdFallbackCount}/{MaxClientIdFallbacks}），不再回退，改为按 2s→30s 退避继续重试 SUBSCRIBE");
				}
				StartSubscribeRetryLoop(epoch);
				return false;
			}
			try
			{
				if (_disposed)
				{
					return false;
				}
				await RequestInitialStateAsync();
			}
			catch (Exception ex4)
			{
				Logger.WriteLineIfChanged("mqtt-connect:" + _clientId, "MechrevoHw 初始状态请求失败: " + ex4.Message);
				MarkReconnectReason("初始状态未就绪: " + ex4.Message);
				_subscriptionsReady = false;
				_reconnect.MarkDisconnectRequested();
				try
				{
					await _client.DisconnectAsync();
				}
				catch (Exception ex5)
				{
					Logger.WriteLine("MQTT disconnect after initialization failure: " + ex5.Message);
				}
				if (!_disposed)
				{
					ReconnectLoopAsync();
				}
				return false;
			}
			if (_disposed)
			{
				return false;
			}
			NotifyConnectionReady();
			return true;
		}
		finally
		{
			_connectLock.Release();
		}
	}

	internal async Task RequestInitialStateAsync()
	{
		// 2026-10-15（C-1 第 2 步，对照 A-22）：老接口后端**没有 MQTT**，一条 GETSTATUS 都不发 ——
		// 只读一次老接口档位。⛔ GCU 分支一字未动。
		// ⭐ 2026-10-16：⭐ 宝龙达同样没有 MQTT ⇒ ⭐ 也走这一支 ✅
		if (UsesBuiltInModes)
		{
			await RefreshLegacyModeAsync().ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		await Publish("System/Control", new Dictionary<string, object> { ["Action"] = "System_ON" });
		await Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
		await Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GET_FAN_SPEED_CURVE_SETTING" });
		await Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
		await Publish("LCHWOC/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
		await Publish("Keyboard/Ctrl", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
		await Publish("HidLightbar/Ctrl", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
		await Publish("HidLightbar_Logo/Ctrl", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
        await Publish("HidLightbar_Hinge/Ctrl", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
        await Publish("HidLightbar_Sync/Ctrl", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
		await Publish("BT_LC/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
		await Publish("Customize/Control", new Dictionary<string, object> { ["Action"] = "GETSUPPORT" });
		await Publish("BatteryProtection/Control", new Dictionary<string, object> { ["Report"] = "GET" });
	}

	internal async Task RequestBatteryProtectionAsync()
	{
		if (!IsConnected)
		{
			return;
		}
		await Publish("BatteryProtection/Control", new Dictionary<string, object> { ["Report"] = "GET" });
	}

	internal static void OnServiceProfileMayHaveChanged()
	{
		MechrevoDeviceCapabilities.Invalidate();
	}

	// 首选 clientId 连不上/订阅失败时，按白名单链回退一档（次数有上限，只前进不回头）。
	internal static (string User, string Pwd) ResolveClientIdCredentials(string clientId)
	{
		if (clientId == HelperClientId)
		{
			return (User3, Pwd3);
		}
		if (clientId == SecondaryClientId)
		{
			return (User2, Pwd2);
		}
		return (User, Pwd);
	}

	internal bool TryFallbackClientId(string reason)
	{
		// 2026-10-09（批次3 #7）：回退仍只前进不回头（单调），且**选定后固定用** —— 每次都走这一处判定；
		// 「恢复首选 _4」是**另一条**独立路径（TryRecoverPreferredClientId，要求会话稳定 ≥5 分钟 + 10 分钟冷却），
		// ⛔ 绝不在这里顺手把 id 换回去（那才会变成 _4/_3/_2 来回抖的"三连轮询"）。
		if (_disposed || _clientIdFallbackCount >= MaxClientIdFallbacks)
		{
			return false;
		}
		int num = Array.IndexOf(ClientIdFallbackChain, _clientId);
		if (num < 0 || num + 1 >= ClientIdFallbackChain.Length)
		{
			return false;
		}
		string text = ClientIdFallbackChain[num + 1];
		string text2 = _clientId;
		_clientId = text;
		(_clientUser, _clientPwd) = ResolveClientIdCredentials(text);
		_clientIdFallbackCount++;
		Logger.WriteLine($"mqtt clientId 回退: {text2} -> {text} (原因: {reason})");
		return true;
	}

	internal int NotifyConnectionReady()
	{
		OnServiceProfileMayHaveChanged();
		int num = Interlocked.Increment(ref _connectionGeneration);
		Volatile.Write(ref _reconnectStartedTick, 0L);
		// 2026-10-09（批次3 #7）：记下「会话就绪」时刻 —— 「clientId 回退可恢复」（稳定 5 分钟后回首选 _4）用它判稳定性。
		Interlocked.Exchange(ref _sessionReadySinceTick, Environment.TickCount64);
		Logger.ResetChangeTracking("mqtt-connect:" + _clientId);
		Logger.ResetChangeTracking("mqtt-reconnect:" + _clientId);
		Logger.WriteLine($"MechrevoHw connection ready: generation={num}");
		Logger.WriteLineIfChanged("mqtt-clientid-active", "mqtt clientId 生效: " + _clientId + ((_clientIdFallbackCount > 0) ? $" (已回退 {_clientIdFallbackCount} 次, 首选 {PrimaryClientId})" : " (首选, 未回退)"));
		string[] statusLogKeys = StatusLogKeys;
		for (int i = 0; i < statusLogKeys.Length; i++)
		{
			Logger.ResetChangeTracking(statusLogKeys[i]);
		}
		Action<int> action = ConnectionReady;
		if (action == null)
		{
			return num;
		}
		Delegate[] invocationList = action.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			Action<int> action2 = (Action<int>)invocationList[i];
			try
			{
				action2(num);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("ConnectionReady handler failed: " + ex.Message);
			}
		}
		return num;
	}

	internal bool HasTelemetrySince(long tick)
	{
		return Interlocked.Read(in _lastTelemetryTick) >= tick;
	}

	internal bool IsTelemetryStale(TimeSpan maximumAge, long now = -1L)
	{
		long num = Interlocked.Read(in _lastTelemetryTick);
		if (num == 0L)
		{
			return true;
		}
		return (double)(((now >= 0) ? now : Environment.TickCount64) - num) > maximumAge.TotalMilliseconds;
	}

	private async Task ReconnectLoopAsync()
	{
		_reconnect.MarkDisconnectRequested();
		if (!_reconnect.TryEnterLoop())
		{
			return;
		}
		Interlocked.Exchange(ref _reconnecting, 1);
		if (Volatile.Read(in _reconnectStartedTick) == 0L)
		{
			Volatile.Write(ref _reconnectStartedTick, Environment.TickCount64);
		}
		try
		{
			// 2026-10-09（批次3 #7「重连循环」）：⭐ 传输还活着、只是**会话**没就绪、且订阅重试循环正在跑 ⇒
			// 这一轮根本不该由本循环驱动（ConnectAsync 会在护栏处直接返回 false）：
			// 那样本循环只会「250ms 醒一次 → 被护栏挡回 → 翻倍」地空转，还让 UI 一直显示"重连中"。
			// 现在明确**让位**：写一条能看出区别的日志就退出，SUBSCRIBE 的重发由订阅重试循环（2s→30s）负责。
			bool transportAlive = IsTransportConnected;
			if (transportAlive && !IsSessionReady && Volatile.Read(in _subscribeRetryLoop) != 0)
			{
				Logger.WriteLine($"MQTT 重连循环让位: 原因={ReconnectReasonText()} clientId={_clientId} 传输连接=是 会话就绪=否 ⇒ 不 CONNECT、不重连，交回订阅重试循环（只重发 SUBSCRIBE，2s→30s 封顶）");
				return;
			}
			int retryDelayMs = ReconnectInitialDelayMs(Volatile.Read(in _quickDropStreak));
			// ⚠️「空闲无入站」不是断线（用户口径）：下面这条把**原因**、**当前 clientId**、**退避时长**、
			// **传输/会话两种状态**、**短命会话计数**一次写清 —— 真机核对重连循环就看这一行。
			Logger.WriteLine($"MQTT 断线，重连中… 原因={ReconnectReasonText()} clientId={_clientId} 首选={PrimaryClientId} fallbackCount={_clientIdFallbackCount} 退避起步={retryDelayMs}ms（250ms 起、×2、30s 封顶） 短命会话连击={Volatile.Read(in _quickDropStreak)} 传输连接={(transportAlive ? "是" : "否")}");
			int attempt = 0;
			while (_reconnect.ShouldContinue(_subscriptionsReady && (_client?.IsConnected ?? false), _disposed))
			{
				await Task.Delay(retryDelayMs).ConfigureAwait(continueOnCapturedContext: false);
				if (_disposed)
				{
					break;
				}
				// 2026-10-09（批次3 #7）：订阅重试循环接手了（连接仍在、只是没订上）⇒ 同样让位（见上面的长注释）。
				if (IsTransportConnected && !IsSessionReady && Volatile.Read(in _subscribeRetryLoop) != 0)
				{
					Logger.WriteLine($"MQTT 重连循环让位(等待中): 原因={ReconnectReasonText()} clientId={_clientId} 传输连接=是 会话就绪=否 ⇒ 停止本循环的 CONNECT 尝试（交回订阅重试循环）");
					break;
				}
				attempt++;
				try
				{
					if (await ConnectAsync())
					{
						break;
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLineIfChanged("mqtt-reconnect:" + _clientId, "MQTT reconnect attempt failed: " + ex.Message);
				}
				Logger.WriteLine($"MQTT 重连尝试 #{attempt}: 原因={ReconnectReasonText()} clientId={_clientId} 本次退避={retryDelayMs}ms 下次退避={NextReconnectDelayMs(retryDelayMs)}ms 传输连接={((_client?.IsConnected ?? false) ? "是" : "否")} 会话就绪={(_subscriptionsReady ? "是" : "否")}");
				retryDelayMs = NextReconnectDelayMs(retryDelayMs);
			}
		}
		finally
		{
			Volatile.Write(ref _reconnecting, 0);
			if (_reconnect.ExitLoop() && !_disposed)
			{
				ReconnectLoopAsync();
			}
		}
	}

	// 2026-10-09（批次3 #7「重连循环」）：本次重连**原因**的唯一写入处（只为日志可读，不改任何时序/判定）。
	private void MarkReconnectReason(string reason)
	{
		Volatile.Write(ref _reconnectReason, reason ?? "(未标注)");
	}

	/// <summary>本次重连原因的可读文本（空则给占位，保证日志格式稳定）。</summary>
	private string ReconnectReasonText()
	{
		string text = Volatile.Read(ref _reconnectReason);
		return string.IsNullOrWhiteSpace(text) ? "(未标注)" : text;
	}

	/// <summary>
	/// 2026-10-09（批次3 #7）：**clientId 回退可恢复** —— 会话稳定 ≥ <see cref="ClientIdRecoverStableMs" />（5 分钟）
	/// 之后，下次真要 CONNECT 时把 clientId 换回**首选 _4** 并把回退计数清零（于是再降级又有一整套预算）。
	/// <list type="bullet">
	/// <item>⛔ **绝不为了恢复去拆连接**：本方法只在 <c>ConnectAsync</c> 已经决定要发 CONNECT 的那一刻被调用。</item>
	/// <item>⛔ **不抖**：两次恢复之间至少 <see cref="ClientIdRecoverCooldownMs" />（10 分钟），且必须"稳够 5 分钟"。</item>
	/// <item>会话从没就绪过（<c>_sessionReadySinceTick == 0</c>）⇒ 不恢复（那说明当前 id 还没被证明用过）。</item>
	/// </list>
	/// </summary>
	private bool TryRecoverPreferredClientId()
	{
		if (_disposed || _clientIdFallbackCount <= 0 || string.Equals(_clientId, PrimaryClientId, StringComparison.Ordinal))
		{
			return false;
		}
		long now = Environment.TickCount64;
		long readySince = Interlocked.Read(in _sessionReadySinceTick);
		if (readySince == 0L || now - readySince < ClientIdRecoverStableMs)
		{
			return false;
		}
		long lastRecover = Interlocked.Read(in _lastClientIdRecoverTick);
		if (lastRecover != 0L && now - lastRecover < ClientIdRecoverCooldownMs)
		{
			return false;
		}
		Interlocked.Exchange(ref _lastClientIdRecoverTick, now);
		string previous = _clientId;
		_clientId = PrimaryClientId;
		(_clientUser, _clientPwd) = ResolveClientIdCredentials(_clientId);
		_clientIdFallbackCount = 0;
		Logger.WriteLine($"mqtt clientId 恢复首选: {previous} -> {PrimaryClientId}（会话已稳定 {(now - readySince) / 1000}s ≥ {ClientIdRecoverStableMs / 1000}s ⇒ 回退计数清零，下次再降级仍有 {MaxClientIdFallbacks} 次预算；冷却 {ClientIdRecoverCooldownMs / 1000}s 内不再恢复）");
		return true;
	}

	internal static int NextQuickDropStreak(int streak, long sessionLifetimeMs)
	{
		if (sessionLifetimeMs >= 10000)
		{
			if (sessionLifetimeMs < 30000)
			{
				return streak;
			}
			return 0;
		}
		return Math.Min(streak + 1, 16);
	}

	internal static int ReconnectInitialDelayMs(int quickDropStreak)
	{
		if (quickDropStreak > 1)
		{
			return (int)Math.Min(30000L, 250L << Math.Min(quickDropStreak, 7));
		}
		return 250;
	}

	internal static int NextReconnectDelayMs(int delayMs)
	{
		return (int)Math.Min(30000L, Math.Max(250L, delayMs) * 2);
	}

	// 2026-10-09（GCU 订阅侧取证）：报文级观察回调。MQTTnet 在收发路径上回调，这里只做两件事：
	// 记账 + 给 SUBSCRIBE/SUBACK 各写一行日志；绝不改报文、不改时序，异常一律吞掉（取证不允许影响收发）。
	private Task OnInspectMqttPacketAsync(InspectMqttPacketEventArgs args)
	{
		try
		{
			string? line = _packetWatch.Observe(args.Direction, args.Buffer);
			if (line != null)
			{
				Logger.WriteLine(line);
			}
		}
		catch
		{
		}
		return Task.CompletedTask;
	}

	// 2026-10-09：订阅选项构建（与改动前逐字一致：SubscribedTopicFilters 全量、默认 QoS=AtMostOnce=0，
	// 不新增订阅、不改主题集合、不改 QoS）。
	private MqttClientSubscribeOptions BuildSubscribeOptions()
	{
		MqttClientSubscribeOptionsBuilder mqttClientSubscribeOptionsBuilder = new MqttClientSubscribeOptionsBuilder();
		string[] subscribedTopicFilters = SubscribedTopicFilters;
		foreach (string topic in subscribedTopicFilters)
		{
			mqttClientSubscribeOptionsBuilder.WithTopicFilter(topic);
		}
		return mqttClientSubscribeOptionsBuilder.Build();
	}

	// 2026-10-09：本次等待 SUBACK 的上限（MQTTnet 的客户端级操作超时），只读不改。
	// ⚠️ 文案刻意不写 Timeout 这个词：Logger.ClassifySeverity 会把含 timeout 的行判成 Error
	// （连成功路径也会污染 Errors 环形缓冲），所以这里只写毫秒数与中文说明。
	private string DescribeSubscribeTimeout()
	{
		try
		{
			TimeSpan? timeout = _client?.Options?.Timeout;
			if (timeout.HasValue && timeout.Value > TimeSpan.Zero)
			{
				return timeout.Value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + "ms(客户端操作上限)";
			}
			return "n/a";
		}
		catch
		{
			return "n/a";
		}
	}

	// 2026-10-09（GCU 订阅侧【修复】）：断开→重连的既有动作收拢成一处（原订阅失败分支的那段逐字保留：
	// 立刻回退 clientId 已从这条路径上摘掉，改由调用方决定是否回退）。
	private async Task DropConnectionAndReconnectAsync(string reason)
	{
		_subscriptionsReady = false;
		_reconnect.MarkDisconnectRequested();
		Logger.WriteLine($"MQTT 断开重连: clientId={_clientId} 原因={reason} 四元组={_diagAdapterFactory.DescribeSocket()}");
		IMqttClient? client = _client;
		if (client != null)
		{
			try
			{
				await client.DisconnectAsync();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("MQTT disconnect after subscribe failure: " + ex.Message);
			}
		}
		if (_client?.IsConnected ?? false)
		{
			try
			{
				_client.Dispose();
			}
			catch
			{
			}
			_client = null;
		}
		if (!_disposed)
		{
			ReconnectLoopAsync();
		}
	}

	// 2026-10-09（本次修复：判死重建）：订阅重试循环专用的「重建连接」入口 —— 只是给既有 ConnectAsync 打一个
	// 「这次 CONNECT 是重试循环自己发起的」标记（别的时候其它调用方一律让位），CONNECT / CONNACK / 订阅 / 护栏
	// 全部逐字复用 ConnectAsync，不另写一套连接逻辑。
	private async Task<bool> ReconnectForSubscribeRetryAsync()
	{
		Volatile.Write(ref _subscribeRetryReconnecting, 1);
		try
		{
			return await ConnectAsync();
		}
		finally
		{
			Volatile.Write(ref _subscribeRetryReconnecting, 0);
		}
	}

	// 2026-10-09（GCU 订阅侧【修复】）：订阅重试循环入口（同一时刻只允许一个）。
	private void StartSubscribeRetryLoop(int epoch)
	{
		if (Interlocked.CompareExchange(ref _subscribeRetryLoop, 1, 0) != 0)
		{
			return;
		}
		_ = SubscribeRetryLoopAsync(epoch);
	}

	// 2026-10-09（GCU 订阅侧【修复】+ 本次「判死重建」）：订阅没建立时的专用重试循环 ——
	// ① 连接仍在：**只重发 SUBSCRIBE**（不换 clientId、不重发 CONNECT、不断开），间隔 2s→4s→…→30s 封顶；
	// ② 连接已被看门狗判死（IsConnected=false）：用既有 ConnectAsync（含它的「不重复 CONNECT」护栏 + 世代号）
	//    **重建连接**，连上后继续重试同一批 SUBSCRIBE，退避同样 2s→4s→…→30s 封顶 —— 不再「整段拆连」，
	//    也不再把「判死」当成回退 clientId 的理由；判死重建一轮循环内最多 MaxReconnectRounds 次，到点退出
	//    本循环、交回既有重连机制（绝不在这里长出新的无限拆连循环）；
	// ③ 连续失败轮数到阈值才允许回退 clientId 一档（总次数仍受 MaxClientIdFallbacks=2 约束，
	//    耗尽后停在最后一个 id、不再回退）。世代号变了 / 订阅成了 / 被 Dispose ⇒ 立刻退出。
	private async Task SubscribeRetryLoopAsync(int epoch)
	{
		int delayMs = MqttSubscribeRetryPolicy.FirstRoundDelayMs;
		int reconnectRounds = 0;
		try
		{
			while (!_disposed)
			{
				await Task.Delay(delayMs).ConfigureAwait(continueOnCapturedContext: false);
				if (_disposed || Volatile.Read(in _connectEpoch) != epoch)
				{
					return;
				}
				if (_subscriptionsReady)
				{
					return;
				}
				IMqttClient? retryClient = _client;
				if (retryClient == null || !retryClient.IsConnected)
				{
					// 2026-10-09（本次修复：判死重建）：连接没了（真机上就是看门狗 1.5×KeepAlive 判死）⇒
					// **重建连接**，而不是把整段拆掉再回退 clientId。
					if (reconnectRounds >= MqttSubscribeRetryPolicy.MaxReconnectRounds)
					{
						Logger.WriteLine($"MQTT 订阅重试停止: clientId={_clientId} 判死重建已达上限 重试={reconnectRounds}/{MqttSubscribeRetryPolicy.MaxReconnectRounds} 轮仍未订上，退出重试循环交回既有重连机制（重连退避 30s 封顶、回退仍受 连续{MqttSubscribeRetryPolicy.RoundsBeforeFallback}轮 + MaxClientIdFallbacks={MaxClientIdFallbacks} 约束）");
						return;
					}
					reconnectRounds++;
					Logger.WriteLine($"MQTT 订阅重试: 判死后重建连接 clientId={_clientId} 世代={epoch} 重试={reconnectRounds}/{MqttSubscribeRetryPolicy.MaxReconnectRounds} 退避={delayMs}ms 连接仍在=否 处理=复用 ConnectAsync 重建连接（不重复 CONNECT 护栏生效）后继续重试同一批 SUBSCRIBE（不整段拆连、不立刻回退 clientId） 四元组={_diagAdapterFactory.DescribeSocket()}");
					try
					{
						await ReconnectForSubscribeRetryAsync();
					}
					catch (Exception exReconnect)
					{
						Logger.WriteLine("MQTT 订阅重试重建连接异常: " + MqttDiagExceptionText(exReconnect));
					}
					if (_disposed)
					{
						return;
					}
					if (_subscriptionsReady)
					{
						// 刚才那次重建里的订阅成了（ConnectAsync / 本循环自己会 NotifyConnectionReady），收工。
						return;
					}
					// ConnectAsync 每次真正 CONNECT 都会推进 _connectEpoch。重建职责归本循环（这期间 ConnectAsync
					// 的护栏会挡掉重连循环 / 诊断等别的调用方），所以把基准世代刷新成刚才那次 CONNECT 的世代，
					// 免得下一轮把自己引起的世代变化误判成「别人重连了」而提前退出。
					epoch = Volatile.Read(in _connectEpoch);
					delayMs = MqttSubscribeRetryPolicy.NextRoundDelayMs(delayMs);
					continue;
				}
				_packetWatch.BeginSubscribeWait();
				long attemptStartTick = Environment.TickCount64;
				Logger.WriteLine($"MQTT SUBSCRIBE 即将发出: clientId={_clientId} 尝试=重试循环 topics={SubscribedTopicFilters.Length} qos=0 等待上限={DescribeSubscribeTimeout()} 明细=[{string.Join(",", SubscribedTopicFilters)}] 时刻={MqttPacketWatch.DiagClock(attemptStartTick)}");
				try
				{
					MqttClientSubscribeResult retryResult = await retryClient.SubscribeAsync(BuildSubscribeOptions());
					if (_disposed || Volatile.Read(in _connectEpoch) != epoch)
					{
						return;
					}
					if (!_subscriptionsReady)
					{
						_subscriptionsReady = true;
						_subscribeFailureRounds = 0;
						Volatile.Write(ref _sessionReadyTick, Environment.TickCount64);
						Logger.WriteLine($"MQTT 订阅成功(保持连接的订阅重试): clientId={_clientId} topics={SubscribedTopicFilters.Length} 全部 GrantedQoS≤2 耗时={Environment.TickCount64 - attemptStartTick}ms packetId={retryResult.PacketIdentifier} 结果码=[{MqttSubscribeForensics.DescribeSubscribeResultCodes(retryResult)}] {_packetWatch.DescribeWaitOutcome()} 四元组={_diagAdapterFactory.DescribeSocket()}");
						try
						{
							await RequestInitialStateAsync();
						}
						catch (Exception ex)
						{
							Logger.WriteLine("MQTT 订阅重试后初始状态请求失败: " + MqttDiagExceptionText(ex));
						}
						if (!_disposed)
						{
							NotifyConnectionReady();
						}
					}
					return;
				}
				catch (Exception ex2)
				{
					Logger.WriteLine($"MQTT 订阅失败(重试循环): clientId={_clientId} 耗时={Environment.TickCount64 - attemptStartTick}ms 等待上限={DescribeSubscribeTimeout()} 发出判定={MqttSubscribeForensics.DescribeSendVerdict(ex2)} 连接仍在={((_client?.IsConnected ?? false) ? "是" : "否")} 例外={MqttDiagExceptionText(ex2)} {_packetWatch.DescribeWaitOutcome()} 四元组={_diagAdapterFactory.DescribeSocket()}");
					// 2026-10-09（本次修复：判死重建）：这里**不再**因为连接没了就退出循环 —— 连接被判死时
					// 记完连续失败轮数后回到循环顶部，走「判死后重建连接」分支再试一次 SUBSCRIBE。
				}
				_subscribeFailureRounds++;
				if (_subscribeFailureRounds >= MqttSubscribeRetryPolicy.RoundsBeforeFallback)
				{
					_subscribeFailureRounds = 0;
					if (TryFallbackClientId($"订阅重试连续 {MqttSubscribeRetryPolicy.RoundsBeforeFallback} 轮没成"))
					{
						await DropConnectionAndReconnectAsync("订阅重试连续失败后回退 clientId");
						return;
					}
					Logger.WriteLine($"mqtt clientId 回退已耗尽: 停在 {_clientId}（fallbackCount={_clientIdFallbackCount}/{MaxClientIdFallbacks}），不再回退，继续在保持连接上重试 SUBSCRIBE");
				}
				if (_subscriptionsReady)
				{
					return;
				}
				delayMs = MqttSubscribeRetryPolicy.NextRoundDelayMs(delayMs);
			}
		}
		catch (Exception ex3)
		{
			Logger.WriteLine("MQTT 订阅重试循环异常: " + MqttDiagExceptionText(ex3));
		}
		finally
		{
			Volatile.Write(ref _subscribeRetryLoop, 0);
		}
	}

	internal static bool ShouldApplyBuiltInCurveDefaults(MechrevoDeviceCapabilities capabilities)
	{
		ArgumentNullException.ThrowIfNull(capabilities, "capabilities");
		if (!capabilities.FanSettings)
		{
			return !capabilities.ProfileAvailable;
		}
		return true;
	}

	private void LoadDefaultCurveFromDisk()
	{
		string[] array = new string[3] { "DefaultCurve_Gaming.json", "DefaultCurve_Office.json", "DefaultCurve_Turbo.json" };
		if (!ShouldApplyBuiltInCurveDefaults(Capabilities))
		{
			Logger.WriteLine("LoadDefaultCurve skipped: the service profile denies fan settings (FanSettingsSupport=0).");
			return;
		}
		try
		{
			for (int i = 0; i < 3; i++)
			{
				string path = Path.Combine(AppContext.BaseDirectory, "Resources", array[i]);
				if (File.Exists(path))
				{
					JObject jObject = JObject.Parse(File.ReadAllText(path));
					var (array2, array3) = ParseCurve(jObject["CPU"] as JArray);
					var (array4, array5) = ParseCurve(jObject["GPU"] as JArray);
					_defaultCpuUpT[i] = array2;
					_defaultGpuUpT[i] = array4;
					_defaultCpuDuty[i] = array3;
					_defaultGpuDuty[i] = array5;
				}
			}
			CurveName = "DefaultCurve_Gaming";
			byte[] array6 = _defaultCpuUpT[0];
			if (array6 != null)
			{
				Array.Copy(array6, CpuCurveUpT, 16);
			}
			byte[] array7 = _defaultCpuDuty[0];
			if (array7 != null)
			{
				Array.Copy(array7, CpuCurveDuty, 16);
			}
			byte[] array8 = _defaultGpuUpT[0];
			if (array8 != null)
			{
				Array.Copy(array8, GpuCurveUpT, 16);
			}
			byte[] array9 = _defaultGpuDuty[0];
			if (array9 != null)
			{
				Array.Copy(array9, GpuCurveDuty, 16);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("LoadDefaultCurve fail: " + ex.Message);
		}
		Logger.WriteLine("LoadDefaultCurve done: files=" + string.Join(",", array.Select((string f) => (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", f))) ? "N" : "Y")));
	}

	internal void HandleMessage(string topic, string payload)
	{
		try
		{
			RawMessageObserved?.Invoke(topic, payload);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RawMessageObserved subscriber failed: " + ex.Message);
		}
		try
		{
			JObject o = JObject.Parse(payload);
			// 2026-10-06（jiaolong15）：灯条**原始报文**日志。既有的 LB 行只打派生值
			// （type/power/light/presence），看不出每个值取自哪个 JSON 字段，Logo 判据排查时无法对照；
			// 这里在收到灯条四主题之一时额外打一行原始 JSON。仅日志，不改任何判定，也不写任何硬件设置。
			if (IsLightbarStatusTopic(topic))
			{
				LogLightbarRawPayload(topic, o);
			}
			string text = (IsCapabilityStatusTopic(topic) ? SnapshotCapabilityFlags() : null);
			switch (topic)
			{
			case "System/CpuInfo":
				OnSystemCpuInfo(o);
				break;
			case "System/GpuInfo":
				OnSystemGpuInfo(o);
				break;
			case "System/MemoryInfo":
				OnSystemMemoryInfo(o);
				break;
			case "System/FanInfo":
				OnSystemFanInfo(o);
				break;
			case "System/BatteryInfo":
				OnSystemBatteryInfo(o);
				break;
			case "System/NetworkInfo":
				OnSystemNetworkInfo(o);
				break;
			case "System/HardwareInfo":
				OnSystemHardwareInfo(o);
				break;
			case "System/FanErrorInfo":
				OnSystemFanErrorInfo(o);
				break;
			case "HidLightbar/Status":
			case "HidLightbar_Logo/Status":
					OnLightbarOrLogoLightStatus(topic, o);
					break;
				case "HidLightbar_Hinge/Status":
				case "HidLightbar_Sync/Status":
					OnHingeOrSyncLightStatus(topic, o);
					break;
			case "Keyboard/Status":
				OnKeyboardStatus(o);
				break;
			case "BT_LC/Status":
				OnBtLcStatus(o);
				break;
			case "System/BatteryProtection":
				OnSystemBatteryProtection(o);
				break;
			case "Fan/Table":
				OnFanTable(o);
				break;
			case "GPUDevice/Status":
				OnGpuDeviceStatus(o);
				break;
			case "Settings/DeviceSwitchItemStatus":
				OnSettingsDeviceSwitchItemStatus(o);
				break;
			case "Setting/Status":
				OnSettingStatus(o);
				break;
			case "Customize/Info":
				OnCustomizeInfo(o);
				break;
			case "Display/Status":
				OnDisplayStatus(o);
				break;
			case "Fan/Status":
				OnFanStatus(o);
				break;
			case "LCHWOC/Status":
				OnLchwocStatus(o);
				break;
			}
			if (text != null)
			{
				string b = SnapshotCapabilityFlags();
				Logger.WriteLineIfChanged("resolved-caps", DescribeResolvedCapabilities());
				if (!string.Equals(text, b, StringComparison.Ordinal))
				{
					RaiseIsolated(CapabilitiesChanged, "CapabilitiesChanged");
				}
			}
			RaiseIsolated(StateChanged, "StateChanged", topic);
			if (IsTelemetryTopic(topic))
			{
				Interlocked.Exchange(ref _lastTelemetryTick, Environment.TickCount64);
				RaiseIsolated(DataChanged, "DataChanged");
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLineThrottled("parse-fail-" + topic, $"MechrevoHw parse fail {topic}: {ex2.GetType().Name}: {ex2.Message}");
		}
	}

	private int? _mqttColorCalibrationLevel;

	/// <summary>GCU 实时上报的校色档位数（Customize/Info），优先于注册表 ItemSupport。</summary>
	public int? MqttColorCalibrationLevel => _mqttColorCalibrationLevel;

	private void OnCustomizeInfo(JObject o)
	{
		string? projectId = o.GetValue("ProjectID", StringComparison.OrdinalIgnoreCase)?.ToString()
			?? o.GetValue("BIOS_PROJECT_ID", StringComparison.OrdinalIgnoreCase)?.ToString();
		// 2026-10-09：守卫 —— GCU 的 Customize/Info ProjectID 实测可能只是**纯数字**（如 "26"），
		// 那不是 BIOS 画像码。改前会无条件覆盖掉画像读到的 IDA/IDB/IDX/IDY/IDZ，
		// 于是 IsZoneProbedBios 变 false ⇒ 耀世（IDY）的铰链灯 / 同步灯带恒 hidden。
		// 现在只在「新值不是纯数字」或「当前值还不是画像码」时才接受（纯数字日志照旧打印）。
		string? candidate = string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();
		bool candidateIsNumeric = candidate != null && candidate.Length > 0;
		if (candidate != null)
		{
			foreach (char ch in candidate)
			{
				if (!char.IsAsciiDigit(ch))
				{
					candidateIsNumeric = false;
					break;
				}
			}
		}
		if (candidate != null && (!candidateIsNumeric || BiosProjectId == null) && BiosProjectId != candidate)
		{
			BiosProjectId = candidate;
			Logger.WriteLine("Customize/Info: ProjectID=" + BiosProjectId + (candidateIsNumeric ? "（纯数字，仅作记录）" : ""));
		}
		JToken? v = o["SupportColorCalibration"];
		if (v == null || v.Type != JTokenType.Integer)
		{
			return;
		}
		int level = (int)v;
		if (level <= 0)
		{
			return;
		}
		if (_mqttColorCalibrationLevel != level)
		{
			_mqttColorCalibrationLevel = level;
			Logger.WriteLine("Customize/Info: SupportColorCalibration=" + level);
		}
	}

	private void OnDisplayStatus(JObject o)
	{
		JToken sw = o["DisplaySwitch"];
		if (sw != null && sw.Type != JTokenType.Null && bool.TryParse(sw.ToString(), out bool swOn))
		{
			DisplayEffectOn = swOn;
		}
		string mode = o["CurrentSelectName"]?.ToString();
		if (!string.IsNullOrWhiteSpace(mode))
		{
			DisplayEffectMode = mode;
		}
		JArray list = o["ProfileNameList"] as JArray;
		if (list != null)
		{
			DisplayEffectProfiles.Clear();
			foreach (JObject item in list.OfType<JObject>())
			{
				string name = item["colorProfileName"]?.ToString() ?? "";
				if (name.Length == 0)
				{
					continue;
				}
				DisplayEffectProfiles[name] = new DisplayEffectProfile
				{
					Name = name,
					Brightness = OptionalInt(item, "brightness", 0),
					ColorTemp = OptionalInt(item, "ColorTemp", 0),
					ColorR = OptionalFloat(item, "ColorR", 0f),
					ColorG = OptionalFloat(item, "ColorG", 0f),
					ColorB = OptionalFloat(item, "ColorB", 0f),
					VibrantValue = OptionalFloat(item, "VibrantValue", 0f),
					Constrast = OptionalFloat(item, "Constrast", 0f),
					Gamma = OptionalFloat(item, "Gamma", 0f)
				};
			}
		}
		Logger.WriteLineIfChanged("display-effect", $"DisplayEffect: on={(DisplayEffectOn?.ToString() ?? "-")} mode={DisplayEffectMode} profiles={DisplayEffectProfiles.Count}");
	}

	private void OnSystemCpuInfo(JObject o)
	{
		CpuTemp = Int(o, "CpuTemperature");
		CpuUsage = Int(o, "CpuUsage");
		CpuFrequency = Int(o, "CpuFrequency");
		Interlocked.Exchange(ref _cpuInfoReceivedAt, Environment.TickCount64);
	}

	private void OnSystemGpuInfo(JObject o)
	{
		GpuTemp = Int(o, "GpuTemperature");
		GpuUsage = Int(o, "GpuUsage");
		GpuCoreFreq = Int(o, "GpuCoreFreq");
		VramUsedMb = Int(o, "GpuMem");
		Interlocked.Exchange(ref _gpuInfoReceivedAt, Environment.TickCount64);
	}

	private void OnSystemMemoryInfo(JObject o)
	{
		RamUsage = Int(o, "MemoryUsage");
		RamUsedGb = Double(o, "TotalUsingMemory", RamUsedGb);
	}

	private void OnSystemFanInfo(JObject o)
	{
		Interlocked.Exchange(ref _fanInfoReceivedAt, Environment.TickCount64);
		CpuFanDuty = Int(o, "CpuFanDuty");
		GpuFanDuty = Int(o, "GpuFanDuty");
		CpuFanRpm = Int(o, "CpuFanRpm");
		GpuFanRpm = Int(o, "GpuFanRpm");
	}

	private void OnSystemBatteryInfo(JObject o)
	{
		int gcuPercent = Int(o, "BatteryLifePercent");
		int winPercent = ReadWindowsBatteryLifePercent();
		BatteryPercent = (winPercent >= 0) ? winPercent : gcuPercent;
		BatteryCycleCount = OptionalInt(o, BatteryCycleCount, "BatteryCycleCount");
		BatteryAbnormal = OptionalBool(o, "BatteryAbnormal") == true;
		string text = o["BatteryCapacity"]?.ToString();
		if (text != null)
		{
			BatteryCapacityText = text;
		}
		Logger.WriteLineIfChanged("battery-info", $"BatteryInfo percent={BatteryPercent} (gcu={gcuPercent}, win={winPercent}) cycles={BatteryCycleCount} capacity={BatteryCapacityText} abnormal={BatteryAbnormal}");
	}

	private static int ReadWindowsBatteryLifePercent()
	{
		try
		{
			var sps = new SystemPowerStatus();
			if (GetSystemPowerStatus(sps) && sps.BatteryLifePercent <= 100)
			{
				return sps.BatteryLifePercent;
			}
		}
		catch
		{
		}
		return -1;
	}

	[DllImport("Kernel32")]
	private static extern bool GetSystemPowerStatus(SystemPowerStatus sps);

	[StructLayout(LayoutKind.Sequential)]
	private sealed class SystemPowerStatus
	{
		public byte ACLineStatus;
		public byte BatteryFlag;
		public byte BatteryLifePercent;
		public byte SystemStatusFlag;
		public int BatteryLifeTime;
		public int BatteryFullLifeTime;
	}

	private void OnSystemNetworkInfo(JObject o)
	{
		NetworkDownload = o["NetworkDownload"]?.ToString() ?? NetworkDownload;
		NetworkUpload = o["NetworkUpload"]?.ToString() ?? NetworkUpload;
		NetworkInfoSeen = true;
	}

	private void OnSystemHardwareInfo(JObject o)
	{
		HardwareInfoSeen = true;
		string text = FirstField(o, "ECVersion", "EcVersion", "EC_Version", "ECFWVersion")?.ToString();
		if (!string.IsNullOrWhiteSpace(text))
		{
			EcFirmwareVersion = text;
		}
		Logger.WriteLineIfChanged("hardware-info", "HardwareInfo: " + o.ToString(Formatting.None));
	}

	private void OnSystemFanErrorInfo(JObject o)
	{
		FanErrorSeen = true;
		bool fanError = false;
		foreach (JProperty item in o.Properties())
		{
			if ((item.Name.Contains("Error", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("Abnormal", StringComparison.OrdinalIgnoreCase)) && OptionalBool(o, item.Name) == true)
			{
				fanError = true;
			}
		}
		FanError = fanError;
		Logger.WriteLineIfChanged("fan-error", "FanErrorInfo: " + o.ToString(Formatting.None));
	}

	/// <summary>
	/// 2026-10-06（jiaolong15）：是不是我们要打原始报文的灯条状态主题（四个灯条通道共用一条日志）。
	/// </summary>
	private static bool IsLightbarStatusTopic(string topic)
	{
		return topic.Equals("HidLightbar/Status", StringComparison.OrdinalIgnoreCase)
			|| topic.Equals("HidLightbar_Logo/Status", StringComparison.OrdinalIgnoreCase)
			|| topic.Equals("HidLightbar_Hinge/Status", StringComparison.OrdinalIgnoreCase)
			|| topic.Equals("HidLightbar_Sync/Status", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 2026-10-06（jiaolong15）：打印灯条原始 JSON 报文，只为排查（不参与任何判定）。
	/// ① 只在日志级别 = 全部(All) 时输出（Logger.CurrentLevel 现读配置，Off/仅错误 下一行不写，避免刷屏）；
	/// ② 关键字段先单独提取一遍（type/powerStatus/power/light/brightNess），保证即使下面被截断也看得到来源；
	/// ③ 原始报文超过 <see cref="RawPayloadMaxLength"/> 字符时截断并标注 (truncated)；
	/// ④ 用 WriteInfo 而不是 WriteLine：报文里若恰好含 "fail/error" 等字样，不该被当成错误计数。
	/// </summary>
	private static void LogLightbarRawPayload(string topic, JObject o)
	{
		if (Logger.CurrentLevel != Logger.LogLevel.All)
		{
			return;
		}
		string keys = "type=" + LightbarRawField(o, "type")
			+ " powerStatus=" + LightbarRawField(o, "powerStatus")
			+ " power=" + LightbarRawField(o, "power")
			+ " light=" + LightbarRawField(o, "light")
			+ " brightNess=" + LightbarRawField(o, "brightNess");
		string raw = o.ToString(Formatting.None);
		if (raw.Length > RawPayloadMaxLength)
		{
			raw = raw.Substring(0, RawPayloadMaxLength) + "...(truncated)";
		}
		Logger.WriteInfo($"LB-RAW {topic}: keys[{keys}] raw={raw}");
	}

	private const int RawPayloadMaxLength = 700;

	private static string LightbarRawField(JObject o, string name)
	{
		JToken? token = o.GetValue(name, StringComparison.OrdinalIgnoreCase);
		if (token == null || token.Type == JTokenType.Null)
		{
			return "-";
		}
		string text = token.ToString();
		return string.IsNullOrEmpty(text) ? "-" : text;
	}

	private void OnLightbarOrLogoLightStatus(string topic, JObject o)
	{
		// 2026-10-09：先无条件记下本主题最近一次报文指纹（哪怕"无硬件证据"的报文也照记）。
		// Program 的灯带去重靠它判断灯带行与 Logo 行是不是同一块物理灯带；只读缓存，不改任何判定。
		_lightbarStatusFingerprint[topic] = LightbarFingerprint(o);
		// 2026-10-09（lightmerge）：顺带做一次只读观测 —— 两个主题的指纹是否曾经不同（独立设备证据）。
		NoteLightbarStatusDivergence(topic);
		bool flag = HasLightbarContent(o);
		// 2026-10-09（lightfix2，③）：**存在性证据**与"报文非空"分开 —— 前者要实值（powerStatus/power/light），
		// 后者只说明 GCU 回了一帧（占位帧 type=Normal 也算）。灯带行的"存在"只认前者，理由见
		// HasLightbarPresenceEvidence 的注释；type/powerStatus 的旧口径 flag 仍供 Hinge/Sync 与日志使用。
		bool lightbarPresence = HasLightbarPresenceEvidence(o);
		bool logoEvidence = false;
		string key;
		if (topic == "HidLightbar_Logo/Status")
		{
			// 2026-10-06：type 为 null 不再等于"没有这条灯带"。只要功率/开关/亮度有值即视为存在；
			// 真·空报文（所有存在性字段都空）仍视为不存在，避免把空状态当成有灯带。
			logoEvidence = HasLogoLightEvidence(o);
			LogoLightStatusSeen |= (flag && logoEvidence);
			key = "logolight";
		}
		else
		{
			// ⚠️ 2026-10-09（lightfix2）改动点：原来这里是 `|= flag`（type 非空即算存在）⇒ 占位帧
			// type=Normal 让 LightbarStatusSeen 恒真 ⇒ SupportsLightbar 的 Status 回退在"画像明确没有灯带"
			// 的机型上也成立 ⇒ 灯带行出现且照旧发帧。现在只认实值证据。
			LightbarStatusSeen |= lightbarPresence;
			key = "lightbar";
					string? lightbarType = o.GetValue("type", StringComparison.OrdinalIgnoreCase)?.ToString();
					if (!string.IsNullOrWhiteSpace(lightbarType)) { LightbarStatusType = lightbarType; }
					LightbarHingeSupport = OptionalBool(o, "HingeSupport") ?? LightbarHingeSupport;
					LightbarLogoSupport = OptionalBool(o, "LogoSupport") ?? LightbarLogoSupport;
			LightbarBaseSupport = OptionalBool(o, "BaseSupport") ?? LightbarBaseSupport;
			LightbarNewLogoSupport = OptionalBool(o, "NewlogoSupport") ?? LightbarNewLogoSupport;
			LightbarMbLogoSupport = OptionalBool(o, "MBlogoSupport") ?? LightbarMbLogoSupport;
		}
		string text = o["powerStatus"]?.ToString();
		if (text != null)
		{
			QuickSwitches[key] = string.Equals(text, "On", StringComparison.OrdinalIgnoreCase);
		}
		Logger.WriteLineIfChanged("lb-status-" + topic, (flag || logoEvidence) ? $"LB {topic}: type={o["type"]?.ToString() ?? "-"} power={text ?? "-"} light={o.GetValue("brightNess", StringComparison.OrdinalIgnoreCase)?.ToString() ?? "-"} -> presence={(topic == "HidLightbar_Logo/Status" ? (logoEvidence ? "logo-present" : "logo-absent") : (lightbarPresence ? "lightbar-present" : (flag ? "lightbar-absent(placeholder:type-only)" : "lightbar-absent")))}" : ("LB " + topic + ": no hardware evidence (type/powerStatus/power/light/brightNess all empty), raw=" + o.ToString(Formatting.None)));
	}

	private void OnHingeOrSyncLightStatus(string topic, JObject o)
	{
		bool flag = HasLightbarContent(o);
		string type = o.GetValue("type", StringComparison.OrdinalIgnoreCase)?.ToString() ?? "";
		string key;
		if (topic == "HidLightbar_Hinge/Status")
		{
			HingeLightStatusSeen |= flag;
			if (!string.IsNullOrWhiteSpace(type)) HingeStatusType = type;
			key = "hingelight";
		}
		else
		{
			SyncLightStatusSeen |= flag;
			if (!string.IsNullOrWhiteSpace(type)) SyncStatusType = type;
			key = "synclight";
		}
		string? power = o["powerStatus"]?.ToString();
		if (!string.IsNullOrWhiteSpace(power))
		{
			QuickSwitches[key] = string.Equals(power, "On", StringComparison.OrdinalIgnoreCase);
		}
		Logger.WriteLineIfChanged("lb-status-" + topic, flag ? $"LB {topic}: type={type ?? "-"} power={power ?? "-"} light={o["brightNess"]?.ToString() ?? "-"}" : ("LB " + topic + ": no hardware evidence"));
	}

	private void OnKeyboardStatus(JObject o)
	{
		KeyboardStatusSeen = true;
			LightbarHingeSupport = OptionalBool(o, "HingeSupport") ?? LightbarHingeSupport;
		string text = o["effect"]?.ToString();
		if (text != null)
		{
			KeyboardEffect = text;
		}
		string text2 = o["light"]?.ToString();
		if (text2 != null && int.TryParse(text2, out var result))
		{
			KeyboardLight = result;
		}
		string text3 = o.GetValue("brightNess", StringComparison.OrdinalIgnoreCase)?.ToString();
		int result4;
		if (text3 != null && double.TryParse(text3, NumberStyles.Float, CultureInfo.InvariantCulture, out var result2))
		{
			int legacyLevel = (int.TryParse(text2, out var result3) ? result3 : (-1));
			KeyboardBrightness = KeyboardRgb.MapReportedHardwareBrightness((int)Math.Round(result2), legacyLevel);
		}
		else if (int.TryParse(text2, out result4))
		{
			KeyboardBrightness = KeyboardRgb.MapReportedHardwareBrightness(-1, result4);
		}
		string text4 = o["speed"]?.ToString();
		if (text4 != null && int.TryParse(text4, out var result5))
		{
			KeyboardSpeed = result5;
		}
		string text5 = o["direction"]?.ToString();
		if (text5 != null)
		{
			KeyboardDirection = text5;
		}
		string text6 = o["powerStatus"]?.ToString();
		if (text6 != null)
		{
			KeyboardPower = text6 == "On";
		}
		string text7 = o["solution"]?.ToString();
		if (text7 != null)
		{
			KeyboardSolution = text7;
		}
		Interlocked.Increment(ref _keyboardStatusVersion);
		Logger.WriteLineIfChanged("kb-status", $"KB: solution={KeyboardSolution} effect={KeyboardEffect} light={KeyboardLight} brightness={KeyboardBrightness}% speed={KeyboardSpeed} direction={KeyboardDirection} power={KeyboardPower}");
	}

	private void OnBtLcStatus(JObject o)
	{
		LcStatusSeen = true;
		string text = o.GetValue("connected", StringComparison.OrdinalIgnoreCase)?.ToString();
		string text2 = o.GetValue("ConnectString", StringComparison.OrdinalIgnoreCase)?.ToString();
		if (text != null || text2 != null)
		{
			LcConnectionStateReported = true;
		}
		if (text != null)
		{
			LcReportedConnected = OptionalBool(o, "connected") ?? string.Equals(text, "Connected", StringComparison.OrdinalIgnoreCase);
			if (text2 == null)
			{
				LcConnectString = (LcReportedConnected ? "Connected" : "Disconnected");
				LcConnectStringReported = false;
			}
		}
		if (text2 != null)
		{
			LcConnectStringReported = true;
			LcConnectString = text2;
			if (text == null)
			{
				LcReportedConnected = string.Equals(text2, "Connected", StringComparison.OrdinalIgnoreCase);
			}
		}
		if (LcConnectionStateReported)
		{
			LcConnected = LcReportedConnected && (text2 == null || string.Equals(text2, "Connected", StringComparison.OrdinalIgnoreCase));
		}
		bool? flag = OptionalBool(o, "AutoConnect");
		if (flag.HasValue)
		{
			bool valueOrDefault = flag == true;
			LcAutoConnect = valueOrDefault;
		}
		flag = OptionalBool(o, "LC_action");
		if (flag.HasValue)
		{
			bool valueOrDefault2 = flag == true;
			LcActionSupported = valueOrDefault2;
			LcActionSupportReported = true;
		}
		flag = OptionalBool(o, "LC_CoolingAuto");
		if (flag.HasValue)
		{
			bool valueOrDefault3 = flag == true;
			LcCoolingAutoSupported = valueOrDefault3;
		}
		LcPumpDuty = OptionalInt(o, "PumpDuty", LcPumpDuty);
		LcFanDuty = OptionalInt(o, "FanDuty", LcFanDuty);
		flag = OptionalBool(o, "LC_MeterNormal");
		if (flag.HasValue)
		{
			bool flag2 = (LcMeterNormal = flag == true);
			if (!flag2 && LcGcuControllable && LcPumpDuty > 0)
			{
				Interlocked.Increment(ref _lcConsecutiveMeterFaults);
			}
			else
			{
				Interlocked.Exchange(ref _lcConsecutiveMeterFaults, 0);
			}
		}
		else if (!LcGcuControllable || LcPumpDuty <= 0)
		{
			Interlocked.Exchange(ref _lcConsecutiveMeterFaults, 0);
		}
		LcPumpControl = OptionalInt(o, "LC_PumpCtrl", LcPumpControl);
		LcFanControl = OptionalInt(o, "LC_FanCtrl", LcFanControl);
		string text3 = o.GetValue("DevFWVersion", StringComparison.OrdinalIgnoreCase)?.ToString();
		if (!string.IsNullOrWhiteSpace(text3))
		{
			LcFwVersion = text3;
		}
		LcLedRed = OptionalInt(o, "LCLED_R", LcLedRed);
		LcLedGreen = OptionalInt(o, "LCLED_G", LcLedGreen);
		LcLedBlue = OptionalInt(o, "LCLED_B", LcLedBlue);
		LcLedRedMinimum = OptionalInt(o, "LCLED_RMinimum", LcLedRedMinimum);
		LcLedRedMaximum = OptionalInt(o, "LCLED_RMaximum", LcLedRedMaximum);
		LcLedGreenMinimum = OptionalInt(o, "LCLED_GMinimum", LcLedGreenMinimum);
		LcLedGreenMaximum = OptionalInt(o, "LCLED_GMaximum", LcLedGreenMaximum);
		LcLedBlueMinimum = OptionalInt(o, "LCLED_BMinimum", LcLedBlueMinimum);
		LcLedBlueMaximum = OptionalInt(o, "LCLED_BMaximum", LcLedBlueMaximum);
		LcHeadLightMode = OptionalInt(o, "LCLED_Mode", LcHeadLightMode);
		LcFanLightMode = OptionalInt(o, "LCFanLED_Mode", LcFanLightMode);
		JToken value = o.GetValue("DeviceMacList", StringComparison.OrdinalIgnoreCase);
		if (value is JArray source)
		{
			_lcDeviceMacs = (from mac in source
				select mac?.ToString() into mac
				where !string.IsNullOrWhiteSpace(mac)
				select mac).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		}
		else if (value != null && value.Type == JTokenType.String)
		{
			string text4 = value.ToString();
			if (text4 != null && text4.Length > 0)
			{
				string[] array = (text4.TrimStart().StartsWith("[", StringComparison.Ordinal) ? TryParseMacArray(text4) : text4.Split(new char[2] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
				if (array.Length != 0)
				{
					_lcDeviceMacs = array.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
				}
			}
		}
		string text5 = o.GetValue("DevMACString", StringComparison.OrdinalIgnoreCase)?.ToString();
		if (!string.IsNullOrWhiteSpace(text5))
		{
			LcCurrentMac = text5;
		}
		Interlocked.Increment(ref _lcStatusVersion);
		Interlocked.Exchange(ref _lcStatusReceivedAt, Environment.TickCount64);
		Logger.WriteLineIfChanged("lc-status", $"LC: reported={LcReportedConnected} controllable={LcGcuControllable} state={LcConnectString} pump={LcPumpDuty} fan={LcFanDuty} pumpCtrl={LcPumpControl} fanCtrl={LcFanControl} auto={LcCoolingAutoSupported} fw={LcFwVersion} macs={LcDeviceMacs.Count} curMac={LcCurrentMac}");
		RaiseIsolated(LcChanged, "LcChanged");
	}

	private void OnSystemBatteryProtection(JObject o)
	{
		int num = Int(o, "HealthProtectionStatus");
		if (num >= 0 && num != BatteryProtection)
		{
			BatteryProtection = num;
			BatteryProtectionChanged?.Invoke();
		}
	}

	private void OnFanTable(JObject o)
	{
		FanCurveSeen = true;
		CurveName = o["Name"]?.ToString() ?? CurveName;
		bool? flag = OptionalBool(o, "FanControlRespective");
		bool flag2 = flag.HasValue && (!FanRespectiveSeen || FanRespective != flag.Value);
		if (flag.HasValue)
		{
			FanRespective = flag.Value;
			FanRespectiveSeen = true;
		}
		(byte[] upT, byte[] duty) tuple = ParseCurve(o["CPU"] as JArray);
		byte[] item = tuple.upT;
		byte[] item2 = tuple.duty;
		(byte[] upT, byte[] duty) tuple2 = ParseCurve(o["GPU"] as JArray);
		byte[] item3 = tuple2.upT;
		byte[] item4 = tuple2.duty;
		int num = TableNameToMode(CurveName);
		if (num < 0)
		{
			num = OperatingMode switch
			{
				0 => 1, 
				1 => 0, 
				2 => 2, 
				_ => 0, 
			};
		}
		Logger.WriteLineIfChanged("fan-table", $"Fan/Table [{CurveName}] fallbackMode={num} cpuDuty={string.Join(",", item2.Take(8))} uninitialized={IsUninitializedCurve(item2)}");
		if (num >= 0 && IsUninitializedCurve(item2))
		{
			byte[] array = _defaultCpuDuty[num];
			if (array != null)
			{
				byte[] array2 = _defaultCpuUpT[num];
				if (array2 != null)
				{
					Array.Copy(array2, item, 16);
					Array.Copy(array, item2, 16);
					Logger.WriteLine("fallback CPU -> duty=" + string.Join(",", item2.Take(8)) + " upT=" + string.Join(",", item.Take(8)));
				}
			}
		}
		if (num >= 0 && IsUninitializedCurve(item4))
		{
			byte[] array3 = _defaultGpuDuty[num];
			if (array3 != null)
			{
				byte[] array4 = _defaultGpuUpT[num];
				if (array4 != null)
				{
					Array.Copy(array4, item3, 16);
					Array.Copy(array3, item4, 16);
					Logger.WriteLine("fallback GPU -> duty=" + string.Join(",", item4.Take(8)) + " upT=" + string.Join(",", item3.Take(8)));
				}
			}
		}
		CpuCurveUpT = item;
		CpuCurveDuty = item2;
		GpuCurveUpT = item3;
		GpuCurveDuty = item4;
		string text = CurveName + "|" + string.Join(",", CpuCurveDuty) + "|" + string.Join(",", GpuCurveDuty);
		if (text != _lastCurveHash)
		{
			_lastCurveHash = text;
			RaiseIsolated(CurveUpdated, "CurveUpdated");
		}
		if (flag2)
		{
			RaiseIsolated(CustomModeChanged, "CustomModeChanged");
		}
		Interlocked.Increment(ref _fanTableVersion);
		if (OperatingMode == 2 && TurboFanCurveIsPathological)
		{
			RepairPathologicalTurboFanCurveAsync();
		}
	}

	private void OnGpuDeviceStatus(JObject o)
	{
		GpuDeviceStatusSeen = true;
		if (o["currentHZList"] is JArray source)
		{
			_hzList = (from value in (from h in source
					select int.TryParse(h?.ToString(), out var result) ? result : 0 into value
					where value > 0
					select value).Distinct()
				orderby value descending
				select value).ToArray();
		}
		CurrentHz = Int(o, "currentHZ");
		GpuSaveMode = Int(o, "currentSaveingMode");
		bool? flag = OptionalBool(o, "DC_HZ");
		if (flag.HasValue)
		{
			DcHz = flag.Value;
			DcHzSeen = true;
		}
		Logger.WriteLine($"GPUDevice/Status parsed: hasHZList={o["currentHZList"] != null} hzList=[{string.Join(",", _hzList)}] currentHz={CurrentHz} gpuSaveMode={GpuSaveMode} dcHzSeen={DcHzSeen} dcHz={(DcHzSeen ? DcHz.ToString() : "?")}");
		RefreshRefreshRateFromActiveDisplay();
	}

	// 2026-10-09（多显示器刷新率修复）
	// 机主反馈：「针对多显示屏切换刷新率的处理不太行」「从外接显示器切回电脑内屏的时候刷新率不可用」
	// 「没有我电脑内屏那一档的刷新率()」；随后机主更正「外接屏幕300 极光x内屏是180的」。
	// 根因：currentHZList / currentHZ 是「GCU 报什么就是什么」的单一列表，不区分"当前活动显示器"：
	// 接外接屏时 GCU 上报的可能是外接屏的档位 ⇒ 切回内屏后列表里没有内屏那一档、当前值也对不上。
	// 处理：在「GCU 上报 GPUDevice/Status」与「显示配置变化」两个时机，用**当前活动内屏**的系统侧实测值校正：
	//   ① 用内屏实测最大刷新率过滤掉物理不可能的档位（历史问题：GCU 报 300 而面板最高 180）；
	//   ② 把内屏实测可用档位并入列表，保证内屏档位一定在（±2Hz 视为同一档，规避 59.94 取整出 59/60 两档）；
	//   ③ 当前值以当前活动内屏的真实刷新率（EnumDisplaySettings 实测）为准，不再沿用别的屏的数值。
	// ⚠️ **取屏口径是本修复的前提**：必须严格锁定内屏 —— 只接受 `ScreenNative` 判定为
	//    `InternalScreenConfidence.Embedded`（输出技术明确是 INTERNAL / DISPLAYPORT_EMBEDDED＝eDP）的那块屏；
	//    只靠型号名匹配（UniqueName）、认不出（None）、或"退化为 primary"时**一律不校正** ——
	//    否则会把外接屏/虚拟屏（极光X 外接屏是 300Hz）的档位塞进内屏这一行、并用它的刷新率当"当前值"（那是回归）。
	// 内屏读不到或 GCU 从未上报过状态时同样一律不动 —— 保持
	// 「读不到状态就不允许切换」的既有护栏（SwitchRefreshRate 依旧要求 HzList 命中）。
	internal bool RefreshRefreshRateFromActiveDisplay()
	{
		if (!GpuDeviceStatusSeen)
		{
			return false;
		}
		lock (_hzCorrectionLock)
		{
			try
			{
				string? laptopScreen = MechrevoLite.Display.ScreenNative.FindLaptopScreen(out MechrevoLite.Display.ScreenNative.InternalScreenConfidence screenConfidence, out string screenTechnology);
				if (screenConfidence != MechrevoLite.Display.ScreenNative.InternalScreenConfidence.Embedded)
				{
					LogHzScreenIdentify(laptopScreen, screenConfidence, screenTechnology, null, -1);
					return false;
				}
				int realHz = MechrevoLite.Display.ScreenNative.GetRefreshRate(laptopScreen);
				if (realHz <= 0)
				{
					LogHzScreenIdentify(laptopScreen, screenConfidence, screenTechnology, null, -1);
					return false;
				}
				IReadOnlyList<int> measured = MechrevoLite.Display.ScreenNative.GetAvailableRefreshRates(laptopScreen);
				int maxHz = 0;
				foreach (int hz in measured)
				{
					if (hz > maxHz)
					{
						maxHz = hz;
					}
				}
				LogHzScreenIdentify(laptopScreen, screenConfidence, screenTechnology, measured, realHz);
				int gcuHz = CurrentHz;
				string gcuList = string.Join(",", _hzList);
				List<int> list = new List<int>();
				foreach (int hz in _hzList)
				{
					if (maxHz <= 0 || hz <= maxHz)
					{
						AddNearHz(list, hz);
					}
				}
				foreach (int hz in measured)
				{
					AddNearHz(list, hz);
				}
				AddNearHz(list, realHz);
				list.Sort((int a, int b) => b.CompareTo(a));
				int current = ((CurrentHz > 0 && list.Any((int hz) => Math.Abs(hz - CurrentHz) <= 2)) ? CurrentHz : list.First((int hz) => Math.Abs(hz - realHz) <= 2));
				bool changed = current != CurrentHz || !list.SequenceEqual(_hzList);
				_hzList = list.ToArray();
				CurrentHz = current;
				if (changed)
				{
					Logger.WriteLine($"Hz corrected for active display {laptopScreen}: gcu=[{gcuList}] gcuHz={gcuHz} measured=[{string.Join(",", measured)}] max={maxHz} real={realHz} -> hzList=[{string.Join(",", _hzList)}] currentHz={CurrentHz}");
				}
				return changed;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Hz correction failed: " + ex.Message);
				return false;
			}
		}
	}

	// 把 hz 并入档位列表：已有 ±2Hz 内的档位视为同一档（规避 59.94 取整出现 59 与 60 两个按钮）。
	private static void AddNearHz(List<int> list, int hz)
	{
		if (hz <= 0)
		{
			return;
		}
		foreach (int item in list)
		{
			if (Math.Abs(item - hz) <= 2)
			{
				return;
			}
		}
		list.Add(hz);
	}

	// 2026-10-09：把"这次校正作用在哪块屏、可信度如何、实测档位是什么"留一条日志 ——
	// 只在识别结果/实测档位变化时写（不刷屏），这是真机多显示器验证唯一需要的证据。
	// confidence=Embedded（eDP/INTERNAL）才会校正；None/UniqueName 一律不校正并把原因写清楚。
	private void LogHzScreenIdentify(string? screen, MechrevoLite.Display.ScreenNative.InternalScreenConfidence confidence, string technology, IReadOnlyList<int>? measured, int realHz)
	{
		string text = (screen ?? "(null)") + "|" + confidence + "|" + technology + "|" + ((measured == null) ? "-" : string.Join(",", measured)) + "|" + realHz;
		if (text == _hzScreenDiagKey)
		{
			return;
		}
		_hzScreenDiagKey = text;
		Logger.WriteLine($"Hz screen identify: device={screen ?? "(null)"} tech={technology} confidence={confidence} measured=[{((measured == null) ? "-" : string.Join(",", measured))}] real={realHz} (仅 Embedded 才参与校正)");
	}

	private void OnSettingsDeviceSwitchItemStatus(JObject o)
	{
		DeviceSwitchStatusSeen = true;
		int num = Int(o, "ScreenBrightness");
		if (num >= 0)
		{
			ScreenBrightness = num;
			ScreenBrightnessSeen = true;
		}
		bool? flag = OptionalBool(o, "TochpadEnable");
		if (flag.HasValue)
		{
			bool valueOrDefault = flag == true;
			QuickSwitches["touchpad"] = valueOrDefault;
			TouchpadSeen = true;
		}
		flag = OptionalBool(o, "WIFIEnable");
		if (flag.HasValue)
		{
			bool valueOrDefault2 = flag == true;
			QuickSwitches["wifi"] = valueOrDefault2;
			WifiSeen = true;
		}
		flag = OptionalBool(o, "BTEnable");
		if (flag.HasValue)
		{
			bool valueOrDefault3 = flag == true;
			QuickSwitches["bt"] = valueOrDefault3;
			BluetoothSeen = true;
		}
		flag = OptionalBool(o, "WebCamEnable");
		if (flag.HasValue)
		{
			bool valueOrDefault4 = flag == true;
			QuickSwitches["webcam"] = valueOrDefault4;
			WebcamSeen = true;
		}
	}

	private void OnSettingStatus(JObject o)
	{
		SettingStatusSeen = true;
		bool? lcdOverdriveSupport = FirstOptionalBool(o, "LCDOverdriveSupport", "LcdOverdriveSupport");
		if (lcdOverdriveSupport.HasValue)
		{
			LcdOverdriveSupport = lcdOverdriveSupport;
		}
		bool? localDimmingSupport = FirstOptionalBool(o, "LocalDimmingSupport");
		if (localDimmingSupport.HasValue)
		{
			LocalDimmingSupport = localDimmingSupport;
		}
		string text = o["LocalDimmingSwitch"]?.ToString();
		if (text != null)
		{
			LocalDimming = text == "LOCALDIMMING_ON";
			if (LocalDimmingSupport != false)
			{
				LocalDimmingSeen = true;
			}
		}
		string text2 = o["LCDOverdriveSwitch"]?.ToString();
		if (text2 != null)
		{
			LcdOverdrive = text2 == "LCDOverdrive_ON";
			if (LcdOverdriveSupport != false)
			{
				LcdOverdriveSeen = true;
			}
		}
		JToken token = FirstField(o, "ColorCalibrationSwitch", "ColorCalibrationSwitch_Status", "ColorCalibrationStatus");
		bool? flag = null;
		bool flag2 = false;
		try
		{
			flag = MechrevoService.ResolveColorCalibrationSwitch(MechrevoService.TryReadColorCalibrationOn(), ParseColorCalibrationSwitch(token));
			if (flag.HasValue)
			{
				ColorCalibrationSwitch = flag.Value;
				ColorCalibrationSwitchSeen = true;
				ColorCalibrationSeen = true;
				flag2 = true;
			}
			else if (!ColorCalibrationSwitchSeen)
			{
				ColorCalibrationSwitch = MechrevoService.IsColorCalibrationOn();
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("cc-registry", "ColorCalibration registry read failed: " + ex.Message);
		}
		int num = ParseColorCalibrationMode(FirstField(o, "CurrentColorCalibration", "ColorCalibrationMode", "ColorCalibration"));
		if (num >= 1 && num <= 4)
		{
			ColorCalibrationMode = num;
			ColorCalibrationModeSeen = true;
			ColorCalibrationSeen = true;
			flag2 = true;
		}
		string text3 = o["ColorCalibrationResultCode"]?.ToString();
		if (text3 != null && int.TryParse(text3, out var result))
		{
			ColorCalibrationResult = result;
			ColorCalibrationSeen = true;
			flag2 = true;
		}
		if (flag2)
		{
			Interlocked.Increment(ref _colorCalibrationStatusVersion);
		}
		int? num2 = null;
		string text4 = o["CloseTimer"]?.ToString();
		if (text4 != null && int.TryParse(text4, out var result2) && result2 != CloseTimerMinutes)
		{
			CloseTimerMinutes = result2;
			Logger.WriteLine($"CloseTimer -> {result2} 分钟");
			num2 = result2;
		}
		string text5 = o["UsbCharger"]?.ToString();
		if (text5 != null)
		{
			UsbCharger = !text5.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			UsbChargerSeen = true;
		}
		string text6 = o["OSD"]?.ToString();
		if (text6 != null)
		{
			QuickSwitches["osd"] = text6.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			OsdSeen = true;
		}
		string text7 = o["WinKey"]?.ToString();
		if (text7 != null)
		{
			QuickSwitches["winkey"] = !text7.Contains("UNLOCK", StringComparison.OrdinalIgnoreCase);
			WinKeySeen = true;
		}
		string text8 = o["FnKey"]?.ToString();
		if (text8 != null)
		{
			QuickSwitches["fnkey"] = !text8.Contains("UNLOCK", StringComparison.OrdinalIgnoreCase);
			FnKeySeen = true;
		}
		string text9 = o["NumPad"]?.ToString();
		if (text9 != null)
		{
			QuickSwitches["numpad"] = !text9.Contains("UNLOCK", StringComparison.OrdinalIgnoreCase);
			NumpadSeen = true;
		}
		string text10 = o["BatteryLogo_Status"]?.ToString();
		if (!string.IsNullOrWhiteSpace(text10))
		{
			QuickSwitches["batterylogo"] = !text10.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			BatteryLogoSeen = true;
		}
		string text11 = o["TouchpadToggle"]?.ToString();
		if (text11 != null)
		{
			QuickSwitches["touchpadtoggle"] = !text11.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			TouchpadToggleSeen = true;
		}
		string text12 = o["SingleColorKBBL"]?.ToString();
		if (text12 != null)
		{
			QuickSwitches["singlecolorkb"] = !text12.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			SingleColorKbSeen = true;
		}
		string text13 = o["UniSwitch"]?.ToString();
		string text14 = o["OmniSwitch"]?.ToString();
		if (text13 != null)
		{
			QuickSwitches["uni"] = !text13.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			UniOmniSeen = true;
		}
		if (text14 != null)
		{
			QuickSwitches["omni"] = !text14.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			UniOmniSeen = true;
		}
		string text15 = o["PowerLightSwitch"]?.ToString();
		if (text15 != null)
		{
			QuickSwitches["powerlight"] = !text15.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			PowerLightSeen = true;
		}
		if (HasField(o, "PowerLightBrightness"))
		{
			int num3 = Int(o, "PowerLightBrightness");
			if (num3 >= 0)
			{
				PowerLightBrightness = Math.Clamp(num3, 0, 100);
			}
		}
		string text16 = o["HighPerformancePowerModeSwitch"]?.ToString();
		if (text16 != null)
		{
			QuickSwitches["highperf"] = !text16.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			HighPerformanceSeen = true;
		}
		string text17 = o["AcRecoverySwitch_Status"]?.ToString();
		if (text17 != null)
		{
			QuickSwitches["acrecovery"] = !text17.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			AcRecoverySeen = true;
		}
		string text18 = o["DeepSleepSwitch"]?.ToString();
		if (text18 != null)
		{
			QuickSwitches["deepsleep"] = !text18.Contains("OFF", StringComparison.OrdinalIgnoreCase);
			DeepSleepSeen = true;
		}
		string text19 = o["CopilotKey"]?.ToString();
		if (text19 != null)
		{
			QuickSwitches["copilot"] = !text19.Contains("UNLOCK", StringComparison.OrdinalIgnoreCase);
			CopilotSeen = true;
		}
		string text20 = o["DeepSleepTime"]?.ToString();
		if (text20 != null && int.TryParse(text20, out var result3))
		{
			DeepSleepTime = result3;
		}
		string text21 = o["DisplayMode"]?.ToString();
		if (!string.IsNullOrWhiteSpace(text21))
		{
			DisplayColorMode = text21;
		}
		string text22 = o["DisplayFeatureStatus"]?.ToString();
		if (!string.IsNullOrWhiteSpace(text22))
		{
			DisplayFeatureOn = !text22.Contains("OFF", StringComparison.OrdinalIgnoreCase);
		}
		string text23 = o["DGpu"]?.ToString();
		if (!string.IsNullOrWhiteSpace(text23))
		{
			NvControlPanelPreference = text23;
		}
		string[] displayColorParameterFields = DisplayColorParameterFields;
		foreach (string key in displayColorParameterFields)
		{
			int num4 = OptionalInt(o, key, int.MinValue);
			if (num4 != int.MinValue)
			{
				_displayColorParameters[key] = num4;
			}
		}
		if (DisplayColorMode.Length > 0 || DisplayFeatureOn.HasValue || NvControlPanelPreference.Length > 0 || _displayColorParameters.Count > 0)
		{
			Logger.WriteLineIfChanged("display-color", $"Display: mode={((DisplayColorMode.Length > 0) ? DisplayColorMode : "-")} feature={((!DisplayFeatureOn.HasValue) ? "-" : (DisplayFeatureOn.Value ? "On" : "Off"))} nvPanel={((NvControlPanelPreference.Length > 0) ? NvControlPanelPreference : "-")} params={{{string.Join(",", _displayColorParameters.Select((KeyValuePair<string, int> p) => p.Key + "=" + p.Value))}}}");
		}
		string text24 = FirstField(o, "DiscreteGpuDirectConnectionSwitch_Status", "DGpuDirectConnectionSwitch_Status", "DGPU_DIRECT_STATUS")?.ToString();
		string text25 = FirstField(o, "IGpuOnlyConnectionSwitch_Status", "IGPUOnlyConnectionSwitch_Status", "IGPU_ONLY_STATUS")?.ToString();
		bool flag3 = !string.IsNullOrWhiteSpace(text24) || !string.IsNullOrWhiteSpace(text25);
		// 2026-10-06（gcu 门）：顺手记下**原文取值**（Support / NotSupport / …），只用于
		// "GCU 明确说不支持独显直连 -> 隐藏显卡行"时写一行可读日志（见 CanOfferGpuModeSwitch）。
		// 解析规则完全没变：仍是 FirstOptionalBool（= 逐个键做 ParseFlexibleBool，认不出就换下一个键）。
		string? gcuDgpuDirectSupportRaw = FirstField(o, "DiscreteGpuDirectConnectionSwitch_Support", "DGpuDirectConnectionSwitch_Support", "DGPU_DIRECT_SUPPORT")?.ToString()?.Trim();
		bool? gcuDgpuDirectSupportParsed = FirstOptionalBool(o, "DiscreteGpuDirectConnectionSwitch_Support", "DGpuDirectConnectionSwitch_Support", "DGPU_DIRECT_SUPPORT");
		DgpuDirectStatusSupport = gcuDgpuDirectSupportParsed ?? DgpuDirectStatusSupport;
		if (gcuDgpuDirectSupportParsed.HasValue && ParseFlexibleBool(gcuDgpuDirectSupportRaw) == gcuDgpuDirectSupportParsed)
		{
			GcuDgpuDirectSupportRaw = gcuDgpuDirectSupportRaw;
		}
		IgpuOnlyStatusSupport = FirstOptionalBool(o, "IGpuOnlyConnectionSwitch_Support", "IGPUOnlyConnectionSwitch_Support", "IGPU_ONLY_SUPPORT") ?? IgpuOnlyStatusSupport;
		if (IsRecognizedDgpuDirectStatus(text24))
		{
			bool? dgpuDirectStatusSupport = DgpuDirectStatusSupport;
			bool valueOrDefault = dgpuDirectStatusSupport == true;
			if (!dgpuDirectStatusSupport.HasValue)
			{
				valueOrDefault = true;
				bool? dgpuDirectStatusSupport2 = valueOrDefault;
				DgpuDirectStatusSupport = dgpuDirectStatusSupport2;
			}
		}
		if (IsRecognizedIgpuOnlyStatus(text25))
		{
			bool? dgpuDirectStatusSupport = IgpuOnlyStatusSupport;
			bool valueOrDefault = dgpuDirectStatusSupport == true;
			if (!dgpuDirectStatusSupport.HasValue)
			{
				valueOrDefault = true;
				bool? dgpuDirectStatusSupport2 = valueOrDefault;
				IgpuOnlyStatusSupport = dgpuDirectStatusSupport2;
			}
		}
		string text26 = o.GetValue("CheckDGpuStatusforIGpuOnlyOnSuccess", StringComparison.OrdinalIgnoreCase)?.ToString();
		bool flag4 = !string.IsNullOrWhiteSpace(text26);
		if (flag4)
		{
			GpuSwitchResult = (text26.Contains('1') ? 1 : (text26.Contains('2') ? 2 : 0));
			GpuSwitchResultReported = true;
		}
		else
		{
			string text27 = o.GetValue("CheckDGpuStatusforIGpuOnlySwitch", StringComparison.OrdinalIgnoreCase)?.ToString();
			if (!string.IsNullOrWhiteSpace(text27) && int.TryParse(text27.Trim(), out var result4))
			{
				GpuSwitchResult = ((result4 == 85) ? 2 : ((result4 == 170) ? 1 : 0));
				GpuSwitchResultReported = true;
			}
		}
		int igpuOnlySave = ResolveIgpuOnlySaveMode(text25);
		if (igpuOnlySave >= 0)
		{
			IgpuOnlySaveMode = igpuOnlySave;
		}
		// 2026-10-05：不再传 text25（核显省电字段）—— 显示路由与核显省电无关（§9.6）
		int num5 = ResolveGpuModeStatus(GpuMode, text24);
		string text28 = FirstField(o, "IGpuCannotBeSwitchNowVisibility")?.ToString();
		if (!string.IsNullOrWhiteSpace(text28))
		{
			IgpuSwitchBlocked = num5 != 2 && !text28.Contains("false", StringComparison.OrdinalIgnoreCase);
		}
		bool flag5 = num5 != GpuMode;
		if (flag5)
		{
			GpuMode = num5;
		}
		if (flag4)
		{
			Interlocked.Increment(ref _gpuSwitchResultVersion);
		}
		if (flag3)
		{
			Interlocked.Increment(ref _gpuModeStatusVersion);
		}
		Interlocked.Increment(ref _settingStatusVersion);
		if (flag5)
		{
			Logger.WriteLine($"GpuMode -> {num5} (dgpu={text24 ?? "-"}, igpu={text25 ?? "-"})");
		}
		if (num2.HasValue)
		{
			RaiseIsolated(argument: num2.GetValueOrDefault(), handlers: CloseTimerChanged, eventName: "CloseTimerChanged");
		}
		if (flag5)
		{
			RaiseIsolated(GpuModeChanged, "GpuModeChanged");
		}
	}

	private void OnFanStatus(JObject o)
	{
		int num = Int(o, "OperatingMode");
		int num2 = OptionalInt(o, "CustomProfileIndex", -1);
		PendingModeSwitch pendingModeSwitch = Volatile.Read(in _pendingModeSwitch);
		if ((object)pendingModeSwitch != null && Environment.TickCount64 <= pendingModeSwitch.ExpiresAtTick)
		{
			bool conflicting = false;
			if (num >= 0 && num != pendingModeSwitch.ExpectedOperatingMode)
			{
				conflicting = true;
			}
			else if (pendingModeSwitch.ExpectedCustomProfile.HasValue)
			{
				int expectedProfile = pendingModeSwitch.ExpectedCustomProfile.GetValueOrDefault();
				if (num2 >= 0 && (num == 3 || num < 0) && num2 != expectedProfile)
				{
					conflicting = true;
				}
			}
			if (conflicting)
			{
				Logger.WriteLineThrottled("stale-op-mode", $"Ignored stale mode/profile {num}/{num2}; pending={pendingModeSwitch.ExpectedOperatingMode}/{pendingModeSwitch.ExpectedCustomProfile}", 500);
				return;
			}
		}
		FanStatusSeen = true;
		IsAC = OptionalBool(o, "IsAC") ?? IsAC;
		bool? flag = OptionalBool(o, "FanBoostEnable");
		if (flag.HasValue)
		{
			FanBoost = flag.Value;
			FanBoostSeen = true;
		}
		int? num2Handled = null;
		if (num != OperatingMode && num >= 0)
		{
			(bool Active, int ExpectedOperatingMode) modeSwitchPendingState = GetModeSwitchPendingState();
			bool item = modeSwitchPendingState.Active;
			int item2 = modeSwitchPendingState.ExpectedOperatingMode;
			bool flag2 = item && item2 == num;
			if (item && item2 >= 0 && num != item2)
			{
				Logger.WriteLineThrottled("stale-op-mode", $"Ignored stale opMode {num}; pending={item2}", 500);
			}
			else
			{
				Logger.WriteLine($"opMode {OperatingMode}->{num} pending={item} expected={flag2}");
				OperatingMode = num;
				if (!item | flag2)
				{
					num2Handled = num;
				}
			}
		}
		int num3 = Int(o, "CPU_PL1");
		int num4 = Int(o, "CPU_PL2");
		bool? amdPlatformFlag = FirstOptionalBool(o, "IsAMDPlatform", "IsAmdPlatform");
		if (amdPlatformFlag.HasValue)
		{
			AmdPlatformReported = amdPlatformFlag.Value;
		}
		bool? amdPlatformReported2 = AmdPlatformReported;
		bool usesAmdByFields;
		if (!amdPlatformReported2.HasValue)
		{
			usesAmdByFields = Capabilities.AmdPlatform || HasUsablePowerValue(o, "CPU_AmdSPL") || HasUsablePowerValue(o, "CPU_AmdSPPT") || HasUsablePowerValue(o, "CPU_AmdFPPT");
		}
		else
		{
			usesAmdByFields = amdPlatformReported2 == true;
		}
		if (usesAmdByFields)
		{
			AmdPowerStatusSeen = true;
		}
		CpuAmdSpl = OptionalUsablePowerValue(o, "CPU_AmdSPL", CpuAmdSpl);
		CpuAmdSppt = OptionalUsablePowerValue(o, "CPU_AmdSPPT", CpuAmdSppt);
		CpuAmdFppt = OptionalUsablePowerValue(o, "CPU_AmdFPPT", CpuAmdFppt);
		Pl1 = ((UsesAmdPowerFields && CpuAmdSpl > 0) ? CpuAmdSpl : num3);
		Pl2 = ((UsesAmdPowerFields && CpuAmdSppt > 0) ? CpuAmdSppt : num4);
		TccOffset = Int(o, "CPU_TccOffset");
		if (HasField(o, "CPU_TccOffset") || HasField(o, "CPU_TccOffsetMinimum") || HasField(o, "CPU_TccOffsetMaximum") || HasField(o, "CPU_TccOffsetSwitch") || HasField(o, "CPU_AmdTccTarget"))
		{
			TccStatusSeen = true;
		}
		Pl1Minimum = OptionalInt(o, Pl1Minimum, "CPU_AmdSPLMinimum", "CPU_AmdSPLMin", "CPU_AmdSPL_Minimum", "CPU_AmdSPL_Min", "CPU_PL1Minimum");
		Pl1Maximum = OptionalInt(o, Pl1Maximum, "CPU_AmdSPLMaximum", "CPU_AmdSPLMax", "CPU_AmdSPL_Maximum", "CPU_AmdSPL_Max", "CPU_PL1Maximum");
		Pl2Minimum = OptionalInt(o, Pl2Minimum, "CPU_AmdSPPTMinimum", "CPU_AmdSPPTMin", "CPU_AmdSPPT_Minimum", "CPU_AmdSPPT_Min", "CPU_PL2Minimum");
		Pl2Maximum = OptionalInt(o, Pl2Maximum, "CPU_AmdSPPTMaximum", "CPU_AmdSPPTMax", "CPU_AmdSPPT_Maximum", "CPU_AmdSPPT_Max", "CPU_PL2Maximum");
		TccRawMinimum = OptionalInt(o, "CPU_TccOffsetMinimum", TccRawMinimum);
		TccRawMaximum = OptionalInt(o, "CPU_TccOffsetMaximum", TccRawMaximum);
		GpuTgpMinimum = OptionalInt(o, "GPU_ConfigurableTGPMinimum", GpuTgpMinimum);
		GpuTgpMaximum = OptionalInt(o, "GPU_ConfigurableTGPMaximum", GpuTgpMaximum);
		GpuDbMinimum = OptionalInt(o, "GPU_DynamicBoostMinimum", GpuDbMinimum);
		GpuDbMaximum = OptionalInt(o, "GPU_DynamicBoostMaximum", GpuDbMaximum);
		UpdateGpuOffsetRanges(o);
		TableName = o["FAN_TableName"]?.ToString() ?? TableName;
		JToken jToken2 = o["ProfileName"];
		if (jToken2 != null && jToken2.Type == JTokenType.String)
		{
			ProfileName = jToken2.ToString();
		}
		string text = o["CustomProfileIndex"]?.ToString();
		if (text != null && int.TryParse(text, out var result))
		{
			CustomProfileIndex = result;
		}
		int num5 = OptionalUsablePowerValue(o, "GPU_ConfigurableTGPTarget", -1);
		if (num5 > 0)
		{
			GpuTgp = num5;
		}
		string text2 = o["GPU_DynamicBoostSwitch"]?.ToString();
		if (text2 != null)
		{
			GpuDbSwitch = text2 == "1";
		}
		int num6 = OptionalUsablePowerValue(o, "GPU_DynamicBoost", -1);
		if (num6 > 0)
		{
			GpuDb = num6;
		}
		string text3 = o["CPU_TccOffsetSwitch"]?.ToString();
		if (text3 != null)
		{
			TccSwitch = text3 == "1";
		}
		string text4 = o["TjMax"]?.ToString();
		if (text4 != null && int.TryParse(text4, out var result2) && result2 > 0)
		{
			TjMax = result2;
		}
		int num7 = OptionalInt(o, "CPU_AmdTccTarget", -1);
		if (IsAmdPlatformForLimits)
		{
			TccMinimum = 85;
			TccMaximum = ((TccRawMaximum > 0) ? TccRawMaximum : 95);
			if (TccMinimum >= TccMaximum)
			{
				TccMinimum = Math.Max(0, TccMaximum - 10);
			}
			goto IL_066f;
		}
		int num8 = ((TjMax > 0) ? TjMax : 100);
		int num9;
		if (TccRawMinimum == 0)
		{
			num9 = ((TccRawMaximum == 0) ? 1 : 0);
			if (num9 != 0)
			{
				goto IL_060a;
			}
		}
		else
		{
			num9 = 0;
		}
		if (TccRawMaximum < 0)
		{
			goto IL_060a;
		}
		int num10 = num8 - TccRawMaximum;
		goto IL_0617;
		IL_066f:
		int num11;
		if (num7 >= 0)
		{
			num11 = num7;
		}
		else
		{
			num11 = ((TccOffset >= 0) ? TccTargetFromRaw(TccOffset) : (-1));
		}
		TccTarget = ((num11 >= 0) ? Math.Clamp(num11, TccMinimum, TccMaximum) : (-1));
		string text5 = o["CPU_PL4_Double_Flag"]?.ToString();
		if (text5 != null)
		{
			Pl4Double = text5 == "1";
		}
		_pl4Raw = OptionalUsablePowerValue(o, "CPU_PL4", _pl4Raw);
		_pl4RawMinimum = FirstUsablePowerValue(o, _pl4RawMinimum, "CPU_PL4Minimum", "CPU_PL4Min", "CPU_PL4_Minimum", "CPU_PL4_Min");
		_pl4RawMaximum = FirstUsablePowerValue(o, _pl4RawMaximum, "CPU_PL4Maximum", "CPU_PL4Max", "CPU_PL4_Maximum", "CPU_PL4_Max");
		int num12 = ((!Pl4Double) ? 1 : 2);
		int num13 = ((_pl4Raw >= 0) ? (_pl4Raw * num12) : (-1));
		Pl4 = ((UsesAmdPowerFields && CpuAmdFppt > 0) ? CpuAmdFppt : num13);
		Pl4Minimum = ((_pl4RawMinimum >= 0) ? (_pl4RawMinimum * num12) : (-1));
		Pl4Maximum = ((_pl4RawMaximum >= 0) ? (_pl4RawMaximum * num12) : (-1));
		bool? flag3 = FirstOptionalBool(o, "FAN_FanSwitchSpeedEnabled", "FanSwitchSpeedEnabled");
		if (flag3.HasValue)
		{
			FanSwitchSpeedEnabled = flag3.Value;
			FanSwitchSpeedSeen = true;
		}
		int num14 = OptionalInt(o, -1, "FAN_FanSwitchSpeed", "FanSwitchSpeed");
		if (num14 >= 0)
		{
			FanSwitchSpeed = num14;
			FanSwitchSpeedSeen = true;
		}
		int num15 = OptionalInt(o, -1, "FAN_FanSwitchSpeedMinimum", "FAN_FanSwitchSpeedMin", "FanSwitchSpeedMinimum");
		int num16 = OptionalInt(o, -1, "FAN_FanSwitchSpeedMaximum", "FAN_FanSwitchSpeedMax", "FanSwitchSpeedMaximum");
		if (num15 >= 0)
		{
			FanSwitchSpeedMinimum = num15;
		}
		if (num16 >= 0)
		{
			FanSwitchSpeedMaximum = num16;
		}
		bool? flag4 = OptionalBool(o, "GameWhitelistSwitch");
		if (flag4.HasValue)
		{
			bool valueOrDefault = flag4 == true;
			QuickSwitches["gamewhitelist"] = valueOrDefault;
			GameWhitelistSeen = true;
		}
		JToken jToken = FirstField(o, "CPU_PerformanceAndOverClockMenuSwitch", "CPUPerformanceAndOverClockMenuSwitch");
		if (jToken != null)
		{
			QuickSwitches["cpuadvperf"] = ParseFlexibleBool(jToken.ToString()) == true;
			CpuAdvancedPerformanceSeen = true;
		}
		bool? overclockMenuSupport = FirstOptionalBool(o, "OcSupport", "OcSettingsSupport");
		if (overclockMenuSupport.HasValue)
		{
			OverclockMenuSupport = overclockMenuSupport;
		}
		bool? whisperModeSupport = OptionalBool(o, "GPU_WhisperModeSupport");
		if (whisperModeSupport.HasValue)
		{
			WhisperModeSupport = whisperModeSupport;
		}
		if (HasField(o, "GPU_WhisperModeSwitch"))
		{
			WhisperMode = OptionalBool(o, "GPU_WhisperModeSwitch") == true;
			WhisperModeSeen = true;
		}
		WhisperModeLevel = OptionalInt(o, WhisperModeLevel, "GPU_WhisperModeSetting");
		bool? flag5 = FirstOptionalBool(o, "OverClockingSwitch", "OverclockingSwitch", "GPU_OverClockingSwitch", "GPU_OverclockingSwitch");
		if (flag5.HasValue)
		{
			OcSwitch = flag5.Value;
		}
		GpuCoreClockOffset = OptionalInt(o, GpuCoreClockOffset, "GPU_CoreClockOffsetOC", "GPU_CoreClockOffset", "GpuCoreClockOffsetOC", "GpuCoreClockOffset", "GPU_CoreOffsetOC", "GPU_CoreOffset", "GpuCoreOffsetOC", "GpuCoreOffset");
		GpuMemClockOffset = OptionalInt(o, GpuMemClockOffset, "GPU_MemoryClockOffsetOC", "GPU_MemoryClockOffset", "GpuMemoryClockOffsetOC", "GpuMemoryClockOffset", "GPU_MemoryOffsetOC", "GPU_MemoryOffset", "GpuMemoryOffsetOC", "GpuMemoryOffset");
		bool? flag6 = OptionalBool(o, "FanControlRespective");
		if (flag6.HasValue)
		{
			FanRespective = flag6.Value;
			FanRespectiveSeen = true;
		}
		Interlocked.Increment(ref _fanStatusVersion);
		if (num == 3 && num2 >= 0 && num2 < 5)
		{
			Interlocked.Increment(ref _customProfileStatusVersion);
		}
		if (num2Handled.HasValue)
		{
			int valueOrDefault2 = num2Handled.GetValueOrDefault();
			RaiseIsolated(ModeChanged, "ModeChanged", valueOrDefault2);
		}
		RaiseIsolated(CustomModeChanged, "CustomModeChanged");
		return;
		IL_060a:
		num10 = 75;
		goto IL_0617;
		IL_0617:
		int value = num10;
		int value2 = ((num9 == 0 && TccRawMinimum >= 0) ? (num8 - TccRawMinimum) : 95);
		TccMinimum = Math.Clamp(value, 75, 95);
		TccMaximum = Math.Clamp(value2, 75, 95);
		if (TccMinimum > TccMaximum)
		{
			TccMinimum = TccMaximum;
		}
		goto IL_066f;
	}

	private void OnLchwocStatus(JObject o)
	{
		LchwocStatusSeen = true;
		bool? flag = OptionalBool(o, "Support");
		if (flag.HasValue)
		{
			LchwocSupportReported = flag.Value;
			LchwocSupport = flag.Value;
		}
		bool? flag2 = OptionalBool(o, "Enable");
		if (flag2.HasValue)
		{
			LchwocEnable = flag2.Value;
		}
		UpdateGpuOffsetRanges(o);
		GpuCoreClockOffset = OptionalInt(o, GpuCoreClockOffset, "GPU_CoreClockOffsetOC", "GPU_CoreClockOffset", "GpuCoreClockOffsetOC", "GpuCoreClockOffset", "GPU_CoreOffsetOC", "GPU_CoreOffset", "GpuCoreOffsetOC", "GpuCoreOffset");
		GpuMemClockOffset = OptionalInt(o, GpuMemClockOffset, "GPU_MemoryClockOffsetOC", "GPU_MemoryClockOffset", "GpuMemoryClockOffsetOC", "GpuMemoryClockOffset", "GPU_MemoryOffsetOC", "GPU_MemoryOffset", "GpuMemoryOffsetOC", "GpuMemoryOffset");
		bool? flag3 = FirstOptionalBool(o, "OverClockingSwitch", "OverclockingSwitch", "GPU_OverClockingSwitch", "GPU_OverclockingSwitch");
		if (flag3.HasValue)
		{
			OcSwitch = flag3.Value;
		}
		Logger.WriteLineIfChanged("hwoc-status", $"HWOC: support={LchwocSupportReported?.ToString() ?? "unknown"} enable={LchwocEnable}");
		RaiseIsolated(CustomModeChanged, "CustomModeChanged");
	}

	internal async Task<bool> WaitForStateAsync(Func<bool> predicate, TimeSpan timeout, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (SafePredicate(predicate))
		{
			return true;
		}
		TaskCompletionSource<bool> completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		StateChanged += Check;
		try
		{
			Check(string.Empty);
			try
			{
				return await completed.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (TimeoutException)
			{
				return SafePredicate(predicate);
			}
			catch (OperationCanceledException)
			{
				return SafePredicate(predicate);
			}
		}
		finally
		{
			StateChanged -= Check;
		}
		void Check(string _)
		{
			try
			{
				if (predicate())
				{
					completed.TrySetResult(result: true);
				}
			}
			catch (Exception ex3)
			{
				Logger.WriteLineThrottled("wait-predicate", "WaitForStateAsync predicate failed: " + ex3.Message);
				completed.TrySetResult(result: false);
			}
		}
	}

	private static void RaiseIsolated(Action? handlers, string eventName)
	{
		if (handlers == null)
		{
			return;
		}
		foreach (Action item in handlers.GetInvocationList().Cast<Action>())
		{
			try
			{
				item();
			}
			catch (Exception ex)
			{
				Logger.WriteLineThrottled("handler-fail-" + eventName, eventName + " handler failed: " + ex.Message);
			}
		}
	}

	private static void RaiseIsolated(Action<string>? handlers, string eventName, string argument)
	{
		if (handlers == null)
		{
			return;
		}
		foreach (Action<string> item in handlers.GetInvocationList().Cast<Action<string>>())
		{
			try
			{
				item(argument);
			}
			catch (Exception ex)
			{
				Logger.WriteLineThrottled("handler-fail-" + eventName, eventName + " handler failed: " + ex.Message);
			}
		}
	}

	private static void RaiseIsolated(Action<int>? handlers, string eventName, int argument)
	{
		if (handlers == null)
		{
			return;
		}
		foreach (Action<int> item in handlers.GetInvocationList().Cast<Action<int>>())
		{
			try
			{
				item(argument);
			}
			catch (Exception ex)
			{
				Logger.WriteLineThrottled("handler-fail-" + eventName, eventName + " handler failed: " + ex.Message);
			}
		}
	}

	private static bool SafePredicate(Func<bool> predicate)
	{
		try
		{
			return predicate();
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("wait-predicate", "WaitForStateAsync predicate failed: " + ex.Message);
			return false;
		}
	}

	internal void NotifyCustomModeChanged()
	{
		CustomModeChanged?.Invoke();
	}

	/// <summary>
	/// 该机型是否支持「模式 × 配置」绑定（官方 30 系控制台：三个模式各绑一个固件配置）。
	/// 判据（2026-10-04 用户拍板）：显卡代际 Gen30 <b>或</b> GcuVariant=="30"。
	/// 40/50 系不满足该判据 → 行为完全不变。
	/// </summary>
	public bool SupportsModeProfileConfig
	{
		get
		{
			if (DgpuGeneration == DgpuGenerationKind.Gen30)
			{
				return true;
			}
			return MechrevoService.IsGcu30;
		}
	}

	/// <summary>
	/// 解析某模式该下发的 ProfileIndex：
	/// 自定义模式 → 固件槽位机制给出的 CustomProfileIndex；
	/// 支持「模式 × 配置」的机型 → 该模式自己记的配置号（ModeProfileConfig，按**视觉模式号**）；
	/// 其余 → 0（保持 LM 原有行为）。
	/// </summary>
	internal static int ResolveModeProfileIndex(MechrevoHw hw, int visualMode, int consoleMode, bool custom)
	{
		if (custom)
		{
			return Math.Clamp(hw.CustomProfileIndex, 0, 3);
		}
		// 注意：ModeProfileConfig 用 **视觉模式号**（0=平衡/游戏 1=狂暴/狂飙 2=静音/办公），
		// 与控制台 OperatingMode（0=办公 1=游戏 2=狂飙）不是同一套编号 —— 勿混用。
		if (hw.SupportsModeProfileConfig && ModeProfileConfig.IsBindable(visualMode))
		{
			int num = ModeProfileConfig.Get(visualMode);
			Logger.WriteLine($"ModeProfileConfig: visualMode={visualMode} consoleMode={consoleMode} profile={num} count={ModeProfileConfig.Count}");
			return num;
		}
		return 0;
	}

	internal static (Dictionary<string, object> Fan, Dictionary<string, object> Overclock) BuildModeSwitchPayloads(string action, int operatingMode, int profileIndex, bool custom)
	{
		return (Fan: new Dictionary<string, object>
		{
			["Action"] = action,
			["ProfileIndex"] = profileIndex
		}, Overclock: custom ? new Dictionary<string, object> { ["IsCustomRun"] = true } : new Dictionary<string, object> { ["IsNormalRun"] = operatingMode });
	}

	public async Task SetMode(int mode)
	{
		string text = mode switch
		{
			0 => "OPERATING_GAMING_MODE", 
			1 => "OPERATING_TURBO_MODE", 
			2 => "OPERATING_OFFICE_MODE", 
			3 => "OPERATING_CUSTOM_MODE", 
			_ => "OPERATING_GAMING_MODE", 
		};
		int expectedOpMode = (int)((mode < 0 || mode > 3) ? ConsoleOperatingMode.Gaming : PowerModeMapping.FromVisualMode(mode));
		bool flag = mode == 3;
		Dictionary<string, object> payload;
		Dictionary<string, object> overclockPayload;
		int profileIndex = ResolveModeProfileIndex(this, mode, expectedOpMode, flag);
		(payload, overclockPayload) = BuildModeSwitchPayloads(text, expectedOpMode, profileIndex, flag);
		// 2026-10-09（实验开关验收）：把**实际进 Fan/Control 的那个 ProfileIndex** 记一行 ——
		// 用于从日志直接确认「带/不带 LMECHREVO_FORCE_30PERF 时下发到底有没有这个字段」。
		// ⚠️ 只写日志：取值/下发/档位/风扇逻辑一行未动（profileIndex 与改动前同源同值）。
		Logger.WriteLine($"[perf-profile] SetMode 下发 Action={text} ProfileIndex={profileIndex}{Force30Perf.LogSourceSuffix}");
		MarkModeSwitchPending(expectedOpMode);
		Logger.WriteLine($"SetMode({mode}) -> {text}");
		try
		{
			await Publish("Fan/Control", payload);
			await Publish("LCHWOC/Control", overclockPayload);
		}
		catch
		{
			ClearModeSwitchPending();
			throw;
		}
		if (!(await WaitForStateAsync(() => OperatingMode == expectedOpMode, TimeSpan.FromMilliseconds(180L))))
		{
			await Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
			await WaitForStateAsync(() => OperatingMode == expectedOpMode, TimeSpan.FromMilliseconds(900L));
		}
		await Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GET_FAN_SPEED_CURVE_SETTING" });
	}

	public async Task<bool> SetBatteryProtection(int mode)
	{
		if ((mode < 0 || mode > 2) ? true : false)
		{
			throw new ArgumentOutOfRangeException("mode");
		}
		string action = mode switch
		{
			0 => "PERFORMANCEDMODE", 
			1 => "BALANCEDMODE", 
			2 => "HEALTHYMODE", 
			_ => throw new ArgumentOutOfRangeException("mode"), 
		};
		Logger.WriteLine($"SetBatteryProtection({mode}) -> {action}");
		await _controlLock.WaitAsync();
		try
		{
			if (BatteryProtection == mode)
			{
				return true;
			}
			await Publish("BatteryProtection/Control", new Dictionary<string, object> { ["Action"] = action });
			if (await WaitForStateAsync(() => BatteryProtection == mode, TimeSpan.FromMilliseconds(180L)))
			{
				return true;
			}
			for (int attempt = 0; attempt < 3; attempt++)
			{
				await Publish("BatteryProtection/Control", new Dictionary<string, object> { ["Report"] = "GET" });
				if (await WaitForStateAsync(() => BatteryProtection == mode, TimeSpan.FromMilliseconds((attempt == 0) ? 500 : 700)))
				{
					return true;
				}
			}
			Logger.WriteLine($"SetBatteryProtection not confirmed: expected={mode} actual={BatteryProtection}");
			return false;
		}
		finally
		{
			_controlLock.Release();
		}
	}

	public async Task<bool> SetGpuMode(int mode)
	{
		if ((mode < 0 || mode > 3) ? true : false)
		{
			throw new ArgumentOutOfRangeException("mode");
		}
		if (!CanSwitchGpuMode(mode))
		{
			Logger.WriteLine($"SetGpuMode({mode}) rejected: unsupported by this device");
			return false;
		}
		// 回退（2026-10-03）：热切换执行停用（与核显省电重叠），统一走重启后应用
		// 2026-10-03：热切换执行已删除（与核显省电功能重叠）
		bool useMuxTarget = mode == 0 && SupportsDgpuDirect && !SupportsIgpuOnly;
		GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, SupportsDgpuDirect, SupportsIgpuOnly, DgpuGeneration);
		if ((object)gpuRouteCommand == null)
		{
			Logger.WriteLine($"SetGpuMode({mode}) rejected: action not in dGPU generation {DgpuGeneration} vocabulary");
			return false;
		}
		Dictionary<string, object> payload = gpuRouteCommand.Payload;
		string action = gpuRouteCommand.Action;
		Logger.WriteLine($"SetGpuMode({mode}) -> {action}");
		await _controlLock.WaitAsync();
		long gpuModeVersion;
		try
		{
			gpuModeVersion = GpuModeStatusVersion;
			bool leavingDirect = GpuMode == 2 && mode != 2;
			// 2026-10-05：同上 —— 核显省电命令不再出现在三档模式切换里（§9.6）。
			await Publish("Setting/Control", payload);
			if (leavingDirect && !useMuxTarget)
			{
				await Task.Delay(300);
				await Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = ((mode == 0) ? "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" : "DGPU_DIRECT_CONNECT_TOGGLE_OFF") });
			}
			if (await WaitForStateAsync(TargetReached, TimeSpan.FromMilliseconds(1200L)))
			{
				return true;
			}
			for (int attempt = 0; attempt < 7; attempt++)
			{
				await Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
				if (await WaitForStateAsync(TargetReached, TimeSpan.FromMilliseconds(1800L)))
				{
					return true;
				}
				bool flag2;
				switch (attempt)
				{
				case 1:
				case 3:
				case 5:
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				if (flag2)
				{
					await Publish("Setting/Control", payload);
				}
			}
			Logger.WriteLine($"SetGpuMode not confirmed: expected={mode} actual={GpuMode}");
			return false;
		}
		finally
		{
			_controlLock.Release();
		}
		bool TargetReached()
		{
			if (GpuMode == mode)
			{
				if (useMuxTarget)
				{
					return GpuModeStatusVersion > gpuModeVersion;
				}
				return true;
			}
			return false;
		}
	}

	public async Task SetFanCurve(int type, int[] duties)
	{
		if ((type < 0 || type > 1) ? true : false)
		{
			throw new ArgumentOutOfRangeException("type");
		}
		int[] array = NormalizeFanCurve(duties, (type == 0) ? CpuCurveUpT : GpuCurveUpT);
		Dictionary<string, object> dictionary = new Dictionary<string, object>
		{
			["Action"] = "SET_FAN_SPEED_CURVE_SETTING",
			["Name"] = ((TableName.Length > 0) ? TableName : CurveName),
			["Type"] = ((type == 0) ? "CPU" : "GPU")
		};
		for (int i = 0; i < 16; i++)
		{
			dictionary[$"T{i}"] = array[i].ToString();
		}
		await Publish("Fan/Control", dictionary);
	}

	public static int[] NormalizeFanCurve(IReadOnlyList<int> duties, IReadOnlyList<byte> temperatures)
	{
		int[] array = new int[16];
		int num = Enumerable.Range(0, Math.Min(16, temperatures.Count)).FirstOrDefault((int i) => temperatures[i] == byte.MaxValue, -1);
		if (num < 2)
		{
			num = Math.Min(11, Math.Max(2, duties.Count));
		}
		array[0] = Math.Clamp((duties.Count > 0) ? duties[0] : 0, 0, 100);
		for (int num2 = 1; num2 < num; num2++)
		{
			int value = ((num2 < duties.Count) ? duties[num2] : array[num2 - 1]);
			array[num2] = Math.Max(array[num2 - 1], Math.Clamp(value, 0, 100));
		}
		int num3 = array[num - 1];
		for (int num4 = num; num4 < array.Length; num4++)
		{
			array[num4] = num3;
		}
		return array;
	}

	public async Task<bool> SetPl1Pl2(int pl1, int pl2)
	{
		if (Pl1Minimum < 0 || Pl1Maximum < Pl1Minimum || pl1 < Pl1Minimum || pl1 > Pl1Maximum || Pl2Minimum < 0 || Pl2Maximum < Pl2Minimum || pl2 < Pl2Minimum || pl2 > Pl2Maximum)
		{
			Logger.WriteLine($"SetPl1Pl2 rejected outside capability: PL1={pl1} [{Pl1Minimum},{Pl1Maximum}], PL2={pl2} [{Pl2Minimum},{Pl2Maximum}]");
			return false;
		}
		await _controlLock.WaitAsync();
		try
		{
			string text = (UsesAmdPowerFields ? "CpuAmdSPL" : "PL1");
			string pl2Key = (UsesAmdPowerFields ? "CpuAmdSPPT" : "PL2");
			await Publish("Fan/Control", new Dictionary<string, object>
			{
				["Action"] = "SET_OPERATING_MODE_DETAIL",
				[text] = pl1.ToString()
			});
			await Task.Delay(120);
			await Publish("Fan/Control", new Dictionary<string, object>
			{
				["Action"] = "SET_OPERATING_MODE_DETAIL",
				[pl2Key] = pl2.ToString()
			});
			if (Pl1 == pl1 && Pl2 == pl2)
			{
				return true;
			}
			await Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
			return await WaitForStateAsync(() => Pl1 == pl1 && Pl2 == pl2, TimeSpan.FromMilliseconds(700L));
		}
		finally
		{
			_controlLock.Release();
		}
	}

	public async Task<bool> SetPl4(int pl4)
	{
		if (Pl4Minimum < 0 || Pl4Maximum < Pl4Minimum || pl4 < Pl4Minimum || pl4 > Pl4Maximum)
		{
			Logger.WriteLine($"SetPl4 rejected outside capability: PL4={pl4} [{Pl4Minimum},{Pl4Maximum}]");
			return false;
		}
		string key = Pl4WireKey;
		int wire = Pl4ToWire(pl4);
		int effective = Pl4Effective(pl4);
		if (effective != pl4)
		{
			Logger.WriteLine($"SetPl4({pl4}) quantised to {effective} W: this model reports PL4 in half-watt units.");
		}
		await _controlLock.WaitAsync();
		try
		{
			await Publish("Fan/Control", new Dictionary<string, object>
			{
				["Action"] = "SET_OPERATING_MODE_DETAIL",
				[key] = wire.ToString()
			});
			if (Pl4 == effective)
			{
				return true;
			}
			await Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
			return await WaitForStateAsync(() => Pl4 == effective, TimeSpan.FromMilliseconds(700L));
		}
		finally
		{
			_controlLock.Release();
		}
	}

	internal int QuantiseFanSwitchSpeed(int milliseconds)
	{
		return Math.Clamp(milliseconds / 100 * 100, FanSwitchSpeedMinimum, FanSwitchSpeedMaximum);
	}

	public Task Publish(string topic, object payload)
	{
		return Publish(topic, payload, MqttQualityOfServiceLevel.ExactlyOnce);
	}

	public async Task Publish(string topic, object payload, MqttQualityOfServiceLevel qos)
	{
		if (_publishOverride != null)
		{
			await _publishOverride(topic, payload).ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		IMqttClient client = _client;
		if (client == null || !IsConnected)
		{
			throw new MqttPublishFailedException(topic, "MQTT 未连接");
		}
		try
		{
			await client.PublishStringAsync(topic, JsonConvert.SerializeObject(payload), qos).WaitAsync(PublishTimeout).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (MqttPublishFailedException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			throw new MqttPublishFailedException(topic, ex2.Message, ex2);
		}
	}

	private static int TableNameToMode(string name)
	{
		if (name.StartsWith("M1"))
		{
			return 0;
		}
		if (name.StartsWith("M2"))
		{
			return 1;
		}
		if (name.StartsWith("M3"))
		{
			return 2;
		}
		return -1;
	}

	private static bool IsUninitializedCurve(byte[] duty)
	{
		return duty.Take(8).All((byte value) => value == 0);
	}

	internal static bool IsPathologicalTurboFanCurve(IReadOnlyList<byte> upT, IReadOnlyList<byte> duty)
	{
		int num = Math.Min(upT.Count, duty.Count);
		for (int i = 0; i < num && upT[i] != byte.MaxValue; i++)
		{
			if (upT[i] <= 50 && duty[i] >= 90)
			{
				return true;
			}
		}
		return false;
	}

	internal bool TryGetBuiltInTurboFanDuties(out int[] cpu, out int[] gpu)
	{
		cpu = (gpu = Array.Empty<int>());
		byte[] array = _defaultCpuDuty[2];
		if (array != null)
		{
			byte[] array2 = _defaultGpuDuty[2];
			if (array2 != null)
			{
				cpu = ((IEnumerable<byte>)array).Select((Func<byte, int>)((byte value) => value)).ToArray();
				gpu = ((IEnumerable<byte>)array2).Select((Func<byte, int>)((byte value) => value)).ToArray();
				return true;
			}
		}
		return false;
	}

	internal async Task<bool> RepairPathologicalTurboFanCurveAsync()
	{
		if (!TurboFanCurveIsPathological)
		{
			return false;
		}
		if (!TryGetBuiltInTurboFanDuties(out int[] cpu, out int[] gpu))
		{
			return false;
		}
		if (Interlocked.CompareExchange(ref _turboFanRepair, 1, 0) != 0)
		{
			return false;
		}
		try
		{
			Logger.WriteLine("Repairing pathological turbo fan curve [" + CurveName + "] with built-in DefaultCurve_Turbo");
			await SetFanCurve(0, cpu);
			await SetFanCurve(1, gpu);
			return true;
		}
		finally
		{
			Interlocked.Exchange(ref _turboFanRepair, 0);
		}
	}

	private static (byte[] upT, byte[] duty) ParseCurve(JArray? arr)
	{
		byte[] array = new byte[16];
		byte[] array2 = new byte[16];
		if (arr == null)
		{
			return (upT: array, duty: array2);
		}
		for (int i = 0; i < 16 && i < arr.Count; i++)
		{
			if (arr[i] is JObject o)
			{
				array[i] = (byte)Math.Clamp(Int(o, "UpT"), 0, 255);
				array2[i] = (byte)Math.Clamp(Int(o, "Duty"), 0, 100);
			}
		}
		return (upT: array, duty: array2);
	}

	internal static int Int(JObject o, string key)
	{
		JToken value = o.GetValue(key, StringComparison.OrdinalIgnoreCase);
		if (value == null || value.Type == JTokenType.Null)
		{
			return -1;
		}
		return ParseOptionalInt(value.ToString()) ?? (-1);
	}

	internal static int? ParseOptionalInt(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string s = text.Trim();
		if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result2) && result2 >= -2147483648.0 && result2 <= 2147483647.0)
		{
			return (int)Math.Round(result2);
		}
		return null;
	}

	private static double Double(JObject o, string key, double fallback)
	{
		JToken value = o.GetValue(key, StringComparison.OrdinalIgnoreCase);
		if (value == null || value.Type == JTokenType.Null)
		{
			return fallback;
		}
		if (!double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return fallback;
		}
		return result;
	}

	public bool EnsureDirectGpuOverclock()
	{
		lock (_gpuOverclockLock)
		{
			if (_gpuOverclock != null)
			{
				if (!_gpuOverclock.Refresh())
				{
					return false;
				}
				bool valueOrDefault = _directGpuOverclockEnabled == true;
				if (!_directGpuOverclockEnabled.HasValue)
				{
					valueOrDefault = _gpuOverclock.CoreOffset.Current != 0 || _gpuOverclock.MemoryOffset.Current != 0;
					_directGpuOverclockEnabled = valueOrDefault;
				}
				return _gpuOverclock.IsAvailable;
			}
			if (_gpuOverclockFactory == null)
			{
				return false;
			}
			try
			{
				_gpuOverclock = _gpuOverclockFactory();
				if (_gpuOverclock == null || !_gpuOverclock.IsAvailable)
				{
					_gpuOverclock?.Dispose();
					_gpuOverclock = null;
					return false;
				}
				_directGpuOverclockEnabled = _gpuOverclock.CoreOffset.Current != 0 || _gpuOverclock.MemoryOffset.Current != 0;
				Logger.WriteLine($"Direct GPU overclock backend present via {_gpuOverclock.Name}: core={_gpuOverclock.CoreOffset.Minimum}..{_gpuOverclock.CoreOffset.Maximum}, memory={_gpuOverclock.MemoryOffset.Minimum}..{_gpuOverclock.MemoryOffset.Maximum}, elevated={_isProcessElevated()}, writable={_gpuOverclock.IsAvailable && _isProcessElevated()}");
				return true;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Direct GPU overclock initialization failed: " + ex.Message);
				_gpuOverclock?.Dispose();
				_gpuOverclock = null;
				return false;
			}
		}
	}

	internal bool TryGetDirectGpuOverclockReadback(out int core, out int memory)
	{
		core = (memory = 0);
		lock (_gpuOverclockLock)
		{
			if (_gpuOverclock == null && _gpuOverclockFactory != null)
			{
				try
				{
					_gpuOverclock = _gpuOverclockFactory();
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Direct GPU OC readback initialization failed: " + ex.Message);
					return false;
				}
			}
			if (_gpuOverclock == null)
			{
				return false;
			}
			_gpuOverclock.Refresh();
			core = _gpuOverclock.CoreOffset.Current;
			memory = _gpuOverclock.MemoryOffset.Current;
			return _gpuOverclock.CoreOffset.IsAdjustable || _gpuOverclock.MemoryOffset.IsAdjustable;
		}
	}

	private int GetEffectiveDirectGpuCoreOffset()
	{
		int? elevatedGpuCoreReadback;
		lock (_gpuOverclockLock)
		{
			elevatedGpuCoreReadback = _elevatedGpuCoreReadback;
		}
		if (TryGetDirectGpuOverclockReadback(out var core, out var _) && (!elevatedGpuCoreReadback.HasValue || core == elevatedGpuCoreReadback.Value))
		{
			return core;
		}
		return elevatedGpuCoreReadback ?? GpuCoreClockOffset;
	}

	private int GetEffectiveDirectGpuMemoryOffset()
	{
		int? elevatedGpuMemoryReadback;
		lock (_gpuOverclockLock)
		{
			elevatedGpuMemoryReadback = _elevatedGpuMemoryReadback;
		}
		if (TryGetDirectGpuOverclockReadback(out var _, out var memory) && (!elevatedGpuMemoryReadback.HasValue || memory == elevatedGpuMemoryReadback.Value))
		{
			return memory;
		}
		return elevatedGpuMemoryReadback ?? GpuMemClockOffset;
	}

	private void ClearElevatedGpuReadback(bool core, bool memory)
	{
		lock (_gpuOverclockLock)
		{
			if (core)
			{
				_elevatedGpuCoreReadback = null;
			}
			if (memory)
			{
				_elevatedGpuMemoryReadback = null;
			}
		}
	}

	private static bool ElevatedGpuOverclockFieldMatches(string key, int value, GpuOverclockApplyResult result)
	{
		return key switch
		{
			"GpuCoreClockOffsetOC" => result.CoreOffset == value, 
			"GpuMemoryClockOffsetOC" => result.MemoryOffset == value, 
			"OverClockingSwitch" => result.Success && IsGpuOverclockEnableConfirmed(value, result.CoreOffset, result.MemoryOffset), 
			_ => false, 
		};
	}

	internal static bool IsGpuOverclockEnableConfirmed(int requestedSwitch, int coreOffset, int memoryOffset)
	{
		switch (requestedSwitch)
		{
		case 1:
			// F1（2026-10-09）：开启开关这一步以"写入被接受"为准，不再看"此刻偏移是否非零"。
			// 偏移字段（GpuCoreClockOffsetOC/GpuMemoryClockOffsetOC）在开关这一步【之后】才下发，
			// 用偏移回读来判定开关必然恒为假——这就是"GPU 超频假失败"的结构性根因。
			// 调用点都拿到了"写调用已成功"：ApplyGpuOverclockAsync 里是 flag3，提升助手分支还额外要求 result.Success。
			// 偏移字段的判定完全不受影响（各自比 core/memory 实时回读），关闭开关（case 0）仍要求偏移真的回到 0。
			return true;
		default:
			return false;
		case 0:
			if (coreOffset == 0)
			{
				return memoryOffset == 0;
			}
			return false;
		}
	}

	internal bool DriverGpuOverclockFieldMatches(string key, int value)
	{
		if (!TryGetDirectGpuOverclockReadback(out var core, out var memory))
		{
			return false;
		}
		return key switch
		{
			"GpuCoreClockOffsetOC" => core == value, 
			"GpuMemoryClockOffsetOC" => memory == value, 
			"OverClockingSwitch" => IsGpuOverclockEnableConfirmed(value, core, memory), 
			_ => false, 
		};
	}

	// F4（2026-10-09）：把"这一批字段是否都已被驱动实时回读确认"抽出来，便于首次不一致时短延迟重读一次。
	// 判定规则与原来逐字段的写法完全一致（偏移比实时读数；开关走 IsGpuOverclockEnableConfirmed）。
	private bool DirectGpuFieldsMatchDriverReadback(IReadOnlyCollection<KeyValuePair<string, string>> fields)
	{
		foreach (KeyValuePair<string, string> field in fields)
		{
			if (!int.TryParse(field.Value, out var value) || !DriverGpuOverclockFieldMatches(field.Key, value))
			{
				return false;
			}
		}
		return true;
	}

	internal async Task<bool> ApplyGpuOverclockAsync(IReadOnlyCollection<KeyValuePair<string, string>> fields, CancellationToken cancellationToken = default(CancellationToken))
	{
		int? core = null;
		int? memory = null;
		bool? enabled = null;
		foreach (KeyValuePair<string, string> field in fields)
		{
			if (!int.TryParse(field.Value, out var result))
			{
				return false;
			}
			switch (field.Key)
			{
			case "GpuCoreClockOffsetOC":
				core = result;
				break;
			case "GpuMemoryClockOffsetOC":
				memory = result;
				break;
			case "OverClockingSwitch":
				enabled = result == 1;
				break;
			}
		}
		foreach (KeyValuePair<string, string> field2 in fields)
		{
			bool flag = int.TryParse(field2.Value, out var result2);
			if (flag)
			{
				string key = field2.Key;
				bool flag2 = ((key == "GpuCoreClockOffsetOC" || key == "GpuMemoryClockOffsetOC") ? true : false);
				flag = flag2;
			}
			if (flag && !DirectGpuOverclockFieldWithinDriverRange(field2.Key, result2))
			{
				Logger.WriteLine($"GPU OC rejected outside independent driver range: {field2.Key}={result2}");
				return false;
			}
		}
		ClearElevatedGpuReadback(core.HasValue, memory.HasValue);
		if (GpuOverclockRequiresElevation)
		{
			Logger.WriteLine("GPU OC refused: the NVIDIA direct write requires an elevated process (restart as administrator); no write attempted, no UAC prompt raised.");
			return false;
		}
		bool flag3 = true;
		foreach (KeyValuePair<string, string> field3 in fields)
		{
			if (!int.TryParse(field3.Value, out var result3) || !TrySetDirectGpuOverclockField(field3.Key, result3))
			{
				flag3 = false;
				break;
			}
		}
		// F1（2026-10-09）：开关与偏移拆开判定——开关只要 TrySetDirectGpuOverclockField 返回 true（写入被接受）即通过；
		// 偏移字段判定一行未改，仍是"驱动实时回读 == 目标值"。
		bool flag5 = flag3 && DirectGpuFieldsMatchDriverReadback(fields);
		if (flag5)
		{
			return true;
		}
		if (flag3)
		{
			// (a)（2026-10-09，用户原话「OK 按你说的」）：写入**已被接受**、只是驱动回读还没跟上（实测滞后 2~4s）
			// ⇒ **不再阻塞 GpuOverclockReadbackRecheckMs 只重读一次**，立刻按"已下发·待确认"返回 true：
			//   ① 上层 PerfModeRunner 会再问一次 VerifyAsync，此刻对不上就是 Sent（"已下发，暂时确认不了"），不再当失败；
			//   ② 多拍重读（300ms/2s/4s + 每次 GCU 状态刷新）由懒验证在后台跑，读到一致后升级为 Confirmed，
			//      并回调 NotifyGpuOverclockReadbackConfirmed() 丢掉滞后快照、刷新界面；
			//   ③ 期限（30s）内一直读到"对不上"才标"可能没生效"（仍不算失败，可再点一次『应用』）。
			// ⚠️ 本进程里 _elevatedGpuOverclockApplier 恒为 null（MechrevoHw() → 工厂 ctor 传 null，见 1550 行），
			//    原先 300ms 重读失败后走到的那条"提升助手"分支本来就只记一行日志然后 return false；
			//    这里提前返回**没有功能损失**，还少一次无意义的硬件写与一次 300ms 阻塞。
			Logger.WriteLine($"GPU OC direct write accepted but the driver readback has not caught up yet (immediate match={flag5}); returning 'sent, awaiting confirmation' without blocking (first recheck delay {GpuOverclockReadbackRecheckMs}ms; multi-shot 300ms/2s/4s is owned by the lazy verifier).");
			return true;
		}
		// 写调用真的没被接受（flag3=false）⇒ 维持既有失败口径：回滚内存里的开关标志、如实返回失败。
		MarkEnableUnconfirmed();
		if (_elevatedGpuOverclockApplier == null)
		{
			Logger.WriteLine("GPU OC direct write was rejected and no elevated helper is available.");
			return false;
		}
		GpuOverclockApplyResult result4 = await _elevatedGpuOverclockApplier.ApplyAsync(new GpuOverclockApplyRequest(core, memory, enabled), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (!result4.Success)
		{
			MarkEnableUnconfirmed();
			Logger.WriteLine("GPU OC elevated write failed: " + result4.Error);
			return false;
		}
		lock (_gpuOverclockLock)
		{
			_directGpuOverclockEnabled = enabled ?? (result4.CoreOffset != 0 || result4.MemoryOffset != 0);
			_elevatedGpuCoreReadback = result4.CoreOffset;
			_elevatedGpuMemoryReadback = result4.MemoryOffset;
			if (core.HasValue)
			{
				SaveDirectGpuOverclockValue("GpuCoreClockOffsetOC", core.Value);
			}
			if (memory.HasValue)
			{
				SaveDirectGpuOverclockValue("GpuMemoryClockOffsetOC", memory.Value);
			}
			if (enabled.HasValue)
			{
				SaveDirectGpuOverclockValue("OverClockingSwitch", enabled.Value ? 1 : 0);
			}
		}
		bool flag4 = fields.All((KeyValuePair<string, string> field) => int.TryParse(field.Value, out var result5) && ElevatedGpuOverclockFieldMatches(field.Key, result5, result4));
		if (!flag4)
		{
			// (a)（2026-10-09，用户原话「OK 按你说的」）：提升助手返回的是它那一刻的回读快照，同样会滞后
			//（能走到这里说明 result4.Success 已为真）⇒ 不再阻塞 GpuOverclockReadbackRecheckMs 重读，
			// 一律按"已下发·待确认"返回 true：上层 Runner 的 VerifyAsync 此刻对不上就是 Sent（不是失败），
			// 多拍重读（300ms/2s/4s + 每次 GCU 状态刷新）由懒验证在后台跑，读到一致后升级为 Confirmed。
			Logger.WriteLine($"GPU OC elevated write accepted by the helper but its snapshot has not caught up yet (first match={flag4}); returning 'sent, awaiting confirmation' without blocking (first recheck delay {GpuOverclockReadbackRecheckMs}ms; multi-shot 300ms/2s/4s is owned by the lazy verifier).");
			return true;
		}
		Logger.WriteLine($"GPU OC elevated readback: core={result4.CoreOffset}, memory={result4.MemoryOffset}, confirmed={flag4}");
		CustomModeChanged?.Invoke();
		return true;
		void MarkEnableUnconfirmed()
		{
			if (enabled != true)
			{
				return;
			}
			lock (_gpuOverclockLock)
			{
				_directGpuOverclockEnabled = false;
			}
		}
	}

	/// <summary>
	/// (b)（2026-10-09）：懒验证专用的**三值**驱动回读判据 —— 读得到就给 true/false，**读不到给 null**
	/// （读不到 ≠ 不一致；避免把"驱动后端缺失"误报成"没生效"）。与 <see cref="DriverGpuOverclockFieldMatches" />
	/// 的判定规则逐字一致，只是把"读不到"从 false 改成 null。
	/// </summary>
	internal bool? DriverGpuOverclockFieldVerdict(string key, int value)
	{
		if (!TryGetDirectGpuOverclockReadback(out var core, out var memory))
		{
			return null;
		}
		return key switch
		{
			"GpuCoreClockOffsetOC" => new bool?(core == value), 
			"GpuMemoryClockOffsetOC" => new bool?(memory == value), 
			"OverClockingSwitch" => new bool?(IsGpuOverclockEnableConfirmed(value, core, memory)), 
			_ => null, 
		};
	}

	/// <summary>(b)：同上，给 <see cref="PerfApplyStepKind.ApplyGpuOverclock"/> 那种"整组偏移"用的三值判据。</summary>
	internal bool? DriverGpuOverclockOffsetsVerdict(int core, int memory)
	{
		if (!TryGetDirectGpuOverclockReadback(out var core2, out var memory2))
		{
			return null;
		}
		return new bool?(core2 == core && memory2 == memory);
	}

	/// <summary>
	/// (a)(b)（2026-10-09，用户原话「OK 按你说的」）：懒验证（PerfLazyVerifier）多拍重读**读到与目标一致**后回调这里。
	/// 只做两件事：丢掉提升助手那份可能滞后的回读快照（让 GetEffective* 改用驱动实时值），并刷新一次界面。
	/// ⚠️ 不写任何硬件、不改目标值、不重发 —— 纯状态收敛。
	/// </summary>
	internal void NotifyGpuOverclockReadbackConfirmed()
	{
		ClearElevatedGpuReadback(true, true);
		RaiseIsolated(CustomModeChanged, "CustomModeChanged");
	}

	internal bool CanSetGpuOverclockThroughGcu(string key, int value)
	{
		if (!GcuGpuOverclockAvailable)
		{
			return false;
		}
		return key switch
		{
			"OverClockingSwitch" => (uint)value <= 1u, 
			"GpuCoreClockOffsetOC" => CanSetGcuOffset(value, GpuCoreOffsetMinimum, GpuCoreOffsetMaximum, GpuCoreOffsetUserMinimum, GpuCoreOffsetUserMaximum, key), 
			"GpuMemoryClockOffsetOC" => CanSetGcuOffset(value, GpuMemoryOffsetMinimum, GpuMemoryOffsetMaximum, GpuMemoryOffsetUserMinimum, GpuMemoryOffsetUserMaximum, key), 
			_ => false, 
		};
	}

	private bool CanSetGcuOffset(int value, int gcuMinimum, int gcuMaximum, int userMinimum, int userMaximum, string key)
	{
		if (value >= gcuMinimum && value <= gcuMaximum)
		{
			return true;
		}
		if (LchwocSupportReported == true && !CanSetGpuOverclockThroughDriver(key, value) && value >= userMinimum)
		{
			return value <= userMaximum;
		}
		return false;
	}

	internal void SelectGcuGpuOverclockBackend()
	{
		lock (_gpuOverclockLock)
		{
			_directGpuOverclockEnabled = false;
			_elevatedGpuCoreReadback = null;
			_elevatedGpuMemoryReadback = null;
		}
		if (_persistDirectGpuOverclock)
		{
			int customProfileIndex = CustomProfileIndex;
			int profileIndex = ((customProfileIndex >= 0 && customProfileIndex <= 3) ? CustomProfileIndex : 0);
			AppConfig.Set(DirectGpuProfileKey("enabled", profileIndex), 0);
			AppConfig.Set(DirectGpuProfileKey("core", profileIndex), 0);
			AppConfig.Set(DirectGpuProfileKey("memory", profileIndex), 0);
		}
	}

	internal void MarkGcuGpuOverclockActive()
	{
		lock (_gpuOverclockLock)
		{
			_directGpuOverclockEnabled = null;
			_elevatedGpuCoreReadback = null;
			_elevatedGpuMemoryReadback = null;
		}
		CustomModeChanged?.Invoke();
	}

	internal bool CanSetGpuOverclockThroughDriver(string key, int value)
	{
		lock (_gpuOverclockLock)
		{
			if (_gpuOverclock == null || !_gpuOverclock.IsAvailable)
			{
				return false;
			}
			bool result;
			switch (key)
			{
			case "OverClockingSwitch":
			{
				bool flag = (uint)value <= 1u;
				result = flag;
				break;
			}
			case "GpuCoreClockOffsetOC":
				result = _gpuOverclock.CoreOffset.IsAdjustable && _gpuOverclock.CoreOffset.Contains(value);
				break;
			case "GpuMemoryClockOffsetOC":
				result = _gpuOverclock.MemoryOffset.IsAdjustable && _gpuOverclock.MemoryOffset.Contains(value);
				break;
			default:
				result = false;
				break;
			}
			return result;
		}
	}

	internal bool DirectGpuOverclockFieldWithinDriverRange(string key, int value)
	{
		lock (_gpuOverclockLock)
		{
			if (_gpuOverclock == null)
			{
				return true;
			}
			bool result;
			switch (key)
			{
			case "OverClockingSwitch":
			{
				bool flag = (uint)value <= 1u;
				result = flag;
				break;
			}
			case "GpuCoreClockOffsetOC":
				result = _gpuOverclock.CoreOffset.IsAdjustable && _gpuOverclock.CoreOffset.Contains(value);
				break;
			case "GpuMemoryClockOffsetOC":
				result = _gpuOverclock.MemoryOffset.IsAdjustable && _gpuOverclock.MemoryOffset.Contains(value);
				break;
			default:
				result = false;
				break;
			}
			return result;
		}
	}

	internal bool TrySetDirectGpuOverclockField(string key, int value)
	{
		if (!EnsureDirectGpuOverclock())
		{
			return false;
		}
		bool flag;
		lock (_gpuOverclockLock)
		{
			if (_gpuOverclock == null)
			{
				return false;
			}
			flag = key switch
			{
				"OverClockingSwitch" => SetDirectGpuOverclockEnabled(value == 1), 
				"GpuCoreClockOffsetOC" => CanSetGpuOverclockThroughDriver(key, value) && _gpuOverclock.SetCoreOffset(value), 
				"GpuMemoryClockOffsetOC" => CanSetGpuOverclockThroughDriver(key, value) && _gpuOverclock.SetMemoryOffset(value), 
				_ => false, 
			};
			bool flag2 = flag;
			if (flag2)
			{
				bool flag3 = ((key == "GpuCoreClockOffsetOC" || key == "GpuMemoryClockOffsetOC") ? true : false);
				flag2 = flag3;
			}
			if (flag2)
			{
				_directGpuOverclockEnabled = true;
			}
			if (flag && key == "OverClockingSwitch")
			{
				_elevatedGpuCoreReadback = null;
				_elevatedGpuMemoryReadback = null;
			}
			if (flag && key == "GpuCoreClockOffsetOC")
			{
				_elevatedGpuCoreReadback = null;
			}
			if (flag && key == "GpuMemoryClockOffsetOC")
			{
				_elevatedGpuMemoryReadback = null;
			}
			if (flag)
			{
				SaveDirectGpuOverclockValue(key, value);
			}
		}
		if (flag)
		{
			CustomModeChanged?.Invoke();
		}
		return flag;
	}

	private bool SetDirectGpuOverclockEnabled(bool enabled)
	{
		if (_gpuOverclock == null)
		{
			return false;
		}
		if (!enabled)
		{
			bool num = !_gpuOverclock.CoreOffset.IsAdjustable || _gpuOverclock.CoreOffset.Current == 0 || _gpuOverclock.SetCoreOffset(0);
			bool flag = !_gpuOverclock.MemoryOffset.IsAdjustable || _gpuOverclock.MemoryOffset.Current == 0 || _gpuOverclock.SetMemoryOffset(0);
			if (!num || !flag)
			{
				return false;
			}
		}
		_directGpuOverclockEnabled = enabled;
		if (!enabled)
		{
			_elevatedGpuCoreReadback = null;
			_elevatedGpuMemoryReadback = null;
		}
		return true;
	}

	public bool RestoreDirectGpuOverclockProfile(int profileIndex)
	{
		bool flag = !_persistDirectGpuOverclock;
		if (!flag)
		{
			bool flag2 = ((profileIndex < 0 || profileIndex > 3) ? true : false);
			flag = flag2;
		}
		if (flag || !AppConfig.Is(DirectGpuProfileKey("enabled", profileIndex)))
		{
			return false;
		}
		if (!EnsureDirectGpuOverclock())
		{
			return false;
		}
		if (GpuOverclockRequiresElevation)
		{
			Logger.WriteLine($"Restore direct GPU OC profile {profileIndex + 1} skipped: " + "the NVIDIA direct write requires an elevated process (restart as administrator).");
			return false;
		}
		int num = AppConfig.Get(DirectGpuProfileKey("core", profileIndex), 0);
		int num2 = AppConfig.Get(DirectGpuProfileKey("memory", profileIndex), 0);
		bool gpuCoreOffsetAdjustable = GpuCoreOffsetAdjustable;
		bool gpuMemoryOffsetAdjustable = GpuMemoryOffsetAdjustable;
		bool num3 = !gpuCoreOffsetAdjustable || (num >= GpuCoreOffsetUserMinimum && num <= GpuCoreOffsetUserMaximum);
		bool flag3 = !gpuMemoryOffsetAdjustable || (num2 >= GpuMemoryOffsetUserMinimum && num2 <= GpuMemoryOffsetUserMaximum);
		if (!num3 || !flag3)
		{
			Logger.WriteLine($"Restore direct GPU OC profile {profileIndex + 1} rejected: core={num}, memory={num2}");
			return false;
		}
		bool flag4;
		bool flag5;
		lock (_gpuOverclockLock)
		{
			if (_gpuOverclock == null)
			{
				return false;
			}
			_elevatedGpuCoreReadback = null;
			_elevatedGpuMemoryReadback = null;
			flag4 = !gpuCoreOffsetAdjustable || _gpuOverclock.CoreOffset.Current == num || _gpuOverclock.SetCoreOffset(num);
			flag5 = !gpuMemoryOffsetAdjustable || _gpuOverclock.MemoryOffset.Current == num2 || _gpuOverclock.SetMemoryOffset(num2);
			_directGpuOverclockEnabled = flag4 & flag5;
		}
		Logger.WriteLine($"Restore direct GPU OC profile {profileIndex + 1}: core={num}/{flag4}, memory={num2}/{flag5}");
		if (_directGpuOverclockEnabled == true)
		{
			CustomModeChanged?.Invoke();
		}
		return _directGpuOverclockEnabled == true;
	}

	public void SuspendDirectGpuOverclock()
	{
		lock (_gpuOverclockLock)
		{
			if (_gpuOverclock == null || _directGpuOverclockEnabled != true)
			{
				return;
			}
			SetDirectGpuOverclockEnabled(enabled: false);
			Logger.WriteLine("Direct GPU overclock suspended outside custom mode.");
		}
		CustomModeChanged?.Invoke();
	}

	private void SaveDirectGpuOverclockValue(string key, int value)
	{
		if (!_persistDirectGpuOverclock)
		{
			return;
		}
		int customProfileIndex = CustomProfileIndex;
		int profileIndex = ((customProfileIndex >= 0 && customProfileIndex <= 3) ? CustomProfileIndex : 0);
		switch (key)
		{
		case "OverClockingSwitch":
			AppConfig.Set(DirectGpuProfileKey("enabled", profileIndex), (value == 1) ? 1 : 0);
			if (value == 0)
			{
				AppConfig.Set(DirectGpuProfileKey("core", profileIndex), 0);
				AppConfig.Set(DirectGpuProfileKey("memory", profileIndex), 0);
			}
			break;
		case "GpuCoreClockOffsetOC":
			AppConfig.Set(DirectGpuProfileKey("core", profileIndex), value);
			AppConfig.Set(DirectGpuProfileKey("enabled", profileIndex), 1);
			break;
		case "GpuMemoryClockOffsetOC":
			AppConfig.Set(DirectGpuProfileKey("memory", profileIndex), value);
			AppConfig.Set(DirectGpuProfileKey("enabled", profileIndex), 1);
			break;
		}
	}

	private static string DirectGpuProfileKey(string field, int profileIndex)
	{
		return $"direct_gpu_oc_{field}_{profileIndex}";
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposedFlag, 1) != 0)
		{
			return;
		}
		_disposed = true;
		_subscriptionsReady = false;
		bool flag = false;
		try
		{
			flag = _connectLock.Wait(DisposeDrainTimeout);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Dispose: can't drain _connectLock: " + ex.Message);
		}
		if (!flag)
		{
			Logger.WriteLine($"Dispose: ConnectAsync still running after {DisposeDrainTimeout.TotalSeconds:0}s; disposing anyway");
		}
		try
		{
			IMqttClient? client = _client;
			if (client != null && client.IsConnected)
			{
				_client.DisconnectAsync().WaitAsync(DisposeDrainTimeout).GetAwaiter()
					.GetResult();
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("MQTT disconnect failed: " + ex2.Message);
		}
		lock (_gpuOverclockLock)
		{
			_gpuOverclock?.Dispose();
			_gpuOverclock = null;
		}
		if (_elevatedGpuOverclockApplier is IDisposable disposable)
		{
			try
			{
				disposable.Dispose();
			}
			catch (Exception ex3)
			{
				Logger.WriteLine("Elevated GPU applier dispose failed: " + ex3.Message);
			}
		}
		_client?.Dispose();
		// 2026-10-15（C-1 第 2 步，对照 A-22）：先停 3 s 定时器再释放 WMI 实例
		// （否则可能在释放之后还来一次读）。
		_legacyTimer?.Dispose();
		_legacyTimer = null;
		_legacyMifs?.Dispose();
		_legacyMifs = null;
		// ⭐ 2026-10-16：⭐ 宝龙达后端同样要放（⭐ 它持有 WMI 实例 ✅）
		//   ⭐⭐ 而且在放之前 ⭐ **必须先把风扇交回固件** ⚠️
		//      （⭐ 否则风扇会**停在我们上次写的转速上** ⚠️ —— ⭐ 那是用户在关机前会明显感到的问题 ✅）
				// ⭐⭐ 2026-10-16（"回读"需求）：⭐ 启动时把"⭐ 本机哪些功能号是通的"⭐ 打**一次** ✅
		//   ⭐ 为什么不周期打：⭐ 快照里的温度/转速每 3 秒都变 ⚠️ ⇒ ⭐ 周期打就是刷屏 ❌
		//   ⭐ 这一份清单 ⭐ **正好也是诊断包要的内容**（⭐ 让机主一眼看出本机支持情况 ✅）
		try
		{
			BaolongdaStateSnapshot supportSnap = BaolongdaStateSnapshot.Capture(_baolongda?.Wmi);
			foreach (string supportLine in supportSnap.DescribeSupportList())
			{
				Logger.WriteLine("宝龙达状态支持清单：" + supportLine);
			}
			Logger.WriteLine("宝龙达状态支持清单（⭐ 说明）：⭐ 上面的 ✅ 表示**本机该功能号可读** ✅；"
				+ "⚠️ 表示读不到 ⇒ ⭐ 相关的界面开关**应当隐藏**（⛔ 免得点了没反应 ✅）");
		}
		catch (Exception exSnap)
		{
			Logger.WriteLine("宝龙达状态支持清单：⭐ 采集失败（已吞掉）：" + exSnap.Message);
		}RestoreBaolongdaFanFirmwareControl();
		_baolongda?.Dispose();
		_baolongda = null;
		DataChanged = null;
		ConnectionReady = null;
		StateChanged = null;
		ModeChanged = null;
		CurveUpdated = null;
		GpuModeChanged = null;
		CustomModeChanged = null;
		CloseTimerChanged = null;
		LcChanged = null;
		CapabilitiesChanged = null;
		RawMessageObserved = null;
		if (flag)
		{
			try
			{
				_connectLock.Dispose();
			}
			catch (Exception ex4)
			{
				Logger.WriteLine("Dispose: _connectLock: " + ex4.Message);
			}
		}
		TryDisposeGate(_controlLock, "_controlLock");
	}

	private static void TryDisposeGate(SemaphoreSlim gate, string name)
	{
		try
		{
			if (gate.Wait(DisposeDrainTimeout))
			{
				gate.Dispose();
				return;
			}
			Logger.WriteLine($"Dispose: {name} is still held after {DisposeDrainTimeout.TotalSeconds:0}s; leaving it undisposed");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Dispose: can't drain " + name + ": " + ex.Message);
		}
	}

	private static int OptionalInt(JObject o, string key, int fallback)
	{
		JToken value = o.GetValue(key, StringComparison.OrdinalIgnoreCase);
		if (value == null || value.Type == JTokenType.Null)
		{
			return fallback;
		}
		return ParseOptionalInt(value.ToString()) ?? fallback;
	}

	private static float OptionalFloat(JObject o, string key, float fallback)
	{
		JToken value = o.GetValue(key, StringComparison.OrdinalIgnoreCase);
		if (value == null || value.Type == JTokenType.Null)
		{
			return fallback;
		}
		return float.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r) ? r : fallback;
	}

	private static string[] TryParseMacArray(string text)
	{
		try
		{
			return (from token in JArray.Parse(text)
				select token?.ToString() into mac
				where !string.IsNullOrWhiteSpace(mac)
				select mac).Cast<string>().ToArray();
		}
		catch (JsonException)
		{
			return Array.Empty<string>();
		}
	}

	private void UpdateGpuOffsetRanges(JObject o)
	{
		GpuCoreOffsetMinimum = OptionalInt(o, GpuCoreOffsetMinimum, "GPU_CoreClockOffsetMinimumHWOC", "GPU_CoreClockOffsetMinimum", "GpuCoreClockOffsetMinimum", "GpuCoreClockOffsetMin");
		GpuCoreOffsetMaximum = OptionalInt(o, GpuCoreOffsetMaximum, "GPU_CoreClockOffsetMaximumHWOC", "GPU_CoreClockOffsetMaximum", "GpuCoreClockOffsetMaximum", "GpuCoreClockOffsetMax");
		GpuMemoryOffsetMinimum = OptionalInt(o, GpuMemoryOffsetMinimum, "GPU_MemoryClockOffsetMinimumHWOC", "GPU_MemoryClockOffsetMinimum", "GpuMemoryClockOffsetMinimum", "GpuMemoryClockOffsetMin");
		GpuMemoryOffsetMaximum = OptionalInt(o, GpuMemoryOffsetMaximum, "GPU_MemoryClockOffsetMaximumHWOC", "GPU_MemoryClockOffsetMaximum", "GpuMemoryClockOffsetMaximum", "GpuMemoryClockOffsetMax");
	}

	private static int OptionalInt(JObject o, int fallback, params string[] keys)
	{
		foreach (string propertyName in keys)
		{
			JToken value = o.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
			if (value != null && value.Type != JTokenType.Null)
			{
				int? num = ParseOptionalInt(value.ToString());
				if (num.HasValue)
				{
					return num.Value;
				}
			}
		}
		return fallback;
	}

	public int TccTargetToRaw(int target)
	{
		return ModeDetailEncoding.TccOffset(TjMax, target);
	}

	public int TccTargetFromRaw(int offset)
	{
		return ModeDetailEncoding.TccOffset(TjMax, offset);
	}

	private static bool HasField(JObject o, string key)
	{
		return o.GetValue(key, StringComparison.OrdinalIgnoreCase) != null;
	}

	internal static bool HasUsablePowerValue(JObject o, string key)
	{
		return OptionalUsablePowerValue(o, key, -1) > 0;
	}

	internal static int OptionalUsablePowerValue(JObject o, string key, int fallback)
	{
		JToken value = o.GetValue(key, StringComparison.OrdinalIgnoreCase);
		if (value == null || value.Type == JTokenType.Null)
		{
			return fallback;
		}
		int? num = ParseOptionalInt(value.ToString());
		if (!num.HasValue || num.GetValueOrDefault() <= 0)
		{
			return fallback;
		}
		return num.Value;
	}

	internal static int FirstUsablePowerValue(JObject o, int fallback, params string[] keys)
	{
		foreach (string key in keys)
		{
			int num = OptionalUsablePowerValue(o, key, -1);
			if (num > 0)
			{
				return num;
			}
		}
		return fallback;
	}

	private static JToken? FirstField(JObject o, params string[] keys)
	{
		foreach (string propertyName in keys)
		{
			JToken value = o.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
			if (value != null && value.Type != JTokenType.Null)
			{
				return value;
			}
		}
		return null;
	}

	private static bool? FirstOptionalBool(JObject o, params string[] keys)
	{
		foreach (string key in keys)
		{
			bool? result = OptionalBool(o, key);
			if (result.HasValue)
			{
				return result;
			}
		}
		return null;
	}

	private static bool? ParseColorCalibrationSwitch(JToken? token)
	{
		if (token == null)
		{
			return null;
		}
		bool? result = ParseFlexibleBool(token.ToString());
		if (result.HasValue)
		{
			return result;
		}
		string text = token.ToString() ?? "";
		if (text.Contains("OFF", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (text.Contains("ON", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return null;
	}

	private static int ParseColorCalibrationMode(JToken? token)
	{
		if (token == null)
		{
			return 0;
		}
		if (int.TryParse(token.ToString(), out var result))
		{
			return result;
		}
		string text = token.ToString() ?? "";
		if (text.Contains("sRGB", StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		if (text.Contains("P3", StringComparison.OrdinalIgnoreCase))
		{
			return 3;
		}
		if (text.Contains("Adobe", StringComparison.OrdinalIgnoreCase))
		{
			return 4;
		}
		return text.Contains("Default", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
	}

	private static bool? OptionalBool(JObject o, string key)
	{
		JToken value = o.GetValue(key, StringComparison.OrdinalIgnoreCase);
		if (value == null || value.Type == JTokenType.Null)
		{
			return null;
		}
		return ParseFlexibleBool(value.ToString());
	}

	internal static bool? ParseFlexibleBool(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string text2 = text.Trim();
		if (bool.TryParse(text2, out var result))
		{
			return result;
		}
		if (int.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
		{
			return result2 != 0;
		}
		StringBuilder stringBuilder = new StringBuilder(text2.Length);
		string text3 = text2;
		foreach (char c in text3)
		{
			bool flag;
			switch (c)
			{
			case ' ':
			case '-':
			case '.':
			case '_':
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				stringBuilder.Append(char.ToUpperInvariant(c));
			}
		}
		switch (stringBuilder.ToString())
		{
		case "OPEN":
		case "TRUE":
		case "YES":
		case "Y":
		case "ON":
		case "ENABLE":
		case "ENABLED":
		case "SUPPORT":
		case "AVAILABLE":
		case "SUPPORTED":
		case "SUPPORTS":
			return true;
		case "NONE":
		case "OFF":
		case "N":
		case "NO":
		case "CLOSED":
		case "DISABLE":
		case "UNSUPPORT":
		case "DISABLED":
		case "CLOSE":
		case "FALSE":
		case "NONSUPPORT":
		case "NOTSUPPORT":
		case "UNAVAILABLE":
		case "UNSUPPORTED":
		case "NOTSUPPORTS":
		case "NOTSUPPORTED":
			return false;
		default:
			return null;
		}
	}

	private static bool IsAdjustable(int minimum, int maximum)
	{
		if (minimum >= 0)
		{
			return maximum > minimum;
		}
		return false;
	}

	private static bool IsOffsetAdjustable(int minimum, int maximum)
	{
		return maximum > minimum;
	}

	private (int Minimum, int Maximum) GetMergedGpuOffsetRange(bool core)
	{
		int num = ((!core) ? (-1000) : 0);
		int num2 = (core ? 250 : 2000);
		if (!GcuOffsetChannelUsable)
		{
			if (!TryGetDirectOffsetRange(core, out GpuClockOffsetRange range))
			{
				return (Minimum: -1, Maximum: -1);
			}
			return (Minimum: range.Minimum, Maximum: range.Maximum);
		}
		if (!_isProcessElevated())
		{
			return (Minimum: num, Maximum: num2);
		}
		if (!TryGetDirectOffsetRange(core, out GpuClockOffsetRange range2))
		{
			return (Minimum: num, Maximum: num2);
		}
		return (Minimum: Math.Min(range2.Minimum, num), Maximum: Math.Max(range2.Maximum, num2));
	}

	private static bool IsTelemetryTopic(string topic)
	{
		switch (topic)
		{
		case "System/CpuInfo":
		case "System/GpuInfo":
		case "System/MemoryInfo":
		case "System/FanInfo":
		case "System/BatteryInfo":
			return true;
		default:
			return false;
		}
	}

	private static bool IsCapabilityStatusTopic(string topic)
	{
		switch (topic)
		{
		case "HidLightbar/Status":
		case "HidLightbar_Logo/Status":
		case "Keyboard/Status":
		case "BT_LC/Status":
		case "Fan/Table":
		case "GPUDevice/Status":
		case "Settings/DeviceSwitchItemStatus":
		case "Setting/Status":
		case "Fan/Status":
		case "LCHWOC/Status":
			return true;
		default:
			return false;
		}
	}
}
