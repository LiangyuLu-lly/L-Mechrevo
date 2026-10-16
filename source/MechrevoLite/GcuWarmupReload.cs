using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;
using MechrevoLite.UI;

namespace MechrevoLite;

/// <summary>
/// 2026-10-09（warmupreload）：首次装 GCU 有「预热启动」期 —— LM 先起来时，ItemSupport 能力位 /
/// GCU 连接 / 机型身份都还没上报完，于是 <see cref="RuntimeModelSupport.Current" /> 判成"不支持"，
/// 电池区闸门（<c>SettingsForm.ApplyChargeLimitSliderGating</c>）与
/// <c>SettingsForm.ApplyUnsupportedModelNotice</c> 那一族（<c>WriteEntryControlNames</c> 的 7 个写入类
/// 控件 + 全部 <c>_quickSwitches</c>）就被隐藏/灰掉，**不重启 LM 不会自己回来**（用户真机证据：耀世机主
/// 重启 LM 后电池滑块就出现了）。
///
/// 修法（用户原话：「我思考能不能做个首次运行连上 GCU 5s 后强制刷新一次 LM（相当于重启重载）」）：
/// **确认 GCU 已连接**之后再等 5 秒，做一次**软重载** —— 重新读能力位/机型身份（全是只读的注册表
/// 快照读取），重新评估依赖它们的可见性/启用态，然后写一条日志。口径逐条：
/// <list type="bullet">
/// <item>⚠️ **绝不定时轮询、绝不循环重载**：本类只被 <c>Program</c> 的 <c>hw.ConnectionReady</c>
/// （"订阅就绪"事件，见 <c>MechrevoHw.NotifyConnectionReady</c>）调用；进程内一次性闩 + config 里
/// 跨进程一次性标记（<c>gcu_warmup_reload_done</c>，重载执行后立刻写下）⇒ 每台机器/每次安装最多一次。
/// 延后走的是一次性的 <see cref="Application.Idle" />（判定不通过就返回、不重排），不是轮询。</item>
/// <item>⚠️ **触发条件是"连上 GCU 后满 5 秒"**，不是"启动后 5 秒"：没连上就一直不重载（保持现有行为）；
/// 同一次运行里断开再连**不再**重新计时（闩已置位）—— 软重载只在"刚装好还没上报"这一刻有意义。</item>
/// <item>⚠️ **不打扰用户**：先抓滚动位置与键盘焦点，重载后按新范围还原；此刻用户正在交互
/// （下拉展开 / 左键按住拖滑块 / 焦点在下拉框或滑块上）⇒ 延后到下一次空闲再试，日志写"已延后"。</item>
/// <item>⚠️ **只读**：不写注册表/EC/NVRAM，不切显卡/性能模式，不动显示、灯光、电源计划、校色、睡眠省电。
/// 唯一的写是 config 里那一个一次性标记键。</item>
/// </list>
///
/// 2026-10-09（warmupreload 兜底，用户原话：「那前面你说的一次性做掉吧」）：软重载**覆盖不到**
/// 「构造时只读一次」的那部分状态（<c>SettingsForm</c> 构造期读的能力/机型快照、以及任何在
/// <c>RefreshDeviceCapabilities()</c> 之外缓存下来的判定）。所以软重载之后若**仍判不支持**，就在
/// 这里补一条**应用内一次性提示**：「部分功能可能需要重启 L-Mechrevo 后才会出现」＋「立即重启 LM」
/// （MessageBox 是/否，与语言切换那条提示同一形态）；用户点「是」＝ <c>--restart</c> 重起进程。
/// ⚠️ **只提示一次**：独立 config 键 <see cref="RestartHintMarkerKey" />，且**弹窗之前**就写下。
/// 安装器侧还有最后一道兜底（装完 GCU 时若 LM 在跑就重启它一次），见
/// <c>installer-src\files\GCU\Install-Gcu.ps1</c> 的 <c>Restart-AppAfterGcuInstall</c>。
/// </summary>
internal static class GcuWarmupReload
{
	/// <summary>跨进程的一次性标记（AppConfig，机器上那份 config.json）。写下的时机＝重载执行之后。</summary>
	internal const string MarkerKey = "gcu_warmup_reload_done";

	/// <summary>
	/// 2026-10-09（warmupreload 兜底）：软重载跑完之后**仍判不支持**时那次「可能要重启」提示的
	/// 跨进程一次性标记（AppConfig）。⚠️ 与 <see cref="MarkerKey" /> **分开**：软重载本身成功过
	/// （标记已写）但结论仍是不支持时，提示才需要出现；反之软重载成功且已经支持了，这个键永远不写。
	/// </summary>
	internal const string RestartHintMarkerKey = "gcu_warmup_restart_hint_shown";

	/// <summary>"确认连上 GCU"之后再等多久 —— 用户口径是 5 秒，留成常量便于日后调整/自检。</summary>
	internal const int WarmupDelayMs = 5000;

	/// <summary>2026-10-15（restartgrade）：主动重发一次全量状态查询后，等多久再重读能力快照（毫秒）。
	/// 有界常量：GCU 是本地回环 MQTT，实测响应在几十毫秒级；留 800ms 是给"GCU 收到查询后把新值写进
	/// ItemSupport 等快照"留余量。若这一拍仍没等到，阶梯的下一拍会再要一次（最多 <see cref="MaxRungs" /> 拍）。</summary>
	internal const int RestartGradeWaitMs = 800;

	// ============================================================================================
	// 2026-10-15（firstrun-reload）：用户原话「之前不是有首次安装 5s 刷新界面吗 还是不能把全部功能
	// 显示出来 能不能首次启动 5s 自动重启一次或者有重启级别的刷新也行」。
	//
	// 【为什么 5s 那一次仍显示不全】软重载发生在"连上 GCU 后 5 秒"，它重读的是 GCU 已经**上报过**
	// 的能力位（ItemSupport 注册表快照 + 本进程已收到的 MQTT 上报）。若 GCU 在 5 秒后才把
	// ItemSupport / 各类 *Seen（BatteryProtection、Setting/Status、DcHz…）补齐，那 5 秒这一拍读到的
	// 仍是"半成品"，而**原实现是一次性闩**（`gcu_warmup_reload_done` 一写就再也不重来）⇒ 漏报的部分
	// 不重启 LM 永远不会回来。也就是说：问题不是"5 秒这一拍做错了"，而是"**只做了一拍**"。
	//
	// 【方案选择（用户给了 A. 5s 后自动重启 / B. 有重启级刷新；我选 B 的受限版）】
	//   * ⛔ 不选 A（进程自动重启）：对用户很突兀（窗口会自己关掉再弹回来、托盘图标闪一下），
	//     且"首次启动"这个判据本身不可靠 —— 重启后仍算首次就极易陷入**重启循环**。用户自己也说
	//     "或者有重启级别的刷新也行" ⇒ 优先用"不改进程生命周期"的等价手段。
	//   * ✅ 选 B：把**一次性的软重载改成有上限的重试阶梯** —— 5s / 15s / 30s / 60s 各跑一次同一个
	//     幂等软重载（它做的事与"重启 LM 后第一帧"等价：ReloadCapabilities 重读 ItemSupport 快照
	//     ＋ RefreshDeviceCapabilities 重新评估全部可见性/闸门），**一旦判据齐了就提前收手**。
	//   * ✅ 保留原有的"仍判不支持 ⇒ 一次性提示用户重启"兜底（ShowRestartHintOnce）—— 用户明确的
	//     "需要一个重启级出口"由它承载，且**是提示不是自动重启**（用户点"是"才 --restart）。
	//
	// 【防无限循环（三重闩，缺一不可）】
	//   ① 阶梯**长度固定**（RungDelayMs 是常量数组，4 拍跑完必停，无任何"再来一轮"的路径）；
	//   ② 进程内闩 `_armed`（Interlocked，一次运行最多触发一条阶梯）；
	//   ③ 跨进程标记 `MarkerKey`（config，第一拍**执行后立刻**写；后续拍是同一阶梯内的补拍，
	//      在 `_ladderRunning` 为真时才允许越过该键）。
	//   另外每一拍都复用原有的"用户正在交互就延后"护栏，绝不打断操作。
	// ============================================================================================

	/// <summary>
	/// 2026-10-15（firstrun-reload）：软重载的**重试阶梯**（毫秒，相对"确认连上 GCU"那一刻）。
	/// 第 0 拍即原有的 5 秒那一拍（<see cref="WarmupDelayMs" />，行为与改动前逐位一致）；
	/// 之后 15s / 30s / 60s 各补一拍，用于收拾"GCU 上报比 5 秒更晚"的漏报。
	/// ⚠️ 数组长度就是**硬上限**：跑完即止，不存在第二轮（防无限刷新）。
	/// </summary>
	internal static readonly int[] RungDelayMs = new int[4] { WarmupDelayMs, 15000, 30000, 60000 };

	/// <summary>
	/// 2026-10-15（firstrun-reload）：阶梯的**上限拍数**（= <see cref="RungDelayMs" />.Length）。
	/// 抽成属性只为让"跑到第几拍"的日志与断言有单一来源。
	/// </summary>
	internal static int MaxRungs => RungDelayMs.Length;

	/// <summary>进程内一次性闩：同一次运行里断开再连不再重来（也拦住重复调用）。</summary>
	private static int _armed;

	/// <summary>2026-10-15：当前阶梯下标（0 = 第一拍）。仅 UI 线程读写。</summary>
	private static int _rungIndex;

	/// <summary>
	/// 2026-10-15：阶梯"正在进行中"标志 —— 它只用来让**同一阶梯的补拍**越过
	/// <see cref="MarkerKey" /> 那道跨进程门（第一拍执行后就会写下该键）。
	/// ⚠️ 进程内字段，重启后必为 false ⇒ 重启后第一次仍走"全新阶梯"，行为与改动前一致。
	/// </summary>
	private static bool _ladderRunning;

	private static System.Windows.Forms.Timer? _timer;

	private static SettingsForm? _form;

	private static EventHandler? _idleHandler;

	/// <summary>
	/// 入口：<c>Program</c> 在 <c>hw.ConnectionReady</c> 里调用。先过"此刻确实连上 + 本机还没标记过 +
	/// 本进程还没触发过"三道门，然后回 UI 线程起一个 5 秒的**单次**计时器。任何异常一律吞掉
	/// （预热重载失败绝不能影响启动路径）。
	/// </summary>
	internal static void TryTrigger(SettingsForm? form)
	{
		try
		{
			if (form == null || form.IsDisposed || !form.IsHandleCreated)
			{
				return;
			}
			if (Environment.HasShutdownStarted)
			{
				return;
			}
			// 2026-10-15（firstrun-reload）：跨进程门在**阶梯进行中**时放行（第一拍执行后就会写下
			// MarkerKey，若这里不放行，阶梯的第 2~4 拍永远起不来）。阶梯跑完 _ladderRunning=false，
			// 之后的行为与改动前逐位一致：本机标记过就再不动。
			if (AppConfig.Is(MarkerKey) && !_ladderRunning)
			{
				// 2026-10-15（restartgrade）：这一段原先**静默**返回 ⇒ 诊断包里看不出"为什么这次没补拍"。
				// 补一条（WriteLineIfChanged：同一进程内文案不变就只写一次，不会刷屏）：说明这是**按设计**
				// 跳过 —— 本机那次「全新安装后第一次启动」的补拍机会已经用掉了。
				Logger.WriteLineIfChanged("gcu-warmup-done-skip", $"GCU 预热重载：跳过阶梯（AppConfig 键 {MarkerKey}={AppConfig.Get(MarkerKey)} 已存在 ⇒ 按设计跳过，本机只在「全新安装后第一次启动」补拍）");
				return;
			}
			MechrevoHw? hw = Program.hw;
			// ⭐⭐ 2026-10-16（0.291.8.1 ·「首次安装的刷新效果不明显」）：门槛从"必须已连接"放宽为
			// "已连接 或 这台机器装了 GCU 服务（或留过 ItemSupport 快照）" ⇒ 首装时预热阶梯也能启动。
			bool gcuExpected = (hw != null) && (hw.IsConnected || MechrevoHw.IsGcuServiceInstalled);
			{
			}
			{
			}
			if (hw == null || !gcuExpected)
			{
				Logger.WriteLineIfChanged("gcu-warmup-skip", "GCU 预热重载：连接就绪事件已触发但此刻 GCU 未连接 ⇒ 保持现有行为，不重载");
				return;
			}
			if (hw != null && !hw.IsConnected && gcuExpected)
			{
				Logger.WriteLine("GCU 预热重载：GCU 尚未连接，但这台机器装了 GCU 服务 ⇒ 先启动阶梯（后续拍会重新读能力）");
			}
			if (Interlocked.Exchange(ref _armed, 1) != 0)
			{
				return;
			}
			// 2026-10-15（firstrun-reload）：日志改成报"阶梯计划"，让人一眼看出会补几拍、各在何时。
			Logger.WriteLine($"GCU 预热重载：检测到 GCU 已连接（generation={hw.ConnectionGeneration}）⇒ 按阶梯 {string.Join("/", RungDelayMs)} ms 软重载（最多 {MaxRungs} 拍，判据齐了提前收手）");
			form.BeginInvoke((Action)(() => ArmOnUiThread(form)));
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GCU 预热重载：触发失败（已忽略）：" + ex.Message);
		}
	}

	private static void ArmOnUiThread(SettingsForm form)
	{
		try
		{
			if (form.IsDisposed)
			{
				return;
			}
			_form = form;
			// 2026-10-15（firstrun-reload）：进入阶梯 ⇒ 允许后续补拍越过跨进程标记门（见 TryTrigger）。
			_ladderRunning = true;
			_rungIndex = 0;
			StartRungTimer();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GCU 预热重载：计时器启动失败（已忽略）：" + ex.Message);
			Detach();
		}
	}

	/// <summary>
	/// 2026-10-15（firstrun-reload）：按当前阶梯下标起一个**单次**计时器。
	/// 第 0 拍用 <see cref="WarmupDelayMs" />（与改动前一致）；第 n 拍用
	/// <c>RungDelayMs[n] - RungDelayMs[n-1]</c>（因为每拍都是独立单次计时器，间隔要取**差值**）。
	/// </summary>
	private static void StartRungTimer()
	{
		int num = Math.Clamp(_rungIndex, 0, MaxRungs - 1);
		int num2 = ((num == 0) ? RungDelayMs[0] : (RungDelayMs[num] - RungDelayMs[num - 1]));
		if (num2 < 1)
		{
			num2 = 1;
		}
		System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer
		{
			Interval = num2
		};
		timer.Tick += OnTimerTick;
		_timer = timer;
		timer.Start();
	}

	/// <summary>
	/// 2026-10-15（firstrun-reload）：第 <c>_rungIndex</c> 拍的计时器到了。用户正忙 ⇒ 挂到下一次空闲
	/// 再试（日志写"已延后"，**不消耗**拍数）；否则执行这一拍，然后决定是否还有下一拍。
	/// </summary>
	private static void OnTimerTick(object? sender, EventArgs e)
	{
		StopTimer();
		if (_form == null || _form.IsDisposed)
		{
			Detach();
			return;
		}
		if (IsUserInteracting(_form))
		{
			Logger.WriteLine($"GCU 预热重载：第 {_rungIndex + 1}/{MaxRungs} 拍到点但此刻用户正在交互（下拉展开 / 正拖滑块 / 焦点在下拉框或滑块上）⇒ 已延后到下一次空闲");
			AttachIdle();
			return;
		}
		Run(_form);
	}

	private static void OnApplicationIdle(object? sender, EventArgs e)
	{
		if (_form == null || _form.IsDisposed)
		{
			Detach();
			return;
		}
		if (IsUserInteracting(_form))
		{
			return;
		}
		Logger.WriteLine("GCU 预热重载：用户已停止交互 ⇒ 执行延后的软重载");
		Run(_form);
	}

	/// <summary>
	/// 2026-10-15（restartgrade，用户原话：「当前只是刷新界面 好像用处不大 应当是重新检查一次能力
	/// 然后刷新的 应该和启动 LM 差不多」）：向 GCU **主动再要一次全量状态**，与启动路径
	/// （<c>MechrevoHw.ConnectAsync</c>）调用的是**同一个** <see cref="MechrevoHw.RequestInitialStateAsync" />
	/// （一整套 GETSTATUS ＋ <c>Customize/Control GETSUPPORT</c> ＋ <c>BatteryProtection GET</c>），
	/// 然后等 <see cref="RestartGradeWaitMs" /> 让 GCU 把新值写进 ItemSupport 等快照。
	/// <para>⚠️ **这是"重启级"的关键**：只调 <see cref="MechrevoHw.ReloadCapabilities" /> 是
	/// "拿上次上报的数据再算一遍"，晚于首拍才上报的能力位永远补不回来。</para>
	/// <para>⚠️ **全程有界、不发散**：一次 MQTT 批量查询 ＋ 一次固定等待；**不轮询、不重试、不断线**
	/// （断线重连由 <c>MqttReconnectCoordinator</c> 负责，本类不碰它）。老接口后端在
	/// <see cref="MechrevoHw.RequestInitialStateAsync" /> 内部自己分支（不发 MQTT，只读一次档位）。
	/// 失败/超时只记日志并按"未查到"返回，**绝不外抛** ⇒ 调用方照旧继续重读快照。</para>
	/// </summary>
	private static async Task<bool> RequestFreshStateAsync(MechrevoHw hw)
	{
		try
		{
			await hw.RequestInitialStateAsync();
			await Task.Delay(RestartGradeWaitMs);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("gcu-warmup-restartgrade-fail", "GCU 预热重载：重启级状态查询失败（按未查到处理，继续重读快照）：" + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// 真正的软重载。⚠️ 全程只读硬件、只在 UI 线程动可见性/启用态；不切模式、不写 EC。
	/// 顺序：① 先抓"用户正在看的东西"（滚动位置 + 键盘焦点）→ ② 丢弃并重读能力位快照
	/// （<see cref="MechrevoHw.ReloadCapabilities" />）→ ③ <c>RefreshDeviceCapabilities()</c> 重新评估
	/// 全部依赖它的行/闸门 → ④ 还原滚动位置与焦点（按新内容范围夹取）→ ⑤ 写一次性标记 + 写日志
	/// → ⑥ 「仍判不支持」时补一条一次性重启提示（<see cref="ShowRestartHintOnce" />，兜底）。
	/// <para>2026-10-15（firstrun-reload）：本方法现在是**幂等**的，会被"重试阶梯"调用最多
	/// <see cref="MaxRungs" /> 次。每次调用结束后由 <see cref="AdvanceOrFinish" /> 决定
	/// "还要补拍 ⇒ 起下一个计时器" 还是 "阶梯结束（判据齐 / 跑满）"。⚠️ 只有阶梯**结束**的那一拍
	/// 才继续往下走"是否提示重启"；中途补拍一律直接 return（避免连弹 4 次）。判据已齐时那段提示
	/// 自己也不会弹（它还要求 <c>!IsSupported || IsReadOnlyDegraded</c>）。</para>
	/// </summary>
	private static async void Run(SettingsForm form)
	{
		try
		{
			Detach();
			int num = Math.Clamp(_rungIndex, 0, MaxRungs - 1);
			int scrollBefore = form.CaptureDashboardScroll(out int scrollMax);
			IWin32Window? focusBefore = form.CaptureKeyboardFocus();
			MechrevoHw? hw = Program.hw;
			SupportDecision decisionBefore = RuntimeModelSupport.Current();
			bool batteryPanelBefore = form.IsBatteryPanelVisible();
			bool bannerBefore = form.IsUnsupportedModelBannerVisible();
			long revisionBefore = MechrevoDeviceCapabilities.SnapshotRevision;
			// 2026-10-15（restartgrade）：**先要一次新状态，再重读快照** —— 顺序不能反。
			// hw 可能为 null（GCU 刚断开）⇒ 短路跳过，失败一律按"未查到"处理（helper 内已吞异常）。
			bool freshRequested = hw != null && await RequestFreshStateAsync(hw);
			bool capsReplaced = hw?.ReloadCapabilities() ?? false;
			long revisionAfter = MechrevoDeviceCapabilities.SnapshotRevision;
			form.RefreshDeviceCapabilities();
			SupportDecision decisionAfter = RuntimeModelSupport.Current();
			bool batteryPanelAfter = form.IsBatteryPanelVisible();
			bool bannerAfter = form.IsUnsupportedModelBannerVisible();
			form.RestoreDashboardScroll(scrollBefore, scrollMax);
			form.RestoreKeyboardFocus(focusBefore);
			AppConfig.Set(MarkerKey, 1);
			AppConfig.Flush();
			// ⚠️ hw 可能为 null（GCU 刚断开）：这里显式传一个非空对象给判据方法，方法内先查 IsConnected。
			// 用 Program.hw 重新取一次而非复用上面可能已为 null 的局部量，语义与改动前一致（同一个属性）。
			bool flag = hw != null && IsCapabilitySetComplete(hw, decisionAfter, form);
			Logger.WriteLine(
				$"GCU 预热重载：第 {num + 1}/{MaxRungs} 拍（连接后 {RungDelayMs[num]}ms）=> 重载能力（重启级状态查询={freshRequested}，ItemSupport 快照已重读，修订 " +
				$"{revisionBefore}->{revisionAfter}，hw.Capabilities 已替换={capsReplaced}，机型 {decisionBefore.ProjectId}/{decisionBefore.Reason}" +
				$"->{decisionAfter.ProjectId}/{decisionAfter.Reason}，supported={decisionAfter.IsSupported}）=> 已重新评估可见性（电池区 panelBattery=" +
				$"{batteryPanelBefore}->{batteryPanelAfter}，不支持横幅={bannerBefore}->{bannerAfter}，快捷开关族 Enabled={!bannerAfter}）" +
				$"=> 判据齐备={flag}；已写入一次性标记 {MarkerKey}=1（本机不再重复，本阶梯内的补拍不受它拦）");
			// 2026-10-15（firstrun-reload）：先决定"还要不要补拍" —— 还要补拍就起下一个计时器并直接
			// return（把"是否提示重启"留到最后一拍说，避免连弹 4 次）；阶梯结束（判据齐 / 跑满）时
			// 继续往下走原有的重启提示逻辑（判据已齐时它自己不会再弹）。
			if (AdvanceOrFinish(flag))
			{
				return;
			}
			// 2026-10-09（兜底）：软重载跑完之后**仍判不支持** ⇒ 软重载覆盖不到的状态还在，
			// 只有真正重启 LM 才会好。两个判据任一为真都算"仍不支持"：
			//   ① 判定本身仍非 Supported —— decisionAfter 就是 ModelSupport.Determine 的结果
			//      （经 RuntimeModelSupport.Current 的 override 状态机包装），IsSupported == false；
			//   ② unsupportedModel 闸门仍为真 —— form.IsReadOnlyDegraded 由 ApplyUnsupportedModelNotice
			//      写，正是"不支持横幅 + 7 个写入类控件 + 全部快捷开关被灰掉"的那个 flag。
			// ⚠️ 只在这里（Run 的 try 内、标记已写之后）判：软重载自己抛异常就根本走不到这里，
			// 不会拿"重载失败"冒充"重载后仍不支持"。
			if (!decisionAfter.IsSupported || form.IsReadOnlyDegraded)
			{
				ShowRestartHintOnce(form, decisionAfter);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GCU 预热重载：执行失败（已忽略，未写一次性标记）：" + ex.Message);
			// 2026-10-15：重载自己抛异常时**必须**收掉阶梯，否则可能留下一个"永远不再触发"的半开状态
			// （_ladderRunning=true 却没有任何计时器在跑）。这里只复位标志，不重试、不弹框。
			_ladderRunning = false;
		}
		finally
		{
			Detach();
		}
	}

	/// <summary>
	/// 2026-10-15（firstrun-reload）：判断"能力位是不是已经齐了"，作为阶梯**提前收手**的判据。
	/// ⚠️ 这是"够不够好"的**充分**判据而非必要判据 —— 宁可判 false 多跑一两拍（幂等、只读、
	/// 有硬上限），也不要在没齐时提前收手。任一条不满足即视为未齐：
	/// <list type="bullet">
	/// <item>机型身份已确立（<c>decision.IsSupported</c>）且未处于只读降级；</item>
	/// <item>GCU 的连接期回读标志已到位 —— <c>SettingStatusSeen</c>（<c>Setting/Status</c> 首次上报）
	/// 与 <c>DeviceSwitchStatusSeen</c>（设备开关状态首次上报）是"GCU 已把常规状态铺完"的两个代表位
	/// （⚠️ 这两个属性都在 <see cref="MechrevoHw" /> 上确证存在，见 MechrevoHw.cs:408/412）；</item>
	/// <item>屏幕区的两个能力位至少有其一（<c>DisplayRefresh</c> / <c>ScreenBrightnessSeen</c>）——
	/// 这正是用户这轮报的"屏幕区显示不全"相关的那两个。</item>
	/// </list>
	/// 全部 try/catch：判不出来按"未齐"处理（保守，最多多跑一拍）。
	/// </summary>
	private static bool IsCapabilitySetComplete(MechrevoHw hw, SupportDecision decisionAfter, SettingsForm form)
	{
		try
		{
			{
			}
			{
			}
			{
				return false;
			}
			if (!decisionAfter.IsSupported || form.IsReadOnlyDegraded)
			{
				return false;
			}
			if (!hw.SettingStatusSeen || !hw.DeviceSwitchStatusSeen)
			{
				return false;
			}
			return (hw.Capabilities?.DisplayRefresh ?? false) || hw.ScreenBrightnessSeen;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GCU 预热重载：判据齐备检查失败（按未齐处理）：" + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// 2026-10-15（firstrun-reload）：一拍跑完后的**唯一**出口决策。
	/// 返回 <c>true</c> ＝"**阶梯还要继续**（已起好下一个计时器）"⇒ 调用方直接 return，
	/// 本轮**不**弹重启提示（话留到最后一拍说，避免连弹 4 次）；
	/// 返回 <c>false</c> ＝"阶梯已结束"⇒ 调用方继续往下走"是否提示重启"的那段逻辑
	/// （判据已齐时那段自己不会再弹，因为它还要求 <c>!IsSupported || IsReadOnlyDegraded</c>）。
	/// <para>⚠️ 防无限刷新：只有"判据已齐"或"<c>_rungIndex</c> 已到最后一拍"能结束阶梯，
	/// 没有任何"再来一轮"的路径；<c>_ladderRunning</c> 在结束的两条路径上都会复位。</para>
	/// </summary>
	private static bool AdvanceOrFinish(bool complete)
	{
		if (complete)
		{
			_ladderRunning = false;
			Logger.WriteLine($"GCU 预热重载：第 {_rungIndex + 1}/{MaxRungs} 拍后判据已齐 ⇒ 收手，不再补拍");
			return false;
		}
		if (_rungIndex >= MaxRungs - 1)
		{
			_ladderRunning = false;
			Logger.WriteLine($"GCU 预热重载：已跑满 {MaxRungs} 拍仍未齐 ⇒ 结束阶梯（不再补拍，交由重启提示兜底）");
			return false;
		}
		_rungIndex++;
		Logger.WriteLine($"GCU 预热重载：判据未齐 ⇒ 追加第 {_rungIndex + 1}/{MaxRungs} 拍（连接后 {RungDelayMs[_rungIndex]}ms）");
		try
		{
			StartRungTimer();
			return true;
		}
		catch (Exception ex)
		{
			_ladderRunning = false;
			Logger.WriteLine("GCU 预热重载：补拍计时器启动失败（结束阶梯）：" + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// 软重载后仍判不支持时的**应用内一次性提示**（兜底之一，另一条在安装器里）。
	/// 触发条件（调用点已判，这里只兜重复）：<c>!decisionAfter.IsSupported || form.IsReadOnlyDegraded</c>
	/// ＝ 软重载没能救回来。文案「部分功能可能需要重启 L-Mechrevo 后才会出现」＋「立即重启 LM」。
	/// ⚠️ **只提示一次**：跨进程键 <see cref="RestartHintMarkerKey" />，先写后弹 —— 即使弹窗本身抛异常、
	/// 或用户直接关掉窗口，下次启动也不会再弹（该键与软重载标记互相独立）。
	/// ⚠️ 用户选「是」＝按语言切换那条同款路径重起进程（<c>--restart</c>）；选「否」不强制、不改任何状态。
	/// ⚠️ 全程 try/catch，提示失败只记日志，绝不影响已经完成的软重载。
	/// </summary>
	private static void ShowRestartHintOnce(SettingsForm form, SupportDecision decisionAfter)
	{
		try
		{
			if (AppConfig.Is(RestartHintMarkerKey))
			{
				return;
			}
			AppConfig.Set(RestartHintMarkerKey, 1);
			AppConfig.Flush();
			Logger.WriteLine(
				"GCU 预热重载：软重载后仍判不支持（supported=" + decisionAfter.IsSupported + "，机型 " +
				decisionAfter.ProjectId + "/" + decisionAfter.Reason + "，只读降级=" + form.IsReadOnlyDegraded +
				$"）⇒ 一次性提示「部分功能可能需要重启 L-Mechrevo」（已写 {RestartHintMarkerKey}=1，本机只提示这一次）");
			DialogResult answer = MessageBox.Show(form, Strings.GcuWarmupRestartHint, Strings.GcuWarmupRestartHintTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
			if (answer == DialogResult.Yes)
			{
				Logger.WriteLine("GCU 预热重载：用户选择立即重启 L-Mechrevo（--restart）");
				Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? Application.ExecutablePath, "--restart")
				{
					UseShellExecute = true
				});
				Environment.Exit(0);
			}
			else
			{
				Logger.WriteLine("GCU 预热重载：用户选择稍后自行重启，不强制");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GCU 预热重载：重启提示失败（已忽略）：" + ex.Message);
		}
	}

	/// <summary>
	/// 交互护栏（用户明确要求：不要在用户正在操作时刷新）—— 任一为真就延后：
	/// ① 任一已打开窗里焦点在 <c>ComboBox</c>/<c>RSlider</c> 上；② 任一 <c>ComboBox.DroppedDown</c>；
	/// ③ 鼠标左键按住且落在某个 <c>RSlider</c> 上（拖动中）。
	/// ⚠️ 只是"要不要现在动布局"，不改变任何控件状态；判定不出（拿不到焦点）时按"不忙"处理。
	/// </summary>
	private static bool IsUserInteracting(SettingsForm form)
	{
		try
		{
			foreach (Form openForm in Application.OpenForms)
			{
				if (openForm.IsDisposed)
				{
					continue;
				}
				Control? focused = openForm.ActiveControl;
				while (focused is ContainerControl container && container.ActiveControl != null)
				{
					focused = container.ActiveControl;
				}
				if (focused is ComboBox || focused is RSlider)
				{
					return true;
				}
			}
			return ControlHasDropDownOpen(form) || SliderIsBeingDragged(form);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GCU 预热重载：交互判定失败（按不忙处理）：" + ex.Message);
			return false;
		}
	}

	private static bool ControlHasDropDownOpen(Control root)
	{
		if (root is ComboBox combo && combo.DroppedDown)
		{
			return true;
		}
		foreach (Control child in root.Controls)
		{
			if (ControlHasDropDownOpen(child))
			{
				return true;
			}
		}
		return false;
	}

	private static bool SliderIsBeingDragged(Control root)
	{
		if (root is RSlider slider && (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left && slider.Capture)
		{
			return true;
		}
		foreach (Control child in root.Controls)
		{
			if (SliderIsBeingDragged(child))
			{
				return true;
			}
		}
		return false;
	}

	private static void AttachIdle()
	{
		if (_idleHandler == null)
		{
			_idleHandler = OnApplicationIdle;
			Application.Idle += _idleHandler;
		}
	}

	private static void StopTimer()
	{
		System.Windows.Forms.Timer? timer = _timer;
		_timer = null;
		if (timer != null)
		{
			timer.Stop();
			timer.Tick -= OnTimerTick;
			timer.Dispose();
		}
	}

	private static void Detach()
	{
		StopTimer();
		if (_idleHandler != null)
		{
			Application.Idle -= _idleHandler;
			_idleHandler = null;
		}
	}
}
