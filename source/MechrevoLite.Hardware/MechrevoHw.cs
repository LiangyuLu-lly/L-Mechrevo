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
	private bool _nucLocalReady;
	private bool _xingyaoLocalReady;

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
	/// <summary>2026-10-21：GCU 最近一次上报的 CPU_PL1/PL2（用于区分“谁把功耗改回去”：GCU 上报变化 = GCU/固件侧）。</summary>
	private int _gcuReportedPl1 = -1;

	private int _gcuReportedPl2 = -1;


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
		// 就有已验证的直连动作可做 —— 30 系已确认的动作词汇只有
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
			return Capabilities.Lightbar || Capabilities.RgbLightbar || LightbarStatusSeen || IsIntelNucMachine;
		}
	}

	public bool SupportsLogoLight
{
    get
    {
        return Capabilities.LogoLight || LogoLightStatusSeen
;
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
			// 2026-10-21：同方 Intel + GCU 机型状态里可能不带 CPU_TccOffset* 字段；
			// GCU 本身支持 TCC（写 EC 0x786），这里按“支持”处理，范围由界面兜底。
			return IsGcuConnected && !UsesAmdPowerFields;
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
	// 探测一次、结果缓存；UI 只读 KeyboardLightingCaps 决定亮度滑块范围
	private KeyboardLightingCalibration _kbdCaps;

	/// <summary>⭐ 保证"同一时刻只有一个线程在探测"（WMI 写 + 回读必须串行）。</summary>
	private readonly object _kbdCapsGate = new object();
	// （30 系只读诊断）：raw OutData 一次性探测状态（0=未探 / 1=探测中 / 2=完成）。
	private static int _kbdRawProbeState;
	// （机主：开灯后过一会自己变关）：最近一次**用户灯效动作**的时间戳（TickCount64）。
	private long _kbdLastUserActionTick;
	// <summary> 自动关灯的“最近用户动作”保护窗口（毫秒）。</summary>
	private const int KeyboardAutoOffUserActionGuardMs = 5000;

	/// <summary>
	/// ⭐ 本机的**键盘灯自校准结果**（只读；<c>null</c> = 还没探测过）。
	/// <para>⭐ UI 用它决定：亮度滑块上限（<see cref="KeyboardLightingCalibration.BrightnessMax" />）✅。</para>
	/// </summary>
	internal KeyboardLightingCalibration KeyboardLightingCaps => Volatile.Read(ref _kbdCaps);
	// <summary> 软件彩虹的状态锁（启动 / 停止 / 亮度更新的唯一串行点）。</summary>
	private readonly object _rainbowGate = new object();

	/// <summary>⭐ 当前软件彩虹的取消源（<c>null</c> = 没在跑）。</summary>
	private CancellationTokenSource _rainbowCts;

	/// <summary>⭐ 后台刷色任务（停止时等它退出，保证"停止后无周期性写入"）。</summary>
	private Task _rainbowTask;

	/// <summary>⭐ 软件彩虹当前亮度（硬件级；停止时用于恢复）。</summary>
	private byte _rainbowBrightness;

	/// <summary>⭐ 刷色循环代号（防"旧循环刚退出就把新循环的运行标志清掉"的竞态）。</summary>
	private int _rainbowGeneration;

	/// <summary>⭐ 软件彩虹是否正在后台刷色（只读；生命周期判据/诊断用）。</summary>
	private volatile bool _rainbowRunning;

	/// <summary>⭐ 软件彩虹是否正在后台刷色（只读；⛔ 不触发任何写入）。</summary>
	internal bool IsKeyboardRainbowRunning => _rainbowRunning;
	// 每 5s 只读检查：GetLastInputInfo + BaolongdaState 状态缓存
	private System.Threading.Timer _kbdAutoOffTimer;

	/// <summary>⭐ 自动关灯状态锁（应用/恢复/手动清除的唯一串行点）。</summary>
	private readonly object _kbdAutoOffGate = new object();

	/// <summary>⭐ 是否已因空闲而关灯（0/1；=1 时检测到新输入才恢复）。</summary>
	private int _kbdAutoOffApplied;

	/// <summary>⭐ 恢复写入期间置位（0/1）⇒ ⛔ 不把"恢复"误当成"用户手动改灯"而清标记 ✅。</summary>
	private int _kbdAutoOffRestoring;

	/// <summary>⭐ 计时器回调防重入（0/1）✅。</summary>
	private int _kbdAutoOffTickBusy;

	/// <summary>⭐ 关灯写入失败后置位 ⇒ 等用户重新输入再试（⛔ 不每 5s 重写）✅。</summary>
	private int _kbdAutoOffSuppressUntilInput;

	/// <summary>⭐ "已启用"日志只打一次（0/1）✅。</summary>
	private int _kbdAutoOffEnabledLogged;

	/// <summary>⭐ 自动关灯前记录的完整键盘灯状态（恢复用；只存内存、不落盘）✅。</summary>
	private KeyboardAutoOffSavedState _kbdAutoOffSaved;

	/// <summary>⭐ 自动关灯配置缓存（秒；-1 = 还没读过配置）。</summary>
	private static int _kbdAutoOffSecondsCache = -1;

	/// <summary>⭐ 自动关灯配置的读写锁（静态缓存 + 旧键迁移只做一次）✅。</summary>
	private static readonly object _kbdAutoOffConfigGate = new object();

	/// <summary>⭐ 自动关灯前记录的完整键盘灯状态（恢复用）✅。</summary>
	private sealed class KeyboardAutoOffSavedState
	{
		internal byte Brightness;
		internal int ColorArgb;
		internal bool Rainbow;
	}

	/// <summary>
	/// ⭐ 最近一次键盘灯亮度写入的**请求值**（硬件级；-1 = 还没写过）。
	/// <para>⚠️ 它可能已被 <see cref="MechrevoHw.BaolongdaBrightnessFromUi" /> 夹过；⛔ 不代表用户拖到的界面值。</para>
	/// </summary>
	internal int LastKeyboardBrightnessRequested { get; private set; } = -1;

	/// <summary>
	/// ⭐ 最近一次键盘灯亮度写入后**回读到的实际值**（硬件级；-1 = 读不到）。
	/// <para>⚠️ 用途（机主「拖到最大没反应/回弹」）：UI 显示**实际生效值**，⛔ 不显示用户拖到的值 ✅。</para>
	/// </summary>
	internal int LastKeyboardBrightnessApplied { get; private set; } = -1;

	/// <summary>
	/// ⭐ 键盘灯亮度**写入并回读后**触发（参数 = 硬件实际值；-1 = 读不到）。
	/// <para>⚠️ 可能在**后台线程**触发 ⇒ UI 订阅方必须自己 <c>BeginInvoke</c> 回 UI 线程 ✅。</para>
	/// <para>⛔ 只在真的写了亮度时触发（"只写变化项"路径里亮度没变就不写、也就不会触发）✅。</para>
	/// </summary>
	internal event Action<int> KeyboardBrightnessApplied;

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
	public bool IsConnected => UsesBuiltInModes || IsGcuConnected || _nucLocalReady || _xingyaoLocalReady;

	internal void MarkNucLocalReady()
	{
		_nucLocalReady = true;
	}

	internal void MarkXingyaoLocalReady()
	{
		_xingyaoLocalReady = true;
	}

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

	/// <summary>UMI 机器（型号名含 Umi）。</summary>
	internal bool IsUmiMachine => BackendRouter.LastDecision?.Identity?.IsUmi == true;

	/// <summary>Intel NUC X15（型号含 NUC/X15 或板号 LAPKC*）。</summary>
	internal bool IsIntelNucMachine =>
		BackendRouter.LastDecision?.Identity?.IsIntelNuc == true
		// 路由尚未落 LastDecision 时（首次能力刷新）也要能识别 LAPKC/NUC 板，
		// 否则第三档会被永久隐藏。
		|| BoardWhitelist.ReadBoardProduct().StartsWith("LAPKC", StringComparison.OrdinalIgnoreCase);

	/// <summary>把 ACPI H2RAM 读到的 NUC 风扇值回写到门面属性，供所有 UI/曲线读取。</summary>
	internal void ApplyIntelNucFanSpeeds(int mainFanRpm, int secondFanRpm)
	{
		if (mainFanRpm >= 0) CpuFanRpm = mainFanRpm;
		if (secondFanRpm >= 0) GpuFanRpm = secondFanRpm;
		// 与 GCU Fan/Status 同口径：读到风扇就刷新“新鲜度”，否则迷你窗的
		// FanDutyText 会因 IsFanInfoFresh=false 把百分比整段省略。
		System.Threading.Interlocked.Exchange(ref _fanInfoReceivedAt, Environment.TickCount64);
	}

	/// <summary>把星耀 WMAA 读到的风扇转速回写到门面属性，供迷你窗/托盘/宽屏共用。</summary>
	internal void ApplyXingyaoFanSpeeds(int mainFanRpm, int secondFanRpm)
	{
		if (mainFanRpm >= 0) CpuFanRpm = mainFanRpm;
		if (secondFanRpm >= 0) GpuFanRpm = secondFanRpm;
		System.Threading.Interlocked.Exchange(ref _fanInfoReceivedAt, Environment.TickCount64);
	}

	internal void ApplyIntelNucFanDuty(int mainDutyPercent, int secondDutyPercent)
	{
		if (mainDutyPercent >= 0) CpuFanDuty = mainDutyPercent;
		if (secondDutyPercent >= 0) GpuFanDuty = secondDutyPercent;
	}

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
	/// ⭐⭐ 2026-10-27（task-41）：**路由声明**（来自 <see cref="BackendRouter" /> 的规则表）。
	/// <para>⚠️ 与 <see cref="ActiveRoute" /> 的区别：<see cref="ActiveRoute" /> 回答"现在实际连着谁"
	/// （由连接对象推导，未连上 = None）；本属性回答"按机型事实应该走谁"。两者不一致时由
	/// <see cref="RouteDeclarationMatchesActual" /> / <c>[gate] 机型分类</c> 日志如实暴露（⛔ 不静默换后端）。</para>
	/// </summary>
	internal BackendRoute DeclaredRoute
	{
		get
		{
			BackendRouteDecision decision = BackendRouter.LastDecision;
			return decision == null ? BackendRoute.None : BackendRouter.ToRoute(decision.Backend);
		}
	}

	/// <summary>
	/// ⭐ 声明后端与实际选中后端是否一致（实际 = <see cref="_activeBackend" />，由探测/连接路径写入）。
	/// <para>⚠️ 用途只是**审计/日志**：返回值不参与任何硬件下发判断（现有功能判据仍用
	/// <see cref="ActiveRoute" /> / <c>UsesBaolongda</c> 等，逐位不变）。</para>
	/// </summary>
	internal bool RouteDeclarationMatchesActual => DeclaredRoute == BackendRouter.ToRoute(_activeBackend);

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
		// UMI 30 系有显卡模式切换硬件，但能力位可能不上报；只放两档（混合/直连）。
		if (IsUmiMachine && IsGcuConnected && (mode == 1 || mode == 2))
		{
			return true;
		}
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
	// 强冷可见性 = 旧判据（GCU 能力位 / 宝龙达）**或** 无界 APU（GCU 后端；入口恢复，避免能力位刷新前被永久隐藏）。
	internal static readonly string[] QuickSwitchKeys = new string[26]
	{
		"touchpad", "wifi", "bt", "webcam", "winkey", "fnkey", "capslock", "numpad", "osd", "usb",
		"deepsleep", "copilot", "acrecovery", "highperf", "fanboost", "apuboost", "lightbar", "logolight",
		"touchpadtoggle", "singlecolorkb", "uni", "omni", "powerlight", "batterylogo", "gamewhitelist", "cpuadvperf"
	};
	// <para> 与 SwitchQuick 实现同源维护；FunctionVerifier 自检用它找「可见但静默失败」的缺口 </para>
	internal static bool HasQuickSwitchDispatch(string key)
	{
		switch (key)
		{
		case "touchpad":
		case "wifi":
		case "bt":
		case "webcam":
		case "winkey":
		case "fnkey":
		case "capslock":
		case "numpad":
		case "osd":
		case "highperf":
		case "apuboost":
			// APU 防缩缸实际是 Windows 电源计划策略（Processor performance boost mode），
			// 不依赖 AMD/GCU 硬件；Intel 机器也应有这个开关。
			return true;
		case "usb":
			// 30 系 NUC/UMI 的关机 USB 供电走 Setting/Control 的 USB_CHARGER_ON/OFF；
			// 能力位可能没上报，不能因此隐藏（旧版/原厂控制台都可下发）。
			return true;
		case "fanboost":
		case "micmute":
		case "deepsleep":
		case "gamewhitelist":
		case "cpuadvperf":
		case "lightbar":
		case "logolight":
			return true;
		case "copilot":
		case "acrecovery":
		case "batterylogo":
		case "touchpadtoggle":
		case "singlecolorkb":
		case "uni":
		case "omni":
		case "powerlight":
			return true;
		default:
			return false;
		}
	}
	internal bool SupportsFanBoostQuickSwitch
	{
		get
		{
			bool oldPredicate = SupportsFanBoost || UsesBaolongda;
			// NUC/UMI 的强冷走本地 EC 0x751 bit6，不依赖 GCU 能力位。
			bool visible = oldPredicate || WujieSmuApu || IsIntelNucMachine || IsUmiMachine;
			string message = "[fan] 强冷可见性：backend=" + ActiveBackend + " · caps.FanBoost=" + Capabilities.FanBoost
				+ " · 旧判据=" + oldPredicate + " ⇒ visible=" + visible;
			if (Logger.TryMarkChanged("fan-boost-visible", message))
			{
				Logger.WriteError(message);
			}
			return visible;
		}
	}
	public bool SupportsQuickSwitch(string key)
	{
		return key switch
		{
			"touchpad" => true, 
			"wifi" => WifiSeen || (UsesBaolongda && RadioSwitch.Available(RadioSwitchKind.Wifi)), 
			"bt" => BluetoothSeen || (UsesBaolongda && RadioSwitch.Available(RadioSwitchKind.Bluetooth)), 
			// 现在只读**缓存值**（HasCameraCached，未探测 = false， 不枚举）；
			"webcam" => WebcamSeen || (UsesBaolongda && (CameraPnpControl.HasCameraCached || CameraPrivacy.Available)) || IsIntelNucMachine || IsUmiMachine,
			"winkey" => WinKeySeen || IsIntelNucMachine || IsUmiMachine, 
			"fnkey" => FnKeySeen || UsesBaolongda || IsIntelNucMachine || IsUmiMachine, 
			// ⭐⭐ 2026-10-17（大小写锁定 / 数字小键盘锁定）：这两个**不是 WMI 开关** ——
			//   宝龙达的 WMI 功能号表（8 起）里只有 `FnLock=11` / `TPLock=12`，没有它们；
			//   它们本来就是键盘自身的锁定键 ⇒ 由 Windows 的键盘状态机负责，靠"模拟一次按键"切换
			//   （实现见 `MechrevoLite.Helpers.NativeLockKey`：只调 user32 的
			//    `GetKeyState` / `keybd_event`，⛔ 不碰 EC、⛔ 不碰厂商寄存器、⛔ 不需驱动或提权）。
			//   ⚠️ `capslock` 是**新增开关**，对**所有机型**可用（纯系统级，不依赖任何后端）。
			//   ⚠️ `numpad` 原本只在 GCU 上报过（`NumpadSeen`）时才显示；宝龙达没有 GCU
			//      ⇒ 原来恒 false ⇒ 开关根本不出现 ⇒ 现补宝龙达一支（同样走模拟按键）。
			//   ⚠️ **非宝龙达逐位不变**：老 GCU 机型仍只看 `NumpadSeen`（与改动前逐字一致）。
			"capslock" => true, 
			"numpad" => NumpadSeen || UsesBaolongda, 
			"osd" => true, 
			// ⭐⭐ 2026-10-17 真机修复（蛟龙16 · 板号 `MRID6` · 用户反馈「关机 USB 供电点不动」）：
			//   ⚠️ `SwitchUsbCharger` 的**唯一**通道是 ⭐ MQTT `Setting/Control`
			//      （`USB_CHARGER_ON/OFF`，见 `MechrevoService.SwitchUsbCharger`）⇒ ⭐ **必须有 GCU** ⚠️
			//   ⇒ ⭐ 宝龙达（无 GCU）**没有任何下发通路** ⇒ ⭐ 原来无条件 `true` ⇒ ⭐ 开关可见但点了没反应 ❌
			//   ⇒ ⭐ 现按能力位判据「⭐ 已接好下发路径」收紧：⭐ 宝龙达下**整条隐藏** ✅
			//   ⚠️ **非宝龙达逐位不变**：⭐ 老接口/GCU 机型仍恒 `true`（⭐ 与改动前逐字一致 ✅）。
			"usb" => !UsesBaolongda, 
			"deepsleep" => DeepSleepSeen, 
			"copilot" => CopilotSeen || XingyaoQuickSwitch.IsApplicable(this), 
			"acrecovery" => Capabilities.AcRecovery || AcRecoverySeen || IsIntelNucMachine || IsUmiMachine, 
			// ⇒ 界面显示"下发被拒、已回滚"（机主日志 [QS-apply] 键=highperf 下发结果=False）
			"highperf" => true, 
			// （风扇强冷 · 无 GCU）：宝龙达用本机通道（EC 目标档/掩码 + 13 回读）
		"fanboost" => SupportsFanBoostQuickSwitch || IsIntelNucMachine || IsUmiMachine,
		// （APU 防缩缸）： 纯 Windows 电源策略 ⇒ 所有机型可用
		"apuboost" => true, 
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
	/// <summary>
	/// ⭐⭐ 2026-10-17（真机反馈「30 系 CPU 温度不识别」）：⭐ 由 **LHM 兜底**给出有效 CPU 温度时，
	///   把 <c>_cpuInfoReceivedAt</c> 也盖上时间戳 ⚠️
	/// <para>⚠️ <b>为什么必须有</b>：界面那格 CPU 温度的判据是
	///   <c>HardwareControl.cpuTemp &gt; 0 &amp;&amp; IsSensorInfoFresh(cpu: true, …)</c>，
	///   而 <c>_cpuInfoReceivedAt</c> 原来**只有两条路**会写：宝龙达功能号 22 读到有效值、GCU 推送 ⚠️。
	///   ⭐ 30 系宝龙达的 22 **返回 0**（无效）、又没有 GCU ⇒ ⭐ 这个时间戳**永远是 0** ⇒
	///   ⭐ `IsSensorInfoFresh` 恒 false ⇒ ⭐ **即使 LHM 拿到了温度，UI 也永远显示「—」** ❌
	///   （⭐ 症状正是"CPU 温度不识别" ✅）</para>
	/// <para>⚠️ 非宝龙达 / GCU 机型：本方法不会被走到，行为逐位不变 ✅</para>
	/// </summary>
	internal void MarkCpuInfoReceived()
	{
		Interlocked.Exchange(ref _cpuInfoReceivedAt, Environment.TickCount64);
	}

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
	/// ⭐⭐ 2026-10-18（机主：悬浮窗 CPU 温度停在开机时的温度）：⭐ **外部取到 CPU 温度时写回门面层** ✅
	/// —— ⭐ 与 <see cref="MarkExternalGpuTemp" /> 同款（⭐ **连值带时间戳** ✅）。
	/// <para>⚠️ <b>为什么需要</b>：⭐ 主界面那行遥测读的是 <b><c>hw.CpuTemp</c></b> ⚠️；而 30 系宝龙达的
	/// 功能号 22 是**整条关掉**的（<see cref="BaolongdaWmi.VendorTemperatureUnsupported" />），
	/// 只能靠 LHM / ACPI 兜底 ⇒ ⭐ 不写回门面层的话，⭐ 即使 LHM 取到了温度、⭐ 主界面也**永远看不到**
	/// （⛔ 恒「—」）❌。⭐ 40 系则用来把刚落到的功能号 22 值同步进门面层 ✅。</para>
	/// <para>⚠️ <b>调用口径</b>：⭐ 只由 `HardwareControl.RefreshCpuTempFromLocalSources` 在
	/// <b>非厂商推送来源</b>（func22 / LHM / ACPI）时调用 ✅ ⇒ ⭐ GCU 机型的「厂商值优先」语义
	/// ⛔ 一字不改（⭐ 厂商推送仍由 `DataChanged` 直接写 ✅）。</para>
	/// <para>⚠️ 与 <see cref="MarkCpuInfoReceived" /> 的区别：⭐ 本方法**连值一起写**（⭐ 主界面读的是值本身 ⚠️）；
	/// ⭐ 那个只打时间戳 ✅。⭐ 本方法内部**也会打时间戳** ✅（= 复用 `IsSensorInfoFresh(cpu: true)` 体系 ✅）。</para>
	/// </summary>
	internal void MarkExternalCpuTemp(int celsius)
	{
		if (celsius <= 0)
		{
			return; // ⛔ 无效值不写、⭐ 也不打时间戳（⭐ 免得把「—」变成假的有效 ✅）
		}
		CpuTemp = celsius;
		Interlocked.Exchange(ref _cpuInfoReceivedAt, Environment.TickCount64);
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
		// 启动时**只打一行**（去重）说明本机是"非宝龙达 ⇒ 这些周期任务全部跳过"
		LogNonBaolongdaPeriodicGateOnce("LegacyMifs");
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
			// 但仍是纯空转调用，其中 ApplyBaolongdaFanCurveStep 还会打"未验证"日志 ）
			if (UsesBaolongda)
			{
				RefreshBaolongdaSensors();
				RefreshBaolongdaSwitchStates();
				ApplyBaolongdaFanCurveStep();
			}
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
	/// ⭐⭐ 2026-10-18（宝龙达 Logo 灯「UI 与硬件状态不同步」）：⭐ **把功能号 15 的真实状态回灌
	/// <see cref="QuickSwitches" /> 的唯一入口** ✅
	/// <para>⚠️ 根因（机主诊断包 0.291.9.11-20261018-113538 实证）：启动「灯效恢复（单次下发）」把 LOGO
	/// 点亮了，而 <see cref="TrySetLogoLightViaBackend" /> 原来**只回读、不回灌** ⇒
	/// <c>QuickSwitches["logolight"]</c> 还是旧值（关）⇒ 界面（<c>SettingsForm.SyncLightRows</c>
	/// 读的就是它）一直显示"关"❌。</para>
	/// <para>⭐ 纪律：⭐ **只同步真值** —— <paramref name="source" /> 必须如实写清这个值是怎么来的
	/// （⭐ 功能号 15 回读确认 ／ ⭐ 回读不一致时按实际读到的值），⛔ 绝不把"想让它变成什么"当成状态 ✅
	/// （与 3 s 周期 <see cref="RefreshBaolongdaSwitchStates" /> 同一口径 ✅）。</para>
	/// </summary>
	internal void SyncLogoLightQuickSwitch(bool on, string source)
	{
		SyncBaolongdaQuickSwitch("logolight", on ? 1 : 0);
		// ⭐ 用 WriteLineIfChanged 去重：状态没变就不刷屏 ✅（一变就换一行 ⇒ 诊断包能回查"谁改的"）
		Logger.WriteLineIfChanged("logo-state-sync", $"[logo] 状态同步 ⇒ {(on ? "on" : "off")}（来源：{source}）");
	}

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
			bool readbackSeen = false;
			bool readbackValue = false;
			for (int attempt = 0; attempt < 5; attempt++)
			{
				if (wmi.TryReadLogoLight(out bool current))
				{
					readbackSeen = true;
					readbackValue = current;
					if (current == on)
					{
						confirmed = true;
						Logger.WriteLine($"宝龙达 LOGO 灯：⭐ 已{(on ? "点亮" : "熄灭")}并回读确认 ✅（第 {attempt + 1} 次）");
						// ⭐⭐ 2026-10-18（「UI 与硬件状态不同步」根因：写入后没有同步状态镜像）：
						//   ⭐ 写成功 + 回读确认后**必须立刻**把功能号 15 的真值回灌 `QuickSwitches` ✅
						//   ⚠️ 原来这里只回读、不回灌 ⇒ LOGO 灯被"灯效恢复"点亮后：
						//      ① `QuickSwitches["logolight"]` 要等下一轮 3 s 周期回读才刷新 ⚠️
						//      ② 界面灯光行（`SettingsForm.SyncLightRows`）原来根本没有周期消费 ⚠️
						//      ⇒ 机主看到「硬件实际是开、界面显示关」❌（诊断包 0.291.9.11 实证）。
						//   ⭐ 同步的是**回读确认过的真值**，⛔ 不是"意图"✅。
						SyncLogoLightQuickSwitch(on, "功能号15 回读确认");
						return true;
					}
				}
				Thread.Sleep(150);
			}
			// ⭐ 回读到了但与目标不一致 ⇒ ⭐ 按**回读真值**同步（⛔ 不拿意图当状态 ✅；读不到才保持原镜像）
			if (readbackSeen)
			{
				SyncLogoLightQuickSwitch(readbackValue, "功能号15 回读（与目标不一致）");
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
				//   ⭐ 2026-10-18：`TrySetLogoLightViaBackend` 内部已在回读确认分支调用
				//      `SyncLogoLightQuickSwitch`（含 `[logo] 状态同步` 日志）⇒ 下面这句现在只是**兜底**
				//      （值已相同 ⇒ `SyncBaolongdaQuickSwitch` 不会重复刷日志 ✅）。
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
				// ⭐⭐ 2026-10-16（宝龙达 · 键盘灯「关不掉」BUGFIX）：⭐ 关灯改为**与另一条路共用的统一实现** ✅
				//   ⚠️ 真机依据（p2 = `L-Mechrevo-诊断包-0.291.9.4-20261016-225820` · 宝龙达 MRID6）：
				//      ⭐ 写【功能号 16 = 0】后回读**仍为 2** ⇒ ⭐ **模式写不生效** ❌
				//      ⇒ ⭐ 光写一次模式不够 ⇒ 改为多路尝试
				//        （见 <see cref="TrySetKeyboardLightingViaBackend" /> 的路线 A / 路线 B ✅）
				//   ⇒ ⭐ 两条路（`PublishLightPower` 分派 ／ `SetKeyboardEffect`）**共用同一实现与同一判据** ✅
				//      ⛔ 不再一处判功能号 16、另一处判功能号 10
				//      （⚠️ 那正是上一轮「关了又开」修完后**仍然不一致**的地方 —— 功能号 10 恒为 0 ⇒ 假阳性 ⚠️）
				if (!TrySetKeyboardLightingViaBackend(false, 0, 0, out confirmed))
				{
					return false; // ⭐ 理论上不会发生（宝龙达在管事）⇒ 交回原路更安全 ✅
				}
				// （30 系）：开关镜像只认 EC 0x1807；读不到就保留上一次（ 不凭 WMI16 恢复 ON）
				if (DgpuGeneration == DgpuGenerationKind.Gen30 && UsesBaolongda)
				{
					if (TryReadKeyboardPowerFromEc(out bool ecOff, out int ecOffValue))
					{
						TraceKeyboardPowerWrite(ecOff, "关灯后-EC回读");
						KeyboardPower = ecOff;
						confirmed = !ecOff;
						Logger.WriteError($"[kbd] 30 系关灯后开关状态：EC 0x1807=0x{ecOffValue:X2} ⇒ {(ecOff ? "ON" : "OFF")}"
							+ $" · confirmed={confirmed}");
					}
					else
					{
						Logger.WriteError("[kbd] 30 系关灯后 EC 0x1807 读不到 ⇒ 保留上一次开关状态（⛔ 不凭 WMI16 改）");
					}
				}
				// （40 系）：开关镜像只认 WMI18；读不到保留上一次（ 不凭 WMI16）
				if (DgpuGeneration == DgpuGenerationKind.Gen40 && UsesBaolongda)
				{
					if (TryReadKeyboardPowerFromWmi18(out bool wmi18Off, out int wmi18OffLevel))
					{
						TraceKeyboardPowerWrite(wmi18Off, "40系关灯后-WMI18回读");
						KeyboardPower = wmi18Off;
						confirmed = !wmi18Off;
						Logger.WriteError($"[kbd] 宝龙达 40 系关灯后开关状态：WMI18={wmi18OffLevel} ⇒ {(wmi18Off ? "ON" : "OFF")}"
							+ $" · confirmed={confirmed}");
					}
					else
					{
						Logger.WriteError("[kbd] 宝龙达 40 系关灯后 WMI18 读不到 ⇒ 保留上一次开关状态（⛔ 不凭 WMI16 改）");
					}
				}
				return true; // ⭐ 已接管 → 判据由统一实现给出（功能号 16 模式值 ✅）⇒ ⛔ 不再走下面的重复回读 ✅
			}
			else
			{
				// ⭐⭐ 2026-10-17（真机「每次下发都是白」）：⭐ 本入口只带开关 ⇒ **沿用已保存的用户颜色/亮度** ✅
				//   ⚠️ 原来硬编码白色 0xFFFFFFFF ⇒ ⭐ 每次点亮都把用户颜色冲成白 ❌
				LightChannelSettings stored = LightingSettingsStore.Load("Keyboard/Ctrl", "Single");
				int defaultArgb = stored.EffectiveColorArgb();
				int defaultBrightness = Math.Clamp(Math.Max(1, stored.Light), 1, EffectiveKeyboardBrightnessMax());
				// 现在：已保存效果 → 模式；彩虹（1）⇒ 统一写入入口会启动**软件刷色**
				byte powerOnMode = 2;
				if (TryMapBaolongdaKeyboardMode(stored.Effect, out byte mappedMode) && mappedMode != 0)
				{
					powerOnMode = mappedMode;
				}
				Logger.WriteError($"宝龙达 灯光：⭐ 本入口只带开关（topic={topic}）⇒ 点亮**沿用已保存颜色/效果**"
					+ $"（#{(defaultArgb & 0xFFFFFF):X6} · 效果={stored.Effect} ⇒ 模式 {powerOnMode}"
					+ $" · 界面亮度 {stored.Light} ⇒ 硬件 {defaultBrightness}/{EffectiveKeyboardBrightnessMax()}）✅"
					+ " —— ⭐ 界面若另有配色/亮度入口，那条会各自下发 ✅");
				if (!TrySetKeyboardLightingViaBackend(true, defaultArgb, defaultBrightness, powerOnMode, out confirmed))
				{
					return false; // ⭐ 理论上不会发生 ⇒ 交回原路更安全 ✅
				}
				// ⭐ 这里**刻意不提前 return** —— ⭐ 继续走下面的「功能号 16 真实模式回读」✅
				//   （⭐ 只有那样才会把 `KeyboardPower` 镜像写成真值 ✅ ⇒ `ConfirmLightPower` 才不会再发 MQTT ✅）
			}
			// 根因（ 机主诊断包实证）：本方法原来用【功能号 10 能不能读到】当"回读确认"
			if (DgpuGeneration == DgpuGenerationKind.Gen30 && UsesBaolongda)
			{
				if (TryReadKeyboardPowerFromEc(out bool ecOn, out int ecValue))
				{
					TraceKeyboardPowerWrite(ecOn, "开灯后-EC回读");
					KeyboardPower = ecOn;
					confirmed = ecOn == on;
					Logger.WriteError($"[kbd] 30 系开关确认：EC 0x1807=0x{ecValue:X2} ⇒ {(ecOn ? "ON" : "OFF")}"
						+ $"（目标={(on ? "开" : "关")} · confirmed={confirmed}）");
					return true;
				}
				// ⭐ EC 读不到 ⇒ 保留上一次状态（⛔ 不凭 WMI16=2 恢复 ON）✅
				Logger.WriteError("[kbd] 30 系开关确认：EC 0x1807 读不到 ⇒ 保留上一次状态（⛔ 不凭 WMI16 改）");
				return true;
			}
			// （40 系）：开关镜像只认 WMI18（WMI16 恒 2，不能拿来判「开」）
			if (DgpuGeneration == DgpuGenerationKind.Gen40 && UsesBaolongda)
			{
				if (TryReadKeyboardPowerFromWmi18(out bool wmi18On, out int wmi18Level))
				{
					TraceKeyboardPowerWrite(wmi18On, "40系开灯后-WMI18回读");
					KeyboardPower = wmi18On;
					confirmed = wmi18On == on;
					Logger.WriteError($"[kbd] 宝龙达 40 系开关确认：WMI18={wmi18Level} ⇒ {(wmi18On ? "ON" : "OFF")}"
						+ $"（目标={(on ? "开" : "关")} · confirmed={confirmed}）");
					return true;
				}
				Logger.WriteError("[kbd] 宝龙达 40 系开关确认：WMI18 读不到 ⇒ 保留上一次状态（⛔ 不凭 WMI16 改）");
				return true;
			}
			for (int attempt = 0; attempt < 5; attempt++)
			{
				if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardMode, out byte mode) && mode != 255)
				{
					bool actual = mode != 0;
					TraceKeyboardPowerWrite(actual, "开关确认-WMI16回读(non30)");
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
				// 门控：**必须同时是宝龙达**（同方 30 系也 Gen30， 不进 EC）
				if (DgpuGeneration == DgpuGenerationKind.Gen30 && UsesBaolongda)
				{
					if (TryReadKeyboardPowerFromEc(out bool ecOn, out int ecValue))
					{
						TraceKeyboardPowerWrite(ecOn, "TryConfirm-topic-EC回读");
						KeyboardPower = ecOn;
						confirmed = ecOn == on;
						Logger.WriteError($"[kbd] 30 系开关确认（topic）：EC 0x1807=0x{ecValue:X2} ⇒ {(ecOn ? "ON" : "OFF")}"
							+ $"（目标={(on ? "开" : "关")} · confirmed={confirmed}）");
						return true;
					}
					// ⭐ EC 读不到 ⇒ 保留上一次状态（⛔ 不凭 WMI16=2 恢复 ON）✅
					Logger.WriteError("[kbd] 30 系开关确认（topic）：EC 0x1807 读不到 ⇒ 保留上一次状态（⛔ 不凭 WMI16 改）");
					return true;
				}
				// （40 系）：开关确认只认 WMI18（WMI16 恒 2，不能判「开」）
				if (DgpuGeneration == DgpuGenerationKind.Gen40 && UsesBaolongda)
				{
					if (TryReadKeyboardPowerFromWmi18(out bool wmi18On, out int wmi18Level))
					{
						TraceKeyboardPowerWrite(wmi18On, "TryConfirm-topic-WMI18回读");
						KeyboardPower = wmi18On;
						confirmed = wmi18On == on;
						Logger.WriteError($"[kbd] 宝龙达 40 系开关确认（topic）：WMI18={wmi18Level} ⇒ {(wmi18On ? "ON" : "OFF")}"
							+ $"（目标={(on ? "开" : "关")} · confirmed={confirmed}）");
						return true;
					}
					Logger.WriteError("[kbd] 宝龙达 40 系开关确认（topic）：WMI18 读不到 ⇒ 保留上一次状态（⛔ 不凭 WMI16 改）");
					return true;
				}
				// ⭐ 口径同 TrySetLightPowerViaBackend：⭐ Mode_Off=0 ⇒ 关 ✅ · ⭐ 非 0 ⇒ 开 ✅ · ⭐ Unknow=255 ⇒ 读不到 ✅
				if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardMode, out byte mode) && mode != 255)
				{
					bool actual = mode != 0;
					TraceKeyboardPowerWrite(actual, "TryConfirm-topic-WMI16回读");
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
					// ⭐ 2026-10-18：统一走 `SyncLogoLightQuickSwitch` —— 读到的真值回灌镜像，
					//   `[logo] 状态同步` 那条日志与写入路径共用同一个去重键 ⇒ 值没变时不会多刷一行 ✅
					SyncLogoLightQuickSwitch(logoOn, "功能号15 回读确认");
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
				// ⭐⭐ 2026-10-16（键盘灯「关不掉」第二轮修正）：⭐ 关灯**不再写模式 0** ❌
				//   ⇒ ⭐ 改为**写亮度（功能号 18）= 0** ✅ —— ⭐ 参考实现（`JiaolongControl` · MIT · ⭐ 只取事实 ✅）
				//     界面原文：「背光亮度设置为 0 时将关闭键盘灯光」✅；
				//     ⭐ 斗战者亦**全仓没有独立关灯操作**、其背光值域就是 0~3（⭐ 0 ＝ 灭 ✅）。
				//   ⚠️ 真机依据（⭐ 0.291.9.4 诊断包 · 宝龙达 MRID6）：⭐ 写模式 0 后**回读恒为 2**
				//     ⇒ ⭐ 固件**不接受模式 0** ❌ ⇒ ⭐ 旧的两条路线（A：只写模式 0 · B：模式 0 ＋ 颜色黑）已废弃 ✅
				return TrySetKeyboardLightingViaBackend(false, 0, 0, 0, out confirmed);
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
					// ⭐⭐ 2026-10-17（真机「每次下发都是白」）：⭐ 回读不到时**不要用白色** ❌
					//   ⇒ ⭐ 退回**用户保存的颜色**（预设色板 → 真实 ARGB ✅），⛔ 不拿默认白冲掉用户选择 ✅
					LightChannelSettings storedColor = LightingSettingsStore.Load("Keyboard/Ctrl", "Single");
					argb = storedColor.EffectiveColorArgb();
					Logger.WriteError("宝龙达 键盘灯效：⚠️ 界面没给颜色、⭐ 回读也读不到当前值 ⇒"
						+ " 用**已保存的用户颜色** #" + (argb & 0xFFFFFF).ToString("X6") + " ✅（⛔ 不再硬编码白色 ✅）");
				}
				// ⭐⭐ 2026-10-16（键盘灯模式 · 效果 Id → 模式）：⭐ 由**单一来源**映射 ✅
				//   （⭐ 同表也供界面的下拉与索引映射用 ⇒ ⭐ 各处口径必然一致 ✅）
				//   ⚠️ 来源定性（⭐ 2026-10-17）：
				//     ✅ 2（固定色）与 ⭐ 1（自动循环）＝ **原厂实际使用** ✅
				//     ⚠️ 0（关）：⛔ 不用模式 0 关灯（关灯 ＝ 写亮度 18 ＝ 0 ✅）
				//     ⛔ 3（自定义颜色）：⭐ 40 系真机写 3 后回读**仍为 2** ⇒ 本平台**不支持** ❌
				//        ⇒ ⭐ 已从界面撤掉；旧配置里的 Manual 在此降级为固定色（模式 2）✅
				byte mode;
				if (!TryMapBaolongdaKeyboardMode(effect, out mode))
				{
					mode = 2;
					Logger.WriteError("宝龙达 键盘灯效：⚠️ 效果「" + effect + "」**没有对应的固件模式** ⇒ ⭐ 降级为固定色（模式 2）✅"
						+ "（⭐ 本机固件可写的模式只有 自动循环／固定色 两种 ⚠️；"
						+ "⭐ speed/direction 在宝龙达协议里没有对应字段 ⇒ ⭐ 已忽略 ✅）");
				}
				// ⭐⭐ 2026-10-16（键盘灯「亮度不对」修正）：⭐ 亮度换算收归**唯一来源** ✅
				//   ⚠️ 旧调用方用 `light0to4 - 1` ⇒ ⭐ 界面第 1 档（⭐ 用户想要"最暗"）会算出硬件 0
				//     ⇒ ⭐ 而硬件 0 **就是灭**（⭐ 见关灯那段）⇒ ⭐ 用户看到的是**灯灭**而不是变暗 ❌
				int brightness = BaolongdaBrightnessFromUi(light);
				if (light < 0 || light > 4)
				{
					Logger.WriteError("宝龙达 键盘灯效：⭐ 亮度 " + light + " 超出界面范围（0~4）⇒ ⭐ **夹紧到 "
						+ brightness + "**（⭐ 宝龙达硬件只有 0~3 共 4 级、且 0 ＝ 灭 ✅）");
				}
				if (!TrySetKeyboardLightingViaBackend(true, argb, brightness, mode, out confirmed))
				{
					return false; // ⭐ 理论上不会发生 ⇒ 交回原路更安全 ✅
				}
				return true; // ⭐ 里面已经记好日志了（⭐ 含三层诚实边界 ✅）
			}
			// ⭐⭐ 2026-10-16（键盘灯 · 假阳性修正）：⛔ 删掉原来那段「读一次功能号 10 就 confirmed=true」✅
			//   ⚠️ 真机实测：⭐ 功能号 10 **恒为 0**、且**总是可读**（启动／关灯后／开灯后都是 0 ✅）
			//   ⇒ ⭐ 那段循环**必然**把 confirmed 置真 ⇒ ⭐ 那正是本包「关灯 ⇒ 回读=True」却关不掉的**假阳性**来源 ❌
			//   ⇒ ⭐ 现在 confirmed 由 `TrySetKeyboardLightingViaBackend` **按功能号 16／18 逐值回读**给出 ✅
		}
		catch (Exception ex)
		{
			Logger.WriteError("宝龙达 键盘灯效：⭐ 操作异常（已吞掉，不影响别处）：" + ex.Message);
			return true; // ⭐ 已接管 ⇒ ⛔ 不让调用方再走 GCU ✅
		}
	}

	/// <summary>⭐ 判「关灯」—— ⭐ 界面口径：⭐ 效果是 Off ／ None ／ 空 ✅。</summary>
	internal static bool IsLightEffectOff(string? effect)
	{
		return string.IsNullOrWhiteSpace(effect)
			|| effect.Equals("Off", StringComparison.OrdinalIgnoreCase)
			|| effect.Equals("None", StringComparison.OrdinalIgnoreCase);
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

	/// <summary>
	/// ⭐⭐ 2026-10-18（启动灯光策略：只读）：⭐ 读一次宝龙达键盘灯真实状态（⭐ 只读，⛔ 不写任何字节 ✅）。
	/// <para>⭐ 判据（机主确认）：<c>16 != 0 且 18 &gt; 0</c> 才算「亮着」—— ⚠️ 只判功能号 16 会误判 ❌；
	/// ⛔ 功能号 10 在宝龙达恒 0，**不用**它 ✅。</para>
	/// <para>⭐ 非宝龙达 / 读不到 ⇒ 返回 <c>false</c>（调用方保持原判据，⛔ 不编造状态）✅。</para>
	/// </summary>
	/// <param name="on">⭐ 16!=0 且 18&gt;0 ——「亮着」✅。</param>
	/// <param name="mode">⭐ 功能号 16 原值（读不到 = -1）✅。</param>
	/// <param name="brightness">⭐ 功能号 18 原值（读不到 = -1）✅。</param>
	/// <param name="detail">⭐ 供日志的如实摘要（16=… · 18=…）✅。</param>
	internal bool TryReadKeyboardLightingViaBackend(out bool on, out int mode, out int brightness, out string detail)
	{
		on = false;
		mode = -1;
		brightness = -1;
		detail = string.Empty;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			detail = "宝龙达后端不可用";
			return false;
		}
		// （机主：状态=亮度仅限 30 系宝龙达）：首次进入时明确判据来源
		if (UsesBaolongda && DgpuGeneration == DgpuGenerationKind.Gen30)
		{
			string ecStateMessage = "[kbd] 30 系宝龙达：开关状态=亮度（EC 0x1807）";
			if (Logger.TryMarkChanged("kbd30-state-source", ecStateMessage))
			{
				Logger.WriteError(ecStateMessage);
			}
		}
		else if (!UsesBaolongda)
		{
			string nonBaolongdaMessage = "[kbd] 非宝龙达：沿用原 WMI/GCU 判据（⛔ 不读 EC）";
			if (Logger.TryMarkChanged("kbd-nonbd-state-source", nonBaolongdaMessage))
			{
				Logger.WriteError(nonBaolongdaMessage);
			}
		}
		try
		{
			// EC 读不到 ⇒ 返回 false（调用方保留上一次状态； 绝不凭 WMI16 恢复 ON）
			if (TryReadKeyboardPowerFromEc(out bool ecOn, out int ecValue))
			{
				on = ecOn;
				detail = $"30 系判据：EC 0x1807=0x{ecValue:X2} ⇒ {(ecOn ? "ON" : "OFF")}（⛔ 忽略 WMI16/18）";
				return true;
			}
			if (DgpuGeneration == DgpuGenerationKind.Gen30 && UsesBaolongda)
			{
				detail = "30 系 EC 0x1807 读不到 ⇒ 保留上一次状态（⛔ 不凭 WMI16/18 判开关）";
				return false;
			}
			if (DgpuGeneration == DgpuGenerationKind.Gen40 && UsesBaolongda)
			{
				if (TryReadKeyboardPowerFromWmi18(out bool wmi18On, out int wmi18Level))
				{
					on = wmi18On;
					detail = $"宝龙达 40 系：开关状态=亮度（WMI18={wmi18Level} ⇒ {(wmi18On ? "ON" : "OFF")}；⛔ 不用 16）";
					return true;
				}
				detail = "40 系 WMI18 读不到 ⇒ 保留上一次状态（⛔ 不凭 WMI16 判开关）";
				return false;
			}
			bool modeRead = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardMode, out byte modeValue) && modeValue != 255;
			bool brightRead = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte brightValue) && brightValue != 255;
			if (modeRead)
			{
				mode = modeValue;
			}
			if (brightRead)
			{
				brightness = brightValue;
			}
			if (!modeRead && !brightRead)
			{
				detail = "功能号 16/18 都读不到";
				return false;
			}
			// ⭐ 机主确认的判据：16 != 0 且 18 > 0 才算亮；⛔ 不用功能号 10 ✅
			on = modeRead && brightRead && modeValue != 0 && brightValue > 0;
			detail = "16=" + (modeRead ? modeValue.ToString() : "读不到")
				+ " · 18=" + (brightRead ? brightValue.ToString() : "读不到");
			return true;
		}
		catch (Exception ex)
		{
			detail = ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}
	// <para>日志格式：[kbd-trace] KeyboardPower=True ← 来源=… · 调用链=类型.方法 ← …（ 只截前 3 帧，不打整栈）。</para>
	private void TraceKeyboardPowerWrite(bool value, string source)
	{
		// ⚠️ 只在宝龙达 30/40 系才打（否则其它机型会被追踪日志刷屏）✅
		if (UsesBaolongda && (DgpuGeneration == DgpuGenerationKind.Gen30 || DgpuGeneration == DgpuGenerationKind.Gen40))
		{
			Logger.WriteError($"[kbd-trace] KeyboardPower={value}（电源意图={(value ? "开" : "关")}）"
				+ $" ← 来源={source} · 调用链={DescribeCallChain(3)}");
		}
	}
	// <summary> 截前 N 帧调用链（类型.方法 ← …）， 不打完整栈 。</summary>
	private static string DescribeCallChain(int maxFrames)
	{
		try
		{
			System.Diagnostics.StackTrace stackTrace = new System.Diagnostics.StackTrace(2, fNeedFileInfo: false);
			StringBuilder text = new StringBuilder();
			int count = Math.Min(Math.Max(0, maxFrames), stackTrace.FrameCount);
			for (int i = 0; i < count; i++)
			{
				var method = stackTrace.GetFrame(i)?.GetMethod();
				if (method == null)
				{
					continue;
				}
				if (text.Length > 0)
				{
					text.Append(" ← ");
				}
				text.Append(method.DeclaringType?.Name).Append('.').Append(method.Name);
			}
			return (text.Length > 0) ? text.ToString() : "n/a";
		}
		catch
		{
			return "n/a";
		}
	}
	// <para> 40 系不再用 WMI16 判开关（16 恒 2）；读失败返回 false（调用方保留上一次状态）。</para>
	private bool TryReadKeyboardPowerFromWmi18(out bool on, out int brightness)
	{
		on = false;
		brightness = -1;
		if (DgpuGeneration != DgpuGenerationKind.Gen40 || !UsesBaolongda)
		{
			return false;
		}
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		if (!wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte value) || value == 255)
		{
			return false;
		}
		brightness = value;
		on = value != 0;
		string message = $"[kbd] 宝龙达 40 系：开关状态=亮度（WMI18={value} ⇒ {(on ? "ON" : "OFF")}）";
		if (Logger.TryMarkChanged("kbd40-wmi18-state", message))
		{
			Logger.WriteError(message);
		}
		return true;
	}
	// <para>0x00 ⇒ OFF；!=0 ⇒ ON；读失败返回 <c>false</c>（调用方保留上一次状态； 不凭 WMI16/18 恢复 ON）。</para>
	internal bool TryReadKeyboardPowerFromEc(out bool on, out int ecValue)
	{
		on = false;
		ecValue = -1;
		if (DgpuGeneration != DgpuGenerationKind.Gen30)
		{
			return false;
		}
		// ⚠️ 门控：**必须同时是宝龙达**（同方 30 系也 Gen30，EC 0x1807 含义不同）✅
		if (!UsesBaolongda)
		{
			string nonBaolongdaMessage = "[kbd] 非宝龙达：沿用原 WMI/GCU 判据（⛔ 不读 EC）";
			if (Logger.TryMarkChanged("kbd-nonbd-state-source", nonBaolongdaMessage))
			{
				Logger.WriteError(nonBaolongdaMessage);
			}
			string skipMessage = $"[kbd] 30 系 EC 键盘：跳过（非宝龙达 · gen={DgpuGeneration}）";
			if (Logger.TryMarkChanged("kbd30-ec-skip", skipMessage))
			{
				Logger.WriteError(skipMessage);
			}
			return false;
		}
		// ⭐ 首次进入时明确日志：宝龙达 30 系的状态来源 = EC 0x1807
		string ecStateMessage = "[kbd] 30 系宝龙达：开关状态=亮度（EC 0x1807）";
		if (Logger.TryMarkChanged("kbd30-state-source", ecStateMessage))
		{
			Logger.WriteError(ecStateMessage);
		}
		if (!EcPortIo.TryReadByte(KeyboardEcBrightness.RegisterAddress, out int value))
		{
			return false;
		}
		ecValue = value;
		on = value != 0;
		string message = $"[kbd] 宝龙达 30 系：开关状态=亮度（EC 0x1807=0x{value:X2} ⇒ {(on ? "ON" : "OFF")}）"
			+ (on ? "" : "（⛔ 忽略 WMI16=2 的“开”）");
		if (Logger.TryMarkChanged("kbd30-ec-power", message))
		{
			Logger.WriteError(message);
		}
		return true;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（启动灯光策略：只读）：⭐ 把回读到的键盘灯真实状态写进 <see cref="KeyboardPower" /> 镜像
	/// （⭐ 供 UI/「灯光」行同步；⛔ **不写硬件**、⛔ 不改用户保存的配置 ✅）。
	/// </summary>
	internal void SyncKeyboardPowerFromHardware(bool on, string source)
	{
		if (KeyboardPower == on)
		{
			return;
		}
		TraceKeyboardPowerWrite(on, "SyncKeyboardPowerFromHardware(" + source + ")");
		KeyboardPower = on;
		Logger.WriteLine($"[light] 键盘灯电源镜像同步 ⇒ {(on ? "On" : "Off")}（来源：{source}；⛔ 未写硬件）");
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
	/// <summary>⭐ 兼容旧调用（⭐ 不带模式）⇒ ⭐ 沿用「固定色」（模式 2）✅ —— ⭐ 唯一实现见下面五参版 ✅。</summary>
	internal bool TrySetKeyboardLightingViaBackend(bool on, int colorArgb, int brightness0to3, out bool confirmed)
	{
		return TrySetKeyboardLightingViaBackend(on, colorArgb, brightness0to3, 2, out confirmed);
	}
	// 键盘灯下发的**唯一实现** （ 固定色顺序：模式 16 ⇒ 500ms ⇒ 颜色 17 ⇒ 亮度 18 ）。
	internal bool TrySetKeyboardLightingViaBackend(bool on, int colorArgb, int brightness0to3, byte mode, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		// （30 系只读诊断）：每进程一次 raw OutData 探测（ 不写任何字节）
		ProbeKeyboardRawOnce(wmi);
		// （追踪「谁把开关/亮度改回开」）：只在宝龙达 30/40 系才打（ 不刷别的机型）
		if (UsesBaolongda && (DgpuGeneration == DgpuGenerationKind.Gen30 || DgpuGeneration == DgpuGenerationKind.Gen40))
		{
			Logger.WriteError($"[kbd-trace] TrySetKeyboardLightingViaBackend(on={on} · level={brightness0to3} · mode={mode})"
				+ $" · 电源镜像={(KeyboardPower ? "开" : "关")} ← 调用链={DescribeCallChain(3)}");
		}
		// 记录「最近用户灯效动作」时间（自动关灯 tick 据此 5s 内不触发）
		if (Volatile.Read(ref _kbdAutoOffRestoring) == 0)
		{
			Interlocked.Exchange(ref _kbdLastUserActionTick, Environment.TickCount64);
		}
		try
		{
			// （自动关灯）：任何**非恢复**的键盘灯写入都视为"用户手动改灯"
			if (Volatile.Read(ref _kbdAutoOffRestoring) == 0)
			{
				ClearKeyboardAutoOffApplied("键盘灯写入");
			}
			// ⭐ 确保自动关灯计时器与配置一致（0 = 关闭 ⇒ 不创建；⛔ 无额外检查开销）✅
			ApplyKeyboardAutoOffSetting();
			// ⭐ ① 先试读一次「键盘灯状态」（⭐ 功能号 10）—— ⭐ 读得到才敢写 ⚠️
			if (!wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardStatus, out byte status))
			{
				Logger.WriteError("宝龙达 键盘灯：⚠️ 读不到键盘灯状态（⭐ 功能号 10）⇒ ⭐ 拒绝盲写，本轮不动 ✅"
					+ "（⚠️ 说明 16/17/18 这条链在本机可能不成立 ⇒ ⭐ 需真机进一步确认）");
				return true; // ⭐ 已接管：⭐ 不让调用方再去发 GCU ✅
			}
			_ = status;
			if (!on)
			{
				// （软件彩虹生命周期）： 关灯 ⇒ **先停掉后台刷色**
				StopKeyboardRainbow();
				// ⇒ 30 系统一走 EC（与档位 1/2/3 同一套：读→写→回读→失败再试 LPC→不重试）
				if (DgpuGeneration == DgpuGenerationKind.Gen30 && UsesBaolongda)
				{
					LogKeyboardLightingState(wmi, "关灯前");
					bool ecOffOk = KeyboardEcBrightness.TrySetOff(wmi, out int ecOffReadback, out int ecOffWmi18,
						"关灯写(EC) · TrySetKeyboardLightingViaBackend(on=false)");
					confirmed = ecOffOk;
					if (ecOffOk)
					{
						RecordKeyboardBrightnessApplied(0);
					}
					else
					{
						RecordAppliedKeyboardBrightness(wmi, requestedLevel: 0);
					}
					Logger.WriteError("宝龙达 键盘灯：⭐ 30 系关灯走 EC 0x1807："
						+ (ecOffOk ? "成功" : "失败（不重试）")
						+ $" · 回读={(ecOffReadback >= 0 ? "0x" + ecOffReadback.ToString("X2") : "读不到")}"
						+ $" · WMI18={(ecOffWmi18 >= 0 ? ecOffWmi18.ToString() : "读不到")}"
						+ " · ③ ⚠️ 灯是否真的灭了 —— **我们测不到，需目视确认** ⚠️");
					LogKeyboardLightingState(wmi, "关灯后");
					return true;
				}
				if (DgpuGeneration == DgpuGenerationKind.Gen40 && UsesBaolongda)
				{
					// （宝龙达 40 系：状态=亮度）：关灯 = WMI18=0；先记忆关灯前档位
					int priorLevel = -1;
					if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte prior18) && prior18 != 255 && prior18 > 0)
					{
						priorLevel = Math.Clamp((int)prior18, 1, EffectiveKeyboardBrightnessMax());
						KeyboardEcBrightness.RememberCurrentLevel(priorLevel);
					}
					LogKeyboardLightingState(wmi, "关灯前");
					bool wrote = wmi.TrySetKeyboardBrightness(0);
					bool readOk = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte read18) && read18 != 255;
					bool offOk = wrote && readOk && read18 == 0;
					confirmed = offOk;
					if (offOk)
					{
						RecordKeyboardBrightnessApplied(0);
					}
					else
					{
						RecordAppliedKeyboardBrightness(wmi, requestedLevel: 0);
					}
					if (TryReadKeyboardPowerFromWmi18(out bool wmi18On, out int wmi18Level))
					{
						TraceKeyboardPowerWrite(wmi18On, "40系关灯后-WMI18回读");
						KeyboardPower = wmi18On;
					}
					Logger.WriteError($"[kbd] 宝龙达 40 系关灯：WMI18=0（写={(wrote ? "ok" : "失败")}"
						+ $" · 回读={(readOk ? read18.ToString() : "读不到")}）· 记忆档位={KeyboardEcBrightness.GetLastLevel()}"
						+ $" ⇒ {(offOk ? "成功" : "失败（不重试，如实报告；若固件忽略 18=0 则本机无真正关灯手段）")}");
					LogKeyboardLightingState(wmi, "关灯后");
					return true;
				}
				// ── ⭐ 关灯：⭐ 写亮度 0（⭐ 功能号 18）✅ —— ⚠️ 不再尝试模式 0（⭐ 已被真机否定 ❌）──
				LogKeyboardLightingState(wmi, "关灯前");
				if (!wmi.TrySetKeyboardBrightness(0))
				{
					Logger.WriteError("宝龙达 键盘灯：⚠️ 关灯失败 —— 写「亮度 0」（功能号 18）未被接受"
						+ " ⇒ ⭐ 如实报告未确认 ⚠️");
					LogKeyboardLightingState(wmi, "关灯失败后");
					return true;
				}
				// （只读诊断）：写 18=0 后连续读 5 次 raw（看是否滞后/是否夹回）
				LogKeyboardRawAfterBrightnessWrite(wmi, 0);
				confirmed = TryConfirmKeyboardBrightness(wmi, 0);
				// 记录**回读到的实际值**（ UI 显示实际生效值， 不显示用户拖到的值）
				RecordAppliedKeyboardBrightness(wmi, requestedLevel: 0);
				if (confirmed)
				{
					Logger.WriteError("宝龙达 键盘灯：⭐ 关灯已写「亮度 0」（功能号 18）"
						+ " ⇒ ⭐ ① 写成功=是 · ② 回读一致=是（=0）✅"
						+ " · ③ ⚠️ 灯是否真的灭了 —— **我们测不到，需目视确认** ⚠️");
				}
				else
				{
					Logger.WriteError("宝龙达 键盘灯：⚠️ 已写「亮度 0」（功能号 18）但**回读不一致**"
						+ " ⇒ ⭐ 如实报告未确认 ⚠️（⭐ 若回读也不是 0 ⇒ 本机固件不接受亮度 0"
						+ " ⇒ ⭐ 那本机可能**没有关灯手段** ⚠️ —— ⭐ 届时应按能力位处理，⛔ 留着「能点没反应」更糟 ✅）");
				}
				LogKeyboardLightingState(wmi, "关灯后");
				return true;
			}
			// （机主批准「软件彩虹」）： **拦截 mode==1** ——  不再写硬件 16=1
			if (mode == 1)
			{
				// ⭐ 开灯状态下亮度不允许是 0（0 ＝ 灭）⇒ 彩虹至少给 1 档 ✅
				int rainbowLevel = Math.Clamp(brightness0to3, 1, EffectiveKeyboardBrightnessMax());
				if (IsKeyboardRainbowRunning)
				{
					// ⭐ 亮度变化 ⇒ 只更新亮度，⛔ **不重启循环**（否则色相会跳回 0）✅
					UpdateKeyboardRainbowBrightness((byte)rainbowLevel);
					confirmed = true;
					Logger.WriteError($"[kbd] 软件彩虹：已在运行 ⇒ 只更新亮度={rainbowLevel}（⛔ 不重启刷色循环）");
					return true;
				}
				StartKeyboardRainbow((byte)rainbowLevel, CancellationToken.None);
				confirmed = true;
				Logger.WriteError($"[kbd] 软件彩虹：已启动（模式 16=2 + 每帧 17 刷色 · 亮度={rainbowLevel}）");
				LogKeyboardLightingState(wmi, "软件彩虹启动后");
				return true;
			}
			// ⭐ 非彩虹（单色）⇒ **先停掉软件彩虹**（恢复单色）再走原来的固定色写入 ✅
			StopKeyboardRainbow();
			// ⭐⭐ 2026-10-17（真机反馈「键盘灯没颜色 / 氛围灯无效」）：⭐ **顺序必须【模式 → 颜色】** ⚠️
			//   ⭐ 依据（30 系官方控制台 `SetSP_KeyboardColor`，v0.2.35）：
			//     `SetValue(RGBKeyboardMode, Mode_RGBFixedMode)` **先写模式 2**
			//     然后 `SetValue(RGBKeyboardColor, new byte[3]{r,g,b})` **再写颜色** ✅
			//   ⚠️ 旧实现是「颜色 → 模式」⇒ ⭐ 写模式会**重置/覆盖颜色** ⇒ 真机回读颜色恒 0,0,0 ❌
			//     （0.291.9.6 诊断包实证：写 rgb=#FFFFFF 后功能号 17 回读 0,0,0，而 16/18 都能读回 ✅）
			//   ⚠️ 40 系官方在"模式 2 → 颜色"之间还等 500ms；**30 系官方没有延迟** ⇒ 本机不加 ✅
			// ⭐⭐ 2026-10-17（按 7.3 与 JiaolongControl 的做法重写"写什么"）：
			//   ⭐ 两家参考都**只写变化的那一项**：
			//     · 7.3：改颜色 = 模式 2 → 等 500 ms → 颜色（**不重写亮度**）；改亮度只写 18 ✅
			//     · JiaolongControl(MIT)：颜色/模式/亮度各自独立下发（SetColor 只写 17）✅
			//   ⚠️ 我们原来**每次都把 16/17/18 全写一遍** ⇒ 改颜色时顺带重写亮度，
			//     固件可能据此重新应用配置 ⇒ 颜色被顶掉
			//     （真机现象：亮度可调、颜色/效果无反应，而 16/17/18 回读全"成功" —— 完全对得上）
			// ⭐⭐ 2026-10-17（真机「模式 3 写不进」）：⭐ 本平台固件只接受模式 1／2 ⚠️
			//   ⭐ 真机依据（MRID6-23 / 40 系）：⭐ 写 3 后回读**仍为 2** ⇒ ⭐ 模式 3 不被接受 ❌
			//   ⇒ ⭐ 这里**兜底收敛**（即使调用方仍传 3）⇒ ⭐ 按固定色 2 下发，⛔ 不假装支持 ✅
			if (!BaolongdaProfile.IsKeyboardModeWritable(mode))
			{
				Logger.WriteError("宝龙达 键盘灯：⚠️ 模式 " + mode + " 本机固件不支持（40 系实测写 3 回读仍为 2）"
					+ " ⇒ ⭐ 降级为固定色（模式 2）✅ —— ⛔ 不假装写进去了 ✅");
				mode = 2;
			}
			// ⭐ ② 模式（功能号 16）—— **只在变化时写**（彩虹已在上方拦截，这里只剩固定色模式 2）
			bool modeKnown = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardMode, out byte curMode);
			bool modeChanged = !modeKnown || curMode != mode;
			bool modeOk = !modeChanged || wmi.TrySetKeyboardMode(mode);
			//   ⭐ 只在**真的改了模式**之后才等 500 ms（40/50 系官方与 7.3 都是这个时序）✅
			//   ⚠️ 30 系不加延迟（与官方 30 系一致）；⛔ 只在写入线程上等，不阻塞 UI ✅
			DgpuGenerationKind kbGen = DgpuGeneration;
			if (modeChanged && (kbGen == DgpuGenerationKind.Gen40 || kbGen == DgpuGenerationKind.Gen50))
			{
				Thread.Sleep(500);
			}
			// ⭐ ③ 颜色（功能号 17，三字节 R,G,B）—— 颜色**不可靠回读** ⇒ 每次都写 ✅
			int r = (colorArgb >> 16) & 0xFF;
			int g = (colorArgb >> 8) & 0xFF;
			int b = colorArgb & 0xFF;
			bool colorOk = wmi.TrySetKeyboardColor((byte)r, (byte)g, (byte)b);
			// （机主实测纠正 + 自校准）： 本机上限 = **探测值**
			byte level = (byte)Math.Clamp(brightness0to3, 0, EffectiveKeyboardBrightnessMax());
			// ⭐⭐ 2026-10-18（机主：「键盘灯打开得手动调一下亮度才能开」）：
			//   ⚠️ 亮了但亮度=0 ⇒ 看起来就是"没开"；用户必须再拖一下亮度才有光 ❌
			//   ⭐ 规则：**开灯状态下亮度不允许是 0**（0 等于关），最低给 1 ✅
			//   ⚠️ 关灯（mode=0 / on=false）不受影响，仍写 0 ✅
			bool wantOn = on && mode != 0;
			if (wantOn && level == 0)
			{
				level = 1;
				Logger.WriteLine("[kbd] 开灯时亮度为 0 ⇒ 自动抬到 1（否则表现为\"开了不亮\"，需再拖亮度）");
			}
			bool needBright = false;
			bool brightOk;
			bool ecBrightnessUsed = false;
			bool ecBrightnessOk = false;
			bool wmi18BrightnessUsed = false;
			bool wmi18BrightnessOk = false;
			int ecBrightnessReadback = -1;
			int ecBrightnessWmi18 = -1;
			if (kbGen == DgpuGenerationKind.Gen30 && UsesBaolongda)
			{
				// 关灯（档位 0）仍走上面 !on 分支的原有 18=0 路径，**未改**；
				ecBrightnessUsed = true;
				// （机主：开关直接跟亮度走 + 关灯记忆上次亮度）
				if (EcPortIo.TryReadByte(KeyboardEcBrightness.RegisterAddress, out int currentPwm) && currentPwm == 0)
				{
					int rememberedLevel = KeyboardEcBrightness.GetLastLevel();
					if (rememberedLevel != level)
					{
						Logger.WriteError($"[kbd] 30 系开灯：EC 0x1807=0x00 ⇒ 用上次亮度记忆={rememberedLevel}"
							+ $"（请求档位={level}）");
						level = (byte)rememberedLevel;
					}
				}
				ecBrightnessOk = KeyboardEcBrightness.TrySetLevel(level, wmi, out ecBrightnessReadback, out ecBrightnessWmi18,
					"亮度下发(固定色) · TrySetKeyboardLightingViaBackend(on=true)");
				brightOk = ecBrightnessOk;
				// （单源状态机）：电源镜像**只由 EC 回读推导**（ 不用请求值）
				if (ecBrightnessOk && TryReadKeyboardPowerFromEc(out bool ecOnAfter, out int _))
				{
					TraceKeyboardPowerWrite(ecOnAfter, "亮度路径后-EC回读");
					KeyboardPower = ecOnAfter;
				}
				Logger.WriteError($"[kbd] 30 系亮度走 EC 0x1807：档位={level} ⇒ 回读="
					+ $"{(ecBrightnessReadback >= 0 ? "0x" + ecBrightnessReadback.ToString("X2") : "读不到")}"
					+ $" · WMI18={(ecBrightnessWmi18 >= 0 ? ecBrightnessWmi18.ToString() : "读不到")}"
					+ $" ⇒ {(ecBrightnessOk ? "成功" : "失败（不重试）")}");
			}
			else if (kbGen == DgpuGenerationKind.Gen40 && UsesBaolongda)
			{
				// （宝龙达 40 系：状态=亮度）：WMI18 是唯一状态源；
				wmi18BrightnessUsed = true;
				int targetLevel = level;
				if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte current18) && current18 == 0)
				{
					int rememberedLevel = Math.Clamp(KeyboardEcBrightness.GetLastLevel(), 1, EffectiveKeyboardBrightnessMax());
					if (rememberedLevel != targetLevel)
					{
						Logger.WriteError($"[kbd] 宝龙达 40 系开灯：WMI18=0 ⇒ 用上次亮度记忆={rememberedLevel}（请求档位={targetLevel}）");
						targetLevel = rememberedLevel;
					}
				}
				targetLevel = Math.Clamp(targetLevel, 1, EffectiveKeyboardBrightnessMax());
				bool wrote = wmi.TrySetKeyboardBrightness((byte)targetLevel);
				int readback = ReadKeyboardBrightnessAfterWrite(wmi, (byte)targetLevel, attempts: 3, delayMs: 60);
				wmi18BrightnessOk = wrote && readback == targetLevel;
				brightOk = wmi18BrightnessOk;
				level = (byte)targetLevel;
				if (wmi18BrightnessOk)
				{
					KeyboardEcBrightness.RememberCurrentLevel(targetLevel);
				}
				if (TryReadKeyboardPowerFromWmi18(out bool wmi18On, out int wmi18Level))
				{
					TraceKeyboardPowerWrite(wmi18On, "40系亮度路径后-WMI18回读");
					KeyboardPower = wmi18On;
				}
				Logger.WriteError($"[kbd] 宝龙达 40 系亮度：档位={targetLevel} ⇒ WMI18 写={(wrote ? "ok" : "失败")}"
					+ $" · 回读={(readback < 0 ? "读不到" : readback.ToString())} · 记忆={KeyboardEcBrightness.GetLastLevel()}"
					+ $" ⇒ {(wmi18BrightnessOk ? "成功" : "失败（不重试，如实报告）")}");
			}
			else
			{
				bool brightKnown = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte curBright);
				needBright = !brightKnown || curBright != level;
				if (!needBright)
				{
					brightOk = true;
				}
				else
				{
					// 复位序列的"写满"也取**本机探测上限**（ 不写超过上限的值）
					byte maxLevel = (byte)EffectiveKeyboardBrightnessMax();
					wmi.TrySetKeyboardBrightness(maxLevel);
					Thread.Sleep(120);
					wmi.TrySetKeyboardBrightness(0);
					Thread.Sleep(120);
					brightOk = wmi.TrySetKeyboardBrightness(level);
					Logger.WriteLine($"[kbd] 亮度复位序列：{maxLevel} → 0 → {level}（7.3 做法）⇒ ok={brightOk}");
				}
				// （只读诊断）：真的写了 18 之后连续读 5 次 raw（看滞后/夹回）
				if (needBright)
				{
					LogKeyboardRawAfterBrightnessWrite(wmi, level);
				}
			}
			// ⭐⭐ 回读：⭐ 逐值比对（⭐ 旧代码只判"非 0 ＝ 开" ⇒ ⭐ 选自动循环/自定义时会误判成功 ❌）
			bool modeMatch = TryConfirmModeExact(wmi, mode);
			bool brightMatch = ecBrightnessUsed
				? ecBrightnessOk
				: (wmi18BrightnessUsed ? wmi18BrightnessOk : TryConfirmKeyboardBrightness(wmi, level));
			confirmed = modeMatch && brightMatch;
			// 亮度写 4/5（或超过本机上限的值）时若**回读被夹回** ⇒
			if (ecBrightnessUsed && ecBrightnessOk)
			{
				// ⭐ 30 系 EC 写成功：UI 直接显示目标档位（EC 回读已校验，⛔ 不用 WMI18 的旧值冒充）✅
				RecordKeyboardBrightnessApplied(level);
			}
			else if (wmi18BrightnessUsed && wmi18BrightnessOk)
			{
				// ⭐ 40 系 WMI18 写成功且回读一致：UI 显示目标档位 ✅
				RecordKeyboardBrightnessApplied(level);
			}
			else
			{
				RecordAppliedKeyboardBrightness(wmi, level);
			}
			Logger.WriteError($"宝龙达 键盘灯：⭐ 下发 rgb=#{r:X2}{g:X2}{b:X2} · 模式={mode} · 亮度={level}"
				+ (ecBrightnessUsed
					? $" · 30系亮度=EC 0x1807（{(ecBrightnessOk ? "成功" : "失败")}）"
					: $" · ⭐ 本次**只写变化项**（模式={modeChanged} · 颜色=是 · 亮度={needBright}）")
				+ $" ⇒ ⭐ ① 写成功={colorOk && modeOk && brightOk}（色={colorOk} 模式={modeOk} 亮度={brightOk}）"
				+ $" · ⭐ ② 回读一致={confirmed}（模式={modeMatch} 亮度={brightMatch}）"
				+ " · ⭐ ③ ⚠️ 灯效/亮度是否**真的**变了 —— **我们测不到，需目视确认** ⚠️");
			LogKeyboardLightingState(wmi, "下发后");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteError("宝龙达 键盘灯：⭐ 操作异常（已吞掉）：" + ex.Message);
			return true;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 键盘灯模式判据）：⭐ 回读**精确等于**目标模式才算一致 ✅。
	/// <para>⚠️ 与旧 <c>TryConfirmKeyboardMode(expectedOn)</c> 的区别：⭐ 旧的只判「非 0 ＝ 开」
	/// ⇒ ⭐ 用户选「自动循环」/「自定义颜色」时它会**误判成功** ⚠️（⭐ 只要模式非 0 就算通过 ❌）。
	/// ⭐ 现在四项模式各有确定值 ⇒ ⭐ 必须**逐值比对** ✅。</para>
	/// </summary>
	private static bool TryConfirmModeExact(BaolongdaWmi wmi, byte expected)
	{
		for (int attempt = 0; attempt < 4; attempt++)
		{
			if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardMode, out byte mode) && mode != 255 && mode == expected)
			{
				return true;
			}
			Thread.Sleep(100);
		}
		return false;
	}

	/// <summary>⭐ 回读键盘灯亮度（⭐ 功能号 18）是否**精确等于**目标值 ✅（⭐ 逐值比，⛔ 不判"非 0" ⚠️）。</summary>
	private static bool TryConfirmKeyboardBrightness(BaolongdaWmi wmi, byte expected)
	{
		for (int attempt = 0; attempt < 4; attempt++)
		{
			if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte level) && level == expected)
			{
				return true;
			}
			Thread.Sleep(100);
		}
		return false;
	}
	// 亮度写入后**立刻回读**，把【请求值 / 实际值】都记下来，并在被固件夹回时记一条 [kbd-cal] 日志 。
	private void RecordAppliedKeyboardBrightness(BaolongdaWmi wmi, int requestedLevel)
	{
		LastKeyboardBrightnessRequested = requestedLevel;
		int actual = -1;
		if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte readback))
		{
			actual = readback;
		}
		LastKeyboardBrightnessApplied = actual;
		if (actual >= 0 && actual != requestedLevel)
		{
			Logger.WriteError($"[kbd-cal] ⚠️ 亮度写入被夹回：请求={requestedLevel} · 回读实际={actual}"
				+ $" ⇒ ⭐ UI 显示**实际生效值 {actual}**（⛔ 不显示用户拖到的 {requestedLevel}）"
				+ " —— ⭐ 本机固件只接受 0~" + EffectiveKeyboardBrightnessMax() + " 档 ⚠️");
		}
		else if (actual < 0)
		{
			Logger.WriteError($"[kbd-cal] ⚠️ 亮度写入后**回读不到**：请求={requestedLevel}"
				+ " ⇒ ⭐ UI 无法显示实际值（⛔ 不拿请求值冒充实际值）⚠️");
		}
		Action<int> handler = KeyboardBrightnessApplied;
		if (handler == null)
		{
			return;
		}
		try
		{
			handler(actual);
		}
		catch (Exception ex)
		{
			// ⭐ UI 订阅方自己的异常⛔ 不允许影响硬件写入链路（⭐ 已写完，这里只是通知）✅
			Logger.WriteLine("[kbd-cal] 亮度实际值通知 UI 时异常（已吞掉）：" + ex.Message);
		}
	}
	// （30 系 EC 亮度）：EC 0x1807 写成功且回读已校验 ⇒ 直接把**目标档位**作为实际生效值
	private void RecordKeyboardBrightnessApplied(int appliedLevel)
	{
		LastKeyboardBrightnessRequested = appliedLevel;
		LastKeyboardBrightnessApplied = appliedLevel;
		Action<int> handler = KeyboardBrightnessApplied;
		if (handler == null)
		{
			return;
		}
		try
		{
			handler(appliedLevel);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[kbd-cal] 亮度实际值通知 UI 时异常（已吞掉）：" + ex.Message);
		}
	}
	// （机主：开关跟亮度走/统一两条路径）：写亮度档位的统一入口 ——
	private bool TryApplyKeyboardBrightnessLevel(BaolongdaWmi wmi, byte level, string source)
	{
		if (DgpuGeneration == DgpuGenerationKind.Gen30 && UsesBaolongda)
		{
			return KeyboardEcBrightness.TrySetLevel(level, wmi, out int _, out int _, source);
		}
		return wmi.TrySetKeyboardBrightness(level);
	}
	// （30 系只读诊断）： **每进程一次**，连续 5 次读 16/17/18 的**完整 OutData**
	private void ProbeKeyboardRawOnce(BaolongdaWmi wmi)
	{
		if (DgpuGeneration != DgpuGenerationKind.Gen30)
		{
			return;
		}
		if (Interlocked.CompareExchange(ref _kbdRawProbeState, 1, 0) != 0)
		{
			return;
		}
		try
		{
			for (int round = 1; round <= 5; round++)
			{
				Logger.WriteError($"[kbd-probe] 第 {round} 次 16={FormatKeyboardRaw(wmi, BaolongdaWmi.Function.RgbKeyboardMode)}"
					+ $" · 17={FormatKeyboardRaw(wmi, BaolongdaWmi.Function.RgbKeyboardColor)}"
					+ $" · 18={FormatKeyboardRaw(wmi, BaolongdaWmi.Function.RgbKeyboardBrightness)}");
				if (round < 5)
				{
					Thread.Sleep(60);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteError("[kbd-probe] raw 探测异常（已吞掉）：" + ex.Message);
		}
		finally
		{
			Volatile.Write(ref _kbdRawProbeState, 2);
		}
	}

	/// <summary>⭐ 只读：读一个功能号的完整 OutData 摘要（下标 2..8，含长度）✅。</summary>
	private static string FormatKeyboardRaw(BaolongdaWmi wmi, BaolongdaWmi.Function function)
	{
		if (!wmi.TryReadRawOutData(function, out byte[] data) || data == null)
		{
			return "fail";
		}
		int end = Math.Min(data.Length - 1, 8);
		string text = "";
		for (int i = 2; i <= end; i++)
		{
			text += (i == 2 ? "" : "-") + data[i].ToString("X2");
		}
		return "raw(OutData[2.." + end + "])=" + text + " (len=" + data.Length + ")";
	}
	// （写完立刻读是旧值、稍后读才对）。 只读； 不改写入顺序/不重试写入 。
	private void LogKeyboardRawAfterBrightnessWrite(BaolongdaWmi wmi, int requestedLevel)
	{
		if (DgpuGeneration != DgpuGenerationKind.Gen30)
		{
			return;
		}
		string text = "";
		for (int round = 1; round <= 5; round++)
		{
			if (round > 1)
			{
				Thread.Sleep(60);
			}
			text += (round == 1 ? "" : " · ") + $"[{round}]" + FormatKeyboardRaw(wmi, BaolongdaWmi.Function.RgbKeyboardBrightness);
		}
		Logger.WriteError($"[kbd-probe] 写 18={requestedLevel} 后连续读 5 次 raw：{text}");
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16：⭐ 宝龙达键盘灯「效果 Id → 模式（功能号 16）」的**单一事实来源** ✅
	/// （⭐ 下拉、⭐ 索引→Id 映射、⭐ 点进 LightForm 那扇窗、⭐ 以及下发 ⭐ **四处共用** ✅）。
	///
	/// <para>⭐⭐ <b>可写模式只有两个</b>：<c>2</c>（固定色 ✅ 原厂实际使用）与
	/// <c>1</c>（自动循环 ✅ 原厂实际使用）；<c>0</c> 不作为"写模式"使用 ——
	/// ⭐ 关灯 ＝ 写亮度（功能号 18）＝ 0 ✅。</para>
	///
	/// <para>⛔ <b><c>3</c>（自定义颜色）不在本表</b>：⭐ 40 系真机（MRID6-23）实测
	/// **写 3 后回读仍为 2** ⇒ ⭐ 固件不接受 ❌；⭐ 本协议也没有多段颜色载荷
	/// ⇒ ⭐ 按能力位铁律**不提供该选项**（⛔ 不假装支持 ✅）。
	/// 旧配置里的 <c>Manual</c> 走「没有对应固件模式 ⇒ 降级固定色（模式 2）」✅。</para>
	/// </summary>
	internal static bool TryMapBaolongdaKeyboardMode(string? effect, out byte mode)
	{
		mode = 2;
		if (string.IsNullOrWhiteSpace(effect))
		{
			return false;
		}
		if (effect.Equals("Off", StringComparison.OrdinalIgnoreCase) || effect.Equals("None", StringComparison.OrdinalIgnoreCase))
		{
			mode = 0;
			return true;
		}
		if (effect.Equals("Single", StringComparison.OrdinalIgnoreCase) || effect.Equals("Static", StringComparison.OrdinalIgnoreCase) || effect.Equals("Fixed", StringComparison.OrdinalIgnoreCase))
		{
			mode = 2;
			return true;
		}
		if (effect.Equals("Rainbow", StringComparison.OrdinalIgnoreCase))
		{
			// （软件彩虹）： 模式 1 是**本仓的哨兵值** ——
			mode = 1;
			return true;
		}
		// ⭐⭐ 2026-10-17：⭐ **不再映射到模式 3** —— ⭐ 本平台固件不接受（40 系真机写 3 回读仍为 2）❌
		//   ⇒ ⭐ 让调用方走已有的「没有对应固件模式 ⇒ 降级为固定色（模式 2）」路径 ✅
		//   （⛔ 不把 Manual 当成"官方支持的自定义颜色" ⚠️；⭐ 单色 = 模式 2 ＋ 功能号 17 ✅）
		return false;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（键盘灯「亮度不对」修正）：⭐ 界面亮度（⭐ <c>0~4</c>，⭐ <b>0 ＝ 关</b>）
	/// → ⭐ 宝龙达硬件级（⭐ <c>0~3</c>，⭐ <b>0 ＝ 灭</b>）✅ 的**唯一换算入口** ✅。
	/// <para>⚠️ <b>为什么必须由它统一换算</b>：⭐ 硬件 0 **就是灭灯**（⭐ 参考实现界面原文
	/// 「背光亮度设置为 0 时将关闭键盘灯光」✅）⇒ ⚠️ 若沿用旧调用方的 <c>light0to4 - 1</c>，
	/// ⭐ 界面第 1 档（⭐ 用户想要"最暗"）会算出硬件 <c>0</c> ⇒ ⭐ **变成关灯** ❌
	/// （⭐ 那正是"亮度不能调节"的一个真实成因 ⚠️）。
	/// ⇒ ⭐ 本换算保证：⭐ <b>0 ⇒ 0（灭）· ⭐ 正数 ⇒ 夹到 1~3，⛔ 绝不映射成 0</b> ✅。</para>
	/// </summary>
	internal static byte BaolongdaBrightnessFromUi(int ui0to4)
	{
		if (ui0to4 <= 0)
		{
			return 0;
		}
		// 现在上限 = **运行时自校准值**（探测失败才退回声明常量，当前声明值也是 3）
		return (byte)Math.Clamp(ui0to4, 1, EffectiveKeyboardBrightnessMax());
	}
	// 本机键盘灯亮度**上限**（硬件级）—— UI 滑块与所有下发夹取**共用这一个来源** 。
	internal static int EffectiveKeyboardBrightnessMax()
	{
		MechrevoHw hw = Program.hw;
		KeyboardLightingCalibration caps = hw?.KeyboardLightingCaps;
		if (caps != null && caps.BrightnessProbed)
		{
			return Math.Max(1, caps.BrightnessMax);
		}
		return Math.Max(1, BaolongdaProfile.BrightnessMax(hw?.DgpuGeneration ?? DgpuGenerationKind.Unknown));
	}
	// 键盘灯**亮度上限探测** —— 打开键盘灯设置时调用一次，结果缓存。
	internal KeyboardLightingCalibration EnsureKeyboardLightingCalibration()
	{
		KeyboardLightingCalibration cached = Volatile.Read(ref _kbdCaps);
		if (cached != null)
		{
			return cached;
		}
		lock (_kbdCapsGate)
		{
			cached = _kbdCaps;
			if (cached != null)
			{
				return cached;
			}
			KeyboardLightingCalibration probed = ProbeKeyboardLightingCapabilities();
			Volatile.Write(ref _kbdCaps, probed);
			return probed;
		}
	}

	/// <summary>
	/// ⭐⭐ 宝龙达键盘灯**亮度上限探测本体**（只在本机固件上做有限次写；全程 try/finally 恢复）✅。
	/// <para>⚠️ 判据（与机主真机口径一致）：亮度探测范围 <b>1..声明上限（当前 3）</b> ——
	/// ⛔ 不试 4/5/6，避免"写超范围被夹回"反而误判（也避免把键盘写到不支持的值上）✅。</para>
	/// <para>⭐ 失败保守：亮度上限保持**声明值**（⛔ 不凭探测失败就缩小可调范围）✅。</para>
	/// <para>⚠️ 彩虹**不在这里探测**：它已改为软件刷色（`StartKeyboardRainbow`），与固件模式无关 ✅。</para>
	/// </summary>
	private KeyboardLightingCalibration ProbeKeyboardLightingCapabilities()
	{
		DgpuGenerationKind gen = DgpuGeneration;
		int declaredMax = Math.Max(1, BaolongdaProfile.BrightnessMax(gen));
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			string reason = "非宝龙达后端 / 实例不可用";
			Logger.WriteError("[kbd-cal] 跳过探测（" + reason + "）⇒ 保守：亮度上限=" + declaredMax + "（声明值）");
			return KeyboardLightingCalibration.Conservative(declaredMax, reason);
		}
		// ── ⭐ ① 记录原始亮度（读不到就**不写**，否则无法恢复）──
		bool brightnessKnown = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte originalBrightness);
		Logger.WriteError($"[kbd-cal] 开始探测键盘灯亮度上限：当前亮度={(brightnessKnown ? originalBrightness.ToString() : "读不到")}"
			+ $" · 声明上限={declaredMax}（探测后**恢复原值**）");
		LogKeyboardLightingState(wmi, "探测前");
		int probedMax = 0;
		string clampNote = string.Empty;
		try
		{
			// ── ⭐ ② 亮度上限：写 k → 回读，能写进去的最大 k ──
			if (!brightnessKnown)
			{
				Logger.WriteError("[kbd-cal] ⚠️ 原始亮度读不到 ⇒ **跳过亮度写入探测**"
					+ "（无法恢复 ⇒ 宁可不测，⛔ 不把键盘留在探测状态）");
			}
			else
			{
				for (int k = 1; k <= declaredMax; k++)
				{
					bool writeOk = wmi.TrySetKeyboardBrightness((byte)k);
					int readback = writeOk ? ReadKeyboardBrightnessAfterWrite(wmi, k, attempts: 3, delayMs: 80) : -1;
					string readText = (readback < 0) ? "读不到" : readback.ToString();
					Logger.WriteError($"[kbd-cal] 亮度探测：写 {k} ⇒ 回读 {readText}");
					if (readback == k)
					{
						probedMax = k;
						continue;
					}
					clampNote = (readback >= 0)
						? $"写到 {k} 回读为 {readback} 被夹回"
						: $"写 {k} 后回读不到";
					break;
				}
			}
		}
		finally
		{
			// ── ⭐ ③ 无论成功/失败/异常 ⇒ 恢复原始亮度 ──
			RestoreKeyboardBrightnessAfterCalibration(wmi, brightnessKnown, originalBrightness);
		}
		// ── ⭐ ④ 汇总 + 缓存 ──
		bool brightnessProbed = probedMax >= 1;
		int brightnessMax = brightnessProbed ? Math.Clamp(probedMax, 1, declaredMax) : declaredMax;
		string brightnessPart;
		if (brightnessProbed)
		{
			brightnessPart = (brightnessMax >= declaredMax)
				? $"亮度上限={brightnessMax}（与机主口径一致）"
				: $"亮度上限={brightnessMax}（{clampNote}；声明上限={declaredMax}）";
		}
		else
		{
			brightnessPart = (clampNote.Length > 0)
				? $"亮度上限={brightnessMax}（声明值 · 探测失败：{clampNote}）"
				: $"亮度上限={brightnessMax}（声明值 · 探测失败）";
		}
		Logger.WriteError("[kbd-cal] " + brightnessPart);
		return KeyboardLightingCalibration.FromProbe(declaredMax, probedMax, brightnessPart);
	}

	/// <summary>⭐ 写亮度后**带重试**回读（等待固件生效；⛔ 一次读不到就下结论）✅。</summary>
	private static int ReadKeyboardBrightnessAfterWrite(BaolongdaWmi wmi, int expected, int attempts, int delayMs)
	{
		int last = -1;
		for (int attempt = 0; attempt < attempts; attempt++)
		{
			Thread.Sleep(delayMs);
			if (!wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte value))
			{
				continue;
			}
			last = value;
			if (value == expected)
			{
				return value;
			}
		}
		return last;
	}

	/// <summary>
	/// ⭐⭐ 探测收尾：把键盘灯**亮度恢复成探测前的值** ✅。
	/// <para>⚠️ 最多重试 3 次写 + 回读；**恢复不一致也如实记日志**（⛔ 不假装恢复成功）⚠️。</para>
	/// <para>⚠️ 原值读不到时**不写**（⭐ 探测时也没写 ⇒ 它本来就该在原状态）✅。</para>
	/// <para>⚠️ 亮度探测**不碰键盘模式**：所以这里只需恢复亮度（模式自始至终没被改）✅。</para>
	/// </summary>
	private static void RestoreKeyboardBrightnessAfterCalibration(BaolongdaWmi wmi, bool brightnessKnown, byte originalBrightness)
	{
		if (!brightnessKnown)
		{
			Logger.WriteError("[kbd-cal] ⚠️ 原亮度读不到 ⇒ 无法恢复（探测未写亮度，键盘仍在原状态）");
			return;
		}
		int restoredBrightness = -1;
		for (int attempt = 0; attempt < 3; attempt++)
		{
			wmi.TrySetKeyboardBrightness(originalBrightness);
			restoredBrightness = ReadKeyboardBrightnessAfterWrite(wmi, originalBrightness, attempts: 2, delayMs: 60);
			if (restoredBrightness == originalBrightness)
			{
				break;
			}
		}
		Logger.WriteError($"[kbd-cal] 恢复亮度：写 {originalBrightness} ⇒ 回读 {(restoredBrightness < 0 ? "读不到" : restoredBrightness.ToString())}"
			+ (restoredBrightness == originalBrightness ? " ✅" : " ⚠️ 恢复不一致（如实记录）"));
		LogKeyboardLightingState(wmi, "探测后（已恢复）");
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 键盘灯）：⭐ 键盘灯的**默认「仅错误」级别可见**诊断 ——
	/// 把功能号 ⭐ 10 ／ 16 ／ 17(3 字节) ／ 18 的真实值**全打出来** ✅。
	/// <para>⚠️ <b>为什么必须打</b>（真机实测对照 · p1 vs p2）：
	/// ⭐ 功能号 10 **恒为 0**（不随状态变化）· ⭐ 功能号 18（亮度）**会变**（p2 期间 0 ⇒ 3 ✅）·
	/// ⭐ 功能号 16（模式）写 0 后**回读仍为 2** ❌
	/// ⇒ ⭐ 下一份诊断包据此可定位「⭐ 模式写为何不生效」⚠️（⭐ 这是当前唯一没查清的一环 ✅）。</para>
	/// </summary>
	private static void LogKeyboardLightingState(BaolongdaWmi wmi, string phase)
	{
		bool ok10 = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardStatus, out byte v10);
		bool ok16 = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardMode, out byte v16);
		bool ok18 = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte v18);
		bool ok17 = wmi.TryReadBytesChecked(BaolongdaWmi.Function.RgbKeyboardColor, 3, out byte[] c17);
		string rgb = (ok17 && c17 != null && c17.Length >= 3)
			? $"{c17[0]},{c17[1]},{c17[2]}"
			: "读不到";
		Logger.WriteError($"宝龙达 键盘灯[{phase}]：功能号 10 状态={(ok10 ? v10.ToString() : "读不到")}"
			+ $" · 16 模式={(ok16 ? v16.ToString() : "读不到")} · 17 颜色={rgb}"
			+ $" · 18 亮度={(ok18 ? v18.ToString() : "读不到")}"
			+ "（⭐ 判据用 16：0=关 · 非0=开；⚠️ 10 恒 0 故不作判据 ⚠️）");
	}
	// ④ 停止后**再无周期性 WMI 写入**：Stop 先取消、等循环退出，再恢复单色
	private const double RainbowHueStepDegrees = 3.0;

	/// <summary>⭐ 软件彩虹：基础帧间隔（毫秒）✅。</summary>
	private const int RainbowFrameIntervalMs = 50;

	/// <summary>⭐ 软件彩虹：单帧写入超过这个耗时 ⇒ 自动放慢到 80ms/帧（机主要求）✅。</summary>
	private const int RainbowSlowWriteThresholdMs = 40;

	/// <summary>⭐ 软件彩虹：放慢后的帧间隔（毫秒）✅。</summary>
	private const int RainbowSlowFrameIntervalMs = 80;
	// （机主批准「软件彩虹」）：**启动软件彩虹刷色**
	internal void StartKeyboardRainbow(byte brightness, CancellationToken token)
	{
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return;
		}
		// ⭐ 先把可能还在跑的旧循环停掉（不恢复单色 ⇒ 马上会被本方法重新写）✅
		StopKeyboardRainbowCore(restoreSingleColor: false, noRestoreLog: "[kbd] 软件彩虹：停止（准备重新启动）");
		byte level = (byte)Math.Clamp((int)brightness, 1, EffectiveKeyboardBrightnessMax());
		// ⭐ ① 起始状态：固定色模式 16=2 + 亮度 18（7.3 彩虹的起始状态）✅
		bool modeOk = wmi.TrySetKeyboardMode(2);
		bool brightOk = TryApplyKeyboardBrightnessLevel(wmi, level, "软件彩虹起始亮度");
		CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token);
		int generation;
		lock (_rainbowGate)
		{
			generation = Interlocked.Increment(ref _rainbowGeneration);
			_rainbowCts = linked;
			_rainbowBrightness = level;
			_rainbowRunning = true;
			_rainbowTask = Task.Run(() => RunKeyboardRainbowLoopAsync(generation, linked.Token));
		}
		Logger.WriteError($"[kbd] 软件彩虹：启动（{RainbowFrameIntervalMs}ms/帧 · 色相步进 {RainbowHueStepDegrees:0}° · 亮度={level}）"
			+ $"（起始 16=2 {(modeOk ? "ok" : "失败")} · 18 {(brightOk ? "ok" : "失败")}）");
	}
	// <para> 取消后台循环 → 等它真正退出（最多 500ms）→ 恢复「模式 2 + 用户当前颜色/亮度」。</para>
	internal void StopKeyboardRainbow()
	{
		StopKeyboardRainbowCore(restoreSingleColor: true);
	}

	/// <summary>⭐ 软件彩虹停止/重启共用实现（<paramref name="restoreSingleColor" /> = 是否恢复单色）✅。</summary>
	/// <param name="restoreSingleColor">⭐ 是否在停止后恢复单色（自动关灯等场景传 <c>false</c>，由调用方自己写目标状态）✅。</param>
	/// <param name="noRestoreLog">⭐ 不恢复单色时要打的日志（<c>null</c> = 安静；自动关灯有自己的一行日志）✅。</param>
	private void StopKeyboardRainbowCore(bool restoreSingleColor, string noRestoreLog = null)
	{
		CancellationTokenSource cts;
		Task task;
		byte brightness;
		lock (_rainbowGate)
		{
			cts = _rainbowCts;
			task = _rainbowTask;
			brightness = _rainbowBrightness;
			_rainbowCts = null;
			_rainbowTask = null;
			_rainbowRunning = false;
			Interlocked.Increment(ref _rainbowGeneration);
		}
		if (cts == null)
		{
			return; // ⭐ 本来就没在跑 ⇒ 幂等返回，⛔ 不再写一个字节 ✅
		}
		try
		{
			cts.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		// ⭐ 等后台循环**真正退出**后再恢复 ⇒ ⛔ 停止后不会再有一帧写到硬件 ✅
		try
		{
			if (task != null && !task.Wait(500))
			{
				Logger.WriteError("[kbd] 软件彩虹：⚠️ 停止等待超时（后台循环 500ms 内未退出，仍会尽快自行退出）");
			}
		}
		catch (AggregateException)
		{
			// ⭐ 循环内部已吞异常 ⇒ 这里只是防御，⛔ 不让它影响停止流程 ✅
		}
		cts.Dispose();
		if (restoreSingleColor)
		{
			RestoreKeyboardAfterRainbow(brightness);
		}
		else if (noRestoreLog != null)
		{
			Logger.WriteError(noRestoreLog);
		}
	}

	/// <summary>⭐ 软件彩虹运行中亮度变化：⛔ 只更新亮度、**不重启循环**（否则色相会跳回 0）✅。</summary>
	internal void UpdateKeyboardRainbowBrightness(byte brightness)
	{
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null)
		{
			return;
		}
		byte level = (byte)Math.Clamp((int)brightness, 1, EffectiveKeyboardBrightnessMax());
		lock (_rainbowGate)
		{
			_rainbowBrightness = level;
		}
		bool ok = TryApplyKeyboardBrightnessLevel(wmi, level, "软件彩虹亮度更新");
		Logger.WriteError($"[kbd] 软件彩虹：亮度更新={level}（循环继续，⛔ 不重启）⇒ ok={ok}");
	}

	/// <summary>
	/// ⭐ 软件彩虹停止后：恢复「模式 2 + 用户当前颜色/亮度」✅。
	/// <para>⚠️ 颜色取**已保存的用户配置**（<see cref="LightChannelSettings.EffectiveColorArgb" />），
	/// ⛔ 绝不用彩虹最后一帧的颜色当"用户颜色"✅。</para>
	/// </summary>
	private void RestoreKeyboardAfterRainbow(byte brightness)
	{
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return;
		}
		LightChannelSettings stored = LightingSettingsStore.Load("Keyboard/Ctrl", "Single");
		int argb = stored.EffectiveColorArgb();
		int level = Math.Clamp(Math.Max(1, (int)brightness), 1, EffectiveKeyboardBrightnessMax());
		int r = (argb >> 16) & 0xFF;
		int g = (argb >> 8) & 0xFF;
		int b = argb & 0xFF;
		bool modeOk = wmi.TrySetKeyboardMode(2);
		bool colorOk = wmi.TrySetKeyboardColor((byte)r, (byte)g, (byte)b);
		bool brightOk = TryApplyKeyboardBrightnessLevel(wmi, (byte)level, "彩虹停止恢复单色亮度");
		Logger.WriteError($"[kbd] 软件彩虹：停止 ⇒ 恢复单色 rgb=#{(argb & 0xFFFFFF):X6} 亮度={level}"
			+ $"（16=2 {(modeOk ? "ok" : "失败")} · 17 {(colorOk ? "ok" : "失败")} · 18 {(brightOk ? "ok" : "失败")}）");
	}

	/// <summary>
	/// ⭐ 软件彩虹后台循环：每帧只写 <c>17=RGB</c>；`Stopwatch` 绝对时刻调度（⛔ 不累积漂移）✅。
	/// <para>⚠️ 任何异常**一律吞掉并停止**；取消是正常路径（⛔ 不记错误）✅。</para>
	/// </summary>
	private async Task RunKeyboardRainbowLoopAsync(int generation, CancellationToken token)
	{
		double hue = 0.0;
		int intervalMs = RainbowFrameIntervalMs;
		System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
		long nextDueMs = 0;
		try
		{
			while (!token.IsCancellationRequested)
			{
				BaolongdaWmi wmi = _baolongda?.Wmi;
				if (wmi == null)
				{
					Logger.WriteError("[kbd] 软件彩虹：⚠️ 宝龙达后端已不可用 ⇒ 停止");
					return;
				}
				(byte r, byte g, byte b) = HsbToRgb(hue, 1.0, 1.0);
				long frameStartMs = clock.ElapsedMilliseconds;
				// ⭐ 每帧只写一个功能号（17 = 颜色）✅
				if (!wmi.TryWriteColor(r, g, b))
				{
					Logger.WriteError("[kbd] 软件彩虹：⚠️ 写颜色（功能号 17）失败 ⇒ 停止");
					return;
				}
				long writeMs = clock.ElapsedMilliseconds - frameStartMs;
				if (writeMs > RainbowSlowWriteThresholdMs && intervalMs != RainbowSlowFrameIntervalMs)
				{
					intervalMs = RainbowSlowFrameIntervalMs;
					Logger.WriteError($"[kbd] 软件彩虹：⚠️ 单帧写入耗时 {writeMs}ms > {RainbowSlowWriteThresholdMs}ms"
						+ $" ⇒ 自动放慢到 {intervalMs}ms/帧");
				}
				hue += RainbowHueStepDegrees;
				if (hue >= 360.0)
				{
					hue -= 360.0;
				}
				nextDueMs += intervalMs;
				long delayMs = nextDueMs - clock.ElapsedMilliseconds;
				if (delayMs > 0)
				{
					await Task.Delay((int)delayMs, token).ConfigureAwait(continueOnCapturedContext: false);
				}
				else if (clock.ElapsedMilliseconds - nextDueMs > 1000)
				{
					// ⭐ 落后超过 1s（系统挂起/被抢占）⇒ 直接对齐当前时刻，⛔ 不补帧风暴 ✅
					nextDueMs = clock.ElapsedMilliseconds;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// ⭐ 正常停止（Stop 取消）⇒ 不记错误 ✅
		}
		catch (Exception ex)
		{
			// ⭐ 任何异常**一律吞掉并停止**（⛔ 不让它穿透后台 Task，防卡死）✅
			Logger.WriteError("[kbd] 软件彩虹：⚠️ 异常已停止：" + ex.Message);
		}
		finally
		{
			clock.Stop();
			// ⭐ 只清理"自己这一代"的运行标志（否则会把刚启动的新循环误标成已停）✅
			if (generation == Volatile.Read(ref _rainbowGeneration))
			{
				_rainbowRunning = false;
			}
		}
	}

	/// <summary>
	/// ⭐ 软件彩虹用：**HSB → RGB** 换算（色相 0~360° · 饱和度/明度 0~1 → RGB 0~255）✅。
	/// <para>⚠️ 本仓**自写**的换算（标准六扇区算法），⛔ 未复制任何第三方代码 ✅。</para>
	/// </summary>
	private static (byte R, byte G, byte B) HsbToRgb(double hueDegrees, double saturation, double value)
	{
		double h = hueDegrees % 360.0;
		if (h < 0.0)
		{
			h += 360.0;
		}
		h /= 60.0;
		double s = Math.Clamp(saturation, 0.0, 1.0);
		double v = Math.Clamp(value, 0.0, 1.0);
		int sector = (int)Math.Floor(h) % 6;
		double f = h - Math.Floor(h);
		double p = v * (1.0 - s);
		double q = v * (1.0 - s * f);
		double t = v * (1.0 - s * (1.0 - f));
		(double r, double g, double b) = sector switch
		{
			0 => (v, t, p),
			1 => (q, v, p),
			2 => (p, v, t),
			3 => (p, q, v),
			4 => (t, p, v),
			_ => (v, p, q)
		};
		return (
			(byte)Math.Round(r * 255.0),
			(byte)Math.Round(g * 255.0),
			(byte)Math.Round(b * 255.0));
	}
	// 红线：5s 检查**只读**（GetLastInputInfo + BaolongdaState 缓存）；
	[StructLayout(LayoutKind.Sequential)]
	private struct KeyboardAutoOffLastInputInfo
	{
		internal uint cbSize;
		internal uint dwTime;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool GetLastInputInfo(ref KeyboardAutoOffLastInputInfo plii);

	/// <summary>⭐ 读系统输入空闲毫秒（⭐ 读不到 ⇒ <c>false</c> ⇒ 自动关灯**不动作**，保守）✅。</summary>
	private static bool TryGetKeyboardIdleMilliseconds(out long idleMilliseconds)
	{
		idleMilliseconds = 0;
		try
		{
			KeyboardAutoOffLastInputInfo info = default;
			info.cbSize = (uint)Marshal.SizeOf<KeyboardAutoOffLastInputInfo>();
			if (!GetLastInputInfo(ref info))
			{
				return false;
			}
			idleMilliseconds = unchecked((uint)Environment.TickCount - info.dwTime);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>⭐ 自动关灯配置的**唯一读入口**（秒；0 = 关）—— 缺键默认 0，旧键 `light_autooff_minutes` 只迁移一次 ✅。</summary>
	internal static int GetKeyboardAutoOffSeconds()
	{
		int cached = Volatile.Read(ref _kbdAutoOffSecondsCache);
		if (cached >= 0)
		{
			return cached;
		}
		lock (_kbdAutoOffConfigGate)
		{
			cached = _kbdAutoOffSecondsCache;
			if (cached >= 0)
			{
				return cached;
			}
			int seconds = AppConfig.Get("light_autooff_seconds", -1);
			if (seconds < 0)
			{
				// ⭐ 旧键迁移（只读一次、只写新键；⛔ 不维护两个键并行）✅
				int oldMinutes = AppConfig.Get("light_autooff_minutes", 0);
				seconds = oldMinutes switch
				{
					5 => 300,
					10 => 600,
					30 => 1800,
					_ => 0
				};
				AppConfig.Set("light_autooff_seconds", seconds);
			}
			seconds = NormalizeKeyboardAutoOffSeconds(seconds);
			Volatile.Write(ref _kbdAutoOffSecondsCache, seconds);
			return seconds;
		}
	}

	/// <summary>⭐ 自动关灯配置的**唯一写入口**（秒；只接受 0/10/300/600/1800）✅。</summary>
	internal static void SetKeyboardAutoOffSeconds(int seconds)
	{
		seconds = NormalizeKeyboardAutoOffSeconds(seconds);
		lock (_kbdAutoOffConfigGate)
		{
			AppConfig.Set("light_autooff_seconds", seconds);
			Volatile.Write(ref _kbdAutoOffSecondsCache, seconds);
		}
	}

	/// <summary>⭐ 把任意输入收敛到受支持的选项值（0 = 关；其它一律按"关"处理，⛔ 不猜）✅。</summary>
	private static int NormalizeKeyboardAutoOffSeconds(int seconds)
	{
		// 2026-10-22（机主：官方控制台能选 10/15/20/30/45 秒等，LM 也要能选）：
		// 只接受选项表里出现过的值，其它一律按“关”处理。
		return seconds switch
		{
			10 => 10,
			15 => 15,
			20 => 20,
			30 => 30,
			45 => 45,
			60 => 60,
			120 => 120,
			300 => 300,
			600 => 600,
			900 => 900,
			1200 => 1200,
			1800 => 1800,
			2700 => 2700,
			3600 => 3600,
			7200 => 7200,
			10800 => 10800,
			_ => 0
		};
	}

	/// <summary>⭐ 人读描述（日志用；秒 ⇒ "10 秒" / "N 分钟"）✅。</summary>
	private static string DescribeKeyboardAutoOffSeconds(int seconds)
	{
		if (seconds < 60)
		{
			return seconds + " 秒";
		}
		if (seconds % 3600 == 0)
		{
			return (seconds / 3600) + " 小时";
		}
		return (seconds / 60) + " 分钟";
	}

	/// <summary>
	/// ⭐⭐ 应用「自动关灯」配置（启动 / 设置变更 / 键盘灯写入时调用）✅：
	/// ⭐ 0 = 关闭 ⇒ **停计时器、不创建任何计时任务**；&gt;0 ⇒ 只创建一个 **5s tick** 计时器（⛔ 不高频）✅。
	/// <para>⚠️ 由关闭切到启用时打一行 `[light] 自动关灯：已启用（…）`；默认关**不记**（保持安静）✅。</para>
	/// </summary>
	internal void ApplyKeyboardAutoOffSetting()
	{
		if (_disposed || !UsesBaolongda)
		{
			StopKeyboardAutoOffTimer();
			return;
		}
		int seconds = GetKeyboardAutoOffSeconds();
		if (seconds <= 0)
		{
			StopKeyboardAutoOffTimer();
			// ⭐ 用户把功能关掉时若正处于"自动关灯"状态 ⇒ 立刻恢复一次（用户刚操作 ≠ 空闲）✅
			if (Volatile.Read(ref _kbdAutoOffApplied) != 0)
			{
				RestoreKeyboardAfterAutoOff(0L, 0);
			}
			return;
		}
		if (_kbdAutoOffTimer == null)
		{
			_kbdAutoOffTimer = new System.Threading.Timer(KeyboardAutoOffTick, null, 5000, 5000);
		}
		if (Interlocked.Exchange(ref _kbdAutoOffEnabledLogged, 1) == 0)
		{
			Logger.WriteError("[light] 自动关灯：已启用（" + DescribeKeyboardAutoOffSeconds(seconds) + " · 软件计时 · 平台=宝龙达）");
		}
	}

	/// <summary>⭐ 停掉自动关灯计时器（幂等；⛔ 不写灯）✅。</summary>
	private void StopKeyboardAutoOffTimer()
	{
		System.Threading.Timer timer = Interlocked.Exchange(ref _kbdAutoOffTimer, null);
		timer?.Dispose();
	}

	/// <summary>
	/// ⭐ 5s tick：**只读检查**（`GetLastInputInfo` + `BaolongdaState` 缓存）✅。
	/// <para>⛔ 正常情况下**一行日志都不打**（只有"超时/恢复/读不到/异常"这些状态变化才记，且去重）✅。</para>
	/// </summary>
	private void KeyboardAutoOffTick(object state)
	{
		if (_disposed || Interlocked.Exchange(ref _kbdAutoOffTickBusy, 1) != 0)
		{
			return;
		}
		try
		{
			KeyboardAutoOffTickCore();
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("kbd-autooff-tick-fail", "[light] 自动关灯：检查异常（已忽略）：" + ex.Message);
		}
		finally
		{
			Interlocked.Exchange(ref _kbdAutoOffTickBusy, 0);
		}
	}

	/// <summary>⭐ tick 主体（⭐ 只读；只有真超时/真恢复才调写入）✅。</summary>
	private void KeyboardAutoOffTickCore()
	{
		if (_disposed || !UsesBaolongda)
		{
			return;
		}
		int seconds = GetKeyboardAutoOffSeconds();
		if (seconds <= 0)
		{
			return; // ⭐ 功能关闭 ⇒ 直接返回（连 GetLastInputInfo 都不调）✅
		}
		if (!TryGetKeyboardIdleMilliseconds(out long idleMilliseconds))
		{
			Logger.WriteLineIfChanged("kbd-autooff-idle-unreadable",
				"[light] 自动关灯：读不到系统空闲时间 ⇒ 本轮**不关灯**（保守）");
			return;
		}
		long thresholdMilliseconds = (long)seconds * 1000L;
		bool applied = Volatile.Read(ref _kbdAutoOffApplied) != 0;
		if (!applied)
		{
			if (idleMilliseconds < thresholdMilliseconds)
			{
				Volatile.Write(ref _kbdAutoOffSuppressUntilInput, 0);
				return; // ⭐ 未超时 ⇒ 纯只读返回 ✅
			}
			// （机主：开灯后过一会自己变关）：最近 5s 有过**用户灯效动作** ⇒ 不触发自动关
			long lastUserAction = Interlocked.Read(ref _kbdLastUserActionTick);
			// 自动关灯按「开灯后经过的时间」算，不能拿系统空闲时间直接套：
			// 用户手动开灯时系统可能已经空闲很久，5s 保护期一过就会被立刻关掉。
			long actionGuard = Math.Max(KeyboardAutoOffUserActionGuardMs, thresholdMilliseconds);
			if (lastUserAction != 0 && Environment.TickCount64 - lastUserAction < actionGuard)
			{
				Logger.WriteLineIfChanged("kbd-autooff-recent-action",
					"[light] 自动关灯：本次开灯后未满 " + (actionGuard / 1000) + "s ⇒ 本轮不触发");
				return;
			}
			if (Volatile.Read(ref _kbdAutoOffSuppressUntilInput) != 0)
			{
				return; // ⭐ 上次写失败 ⇒ 等用户重新输入后再试（⛔ 不每 5s 重写）✅
			}
			TryApplyKeyboardAutoOff(seconds, idleMilliseconds);
			return;
		}
		// ⭐ 已自动关灯：一旦检测到新输入（空闲时间回到阈值内）⇒ 恢复一次 ✅
		if (idleMilliseconds >= thresholdMilliseconds)
		{
			return;
		}
		// ⭐⭐ 恢复前**再读一次只读缓存**：若灯已经被"别的路径"重新点亮（用户按 Fn / 第三方控制台）
		//   ⇒ ⛔ 不拿记录的旧状态覆盖它（清标记、静默返回；⛔ 不写任何灯）✅
		BaolongdaStateSnapshot restoreSnapshot = BaolongdaState;
		if (restoreSnapshot?.KeyboardBrightness.HasValue == true && restoreSnapshot.KeyboardBrightness.Value > 0)
		{
			lock (_kbdAutoOffGate)
			{
				_kbdAutoOffSaved = null;
				Volatile.Write(ref _kbdAutoOffApplied, 0);
			}
			return;
		}
		RestoreKeyboardAfterAutoOff(idleMilliseconds, seconds);
	}

	/// <summary>
	/// ⭐⭐ **关灯动作**（只有真正超时那一刻调用一次）✅：
	/// ⭐ 记录关灯前完整状态 → 若彩虹在跑先停循环（⛔ 不恢复单色）→ 只写一次亮度 0 ✅。
	/// </summary>
	private void TryApplyKeyboardAutoOff(int seconds, long idleMilliseconds)
	{
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return;
		}
		// ⭐ **只读状态缓存**判断"灯是否开着"（3s 刷新的快照；读不到/不是开 ⇒ ⛔ 不写、不标记）✅
		BaolongdaStateSnapshot snapshot = BaolongdaState;
		int? cachedBrightness = snapshot?.KeyboardBrightness;
		if (!cachedBrightness.HasValue || cachedBrightness.Value <= 0)
		{
			return;
		}
		bool rainbow = IsKeyboardRainbowRunning;
		LightChannelSettings stored = LightingSettingsStore.Load("Keyboard/Ctrl", "Single");
		KeyboardAutoOffSavedState saved = new KeyboardAutoOffSavedState
		{
			Rainbow = rainbow,
			ColorArgb = stored.EffectiveColorArgb(),
			Brightness = rainbow
				? (byte)Math.Clamp(ReadKeyboardRainbowBrightness(), 1, EffectiveKeyboardBrightnessMax())
				: (byte)Math.Clamp(cachedBrightness.Value, 1, EffectiveKeyboardBrightnessMax())
		};
		lock (_kbdAutoOffGate)
		{
			if (Volatile.Read(ref _kbdAutoOffApplied) != 0)
			{
				return;
			}
			_kbdAutoOffSaved = saved;
			Volatile.Write(ref _kbdAutoOffApplied, 1);
		}
		// ⭐⭐ 只有"真正超时这一刻"才写灯 ✅
		if (rainbow)
		{
			StopKeyboardRainbowCore(restoreSingleColor: false, noRestoreLog: null);
		}
		bool ok = (DgpuGeneration == DgpuGenerationKind.Gen30 && UsesBaolongda)
			? KeyboardEcBrightness.TrySetOff(wmi, out int _, out int _, "自动关灯(EC)")
			: wmi.TrySetKeyboardBrightness(0);
		LastKeyboardBrightnessRequested = 0;
		LastKeyboardBrightnessApplied = ok ? 0 : -1;
		if (!ok)
		{
			lock (_kbdAutoOffGate)
			{
				_kbdAutoOffSaved = null;
				Volatile.Write(ref _kbdAutoOffApplied, 0);
			}
			Volatile.Write(ref _kbdAutoOffSuppressUntilInput, 1);
			Logger.WriteError("[light] 自动关灯：⚠️ 写亮度 0 失败 ⇒ 本轮不标记（等你重新输入后再试）");
			return;
		}
		Logger.WriteError($"[light] 自动关灯：空闲 {idleMilliseconds / 1000.0:0} 秒 ≥ {seconds} 秒"
			+ $"（{DescribeKeyboardAutoOffSeconds(seconds)}）⇒ 已关灯（亮度 0{(rainbow ? " · 先停了软件彩虹" : "")}）");
	}

	/// <summary>⭐ 读软件彩虹当前亮度（快照用；未在跑时无意义）✅。</summary>
	private int ReadKeyboardRainbowBrightness()
	{
		lock (_rainbowGate)
		{
			return _rainbowBrightness;
		}
	}

	/// <summary>
	/// ⭐⭐ **恢复动作**（只有检测到新输入、且 <c>_kbdAutoOffApplied == true</c> 时调用一次）✅：
	/// ⭐ 按关灯前记录恢复（彩虹 ⇒ 重启软件彩虹；单色 ⇒ 写回原模式/颜色/亮度）✅。
	/// </summary>
	private void RestoreKeyboardAfterAutoOff(long idleMilliseconds, int seconds)
	{
		KeyboardAutoOffSavedState saved;
		lock (_kbdAutoOffGate)
		{
			if (Volatile.Read(ref _kbdAutoOffApplied) == 0)
			{
				return; // ⭐ 用户手动关的灯（标记已被清）⇒ ⛔ 不恢复 ✅
			}
			saved = _kbdAutoOffSaved;
			_kbdAutoOffSaved = null;
			Volatile.Write(ref _kbdAutoOffApplied, 0);
		}
		if (saved == null)
		{
			return;
		}
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			// ⭐ 后端没就绪 ⇒ 放回标记，下次 tick 再试（⛔ 不丢恢复意图）✅
			lock (_kbdAutoOffGate)
			{
				if (Volatile.Read(ref _kbdAutoOffApplied) == 0)
				{
					_kbdAutoOffSaved = saved;
					Volatile.Write(ref _kbdAutoOffApplied, 1);
				}
			}
			return;
		}
		Interlocked.Exchange(ref _kbdAutoOffRestoring, 1);
		try
		{
			if (saved.Rainbow)
			{
				StartKeyboardRainbow(saved.Brightness, CancellationToken.None);
			}
			else if (saved.Brightness > 0)
			{
				TrySetKeyboardLightingViaBackend(true, saved.ColorArgb, saved.Brightness, (byte)2, out _);
			}
			Logger.WriteError($"[light] 自动关灯：检测到新输入"
				+ (seconds > 0 ? $"（空闲 {idleMilliseconds / 1000.0:0} 秒 < {seconds} 秒）" : "（功能已关闭）")
				+ $"⇒ 已恢复关灯前状态（{(saved.Rainbow ? "软件彩虹" : "单色")} · 亮度={saved.Brightness}）");
		}
		finally
		{
			Interlocked.Exchange(ref _kbdAutoOffRestoring, 0);
		}
	}

	/// <summary>
	/// ⭐⭐ **用户手动改灯** ⇒ 清 `_kbdAutoOffApplied`（⛔ 之后绝不误恢复用户手动关掉的灯）✅。
	/// <para>⚠️ 自动恢复自身引起的写入用 <c>_kbdAutoOffRestoring</c> 排除，⛔ 不会被当成"手动改灯"✅。</para>
	/// </summary>
	private void ClearKeyboardAutoOffApplied(string reason)
	{
		if (Volatile.Read(ref _kbdAutoOffApplied) == 0)
		{
			return;
		}
		lock (_kbdAutoOffGate)
		{
			if (Volatile.Read(ref _kbdAutoOffApplied) == 0)
			{
				return;
			}
			_kbdAutoOffSaved = null;
			Volatile.Write(ref _kbdAutoOffApplied, 0);
		}
		Volatile.Write(ref _kbdAutoOffSuppressUntilInput, 0);
		Logger.WriteLine("[light] 自动关灯：用户手动改灯 ⇒ 已取消自动恢复标记（" + reason + "）");
	}

	// ⭐⭐ 2026-10-17（接手 · 按**原厂口径**修正转速百分比）：⭐ 100% 对应的 RPM ✅
	//
	//  ⭐⭐ （
	//     ⛔ 源码/注释里**不放**它的代码片段，法律风险；取证原文只留在 docs 的分析记录里）：
	//     · 采样：功能号 13（`CPUGPUFanSpeed`）—— 我们本来就在读 ✅
	//     · 折算：RPM **÷ 60** 得到百分比 ⇒ ⭐ **100% = 6000 RPM** ✅
	//     · 显示：四舍五入成**整数**百分比（`33%`）✅
	//  真机对照（40 系 `MRID6-23` / 4070）：官方 1965 RPM ⇒ **33%**（1965 ÷ 6000 = 32.75 ✅）。
	//
	//  ⭐ 6000 不是猜的：功能号 21 的**写**口径是 `x100 RPM`、分母 60（原厂 40 系百分比口径）
	//    （原厂手动转速范围 22~58 ⇒ 2200~5800 RPM）⇒ 60 × 100 = 6000 就是 **40 系百分比**的满刻度 ✅。
	//    ⭐⭐ 2026-10-18（机主 30 系 · 按开源蛟龙放宽）：⭐ 功能号 21 的**写入档位上限**已从 60 放宽到
	//    <see cref="BaolongdaProfile.FanManualMaxUnits100Rpm" /> = **68**（6800 RPM，⭐ 参考 JiaolongControl (MIT)
	//    的 `MAX_FAN_BYTE`，⛔ 未复制代码）—— ⚠️ 但**百分比分母不变**（40 系 6000 / 30 系 4900，⭐ 见下）✅
	//
	//  ⚠️⚠️ **为什么不再读功能号 21**：原厂把它当**手动转速设定值**用 ——
	//    读回来的就是"上次写下去的那个值"（用来回显/定位手动滑块），⛔ 从来不是"最大转速"。
	//    旧实现把读数（真机 = 50）当分母 ⇒ 5000 ⇒ 1996 RPM 显示 **40%**，而官方是 **33%** ❌。
	//  ⭐ 合规说明：本改动**没有照搬原厂的任何实现**，只采用"分母 60"这一个**协议事实** ✅。
	//
	//  ⭐⭐ 2026-10-18（机主新决定 · 30 系风扇基准）：⭐ **30 系分母由 3500 改为 4900 RPM** ✅
	//    ⭐ 依据：⭐ **机主实测：30 系 3500 RPM 压不住；原厂上限约 140%（4900 RPM）
	//    ⇒ 以其为 100% 基准** ✅（⭐ 40 系仍是 6000）✅
	internal const int BaolongdaFanRpmAtFullPercent = BaolongdaProfile.FanPercentDenominatorRpm;

	/// <summary>
	/// ⭐⭐ 2026-10-17（接手 · 按原厂口径修正）：⭐ 风扇百分比的**分母**（RPM ✅）。
	/// <para>依据与原委见 <see cref="BaolongdaFanRpmAtFullPercent" /> —— 原厂 40 系是 `rpm ÷ 60`，
	/// ⛔ 不是"读功能号 21 当 max"（那个读数是**手动转速设定值** ⚠️）。</para>
	/// <para>⚠️ 非宝龙达 ⇒ 返回 0（⭐ 分母由各机型自己的 duty 口径负责 ✅）。</para>
	/// </summary>
	// ⭐⭐ 2026-10-18（机主新决定 · 30 系风扇基准）：⭐ 分母**按代际** ——
	//   ⭐ **30 系 = 4900 RPM**（机主实测：3500 压不住；原厂上限约 140% ≈ 4900 ⇒ 以其为 100% 基准）✅
	//   ⭐ 40 系 = 6000 RPM（原厂 `rpm / 60f`，真机 1965 RPM ⇒ 33% 已验证）✅
	//   ⚠️ 非宝龙达恒 0（走原来的 duty 读法，逐位不变）✅
	public int FanRpmMaxForPercent => UsesBaolongda
		? BaolongdaProfile.FanPercentDenominatorRpmFor(DgpuGeneration)
		: 0;

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主 30 系反馈）：⭐ 宝龙达风扇百分比的**显示上限** = **140%** ✅
	/// <para>⭐ 依据：机主新基准下 30 系 100% = 4900 RPM；140% 只作为**显示余量**
	/// （⭐ 实测上限一般就是 100% 附近；⛔ 不会真到 140%）✅</para>
	/// <para>⚠️ 这是**显示层**的安全上限：⛔ 不影响功能号 20/21 的任何下发值
	/// （⭐ 下发档位上限见 <see cref="BaolongdaProfile.FanManualMaxUnits100Rpm" /> = **68**，⭐ 6800 RPM ✅）</para>
	/// <para>⚠️ 旧显示层 `Math.Min(100, …)` ⇒ 会少显示超过 100% 的部分 ❌</para>
	/// </summary>
	internal const int BaolongdaFanPercentDisplayMax = 140;

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 功耗墙）：⭐ **意向方法** —— "⭐ 应用一组 CPU 功耗/温度墙" ✅
	/// <para>⭐ 宝龙达协议（⭐ 功能号 23 `CpuPower` ✅）：
	/// ⭐ 写**两字节** `[子命令, 值]`：⭐ `2` = PL1(瓦) · ⭐ `3` = PL2(瓦) · ⭐ `4` = 温度墙(℃) ✅；
	/// ⭐ 写**单字节** ⭐ <c>1</c> = 开启自定义 / ⭐ <c>0</c> = 关闭 ✅（⭐ 只在 <paramref name="enable" /> = false 时用 ✅）。</para>
	/// <para>⭐⭐ 2026-10-16（开源相互印证）：⭐ **不再"最后 enable"** ✅
	/// ⭐ 依据（⭐ 两处独立印证）：⭐ ① 厂商控制台 = 直接写 `[2,值]/[3,值]/[4,值]`、**没有 enable 步骤** ✅；
	/// ⭐ ② 蛟龙开源控制台 UI = `PL1 → PL2 → 温度墙 → 最大频率 → 睿频`、**全程不调用** `SetCustomMode`
	/// （⭐ 它导出了但**没有任何页面调用** ⇒ ⭐ 死代码 ✅）。</para>
	/// <para>⭐⭐ 2026-10-16（"未设置"）：⭐ 三项都**可跳过** —— ⭐ 传 <c>≤0</c> 表示"⭐ 这一项未设置"，
	/// ⭐ 那就**不发**这个子命令（⭐ 让机器保持原样 ✅），⛔ **不拿编造值去凑满三项** ✅。</para>
	/// <para>⚠️⚠️ <b>协议限制（诚实边界）</b>：⭐ 功能号 23 ⭐ **只能写、读不回 PL1/PL2/温度墙的当前值** ⚠️
	/// ⇒ ⭐ 所以 <paramref name="confirmed" /> 的语义**只是"⭐ 已发出的子命令都写成功了"**
	/// （⭐ = WMI 没报错 ⚠️），⛔ **不是**"数值已生效"、⛔ **更不是**"机器上的值等于我们写的值" ⚠️。</para>
	/// <para>⭐ 合理性范围（⭐ 防手滑 · 依据见上面常量注释）：⭐ PL1 `[5,120]` · ⭐ PL2 `[5,150]` ·
	/// ⭐ 温度墙 `[60,105]` ✅。</para>
	/// </summary>
	internal bool TryApplyCpuPowerLimitViaBackend(bool enable, int pl1Watts, int pl2Watts, int tempWallC, out bool confirmed)
	{
		confirmed = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null && !_disposed && CpuVendor.IsIntel && !IsIntelNucMachine && !IsUmiMachine)
		{
			return TryApplyIntelMsrPowerLimit(enable, pl1Watts, pl2Watts, tempWallC, out confirmed);
		}
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
				Logger.WriteError($"宝龙达 功耗墙：⭐ 已关闭自定义 ⇒ 回读确认={confirmed}");
				return true;
			}
			// ⭐ "未设置"（≤0）⇒ ⭐ 夹取后仍是 0 ⇒ ⭐ 后面会跳过它 ✅
			int pl1 = (pl1Watts > 0) ? Math.Clamp(pl1Watts, BaolongdaPlMinWatts, BaolongdaPl1MaxWatts) : BaolongdaUnset;
			int pl2 = (pl2Watts > 0) ? Math.Clamp(pl2Watts, BaolongdaPlMinWatts, BaolongdaPl2MaxWatts) : BaolongdaUnset;
			int wall = (tempWallC > 0) ? Math.Clamp(tempWallC, BaolongdaTccMinCelsius, BaolongdaTccMaxCelsius) : BaolongdaUnset;
			if (pl1 <= BaolongdaUnset && pl2 <= BaolongdaUnset && wall <= BaolongdaUnset)
			{
				// ⭐⭐ 三项都没设过 ⇒ ⭐ **一个子命令都不发** ✅（⭐ 而不是拿编造值去填 ✅）
				//   ⚠️ 返回 true = "⭐ 已接管、⛔ 不走 GCU" ✅（⭐ 与既有约定一致 ✅）
				Logger.WriteError("宝龙达 功耗墙：⚠️ PL1／PL2／温度墙**三项都未设置**"
					+ " ⇒ ⭐ 本次**不下发任何子命令**（⛔ 不编造默认值）✅ —— ⭐ 这是如实行为、⛔ 不是失败 ✅");
				return true;
			}
			// ⭐ 顺序照蛟龙 UI：⭐ PL1 → ⭐ PL2 → ⭐ 温度墙 ✅；⛔ **不再"最后 enable"** ✅
			bool ok1 = (pl1 <= BaolongdaUnset) || wmi.TrySetCpuLongPower((byte)pl1);
			bool ok2 = (pl2 <= BaolongdaUnset) || wmi.TrySetCpuShortPower((byte)pl2);
			bool ok3 = (wall <= BaolongdaUnset) || wmi.TrySetCpuTempWall((byte)wall);
			if (!(ok1 && ok2 && ok3))
			{
				Logger.WriteError($"宝龙达 功耗墙：⚠️ 有子命令写入失败（⭐ PL1={ok1} · ⭐ PL2={ok2} · ⭐ 温度墙={ok3}）⇒ 如实报告");
			}
			// ⭐⭐ 2026-10-16（第 3 步）：⭐ **更新本地记忆** ⇒ ⭐ 这样
			//   `MechrevoService` 的校验（⭐ `_hw.Pl1 == result`）⭐ **同一次请求就能通过** ✅
			//   ⚠️ 前提：⭐ 这是在**同一个锁/线程模型**下改的属性 ⚠️
			//      ⇒ ⭐ 属性是 `{ get; private set; }`（⭐ int，⭐ 非 volatile ⚠️）
			//      ⇒ ⭐ 与 GCU 那条支的写法一致（⭐ 那里也是直接从 MQTT 回调线程写 ✅）
			//      ⇒ ⭐ 保持同款约定，⛔ 不引入新的同步机制 ✅
			//   ⭐ 只记**真正写下去且写成功**的那几项（⭐ 没写的保持"未设置" ✅）
			if (ok1 && pl1 > BaolongdaUnset)
			{
				_baolongdaMemPl1 = pl1;
				Pl1 = pl1;
			}
			if (ok2 && pl2 > BaolongdaUnset)
			{
				_baolongdaMemPl2 = pl2;
				Pl2 = pl2;
			}
			if (ok3 && wall > BaolongdaUnset)
			{
				_baolongdaMemTempWall = wall;
				TccTarget = wall;
			}
			// ⭐ "写成功"就是本方法能给出的**全部**结论 ⚠️ ⇒ ⭐ 如实命名、⛔ 不冒充"数值确认" ✅
			confirmed = ok1 && ok2 && ok3;
			// ⭐ 顺带**只读**一次"自定义开关"状态 —— ⭐ 仅作**信息**报告，
			//   ⛔ **不**当作"数值已生效"的依据 ✅（⭐ 我们不写它、⭐ 它也证明不了数值 ⚠️）
			string switchInfo = (wmi.TryReadCpuCustomEnabled(out bool nowOn) ? (nowOn ? "1" : "0") : "读不到");
			// ⭐⭐ 2026-10-16：⭐ 真机验证功耗墙的**主要证据** ⚠️ ⇒ ⭐ 必须 WriteError（⭐ 默认级别可见 ✅）
			Logger.WriteError($"宝龙达 功耗墙：⭐ 已下发（⭐ 顺序 PL1→PL2→温度墙 · ⛔ 未做 enable）"
				+ $" ⇒ ⭐ ① 写成功={confirmed}（⭐ PL1={ok1} · ⭐ PL2={ok2} · ⭐ 温度墙={ok3}）"
				+ $" · ⭐ 下发值 PL1={ShowUnset(pl1, "W")} PL2={ShowUnset(pl2, "W")} 温度墙={ShowUnset(wall, "℃")}"
				+ $" · ⭐ 自定义开关（只读、仅参考）={switchInfo}"
				+ " ⇒ ⚠️ ② 数值**是否真生效我们测不到**（⭐ 协议读不回 ⚠️）⇒ ⭐ 需目视/实测确认 ⚠️");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 功耗墙：⭐ 操作异常（已吞掉）：" + ex.Message);
			return true;
		}
	}

	/// <summary>⭐ 把"未设置"（⭐ = <see cref="BaolongdaUnset" />）如实打成「未设置」、⛔ 不打成 0 ✅。</summary>
	private static string ShowUnset(int value, string unit)
	{
		return (value > BaolongdaUnset) ? (value.ToString() + unit) : "未设置";
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 风扇手动）：⭐ **意向方法** —— "⭐ 接管风扇并设两个通道的转速" ✅
	/// <para>⭐ 宝龙达协议（⭐ 2026-10-18 校正 · 三份开源实现一致）：⭐ 功能号 20 = **按通道手动接管开关**
	/// （⭐ **写两字节** `[FanType, 0/1]` ✅）· ⭐ 功能号 21 = 该通道目标转速（⭐ 写 `[FanType, x100RPM]` ✅）。</para>
	/// <para>⚠️⚠️ <b>本方法是全部功能里【风险最高】的一个</b>：⭐ 它会**真正改动机器的散热行为** ⚠️
	/// ⇒ ⭐ 因此额外保险：
	/// ⭐ ① ⭐ **按参考实现的顺序**：⭐ **先开该通道手动开关（20=[fanType,1]）→ 再写目标（21）** ✅
	/// （⭐ 旧代码先写 21 再写 20，且 20 只写了一个字节 ⇒ 手动接管从未开启 ⚠️）；
	/// ⭐ ② ⭐ 档位夹在 ⭐ `[0, 68]`（⭐ 即 **0~6800 RPM** ✅ —— 上限 68 参考自开源蛟龙(MIT)，⭐ 实际以 13 回读为准 ⚠️）—— ⭐ 保守上限，⛔ 不让人手滑写爆 ✅；
	/// ⭐ ③ ⭐ `enable=false` 时 ⭐ **先关两通道手动开关并回读确认，再把 21 归零**（⭐ 交回固件自动控制 ✅，
	/// ⛔ 顺序反过来会在手动仍开时把风扇写到 0 ⚠️）。</para>
	/// <para>⚠️ <b>通道归属</b>：⭐ 按参考实现（蛟龙16Pro）⭐ `FanType=0` = ⭐ **大扇/GPU** ·
	/// ⭐ `FanType=1` = ⭐ **小扇/CPU** ⚠️ —— ⭐ 真机两组读数太接近，⭐ **还没交叉验证** ⚠️
	/// ⇒ ⭐ 所以本方法**要求调用方显式传两个通道**（⭐ 而不是替它猜 ✅），⭐ 且日志把两个通道都记全 ✅。</para>
	/// </summary>
	/// <summary>⭐ 软件强冷是否处于开启态（⭐ 崩溃/重启后据此恢复曲线）✅</summary>
	private static bool _fanBoostActive;

	/// <summary>⭐ 强冷前的曲线快照（关闭时原样恢复）✅</summary>
	private static BaolongdaFanCurveSettings _fanBoostSavedCurve;

	/// <summary>⭐ 曲线闭环的"上次已写档位"（x100RPM）—— 用于差异比较，避免同值反复写 ✅</summary>
	private static int _fanCurveLastCh0 = -1;

	private static int _fanCurveLastCh1 = -1;

	/// <summary>⭐ 上次真正写入的时刻（TickCount64）—— 每 30 s 强制重写一次 ✅</summary>
	private static long _fanCurveLastWriteTick;

	/// <summary>⭐ 强制重写周期（ms）⭐ 30 s ✅</summary>
	private const long FanCurveForceRewriteMs = 30000;

	// ══════════════════════════════════════════════════════════════════
	//  ⭐⭐ 2026-10-18（高优先级 · 失败熔断 + 写入节流）：
	//    ⚠️ 真机 0.291.9.14：`写 20 ⇒ 开关回读=False`，但闭环仍每 3 s 写 20/21
	//       ⇒ 对 EC/WMI 的高频**无效**写入 = 卡死高风险 ❌
	//    ⇒ 连续 3 次"写 20 后回读仍为关" ⇒ 判定本机不接受手动接管 ⇒ **停用闭环，不再写**
	//    ⇒ 任何两次写之间至少 5 s；失败时**不再写 21**（20 没接管时写 21 无意义）✅
	// ══════════════════════════════════════════════════════════════════

	/// <summary>⭐ 失败熔断：本机固件是否已被判定“不接受手动风扇接管”（⇒ 停用软件闭环，⛔ 不再写 20/21）✅</summary>
	private static bool _fanCurveUnsupported;

	/// <summary>⭐ 连续“写 20 后回读仍为关”的次数；≥ <see cref="FanCurveRejectLimit" /> ⇒ 熔断 ✅</summary>
	private static int _fanCurveManualRejectStreak;

	/// <summary>⭐ 熔断阈值：连续 3 次 ⇒ 停用软件闭环（⭐ 机主要求）✅</summary>
	private const int FanCurveRejectLimit = 3;

	/// <summary>⭐ 上次**尝试**写风扇的时刻（TickCount64）—— ⭐ 写入节流：两次写至少隔 <see cref="FanCurveMinWriteIntervalMs" /> ✅</summary>
	private static long _fanCurveLastAttemptTick;

	/// <summary>⭐ 写入最小间隔（ms）= 5000（⭐ 防止高频无效写入拖垮 EC/WMI 导致卡死 ✅）</summary>
	private const long FanCurveMinWriteIntervalMs = 5000;

	/// <summary>⭐ 供 UI（只显示层）读取：是否已被判定“本机固件不接受手动风扇接管” ✅</summary>
	internal static bool FanCurveManualUnsupported => _fanCurveUnsupported;

	// ══════════════════════════════════════════════════════════════════
	//  ⭐⭐ 2026-10-18（机主最高优先级 · §9.14「能点但没用」禁止）：
	//    ⭐ **风扇软件闭环可用性闸门** —— 默认 **false**；
	//    ⭐ 只有**只读验证成功**才置 true（且 **30 系 / 40 系分开判定**，接口可能不同）
	//    ⇒ ⭐ 未验证前：UI 隐藏入口 · 闭环不写 · 强冷不写 · 启动也不做交回写 ✅
	//  ⚠️ 验证判据（⭐ 2026-10-18 机主 0.291.9.14 卡死后的收紧版）：
	//    ⭐ **只认 EC 只读**：`0xC83C / 0xC83D / 0xB20` 三个地址都能读、且不是 0xFF 填充值
	//    （⭐ 30 系与 40 系同一判据 —— ⚠️ WMI 13/20 **只作对照打印**，⛔ 不作为开放依据：
	//       ⭐ 卡死那台 WMI 20 读得到、写入却回读 False ⇒ WMI 可读**不能证明**接管可用 ✅）
	//    ⭐ 机主回传 `--fan-probe` 报告后，再按实测决定要不要放宽判据 ✅
	// ══════════════════════════════════════════════════════════════════

	/// <summary>⭐ 30 系只读验证缓存（<c>null</c> = 还没验证过；false = 不可用；true = 可用）✅</summary>
	// ⭐⭐ 2026-10-18（机主：多自定义档各自记录风扇曲线）：⭐ 当前档位 id（默认 "default" 走旧全局存档）✅
	private static string _fanCurveModeId = "default";

	/// <summary>⭐ 当前风扇曲线档位 id（⭐ 诊断/日志只读用 ✅）</summary>
	internal static string CurrentFanCurveModeId => _fanCurveModeId;

	private static bool? _fanCurveVerifiedGen30;

	/// <summary>⭐ 40 系只读验证缓存（<c>null</c> = 还没验证过；false = 不可用；true = 可用）✅</summary>
	private static bool? _fanCurveVerifiedGen40;

	/// <summary>⭐ 最近一次只读验证的结论文字（⭐ 供 UI/日志；默认「尚未验证」）✅</summary>
	private static string _fanCurveVerifyNote = "尚未验证";

	/// <summary>⭐ 上次只读验证的时刻（TickCount64）—— ⭐ 未通过时最多每 30 s 重试一次（⛔ 不刷屏/不反复碰 EC）✅</summary>
	private static long _fanCurveVerifyTick;

	// ⭐⭐ 2026-10-18（机主：取消启动预热）：⭐ 风扇闸门只读验证改为**首次需要时懒加载一次**（进程内缓存）✅
	private static int _fanCurveLazyInitState; // 0=未开始 · 1=进行中 · 2=已完成

	private static readonly object _fanCurveLazyInitLock = new object();

	private static bool _fanCurveDeferredCleanupDone;

	/// <summary>⭐ 风扇接管只读验证结果（⭐ **仅诊断**：30/40 系已验证，UI/写入不再依赖本值）✅</summary>
	/// <para>⭐⭐ 2026-10-18（机主：默认不开）：⭐ 默认 **false**；只有 `--fan-smu-ec-test` 验证通过并写入
	/// `%APPDATA%\MechrevoLite\fan_ec_verified.cfg`（`fan_ec_verified_30=1` / `_40=1`）后，**下次启动**才放开 ✅</para>
	internal bool FanCurveVerified
	{
		get
		{
			if (!UsesBaolongda)
			{
				return false;
			}
			// **getter 只读缓存/配置文件， 不触发 EC 验证**（原实现在这里调 EnsureFanCurveLazyInit()
			if (DgpuGeneration == DgpuGenerationKind.Gen30)
			{
				return _fanCurveVerifiedGen30 ?? FanEcVerifyStore.Load(is30Series: true);
			}
			return _fanCurveVerifiedGen40 ?? FanEcVerifyStore.Load(is30Series: false);
		}
	}
	// **显式**确保风扇闸门只读验证已跑过（进程内缓存一次； 不发 WMI 写、 不写 EC）。
	internal void EnsureFanCurveReady()
	{
		EnsureFanCurveLazyInit();
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：取消启动预热）：⭐ 首次需要风扇闸门时才做**只读验证**；结果进程内缓存一次 ✅。
	/// <para>⛔ 不写 EC、不发 WMI 写；⭐ 失败也只缓存一次，不反复探测 ✅</para>
	/// </summary>
	private void EnsureFanCurveLazyInit()
	{
		if (!UsesBaolongda || Volatile.Read(ref _fanCurveLazyInitState) == 2)
		{
			return;
		}
		lock (_fanCurveLazyInitLock)
		{
			if (Volatile.Read(ref _fanCurveLazyInitState) == 2)
			{
				return;
			}
			_fanCurveLazyInitState = 1;
			try
			{
				long start = Environment.TickCount64;
				TryVerifyFanCurveReadOnly(out _);
				_fanCurveLazyInitState = 2;
				Logger.WriteLine("[init] 首次需要风扇能力 ⇒ 只读验证（耗时 " + (Environment.TickCount64 - start) + " ms，已缓存）");
			}
			catch (Exception ex)
			{
				_fanCurveLazyInitState = 2;
				Logger.WriteLine("[init] 风扇闸门懒加载异常（已吞掉，按未验证处理）：" + ex.GetType().Name + " " + ex.Message);
			}
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：取消启动预热）：⭐ 首次打开设置时执行 —— 懒加载闸门验证 ＋ 启动清理（交回固件）✅。
	/// <para>⚠️ 只在第一次调用时真正执行（进程内缓存）；⛔ 不做任何写操作除非已验证（闸门保持原判据）✅</para>
	/// </summary>
	internal void RunDeferredFanStartupCleanup()
	{
		if (!UsesBaolongda || _fanCurveDeferredCleanupDone)
		{
			return;
		}
		_fanCurveDeferredCleanupDone = true;
		long start = Environment.TickCount64;
		EnsureFanCurveLazyInit();
		// ⭐⭐ 2026-10-18（机主：删除闸门）：⭐ 不再看 cfg，直接做启动清理（交回固件）✅
		RestoreBaolongdaFanFirmwareControl();
		Logger.WriteLine("[init] 首次打开设置 ⇒ 初始化风扇闸门/清理（耗时 " + (Environment.TickCount64 - start) + " ms，已缓存）");
	}
	// <para> 只读；SMU 是否真的可用另判 SmuAvailability.IsReady（与 PerfModeCapabilities 同源）。</para>
	internal bool WujieSmuApu
	{
		get
		{
			bool flag = WujieSmuGate.IsWujieApu();
			// 2026-10-18（机主：无界这类用 SMU 的机型，没装 PawnIO 也要提示）：
			//   SMU 读写走 PawnIO ⇒ 缺驱动时是「功能静默消失」⇒ 这里显式记一次 error 便于机主看到。
			if (flag && !PawnIO.RyzenSmuService.IsPawnInstalled() && !_wujiePawnIoWarned)
			{
				_wujiePawnIoWarned = true;
				Logger.WriteError("[smu] ⛔ 无界 APU 机型但 PawnIO 驱动未安装 => SMU 自定义（PL/温度墙）不可用；"
					+ "请到「设置」或「CPU 调节」页安装 PawnIO 驱动");
			}
			return flag;
		}
	}

	/// <summary>
	/// ⭐ 星耀 AMD（XINGYAO / P916F）SMU 自定义窄判据：星耀平台 + AMD + 无 NVIDIA 独显 + SMU 就绪。
	/// <para>⚠️ 与 <see cref="WujieSmuApu" /> **互斥**：本次只把星耀用于 SMU 自定义链路，
	/// ⛔ 不改 <c>WujieSmuApu</c> 语义，也不打开无界专属的风扇 EC／EC 限充／键盘等。</para>
	/// </summary>
	internal bool XingyaoAmdSmuApu
	{
		get
		{
			WujieSmuGate.LogXingyaoPredicate();
			return WujieSmuGate.IsXingyaoSmuAllowed();
		}
	}

	/// <summary>⭐ SMU 自定义后端合并判据：无界 APU 或 星耀 AMD（仅 SMU 链路使用）。</summary>
	internal bool SmuCustomApu => WujieSmuApu || XingyaoAmdSmuApu;

	/// <summary>无界机型 PawnIO 缺失提示只记一次（避免刷屏）。</summary>
	private static bool _wujiePawnIoWarned;
	/// <summary>⭐ 最近一次只读验证结论（⭐ UI 说明行/日志用）✅</summary>
	internal static string FanCurveVerifyNote => _fanCurveVerifyNote;

	internal static bool FanBoostActive => _fanBoostActive;
	// ③ 读功能号 13 **回读确认转速真的上去了**（ 给风扇起转时间，多次采样）④ 关闭时恢复强冷前的曲线并重启闭环
	private bool TryApplyIntelMsrPowerLimit(bool enable, int pl1Watts, int pl2Watts, int tempWallC, out bool confirmed)
	{
		confirmed = false;
		if (!enable)
		{
			return true;
		}
		if (tempWallC > 0)
		{
			Logger.WriteError("[intel-msr] 温度墙暂不通过 MSR 写入，本次跳过");
		}
		bool ok = PawnIO.IntelMsrPowerLimit.TryApply(pl1Watts, pl2Watts, out bool locked, out string detail);
		confirmed = ok;
		if (ok)
		{
			if (pl1Watts > 0) Pl1 = Math.Clamp(pl1Watts, 5, 200);
			if (pl2Watts > 0) Pl2 = Math.Clamp(pl2Watts, 5, 200);
		}
		Logger.WriteError("[intel-msr] 自定义功耗 PL1=" + pl1Watts + "W PL2=" + pl2Watts + "W lock=" + locked + " ⇒ " + detail);
		return true;
	}

	internal bool TrySetFanBoostViaBackend(bool on, out bool confirmed)
	{
		confirmed = false;
		if (!UsesBaolongda)
		{
			return false;
		}
		if (on)
		{
			// ⭐⭐ 2026-10-18（回归修复 · 机主「宝龙达强冷点不了，点一下闪一下」）：
			//   懒加载改造后「点强冷」这条路径**漏了** `EnsureFanCurveReady()` ⇒ EC 通道没打开 ⇒
			//   `FanWriter` 写入直接失败（日志 `通道=None` / `读 3 次仅成功 0 次`）⇒ 乐观勾选又被回滚 = "闪一下"。
			//   ⇒ ⭐ 每次开强冷前先做一次闸门/通道就绪（进程内缓存，幂等，纯只读探测）✅
			EnsureFanCurveReady();
			// 2026-10-18（诊断包实证：本机 EC 读 0/3 成功、`通道=None`）：根因常是 **PawnIO 未安装**
			//   （EC 写入靠 PawnIO 模块；WMI 转速读取不需要它，所以"能看转速却点不了强冷"）。
			//   ⇒ 这里先探一次，缺失时如实提示用户去装，而不是只报"EC 写入失败"。
			if (!PawnIO.RyzenSmuService.IsPawnInstalled())
			{
				// 2026-10-29：不再直接放弃。EC 通道现在会在 PawnIO 缺失时回退 inpoutx64（0x62/0x66）；
				// 是否真的成功由后面的 FanWriter 写入/回读决定，失败再回滚。
				Logger.WriteError("[fan] PawnIO 驱动未安装 ⇒ 尝试 inpoutx64 EC 回退（0x62/0x66）");
			}
			// ⭐⭐ 2026-10-18（机主：30/40 系均已实测可用 ⇒ 删除闸门）：⭐ 强冷直接走 `FanWriter`（无需 cfg 验证）✅
			//   ⚠️ 仍保留：EC 写失败回滚、单次写、13 轮询确认、交回固件安全顺序 ✅
			_fanBoostSavedCurve = LoadBaolongdaFanCurve();
			_fanBoostActive = true;
			ResetBaolongdaFanCurveSmoothing();
			if (_fanBoostSavedCurve != null && _fanBoostSavedCurve.Enabled)
			{
				SaveBaolongdaFanCurve(CloneCurve(_fanBoostSavedCurve, enabled: false));
			}
			int baselineRpm = 0;
			if (TryReadFanSpeedsViaBackend(out int preFirst, out int preSecond))
			{
				baselineRpm = Math.Max(preFirst, preSecond);
			}
			int units = (DgpuGeneration == DgpuGenerationKind.Gen30)
				? BaolongdaProfile.FanTrueMaxUnits30Series
				: BaolongdaProfile.FanTrueMaxUnits40Series;
			units = Math.Clamp(units, 1, BaolongdaProfile.FanManualMaxUnits100Rpm);
			bool targetsOk = FanWriter.TrySetFanTargets(units, units, out string targetDetail);
			string maskDetail = "未写（目标写入失败）";
			bool maskOk = false;
			if (targetsOk)
			{
				maskOk = FanWriter.TrySetManualMask(true, true, out maskDetail);
			}
			// 0.293.5.1 基线：EC 目标/掩码失败即回滚交回固件，不把 0x751（NUC 寄存器）写到宝龙达。
			if (!targetsOk || !maskOk)
			{
				Logger.WriteError("[fan] 强冷：EC 目标/掩码写入失败 ⇒ 按基线回滚 · "
					+ targetDetail + " · " + maskDetail);
			}
			// ⭐⭐ 2026-10-18（机主诊断包 · 强冷已写入但确认太急）：⭐ 3900→6000 RPM 需要更久 ⇒
			//   改为**最多 10 s 轮询**（每 500 ms 读一次功能号 13，取**最大值**），满足任一条件即确认：
			//   ① 峰值 ≥ 目标×90%  ② 峰值 ≥ 原值×1.15；⛔ 轮询期间**不再写**（只读 13）✅
			bool readOk = false;
			int peakRpm = baselineRpm;
			long boostStart = Environment.TickCount64;
			if (targetsOk && maskOk)
			{
				while (Environment.TickCount64 - boostStart <= 10000)
				{
					Thread.Sleep(500);
					if (!TryReadFanSpeedsViaBackend(out int pollFirst, out int pollSecond))
					{
						continue;
					}
					readOk = true;
					peakRpm = Math.Max(peakRpm, Math.Max(pollFirst, pollSecond));
					bool targetReached = peakRpm >= units * 100 * 9 / 10;
					bool improvedEnough = baselineRpm > 0 && peakRpm >= baselineRpm * 115 / 100;
					if (targetReached || improvedEnough)
					{
						break;
					}
				}
			}
			double waitedSeconds = Math.Max(0, Environment.TickCount64 - boostStart) / 1000.0;
			bool reached = targetsOk && maskOk && readOk
				&& (peakRpm >= units * 100 * 9 / 10 || (baselineRpm > 0 && peakRpm >= baselineRpm * 115 / 100));
			confirmed = reached;
			// ⭐ 日志格式（机主要求）：`[fan] 强冷：写 20→… · 目标=6000 · 实测峰值=… RPM（等待 6.5s）⇒ 确认=True/False`
			Logger.WriteLine($"[fan] 强冷：写 EC→{units}（0xC83C/0xC83D={units} x100RPM · 0xB20=0x0A · 通道={FanWriter.ChannelName(FanWriter.PreferredChannel)}）"
				+ $" · 目标={units * 100} · 实测峰值={peakRpm} RPM（等待 {waitedSeconds:0.0}s）⇒ 确认={confirmed}"
				+ ((!targetsOk || !maskOk)
					? "（⛔ EC 未写入：通道/写入失败，见下一行回滚详情）"
					: (reached ? "" : "（已写入但转速未达预期）")));
			if (!targetsOk || !maskOk)
			{
				FanWriter.TryReleaseToFirmware(out string releaseDetail);
				_fanBoostActive = false;
				confirmed = false;
				Logger.WriteError("[fan] ⚠️ 强冷：EC 写入失败 ⇒ 已回滚（交回固件）· " + releaseDetail);
			}
			return true;
		}
		// 关闭：恢复快照（含 Enabled 状态）
		FanWriter.TryReleaseToFirmware(out string releaseOnOff);
		// 0.293.5.1 关闭路径只交回固件，不再写 WMI 20/21；
		// 这两条 WMI 写在部分宝龙达机型会卡住，导致“强冷关不掉”。
		_fanBoostActive = false;
		// ⭐ 强冷结束 ⇒ 平滑状态清零（⭐ 让恢复后的闭环从真实温度重新收敛，⛔ 不沿用强冷前的旧平滑值）
		ResetBaolongdaFanCurveSmoothing();
		BaolongdaFanCurveSettings saved = _fanBoostSavedCurve;
		_fanBoostSavedCurve = null;
		if (saved != null)
		{
			bool restored = SaveBaolongdaFanCurve(saved);
			Logger.WriteLine($"[fan] 强冷：关 ⇒ 交回固件（{releaseOnOff}）⇒ 已恢复强冷前曲线（enabled={saved.Enabled} · 联动={saved.SyncEnabled}）ok={restored}");
			confirmed = restored;
		}
		else
		{
			Logger.WriteLine("[fan] 强冷：关 ⇒ 交回固件（" + releaseOnOff + "）⇒ 没有快照可恢复（保持手动模式关闭）");
			confirmed = true;
		}
		return true;
	}

	/// <summary>⭐ 复制一份曲线设置（只改 Enabled），用作"暂停闭环/恢复"的快照 ✅</summary>
	private static BaolongdaFanCurveSettings CloneCurve(BaolongdaFanCurveSettings c, bool enabled)
	{
		return new BaolongdaFanCurveSettings
		{
			Enabled = enabled,
			FormatVersion = c.FormatVersion,
			SyncEnabled = c.SyncEnabled,
			HysteresisC = c.HysteresisC,
			IntervalMs = c.IntervalMs,
			MinRpm = c.MinRpm,
			MaxRpm = c.MaxRpm,
			Channel0Temperatures = c.Channel0Temperatures,
			Channel0Rpms = c.Channel0Rpms,
			Channel1Temperatures = c.Channel1Temperatures,
			Channel1Rpms = c.Channel1Rpms
		};
	}

	internal bool TrySetFanManualViaBackend(bool enable, int firstChannelUnits100Rpm, int secondChannelUnits100Rpm, out bool confirmed, out bool manualAccepted)
	{
		confirmed = false;
		manualAccepted = false;
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		try
		{
			// ⭐⭐ 2026-10-18（机主批准 · 最高优先级）：⛔ **不再写功能号 20/21** ——
			//   ⚠️ 真机写 `20=1` 会**卡死**（连 try/finally 都跑不完）⇒ 风扇接管改走 EC；
			//   ⭐ EC 写通路接入前，本方法**一个字节都不写**（只读 API 仍保留给调用方）✅
			Logger.WriteLineIfChanged("baolongda-fanwmi-write-disabled",
				"[fan] WMI 写风扇已禁用（功能号 20=1 在真机会卡死）⇒ 本次不写 20/21（⭐ 风扇接管将改走 EC，见 --fan-ec-test）");
			return false;
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
	//   ⭐ 范围取常数：⭐ PL1 ⭐ 5~120W · ⭐ PL2 ⭐ 5~150W · ⭐ 温度墙 ⭐ 60~105℃ ✅
	//      ⭐ 依据（⭐ 2026-10-16 开源相互印证 · 用户指导「⭐ 开源的都有方案的不是吗」）：
	//         ⭐ 蛟龙开源控制台 Server 的 `ConfigRange` —— ⭐ `CpuLongPower [5,120]` ·
	//         ⭐ `CpuShortPower [5,150]` · ⭐ `CpuTempWall [60,105]` ✅
	//      ⚠️ **不采用**它 UI 滑块的 `30~255`（⭐ 那是 byte 上限、⭐ 写 255 到 PL1 有风险 ❌）✅
	//      ⚠️ 而门面层的写入还会再夹一次（⭐ 见 `TryApplyCpuPowerLimitViaBackend` ✅）
	private const int BaolongdaPlMinWatts = BaolongdaProfile.PlMinWatts;
	private const int BaolongdaPl1MaxWatts = BaolongdaProfile.Pl1MaxWatts;
	private const int BaolongdaPl2MaxWatts = BaolongdaProfile.Pl2MaxWatts;
	private const int BaolongdaTccMinCelsius = BaolongdaProfile.TccMinCelsius;
	private const int BaolongdaTccMaxCelsius = BaolongdaProfile.TccMaxCelsius;

	// ⭐⭐ 2026-10-16（"未设置"语义 · 用户指导「⭐ 开源的都有方案的不是吗」）：⭐ **不再编造默认值** ✅
	//   ⚠️ 为什么删掉原来那组 `45/65/95`：⭐ 它是**从蛟龙两个不同层级各取一个数拼出来的**
	//      （⭐ PL1 两处都 45 · ⭐ PL2 抄 Server 的 `Default` · ⭐ 温度墙抄的是**字段默认** ⚠️）
	//      ⇒ ⭐ **没有任何一处完整支持它** ⇒ ⭐ 那就是我们自己编的数字 ❌
	//   ⭐ 而蛟龙自己两处**互相矛盾**（⭐ Server `Default` = 45/65/90 ｖｓ Client = 45/60/85；
	//      Server 字段默认 = 45/55/95 ⚠️）⇒ ⭐ 也不能直接采用其中任何一套 ✅
	//   ⭐ 采用**斗战者的做法**（⭐ 只取做法、⛔ 不抄代码 —— 它是 GPL-3.0 ✅）：
	//      ⭐ **全可空 · ⭐ 只记用户覆盖过的那几项 · ⭐ 没改过的保持机器原样 · ⛔ 绝不猜默认值** ✅
	//   ⇒ ⭐ 这恰好绕开"⭐ 功能号 23 读不回原值"这个根本难题 —— ⭐ **不预设就不需要猜** ✅
	//   ⇒ ⭐ 也完全符合铁律「⭐ 读不到就显示未知 · ⛔ 绝不编数字」✅
	//   ⭐ 实现：⭐ `_baolongdaMem*` 的 **0 = 未设置**（⭐ 沿用既有语义 ✅）：
	//      ⭐ ① ⭐ 界面对未设置的行显示「（未设置）」（⭐ 见 `CustomModeForm` ✅）
	//      ⭐ ② ⭐ 下发时**跳过**未设置的项（⭐ 见 `TryApplyCpuPowerLimitViaBackend` ✅）
	//      ⭐ ③ ⭐ 未设置时**一个子命令都不发**（⭐ 而不是拿编造值去凑满三项 ✅）
	private const int BaolongdaUnset = BaolongdaProfile.Unset;
	private int _baolongdaMemPl1;
	private int _baolongdaMemPl2;
	private int _baolongdaMemTempWall;
	// * 板号/代际缓存 = 静态判据只读一次注册表。
	private PawnIO.SmuPowerSnapshot _baolongdaSmuSnapshot;
	private DateTime _baolongdaSmuSnapshotAt = DateTime.MinValue;
	private int _baolongda30SeriesCache = -1;

	/// <summary>
	/// ⭐ 把**我们自己的**功耗墙范围与本地记忆值填进那些"原本等 GCU 上报"的属性里 ✅
	/// <para>⭐ 依据：⭐ 见方案文档第 17 节 —— ⭐ 填属性即可，⛔ 不用动 UI ✅</para>
	/// </summary>
	private void ApplyBaolongdaPowerRanges()
	{
		Pl1Minimum = BaolongdaPlMinWatts;
		Pl1Maximum = BaolongdaPl1MaxWatts;
		Pl2Minimum = BaolongdaPlMinWatts;
		Pl2Maximum = BaolongdaPl2MaxWatts;
		TccMinimum = BaolongdaTccMinCelsius;
		TccMaximum = BaolongdaTccMaxCelsius;
		// ⭐ PL4 本机协议里没有（⭐ 参考实现的功能号表里也没这一项 ✅）⇒ ⭐ 保持 -1（⭐ 即不可调 ✅）
		// ⭐⭐ 2026-10-16（"未设置"）：⭐ 当前值 = 本地记忆，**0 就是 0**（⭐ = 未设置 ✅）
		//   ⛔ **不再**填"建议默认 45/65/95" —— ⭐ 那是我自己编的（⭐ 依据见上面那段注释 ⚠️）
		//   ⭐ 界面据此显示「（未设置）」（⭐ `CustomModeForm` 读 `BaolongdaPl1Unset` 等 ✅），
		//      ⛔ 不再假装知道机器上的值 ✅ —— ⭐ 协议读不回 ⇒ ⭐ 我们**本来就不该知道** ✅
		//   ⚠️ 副作用（⭐ 如实记录）：⭐ `CustomModeForm.DeviceRange` 会把 0 钳到**下界**
		//      ⇒ ⭐ 滑块初始停在 `5W`／`60℃` ⚠️ —— ⭐ 但标题写明「（未设置）」
		//      ⇒ ⭐ 不会把那两个数当成真值 ✅（⭐ 这也正是不再编中间值的原因：⭐ 编了更像真的 ⚠️）
		Pl1 = _baolongdaMemPl1;
		Pl2 = _baolongdaMemPl2;
		TccTarget = _baolongdaMemTempWall;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（"未设置"）：⭐ PL1 是否**未设置**（⭐ = 本次运行还没下发过 ✅）
	/// —— ⭐ 供界面把标题显示成「（未设置）」✅（⭐ 铁律：⭐ 读不到就显示未知 · ⛔ 绝不编数字 ✅）。
	/// <para>⚠️ 语义**如实**：⭐ 宝龙达协议对功耗墙**只写不读** ⚠️ ⇒ ⭐ "未设置"的含义是
	/// **"本次运行我们还没下发过"**，⛔ **不是**"机器上没设过"（⭐ 机器上有没有、⭐ 是多少，⭐ 我们读不回 ⚠️）✅</para>
	/// </summary>
	public bool BaolongdaPl1Unset => UsesBaolongda && _baolongdaMemPl1 <= BaolongdaUnset;

	/// <summary>⭐ 同 <see cref="BaolongdaPl1Unset" />：⭐ PL2 是否**未设置** ✅。</summary>
	public bool BaolongdaPl2Unset => UsesBaolongda && _baolongdaMemPl2 <= BaolongdaUnset;

	/// <summary>⭐ 同 <see cref="BaolongdaPl1Unset" />：⭐ 温度墙是否**未设置** ✅。</summary>
	public bool BaolongdaTempWallUnset => UsesBaolongda && _baolongdaMemTempWall <= BaolongdaUnset;
	// ⇒ PL1/PL2/温度墙**只能走 SMU**；40 系（<c>MRID6-23</c>）有功能号 23 ⇒ 保持原 WMI 路径不变。</para>
	internal bool IsBaolongda30Series
	{
		get
		{
			if (!UsesBaolongda)
			{
				return false;
			}
			if (DgpuGeneration == DgpuGenerationKind.Gen30)
			{
				return true;
			}
			int cached = Volatile.Read(ref _baolongda30SeriesCache);
			if (cached >= 0)
			{
				return cached == 1;
			}
			// （机主：宝龙达被判成同方）： 板号统一走多来源取证（reg/WMI），判据仍是精确 MRID6
			string board = BoardWhitelist.ReadBoardProduct();
			bool match = string.Equals(board, "MRID6", StringComparison.OrdinalIgnoreCase);
			Volatile.Write(ref _baolongda30SeriesCache, match ? 1 : 0);
			return match;
		}
	}
	// （机主：「是持续功耗时间（STAPM time）」）：当前 **STAPM 时间窗（秒）**；
	internal int BaolongdaStapmTimeSeconds { get; private set; }
	/// <summary>当前 SMU slow 时间窗（秒，0 = 读不到）；30 系与无界共用，只读采样。</summary>
	internal int SmuSlowTimeSeconds { get; private set; }
	/// <summary>本机 GCU 载荷是否为 WJ（Lunar Lake 无界专用的 1.0.2.70 分支）。只读。</summary>
	internal bool UsesWjGcuPayload => string.Equals(GcuIdentity.Variant, "WJ", StringComparison.OrdinalIgnoreCase);

	/// <summary>无界：最近一次从 PM 表读到的 STAPM / fPPT 实况值（只用于日志/对比，⛔ 不回写）。</summary>
	internal int WujieLiveStapmWatts { get; private set; }

	internal int WujieLiveFastWatts { get; private set; }

	/// <summary>当前 SMU 慢速功率(sPPT)（W，0 = 读不到）；只读采样。</summary>
	internal int SmuSlowLimitWatts { get; private set; }

	/// <summary>最近一次 30 系 SMU **只读**采样（null = 还没读/读不到；仅供日志与 UI 判断，不参与下发）。</summary>
	internal PawnIO.SmuPowerSnapshot BaolongdaSmuSnapshot => _baolongdaSmuSnapshot;
	// <para> 失败方向：PawnIO 不可用 / 非 AMD / 读表失败 ⇒ 返回 false + 日志；调用方必须如实处理
	/// <summary>2026-10-21：无界（Wujie）的 SMU 只读快照刷新（时间窗见长；不动 PL/温度墙的本地记忆）。</summary>
	internal void RefreshWujieSmuSnapshot()
	{
		try
		{
			PawnIO.RyzenSmuService smu = MechrevoLite.Mode.ModeControl.SharedSmu;
			if (smu == null)
			{
				return;
			}
			PawnIO.SmuPowerSnapshot snapshot = smu.GetPowerSnapshot(writeLog: false);
			if (snapshot == null)
			{
				return;
			}
			_baolongdaSmuSnapshot = snapshot;
			_baolongdaSmuSnapshotAt = DateTime.UtcNow;
			// 2026-10-21（诊断包实证）：该固件 stapm_time/slow_time 回读是乱跳值 ⇒ 不写本地记忆（UI 行也已隐藏）
			BaolongdaStapmTimeSeconds = 0;
			SmuSlowTimeSeconds = 0;
			SmuSlowLimitWatts = 0;
			int wjStapm = (snapshot.Stapm > 0f && !float.IsNaN(snapshot.Stapm)) ? (int)Math.Round(snapshot.Stapm) : 0;
			int wjFast = (snapshot.Fast > 0f && !float.IsNaN(snapshot.Fast)) ? (int)Math.Round(snapshot.Fast) : 0;
			int wjLo = MechrevoLite.Hardware.WujieSmuGate.PlMinWatts;
			int wjHi = MechrevoLite.Hardware.WujieSmuGate.PlMaxWatts;
			// 2026-10-21：这里**不覆盖** Pl1/Pl2（那是“最后下发的意图值”，下一步下发要拿它当基线）；
			// 固件可能把 STAPM 改回默认值，覆盖会把回退值当成基线再写回去。
			// 2026-10-21：记录“外部变化”（非本进程下发）—— 用来区分是固件/GCU/模式切换把值改回去，还是我们写错。
			try
			{
				if (WujieLiveStapmWatts > 0 && wjStapm > 0 && Math.Abs(wjStapm - WujieLiveStapmWatts) > 1)
				{
					Logger.WriteError("[smu] 无界 STAPM 外部变化：" + WujieLiveStapmWatts + "W → " + wjStapm + "W（非本次下发；可能固件回退/GCU 下发/切换性能模式）");
				}
				if (WujieLiveFastWatts > 0 && wjFast > 0 && Math.Abs(wjFast - WujieLiveFastWatts) > 1)
				{
					Logger.WriteError("[smu] 无界 fPPT 外部变化：" + WujieLiveFastWatts + "W → " + wjFast + "W（非本次下发；可能固件回退/GCU 下发/切换性能模式）");
				}
			}
			catch
			{
			}
			if (wjStapm >= wjLo && wjStapm <= wjHi)
			{
				WujieLiveStapmWatts = wjStapm;
			}
			if (wjFast >= wjLo && wjFast <= wjHi)
			{
				WujieLiveFastWatts = wjFast;
			}
			Logger.WriteLine("[smu] 无界当前值（只读 PM 表）：STAPM(PL1)=" + (wjStapm > 0 ? wjStapm + "W" : "读不到")
				+ " · fast(PL2)=" + (wjFast > 0 ? wjFast + "W" : "读不到"));
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[smu] 无界时间窗刷新异常（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
		}
	}

	/// <summary>2026-10-22：读无界风扇转速（大端 16 位）；I2EC 优先，回退 ACPI-EC。</summary>
	private static bool TryReadWujieFanWord(int address, out int rpm)
	{
		rpm = -1;
		if (I2ecInpOut.IsAvailable
			&& I2ecInpOut.TryReadEcByte(address, out int high, out _)
			&& I2ecInpOut.TryReadEcByte(address + 1, out int low, out _))
		{
			rpm = ((high & 0xFF) << 8) | (low & 0xFF);
			return true;
		}
		if (EcPortIo.TryReadBytes(address, 2, out int[] values) && values.Length == 2)
		{
			rpm = ((values[0] & 0xFF) << 8) | (values[1] & 0xFF);
			return true;
		}
		return false;
	}

	/// <summary>GCU FanInfo 缺失时的只读 EC 兜底：CPU 0x0464/0x0465，GPU 0x046C/0x046B（大端）。</summary>
	internal bool TryReadGcuEcFanSpeeds(out int cpuRpm, out int gpuRpm)
	{
		cpuRpm = -1;
		gpuRpm = -1;
		if (!IsGcuServiceInstalled || UsesBaolongda || WujieSmuApu || IsIntelNucMachine || IsUmiMachine)
		{
			return false;
		}
		bool cpuOk = TryReadEcFanWord(0x0464, out cpuRpm, lowOffset: 1);
		bool gpuOk = TryReadEcFanWord(0x046C, out gpuRpm, lowOffset: -1);
		return cpuOk || gpuOk;
	}

	private static bool TryReadEcFanWord(int highAddress, out int rpm, int lowOffset)
	{
		rpm = -1;
		try
		{
			int high = -1;
			int low = -1;
			if (I2ecInpOut.IsAvailable
				&& I2ecInpOut.TryReadEcByte(highAddress, out high, out _)
				&& I2ecInpOut.TryReadEcByte(highAddress + lowOffset, out low, out _))
			{
				rpm = ((high & 0xFF) << 8) | (low & 0xFF);
				return rpm > 0 && rpm < 8000;
			}
			if (AcpiDriverEc.TryReadByte(highAddress, out byte highByte)
				&& AcpiDriverEc.TryReadByte(highAddress + lowOffset, out byte lowByte))
			{
				rpm = (highByte << 8) | lowByte;
				return rpm > 0 && rpm < 8000;
			}
			if (EcPortIo.TryReadByte(highAddress, out int highValue)
				&& EcPortIo.TryReadByte(highAddress + lowOffset, out int lowValue))
			{
				rpm = ((highValue & 0xFF) << 8) | (lowValue & 0xFF);
				return rpm > 0 && rpm < 8000;
			}
		}
		catch
		{
		}
		return false;
	}

	internal void ApplyGcuEcFanSpeeds(int cpuRpm, int gpuRpm)
	{
		if (cpuRpm >= 0) CpuFanRpm = cpuRpm;
		if (gpuRpm >= 0) GpuFanRpm = gpuRpm;
		MarkFanInfoReceived();
	}

	/// <summary>2026-10-21（无界双风扇）：从 EC 读两路风扇转速（0xC834/0xC835 × 100 RPM）。
	/// <para>只读；仅在无界机型使用，用来补 GCU FanInfo 只给一路/不给的情况。</para></summary>
	internal bool TryReadWujieEcFanSpeeds(out int fan1Rpm, out int fan2Rpm)
	{
		fan1Rpm = -1;
		fan2Rpm = -1;
		if (!WujieSmuApu)
		{
			return false;
		}
		try
		{
			// 2026-10-22（机主：LM 显示的 CPU 转速其实是副扇、GPU 读不到）：
			// 无界风扇转速改用与风扇曲线同源的 I2EC 寄存器：主扇 0x0464/0x0465（大端），副扇 0x046C/0x046D；
			// 主扇 = CPU 侧、副扇 = GPU 侧。I2EC 不可用时回退 ACPI-EC，再回退旧的 0xC834/0xC835（×100）。
			bool okMain = TryReadWujieFanWord(0x0464, out int mainRpm);
			bool okSecondary = TryReadWujieFanWord(0x046C, out int secondaryRpm);
			if (okMain)
			{
				fan1Rpm = mainRpm;
			}
			if (okSecondary)
			{
				fan2Rpm = secondaryRpm;
			}
			if (okMain || okSecondary)
			{
				return true;
			}
			bool ok1 = EcPortIo.TryReadByte(0xC834, out int byte1);
			bool ok2 = EcPortIo.TryReadByte(0xC835, out int byte2);
			if (!ok1 && !ok2)
			{
				return false;
			}
			if (ok1)
			{
				fan1Rpm = byte1 * 100;
			}
			if (ok2)
			{
				fan2Rpm = byte2 * 100;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>2026-10-21：记录刚下发的 slow 时间窗（本地记忆；回读确认由调用方另行判定）。</summary>
	internal void NoteSmuSlowTimeApplied(int seconds)
	{
		SmuSlowTimeSeconds = seconds;
		if (_baolongdaSmuSnapshot != null)
		{
			_baolongdaSmuSnapshot = _baolongdaSmuSnapshot with { SlowTimeSeconds = seconds };
		}
	}

	internal bool RefreshBaolongda30SeriesSmuPower(bool force = false, bool requireStableStapmTime = false)
	{
		if (!IsBaolongda30Series)
		{
			return false;
		}
		if (!force && _baolongdaSmuSnapshot != null && (DateTime.UtcNow - _baolongdaSmuSnapshotAt).TotalSeconds < 30.0)
		{
			return true;
		}
		try
		{
			PawnIO.RyzenSmuService smu = MechrevoLite.Mode.ModeControl.SharedSmu;
			if (smu == null)
			{
				Logger.WriteError("[smu-30] 读取当前功耗墙失败：PawnIO/SMU 不可用（30 系的 PL/温度墙只能走 SMU；需要管理员权限）");
				return false;
			}
			PawnIO.SmuPowerSnapshot snapshot = smu.GetPowerSnapshot(writeLog: false);
			if (snapshot == null)
			{
				Logger.WriteError("[smu-30] 读取当前功耗墙失败：PM 表读不到（SMU 初始化过了但表没有数据）");
				return false;
			}
			_baolongdaSmuSnapshot = snapshot;
			_baolongdaSmuSnapshotAt = DateTime.UtcNow;
			// * 下发基线/回读确认不需要它时传 false（省一次 ~0.4 s 的 PM 表读）。
			int stapmTimeSeconds = snapshot.StapmTimeSeconds ?? 0;
			if (requireStableStapmTime && stapmTimeSeconds > 0)
			{
				PawnIO.SmuPowerSnapshot second = smu.GetPowerSnapshot(writeLog: false);
				int secondTime = (second != null) ? (second.StapmTimeSeconds ?? 0) : 0;
				if (secondTime <= 0 || Math.Abs(secondTime - stapmTimeSeconds) > 1)
				{
					Logger.WriteError($"[smu] STAPM 时间窗读数不稳定（{stapmTimeSeconds}s → {secondTime}s）"
						+ " ⇒ 视为「未设置」：UI 行照建、值显示「未设置」；也不拿它做写入回读确认（⛔ 不显示不可信的值）");
					stapmTimeSeconds = 0;
				}
			}
			BaolongdaStapmTimeSeconds = stapmTimeSeconds;
		SmuSlowTimeSeconds = snapshot.SlowTimeSeconds ?? 0;
		SmuSlowLimitWatts = RoundPositive(snapshot.Slow);
			// ⭐ limits → 本地记忆（只在"值合理"时填；越界/N/A 一律保持原样，⛔ 不硬塞）
			int pl1 = RoundPositive(snapshot.Slow);
			if (pl1 >= BaolongdaPlMinWatts && pl1 <= BaolongdaPl1MaxWatts)
			{
				_baolongdaMemPl1 = pl1;
				Pl1 = pl1;
			}
			int pl2 = RoundPositive(snapshot.Fast);
			if (pl2 >= BaolongdaPlMinWatts && pl2 <= BaolongdaPl2MaxWatts)
			{
				_baolongdaMemPl2 = pl2;
				Pl2 = pl2;
			}
			int wall = RoundPositive(snapshot.TctlTemp);
			if (wall >= BaolongdaTccMinCelsius && wall <= BaolongdaTccMaxCelsius)
			{
				_baolongdaMemTempWall = wall;
				TccTarget = wall;
			}
			Logger.WriteError($"[smu-30] 当前功耗墙（只读 PM 表）：PL1(slow)={ShowUnset(_baolongdaMemPl1, "W")} "
				+ $"PL2(fast)={ShowUnset(_baolongdaMemPl2, "W")} 温度墙(tctl)={ShowUnset(_baolongdaMemTempWall, "℃")} "
				+ $"STAPM 时间窗={(BaolongdaStapmTimeSeconds > 0 ? BaolongdaStapmTimeSeconds + "s" : "读不到")} "
			+ $"slow 时间窗={(SmuSlowTimeSeconds > 0 ? SmuSlowTimeSeconds + "s" : "读不到")} "
				+ $"(PM 表 0x{snapshot.TableVersion:X6})");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteError("[smu-30] 读取当前功耗墙异常（已吞掉、按读不到处理）：" + ex.GetType().Name + " " + ex.Message);
			return false;
		}
		static int RoundPositive(float value)
		{
			if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
			{
				return 0;
			}
			return (int)Math.Round(value, MidpointRounding.AwayFromZero);
		}
	}
	// 30 系 SMU 没有"恢复默认"命令 ⇒ 至少把**本地记忆**清成"未设置"，让界面如实显示"本次运行未下发过"
	internal void ClearBaolongdaPowerLimitMemory()
	{
		_baolongdaMemPl1 = 0;
		_baolongdaMemPl2 = 0;
		_baolongdaMemTempWall = 0;
		Pl1 = 0;
		Pl2 = 0;
		TccTarget = 0;
		// ⭐ SMU 只读快照也一起作废 ⇒ 下一次 `RefreshBaolongda30SeriesSmuPower` 会重新从 PM 表读
		//   （⛔ 不拿"上次我们写的值"冒充当前值）✅
		_baolongdaSmuSnapshot = null;
		_baolongdaSmuSnapshotAt = DateTime.MinValue;
	}
	// （30 系 · SMU 功耗墙）：某几项 **SMU 写成功**后更新"本地记忆"与只读快照
	internal void NoteBaolongdaSmuLimitsApplied(int pl1Watts, int pl2Watts, int tempWallC)
	{
		if (pl1Watts > 0)
		{
			_baolongdaMemPl1 = pl1Watts;
			Pl1 = pl1Watts;
		}
		if (pl2Watts > 0)
		{
			_baolongdaMemPl2 = pl2Watts;
			Pl2 = pl2Watts;
		}
		if (tempWallC > 0)
		{
			_baolongdaMemTempWall = tempWallC;
			TccTarget = tempWallC;
		}
		if (_baolongdaSmuSnapshot != null)
		{
			PawnIO.SmuPowerSnapshot snapshot = _baolongdaSmuSnapshot;
			_baolongdaSmuSnapshot = snapshot with
			{
				Stapm = (pl1Watts > 0) ? pl1Watts : snapshot.Stapm,
				Slow = (pl1Watts > 0) ? pl1Watts : snapshot.Slow,
				Fast = (pl2Watts > 0) ? pl2Watts : snapshot.Fast,
				TctlTemp = (tempWallC > 0) ? tempWallC : snapshot.TctlTemp
			};
			_baolongdaSmuSnapshotAt = DateTime.UtcNow;
		}
	}
	// （30 系 · SMU 功耗墙）：STAPM 时间窗 **SMU 写成功**后更新本地记忆/快照（单位：秒）。
	internal void NoteBaolongdaSmuStapmTimeApplied(int seconds)
	{
		if (seconds <= 0)
		{
			return;
		}
		BaolongdaStapmTimeSeconds = seconds;
		if (_baolongdaSmuSnapshot != null)
		{
			_baolongdaSmuSnapshot = _baolongdaSmuSnapshot with { StapmTimeSeconds = seconds };
			_baolongdaSmuSnapshotAt = DateTime.UtcNow;
		}
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
	/// ⭐⭐ **风扇两通道归属** —— ⚠️ 2026-10-17 已由 owner 确认为正确 ✅
	/// <para>⭐ 确认口径（沿用参考实现 · 与现有代码配对一致）：⭐ <c>FanType = 0</c>（大扇）＝ **GPU** ·
	/// ⭐ <c>FanType = 1</c>（小扇）＝ **CPU** ✅
	/// ⇒ ⭐ 曲线通道 <c>ch0</c> 跟 <b>GPU 温度</b>、<c>ch1</c> 跟 <b>CPU 温度</b> ✅
	/// （⭐ 见 <see cref="ApplyBaolongdaFanCurveStep" /> 里 <c>channel0Rpm</c> ← <c>gpuTemp</c> /
	/// <c>channel1Rpm</c> ← <c>cpuTemp</c> 的配对 ✅）</para>
	/// <para>⚠️ <b>历史</b>：此前恒 <c>false</c> 是因为真机静止读数太接近（3054 / 3098）分不出通道，
	/// 而搞反会让 <b>CPU 风扇按 GPU 温度转</b> ⚠️ ⇒ 宁可锁死。现已确认 ⇒ 放开 ✅</para>
	/// <para>⚠️ 若日后发现风扇行为异常（例如 CPU 满载时 GPU 扇狂转），<b>第一件事</b>就是把本常量改回
	/// <c>false</c> 并复查通道归属 —— 这是唯一开关点，便于快速回滚 ✅</para>
	/// </summary>
	internal const bool FanChannelMappingConfirmed = true;

	/// <summary>
	/// ⭐ 参考实现的通道口径（⚠️ **未确认**，⭐ 等 `--fan-watch` 的结论 ✅）：
	/// ⭐ 宝龙达 ⭐ <c>FanType = 0</c> ⇒ ⭐ **大扇 / GPU** · ⭐ <c>FanType = 1</c> ⇒ ⭐ **小扇 / CPU** ⚠️
	/// </summary>
	internal const int BaolongdaFanTypeLarge = BaolongdaProfile.FanTypeLarge;

	/// <summary>⭐ 见 <see cref="BaolongdaFanTypeLarge" /> ⚠️（⭐ 未确认 ✅）。</summary>
	internal const int BaolongdaFanTypeSmall = BaolongdaProfile.FanTypeSmall;

	// ⚠️ 占空比 → `x100RPM` 需要"该通道最大转速" ⚠️
	// ⭐⭐ 2026-10-16（0.291.9.4 · 第 1 步）：⭐ 这里原来有两个「⭐ 参考 max RPM」⭐ 常量 ⚠️
	//    （⭐ BaolongdaLargeFanMaxRpm = 4400 / ⭐ BaolongdaSmallFanMaxRpm = 8200 ✅）
	//    ⚠️ 它们是**抄来的参考值**、⭐ 而且**蛟龙的这两个数没有来源** ⚠️
	//    ⇒ ⭐ ⭐ 曲线改用 **RPM** 之后 ✅（⭐ 见 FanCurveEngine.cs ✅）⭐ **不再需要 max RPM 这个分母** ✅
	//    ⇒ ⭐ ⭐ **已删除** ✅ —— ⭐ 少一个猜出来的数 ✅

	private volatile bool _baolongdaFanCurveApplied;

	// ⭐⭐ 2026-10-17（接手修复）：两个通道各一个决策器 —— 回差/去重都在这里 ✅
	//   ⚠️ 此前 `FanCurveDecider` 写好了却**没人调用** ⚠️ ⇒ 3 s 定时器每 tick 都无条件重写
	//   ⇒ 现在只在目标变化时写，另每 30 s 重申一次手动控制权（⭐ 防固件自己退回自动 ⚠️）
	private readonly FanCurveDecider _baolongdaCurveDecider0 = new FanCurveDecider();

	private readonly FanCurveDecider _baolongdaCurveDecider1 = new FanCurveDecider();

	private const int BaolongdaCurveReassertMs = 30000;

	private long _baolongdaCurveLastWriteTick;
	// （温度平滑 · 参考自 开源蛟龙(MIT) AutoFanControl 的 AlphaUp/AlphaDown 做法
	private const double BaolongdaFanCurveAlphaUp = 0.5;

	private const double BaolongdaFanCurveAlphaDown = 0.2;

	/// <summary>
	/// ⭐ 双风扇**联动系数**（0.85）✅ —— ⭐ 参考自 开源蛟龙(MIT) `AutoFanControl` 的
	/// <c>SharedHeatPipeSyncRatio</c>（⚠️ 只取这个**系数**，⛔ 未复制代码）✅
	/// <para>⭐ 语义：`max(自己, (int)(对方 × 0.85))` ⇒ ⭐ 一侧转速高时把另一侧"带起来"（共用热管）✅</para>
	/// </summary>
	private const double BaolongdaFanCurveSyncRatio = 0.85;

	/// <summary>⭐ 通道 0（`FanType 0` = GPU）的平滑温度（℃）；NaN = 未初始化 ✅。</summary>
	private static double _baolongdaFanCurveSmoothedGpuTemp = double.NaN;

	/// <summary>⭐ 通道 1（`FanType 1` = CPU）的平滑温度（℃）；NaN = 未初始化 ✅。</summary>
	private static double _baolongdaFanCurveSmoothedCpuTemp = double.NaN;

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主 30 系 · 按开源蛟龙做法加 RPM 斜坡）：⭐ 曲线闭环**已下发的档位**
	/// （x100RPM；<c>-1</c> = 未初始化）。
	/// <para>⭐ 每 tick 只允许向目标靠拢：**涨 ≤24 档 / 降 ≤4 档**（3 s tick · 800/150 RPM/s）✅</para>
	/// <para>⭐ 来源：参考自 <b>JiaolongControl (MIT)</b> 的 `MaxRampUp/DownRpmPerSec` 与
	/// `CurrentSpeedByte` 做法 —— ⭐ 仅取**数值与斜坡思路**，⛔ 未复制代码 ✅。</para>
	/// <para>⚠️ 强冷/交回固件会复位成 <c>-1</c> ⇒ ⭐ 下次曲线**首帧直接跟随目标**（⛔ 不从旧值/0 慢慢爬）✅</para>
	/// </summary>
	private static int _fanCurveRampCh0Units = -1;

	private static int _fanCurveRampCh1Units = -1;

	/// <summary>
	/// ⭐⭐ 温度**平滑**（⭐ 参考自 开源蛟龙(MIT) `AutoFanControl` 的 AlphaUp/AlphaDown 做法，⛔ 未复制代码）✅
	/// <para>⭐ 上升 α=0.5、下降 α=0.2：⭐ 升温**跟得快**（散热优先）、⭐ 降温**走得慢**（防抖、护噪音）✅</para>
	/// <para>⚠️ 温度读不到（≤0）时 ⭐ **不**把它当作"温度很低"喂给平滑器 ✅（⛔ 一次读失败就把风扇拉到底）：
	/// 已有平滑值 ⇒ 沿用；还没有 ⇒ 返回 0（由调用方按曲线第一点处理，⭐ 与改动前一致 ✅）。</para>
	/// </summary>
	private static int SmoothBaolongdaFanCurveTemp(ref double smoothedTempC, int rawTempC)
	{
		if (rawTempC <= 0)
		{
			return double.IsNaN(smoothedTempC)
				? rawTempC
				: (int)Math.Round(smoothedTempC, MidpointRounding.AwayFromZero);
		}
		if (double.IsNaN(smoothedTempC))
		{
			smoothedTempC = rawTempC;
			return rawTempC;
		}
		double alpha = (rawTempC >= smoothedTempC) ? BaolongdaFanCurveAlphaUp : BaolongdaFanCurveAlphaDown;
		smoothedTempC += alpha * (rawTempC - smoothedTempC);
		return (int)Math.Round(smoothedTempC, MidpointRounding.AwayFromZero);
	}

	/// <summary>⭐ 清零两路平滑温度 + 斜坡状态（⭐ 强冷前后 / 交回固件时用 ⇒ 恢复闭环后从真实温度/目标重新收敛 ✅）。</summary>
	private static void ResetBaolongdaFanCurveSmoothing()
	{
		_baolongdaFanCurveSmoothedGpuTemp = double.NaN;
		_baolongdaFanCurveSmoothedCpuTemp = double.NaN;
		// ⭐ 斜坡状态也归零 ⇒ 下次曲线首帧直接跟随目标（⛔ 不从强冷满转慢慢降）✅
		_fanCurveRampCh0Units = -1;
		_fanCurveRampCh1Units = -1;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主 30 系 · 按开源蛟龙做法加 RPM 斜坡）：⭐ 单通道**限速靠拢** ✅。
	/// <para>⭐ <paramref name="state" /> = 上次下发档位（x100RPM，<c>-1</c> = 未初始化）；
	/// ⭐ 返回**本次应下发档位**（⛔ 不直接返回目标 ⇒ 由调用方写下去）✅。</para>
	/// <para>⭐ 首帧（<c>-1</c>）⇒ **直接跟随目标**（⭐ 与 JiaolongControl 的 `CurrentSpeedByte &lt; 0` 处理一致，
	/// ⛔ 不从 0 慢慢爬）✅</para>
	/// </summary>
	private static int RampFanUnitsToward(ref int state, int targetUnits, int maxUpUnits, int maxDownUnits)
	{
		if (state < 0)
		{
			state = targetUnits;
			return state;
		}
		int diff = targetUnits - state;
		if (diff > 0)
		{
			state += Math.Max(0, Math.Min(diff, maxUpUnits));
		}
		else if (diff < 0)
		{
			state -= Math.Max(0, Math.Min(-diff, maxDownUnits));
		}
		return state;
	}

	/// <summary>⭐ 软件闭环当前是否已接管风扇（⭐ 用于"退出/关闭时交回固件" ✅）。</summary>
	internal bool BaolongdaFanCurveApplied => _baolongdaFanCurveApplied;

	/// <summary>
	/// ⭐ 宝龙达软件闭环曲线的门面层入口（⭐ 供 UI 读设置 ✅）。数据来源 <see cref="BaolongdaFanCurveStore" /> ✅。
	/// <para>⭐⭐ 这里把**本机满转**传进存储层 ⇒ 存档不存在时生成的默认曲线按代际校准 ✅
	/// （40 系 6000 / 30 系 4900 —— 复用 <see cref="FanRpmMaxForPercent" /> **同一条代际判据** ✅）。</para>
	/// </summary>
	internal BaolongdaFanCurveSettings LoadBaolongdaFanCurve()
	{
		return LoadBaolongdaFanCurve(_fanCurveModeId);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：多自定义档各自记录曲线）：⭐ 按**档位**读曲线
	///（`custom1..custom5` 各一份；旧全局存档作为兜底）✅。
	/// <para>⭐ 日志：<c>[fan] 曲线：读取档位=custom2（来源=档位存档/默认兜底）</c> ✅</para>
	/// </summary>
	internal BaolongdaFanCurveSettings LoadBaolongdaFanCurve(string modeId)
	{
		string id = string.IsNullOrWhiteSpace(modeId) ? "default" : modeId;
		BaolongdaFanCurveSettings settings = BaolongdaFanCurveStore.LoadForMode(id, FanRpmMaxForPercent, out bool fromModeFile);
		Logger.WriteLineIfChanged("fan-curve-mode-load-" + id,
			"[fan] 曲线：读取档位=" + id + "（来源=" + (fromModeFile ? "档位存档" : "默认兜底") + "）");
		return settings;
	}

	/// <summary>
	/// ⭐⭐ 保存软件闭环曲线（⭐ 只落盘 ＋ ⭐ **保存即应用一次** ✅）。
	/// <para>⚠️ 若 <c>Enabled</c>=false ⇒ ⭐ **同时交回固件控制** ✅
	/// （⭐ 不能"关了开关却还让风扇停在我们上次写的转速上" ⚠️）。</para>
	/// </summary>
	internal bool SaveBaolongdaFanCurve(BaolongdaFanCurveSettings settings)
	{
		return SaveBaolongdaFanCurve(_fanCurveModeId, settings);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：多自定义档各自记录曲线）：⭐ 保存到**指定档位**并立即应用（保存即应用）✅。
	/// <para>⭐ 只影响该档存档；其它档位曲线不被覆盖 ✅</para>
	/// </summary>
	internal bool SaveBaolongdaFanCurve(string modeId, BaolongdaFanCurveSettings settings)
	{
		if (settings == null)
		{
			return false;
		}
		string id = string.IsNullOrWhiteSpace(modeId) ? "default" : modeId;
		_fanCurveModeId = id;
		bool ok = BaolongdaFanCurveStore.SaveForMode(id, settings, out string path);
		Logger.WriteLine("[fan] 曲线：已保存到档位 " + id + "（path=" + path + "）");
		// ⭐ 曲线被保存（含只改开关）⇒ 决策器清零 ⇒ 下一个 tick **必定重写**
		//   （⛔ 否则会拿旧回差状态压住新曲线，用户会以为"保存了没反应" ⚠️）
		_baolongdaCurveDecider0.Reset();
		_baolongdaCurveDecider1.Reset();
		if (settings.Enabled)
		{
			// ⭐⭐ 2026-10-18（高优先级 · 失败熔断）：⭐ **用户手动重新启用** ⇒ 重置熔断，允许再试一次
			//   （⭐ 仍会重新计数：连续 3 次未生效 ⇒ 再次停用 ✅）
			if (_fanCurveUnsupported)
			{
				_fanCurveUnsupported = false;
				_fanCurveManualRejectStreak = 0;
				_fanCurveLastAttemptTick = 0;
				Logger.WriteLine("[fan] 用户重新启用软件闭环 ⇒ 已重置失败熔断（⭐ 连续 3 次手动接管未生效会再次自动停用）");
			}
		}
		if (!settings.Enabled)
		{
			RestoreBaolongdaFanFirmwareControl();
		}
		else
		{
			ApplyBaolongdaFanCurveStep();
		}
		return ok;
	}

	/// <summary>
	/// ⭐⭐ 软件闭环的**一步**：⭐ 读温度 ⇒ ⭐ **平滑** ⇒ ⭐ 查曲线 ⇒ ⭐ **双风扇 0.85 联动** ⇒ ⭐ 写功能号 21 ✅
	/// <para>⚠️⚠️ <b>未确认通道归属前，本方法【什么都不写】** ⚠️（⭐ 见
	/// <see cref="FanChannelMappingConfirmed" /> ✅）—— ⭐ 只记一行日志，⭐ 便于审查者确认闸门有效 ✅。</para>
	/// <para>⭐⭐ <b>强冷优先</b>：<c>_fanBoostActive</c> ⇒ ⭐ 在**平滑/联动之前**直接返回
	/// （⛔ 强冷是直接满转，不能被曲线逻辑改写）✅。</para>
	/// <para>⭐ 非宝龙达后端 ⇒ ⭐ 直接返回 false（⭐ GCU/同方**一行不改** ✅）。</para>
	/// </summary>
	/// <summary>
	/// ⭐⭐ 2026-10-18（机主最高优先级 · 先验证再开放）：⭐ **只读**验证风扇接管接口是否可用。
	/// <para>⛔ 本方法**不写任何字节**：WMI 只发 Get；EC 只读数（只写 SuperIO 窗口地址寄存器用于寻址）✅</para>
	/// <para>⭐ 结论写进 <paramref name="conclusion" />，并缓存进 30/40 系各自的判定位 ✅</para>
	/// </summary>
	internal bool TryVerifyFanCurveReadOnly(out string conclusion)
	{
		conclusion = "尚未验证";
		if (!UsesBaolongda)
		{
			conclusion = "非宝龙达后端 ⇒ 不适用（⛔ 不开放）";
			return false;
		}
		bool is30 = DgpuGeneration == DgpuGenerationKind.Gen30;
		string genName = is30 ? "30系" : "40系";
		// ── A. EC 只读（主管道）：0xC83C=Fan1 目标 · 0xC83D=Fan2 目标 · 0xB20=手动接管掩码 ──
		bool ecOk = false;
		int fan1Set = -1;
		int fan2Set = -1;
		int mask = -1;
		string ecDetail;
		try
		{
			bool r1 = EcLpcIo.TryReadByte(EcLpcIo.Fan1TargetAddress, out fan1Set);
			bool r2 = EcLpcIo.TryReadByte(EcLpcIo.Fan2TargetAddress, out fan2Set);
			bool r3 = EcLpcIo.TryReadByte(EcLpcIo.FanManualMaskAddress, out mask);
			// ⭐⭐ 2026-10-18（机主实测确认后的**最终判据**）：⭐ 闸门**只认身份** ——
			//   `0x2000 == 0x55` 且 `0xC411` 非 0/0xFF 即可 ✅
			//   ⚠️ **不再要求"三地址非全 0"**：机主报告 `0xC83C/0xC83D` 在固件自动控制时本来就是 0，
			//   而 `0xC834/0xC835=0x14`（2000 RPM）与 WMI 13 吻合 ⇒ 旧条件会造成**假否决** ❌
			bool identityOk = EcLpcIo.TryReadEcIdentity(out _, out _, out string idNote);
			ecOk = identityOk
				&& r1 && r2 && r3
				&& fan1Set >= 0 && fan1Set <= 0xC8
				&& fan2Set >= 0 && fan2Set <= 0xC8;
			ecDetail = $"EC 身份：{idNote} ⇒ {(identityOk ? "可访问" : "不可访问")}"
				+ $" · 0xC83C={(r1 ? fan1Set.ToString("X2") : "读不到")}"
				+ $" · 0xC83D={(r2 ? fan2Set.ToString("X2") : "读不到")}"
				+ $" · 0xB20={(r3 ? mask.ToString("X2") : "读不到")} ⇒ ok={ecOk}";
		}
		catch (Exception ex)
		{
			ecDetail = "EC 读取异常：" + ex.GetType().Name;
		}
		// ── B. WMI 只读（⚠️ 仅对照打印，⛔ 不作为开放依据）──
		string wmiDetail = "WMI 不可用";
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi != null)
		{
			try
			{
				string rpmText = wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds)
					? speeds.First + "/" + speeds.Second
					: "读不到";
				string sw20 = "读不到";
				if (wmi.TryInvokeRawGet(BaolongdaWmi.Function.MaxFanSpeedSwitch, new byte[1], out byte[] raw20) && raw20 != null)
				{
					int count = Math.Min(8, raw20.Length);
					string hex = count > 0 ? BitConverter.ToString(raw20, 0, count).Replace('-', ' ') : "空";
					sw20 = hex;
				}
				wmiDetail = $"WMI 13={rpmText} RPM · WMI20=[{sw20}]（⚠️ 仅对照，不作为开放依据）";
			}
			catch
			{
			}
		}
		// ── 判据（⭐ 2026-10-18 最终版）：⭐ 只读通过不开放；⭐ 只有 `--fan-smu-ec-test`
		//   验证通过并写入 `fan_ec_verified.cfg`（或显式 `fan_ec_verified=1`）后才放开 ✅
		//   ⚠️ WMI 可读 ≠ 接管可用（卡死那台 WMI 20 读得到、写入却回读 False）✅
		bool verified = FanEcVerifyStore.Load(is30);
		if (is30)
		{
			_fanCurveVerifiedGen30 = verified;
		}
		else
		{
			_fanCurveVerifiedGen40 = verified;
		}
		_fanCurveVerifyTick = Environment.TickCount64;
		conclusion = verified
			? $"cfg 已标记（{genName} · 仅诊断）"
			: (ecOk
				? $"EC 只读可用（{genName}）· cfg 未标记（仅诊断；UI 已直接开放）"
				: $"不可用（{genName} · EC 三地址未全部读到合理值）");
		_fanCurveVerifyNote = conclusion;
		Logger.WriteLine($"[fan] 风扇接管只读验证（诊断）：{genName} · {ecDetail} · {wmiDetail} ⇒ {conclusion}");
		Logger.WriteLine($"[fan] 风扇控制：宝龙达 ⇒ 直接开放（30/40 系已验证；cfg 仅诊断，不再作为 UI 开关条件）");
		// 2026-10-18（机主：宝龙达应强制要求安装 PawnIO）：
		//   宝龙达的风扇强冷 / 风扇曲线 / 电池充电阈值全部走 EC 写入 => 依赖 PawnIO；
		//   WMI 转速读取不需要它 => 缺失时表现为「能看转速，点强冷一闪就回滚」（真机诊断包已复现）。
		if (!PawnIO.RyzenSmuService.IsPawnInstalled())
		{
			Logger.WriteError("[fan] ⛔ 宝龙达机型但 PawnIO 驱动未安装 => 风扇强冷/曲线/电池充电阈值不可用；"
				+ "请到「CPU 调节」页安装，或重新运行安装包（宝龙达机型已强制勾选该项）");
		}
		return ecOk;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（高优先级 · 失败熔断/异常保护）：⭐ 软件闭环单步的**安全外壳** ——
	/// <para>① 已熔断（<see cref="_fanCurveUnsupported" />）⇒ ⭐ **一个字节都不写**，直接 return ✅</para>
	/// <para>② 单步内任何异常 ⇒ ⭐ 记**默认级别**错误日志 ＋ **停用闭环**（⛔ 不允许异常穿透到定时器 ⇒ 界面卡死）✅</para>
	/// </summary>
	internal bool ApplyBaolongdaFanCurveStep()
	{
		if (_fanCurveUnsupported)
		{
			Logger.WriteLineIfChanged("baolongda-fancurve-unsupported",
				"[fan] 曲线：⛔ 已熔断停用（本机固件不接受手动风扇接管）⇒ 本轮及以后不再写 20/21；"
				+ "请检查 --fan-probe 报告，或重启后在「风扇曲线」里手动重新启用再试 ✅");
			return false;
		}
		// ⭐⭐ 2026-10-18（机主：30/40 系均已实测可用 ⇒ 删除闸门）：⭐ 不再因 cfg 未验证而早退；
		//   ⚠️ 保留失败熔断/异常保护（见下）✅
		try
		{
			return ApplyBaolongdaFanCurveStepCore();
		}
		catch (Exception ex)
		{
			_fanCurveUnsupported = true;
			Logger.WriteError("[fan] ⚠️ 曲线单步异常 ⇒ 已自动停用软件闭环（防止异常穿透定时器导致界面卡死）："
				+ ex.GetType().Name + " " + ex.Message);
			return false;
		}
	}

	private bool ApplyBaolongdaFanCurveStepCore()
	{
		BaolongdaWmi wmi = _baolongda?.Wmi;
		if (wmi == null || _disposed)
		{
			return false;
		}
		// （机主要求 · 强冷核对 c）： 强冷 = "直接满转" ⇒ **必须在温度平滑/联动之前短路**
		if (_fanBoostActive)
		{
			Logger.WriteLineIfChanged("baolongda-fancurve-boost",
				"[fan] 曲线：强冷进行中 ⇒ 跳过软件闭环（⛔ 不覆盖强冷的直接满转）");
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
			// 把**本机满转**传进存储层（40 系 6000 / 30 系 4900）⇒ 新装机默认曲线按代际校准
			BaolongdaFanCurveSettings settings = LoadBaolongdaFanCurve();
			if (!settings.Enabled)
			{
				// （机主：风扇曲线不生效，且日志里**一条曲线行都没有**）
				Logger.WriteLineIfChanged("baolongda-fancurve-disabled",
					$"宝龙达风扇曲线：⭐ 未启用（Enabled=false）⇒ 本轮不写任何字节 · 曲线源={settings.Channel0Temperatures.Length}点/ch0 · 归档可读={!settings.IsUnreadableArchive}");
				return false;
			}
			Logger.WriteLineIfChanged("baolongda-fancurve-enabled",
				$"宝龙达风扇曲线：⭐ 已启用 ⇒ 开始按温度下发（ch0={settings.Channel0Temperatures.Length}点 · ch1={settings.Channel1Temperatures.Length}点 · 双风扇联动={settings.SyncEnabled}）");
			// ⭐ 温度来源：⭐ 用**已有的属性**（⭐ 它们已是"⭐ LHM/NVML 优先、⭐ 厂商兜底"⭐ 的结果 ✅）
			int cpuTemp = CpuTemp;
			int gpuTemp = GpuTemp;
			if (cpuTemp <= 0 && gpuTemp <= 0)
			{
				Logger.WriteLineIfChanged("baolongda-fancurve-notemp", "宝龙达风扇曲线：⚠️ 两个温度都没有有效值 ⇒ 本轮跳过（⛔ 不瞎写风扇 ✅）");
				return false;
			}
			// 平滑值用**整数℃**进决策器（回差/日志都是整数 ）；保留 double 状态供下一轮迭代
			int gpuTempSmooth = SmoothBaolongdaFanCurveTemp(ref _baolongdaFanCurveSmoothedGpuTemp, gpuTemp);
			int cpuTempSmooth = SmoothBaolongdaFanCurveTemp(ref _baolongdaFanCurveSmoothedCpuTemp, cpuTemp);
			// ⭐⭐ 2026-10-16（第 1 步）：⭐ 曲线改 **RPM** ✅ ⇒ ⭐ 不再有「⭐ 占空比 × ⭐ max RPM ÷ 100」这一步 ✅
			//    ⚠️ 而且 ⭐ **原来那两个 max RPM（4400/8200）是抄来的参考值** ⚠️ ⇒ ⭐ 现在整个不需要它了 ✅
			FanCurvePoint[] channel0Points = FanCurveEngine.BuildPoints(settings.Channel0Temperatures, settings.Channel0Rpms);
			FanCurvePoint[] channel1Points = FanCurveEngine.BuildPoints(settings.Channel1Temperatures, settings.Channel1Rpms);
			// ⚠️ 空曲线 ⇒ **不许写**（⛔ 绝不能把"没有曲线"当成"用最低转速"——那等于瞎转风扇）✅
			if (channel0Points.Length == 0 || channel1Points.Length == 0)
			{
				Logger.WriteLineIfChanged("baolongda-fancurve-nopoints",
					"宝龙达风扇曲线：⚠️ 至少一个通道没有可用曲线点 ⇒ ⭐ 本轮不写（⛔ 不拿最低转速顶替 ✅）");
				return false;
			}
			// ⭐⭐ 决策器（回差 3℃）：升温立刻写、降温要过回差、**目标没变就不写** ✅
			//   ⚠️ 温度来源的配对沿用改动前：gpuTemp↔ch0 · cpuTemp↔ch1 ✅
			int? decided0 = _baolongdaCurveDecider0.Decide(channel0Points, gpuTempSmooth, settings.MinRpm, settings.MaxRpm, settings.HysteresisC);
			int? decided1 = _baolongdaCurveDecider1.Decide(channel1Points, cpuTempSmooth, settings.MinRpm, settings.MaxRpm, settings.HysteresisC);
			long nowTick = Environment.TickCount64;
			bool reassertDue = (nowTick - _baolongdaCurveLastWriteTick) >= BaolongdaCurveReassertMs;
			// ⭐ 没变的那一路用决策器记着的**上次落笔值**（⛔ 不用 ResolveRpm 重算，否则绕过回差 ✅）
			//   ⚠️ 2026-10-18：这里**不再**因为"两条通路都没变"就提前 return —— ⭐ 斜坡可能还在向目标靠拢，
			//   必须等**下发档位也到目标**之后才允许跳（⭐ 否则第一 tick 之后斜坡就永远停住 ⚠️）✅
			int channel0Rpm = decided0 ?? _baolongdaCurveDecider0.LastRpm;
			int channel1Rpm = decided1 ?? _baolongdaCurveDecider1.LastRpm;
			// （双风扇 0.85 联动 · 参考自 开源蛟龙(MIT) AutoFanControl 的
			int channel0RpmBeforeSync = channel0Rpm;
			int channel1RpmBeforeSync = channel1Rpm;
			if (settings.SyncEnabled)
			{
				channel0Rpm = Math.Max(channel0RpmBeforeSync, (int)(channel1RpmBeforeSync * BaolongdaFanCurveSyncRatio));
				channel1Rpm = Math.Max(channel1RpmBeforeSync, (int)(channel0RpmBeforeSync * BaolongdaFanCurveSyncRatio));
			}
			// ⭐⭐ 2026-10-18（机主 30 系 · 按开源蛟龙放宽 + 加 RPM 斜坡）：
			//   ① 目标档上限不再按"百分比分母"夹（分母只决定 100% 在哪：40 系 6000 / 30 系 4900），而是按**协议档位上限**
			//      （68 = 6800 RPM；⭐ 实际能用多少**以功能号 13 回读为准**）✅
			//   ② 真正下发的值按**斜坡**向目标靠拢：涨 ≤800 RPM/s、降 ≤150 RPM/s（1 档 = 100 RPM）⇒
			//      3 s tick ⇒ 最多涨 24 档、最多降 4 档（取整**保守**）✅
			//   ③ 强冷**不走斜坡**（用户要立刻狂转，见 `TrySetFanBoostViaBackend`）✅
			//   ⭐ 参考自 **JiaolongControl (MIT)** 的 `MaxRampUp/DownRpmPerSec` 思路，⛔ 未复制代码 ✅
			int fullRpm = BaolongdaProfile.FanManualMaxUnits100Rpm * 100;
			channel0Rpm = Math.Clamp(channel0Rpm, 0, fullRpm);
			channel1Rpm = Math.Clamp(channel1Rpm, 0, fullRpm);
			int channel0Units = FanCurveEngine.ToUnits100Rpm(channel0Rpm);
			int channel1Units = FanCurveEngine.ToUnits100Rpm(channel1Rpm);
			// ⭐ 按本 tick 间隔换算斜坡档位增量（⭐ 向下取整 = 保守；⛔ 至少 0）✅
			int rampIntervalMs = (settings.IntervalMs > 0) ? settings.IntervalMs : LegacyRefreshIntervalMs;
			int maxUpUnits = Math.Max(0, (int)((long)BaolongdaProfile.FanCurveRampUpRpmPerSec * rampIntervalMs / 1000L / 100L));
			int maxDownUnits = Math.Max(0, (int)((long)BaolongdaProfile.FanCurveRampDownRpmPerSec * rampIntervalMs / 1000L / 100L));
			int sentUnits0 = RampFanUnitsToward(ref _fanCurveRampCh0Units, channel0Units, maxUpUnits, maxDownUnits);
			int sentUnits1 = RampFanUnitsToward(ref _fanCurveRampCh1Units, channel1Units, maxUpUnits, maxDownUnits);
			// ⭐ 日志（⭐ 仅当斜坡**限制了**本 tick 的下发值时记一行；`WriteLineIfChanged` 去重 ⛔ 不刷屏）✅
			if (sentUnits0 != channel0Units || sentUnits1 != channel1Units)
			{
				Logger.WriteLineIfChanged("baolongda-fancurve-ramp",
					$"[fan] 曲线：目标 cpu={channel1Rpm} gpu={channel0Rpm} RPM → 下发 cpu={sentUnits1 * 100} gpu={sentUnits0 * 100} RPM"
					+ $"（斜坡 +{BaolongdaProfile.FanCurveRampUpRpmPerSec}/s · -{BaolongdaProfile.FanCurveRampDownRpmPerSec}/s 限制）");
			}
			// ⚠️ 决策器没给新目标、也没到 30 s 重申、且斜坡已到位 ⇒ ⭐ 本轮**一个字节都不写**（原防抖行为）✅
			bool rampPending = (sentUnits0 != channel0Units) || (sentUnits1 != channel1Units);
			if (!decided0.HasValue && !decided1.HasValue && !reassertDue && !rampPending)
			{
				return false;
			}
			// 每 30 秒**强制重写一次**（防固件静默丢状态）； 强冷/开关变化会清空缓存 ⇒ 下一轮必写
			bool sameAsLast = _fanCurveLastCh0 == sentUnits0 && _fanCurveLastCh1 == sentUnits1;
			long curveTick = Environment.TickCount64;
			bool forceRewrite = curveTick - _fanCurveLastWriteTick >= FanCurveForceRewriteMs;
			if (sameAsLast && !forceRewrite)
			{
				// ⭐ 2026-10-18：⭐ 必须**两路都还在手动接管**才敢跳过（⭐ 任一路被固件重置 ⇒ 强制重写）✅
				bool stillCh0On = false;
				bool stillCh1On = false;
				bool manualReadOk = wmi != null && wmi.TryReadFanManualSwitches(out stillCh0On, out stillCh1On);
				if (manualReadOk && stillCh0On && stillCh1On)
				{
					Logger.WriteLineIfChanged("baolongda-fancurve-skip-same",
						$"[fan] 曲线：下发值未变（cpu={sentUnits1} gpu={sentUnits0} x100RPM）且 20 回读=1/1 ⇒ ⛔ 本轮不写");
					return true;
				}
				string lost = manualReadOk
					? $"20 回读={(stillCh0On ? "1" : "0")}/{(stillCh1On ? "1" : "0")}"
					: "20 回读=读不到";
				Logger.WriteLineIfChanged("baolongda-fancurve-manual-lost",
					$"[fan] 曲线：⚠️ {lost} ⇒ 手动接管丢了（固件重置/外部改动？）⇒ 本轮强制重写");
			}
			// ⭐⭐ 2026-10-18（高优先级 · 写入节流）：⭐ 两次写之间**至少 5 s** ——
			//   ⚠️ 斜坡/30 s 重申/开关丢失重写都可能让写入变密 ⇒ 对 EC/WMI 高频写 = 卡死高风险 ❌
			long attemptTick = Environment.TickCount64;
			if (attemptTick - _fanCurveLastAttemptTick < FanCurveMinWriteIntervalMs)
			{
				Logger.WriteLineIfChanged("baolongda-fancurve-throttle",
					$"[fan] 曲线：写入节流（间隔 < {FanCurveMinWriteIntervalMs / 1000} s）⇒ ⛔ 本轮不写");
				return false;
			}
			_fanCurveLastAttemptTick = attemptTick;
			// ⭐⭐ 2026-10-18（机主批准 · 统一门面）：⭐ 曲线写入改走 `FanWriter`（ACPI EC → LPC/SMU-EC），
			//   ⛔ 不再走 WMI 20/21（20=1 真机卡死）；⭐ 每次写后回读校验 ✅
			//   ⭐ 通道约定：`sentUnits0` = fanType 0 = GPU = 0xC83D；`sentUnits1` = fanType 1 = CPU = 0xC83C ✅
			bool targetsOk = FanWriter.TrySetFanTargets(sentUnits1, sentUnits0, out string targetDetail);
			string maskDetail = "未写（目标写入失败）";
			bool maskOk = false;
			if (targetsOk)
			{
				maskOk = FanWriter.TrySetManualMask(true, true, out maskDetail);
			}
			bool handled = targetsOk && maskOk;
			bool confirmed = handled;
			bool manualAccepted = maskOk;
			string fanWriteDetail = (targetDetail.Length > 0 ? targetDetail : "目标未写") + " · " + maskDetail;
			// ⭐⭐ 2026-10-18（高优先级 · 失败熔断）：⭐ 写/回读不确认连续 3 次 ⇒ 判定本机不可用，
			//   停用闭环（⛔ 不再写 EC 风扇寄存器），防止反复无效写入导致卡死 ✅
			if (manualAccepted)
			{
				_fanCurveManualRejectStreak = 0;
			}
			else
			{
				_fanCurveManualRejectStreak++;
				if (_fanCurveManualRejectStreak >= FanCurveRejectLimit)
				{
					_fanCurveUnsupported = true;
					Logger.WriteError("[fan] ⚠️ 风扇写入/回读未确认（连续3次）⇒ 已自动停用软件闭环"
						+ "（防止反复无效写入导致卡死）；请检查 --fan-probe / --fan-smu-ec-test 报告");
				}
			}
			if (handled)
			{
				_fanCurveLastCh0 = sentUnits0;
				_fanCurveLastCh1 = sentUnits1;
				_fanCurveLastWriteTick = Environment.TickCount64;
			}
			// ⭐ 2026-10-18（日志自证）：⭐ 写完读一次 13（实际转速，⭐ 有机械延迟）⇒ ⭐ 一眼看出"写了到底转没转" ✅
			bool rpmReadOk = false;
			int nowFirstRpm = 0;
			int nowSecondRpm = 0;
			if (handled)
			{
				Thread.Sleep(300);
				rpmReadOk = TryReadFanSpeedsViaBackend(out nowFirstRpm, out nowSecondRpm);
			}
			// （机主要求 · 日志格式 + 如实回读）
			Logger.WriteLineIfChanged("baolongda-fancurve-step",
				$"[fan] 曲线：cpu={cpuTemp}℃/gpu={gpuTemp}℃（平滑 cpu={cpuTempSmooth}/gpu={gpuTempSmooth}）"
				+ $" → rpm cpu={channel1RpmBeforeSync} gpu={channel0RpmBeforeSync}"
				+ $"（联动后 cpu={channel1Rpm} gpu={channel0Rpm}）"
				+ $" → 写 EC cpu={sentUnits1} gpu={sentUnits0}（{FanWriter.ChannelName(FanWriter.PreferredChannel)}）ok={confirmed}"
				+ $" · {fanWriteDetail}"
				+ $" · 档位 cpu={sentUnits1} gpu={sentUnits0}"
				+ (rampPending ? $"（斜坡中，目标 cpu={channel1Units} gpu={channel0Units}）" : "")
				+ $" · 13={(rpmReadOk ? nowFirstRpm + "/" + nowSecondRpm : "读不到")} RPM（有延迟）"
				+ (settings.SyncEnabled ? "" : " · 联动=关")
				+ (reassertDue && !decided0.HasValue && !decided1.HasValue ? " · 仅重申" : ""));
			if (!handled)
			{
				Logger.WriteLineIfChanged("baolongda-fancurve-write-fail",
					"[fan] 曲线：⚠️ 写入调用失败（后端不可用 / 异常被吞）⇒ 本轮未确认（⛔ 不假装成功）");
				return false;
			}
			_baolongdaCurveLastWriteTick = nowTick;
			_baolongdaFanCurveApplied = true;
			if (!confirmed)
			{
				Logger.WriteLineIfChanged("baolongda-fancurve-unconfirmed",
					$"[fan] 曲线：⚠️ 已下发但**回读未确认**（手动开关回读不一致或读不到）· 本轮值 cpu={channel1Rpm} gpu={channel0Rpm}（⛔ 不谎报成功）");
			}
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达风扇曲线：⭐ 单步异常（已吞掉，⛔ 不影响别处）：" + ex.Message);
			// ⭐⭐ 2026-10-18（高优先级 · 失败熔断）：⭐ 异常也停用闭环（⛔ 不让异常反复穿透定时器）✅
			_fanCurveUnsupported = true;
			Logger.WriteError("[fan] ⚠️ 曲线单步异常 ⇒ 已自动停用软件闭环（防止反复异常导致界面卡死）："
				+ ex.GetType().Name + " " + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// ⭐⭐ 把风扇**交回固件**控制（⭐ 关手动开关 ＝ ⭐ 两通道 20=[fanType,0]，⭐ 确认后再把 21 归零 ✅）。
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
		// ⭐⭐ 2026-10-18（机主：30/40 系均已实测可用 ⇒ 删除闸门）：⭐ 启动/关闭清理直接执行；
		//   ⭐ 交回顺序（先清掩码再清目标）与低转速保护在 `FanWriter.TryReleaseToFirmware` 内保证 ✅
		// ⭐ 交回固件后**下一次启用必须重写**（⛔ 不能因为"目标没变"就跳过首写）✅
		_baolongdaCurveDecider0.Reset();
		_baolongdaCurveDecider1.Reset();
		// 同时清零温度平滑 ⇒ 重新启用时从真实温度重新收敛（ 不在旧平滑值上慢慢爬）
		ResetBaolongdaFanCurveSmoothing();
		_baolongdaCurveLastWriteTick = 0;
		try
		{
			// ⭐⭐ 先**按通道读一次**手动开关 ⚠️ ⇒ ⭐ **只有任一路真的是"开"⭐ 才写** ✅
			//    ⭐ 理由：⭐ 这是每次启动都会走的路 ⇒ ⭐ 常见情况（⭐ 手动本就关着）⛔ **一个字节都不该写** ✅
			bool switchesReadOk = false;
			bool ch0Manual = false;
			bool ch1Manual = false;
			try
			{
				switchesReadOk = wmi.TryReadFanManualSwitches(out ch0Manual, out ch1Manual);
			}
			catch
			{
			}
			// ⭐⭐ 2026-10-18（安全优先）：⭐ 只有「**两路都读到关** ＋ ⭐ 曲线本来就是关的」才跳过。
			//   ⚠️ 曲线启用过 ⇒ ⭐ 上次可能被强杀留在手动接管/满转 ⇒ ⭐ **必须**走一次交回固件
			//   （⭐ 2 次幂等的 20=0 写入，代价可忽略；⛔ 绝不赌"读到的关一定是真关"）✅
			bool curveEnabled = false;
			try
			{
				BaolongdaFanCurveSettings savedCurve = LoadBaolongdaFanCurve();
				curveEnabled = savedCurve != null && savedCurve.Enabled;
			}
			catch
			{
			}
			if (switchesReadOk && !ch0Manual && !ch1Manual && !curveEnabled)
			{
				_baolongdaFanCurveApplied = false;
				Logger.WriteLineIfChanged("baolongda-fanrestore-skip",
					"宝龙达风扇曲线：⭐ 启动自检 ⇒ ⭐ 两通道手动接管开关**本来就是关的**（20 回读=0/0）且曲线未启用 ⇒ ⛔ 不写任何东西（⭐ 交回固件已成立 ✅）");
				return;
			}
			string detail = switchesReadOk
				? $"20 回读={(ch0Manual ? "1" : "0")}/{(ch1Manual ? "1" : "0")}"
				: "20 回读=读不到";
			Logger.WriteLine($"宝龙达风扇曲线：⚠️ 启动自检 ⇒ {detail} · 曲线启用={curveEnabled}"
				+ "（⭐ 可能是上次被强杀留下的 ⚠️）⇒ ⭐ 现在交回固件控制 ✅");
			// ⭐⭐ 2026-10-18（统一门面）：⭐ 交回固件改走 `FanWriter`（ACPI EC → LPC/SMU-EC）；
			//   ⛔ 不再写 WMI 20/21（20=1 真机卡死；写 0 也统一走 EC）✅
			bool released = FanWriter.TryReleaseToFirmware(out string releaseDetail);
			if (released)
			{
				_baolongdaFanCurveApplied = false;
				Logger.WriteLine($"宝龙达风扇曲线：⭐ 已交回固件控制（{releaseDetail} ✅）");
			}
			else
			{
				_baolongdaFanCurveApplied = false;
				Logger.WriteLine("宝龙达风扇曲线：⚠️ 交回固件未确认（通道均失败）⇒ 已记录、⛔ 不假装成功：" + releaseDetail);
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
		// ⇒ 立刻停掉 Program 里同方/GCU 的 2s 空闲计时器（若已建/已动作则恢复）
		Program.StopVendorLightingIdleMonitorForBaolongda();
		// ⭐⭐ 2026-10-16：⭐ 能力位**只认一个来源** —— <see cref="BaolongdaBackend.BuildCapabilities" /> ✅
		//    ⚠️ 起因：⭐ 我先前在两个地方各写了一份（⭐ 这里 ＋ ⭐ `BuildCapabilities`）⇒ ⭐ **必然有一天只改一处** ⚠️
		//    ⇒ ⭐ 现在统一走 `BuildCapabilities()` ✅（⭐ 与 GCU 那条支的风格一致：⭐
		//      ⭐ `RefreshDeviceCapabilities` 也是从 `MechrevoDeviceCapabilities` 一处产出 ✅）
		Capabilities = backend.BuildCapabilities();
		// ⭐⭐ 2026-10-17（WiFi / 蓝牙 · 本地开关）：⭐ 启动 Windows Radio 的后台轮询（3 s 一次）
		//   ⚠️ 只在宝龙达后端启动 ⇒ GCU 机型**不会**跑这个轮询（行为逐位不变 ✅）
		RadioSwitch.Start();
		// ⭐ 可用性变化（例如首次探测到 Radio / 被组策略禁用）⇒ 让界面重新评估这两个开关是否显示
		RadioSwitch.AvailabilityChanged = delegate
		{
			RaiseIsolated(DataChanged, "DataChanged");
		};
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
		// ⭐⭐ 2026-10-18（机主最高优先级 · 先验证再开放）：⭐ 启动时先做**只读**验证
		//   ⭐ 未通过 ⇒ UI 隐藏风扇曲线/强冷入口，闭环/强冷/启动交回**都不写**（⭐ 防"能点但没用" + 卡死 ✅）
		// ⭐⭐ 2026-10-18（机主：取消启动预热）：⭐ 启动阶段**不读 EC、不验证闸门、不做交回清理** ——
		//   改为首次打开设置 / 首次需要风扇能力时懒加载（见 EnsureFanCurveLazyInit / RunDeferredFanStartupCleanup）✅
		Logger.WriteLine("[init] 启动跳过设置预热（懒加载）：风扇闸门/EC 只读验证延后到首次需要");
		Logger.WriteLine("[fan] 风扇控制：宝龙达 ⇒ 直接开放（30/40 系已验证）");
		Logger.WriteLine($"Hardware backend: Baolongda ({backend.Wmi.InstancePath});"
			+ $" 功耗墙范围 PL1/PL2=[{Pl1Minimum},{Pl1Maximum}]W · 温度墙=[{TccMinimum},{TccMaximum}]℃（⭐ 本地记忆 PL1={Pl1} PL2={Pl2} 温度墙={TccTarget} ⚠️ 非从机器读回）");
		Logger.WriteLine($"Hardware backend: Baolongda ({backend.Wmi.InstancePath}); built-in modes only (本轮)."
			+ $" fans={backend.Wmi.FanCount} modes={backend.Wmi.ModeCount}");
		// ⭐⭐ 2026-10-17（平台声明式 · 第 1 步）：⭐ 把"声明的事实"打一行 ✅
		//   ⚠️ 纯日志：⛔ 不改变任何行为；⭐ 以后诊断包里能直接看到"这台机器按什么口径算"✅
		Logger.WriteLine("宝龙达平台事实表（声明）："
			+ $"转速百分比分母={FanRpmMaxForPercent} RPM · "
			+ $"手动转速上限={BaolongdaProfile.FanManualMaxUnits100Rpm}（x100RPM） · "
			+ $"功耗墙范围 PL1=[{BaolongdaProfile.PlMinWatts},{BaolongdaProfile.Pl1MaxWatts}]W "
			+ $"PL2=[{BaolongdaProfile.PlMinWatts},{BaolongdaProfile.Pl2MaxWatts}]W "
			+ $"温度墙=[{BaolongdaProfile.TccMinCelsius},{BaolongdaProfile.TccMaxCelsius}]℃ · "
			+ $"温度来源顺序 {BaolongdaProfile.DescribeTemperatureSources()}");
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
			// ⭐ 2026-10-18：LogoLIGHT 统一走 `SyncLogoLightQuickSwitch` —— 与"写后回灌"共用
			//   同一个镜像写入 + `[logo] 状态同步` 日志（⛔ 读不到时不动镜像 ✅）。
			if (snap.LogoLight.HasValue)
			{
				SyncLogoLightQuickSwitch(snap.LogoLight.Value != 0, "功能号15 回读确认");
			}
			// ⭐⭐ 2026-10-16（⭐ 机主反馈「⭐ 触摸板的开关反了」✅）：⭐ 功能号 12 是「⭐ 锁」语义 ⚠️
			//   （⭐ TPLock ⇒ 1 = 锁定 = 禁用 ✅），⭐ 而界面 <c>touchpad</c> 的 true 表示「⭐ 可用」✅
			//   ⇒ ⭐ **取反** ✅ —— ⚠️ 极性来自 <see cref="BaolongdaToggleInverted" />（⭐ 与写入侧**共用** ✅
			//   ⇒ ⛔ 不会出现两侧口径打架 ✅）。
			SyncBaolongdaQuickSwitch("touchpad", snap.TouchpadLock, BaolongdaToggleInverted(BaolongdaWmi.Function.TPLock));
			// ⚠️ Fn 锁（功能号 11）真机**未定论** ⚠️ ⇒ ⭐ 保持直通、⛔ 不取反 ✅（⭐ 机主只反馈了触摸板 ✅）
			SyncBaolongdaQuickSwitch("fnkey", snap.FnLock);
			// ⭐⭐ 2026-10-17（WiFi / 蓝牙 · 本地 Radio）：⭐ 宝龙达没有 GCU ⇒ 这两个状态由
			//   Windows 的 Radio 提供（与 GCU 行为一致 ✅）。
			//   ⚠️ 只同步**读到**的；读不到就不动界面（⭐ 保持"未知"比谎报好 ✅）
			if (RadioSwitch.Available(RadioSwitchKind.Wifi) && RadioSwitch.TryGet(RadioSwitchKind.Wifi, out bool wifiOn, out string wifiNote))
			{
				if (!QuickSwitches.TryGetValue("wifi", out bool wifiCur) || wifiCur != wifiOn)
				{
					QuickSwitches["wifi"] = wifiOn;
					Logger.WriteLine($"宝龙达状态回读：⭐ wifi ⇒ {(wifiOn ? "开" : "关")}（⭐ 来源：Windows Radio · {wifiNote} ✅）");
				}
			}
			if (RadioSwitch.Available(RadioSwitchKind.Bluetooth) && RadioSwitch.TryGet(RadioSwitchKind.Bluetooth, out bool btOn, out string btNote))
			{
				if (!QuickSwitches.TryGetValue("bt", out bool btCur) || btCur != btOn)
				{
					QuickSwitches["bt"] = btOn;
					Logger.WriteLine($"宝龙达状态回读：⭐ bt ⇒ {(btOn ? "开" : "关")}（⭐ 来源：Windows Radio · {btNote} ✅）");
				}
			}
			// ⭐⭐ 2026-10-17（「高性能电源」开关 · 无 GCU）：⭐ 状态来自 LM 自己的开关
			//   （AppConfig bd_power_overlay）——原来只有 GCU 那条 `HighPerformancePowerModeSwitch` 回包会写它
			//   ⇒ 宝龙达上永远读不到 ⇒ 开关看起来"点不动" ✅
			{
				bool hiOn = MechrevoLite.Mode.PerfModeService.Instance?.WindowsPowerOverlayEnabled ?? true;
				if (!QuickSwitches.TryGetValue("highperf", out bool hiCur) || hiCur != hiOn)
				{
					QuickSwitches["highperf"] = hiOn;
					Logger.WriteLine($"宝龙达状态回读：⭐ highperf ⇒ {(hiOn ? "开" : "关")}（⭐ 来源：LM 配置 bd_power_overlay）");
				}
			}
			// （APU 防缩缸）： 状态直接读 Windows 策略（boost mode == 5）
			{
				bool apuOn = MechrevoLite.Mode.PerfModeService.Instance?.ApuAntiDegradeEnabled ?? false;
				if (!QuickSwitches.TryGetValue("apuboost", out bool apuCur) || apuCur != apuOn)
				{
					QuickSwitches["apuboost"] = apuOn;
					Logger.WriteLine($"宝龙达状态回读：⭐ apuboost ⇒ {(apuOn ? "开" : "关")}（⭐ 来源：Windows 电源策略）");
				}
			}
			// ⭐⭐ 2026-10-18（摄像头开关 · 系统级 PnP 禁用/启用 · 机主已批准）：
			//   ⭐ 状态源改成**设备状态**（`CM_Get_DevNode_Status` / `ConfigFlags` —— 见 `CameraPnpControl`），
			//      ⛔ 不再是隐私授权回显（隐私 Allow ≠ 设备没被禁用 ⚠️）；
			//   ⭐ 只有"本机枚举不到可控制的摄像头设备"时才退回旧的隐私键兜底（保功能、⛔ 不谎报来源）✅
			//   ⚠️ 非宝龙达不走本方法 ⇒ GCU 机型仍由 `WebCamEnable` 回包写 `QuickSwitches`（逐位不变 ✅）
			if (CameraPnpControl.TryGetEnabled(out bool camEnabled, out string camPnpNote))
			{
				if (!QuickSwitches.TryGetValue("webcam", out bool camCur) || camCur != camEnabled)
				{
					QuickSwitches["webcam"] = camEnabled;
					Logger.WriteLine($"宝龙达状态回读：⭐ webcam ⇒ {(camEnabled ? "开" : "关")}"
						+ $"（⭐ 来源：Windows 摄像头设备状态（PnP）· {camPnpNote} ✅）");
				}
			}
			else if (CameraPrivacy.TryGetDenied(out bool camDenied, out string camPrivacyNote))
			{
				bool camOn = !camDenied;
				if (!QuickSwitches.TryGetValue("webcam", out bool camCur2) || camCur2 != camOn)
				{
					QuickSwitches["webcam"] = camOn;
					Logger.WriteLine($"宝龙达状态回读：⭐ webcam ⇒ {(camOn ? "开" : "关")}"
						+ $"（⭐ 无 PnP 摄像头设备 ⇒ 兜底来源：Windows 摄像头隐私 · {camPrivacyNote} ⚠️）");
				}
			}
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

	/// <summary>
	/// ⭐⭐ 2026-10-18（摄像头开关 · 系统级 PnP 禁用/启用 · 机主已批准）：⭐ 把**摄像头设备状态**回灌
	/// <see cref="QuickSwitches" />（⭐ 唯一入口，⛔ 与厂商回显区分开）。
	/// <para>⚠️ 根因：宝龙达没有 GCU/摄像头功能号 ⇒ 原来的 `WEBCAM_ON/OFF` 发不出去；
	/// 且 `QuickSwitches["webcam"]` 原来只由"摄像头隐私授权"回读 ⇒ **隐私 Allow ≠ 设备没被禁用** ⚠️
	/// ⇒ 设备被 PnP 禁用后界面仍显示"开"❌。</para>
	/// <para>⭐ 只在**值真的变了**时才写＋记日志 ✅；<paramref name="source" /> 必须如实写清来源
	/// （PnP 写入确认 ／ PnP 设备状态回读），⛔ 不拿意图当状态 ✅。</para>
	/// </summary>
	internal void SyncCameraPnpQuickSwitch(bool on, string source)
	{
		if (!QuickSwitches.TryGetValue("webcam", out bool current) || current != on)
		{
			QuickSwitches["webcam"] = on;
			Logger.WriteLine($"[camera] 状态同步 ⇒ {(on ? "On" : "Off")}（来源：{source}）");
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
				// ⭐⭐ 2026-10-27（task-41）：路由声明 / 实际后端 / 机型事实**一处对齐**并留痕。
				//   只读（复用 Probe 里已经采集的 MachineIdentity，不重读 WMI/注册表），不改任何控制流；
				//   非宝龙达路径也打这一行（原来只有 GCU 兜底分支有 backend 日志）。
				LogMachineRouteGate(kind);
				// ⭐⭐ 2026-10-16（宝龙达适配）：宝龙达模具（⭐ 蛟龙16Pro 等）—— ⭐ 必须**排在老接口之前**判，
				// 因为两者协议同族、老接口的 GUID 校验在宝龙达机上**也会通过**（⭐ 真机实测 GUID 完全一致 ⚠️）
				// ⇒ ⭐ 若先判老接口，宝龙达后端**永远轮不到**，我们新接的风扇/温度/灯效就都用不上 ✅
				// ⚠️ 不会误伤同方（⭐ **真正的保证是 `BaolongdaBackend.TryCreate` 里的【机型白名单】硬闸门** ✅）：
				//    ⭐ 探到 `MICommonInterface` 后还会**先查板号白名单**（`MRID6` 精确 / `MRID6-` 前缀 ✅），
				//    ⭐ 不在白名单 ⇒ ⭐ 立即弃掉 ⇒ ⭐ 回落 `<see cref="MechrevoBackendKind.LegacyMifs" />` ✅
				// ⚠️⚠️ 2026-10-17 更正：旧说法「`BaolongdaWmi.TryCreate` 要求**真读一次 CPU 温度**才算活体」
				//    ⭐ **已不再成立** ⚠️ —— 该判据过严，导致 **30 系宝龙达（`MRID6`·6800H+3070Ti）读不到功能号 22
				//    就整机退回 legacy**（性能模式/FN 锁/灯光/触摸板/校色 全部失效 ❌）。
				//    ⭐ 现在**探测与能力位解耦**：⭐ 白名单命中 ⇒ 必接管（功能号 22 只记日志 ✅）；
				//    ⭐ 白名单未命中 ⇒ 多判据任一成功（⭐ 见 `BaolongdaWmi.ProbeLiveness` ✅）。
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
				// （机主：宝龙达专属周期任务不能在 GCU 机器上空转）：启动只打一行"跳过 N 项"
				LogNonBaolongdaPeriodicGateOnce("Gcu");
				// ⭐ 2026-10-15：**两条分支都要留痕**（原来只有老接口那条有日志）——诊断「GCU 连不上」时必须能
				// 区分"被误判成老机型"与"确实走的是 GCU 通路、连不上另有原因"。
				// 注：级别过滤下（默认 error）本行不进日志，用户可见的信号是顶部状态：非 GCU 后端显示「● 已就绪」。
				Logger.WriteLine($"Hardware backend: GCU (MQTT). IsGcuServiceInstalled={IsGcuServiceInstalled}, legacyProbe={kind}");
			}
			// NUC/UMI 直接走本地 ACPI/EC：不再尝试连接 GCU/MQTT。
			if (IsIntelNucMachine || IsUmiMachine)
			{
				Logger.WriteLine("[backend] NUC/UMI 本地 ACPI 模式：跳过 GCU/MQTT");
				MarkNucLocalReady();
				return true;
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
	/// <summary>
	/// ⭐⭐ 2026-10-27（task-41）：<c>[gate] 机型分类</c> —— 一行同时给出「机型事实 + 路由声明 + 实际后端 + 是否一致」。
	/// <para>事实来源**必须是** <see cref="BackendRouter.LastDecision" /> 里的那一个 <see cref="MachineIdentity" />
	/// （与路由判定/诊断包共用同一份实例，设计文档 §3.4）；只有路由未运行（如 UI 审计注入通路）时才现场补采。</para>
	/// <para>⚠️ 只写日志、⛔ 不改变任何后端选择或硬件下发；一致 = Info（默认级别不落盘，诊断包可见），
	/// 不一致 = <c>WriteError</c>（默认级别也可见）。</para>
	/// </summary>
	private static void LogMachineRouteGate(MechrevoBackendKind actual)
	{
		try
		{
			BackendRouteDecision decision = BackendRouter.LastDecision;
			MachineIdentity identity;
			string declared;
			if (decision != null)
			{
				identity = decision.Identity;
				declared = decision.Backend + "(rule=#" + decision.RuleIndex + " " + decision.Rule + " · decl=" + (decision.DeclarationPassed ? "pass" : "fail") + ")";
			}
			else
			{
				identity = MachineIdentity.Collect();
				declared = "(路由未运行)";
			}
			BackendRoute declaredRoute = decision == null ? BackendRoute.None : BackendRouter.ToRoute(decision.Backend);
			BackendRoute actualRoute = BackendRouter.ToRoute(actual);
			bool consistent = declaredRoute == actualRoute;
			string message = "[gate] 机型分类：" + identity.Summary
				+ " ⇒ declared=" + declared
				+ " · actual=" + actual
				+ " · 一致=" + consistent;
			Logger.WriteLineIfChanged("machine-route-gate", message);
			if (!consistent)
			{
				Logger.WriteError("[强化机型分流] ⚠️ [gate] 机型分类：路由声明与实际后端不一致 ⇒ 已按实际后端运行（诊断包会记录）：" + message);
			}
		}
		catch (Exception ex)
		{
			// 日志失败绝不影响启动（与启动诊断既有约定一致）。
			Logger.WriteLine("[gate] 机型分类日志失败（已吞掉，不影响启动）：" + ex.GetType().Name + " " + ex.Message);
		}
	}

	// **周期任务平台门控**的启动留痕 —— 非宝龙达机器只打**一行**（去重），说明跳过了哪些宝龙达周期任务 。
	private static void LogNonBaolongdaPeriodicGateOnce(string backend)
	{
		string msg = "[gate] 非宝龙达（backend=" + backend + "）⇒ 跳过 6 项宝龙达周期任务"
			+ "（传感器回读 / 开关状态回读 / 风扇闸门只读验证 / 软件闭环+强冷轮询 / 灯光自动关闭计时器 / 软件彩虹刷色）";
		if (Logger.TryMarkChanged("non-baolongda-periodic-gate", msg))
		{
			Logger.WriteError(msg);
		}
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
			case "Setting/Control":
				OnSettingControl(o);
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
			// 2026-10-21（无界键盘灯取证）：键盘/灯光状态只读诊断 —— 每个主题一行（type/solution/effect/light/brightness/power）。
			if ((topic == "Keyboard/Status" || topic == "Setting/Status") && Capabilities != null)
			{
				if (Logger.TryMarkChanged("kbd-diag-" + topic, "type=" + Capabilities.KeyboardType + "|sol=" + KeyboardSolution
					+ "|eff=" + KeyboardEffect + "|light=" + KeyboardLight + "|bri=" + KeyboardBrightness + "|pwr=" + KeyboardPower))
				{
					Logger.WriteError("[kbd] 状态诊断 " + topic + "：KeyboardType=" + Capabilities.KeyboardType
						+ " · solution=" + KeyboardSolution + " · effect=" + KeyboardEffect + " · light=" + KeyboardLight
						+ " · brightness=" + KeyboardBrightness + " · power=" + KeyboardPower);
				}
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

	/// <summary>2026-10-22：GCU/控制台的键盘自动关闭时间（分钟，-1=还没收到）。</summary>
	internal int KeyboardAutoOffMinutes { get; private set; } = -1;

	/// <summary>2026-10-21：Customize/Info 上报的键盘类型（-1=还没收到）。</summary>
	internal int CustomizeKeyboardType { get; private set; } = -1;

	/// <summary>2026-10-21：Customize/Info 上报的灯带类型（0=没有灯带）。</summary>
	internal int CustomizeLightbarType { get; private set; } = -1;

	private void OnCustomizeInfo(JObject o)
	{
		// 2026-10-21（无界键盘灯取证）：GCU 的 Customize/Info 带的 KeyboardType/LightbarType 是机型能力位，
		// Setting/Status 里没有这两个键 ⇒ 旧代码 KeyboardType 恒 0（UI 误落单键 RGB 兜底）。
		try
		{
			string kbTypeText = o.GetValue("KeyboardType", StringComparison.OrdinalIgnoreCase)?.ToString();
			if (!string.IsNullOrWhiteSpace(kbTypeText) && int.TryParse(kbTypeText, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out int kbType) && kbType >= 0)
			{
				if (CustomizeKeyboardType != kbType)
				{
					CustomizeKeyboardType = kbType;
					Logger.WriteLine("[kbd] Customize/Info: KeyboardType=" + kbType);
				}
			}
			string lbTypeText = o.GetValue("LightbarType", StringComparison.OrdinalIgnoreCase)?.ToString();
			if (!string.IsNullOrWhiteSpace(lbTypeText) && int.TryParse(lbTypeText, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out int lbType) && lbType >= 0)
			{
				CustomizeLightbarType = lbType;
			}
		}
		catch (Exception exKbInfo)
		{
			Logger.WriteLine("[kbd] Customize/Info 键盘字段解析失败（已吞掉）：" + exKbInfo.Message);
		}
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

	/// <summary>2026-10-22：解析 <c>Setting/Control</c> —— 目前只取键盘自动关闭时间
	///（<c>{"Action":"KEYBOARD_LIGHTBAR_TIMER_ON","Mins":N}</c>，GCU 侧是软件计时器，不写 EC）。</summary>
	private void OnSettingControl(JObject o)
	{
		try
		{
			string action = o["Action"]?.ToString();
			if (!string.Equals(action, "KEYBOARD_LIGHTBAR_TIMER_ON", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			JToken minsToken = o["Mins"];
			if (minsToken == null || minsToken.Type != JTokenType.Integer)
			{
				return;
			}
			int mins = (int)minsToken;
			if (KeyboardAutoOffMinutes == mins)
			{
				return;
			}
			KeyboardAutoOffMinutes = mins;
			Logger.WriteError("[kbd] 键盘自动关闭时间 = " + mins + " 分钟（来自 GCU 设置；GCU 软件计时器，不写 EC）");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[kbd] Setting/Control 解析失败（已吞掉）：" + ex.Message);
		}
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
			// （单源状态机）：宝龙达 30/40 系的电源镜像**只允许**由亮度回读/用户显式开关写入；
			if (!(UsesBaolongda && (DgpuGeneration == DgpuGenerationKind.Gen30 || DgpuGeneration == DgpuGenerationKind.Gen40)))
			{
				bool gcuKeyboardOn = text6 == "On";
				TraceKeyboardPowerWrite(gcuKeyboardOn, "GCU payload 解析(powerStatus)");
				KeyboardPower = gcuKeyboardOn;
			}
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
		// 表数据不可信（温度不单调/超范围/点数不足）⇒ 保留上一份可用曲线，避免界面出现乱码轴
		if (!CurveLooksSane(item, item2))
		{
			Logger.WriteLineIfChanged("fan-table-cpu-sane", "[fan] Fan/Table [" + CurveName + "] CPU 表不可信 ⇒ 保留上一份曲线");
			item = (byte[])CpuCurveUpT.Clone();
			item2 = (byte[])CpuCurveDuty.Clone();
		}
		if (!CurveLooksSane(item3, item4))
		{
			Logger.WriteLineIfChanged("fan-table-gpu-sane", "[fan] Fan/Table [" + CurveName + "] GPU 表不可信 ⇒ 保留上一份曲线");
			item3 = (byte[])GpuCurveUpT.Clone();
			item4 = (byte[])GpuCurveDuty.Clone();
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
			// （解耦 GCU）： 状态一律以 LM 自己的开关为准（AppConfig bd_power_overlay）
			bool hiOn = MechrevoLite.Mode.PerfModeService.Instance?.WindowsPowerOverlayEnabled ?? true;
			QuickSwitches["highperf"] = hiOn;
			HighPerformanceSeen = true;
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
		// 2026-10-21（机主：怀疑是 GCU 把耗改回去的）：把 GCU 上报的功耗变化显式记出来。
		//   ⚠️ 只记“变化”，默认级别可见；不写任何东西。
		try
		{
			if ((UsesWjGcuPayload || WujieSmuApu) && num3 > 0 && num4 > 0
				&& (num3 != _gcuReportedPl1 || num4 != _gcuReportedPl2))
			{
				Logger.WriteError("[gcu] 上报功耗变化：CPU_PL1 " + (_gcuReportedPl1 > 0 ? _gcuReportedPl1 + "W" : "(首见)")
					+ " → " + num3 + "W · CPU_PL2 " + (_gcuReportedPl2 > 0 ? _gcuReportedPl2 + "W" : "(首见)")
					+ " → " + num4 + "W（OP=" + num + " · 本项来自 GCU 上报，不是 LM 下发）");
				_gcuReportedPl1 = num3;
				_gcuReportedPl2 = num4;
			}
		}
		catch (Exception exGcuPl)
		{
			Logger.WriteLine("[gcu] 上报功耗日志失败（已吞掉）：" + exGcuPl.Message);
		}
		TccOffset = Int(o, "CPU_TccOffset");
		if (HasField(o, "CPU_TccOffset") || HasField(o, "CPU_TccOffsetMinimum") || HasField(o, "CPU_TccOffsetMaximum") || HasField(o, "CPU_TccOffsetSwitch") || HasField(o, "CPU_AmdTccTarget"))
		{
			TccStatusSeen = true;
		}
		Pl1Minimum = OptionalInt(o, Pl1Minimum, "CPU_AmdSPLMinimum", "CPU_AmdSPLMin", "CPU_AmdSPL_Minimum", "CPU_AmdSPL_Min", "CPU_PL1Minimum");
		Pl1Maximum = OptionalInt(o, Pl1Maximum, "CPU_AmdSPLMaximum", "CPU_AmdSPLMax", "CPU_AmdSPL_Maximum", "CPU_AmdSPL_Max", "CPU_PL1Maximum");
		Pl2Minimum = OptionalInt(o, Pl2Minimum, "CPU_AmdSPPTMinimum", "CPU_AmdSPPTMin", "CPU_AmdSPPT_Minimum", "CPU_AmdSPPT_Min", "CPU_PL2Minimum");
		Pl2Maximum = OptionalInt(o, Pl2Maximum, "CPU_AmdSPPTMaximum", "CPU_AmdSPPTMax", "CPU_AmdSPPT_Maximum", "CPU_AmdSPPT_Max", "CPU_PL2Maximum");

		// 部分 Intel + GCU 机型只在 Status 里给当前 PL 值，不给上下限（ItemSupport 仍声明 CPU 性能菜单支持）
		// ⇒ 用当前值兜出可调范围，避免自定义页 PL1/PL2 行被当成不可用而隐藏/禁灰。
		if (IsGcuConnected && !UsesAmdPowerFields && Capabilities.CpuPerformanceTuning)
		{
			if (Pl1 > 0 && !(Pl1Minimum >= 0 && Pl1Maximum > Pl1Minimum))
			{
				Pl1Minimum = 5;
				Pl1Maximum = Math.Max(200, Pl1);
				Logger.WriteLine("[caps] Intel GCU 未上报 PL1 范围 ⇒ 按当前值回退 5.." + Pl1Maximum + "W（当前=" + Pl1 + "）");
			}
			if (Pl2 > 0 && !(Pl2Minimum >= 0 && Pl2Maximum > Pl2Minimum))
			{
				Pl2Minimum = 5;
				Pl2Maximum = Math.Max(200, Pl2);
				Logger.WriteLine("[caps] Intel GCU 未上报 PL2 范围 ⇒ 按当前值回退 5.." + Pl2Maximum + "W（当前=" + Pl2 + "）");
			}
			if (Pl4 > 0 && !(Pl4Minimum >= 0 && Pl4Maximum > Pl4Minimum))
			{
				Pl4Minimum = 5;
				Pl4Maximum = Math.Max(250, Pl4);
				Logger.WriteLine("[caps] Intel GCU 未上报 PL4 范围 ⇒ 按当前值回退 5.." + Pl4Maximum + "W（当前=" + Pl4 + "）");
			}
		}

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
			TccSwitch = text3 == "1" || string.Equals(text3, "true", StringComparison.OrdinalIgnoreCase);
		}
		string text4 = o["TjMax"]?.ToString();
		if (text4 != null && int.TryParse(text4, out var result2) && result2 > 0)
		{
			TjMax = result2;
		}
			// GCU 未上报 TjMax 时，Intel 直接读 MSR 0x1A2（TjMax 逐 CPU 不同，不能写死 100/105）
			if (TjMax <= 0 && CpuVendor.IsIntel)
			{
				int msrTjMax = PawnIO.IntelMsrTjMax.TryRead();
				if (msrTjMax > 0)
				{
					TjMax = msrTjMax;
					Logger.WriteLine("[gcu] TjMax 取 MSR 0x1A2 = " + msrTjMax + "℃");
				}
			}
		// WJ 载荷（Lunar Lake 无界）：Intel 控制字段探测日志（只读，每个不同组合只记一次）
		if (UsesWjGcuPayload || WujieSmuApu)
		{
			try
			{
				string wjxyFields = "variant=" + (GcuIdentity.Variant.Length > 0 ? GcuIdentity.Variant : "(空)")
					+ " · cpu=" + (CpuVendor.IsIntel ? "Intel" : (CpuVendor.IsAmd ? "AMD" : "未知"))
					+ " · CPU_PL1=" + (HasField(o, "CPU_PL1") ? o["CPU_PL1"]?.ToString() : "无")
					+ " · CPU_PL2=" + (HasField(o, "CPU_PL2") ? o["CPU_PL2"]?.ToString() : "无")
					+ " · CPU_TccOffset=" + (HasField(o, "CPU_TccOffset") ? o["CPU_TccOffset"]?.ToString() : "无")
					+ " · CPU_TccOffsetSwitch=" + (HasField(o, "CPU_TccOffsetSwitch") ? o["CPU_TccOffsetSwitch"]?.ToString() : "无")
					+ " · CPU_TccOffsetMaximum=" + (HasField(o, "CPU_TccOffsetMaximum") ? o["CPU_TccOffsetMaximum"]?.ToString() : "无")
					+ " · TjMax=" + (HasField(o, "TjMax") ? o["TjMax"]?.ToString() : "无")
					+ " · CPU_AmdSPL=" + (HasField(o, "CPU_AmdSPL") ? o["CPU_AmdSPL"]?.ToString() : "无")
					+ " · IsAMDPlatform=" + (HasField(o, "IsAMDPlatform") ? o["IsAMDPlatform"]?.ToString() : "无")
					+ " ⇒ 计算值 Pl1=" + Pl1 + " Pl2=" + Pl2 + " TjMax(本地)=" + TjMax
					+ " TccRaw=" + TccRawMinimum + ".." + TccRawMaximum;
				if (Logger.TryMarkChanged("wjxy-intel-fields", wjxyFields))
				{
					Logger.WriteError("[gcu] WJ/LNL Intel 控制字段：" + wjxyFields);
				}
			}
			catch (Exception exWjxy)
			{
				Logger.WriteLine("[gcu] WJ Intel 字段日志失败（已吞掉）：" + exWjxy.Message);
			}
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
		if (IsAmdPlatformForLimits && num7 >= 0)
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
		// 2026-10-21：Intel 的 TCC 字段是“偏移”范围：给出 CPU_TccOffsetMaximum=10 ⇒ 温度墙 95..105
		// （之前把缺失的 minimum 当 -1，导致 max 被算成 95、范围压平 95..95 ⇒ 判不可调、整行隐藏）
		int tjBase = (num8 > 0) ? num8 : 105;
		// 2026-10-21：不再用 CPU_TccOffsetMaximum(10) 当 UI 上限（实测 offset 12/14/21 都能被接受）；
		// Intel TCC 偏移按 0..30 放开 ⇒ 温度墙 [TjMax-30 .. TjMax]（255HX: 75..105℃）。
		TccMinimum = Math.Max(75, tjBase - 30);
		// 实测：温度墙 100℃(偏移5) 可写，104/105℃(偏移1/0) 被固件拒 ⇒ 上限先按 100℃
		// 墙温超过 100℃ 固件不允许（105℃ 机器上偏移 1/0 被拒 = 墙温 104/105）
		TccMaximum = Math.Min(100, tjBase);
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
	/// <para>⭐⭐ 2026-10-17（宝龙达机型 · 真机反馈「性能模式变成三档加下拉了」，用户原话
	/// 「应当还是保存四个按钮 没有下拉 3档加下拉是同方独有的」）：⭐ <b>宝龙达模具先挡掉</b> ——
	/// 「模式 × 配置」是<b>同方官方 30 系控制台</b>的能力，宝龙达 WMI 功能号表里<b>根本没有</b>
	/// ProfileIndex 相关项 ⇒ 本属性对宝龙达必须恒 false。</para>
	/// <para>⚠️ <b>必须挡在 Gen30 判据之前</b>：宝龙达机型常配 30 系显卡（如 RTX 3070Ti），
	/// 仅凭 <c>DgpuGeneration == Gen30</c> 会判真 ⇒ 连锁两处错误 UI：
	/// ① 设置窗的 <c>ApplyPerfConfigRowVisibility()</c> 显示「配置 N」下拉行；
	/// ② 设置窗构造函数里 <c>flagCustomMode = flag17 &amp;&amp; !(SupportsModeProfileConfig || Force30Ui.Enabled)</c>
	/// ⇒ 「自定义」按钮被隐藏 ⇒ 只剩三档（用户报的正是这个）。
	/// ⚠️ 上面两处都是 UI 程序集（MechrevoLite）里的私有成员 ⇒ 这里刻意只写代码名、不用 cref，
	/// 避免跨程序集 cref 解析失败多出 CS1574 警告（本仓库告警数是硬上限）。</para>
	/// <para>⚠️ 实验开关 <c>Force30Ui</c> / <c>Force30Perf</c> <b>一行未动</b>：
	/// <c>Force30Ui.Enabled</c> 在各调用点<b>单独 OR</b>（照旧可做 UI 对照）；
	/// <c>Force30Perf</c> 住在 <see cref="MechrevoService.IsGcu30"/> 里，本挡位把它一并挡在宝龙达之外 ——
	/// 这是<b>有意</b>的：该实验的用途是"验证 40A GCU 能不能正确切 30 系性能方式"，
	/// 而宝龙达机型<b>没有 GCU</b>（无 MQTT 通路）⇒ 该实验在宝龙达上本就没有意义。</para>
	/// </summary>
	public bool SupportsModeProfileConfig
	{
		get
		{
			// ⭐ 2026-10-17：宝龙达没有「模式 × 配置」协议 ⇒ 恒 false（见方法注释的连锁后果说明）。
			if (UsesBaolongda)
			{
				return false;
			}
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
		bool umiGpuSwitch = IsUmiMachine && IsGcuConnected;
		bool supportsDgpuDirect = SupportsDgpuDirect || umiGpuSwitch;
		bool useMuxTarget = mode == 0 && supportsDgpuDirect && !SupportsIgpuOnly;
		GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, supportsDgpuDirect, SupportsIgpuOnly, DgpuGeneration);
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

	/// <summary>2026-10-22（机主：所有机器都要恢复默认曲线）：取内置 DefaultCurve_*.json 的默认 CPU/GPU 曲线；
	/// GCU/同方用 SetFanCurve 写回，读不到资源文件时返回 false。</summary>
	internal bool TryGetBuiltInDefaultFanCurve(out byte[] cpuUpT, out byte[] cpuDuty, out byte[] gpuUpT, out byte[] gpuDuty)
	{
		cpuUpT = null;
		cpuDuty = null;
		gpuUpT = null;
		gpuDuty = null;
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
		if (num < 0 || num >= _defaultCpuDuty.Length)
		{
			return false;
		}
		byte[] array = _defaultCpuUpT[num];
		byte[] array2 = _defaultCpuDuty[num];
		byte[] array3 = _defaultGpuUpT[num];
		byte[] array4 = _defaultGpuDuty[num];
		if (array == null || array2 == null || array3 == null || array4 == null)
		{
			return false;
		}
		cpuUpT = (byte[])array.Clone();
		cpuDuty = (byte[])array2.Clone();
		gpuUpT = (byte[])array3.Clone();
		gpuDuty = (byte[])array4.Clone();
		return true;
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

	/// <summary>曲线表可信度：至少 4 个点、温度单调不超 120℃、占空比 0..100。</summary>
	private static bool CurveLooksSane(byte[] upT, byte[] duty)
	{
		int points = 0;
		int previous = -1;
		for (int i = 0; i < 16; i++)
		{
			if (upT[i] == byte.MaxValue)
			{
				break;
			}
			if (upT[i] > 120 || duty[i] > 100)
			{
				return false;
			}
			if (upT[i] < previous)
			{
				return false;
			}
			previous = upT[i];
			points++;
		}
		return points >= 4;
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
		// （取消循环 + 恢复用户单色）⇒  退出后不再有任何周期性 WMI 写入
		StopKeyboardRainbow();
		// （自动关灯）：退出时停计时器 + 清内存标记（ 不写灯；下次启动按配置正常）
		StopKeyboardAutoOffTimer();
		lock (_kbdAutoOffGate)
		{
			_kbdAutoOffSaved = null;
			Volatile.Write(ref _kbdAutoOffApplied, 0);
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
		RestoreBaolongdaFanFirmwareControl();
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
