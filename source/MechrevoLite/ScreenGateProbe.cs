using System;
using MechrevoLite.Display;

namespace MechrevoLite;

/// <summary>
/// 2026-10-15（screen-gate）：「屏幕」区（<c>panelBrightness</c>）可见性闸门的 **Win32 / WMI 实测判据**。
///
/// <para><b>为什么需要它</b>：该区主体（刷新率行 + 亮度行 + 校色 + 色彩增强）原判据
/// <c>flag7</c> / <c>flag10</c> **全部只认 GCU 上报的能力位**（<c>caps.DisplayRefresh</c> /
/// <c>hw.SupportsDisplayRefresh</c>（← GCU 的 <c>currentHZList</c>）/ <c>hw.ScreenBrightnessSeen</c>），
/// 于是停掉 <c>GCUBridge</c> 后整块消失（用户真机现象：只剩「屏幕」标题 + 「自动刷新率」开关）。
/// 但这**两项的数据源本来就是 Win32/WMI**，与 GCU 无关 —— 停 GCU 只该让"自动刷新率"这类
/// 真依赖 GCU 的开关失效，不该让整块屏幕区消失。</para>
///
/// <para><b>两条判据（都是纯读，绝不落盘、绝不写硬件）</b>：
/// <list type="number">
/// <item><see cref="Win32MaxRefreshRate" /> —— 内屏系统侧最大刷新率，走
/// <see cref="ScreenNative.MeasureMaxRefreshRate" />（<c>EnumDisplaySettingsEx</c> 只测量）。
/// ⚠️ **刻意不用** <see cref="ScreenControl.GetMaxRate" /> / <c>ScreenNative.GetMaxRefreshRate(…, confidence)</c>：
/// 那两个在 <c>confidence == Embedded</c> 时会**往 config 写 <c>screen_max</c>**。可见性判定是
/// 只读路径，不该有落盘副作用（那是既有行为，本轮不去动它，只是不在这里调）。</item>
/// <item><see cref="WmiBrightnessReadable" /> —— <c>root\wmi:WmiMonitorBrightness</c> 能否读到
/// 当前亮度（<see cref="ScreenBrightness.TryGet" />，与 GCU 无关）。</item>
/// </list></para>
///
/// <para><b>缓存策略（本类的存在理由之一）</b>：两条实测各自有开销（一次显示模式枚举 / 一次 WMI
/// 往返），而 <c>RefreshDeviceCapabilities</c> 在一次启动里会被调用很多次（GCU 连接就绪、模式切换、
/// 主题切换、语言切换、插拔录音设备…）。故：
/// <list type="bullet">
/// <item><b>进程内一次性缓存</b>（<see cref="CacheTtlMs" /> = 60 秒）：首次求值后 60 秒内直接返回缓存，
/// 之后放行重新测量 —— 既避免"每次刷新都枚举"，又能让"外接屏/内屏切换后刷新率变了"在
/// 一分钟内自愈（不像"永久记忆"那样一旦读错就永远错）。</item>
/// <item><b>只缓存成功值</b>（<c>&gt; 0</c> / <c>true</c>）：<c>-1</c>/<c>0</c>/<c>false</c> 一律**不缓存**，
/// 下次调用照旧实测。否则"启动那一刻显示器还没枚举好"读到的假阴性会被记住一分钟，
/// 反而把已经修好的屏又判成没数据源。</item>
/// <item><b>失败静默</b>：任何异常一律吞掉并按"读不到"处理（<c>-1</c> / <c>false</c>），
/// 绝不让探测失败影响启动路径或既有判定 —— 最坏情况就是回到改动前的行为（只认 GCU）。</item>
/// </list>
/// ⚠️ 线程安全：缓存字段用 <see cref="System.Threading.Volatile" /> 读写；即使两个线程同时首次求值，
/// 也只是重复测一次（幂等、只读），无副作用。</para>
///
/// <para>⚠️ 本类**只被可见性判据读取**：不写注册表/EC/NVRAM/驱动、不改显示设置、不写 config。</para>
/// </summary>
internal static class ScreenGateProbe
{
	/// <summary>实测判据的进程内缓存有效期（毫秒）。见类注释的取舍说明。</summary>
	internal const int CacheTtlMs = 60000;

	private static readonly object Sync = new object();

	private static long _win32MaxAt;

	private static int _win32MaxValue = -1;

	private static long _wmiBrightAt;

	private static bool _wmiBrightValue;

	/// <summary>
	/// 内屏系统侧最大刷新率（只测量、不落盘）。读不到返回 <c>-1</c>；
	/// ⚠️ 认不出哪块是内屏时 <see cref="ScreenNative.MeasureMaxRefreshRate" /> 会退化为
	/// "当前显示设备"继续测量（其自身语义，见该方法的注释）—— 那正是"无 GCU 也要能用"想要的。
	/// </summary>
	internal static int Win32MaxRefreshRate()
	{
		long now = Environment.TickCount64;
		lock (Sync)
		{
			if (_win32MaxValue > 0 && now - _win32MaxAt < CacheTtlMs)
			{
				return _win32MaxValue;
			}
		}
		int num;
		try
		{
			num = ScreenNative.MeasureMaxRefreshRate(ScreenNative.FindLaptopScreen(log: false));
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("screen-gate-win32-fail", "screen-gate: Win32 刷新率实测失败（按读不到处理）：" + ex.Message);
			num = -1;
		}
		lock (Sync)
		{
			// 只在测到正值时记缓存：假阴性不缓存 ⇒ 下次调用重测（详见类注释）。
			if (num > 0)
			{
				_win32MaxValue = num;
				_win32MaxAt = now;
			}
		}
		return num;
	}

	/// <summary>
	/// <c>root\wmi:WmiMonitorBrightness</c> 是否读得到（= 本机亮度可控）。与 GCU 无关。
	/// 只缓存成功值（<c>true</c>），失败下次重试，理由同上。
	/// </summary>
	internal static bool WmiBrightnessReadable()
	{
		long now = Environment.TickCount64;
		lock (Sync)
		{
			if (_wmiBrightValue && now - _wmiBrightAt < CacheTtlMs)
			{
				return true;
			}
		}
		bool flag;
		try
		{
			flag = ScreenBrightness.TryGet(out int _);
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("screen-gate-wmi-fail", "screen-gate: WMI 亮度探测失败（按读不到处理）：" + ex.Message);
			flag = false;
		}
		lock (Sync)
		{
			if (flag)
			{
				_wmiBrightValue = true;
				_wmiBrightAt = now;
			}
		}
		return flag;
	}
}
