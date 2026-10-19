using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite.Properties;

namespace MechrevoLite.Display;

/// <summary>
/// 「息屏（不睡眠）」执行体 —— 2026-10-11 重构：删除旧的 SC_MONITORPOWER / WMI 背光 0% 方案，
/// 改为 NullSignal 同款「虚拟显示器」方案（usbmmidd_v2 虚拟显示器驱动，第三方驱动版权归 Amyuni Technologies Inc.）：
///   ① 挂载虚拟显示器（未安装则 deviceinstaller64 install 首次注入；已装则直接 enableidd 1 激活）
///   ② displayswitch.exe /external 强切视频信号到虚拟屏 ⇒ 物理屏熄灭、系统保持运行（不睡眠）
///   ③ 100ms 轮询鼠标移动（&gt;5px）→ displayswitch.exe /internal 切回内屏 → enableidd 0 清理虚拟屏
///   ④ 全程持有 ES_SYSTEM_REQUIRED 防空闲睡眠；30 分钟看门狗兜底强制恢复
/// 为什么换方案：S0 现代待机机器上 SC_MONITORPOWER(2) 会被系统当作「进入新型待机」请求
/// （事件 506 原因=SC_MONITORPOWER，软件层绕不开 BIOS 把息屏与睡眠绑定的限制）；虚拟显示器方案
/// 从信号层面切走视频输出，物理屏熄灭但系统完全保持运行，不受待机绑定影响。
/// 参考：https://github.com/IYATT-yx/NullSignal（息屏原理与流程，MIT）。
/// ⛔ 本类不碰电源计划、不写注册表/EC/NVRAM；不持 ES_DISPLAY_REQUIRED（语义=保持显示器亮，与息屏相冲）。
/// 黑屏序列在后台线程执行；驱动组件（usbmmidd_v2 目录）随安装包分发到程序目录。
/// 2026-10-12（用户原话「我发现虚拟屏幕没有关掉,我开了息屏恢复以后」）：生命周期加固 —— 恢复 / 看门狗 /
/// 退出 / 启动四路都走 <see cref="TeardownVirtualDisplay" />，拆除判据改为设备侧状态，且重启时清一次残留。
/// </summary>
internal static class VirtualDisplayBlank
{
	/// <summary>看门狗上限 —— 鼠标检测异常时兜底强制恢复，避免无限黑屏。</summary>
	internal static readonly TimeSpan MaxBlankDuration = TimeSpan.FromMinutes(30L);

	internal const int PollIntervalMs = 100;

	/// <summary>鼠标位移超过该值（像素）视为用户动作，恢复显示。</summary>
	internal const int MouseMoveThresholdPx = 5;

	internal const int TopologySwitchDelayMs = 300;

	internal const int PostRestoreDelayMs = 500;

	private const uint ES_CONTINUOUS = 2147483648u;

	private const uint ES_SYSTEM_REQUIRED = 1u;

	/// <summary>假期间持有的执行态 = ES_CONTINUOUS | ES_SYSTEM_REQUIRED（不含 ES_DISPLAY_REQUIRED）。</summary>
	private const uint BlankExecutionState = 2147483649u;

	private static readonly object Gate = new object();

	private static bool _active;

	private static bool _restoring;
	// （机主：息屏永久卡「an earlier blank session…」）：恢复开始时刻 —— 用于识别残留会话
	private static DateTime _restoreStartedUtc;

	private static DateTime _blankStartedUtc;

	/// <summary>
	/// 2026-10-12（「息屏恢复后虚拟屏没被拆掉」修复）：本次息屏是否**已经把虚拟显示屏打开**。
	/// ⚠️ 必须在调用 enableidd 1 **之前**置 true —— 置位晚于动作的话，恢复/强杀恰好落在中间时，
	/// 拆除逻辑就不知道"屏可能已经开过"，虚拟屏会永久留在系统里。
	/// 只有复检到 0 个虚拟屏之后才清回 false。
	/// </summary>
	private static bool _virtualDisplayOn;

	/// <summary>
	/// 2026-10-12（同上）：本次息屏是否**已经把显示拓扑切到外接/虚拟屏**。只有切过才切回，
	/// ⛔ 避免在用户本来就接了真实外接屏时被启动清理/退出路径改掉多屏布局。
	/// </summary>
	private static bool _topologySwitched;

	/// <summary>
	/// ⭐⭐ 2026-10-17（真机反馈「外接显示器情况下关闭息屏后内屏亮、外屏不亮」）：
	///   ⭐ 息屏**前**记录当时的显示拓扑，恢复时**按它**切回 ⚠️
	///   ⛔ 不再无条件 `displayswitch /internal` —— 那是「仅电脑屏幕」，
	///      会**把所有外接屏关掉** ⇒ 有外接屏的用户恢复后外屏就黑了 ❌
	/// </summary>
	private static DisplayNative.DISPLAYCONFIG_TOPOLOGY_ID _topologyBeforeBlank;

	/// <summary>usbmmidd 单设备最多 4 个虚拟显示屏，且 enableidd 一次只关 1 个（驱动 idd_instructions.txt 语义）。</summary>
	internal const int MaxVirtualMonitors = 4;

	/// <summary>拆除串行化闸门：恢复线程 / 黑屏线程 / 启动清理可能同时要拆，这里只让一个真正动手。</summary>
	private static readonly object TeardownGate = new object();

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern uint SetThreadExecutionState(uint esFlags);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DISPLAY_DEVICE
	{
		public int cb;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string DeviceName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceString;

		public int StateFlags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceID;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceKey;
	}

	/// <summary>
	/// 只读枚举显示适配器：lpDevice=null 表示枚举所有适配器；dwFlags=0 时返回适配器本身。
	/// ⚠️ 参数刻意**不写可空注解**（本文件没有 #nullable 上下文，写 `string?` 只会多一条 CS8632），
	/// 语义等价（只传 null）。
	/// </summary>
	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int X;

		public int Y;
	}

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out POINT lpPoint);

	internal static bool IsActive
	{
		get
		{
			lock (Gate)
			{
				return _active;
			}
		}
	}

	/// <summary>
	/// 启动息屏序列（同步检查 + 后台执行）。返回 null = 已启动；返回字符串 = 启动被拒绝的原因
	/// （已在息屏/恢复中、驱动组件缺失），调用方应把该原因展示给用户。
	/// </summary>
	internal static string Start()
	{
		bool staleReset = false;
		lock (Gate)
		{
			if (_active || _restoring)
			{
				// active > 120s 或 restoring > 30s ⇒ 视为残留（线程异常/睡眠打断）⇒ 强制复位后继续本次息屏
				DateTime now = DateTime.UtcNow;
				double activeAge = _active ? (now - _blankStartedUtc).TotalSeconds : 0.0;
				double restoreAge = _restoring ? (now - _restoreStartedUtc).TotalSeconds : 0.0;
				double age = Math.Max(activeAge, restoreAge);
				bool stale = (_active && activeAge > 120.0) || (_restoring && restoreAge > 30.0);
				if (!stale)
				{
					return Strings.ScreenBlankBusyRestoring;
				}
				Logger.WriteError("[blank] 检测到残留息屏会话（active=" + _active + " restoring=" + _restoring
					+ " age=" + age.ToString("F0") + "s）⇒ 强制复位");
				bool needTeardown = _virtualDisplayOn || _topologySwitched || _active;
				_active = false;
				_restoring = false;
				_restoreStartedUtc = default(DateTime);
				_virtualDisplayOn = false;
				_topologySwitched = false;
				if (needTeardown)
				{
					try
					{
						TeardownVirtualDisplay("stale-reset", restoreTopology: true);
					}
					catch (Exception ex)
					{
						Logger.WriteError("[blank] 残留复位失败 ⇒ " + ex.Message);
						return Strings.ScreenBlankStaleFailed;
					}
				}
				Logger.WriteError("[blank] 残留复位完成 ⇒ 重试 Start()");
				staleReset = true;
			}
			string driverDir = GetDriverDir();
			if (!VerifyDriverFiles(driverDir, out string missing))
			{
				return Strings.ScreenBlankDriverMissing + missing;
			}
			_active = true;
			_blankStartedUtc = DateTime.UtcNow;
			_restoreStartedUtc = default(DateTime);
			// ⭐ 息屏前先记拓扑（必须在 displayswitch /external 之前）⇒ 恢复时按它切回 ✅
			_topologyBeforeBlank = ReadCurrentTopology();
		}
		if (staleReset)
		{
			Logger.WriteLine("[blank] 残留复位后重新开始息屏序列（本次点击不再要求用户重试）");
		}		Task.Run(delegate
		{
			try
			{
				RunBlackoutSequence();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("VirtualDisplayBlank sequence failed: " + ex.Message);
				RestoreCore("sequence-failure");
			}
		});
		return null;
	}

	/// <summary>退出/会话结束/失败时的立即恢复：切回内屏 + 清理虚拟屏 + 清执行态（幂等）。</summary>
	internal static void RestoreImmediate()
	{
		RestoreCore("immediate");
	}

	/// <summary>系统即将进入睡眠（SettingsForm 收到 PBT_APMSUSPEND 时调用）：清理本次息屏状态。</summary>
	internal static void AbortOnSuspend()
	{
		RestoreCore("suspend");
	}

	/// <summary>
	/// 立即恢复（退出 / 会话结束 / 序列失败 / 崩溃兜底）。
	/// 2026-10-12（残屏修复）：拆除判据从"<c>_active</c> 仍为 true"改成**设备侧状态**
	/// （<c>_virtualDisplayOn</c> / <c>_topologySwitched</c>）。旧实现里只要 <c>_active</c> 已被置 false
	/// （序列的 finally、异常路径）这里就直接 return，虚拟屏永远留在系统里。
	/// </summary>
	private static void RestoreCore(string reason)
	{
		bool needTeardown;
		lock (Gate)
		{
			if (_restoring)
			{
				return;
			}
			_restoring = true;
			_restoreStartedUtc = DateTime.UtcNow;
			// 置 _active=false：让黑屏线程的等待循环（WaitForExitCondition）立即退出，随后走它自己的恢复路径
			//（/internal + enableidd 0，与这里幂等重复无害）。
			needTeardown = _active || _virtualDisplayOn || _topologySwitched;
			_active = false;
		}
		try
		{
			if (needTeardown)
			{
				TeardownVirtualDisplay("restore/" + reason, restoreTopology: true);
			}
			Logger.WriteLine("VirtualDisplayBlank: restored (" + reason + ").");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("VirtualDisplayBlank restore failed (" + reason + "): " + ex.Message);
		}
		finally
		{
			lock (Gate)
			{
				_restoring = false;
				_restoreStartedUtc = default(DateTime);
			}
			ApplyExecutionState(ES_CONTINUOUS);
		}
	}

	private static void RunBlackoutSequence()
	{
		ApplyExecutionState(BlankExecutionState);
		try
		{
			string driverDir = GetDriverDir();
			EnsureDriverInstalled(driverDir);
			if (!IsActive)
			{
				// 恢复已经先跑过（退出/睡眠/失败）：⛔ 不要再开虚拟屏，否则它的拆除会跑在 enableidd 1 前面 ⇒ 残屏。
				return;
			}
			lock (Gate)
			{
				_virtualDisplayOn = true;
			}
			EnableVirtualDisplay(driverDir);
			if (!IsActive)
			{
				// 与 RestoreCore 赛跑：恢复的拆除早于这里的 enableidd 1 ⇒ 由本线程补拆（幂等）。
				Logger.WriteLine("Screen blank: restored before topology switch; tearing the virtual display down.");
				TeardownVirtualDisplay("race-after-enable", restoreTopology: false);
				return;
			}
			lock (Gate)
			{
				_topologySwitched = true;
			}
			// 给刚激活的虚拟屏一点出现时间，否则 displayswitch /external 可能还没有可切的目标。
			Thread.Sleep(TopologySwitchDelayMs);
			SwitchTopology(external: true);
			Logger.WriteLine("Screen blank: virtual-display blackout active (mouse move to exit).");
			WaitForExitCondition();
			TeardownVirtualDisplay("input-or-watchdog", restoreTopology: true);
			Logger.WriteLine("Screen blank: restored via virtual-display switch.");
		}
		finally
		{
			lock (Gate)
			{
				_active = false;
			}
			ApplyExecutionState(ES_CONTINUOUS);
		}
	}

	/// <summary>
	/// 启动时清一次残留（幂等、尽力而为、全程写日志）。
	/// 2026-10-12（残屏修复）：崩溃或被强杀（部署脚本 <c>Process.Kill()</c>、任务管理器结束任务）时
	/// RestoreImmediate 根本不会执行，虚拟显示屏会一直留在系统里 —— 用户看到的就是"显示器 2 关不掉"。
	/// 本方法只关本程序自己的 usbmmidd 虚拟屏：⛔ 不切显示拓扑（不动用户真实多屏布局）、
	/// ⛔ 不卸载驱动、⛔ 不删设备节点、⛔ 不碰其它虚拟显示适配器。
	/// </summary>
	internal static void CleanupResidualOnStartup()
	{
		try
		{
			string driverDir = GetDriverDir();
			if (!Directory.Exists(driverDir))
			{
				return;
			}
			int before = CountVirtualDisplays();
			if (before == 0)
			{
				return;
			}
			Logger.WriteLine("VirtualDisplayBlank: residual virtual display(s) found at startup (count=" + before + "); cleaning up.");
			TeardownVirtualDisplay("startup-residue", restoreTopology: false);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("VirtualDisplayBlank: startup residual cleanup failed: " + ex.Message);
		}
	}

	/// <summary>
	/// 拆除虚拟显示屏（可选切回内屏 + 关闭全部虚拟屏）。恢复 / 看门狗 / 退出 / 启动清理共用这一个入口。
	/// 幂等、尽力而为、⛔ 不静默失败：每步都写日志，复检仍有残留会写 ⚠️ 行并保留下次重试的机会。
	/// ⛔ 只操作 usbmmidd 虚拟屏：不卸载驱动、不删设备节点、不碰其它显示适配器。
	/// </summary>
	private static void TeardownVirtualDisplay(string reason, bool restoreTopology)
	{
		lock (TeardownGate)
		{
			try
			{
				if (restoreTopology && _topologySwitched)
				{
					// 先清位再动作：即使 displayswitch 失败也不重复切，且不会再挡住后续的虚拟屏拆除。
					lock (Gate)
					{
						_topologySwitched = false;
					}
					// ⭐ 2026-10-17：按息屏前记录的拓扑恢复（⛔ 不再无条件 /internal —— 那会关掉外接屏）
					RestoreTopology();
					Thread.Sleep(PostRestoreDelayMs);
				}
				string driverDir = GetDriverDir();
				if (!Directory.Exists(driverDir))
				{
					Logger.WriteLine("VirtualDisplayBlank: teardown (" + reason + ") skipped: driver component missing (" + driverDir + ").");
					return;
				}
				int before = CountVirtualDisplays();
				// 探测失败（-1）时按驱动上限 4 次兜底；一次 enableidd 只关 1 个，所以要按数量逐个关。
				int attempts = (before < 0) ? MaxVirtualMonitors : Math.Min(Math.Max(before, 1), MaxVirtualMonitors);
				string tool = Path.Combine(driverDir, "deviceinstaller64.exe");
				int lastExit = 0;
				for (int i = 0; i < attempts; i++)
				{
					lastExit = RunHidden(tool, "enableidd 0", driverDir);
				}
				int after = CountVirtualDisplays();
				if (after != 0)
				{
					// 2026-10-12（本机实测硬事实，也是用户「虚拟屏没关掉」的根因）：
					// usbmmidd 自带的 `enableidd 0` **退出码 0 但什么都不做** —— 连call 4 次后设备仍是
					// Started、4 个虚拟屏适配器一个没少（这就是为什么旧代码"恢复成功"的日志照写、屏照留）。
					// deviceinstaller64.exe 本体就是 devcon（`help` 列的是 devcon 的命令表：
					// disable/enable/status/find/remove/...），所以这里退回 `disable usbmmidd`：
					// 只**禁用**设备 —— 虚拟屏适配器立刻全部消失（实测 4 → 0，显示器设备 OK → PHANTOM），
					// 而**驱动保留**（设备节点还在，只是 Disabled）⇒ 下次息屏只多一条 `enable usbmmidd`，仍然快。
					// ⛔ 不是卸载驱动（remove / dp_delete）、⛔ 不删设备节点、⛔ 不碰其它虚拟显示适配器。
					int disableExit = RunHidden(tool, "disable usbmmidd", driverDir);
					Logger.WriteLine("VirtualDisplayBlank: 'enableidd 0' left " + after + " virtual display(s); fell back to 'disable usbmmidd' exit=" + disableExit + ".");
					lastExit = disableExit;
					after = CountVirtualDisplays();
				}
				lock (Gate)
				{
					// after == -1（枚举失败）⇒ 保留"可能还有"的标记，下次恢复/启动继续拆。
					_virtualDisplayOn = after != 0;
				}
				if (after > 0)
				{
					Logger.WriteLine("VirtualDisplayBlank: WARN teardown (" + reason + ") left " + after + " virtual display(s) (lastExit=" + lastExit + "); will retry on the next restore/startup.");
				}
				else
				{
					Logger.WriteLine("VirtualDisplayBlank: teardown (" + reason + ") done: before=" + before + " attempts=" + attempts + " after=" + after + " lastExit=" + lastExit + ".");
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("VirtualDisplayBlank: teardown (" + reason + ") failed: " + ex.Message);
			}
		}
	}

	/// <summary>
	/// 只读枚举当前系统里的 usbmmidd 虚拟显示屏数量（EnumDisplayDevices，⛔ 不依赖 pnputil 的本地化输出，
	/// ⛔ 不创建/不删除任何显示设备）。返回 -1 = 枚举失败（不确定；调用方按"可能有残留"处理）。
	/// 匹配判据同时看 DeviceID（实测 usbmmidd 适配器 = <c>usbmmidd</c>）与 DeviceString 兜底；
	/// 其它虚拟显示适配器（如第三方 GameViewerIddDriver）不会命中。
	/// </summary>
	private static int CountVirtualDisplays()
	{
		try
		{
			int count = 0;
			DISPLAY_DEVICE device = default(DISPLAY_DEVICE);
			int size = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
			device.cb = size;
			for (uint i = 0u; EnumDisplayDevices(null, i, ref device, 0u); i++)
			{
				if (device.DeviceID.IndexOf("usbmmidd", StringComparison.OrdinalIgnoreCase) >= 0
					|| device.DeviceString.IndexOf("USB Mobile Monitor", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					count++;
				}
				device.cb = size;
			}
			return count;
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("vblank", "virtual-display enumeration failed: " + ex.Message, 60000);
			return -1;
		}
	}

	/// <summary>
	/// 等待退出条件：鼠标移动 &gt; <see cref="MouseMoveThresholdPx"/>px（NullSignal 行为）或看门狗到点；
	/// 期间若被 RestoreCore 置 _active=false（退出/睡眠），也立即退出。
	/// </summary>
	private static void WaitForExitCondition()
	{
		POINT initial;
		GetCursorPos(out initial);
		while (true)
		{
			Thread.Sleep(PollIntervalMs);
			POINT current;
			GetCursorPos(out current);
			if (Math.Abs(current.X - initial.X) > MouseMoveThresholdPx || Math.Abs(current.Y - initial.Y) > MouseMoveThresholdPx)
			{
				return;
			}
			if ((DateTime.UtcNow - _blankStartedUtc) > MaxBlankDuration)
			{
				Logger.WriteLine("Screen blank: watchdog reached (" + MaxBlankDuration.TotalMinutes + "min), forcing restore.");
				return;
			}
			lock (Gate)
			{
				if (!_active)
				{
					return;
				}
			}
		}
	}

	/// <summary>驱动目录 = 程序目录下的 usbmmidd_v2（随安装包分发）。</summary>
	private static string GetDriverDir()
	{
		return Path.Combine(AppContext.BaseDirectory, "usbmmidd_v2");
	}

	/// <summary>校验驱动关键文件存在。返回 false 时 out 给出缺失清单。</summary>
	private static bool VerifyDriverFiles(string driverDir, out string missing)
	{
		missing = "";
		if (!Directory.Exists(driverDir))
		{
			missing = driverDir;
			return false;
		}
		string[] required = new string[2] { "deviceinstaller64.exe", "usbmmidd.inf" };
		foreach (string fileName in required)
		{
			if (!File.Exists(Path.Combine(driverDir, fileName)))
			{
				missing += (missing.Length > 0 ? "; " : "") + fileName;
			}
		}
		return missing.Length == 0;
	}

	/// <summary>检查系统中是否已存在 usbmmidd 设备节点（pnputil）。</summary>
	private static bool IsDriverInstalled()
	{
		try
		{
			string output = RunHiddenCapture("pnputil", "/enum-devices /deviceid usbmmidd", null);
			return output.IndexOf("usb mobile monitor", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("vblank", "usbmmidd driver check failed: " + ex.Message, 60000);
			return false;
		}
	}

	/// <summary>首次运行向系统注入虚拟显示驱动（毫秒级；已安装则跳过）。</summary>
	private static void EnsureDriverInstalled(string driverDir)
	{
		if (IsDriverInstalled())
		{
			return;
		}
		Logger.WriteLine("VirtualDisplayBlank: injecting usbmmidd virtual-display driver (first run).");
		RunHidden(Path.Combine(driverDir, "deviceinstaller64.exe"), "install usbmmidd.inf usbmmidd", driverDir);
	}

	/// <summary>激活虚拟显示屏（enableidd 1）。</summary>
	private static void EnableVirtualDisplay(string driverDir)
	{
		string tool = Path.Combine(driverDir, "deviceinstaller64.exe");
		// 2026-10-12：拆除现在会把 IDD 设备**禁用**（`disable usbmmidd`，见 TeardownVirtualDisplay），
		// 所以激活前必须先确保设备是启用状态；已启用时这条命令幂等（devcon 会报 1 device(s) are enabled）。
		int enableExit = RunHidden(tool, "enable usbmmidd", driverDir);
		if (enableExit != 0)
		{
			Logger.WriteLine("VirtualDisplayBlank: 'enable usbmmidd' exit=" + enableExit + " (continuing).");
		}
		RunHidden(tool, "enableidd 1", driverDir);
	}

	/// <summary>读当前显示拓扑（QDC_ONLY_ACTIVE_PATHS）。失败/异常 ⇒ INTERNAL（最保守）。</summary>
	private static DisplayNative.DISPLAYCONFIG_TOPOLOGY_ID ReadCurrentTopology()
	{
		try
		{
			if (DisplayNative.GetDisplayConfigBufferSizes(DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
			{
				return DisplayNative.DISPLAYCONFIG_TOPOLOGY_ID.DISPLAYCONFIG_TOPOLOGY_INTERNAL;
			}
			uint pc = Math.Max(1u, pathCount);
			uint mc = Math.Max(1u, modeCount);
			DisplayNative.DISPLAYCONFIG_PATH_INFO[] paths = new DisplayNative.DISPLAYCONFIG_PATH_INFO[pc];
			DisplayNative.DISPLAYCONFIG_MODE_INFO[] modes = new DisplayNative.DISPLAYCONFIG_MODE_INFO[mc];
			if (DisplayNative.QueryDisplayConfig(DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, ref pc, paths, ref mc, modes, out DisplayNative.DISPLAYCONFIG_TOPOLOGY_ID topology) != 0)
			{
				return DisplayNative.DISPLAYCONFIG_TOPOLOGY_ID.DISPLAYCONFIG_TOPOLOGY_INTERNAL;
			}
			return topology;
		}
		catch
		{
			return DisplayNative.DISPLAYCONFIG_TOPOLOGY_ID.DISPLAYCONFIG_TOPOLOGY_INTERNAL;
		}
	}

	/// <summary>
	/// ⭐ 2026-10-17：按**息屏前记录的拓扑**恢复（映射到 displayswitch 的 4 个参数）。
	/// ⛔ 旧实现无条件 `/internal`（仅电脑屏幕）⇒ 有外接屏的用户恢复后外屏全黑 ❌
	/// </summary>
	private static void RestoreTopology()
	{
		string arg = _topologyBeforeBlank switch
		{
			DisplayNative.DISPLAYCONFIG_TOPOLOGY_ID.DISPLAYCONFIG_TOPOLOGY_CLONE => "/clone",
			DisplayNative.DISPLAYCONFIG_TOPOLOGY_ID.DISPLAYCONFIG_TOPOLOGY_EXTEND => "/extend",
			DisplayNative.DISPLAYCONFIG_TOPOLOGY_ID.DISPLAYCONFIG_TOPOLOGY_EXTERNAL => "/external",
			_ => "/internal",
		};
		Logger.WriteLine("VirtualDisplayBlank: restore topology by pre-blank kind=" + _topologyBeforeBlank + " => displayswitch " + arg + ".");
		string displayswitch = Path.Combine(Environment.SystemDirectory, "displayswitch.exe");
		int exitCode = RunHidden(displayswitch, arg, null);
		if (exitCode != 0)
		{
			Logger.WriteLine("VirtualDisplayBlank: displayswitch " + arg + " exit=" + exitCode + ".");
		}
	}

	/// <summary>切换显示拓扑：external=true ⇒ 只显示外接/虚拟屏（物理屏灭）。</summary>
	private static void SwitchTopology(bool external)
	{
		string displayswitch = Path.Combine(Environment.SystemDirectory, "displayswitch.exe");
		int exitCode = RunHidden(displayswitch, external ? "/external" : "/internal", null);
		if (exitCode != 0)
		{
			// ⛔ 不静默失败：拓扑没切成功也要留证据（旧实现把退出码整个丢掉）。
			Logger.WriteLine("VirtualDisplayBlank: displayswitch " + (external ? "/external" : "/internal") + " exit=" + exitCode + ".");
		}
	}

	/// <summary>
	/// 隐藏执行外部工具并等它退出（⛔ 不弹窗、不留窗口）。返回退出码，-1 = 启动失败 / 超时（已 Kill）。
	/// 2026-10-12（残屏修复）：旧实现是 <c>void</c> 且把退出码整个丢掉 —— enableidd 0 失败也当成功，
	/// 于是"恢复成功"的日志照写、虚拟屏照留。现在失败一律写日志。
	/// </summary>
	private static int RunHidden(string fileName, string args, string workingDir)
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo(fileName, args)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden
			};
			if (!string.IsNullOrEmpty(workingDir))
			{
				psi.WorkingDirectory = workingDir;
			}
			using (Process? p = Process.Start(psi))
			{
				if (p == null)
				{
					return -1;
				}
				if (!p.WaitForExit(15000))
				{
					try
					{
						p.Kill();
					}
					catch
					{
					}
					return -1;
				}
				return p.ExitCode;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("vblank", "external command failed (" + Path.GetFileName(fileName) + " " + args + "): " + ex.Message, 60000);
			return -1;
		}
	}

	private static string RunHiddenCapture(string fileName, string args, string workingDir)
	{
		ProcessStartInfo psi = new ProcessStartInfo(fileName, args)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			RedirectStandardOutput = true,
			StandardOutputEncoding = System.Text.Encoding.UTF8
		};
		if (!string.IsNullOrEmpty(workingDir))
		{
			psi.WorkingDirectory = workingDir;
		}
		using (Process? p = Process.Start(psi))
		{
			if (p == null)
			{
				return "";
			}
			string output = p.StandardOutput.ReadToEnd();
			if (!p.WaitForExit(15000))
			{
				try
				{
					p.Kill();
				}
				catch
				{
				}
			}
			return output;
		}
	}

	private static void ApplyExecutionState(uint flags)
	{
		SetThreadExecutionState(flags);
	}
}
