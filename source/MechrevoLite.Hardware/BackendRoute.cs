namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ 2026-10-16：**后端路由** —— 当前这一台机器上，某个动作该由「谁」来执行。
/// <para>设计文档：<c>docs/notes/后端路由抽象-设计-20261016.md</c>（⭐ 动机与验收标准都在那里 ✅）。</para>
/// <para>⭐ 存在的理由（用户原话）：⭐「我们以后如果加入 ACPI/EC 控制方式，那以 GCU 为前提就不好用了，
/// 必须提前做好兼容」✅ —— ⭐ 目标是把「加一条新后端」从**改一遍**变成**改一处** ✅。</para>
/// <para>⚠️ 本枚举是**预留型**的：⭐ <see cref="AcpiEc"/> **即使尚未实现也先占位** ✅ ——
/// 这样将来接入时<see cref="MechrevoHw.Query"/>的调用方**零改动** ✅。</para>
/// </summary>
internal enum BackendRoute
{
	/// <summary>⭐ 后端尚未就绪（⭐ 探测未完成 / ⭐ 没有任何一条可用 ✅）—— ⛔ **不等于「不支持」** ⚠️。</summary>
	None = 0,

	/// <summary>⭐ GCU（同方）后端 —— ⭐ 走 MQTT ✅。⭐ 这是历史基线后端 ✅。</summary>
	Gcu,

	/// <summary>⭐ 老机型 WMI 后端（<c>root\WMI:MICommonInterface</c> ✅）—— ⭐ 宝龙达的协议同族 ✅。</summary>
	LegacyMifs,

	/// <summary>⭐ 宝龙达模具后端（⭐ 蛟龙 16Pro 等 ✅）—— ⭐ 板号判据 <c>MRID6-*</c> ✅。</summary>
	Baolongda,

	/// <summary>
	/// ⭐ **预留**：⭐ ACPI / EC 直控后端（⭐ 尚未实现 ⚠️）。
	/// <para>⭐ 占位的意义：⭐ 接入时只需新增一个后端实现 ＋ 一个查表分支 ＋ 探测顺序一行 ✅，
	/// <see cref="MechrevoHw.Query"/> 的调用方**一行不改** ✅ —— ⭐ 这正是本抽象的验收标准 ✅。</para>
	/// </summary>
	AcpiEc,
}

/// <summary>
/// ⭐⭐ 2026-10-16：**功能项** —— 一个「动作」，⭐ 而不是「后端」。
/// <para>⭐ 与 <see cref="MechrevoDeviceCapabilities"/>（⭐ 能力位 ✅）的分工：</para>
/// <list type="bullet">
/// <item>能力位（⭐ <c>Capabilities</c> ✅）：⭐ 回答「⭐ **界面**该显示/启用哪些控件 ✅」—— ⭐ 消费者是 **UI 层** ✅。</item>
/// <item>本枚举（⭐ <see cref="BackendFeature"/> ✅）：⭐ 回答「⭐ **这个动作该走谁** ✅」—— ⭐ 消费者是 **执行层** ✅。</item>
/// </list>
/// <para>⭐ 两者**来源相同**（⭐ 同一份后端事实 ✅），⭐ 但**消费者不同** ✅ —— ⚠️ 这正是
/// <see cref="MechrevoHw.Query"/> 只做「视图」的原因 ✅。</para>
/// </summary>
internal enum BackendFeature
{
	/// <summary>⭐ 性能档切换（⭐ 静音 / ⭐ 平衡 / ⭐ 狂暴 ✅）。</summary>
	PerfModeSwitch,

	/// <summary>⭐ 自定义档「应用」（⭐ 含固件槽写入 ✅）。</summary>
	CustomProfileApply,

	/// <summary>⭐ 功耗墙（⭐ PL1 / ⭐ PL2 / ⭐ 温度墙 ✅）。</summary>
	PowerLimit,

	/// <summary>⭐ 限充（⭐ 充电上限 ✅）。</summary>
	ChargeLimit,

	/// <summary>⭐ 键盘灯（⭐ 颜色 / ⭐ 亮度 / ⭐ 模式 ✅）。</summary>
	KeyboardLighting,

	/// <summary>⭐ LOGO 灯开关 ✅。</summary>
	LogoLight,

	/// <summary>⭐ 灯带（⭐ lightbar ✅）。</summary>
	Lightbar,

	/// <summary>⭐ 显卡模式切换（⭐ 核显 / ⭐ 混合 / ⭐ 独显直连 ✅）。</summary>
	GpuModeSwitch,

	/// <summary>⭐ 风扇转速**读取** ✅。</summary>
	FanRpmRead,

	/// <summary>⭐ 风扇曲线**读取** ✅。</summary>
	FanCurveRead,

	/// <summary>⭐ 风扇曲线**写入** ✅。</summary>
	FanCurveWrite,

	/// <summary>⭐ Fn 键锁 ✅。</summary>
	FnLock,

	/// <summary>⭐ 触摸板 ✅。</summary>
	Touchpad,

	/// <summary>⭐ OSD 提示开关 ✅。</summary>
	OsdToggle,

	/// <summary>⭐ 温度读取 ✅。</summary>
	Temperature,

	/// <summary>⭐ 刷新率 ✅。</summary>
	RefreshRate,
}

/// <summary>
/// ⭐⭐⭐ 2026-10-16：**三态** —— ⭐ 本设计的**核心** ✅。
/// <para>⚠️ **本会话反复踩的坑，正是把 <see cref="NotSupportedByThisBackend"/> 当成了【终局失败】** ❌ ——
/// ⭐ 典型例子：⭐「以 GCU 未连接为由阻断」⭐ 把一个「⭐ 当前后端做不到」⭐ 误当成「⭐ 永远做不到」⭐，
/// 于是整体失败 ✅。</para>
/// <para>⭐ 三态必须分清 ✅ —— ⭐ 这就是整个路由抽象的立足点 ✅。</para>
/// </summary>
internal enum FeatureSupport
{
	/// <summary>⭐ 当前后端**已接好**这条下发路径 ⇒ ⭐ 调用方可以按该后端的路径执行 ✅。</summary>
	Supported,

	/// <summary>
	/// ⭐ 当前后端做不到 —— ⚠️ **但别的后端可能能做** ✅。
	/// <para>⚠️ 调用方**不得**假装成功 ✅：⭐ 要么走自己的原路 ✅，⭐ 要么**如实告知用户「本机不支持」** ✅。</para>
	/// </summary>
	NotSupportedByThisBackend,

	/// <summary>
	/// ⭐ **尚未判定**（⭐ 探测未完成 / ⭐ 本骨架尚未逐项登记 ✅）—— ⛔ ⭐ **不代表「不支持」** ⚠️。
	/// <para>⚠️ 字面语义是「⭐ 我们还不知道 ✅」⇒ ⭐ 调用方应**保守行事**（⭐ 例如等一轮再问 ✅），
	/// ⛔ **绝不能把它当作 <see cref="NotSupportedByThisBackend"/>** ⚠️。</para>
	/// </summary>
	Unknown,
}

