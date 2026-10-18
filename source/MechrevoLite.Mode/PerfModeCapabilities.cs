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

	/// <summary>
	/// ⭐⭐ 2026-10-19（30 系 · SMU 功耗控制）：⭐ <b>本后端的自定义档功耗到底走不走 SMU</b> ✅。
	/// <para>⭐ 为什么单独一个能力位：30 系（宝龙达 <c>MRID6</c> / 6800H）没有 WMI 功能号 23，
	/// PL1/PL2/温度墙/STAPM 时间窗都走 SMU；而"本机**真的**走 SMU"不是仅凭机型就能判定的 ——
	/// PawnIO 没装 / RyzenSMU 初始化失败时，<c>MechrevoService.ApplyPowerLimitViaSmu</c> 会如实失败并返回 false
	/// （30 系**没有** WMI 23 可退回）⇒ 那时候界面就不该建 SMU 专属行（守 §9.14：能力位||本机实现，
	/// 且与真实下发分支**同源**）。</para>
	/// <para>⭐ <b>单一判据</b>：本能力位 = <see cref="UsesSmuForCustomPower" /> 的返回值
	/// （宝龙达 + 30 系 + SMU 通路可用），供规划器生成 <c>SmuStapmTimeSeconds</c>（MP1 0x18）这一步；
	/// UI（<c>CustomModeForm</c>）也只用这同一个函数决定建不建「功耗时间窗（STAPM，秒）」那一行 ✅
	/// （值读不到/不稳定 ⇒ 行**照建**、显示「未设置」，⛔ 不隐藏）。</para>
	/// <para>⚠️ 默认 <c>false</c> ⇒ ⭐ <see cref="All" /> 与所有既有后端（GCU / 老接口 / 40 系）<b>逐位不变</b> ✅。</para>
	/// </summary>
	public bool SupportsSmuPowerControls { get; init; }

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：「它现在接了 SMU 嘛，功耗持续实际能不能放进去」）：
	/// ⭐ <b>「本机自定义档的功耗是不是真的走 SMU」的唯一纯判据</b>（可单测；不碰硬件）✅。
	/// <para>三个条件**同时**成立才算走 SMU：</para>
	/// <list type="number">
	/// <item><c>usesBaolongda</c>：宝龙达后端（30/40 系蛟龙16Pro 那个模具）；</item>
	/// <item><c>isBaolongda30Series</c>：30 系（蛟龙16 / 裸 <c>MRID6</c> / 6800H）—— 30 系原厂 WMI 方法表只到 18，
	/// 没有功能号 23；40 系（<c>MRID6-23</c>）有 23 ⇒ 不走 SMU；</item>
	/// <item><c>smuReady</c>：SMU 通路**真的可用**（PawnIO 已装 + RyzenSMU 初始化成功；
	/// 由 <c>SmuAvailability.IsReady</c> 缓存一次轻量探测，⛔ 不每次开窗读 SMU）。</item>
	/// </list>
	/// <para>⚠️ 为什么必须是**函数**而不是散在各处的 if：机主明确要求 UI 显示与真实下发**同源**，
	/// 避免「界面显示 SMU 行、实际却走 WMI 23（30 系根本没有）」的错位（§9.14）。</para>
	/// </summary>
	internal static bool UsesSmuForCustomPower(bool usesBaolongda, bool isBaolongda30Series, bool smuReady, bool wujieApu = false)
	{
		// ⭐⭐ 2026-10-19（机主拍板：无界 APU 直接开放 SMU 自定义）：新增「无界 APU」分支 ——
		//   model 含 无界/WUJIE/WJXY && CpuVendor.IsAmd && 无 NVIDIA 独显 && SMU 通路可用 ⇒ 允许 SMU 自定义 ✅
		//   ⚠️ 宝龙达 30 系原条件**逐位不变**；其它机型（GCU/同方）默认参数 false ⇒ 行为不变 ✅
		return (usesBaolongda && isBaolongda30Series && smuReady) || (wujieApu && smuReady);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：「显卡超频貌似没用」· 宝龙达无 GCU）：⭐ <b>本后端能不能把 GPU 核心/显存偏移
	/// **直接写进 NVIDIA 驱动**（NVAPI P-States 20 的 P0）</b> ✅。
	/// <para>⚠️ <b>为什么必须单独一个能力位</b>：宝龙达（`UsesBaolongda`）**没有 GCU** ⇒ 自定义档走
	/// <see cref="PerfModeRoute.FirmwareSlot" /> 时，本来该发 GPU 偏移的那几步（`WriteFirmwareField:GpuCoreClockOffsetOC`
	/// ⇒ 厂商 MQTT）**必然失败**；而它其实有另一条可用通路：
	/// <c>PerfModeBackend.ApplyGpuOverclockAsync</c>（⭐ 宝龙达分支 ⇒ <c>NvapiPStates.TrySetP0Offsets</c>）
	/// —— ⭐ 但规划器原来**只在 BuiltIn 路由**里生成 <see cref="PerfApplyStepKind.ApplyGpuOverclock" />，
	/// 而自定义档**永远**是 FirmwareSlot 路由 ⇒ ⭐ 这一步**从来没生成过** ⇒ 界面上的核心/显存滑块
	/// 只是存进了配置、**一个字节都没下发**（⛔ 这正是"显卡超频没用"的根因）✅。</para>
	/// <para>⭐ <b>语义</b>：<c>true</c> ⇒ ⭐ 规划器在 **FirmwareSlot 路由**下也生成
	/// <see cref="PerfApplyStepKind.ApplyGpuOverclock" />（⭐ 后端走本机 NVAPI，写后回读）✅。</para>
	/// <para>⚠️ 默认 <c>false</c> ⇒ ⭐ <see cref="All" /> 与所有既有后端（GCU / 老接口）<b>逐位不变</b> ✅。</para>
	/// </summary>
	public bool SupportsDirectGpuOverclock { get; init; }

	public static readonly PerfModeCapabilities All = new PerfModeCapabilities();
}
