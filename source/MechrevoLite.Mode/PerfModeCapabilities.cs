namespace MechrevoLite.Mode;

public sealed record PerfModeCapabilities
{
	public bool UsesAmdPowerFields { get; init; }

	public bool SupportsPl1 { get; init; } = true;

	public bool SupportsPl2 { get; init; } = true;

	public bool SupportsPl4 { get; init; } = true;

	public bool SupportsTcc { get; init; } = true;

	public bool SupportsTgp { get; init; } = true;

	public bool SupportsDynamicBoost { get; init; } = true;

	public bool SupportsGpuOverclock { get; init; } = true;

	public bool SupportsFanCurve { get; init; } = true;

	public bool SupportsFanBoost { get; init; } = true;

	public bool SupportsFanSwitchSpeed { get; init; } = true;

	public bool SupportsRefreshRate { get; init; } = true;

	public bool SupportsTurboSubMode { get; init; } = true;

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 自定义档诚实性）：⭐ <b>本后端能不能写「固件自定义档」</b> ✅。
	/// <para>⚠️ <b>为什么需要这个能力位</b>：⭐ 自定义档（<see cref="PerfModeRoute.FirmwareSlot" />）的下发是
	/// <c>SwitchFirmwareSlot</c>（⇒ <c>MechrevoService.SwitchCustomProfileWithResend</c>）＋ <c>WriteFirmwareField</c>
	/// （⇒ <c>SetCustomDetail</c>）⭐ —— ⭐ 两条都<b>只走 GCU 的 MQTT</b> ❌；
	/// ⭐ 而宝龙达机型<b>没有 GCU</b> ✅ ⇒ ⭐ 这一步<b>恒失败</b> ⚠️（真机日志：⭐ <c>SwitchCustomProfile: GCU 未连接</c> ❌）。
	/// ⭐ 于是 <see cref="PerfModeApplyPlanner" /> 会生成一个<b>注定失败</b>的档位步 ⇒
	/// <see cref="PerfModeRunner" /> 一见档位步失败就 <c>break</c> ⇒ ⭐ <b>后面所有步骤都不再执行</b> ❌。</para>
	/// <para>⭐ <b>语义</b>：⭐ <c>false</c> ⇒ ⭐ 规划器<b>不生成</b>「档位步 ＋ 固件字段步 ＋ 固件风扇曲线步」，
	/// ⭐ 只保留<b>后端无关</b>的那些（电源计划 / 电源覆盖层 / 睿频 / 刷新率 / 风扇增强 ✅）。
	/// ⭐ 也就是说："⭐ 应用"⭐ 退化为<b>在本机保存配置</b>（<c>perf_custom*_</c> 键本来就存得下 ✅），
	/// ⭐ 而<b>不去</b>假装切换固件档 —— ⭐ 与「能力位＝已接好下发路径」这条铁律一致 ✅。</para>
	/// <para>⚠️ 默认 <c>true</c> ⇒ ⭐ <see cref="All" /> 与所有既有后端<b>逐位不变</b> ✅；
	/// ⭐ 只有明确申报"本机写不了固件档"的后端（宝龙达 ✅）才置 <c>false</c> ✅。</para>
	/// </summary>
	public bool SupportsFirmwareSlot { get; init; } = true;

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 自定义档 · 功耗墙真下发）：⭐ <b>本后端能不能用【非 GCU 的 WMI】写 CPU 功耗墙（PL1 / PL2 / 温度墙）</b> ✅。
	/// <para>⚠️ <b>为什么需要它</b>：⭐ 这三项原本长在「固件字段步」里（<c>WriteFirmwareField</c> ⇒ <c>SetCustomDetail</c> ⇒ MQTT ❌）
	/// ⇒ ⭐ 被 <see cref="PerfModeCapabilities.SupportsFirmwareSlot" /> 那条判据一起关掉了 ⚠️。
	/// ⭐ 但它们在宝龙达上<b>有另一条可用通路</b>：<c>MechrevoService.SetCustomDetail</c> 开头的
	/// <c>TryApplyPowerLimitViaBackend</c> 会把这几个键<b>就地摘出来</b>、改写 WMI 功能号 23（子命令 2/3/4 ✅）
	/// ⇒ ⭐ 所以它们<b>不该</b>跟着固件槽一起被关 ❌。</para>
	/// <para>⭐ <b>语义</b>：⭐ <c>true</c> ⇒ ⭐ 规划器<b>单独</b>生成 PL1 / PL2 / CpuTccOffset 三步（<b>不依赖</b>写得了固件槽 ✅）；
	/// ⭐ <c>false</c> ⇒ ⭐ 这三步仍留在原来的固件字段块里（⭐ 行为逐位不变 ✅）。</para>
	/// <para>⚠️ 默认 <c>false</c> ⇒ ⭐ <see cref="All" /> 与所有既有后端<b>逐位不变</b> ✅。</para>
	/// </summary>
	public bool SupportsWmiPowerFields { get; init; }
	// 且与真实下发分支**同源**）。</para>
	public bool SupportsSmuPowerControls { get; init; }
	// <b>「本机自定义档的功耗是不是真的走 SMU」的唯一纯判据</b>（可单测；不碰硬件）。
	internal static bool UsesSmuForCustomPower(bool usesBaolongda, bool isBaolongda30Series, bool smuReady, bool wujieApu = false)
	{
		// （机主拍板：无界 APU 直接开放 SMU 自定义）：新增「无界 APU」分支 ——
		return (usesBaolongda && isBaolongda30Series && smuReady) || (wujieApu && smuReady);
	}
	// ⇒ 厂商 MQTT）**必然失败**；而它其实有另一条可用通路
	public bool SupportsDirectGpuOverclock { get; init; }
	// 只下发本机 SMU 步骤（PL1/PL2/温度墙/STAPM）与后端无关的 Windows 侧步骤 。
	public bool WujieSmuLocalOnly { get; init; }
	public static readonly PerfModeCapabilities All = new PerfModeCapabilities();
}
