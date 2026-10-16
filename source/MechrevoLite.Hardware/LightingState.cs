using System;

namespace MechrevoLite.Hardware;

internal static class LightingState
{
	/// <summary>亮度"没有可用读数"的哨兵（<c>MechrevoHw.KeyboardBrightness</c> 字段缺省 = -1）。</summary>
	internal const int KeyboardBrightnessUnknown = -1;

	/// <summary>"没有历史亮度"的哨兵值。</summary>
	internal const int NoKeyboardBrightnessHistory = -1;

	/// <summary>没有历史值时抬起亮度的兜底默认（50% = 中间档；⛔ 不写 100，免得夜里刺眼）。</summary>
	internal const int DefaultKeyboardBrightness = 50;

	internal static LightingIdleAction ResolveIdleAction(int timeoutSeconds, long idleMilliseconds, bool suspended)
	{
		if (timeoutSeconds <= 0)
		{
			return LightingIdleAction.None;
		}
		long num = ((TimeSpan.FromSeconds(timeoutSeconds).TotalMilliseconds >= 9.223372036854776E+18) ? long.MaxValue : ((long)TimeSpan.FromSeconds(timeoutSeconds).TotalMilliseconds));
		if (!suspended && idleMilliseconds >= num)
		{
			return LightingIdleAction.Suspend;
		}
		if (!suspended || idleMilliseconds >= 2000)
		{
			return LightingIdleAction.None;
		}
		return LightingIdleAction.Restore;
	}

	internal static bool ShouldSuspendForBattery(bool optionEnabled, bool onBattery)
	{
		return optionEnabled & onBattery;
	}

	internal static bool IsTemporarilySuspended(bool offOnBatteryEnabled, bool onBattery, bool idleSuspended)
	{
		return ShouldSuspendForBattery(offOnBatteryEnabled, onBattery) | idleSuspended;
	}

	internal static bool IsLightSwitchOn(bool channelPowerOn, bool temporarilySuspended)
	{
		if (channelPowerOn)
		{
			return !temporarilySuspended;
		}
		return false;
	}

	/// <summary>
	/// 2026-10-12（键盘灯「有时候控制不了」根因①：门控背离）：把「LM 缓存的键盘灯电源」
	/// 与「硬件/GCU 当时报的电源」合成为**有效电源**。
	/// <para>
	/// 语义（三条，对应离线夹具断言的门控真值表）：
	/// <list type="bullet">
	/// <item><c>explicitOff=true</c>（用户明确关过）⇒ <b>恒 false</b>，硬件报 On 也压不过用户意图。</item>
	/// <item>否则硬件报 <c>hardwarePowerOn=true</c> ⇒ true（硬件当前是唯一的真值来源）。</item>
	/// <item>否则沿用 LM 缓存值。</item>
	/// </list>
	/// </para>
	/// <para>⚠️ 反例保护：<c>explicitOff</c> 只在用户真的点了「键盘」开关 / 拨到亮度 0 / 从
	/// rgb.cfg 读到明确的 <c>kbPower=0</c> 时为 true（见 <c>KeyboardRgb.KbPowerExplicitOff</c>），
	/// 绝不因"配置缺省"或"硬件回灌"而被置位。</para>
	/// </summary>
	internal static bool ResolveEffectiveKbPower(bool cachedPowerOn, bool hardwarePowerOn, bool explicitOff)
	{
		if (explicitOff)
		{
			return false;
		}
		if (hardwarePowerOn)
		{
			return true;
		}
		return cachedPowerOn;
	}

	/// <summary>
	/// 2026-10-12（用户拍板「键盘灯光也修复」）：「硬件也报 Off」的判据 —— <c>explicitOff</c> 这条标记
	/// **只在硬件同时报 Off 时才算数**。
	/// <para>背景（第一次真机验证结论）：rgb.cfg 里那次"明确关灯"留下的 <c>kbPower=0</c> 会在启动时被
	/// <c>LoadConfig</c> 置位成 <c>KbPowerExplicitOff</c>，而机主后来用 Fn 把灯点亮（硬件报 <c>power=True</c>）时
	/// 那个标记**既不清除也不回写** ⇒ 有效电源恒 false，恢复路径每次写 <c>SetLightPower(..., false)</c> +
	/// <c>StopCurrentEffect</c>（硬件报 On 只撑 1~2 秒就被压回），亮度条也被静默吞掉。</para>
	/// </summary>
	internal static bool ShouldClearExplicitOffOnHardwareOn(bool hardwareConnected, bool hardwarePowerOn, bool explicitOff)
	{
		return hardwareConnected && hardwarePowerOn && explicitOff;
	}

	/// <summary>
	/// 2026-10-12（用户拍板「键盘灯光也修复」）：门控的**唯一判定入口** —— 把
	/// <see cref="ShouldWriteKeyboardLighting" /> 与「陈旧标记要不要清掉」合成一次结果
	/// （<see cref="KeyboardLightingPlan" />），供 UI 写入点（<c>KeyboardLightingDecision</c>）与
	/// 恢复路径（<c>Program.SyncKbPowerFromHardwareIfDiverged</c>）共用，⛔ 不再各自复制一份口径。
	/// </summary>
	/// <param name="hardwareConnected">硬件层是否已连接（未连接时**不采信**硬件电源读数）。</param>
	/// <param name="hardwarePowerOn">硬件/GCU 当前报的键盘电源。</param>
	/// <param name="cachedPowerOn">LM 侧缓存的用户意图位。</param>
	/// <param name="explicitOff">LM 侧「用户明确关过」标记。</param>
	/// <param name="forceSwitch">默认 OFF 的 <c>Keyboard/Ctrl</c> 放行开关。</param>
	internal static KeyboardLightingPlan DecideKeyboardLighting(bool hardwareConnected, bool hardwarePowerOn, bool cachedPowerOn, bool explicitOff, bool forceSwitch)
	{
		bool flag = ShouldClearExplicitOffOnHardwareOn(hardwareConnected, hardwarePowerOn, explicitOff);
		bool num = flag ? false : explicitOff;
		return new KeyboardLightingPlan(ResolveEffectiveKbPower(cachedPowerOn, hardwareConnected && hardwarePowerOn, num) || forceSwitch, flag);
	}

	/// <summary>
	/// 2026-10-12（根因①）：**UI 写入是否应当放行** —— 键盘灯写入（HID 帧流 / 走 GCU 回退时的
	/// <c>Keyboard/Ctrl</c>）应不应该发出去。与 <see cref="ResolveEffectiveKbPower"/> 同一份语义，
	/// 外加 <paramref name="forceSwitch"/>（<c>KeyboardCtrlPublishSwitch.Enabled</c>）这条显式放行开关。
	/// <para>⛔ 本方法**只做判定、不改任何状态**；「陈旧的 explicitOff 要清掉」那件事由
	/// <see cref="DecideKeyboardLighting" /> 一起回给调用方（写入口径只有一个）。</para>
	/// </summary>
	/// <param name="cachedPowerOn">LM 侧 <c>KeyboardRgb.KbPowerOn</c>（= 用户意图位）。</param>
	/// <param name="hardwarePowerOn">硬件/GCU 当前报的键盘电源（<c>MechrevoHw.KeyboardPower</c>）。</param>
	/// <param name="explicitOff"><c>KeyboardRgb.KbPowerExplicitOff</c>（调用方应当已经按
	/// <see cref="ShouldClearExplicitOffOnHardwareOn" /> 清过陈旧标记）。</param>
	/// <param name="forceSwitch">默认 OFF 的 <c>Keyboard/Ctrl</c> 放行开关。</param>
	internal static bool ShouldWriteKeyboardLighting(bool cachedPowerOn, bool hardwarePowerOn, bool explicitOff, bool forceSwitch)
	{
		return ResolveEffectiveKbPower(cachedPowerOn, hardwarePowerOn, explicitOff) || forceSwitch;
	}

	internal static bool ShouldRestoreKeyboardPower(bool userWantsPower, bool temporaryPowerOff, bool cachedPowerOn)
	{
		if (userWantsPower)
		{
			if (!temporaryPowerOff)
			{
				return !cachedPowerOn;
			}
			return true;
		}
		return false;
	}

	/// <summary>
	/// 2026-10-12（机主原话「亮度是0，所以打开关闭没有变化，把亮度拉高就好了」）：**亮度 0 归一化** ——
	/// 「电源有效为 On、但亮度是 0」时，把亮度抬到一个**用户看得见**的值。
	/// <para>语义（⭐ 本轮的取舍，报告里写明）：
	/// <list type="bullet">
	/// <item>先判**有效电源**（<see cref="ResolveEffectiveKbPower" />，含 explicitOff 只在硬件也报 Off 时才算数）
	/// ⇒ ⛔ 电源不 On 时**一个字节都不动**（这正是"别与 explicitOff 修复打架"）；</item>
	/// <item>亮度读数不在 <c>0..100</c>（-1 = 未知）⇒ 不动；</item>
	/// <item>亮度 ≥ 1 ⇒ **原样保留**（用户自己调的值绝不覆盖，含 1% 这种"很暗但有意"）；</item>
	/// <item>亮度 = 0 **且** <paramref name="lastNonZeroBrightness" /> 在 <c>1..100</c>
	/// ⇒ 抬回**上一次非 0 的亮度**（= 用户自己的选择，比任何默认值都更可能是他要的）；</item>
	/// <item>亮度 = 0 且没有历史值 ⇒ 抬到 <see cref="DefaultKeyboardBrightness" />；</item>
	/// <item>亮度 = 0 且 <paramref name="brightnessZeroIsOff" />（单分区 <c>KeyboardType∈{1,2}</c>，亮度即档位、
	/// 0 是合法的"最暗/关"）⇒ **不动**（⛔ 不去覆盖那条既有语义）。</item>
	/// </list></para>
	/// </summary>
	/// <param name="effectivePowerOn">有效电源（<see cref="ResolveEffectiveKbPower" /> 的结果）。</param>
	/// <param name="brightness">当前亮度读数（LM 缓存或硬件回读映射后的百分数；-1 = 未知）。</param>
	/// <param name="lastNonZeroBrightness">上一次非 0 的亮度（1..100；无历史 = -1）。</param>
	/// <param name="brightnessZeroIsOff">亮度 0 在本机型是否本来就等于"关"（单分区机型 = true）。</param>
	internal static int NormalizeKeyboardBrightness(bool effectivePowerOn, int brightness, int lastNonZeroBrightness, bool brightnessZeroIsOff)
	{
		if (!effectivePowerOn || brightnessZeroIsOff)
		{
			return brightness;
		}
		if (brightness < 0 || brightness > 100)
		{
			return brightness;
		}
		if (brightness > 0)
		{
			return brightness;
		}
		return ((lastNonZeroBrightness >= 1 && lastNonZeroBrightness <= 100) ? lastNonZeroBrightness : DefaultKeyboardBrightness);
	}

	/// <summary>
	/// 哪次亮度读数该被 **latch 成"上一次非 0 的亮度"**（⛔ 0 与 -1 都不 latch —— 0 是坏读数、-1 是未知）。
	/// </summary>
	internal static bool ShouldLatchLastNonZeroBrightness(int brightness)
	{
		return brightness >= 1 && brightness <= 100;
	}

	/// <summary>
	/// 2026-10-12（用户澄清「我加的部分是为非单键RGB键盘的分流」）：**GCU/MQTT <c>Keyboard/Ctrl</c>
	/// 电源通道该不该被使用** —— 那条分流是**人为加的**，唯一用途是给**非单键 RGB**
	/// （<c>KeyboardType∈{1,2}</c>）的键盘做分流；单键 RGB 机型（如耀世的 <c>KeyboardType=7</c>）
	/// 走**作者自制的 HID 自定义帧**那条路 ⇒ ⛔ 永远不该收到 GCU 的键盘电源/效果下发。
	/// <para>⚠️ 这就是"分流泄漏"：<c>Program.RestoreKeyboardLightingAsync</c> 等恢复/熄灯路径原来
	/// **不看键盘类型**就发 <c>IssueLightPower("Keyboard/Ctrl", on:false)</c> ⇒ 单键机型被反复压灭
	/// （机主日志里那些 <c>SetLightPower(Keyboard/Ctrl, False) confirmed</c>）。</para>
	/// <para>判据与通道归属（<c>KeyboardFirmwareEffects.IsGcuZoneKeyboardPath</c>）**同一套**：
	/// <paramref name="gcuZoneKeyboardPath" /> = <c>KeyboardType∈{1,2}</c>。⛔ 不改分流本身、⛔ 不动 HID 机制。</para>
	/// </summary>
	/// <param name="gcuZoneKeyboardPath">本机键盘是否属于 GCU 分流那一族（<c>KeyboardType∈{1,2}</c>）。</param>
	/// <param name="serviceConnected">GCU/MQTT 服务通道是否可用。</param>
	internal static bool ShouldUseGcuKeyboardPowerChannel(bool gcuZoneKeyboardPath, bool serviceConnected)
	{
		return gcuZoneKeyboardPath && serviceConnected;
	}

	/// <summary>
	/// 2026-10-12（关灯方向）：**"这次关灯要不要退回 GCU 电源通道"的唯一判据** ——
	/// 在 <see cref="ShouldUseGcuKeyboardPowerChannel" /> 之上只多一条：**HID 侧真的不可用**时也退回 GCU。
	/// <para>⭐ 理由：单键机型（HID 自定义帧）"关灯"就是 <c>KeyboardRgb.StopCurrentEffect()</c> 那条
	/// 512 字节全 0 帧；**帧流不在（<paramref name="hidConnected" /> = false）时它等于什么都没发**，
	/// 这时灯其实熄不掉 ⇒ 必须借 GCU 那条 <c>Keyboard/Ctrl</c> <c>off</c> 把电源轨切掉。
	/// HID 正常在的时候行为**一格不变**：⛔ 单键机型照旧一条 GCU 电源都不发。</para>
	/// <para>⛔ 本判据只管**关灯**方向；开灯方向见 <see cref="ShouldIssueKeyboardPowerOn" />，两者⛔ 不共用。</para>
	/// </summary>
	/// <param name="gcuZoneKeyboardPath">本机键盘是否属于 GCU 分流那一族（<c>KeyboardType∈{1,2}</c>）。</param>
	/// <param name="serviceConnected">GCU/MQTT 服务通道是否可用。</param>
	/// <param name="hidConnected">HID 自定义帧流是否可用（<c>KeyboardRgb.IsConnected</c>）。</param>
	internal static bool ShouldUseGcuKeyboardOffChannel(bool gcuZoneKeyboardPath, bool serviceConnected, bool hidConnected)
	{
		return ShouldUseGcuKeyboardPowerChannel(gcuZoneKeyboardPath, serviceConnected) || !hidConnected;
	}

	/// <summary>
	/// 2026-10-12（窄化 GCU 分流 gate）：开机 / 唤醒 / 空闲恢复时**要不要补发** <c>Keyboard/Ctrl</c> 的
	/// <c>on</c>。
	/// <para>⭐ 作者既有的两条语义各归各位：</para>
	/// <list type="bullet">
	/// <item><b>非单键机型</b>（GCU 分流族，<c>KeyboardType∈{1,2}</c>）⇒ 逐字走既有
	/// <see cref="ShouldRestoreKeyboardPower" />（这次是临时熄过灯就必发，否则只在固件报 Off 时发）。</item>
	/// <item><b>单键机型</b>（走作者自制的 HID 自定义帧）⇒ 与作者 UI 的 HID 直通分支**同一句**语义
	/// （<c>SettingsForm</c>：<c>(service != null &amp;&amp; hw != null &amp;&amp; hw.IsConnected &amp;&amp;
	/// !hw.KeyboardPower) ? SetKeyboardPower(on: true) : null</c>）——⛔ 固件电源已经报 On 时**不补发**：
	/// 固件电源为 Off 时 HID 帧不可见（必须先上电），为 On 时再发一遍只是多一次电源抖动。</item>
	/// </list>
	/// <para>⛔ 本判据只管"要不要发 <c>on</c>"；关灯方向的门不在这里（见 <c>Program.SuspendLightingTemporarilyAsync</c>
	/// 与"按用户设置保持关闭"那条分支）。</para>
	/// </summary>
	/// <param name="gcuPowerChannel">本机是否走 GCU 分流电源通道（<see cref="ShouldUseGcuKeyboardPowerChannel" />）。</param>
	/// <param name="userWantsPower">LM 侧用户意图位（<c>KeyboardRgb.KbPowerOn</c>）。</param>
	/// <param name="temporaryPowerOff">本机键盘灯这次是不是刚被"临时熄灭"（GCU 分流族用）。</param>
	/// <param name="reportedPowerOn">固件 / GCU 当前报的键盘电源。</param>
	internal static bool ShouldIssueKeyboardPowerOn(bool gcuPowerChannel, bool userWantsPower, bool temporaryPowerOff, bool reportedPowerOn)
	{
		if (!userWantsPower)
		{
			return false;
		}
		if (gcuPowerChannel)
		{
			return ShouldRestoreKeyboardPower(userWantsPower, temporaryPowerOff, reportedPowerOn);
		}
		return !reportedPowerOn;
	}

	/// <summary>
	/// 2026-10-12（窄化 GCU 分流 gate）：**"键盘灯刚被临时熄掉"这条标记要不要置** ——
	/// 它驱动恢复时的"强制重申自定义帧模式"（<c>Program</c> 里的 <c>forceEffectRestore</c>）。
	/// <para>⭐ 按"这次到底是谁把灯弄灭的"记：</para>
	/// <list type="bullet">
	/// <item>走 GCU 分流 ⇒ 只有 <c>Keyboard/Ctrl</c> <c>off</c> **真的下发成功**才算（电源轨被切了，
	/// 恢复必须重新上电 + 重申自定义帧模式）。</item>
	/// <item>单键机型（HID 通道）⇒ 那次临时熄灯就是 <c>KeyboardRgb.StopCurrentEffect()</c> 的
	/// 512 字节全 0 帧；帧流在（<c>IsConnected</c>）就说明黑帧确实出去了 ⇒ 同样置标记。
	/// ⛔ 这样「临时熄灯 ⇒ 恢复时强制重申自定义帧模式」对单键机型**也成立**，不再只剩调用方
	/// <c>force:true</c> 那一条兜底。</item>
	/// </list>
	/// </summary>
	/// <param name="gcuPowerChannel">本机是否走 GCU 分流电源通道。</param>
	/// <param name="gcuPowerOffDelivered">GCU 的 <c>Keyboard/Ctrl</c> <c>off</c> 是否下发成功。</param>
	/// <param name="hidBlackFrameDelivered">HID 帧流是否在（512 字节全 0 帧确实已下发）。</param>
	internal static bool ShouldMarkKeyboardTemporarilySuspended(bool gcuPowerChannel, bool gcuPowerOffDelivered, bool hidBlackFrameDelivered)
	{
		if (gcuPowerChannel)
		{
			return gcuPowerOffDelivered;
		}
		return hidBlackFrameDelivered;
	}
}
