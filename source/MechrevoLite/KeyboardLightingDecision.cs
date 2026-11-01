using System;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;
using MechrevoLite.Properties;

namespace MechrevoLite;

/// <summary>
/// 2026-10-12（用户拍板 · 键盘灯「有时候控制不了」）：
/// <list type="number">
/// <item><b>① 门控背离</b> —— 把「LM 缓存的键盘灯电源」与「硬件/GCU 当时报的电源」分开，
/// 合并口径落在 <see cref="MechrevoLite.Hardware.LightingState.ShouldWriteKeyboardLighting"/>；
/// 本类是它在 UI 侧的唯一消费点（<c>RgbForm</c> 两处写入门 + 提示）。</item>
/// <item><b>② UI 不再静默</b> —— 被门控挡住时用既有 Toast（<c>ToastForm.ShowNotice</c>）明确提示，
/// ⛔ 不再"什么都不做"。</item>
/// </list>
/// <para>判据（三条，与离线夹具 <c>tools\dev\kblight-selftest</c> 断言的真值表一致）：
/// 「用户明确关过」这条标记**只在硬件也报 Off 时才算数** ⇒ 硬件报 On 且标记还在 = 标记陈旧
/// （机主后来用 Fn 把灯点亮了）⇒ 清标记、回写缓存为 On、放行；标记已清后 ⇒ 硬件报 On 即发；
/// 否则沿用 LM 缓存；<c>KeyboardCtrlPublishSwitch</c> 打开 ⇒ 恒发。</para>
/// <para>⚠️ 「陈旧标记要清掉」不是本类能做的（本类只读不写）—— 落点在
/// <c>Program.SyncKbPowerFromHardwareIfDiverged</c>（恢复路径每一次恢复都会过一遍），
/// 而本类调 <see cref="MechrevoLite.Hardware.LightingState.DecideKeyboardLighting" />
/// 时**已经按"硬件报 On 就不认那个标记"判**，所以即使标记还没轮到清理，写入也不会被它挡住。</para>
/// </summary>
internal static class KeyboardLightingDecision
{
	/// <summary>
	/// UI 写入键盘灯（HID 帧流 / 走 GCU 回退时的 <c>Keyboard/Ctrl</c>）是否应当放行。
	/// <c>hardwareConnected=false</c> 时**不采信**硬件电源读数，退回 LM 缓存值
	/// （<c>KeyboardPower</c> 的字段缺省是 true，未连接时它不代表任何真值）。
	/// </summary>
	/// <param name="hardwareConnected">硬件层是否已连接（<c>MechrevoHw.IsConnected</c>）。</param>
	/// <param name="hardwarePowerOn">硬件/GCU 当前报的键盘电源（<c>MechrevoHw.KeyboardPower</c>）。</param>
	internal static bool ShouldWrite(bool hardwareConnected, bool hardwarePowerOn)
	{
		return LightingState.DecideKeyboardLighting(
			hardwareConnected,
			hardwarePowerOn,
			Program.rgb != null && Program.rgb.KbPowerOn,
			Program.rgb != null && Program.rgb.KbPowerExplicitOff,
			KeyboardCtrlPublishSwitch.Enabled).Write;
	}

	/// <summary>
	/// 「用户明确关过」标记当前是否**仍然算数**（= 标记还在 **且** 硬件确认也是 Off）。
	/// 提示路径与写入路径共用这一个判据：硬件报 On 时那个标记是陈旧的 ⇒ 不算数 ⇒
	/// 该给用户提示（否则这种锁死永远既无日志也无提示）。
	/// </summary>
	internal static bool ExplicitOffStillHonored()
	{
		if (Program.rgb == null || !Program.rgb.KbPowerExplicitOff)
		{
			return false;
		}
		return !(Program.hw != null && Program.hw.IsConnected && Program.hw.KeyboardPower);
	}

	/// <summary>
	/// 2026-10-12（机主原话「亮度是0，所以打开关闭没有变化，把亮度拉高就好了」）：亮度被归一化抬起来时
	/// **说一句**（⛔ 不静默 —— 用户得知道"开关看不出变化"是因为亮度是 0）。同类 15 秒内只弹一次。
	/// </summary>
	/// <param name="reason">归一的依据（写进日志，便于对账）。</param>
	internal static void NotifyBrightnessRestored(int restored, string reason)
	{
		try
		{
			if (restored < 1 || restored > 100 || !ShouldShow("brightness"))
			{
				return;
			}
			string text = string.Format(Strings.KeyboardBrightnessRestored, restored);
			ToastForm.ShowNotice(string.IsNullOrWhiteSpace(text) ? $"Keyboard backlight brightness was 0; raised to {restored}%" : text);
			Logger.WriteLine($"键盘灯亮度 0 已抬起并提示用户：{restored}%（{reason}）");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("键盘灯亮度提示失败: " + ex.GetType().Name + " " + ex.Message);
		}
	}

	/// <summary>
	/// 被门控挡住时的明确提示（⛔ 不再静默）。
	/// </summary>
	/// <param name="gcuKeyboardRouted">
	/// true = 本次写入本来要走 GCU/MQTT 回退通道（<c>RgbForm.IsGcuKeyboardFallback</c>）⇒
	/// 提示"官方通道未启用"；false = HID 句柄还没就绪 ⇒ 提示"键盘设备尚未就绪"。
	/// </param>
	internal static void Notify(bool gcuKeyboardRouted)
	{
		try
		{
			if (Program.rgb == null)
			{
				return;
			}
			// 2026-10-12（用户拍板「键盘灯光也修复」）：⛔ 原来这里见到 KbPowerExplicitOff 就直接 return ⇒
			// 机主那次"陈旧标记锁死"（硬件报 On、标记还在）**既无日志也无提示**。现在改成
			// 「标记**只在硬件也报 Off 时**才静默」—— 硬件报 On 时标记是陈旧的，照旧提示。
			if (ExplicitOffStillHonored())
			{
				return;
			}
			string kind = gcuKeyboardRouted ? "gcu" : "hid-notready";
			string message = gcuKeyboardRouted
				? (KeyboardCtrlPublishSwitch.Enabled ? "Keyboard lighting channel is not ready" : "Keyboard lighting channel is not enabled (official channel is off)")
				: "Keyboard device is not ready yet; please try again shortly";
			if (!ShouldShow(kind))
			{
				return;
			}
			// 文案与 resx 里的 Key 一一对应（中英两份 resx + Strings.cs 访问器）；
			// resx 缺失时回退到内置英文，保证"不静默"这条底线在任何资源状态下都成立。
			string localized = gcuKeyboardRouted
				? (KeyboardCtrlPublishSwitch.Enabled ? Strings.KeyboardChannelNotReady : Strings.KeyboardChannelDisabled)
				: Strings.KeyboardDeviceNotReady;
			ToastForm.ShowNotice(string.IsNullOrWhiteSpace(localized) ? message : localized);
			Logger.WriteLine("键盘灯写入被门控挡住，已提示用户: " + kind);
		}
		catch (Exception ex)
		{
			// 提示本身绝不许把 UI 路径搞崩：失败只记日志。
			Logger.WriteLine("键盘灯门控提示失败: " + ex.GetType().Name + " " + ex.Message);
		}
	}

	/// <summary>
	/// 同类提示 15 秒内只弹一次（用户连续拖亮度/换效果时不刷屏）；不同类各算各的。
	/// 纯按时间戳，⛔ 不引入任何状态机。
	/// </summary>
	private static bool ShouldShow(string kind)
	{
		long now = Environment.TickCount64;
		lock (GateLock)
		{
			long last = kind == "gcu" ? _lastGcuNoticeMs : _lastNotReadyNoticeMs;
			if (last != 0L && now - last < NoticeIntervalMs)
			{
				return false;
			}
			if (kind == "gcu")
			{
				_lastGcuNoticeMs = now;
			}
			else
			{
				_lastNotReadyNoticeMs = now;
			}
			return true;
		}
	}

	private const long NoticeIntervalMs = 15000L;

	private static readonly object GateLock = new object();

	private static long _lastGcuNoticeMs;

	private static long _lastNotReadyNoticeMs;
}
