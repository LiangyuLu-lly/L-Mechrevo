using System;
using MechrevoLite.Properties;using System.Diagnostics;
using MechrevoLite.Properties;using System.Threading.Tasks;
using MechrevoLite.Properties;using MechrevoLite.Hardware;
using MechrevoLite.Properties;using MechrevoLite.Helpers;
using MechrevoLite.Properties;
namespace MechrevoLite.Battery;

public static class BatteryControl
{
	// 2026-10-08（充电上限开关）：charge_full 只是 charge_limit 的派生显示位，开机初值也按
	// charge_limit 归一，不再从 config 的 charge_full 读。
	// 旧行为：用户/三档把 charge_limit 设成 80 而 charge_full 还留着 1（本机实测就是这样）时，
	// 开机与"显示器唤醒/合盖/休眠恢复"这些 AutoBattery() 路径会走 SetBatteryLimitFull()
	// 把 EC 0x7B9 写回 100 —— 静默撤销用户的限充。现在上限只认 charge_limit。
	private static bool _chargeFull = AppConfig.Get("charge_limit", 100) >= 100;

	private static readonly UserGestureLease _userLimitLease = new UserGestureLease();

	public static bool chargeFull
	{
		get
		{
			return _chargeFull;
		}
		set
		{
			AppConfig.Set("charge_full", value ? 1 : 0);
			_chargeFull = value;
		}
	}

	internal static bool CaptureChargeLimitGesture()
	{
		return _userLimitLease.Capture();
	}

	internal static void ResetChargeLimitGesture()
	{
		_userLimitLease.Reset();
	}

	public static bool ApplyChargeLimitFromUserGesture(int limit)
	{
		if (!_userLimitLease.IsActive)
		{
			Logger.WriteLine($"EC 充电上限写入被拒绝：没有真实用户手势凭证（请求 {limit}%）");
			RestoreChargeLimitDisplay();
			return false;
		}
		_userLimitLease.Consume();
		return SetBatteryChargeLimit(limit);
	}

	public static void ToggleBatteryLimitFull()
	{
		if (chargeFull)
		{
			SetBatteryChargeLimit();
		}
		else
		{
			SetBatteryLimitFull();
		}
	}

	public static void SetBatteryLimitFull()
	{
		chargeFull = true;
		if (EcChargeLimit.IsAvailableOnThisMachine())
		{
			SetBatteryChargeLimit(100);
		}
		Program.settingsForm.VisualiseBatteryFull();
	}

	public static void AutoBattery(bool init = false)
	{
		// 2026-10-10（限充门控 · 用户原话「1补 2不动 3改完」「另能不能想办法修了」）：本方法是**所有自动路径**
		// 的唯一出口 —— 开机 Program.SetAutoModes(init:true)、会话结束 SystemEvents_SessionEnding，
		// 以及 SettingsForm.HandlePowerBroadcast 的挂起恢复/合盖/显示器点亮三处。
		// 改动前它们**无条件下发 EC**：按 config.charge_limit 直写 0x7B9（甚至写 0=不限制），即使用户两个
		// 开关都关（EC 直写方式并未生效、充电上限由 GCU 官方路径负责）也会把 GCU 侧刚确立的值覆写掉。
		// ⇒ 门控判据**复用既有单一真源** BatteryControl.ReestablishArmed()（= charge_limit_lock==1 ||
		// charge_limit_slider==1）—— 与 EC 侧重确立保护 BatteryControl.EnsureChargeLimitHeld、
		// ChargeLimitMonitor.Tick 的周期兜底用的是同一个谓词（界面侧 ChargeLimitEcPathActive 是它的等价写法），
		// ⛔ 不新造第三套判据。两开关都关 ⇒ **一个字节都不写 EC**（只记一行日志）。
		// ⚠️ 只门控"写 EC"这一段：下面两条分支的记忆副作用本就无效/同值 —— SetBatteryLimitFull 只写
		// charge_full（自 2026-10-08 起已无任何读取方，见本文件顶部注释），SetBatteryChargeLimit 的
		// CommitChargeLimit 把 charge_limit 写回它刚读到的同一个值 ⇒ ⛔ 未改变记忆行为。
		if (!ReestablishArmed())
		{
			Logger.WriteLine($"限充门控：自动路径跳过写 EC（{LockConfigKey} 与 {SliderConfigKey} 都是关 ⇒ EC 直写方式未生效，充电上限由 GCU 官方路径负责；⛔ 本次一个字节都不写 EC）");
			return;
		}
		if (chargeFull && !init)
		{
			SetBatteryLimitFull();
		}
		else
		{
			SetBatteryChargeLimit();
		}
	}

	/// <summary>
	/// 2026-10-08（充电上限 UI 返工）：开关状态与「当前选中档位」必须分开记忆。
	/// 原因：长效档（100%）与「不限制」在 EC 上是同一个编码（0x7B9 写 0），
	/// 只靠 EC 回读分不出「开关关」与「开关开 + 长效档」。这两个键都是 charge_limit
	/// 相关的记忆位，不动 config 其它键。
	/// </summary>
	internal const string LockConfigKey = "charge_limit_lock";

	internal const string ModeConfigKey = "charge_limit_mode";

	/// <summary>三档上限：长效 100（＝不限制）/ 平衡 80 / 工作站 60。</summary>
	private static readonly int[] ModePercents = new int[3] { 100, 80, 60 };

	private static int NormalizeMode(int percent)
	{
		foreach (int candidate in ModePercents)
		{
			if (candidate == percent)
			{
				return percent;
			}
		}
		return 80;
	}

	/// <summary>开关（EC 限充／实验）是否打开 —— 记忆值，EC 回读在 SyncChargeLimitSwitch 里覆盖它。</summary>
	public static bool LockEnabled()
	{
		return AppConfig.Get(LockConfigKey, 0) == 1;
	}

	public static void SetLockEnabled(bool on)
	{
		AppConfig.Set(LockConfigKey, on ? 1 : 0);
	}

	/// <summary>记忆的档位（不看 EC）—— 用于判断「本该存在限充」。</summary>
	public static int StoredModePercent()
	{
		return NormalizeMode(AppConfig.Get(ModeConfigKey, 80));
	}

	public static void SetSelectedModePercent(int percent)
	{
		if (NormalizeMode(percent) == percent)
		{
			AppConfig.Set(ModeConfigKey, percent);
		}
	}

	/// <summary>
	/// 当前选中档位（UI 高亮 / 开开关时写哪个上限）。
	/// EC 回读是权威：EC 上真的存在限充（40–99）时档位就是那个值 —— 保留上一轮
	/// 「高亮按 EC 回读、不优先信 GCU」的修复；EC 没有限充（0/100）时才用记忆值（缺省 80），
	/// 这样开关关闭时仍然记得用户选的是哪一档。
	/// </summary>
	public static int SelectedModePercent()
	{
		int num = ResolveDisplayLimitPercent();
		if (EcChargeLimit.IsSupportedLimit(num) && num < 100)
		{
			return num;
		}
		return StoredModePercent();
	}

	/// <summary>
	/// 2026-10-08（充电上限 UI 返工）：开关从「关」打到「开」时使用的上限 = 当前选中档位。
	/// 旧实现从 config.charge_limit 猜（40–99 才算数、否则 80），开关关闭时会把档位丢掉。
	/// </summary>
	public static int PreferredLimitPercent()
	{
		return SelectedModePercent();
	}

	public static bool SetBatteryChargeLimit(int setLimit = -1)
	{
		if (setLimit < 0)
		{
			setLimit = AppConfig.Get("charge_limit");
		}
		if (!EcChargeLimit.IsSupportedLimit(setLimit))
		{
			Logger.WriteLine($"充电上限 {setLimit}% 不在支持区间（{40}–{100}%），未写入");
			RestoreChargeLimitDisplay();
			return false;
		}
		// 2026-10-15（隐藏规则 B）：改用 IsEcDirectWriteAllowed() —— 10/16/20/30 系上 EC 直写整体禁用
		// （用户原话「用这个可能损坏硬件…只保留GCU路」）。⚠️ 与既有「机型未验证」同一分支语义：只记日志 +
		// 恢复显示，⛔ 不弹窗（老机型本来就走 GCU 路，弹窗会是纯噪音）。
		if (!EcChargeLimit.IsEcDirectWriteAllowed())
		{
			Logger.WriteLine($"EC 充电上限在本机型未验证或已按代际禁用（10/16/20/30 系），忽略 {setLimit}% 请求（⛔ 未写 EC，GCU 路不受影响）");
			RestoreChargeLimitDisplay();
			return false;
		}
		Task.Run(() =>
		{
			// 2026-10-15（写入前门控预读 · 用户批准的修法③）：TrySet 现在会区分两种失败 ——
			//   · refusedByFirmware=true ⇒ 按证据判定**本机固件不支持**（门控 0x7C3 非 4/5 + ROMID 非 4/5 +
			//     存储区无内容）⇒ 给用户**可见文案**（否则就是用户抱怨的「静默无效」）；
			//   · refusedByFirmware=false ⇒ 与改前逐字一致的普通写失败（沿用 ChargeLimitFailedRestored）。
			bool refusedByFirmware;
			if (EcChargeLimit.TrySet(setLimit, out var appliedPercent, out refusedByFirmware))
			{
				Logger.WriteLine($"EC 充电阈值已确认：上限 {appliedPercent}%（0x{1977:X3} 已写并回读确认，含 bit7 掩码）；复充下限 0x{2000:X3} 按上游口径**不再写入**、交由固件推导（期望值 {EcChargeLimit.LowerValueFor(appliedPercent)}%，只读比对见紧随的观察日志）");
				CommitChargeLimit(appliedPercent);
				// 2026-10-10（限充真效果判据 · 按上游口径）：上面只证明"寄存器写进去了"（旧判据）。
				// 真效果 = 门控还原完成后再等 7s，读 0x0742 bit2=1。异步执行，不阻塞 UI，也不改任何状态。
				ChargeLimitEffectEvidence.Begin(appliedPercent);
			}
			else
			{
				int num = EcChargeLimit.ReadPercent();
				if (num == setLimit)
				{
					Logger.WriteLine($"EC 充电上限回读确认延迟：请求 {setLimit}%，实际阈值已是 {num}%，按成功收尾");
					CommitChargeLimit(num);
					ChargeLimitEffectEvidence.Begin(num);
				}
				else if (refusedByFirmware)
				{
					// 明确告知「本机固件不支持」，而不是笼统的「设置失败」—— 这正是用户点名的「静默无效」的修法。
					Logger.WriteLine($"EC 充电上限未写入：按证据判定本机 EC 固件不支持充电上限（请求 {setLimit}%，⛔ 未盲写 0x1977）");
					RestoreChargeLimitDisplay();
					ToastForm.ShowFailure(Strings.ChargeLimitFirmwareUnsupported);
				}
				else
				{
					Logger.WriteLine($"EC 充电上限写入未确认：请求 {setLimit}%，保持原值");
					RestoreChargeLimitDisplay();
					ToastForm.ShowFailure(Strings.ChargeLimitFailedRestored);
				}
			}
		});
		return true;
	}

	private static void CommitChargeLimit(int limit)
	{
		AppConfig.Set("charge_limit", limit);
		chargeFull = limit >= 100;
		// 2026-10-08：写入确认后统一在这里 Arm 观察器，避免再多一条通道各自 Arm。
		ChargeLimitMonitor.Arm(limit);
		SettingsForm form = Program.settingsForm;
		if (form == null || form.IsDisposed)
		{
			return;
		}
		if (form.InvokeRequired)
		{
			form.Invoke(() =>
			{
				form.VisualiseBattery(limit);
			});
		}
		else
		{
			form.VisualiseBattery(limit);
		}
	}

	// 2026-10-10（与 GCU 的次序 · GCU 启动/档位变更之后重放一次限充）：
	// 竞争写者事实：GCU 每次启动会写官方档 `Enable BatteryProtection2`（EC 0x07A6），比 LM 晚约 3s ——
	// 可能把 LM 刚写好的限充顶掉。确认时机：LM 只在收到 GCU 上报的 `System/BatteryProtection`
	// （HealthProtectionStatus）时才会触发本方法，而那条上报发生在 GCU 自己写完 0x07A6 **之后**
	// ⇒ 天然是"GCU 动作之后"。挂载点见 Program.cs 的 hw.BatteryProtectionChanged（LM 启动后 GCU 首次
	// 上报必然触发一次，因为 BatteryProtection 初值是 -1；档位变更时也会触发）。
	// ⚠️ 只在「记忆里开关是开 + 档位非长效 + EC 回读确实不一致」时才重写 ⇒ 不打断 GCU、不无谓写 EC。
	// ⚠️ 全程不阻塞调用线程（内部走 Task.Run）。
	// 2026-10-10（重确立保护 · 用户拍板「EC 限充开启（含实验开关开/滑块可用）时必须保证不被覆写」）：
	// 在上一版"事件重放"的基础上补**有界周期兜底**，两条入口共用下面这一个方法（⛔ 不造第二套写通道）：
	//   · 事件入口 = Program.cs 的 hw.BatteryProtectionChanged（GCU 写完 0x07A6 之后）⇒ periodic:false；
	//   · 周期入口 = ChargeLimitMonitor.Tick 的 10s 心跳（主窗口既有定时器，⛔ 不新增定时器/轮询）⇒ periodic:true。
	// 选 60s 节流（而不是更短）：固件每秒都在跑控制循环、0x0742 每秒更新，<30s 的反复重写会与固件/GCU
	// 抢 EC 总线且无实际收益；10s 心跳 + 60s 写入节流 ⇒ 外部覆写最迟 70s 内被纠正，且任何来源最多每分钟 1 次。
	private static DateTime _lastChargeLimitReestablish = DateTime.MinValue;

	internal static readonly TimeSpan ExternalReplayMinimumInterval = TimeSpan.FromSeconds(60L);

	// 周期入口一致时的**低频心跳日志**节流：回读一致就不写 EC（用户要求"只在必要时写"），
	// 但也不能每 10s 刷一行 —— 每 10 分钟留一行"保护仍在跑且 EC 未被改"的证据。
	private static DateTime _lastHeldHeartbeatLog = DateTime.MinValue;

	internal static readonly TimeSpan HeldHeartbeatLogInterval = TimeSpan.FromMinutes(10L);

	/// <summary>无极限充（实验性）滑块的显隐键 —— ⚠️ 必须与 SettingsForm 的 private const 字面量一致。</summary>
	internal const string SliderConfigKey = "charge_limit_slider";

	/// <summary>
	/// 重确立保护是否**武装**（这套保护只在该功能生效时运行）：EC 限充开关记忆为开
	/// **或** 无极限充（实验性）滑块开着。
	/// <para>⚠️ 洞 2（界面侧代理只读定位）：Gen50 原本没有 EC 限充开关 ⇒ <c>charge_limit_lock</c> 默认 0，
	/// 旧实现只在 <see cref="LockEnabled"/> 为真时才重放 ⇒ 这种机器上 GCU 写 0x07A6 / 其它写者动了 0x07B9
	/// 之后，滑块的限充根本不会被重确立。</para>
	/// <para>两者都关 ⇒ 整套保护一次 EC 写都不会发生（功能关闭 = 零行为变化）。</para>
	/// </summary>
	internal static bool ReestablishArmed()
	{
		return LockEnabled() || AppConfig.Is(SliderConfigKey);
	}

	/// <summary>
	/// 重确立的**权威目标值**。
	/// <para>⚠️ 洞 1（界面侧代理只读定位）：滑块模式下滑块取值就存在 <c>charge_limit</c>，而
	/// <c>charge_limit_lock</c> 只是被**置灰**、值可能仍是 1 ⇒ 旧实现拿三档记忆 <c>charge_limit_mode</c>
	/// （80）去重放，会把用户刚拖的滑块值（如 73%）覆写回旧档位。故：滑块模式 ⇒ 滑块值权威；
	/// 非滑块模式 ⇒ 三档记忆权威。</para>
	/// </summary>
	internal static int ReestablishTargetPercent()
	{
		return AppConfig.Is(SliderConfigKey) ? AppConfig.Get("charge_limit", 100) : StoredModePercent();
	}

	/// <summary>日志用：当前权威值来自哪一路（滑块 / 三档）。</summary>
	private static string ReestablishSourceName()
	{
		return AppConfig.Is(SliderConfigKey) ? $"无极限充滑块（{SliderConfigKey}=1 ⇒ charge_limit 权威）" : "三档记忆（charge_limit_mode）";
	}

	/// <summary>
	/// EC 限充**重确立**保护 —— 限充生效期间，外部写入者（官方三档经 GCU/MQTT 的 0x07A6 路径、
	/// GCU 启动/周期动作、任何重写 0x07B9 的东西）不得**静默**覆盖我们的上限。本方法是该功能
	/// **唯一的写 EC 入口**（事件 + 周期两条入口共用）。
	///
	/// 触发条件（全部满足才可能写）：① 机型判定可用；② <see cref="ReestablishArmed"/>（EC 限充开关
	/// 记忆为开 **或** 无极限充滑块开着）；③ 权威档位 ∈ [40,99]（"不限"不重确立）；
	/// ④ EC 0x07B9 **掩掉 bit7 后**的回读 ≠ 权威档位；⑤ 距上次重写 ≥
	/// <see cref="ExternalReplayMinimumInterval"/>（60s，事件/周期共用同一节流 ⇒ 不会互写）；
	/// ⑥ <paramref name="periodic"/> 时还要求回读 <c>&gt; 0</c>（真的读到一个不同的值 = 外部覆写的正面证据；
	/// EC 读不到时不周期空写，交给事件入口）。
	/// 回读一致 ⇒ ⛔ 不写 EC（周期入口只按 <see cref="HeldHeartbeatLogInterval"/> 低频记一行）。
	/// </summary>
	internal static void EnsureChargeLimitHeld(string reason, bool periodic = false)
	{
		try
		{
			// 2026-10-15（隐藏规则 B）：周期/事件重确立同样受代际门控约束（老机型 ⛔ 不写 EC）。
			if (!EcChargeLimit.IsEcDirectWriteAllowed())
			{
				return;
			}
			if (!ReestablishArmed())
			{
				if (!periodic)
				{
					Logger.WriteLine($"限充重确立（{reason}）：EC 限充开关与无极限充滑块都是关，跳过（不写 EC）");
				}
				return;
			}
			int limit = ReestablishTargetPercent();
			if (!EcChargeLimit.IsSupportedLimit(limit) || limit >= 100)
			{
				if (!periodic)
				{
					Logger.WriteLine($"限充重确立（{reason}）：权威档位 {limit}% 是长效/不支持（来源：{ReestablishSourceName()}），跳过（不写 EC）");
				}
				return;
			}
			DateTime now = DateTime.Now;
			int ecReadback = EcChargeLimit.ReadPercent();
			if (ecReadback == limit)
			{
				// 一致 ⇒ ⛔ 不写 EC（收敛：重写后下一次检查必然一致，不会来回抖动）。
				// 事件入口照旧记一行；周期入口只按 10 分钟节流留心跳证据。
				if (!periodic)
				{
					Logger.WriteLine($"限充重确立（{reason}）：EC 回读 {ecReadback}%（掩码后）与权威值一致（{ReestablishSourceName()}），无需重写");
				}
				else if (now - _lastHeldHeartbeatLog >= HeldHeartbeatLogInterval)
				{
					_lastHeldHeartbeatLog = now;
					Logger.WriteLine($"限充重确立（{reason}）：EC 0x{EcChargeLimit.UpperRegister:X3} 回读 {ecReadback}%（掩码后）与权威值一致，未写 EC（心跳，每 {HeldHeartbeatLogInterval.TotalMinutes:0} 分钟一行；来源：{ReestablishSourceName()}）");
				}
				return;
			}
			if (periodic && ecReadback <= 0)
			{
				// 周期入口只在**确实读到**一个不同的值时写；EC 不可读（-1）时不由周期兜底空写。
				if (now - _lastHeldHeartbeatLog >= HeldHeartbeatLogInterval)
				{
					_lastHeldHeartbeatLog = now;
					Logger.WriteLine($"限充重确立（{reason}）：EC 0x{EcChargeLimit.UpperRegister:X3} 不可读，周期入口不写（交给事件入口），仅记录");
				}
				return;
			}
			if (now - _lastChargeLimitReestablish < ExternalReplayMinimumInterval)
			{
				if (!periodic)
				{
					Logger.WriteLine($"限充重确立（{reason}）：距上次重写不足 {ExternalReplayMinimumInterval.TotalSeconds:0}s，跳过");
				}
				return;
			}
			_lastChargeLimitReestablish = now;
			Logger.WriteLine($"限充重确立（{reason}）：EC 回读 {(ecReadback < 0 ? "不可读" : ecReadback + "%")} ≠ 权威值 {limit}%（来源：{ReestablishSourceName()}；外部覆写证据），按权威值重写一次");
			SetBatteryChargeLimit(limit);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("限充重确立失败（不影响其它功能）：" + ex.Message);
		}
	}

	/// <summary>事件入口（GCU 上报电池保护状态之后）—— 与周期入口共用 <see cref="EnsureChargeLimitHeld"/>。</summary>
	public static void ReplayChargeLimitAfterExternalWriter(string reason)
	{
		EnsureChargeLimitHeld(reason);
	}

	public static int ResolveDisplayLimitPercent()
	{
		// 2026-10-15（隐藏规则 B）：代际禁用时**不**用 EC 回读当权威值（那可能是上次遗留的旧值）⇒ 直接回落到
		// config 记忆（= 我们最后交给 GCU 的档位），与「只保留 GCU 路」一致。非老机型上判据与原样等价。
		if (EcChargeLimit.IsEcDirectWriteAllowed())
		{
			int num = EcChargeLimit.ReadPercent();
			if (EcChargeLimit.IsSupportedLimit(num))
			{
				return num;
			}
		}
		int num2 = AppConfig.Get("charge_limit");
		if (!EcChargeLimit.IsSupportedLimit(num2))
		{
			return -1;
		}
		return num2;
	}

	private static void RestoreChargeLimitDisplay()
	{
		int resolved = ResolveDisplayLimitPercent();
		bool flag = EcChargeLimit.IsSupportedLimit(resolved);
		SettingsForm form = Program.settingsForm;
		if (form != null && !form.IsDisposed)
		{
			Action action = (flag ? ((Action)(() =>
			{
				form.VisualiseBattery(resolved);
			})) : new Action(form.VisualiseBatteryUnknown));
			if (form.InvokeRequired)
			{
				form.Invoke(action);
			}
			else
			{
				action();
			}
		}
	}

	public static void BatteryReport()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		try
		{
			using Process process = new Process();
			process.StartInfo.WorkingDirectory = folderPath;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.StartInfo.FileName = "powershell";
			process.StartInfo.Arguments = "powercfg /batteryreport; explorer battery-report.html";
			process.Start();
		}
		catch (Exception ex)
		{
			Logger.WriteLine(ex.Message);
		}
	}
}
