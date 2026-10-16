using System;
using MechrevoLite.Hardware;
using MechrevoLite.Mode;

namespace MechrevoLite.Battery;

internal static class ChargeLimitMonitor
{
	private static readonly ChargeLimitEvidence Evidence = new ChargeLimitEvidence();

	private static readonly object Gate = new object();

	private static DateTime _lastTick = DateTime.MinValue;

	private static int _activeLimit = -1;

	private static int _lastLoggedReadback = -1;

	internal static Func<(bool OnAc, bool Charging, int Percent)?>? ChargeStateOverride { get; set; }

	internal static event Action<ChargeLimitVerdict>? VerdictChanged;

	public static void Arm(int limit)
	{
		lock (Gate)
		{
			_activeLimit = limit;
			_lastLoggedReadback = -1;
			Evidence.Reset();
		}
	}

	public static void Tick(DateTime now)
	{
		int activeLimit;
		ChargeLimitVerdict? chargeLimitVerdict;
		lock (Gate)
		{
			if (now - _lastTick < TimeSpan.FromSeconds(10L))
			{
				return;
			}
			_lastTick = now;
			activeLimit = _activeLimit;
			if (activeLimit < 40 || activeLimit >= 100)
			{
				// 2026-10-08（限充开关）：不再只依赖 UI 手势 Arm —— 开机/重连后也从
				// config.charge_limit 恢复观察对象，否则用户设了上限却永远收不到判定。
				activeLimit = AppConfig.Get("charge_limit", 100);
			}
			bool flag = ((activeLimit < 40 || activeLimit >= 100) ? true : false);
			if (flag || EcChargeLimit.Verdict == ChargeLimitVerdict.Ineffective)
			{
				return;
			}
			Func<(bool, bool, int)?> chargeStateOverride = ChargeStateOverride;
			(bool, bool, int)? tuple = ((chargeStateOverride != null) ? chargeStateOverride() : PowerNative.GetChargeState());
			if (!tuple.HasValue)
			{
				return;
			}
			(bool, bool, int) valueOrDefault = tuple.GetValueOrDefault();
			int ecReadback = EcChargeLimit.ReadPercent();
			if (ecReadback > 0 && ecReadback != activeLimit && ecReadback != _lastLoggedReadback)
			{
				_lastLoggedReadback = ecReadback;
				Logger.WriteLine($"充电上限回读不一致：命令 {activeLimit}%，EC 0x{1977:X3} 读到 {ecReadback}%（此处只记录；重确立由 BatteryControl.EnsureChargeLimitHeld 按 60s 节流处理，不判无效）");
			}
			// 2026-10-10（重确立保护 · 有界周期兜底 · 用户拍板「EC 限充开启时必须保证不被覆写」）：
			// 借主窗口这条既有的 10s 心跳做周期检查（⛔ 不新增定时器/轮询）—— 事件入口只跟在 GCU 上报之后，
			// 而 GCU 的**周期动作**或任何别的写者可能在两次事件之间覆写 0x7B9。回读一致 ⇒ 内部直接返回、
			// ⛔ 不写 EC（周期入口每 10 分钟才留一行心跳）；只有"读到 ≠ 记忆"才可能重写，且与事件入口
			// 共用同一个 60s 节流（任何来源的覆写最多每 60s 被纠正一次 ⇒ 不会来回互写）。
			// 门控：① 本方法上面的 activeLimit 门已保证"限充正在生效 / 档位非不限"；② 这里再要求
			// ReestablishArmed（EC 限充开关记忆为开 **或** 无极限充滑块开着 —— 后者是"洞 2"的修法：
			// Gen50 没有 EC 限充开关、lock 默认 0，但滑块开着同样必须重确立）。
			// ⇒ 功能关闭（两者都关，或档位＝不限时上面的门直接 return）时，这一行一次都执行不到（零行为变化）。
			if (BatteryControl.ReestablishArmed())
			{
				BatteryControl.EnsureChargeLimitHeld("周期兜底（10s 心跳，写入节流 60s）", periodic: true);
			}
			chargeLimitVerdict = Evidence.Observe(activeLimit, valueOrDefault.Item1, valueOrDefault.Item2, valueOrDefault.Item3, ecReadback, now);
			if (!chargeLimitVerdict.HasValue || chargeLimitVerdict == EcChargeLimit.Verdict)
			{
				return;
			}
			Evidence.Reset();
		}
		EcChargeLimit.RecordVerdict(chargeLimitVerdict.Value);
		Logger.WriteLine($"充电上限效果判定：{chargeLimitVerdict.Value}（上限 {activeLimit}%）");
		if (chargeLimitVerdict == ChargeLimitVerdict.Ineffective)
		{
			// 2026-10-08：走到这里只可能是「EC 完全读不到」（见 ChargeLimitEvidence）。
			// 再加一道保险：只要 EC 回读仍等于命令上限，就绝不写 100%，避免把真实生效的限充撤销。
			int num = EcChargeLimit.ReadPercent();
			if (num == activeLimit)
			{
				Logger.WriteLine($"充电上限回读确认 {num}% 仍在，取消自动回退满充");
			}
			else
			{
				bool value = EcChargeLimit.TrySet(100, out var _);
				AppConfig.Set("charge_limit", 100);
				AppConfig.Flush();
				lock (Gate)
				{
					_activeLimit = -1;
				}
				Logger.WriteLine($"充电上限判定无效，已恢复满充：{value}");
			}
		}
		try
		{
			VerdictChanged?.Invoke(chargeLimitVerdict.Value);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Charge-limit verdict handler failed: " + ex.Message);
		}
	}

	internal static void ResetForTests()
	{
		lock (Gate)
		{
			_activeLimit = -1;
			_lastTick = DateTime.MinValue;
			_lastLoggedReadback = -1;
			Evidence.Reset();
		}
	}
}
