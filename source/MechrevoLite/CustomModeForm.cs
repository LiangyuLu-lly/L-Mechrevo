using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;
using MechrevoLite.Mode;
using MechrevoLite.Gpu;
using MechrevoLite.Properties;
using MechrevoLite.UI;

namespace MechrevoLite;

public class CustomModeForm : RForm
{
	private sealed record ModeItem(string Id, string Text)
	{
		public override string ToString()
		{
			return Text;
		}
	}

	internal const int RowLogicalHeight = 26;

	// 2026-10-09（自定义性能模式 GPU 偏移）：风险阈值与「细拖」刻度的唯一来源（都是具名常量，便于以后调整）。
	// 阈值：核心偏移 50 系 +200 / 其它代际 +150（代际未知按更严的 150，保守）；显存偏移全代际 +1000。
	/// <summary>2026-10-09：GPU 核心偏移风险阈值 —— 50 系（<see cref="DgpuGenerationKind.Gen50" />）为 +200。</summary>
	internal const int CoreOffsetWarnGen50 = 200;

	/// <summary>2026-10-09：GPU 核心偏移风险阈值 —— 其它代际（30/40 等）为 +150。</summary>
	internal const int CoreOffsetWarnDefault = 150;

	/// <summary>2026-10-09：GPU 显存偏移风险阈值（全代际同一档）为 +1000。</summary>
	internal const int VramOffsetWarn = 1000;
	// <summary> （机主实测）：GCU 固件显存负偏移下限 = -1000（更负写不进去）；宝龙达同口径 </summary>
	internal const int VramOffsetNegativeFloor = -1000;

	/// <summary>2026-10-09：核心偏移滑块「细拖」的一个刻度 = 5 MHz。</summary>
	private const int CoreOffsetDragStep = 5;

	/// <summary>2026-10-09：显存偏移滑块「细拖」的一个刻度 = 25 MHz。</summary>
	private const int VramOffsetDragStep = 25;

	/// <summary>2026-10-09：细拖时「一个刻度」对应的逻辑像素数（越小越灵敏）。</summary>
	private const int OffsetDragPixelsPerStep = 2;

	// 2026-10-09（面板统一「显式应用」）：_pending / _pendingEdits / _gpuOcDirty 三者只登记
	// 「改了但还没点应用」的改动，**一律不即时下发**；唯一的下发入口是 ApplyPendingAsync
	// （底部最左的主按钮「应用」，以及 GPU 超频行里那颗同口径的小「应用」）。
	private readonly Dictionary<string, string> _pending = new Dictionary<string, string>();

	/// <summary>2026-10-09：组合框那类「应用侧」改动的待应用列表（电源模式/电源计划/睿频/风扇增强/刷新率）。</summary>
	private readonly List<Func<PerfModeSettings, PerfModeSettings>> _pendingEdits = new List<Func<PerfModeSettings, PerfModeSettings>>();

	/// <summary>
	/// 2026-10-09（用户拍板「电源计划回退旧行为」）：「电源模式」那一行最近一次登记的待应用改动句柄。
	/// 电源计划这一行改成「选中即生效」后，如果用户接着选了非平衡方案，就把这条还没应用的电源模式改动
	/// 摘掉 —— 免得稍后点【应用】时它按「两者互斥」又把刚下发好的方案清成 null（看起来就是"被抢回"）。
	/// 只影响这一对互斥行，其余行的显式应用不受影响。
	/// </summary>
	private Func<PerfModeSettings, PerfModeSettings>? _pendingPowerModeEdit;

	/// <summary>2026-10-09：面板里有「未应用的改动」（= 三份暂存集合非空）—— 决定「应用」按钮可用性，
	/// 并让硬件回读不把用户刚改好的值抹掉（见 OnCustomChanged / RestorePendingControlValues）。</summary>
	private bool _panelDirty;

	/// <summary>
	/// 2026-10-09：当前档位被锁（Gen40+ 的预设档）—— 其含义是<i>硬件参数被锁</i>（<see cref="OptionControls" />
	/// 名单里的滑块/数字框/开关/风扇曲线整行禁用），**不是整个面板只读**。
	/// 2026-10-09 修正：应用侧那几行（电源模式 / 电源计划 / 睿频 / 风扇增强 / 刷新率）不在名单里，锁定时仍可改，
	/// 也能照常点【应用】（见 <see cref="SyncApplyButtons" /> / <see cref="ApplyPendingAsync" />）。
	/// </summary>
	private bool _optionsLocked;

	private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
	// （机主：初始化纯按需、 不要启动预热）：风扇闸门/清理的"按需触发一次"闸门
	private static int _deferredFanInitScheduled;

	private readonly List<string> _rowLabels = new List<string>();

	private readonly ToolTip _tips = new ToolTip();

	// 2026-10-10 第二轮（用户原话：「修反了 文本框拉长了 滑块反而短了」）：
	// AddSliderRow 那一行的「数字框 + − + +」这一簇的宽度约束（**逻辑像素**，一律经 D() 换算，⛔ 不写设备像素）。
	// ⭐ 本轮的规则**反过来**：数字框保持**窄**且**定宽**（把内容刚好放得下即可），一行的余量**全部**给滑块 ——
	//   滑块宽 = W − 标题列 − 数字框列 −（−/+ 两列）− 必要间距；窗口越宽滑块越长，数字框不再跟着变宽。
	//   * MinValueInputLogicalWidth = 数字框宽度（逻辑）。取回上一批之前的 **52**：本机（96 DPI，Microsoft YaHei UI
	//     9.5pt）逐档实测（%TEMP% 探针，直接实例化本组件的真实 RNumericUpDown）——
	//     52 设备像素的数字框里，内嵌编辑框可用 **36** 设备像素（= 52 − Controls[0] 那个"已隐藏但框架
	//     仍预留 16 设备像素"的原生步进按钮区 − 左右各 2）；文字实测（NoPadding）"12345"=35、"−2000"=33、
	//     "9999"=28、"240"=21 ⇒ **本页真实可能出现的取值（功耗 4 位数 / ±偏移 4 位数含符号 / 三档 Tcc）
	//     全部放得下**；只有 6 位（"200000"=42）会截 —— 该量级只有 °C 感应器原始毫瓦字段才会出现，不进本页。
	//     同口径按 125/150/175% 放大字体复测（52 逻辑像素 → 65/78/91 设备像素，编辑区 45/58/71）：4 位+符号
	//     仍全部放得下（"12345" 需 45/55/60）⇒ 高 DPI 不截断。
	//   * ValueInputMinClusterLogicalWidth = 「数字框 + − + +」这一簇的**固定**宽度（数字框 + 两符按钮 30 + 27）。
	//     本页 9 行都是这 9 个实例、尺寸约束一致 ⇒ 整簇定宽，窗口变宽时它**一个像素都不长**（余量全给滑块）。
	//   * SliderMinLogicalWidth = 一行里留给滑块的最小宽度（整窗最小宽度的算式之一，见构造末尾）。
	private const int MinValueInputLogicalWidth = 52;

	private const int ValueInputMinClusterLogicalWidth = MinValueInputLogicalWidth + 57;

	private const int SliderMinLogicalWidth = 99;

	/// <summary>2026-10-10 第四轮⭐（B，用户原话：「滑块最右边已经有点贴到文本了」）：滑块列与「数字框 + − +」
	/// 那一簇之间的<b>行内间距</b>（逻辑像素）——取 6，与"看得见的框 → −"那条既有的 D(6) 同一量级
	/// ⇒ 一行里的留白读起来是一致的（滑块 | D(6) | 数字框 | D(6) | − | 8 | +）。
	/// ⚠️ 实现只把它当作<b>滑块自己的右 Margin</b>（滑块是 Dock=Fill ⇒ "列宽 − Margin" 才是滑块宽）：
	/// 列宽 / 簇宽 / 数字框几何 / 两颗符按钮一个像素都不动，<see cref="RewidthSliderRows" /> 里
	/// 与间距有关的算式**一行都不用改**。</summary>
	private const int SliderValueInputGapLogicalWidth = 6;

	/// <summary>2026-10-10：<see cref="RewidthSliderRows" /> 的重入闸（改列宽会再触发布局）。</summary>
	private bool _rewidthing;

	/// <summary>2026-10-10：滑块行第 2 列（数字框那一簇）当前已经设过的宽度（设备像素），用于幂等判断。</summary>
	private int _lastSliderClusterWidth;

	/// <summary>2026-10-10 第二轮：滑块行第 0 列（标题列）当前已经设过的宽度（设备像素），用于幂等判断。
	/// ⚠️ 只在"窗口窄到 <c>标题列 + 滑块下限 + 簇宽</c> 放不下"时才会小于构造末尾给的实测标题列宽。</summary>
	private int _lastSliderRowLabelDeviceWidth;

	/// <summary>
	/// 2026-10-10：滑块行的**标题列宽**（设备像素）—— 就是构造末尾给行设的那个 num4
	/// （最宽标题实测宽 + D(8) 内边距 + D(18) 滑条让位）。存下来给 <see cref="RewidthSliderRows" /> 用，
	/// 免得在那里重新 MeasureText（重算还可能因为文本/字体状态不同而算出不同值）。
	/// </summary>
	private int _sliderRowLabelColumnDeviceWidth;

	/// <summary>2026-10-10：AddSliderRow 建出来的那 9 行（只有它们参与"数字框右移 / 滑块拉长"的重排）。</summary>
	private readonly List<TableLayoutPanel> _sliderRows = new List<TableLayoutPanel>();

	/// <summary>2026-10-10 第二轮：那 9 行的数字框（数值框），用于把宽度钉成"簇宽 − 两符按钮列宽"，
	/// 使它**填满簇内第 0 列、右边缘紧贴 −**（⛔ 不设固定左偏移，也就不会在它右边留空块）。</summary>
	private readonly List<Control> _sliderValueInputs = new List<Control>();

	/// <summary>2026-10-10 第二轮：那 9 行的「数字框 + − +」内层容器与两颗符按钮，
	/// 用于同时钉住 D(24) 防裁下限与"按钮落到自己列的最左"（既有行内间距不变）。</summary>
	private readonly List<SliderChipRow> _sliderRowsWithChips = new List<SliderChipRow>();

	/// <summary>2026-10-10 第二轮：簇内两颗符按钮列的实际宽度（设备像素，构造时算一次）——
	/// 数字框宽度 = 簇宽 − 这两列，避免在重排里重算 D() 造成取整不一致。</summary>
	private int _sliderDecColumnDeviceWidth;

	private int _sliderIncColumnDeviceWidth;

	/// <summary>2026-10-10 第三轮：数字框控件**内部**被隐藏的原生步进按钮占掉的那条死条宽（设备像素）
	/// —— 本机实测 96 DPI = 16、175% DPI = 28（≡ 16×scale）。⛔ 本批只把它当作"两颗符按钮向左让多少"
	/// 的**逻辑像素**量（<c>D(16)</c>，与 <see cref="D" /> 同一换算口径），不写设备像素常量，
	/// 也不去改控件内部（那条死条本批一个像素都不动，见 <see cref="AddSliderRow" /> 的 − / + 说明）。</summary>
	private const int NativeSpinReserveLogicalWidth = 16;

	/// <summary>2026-10-10 第三轮⭐（②）：上一次重排量到的死条宽（设备像素）—— 幂等判断的一部分
	/// （DPI 变化 / 换显示器导致死条宽变了 ⇒ 必须重排一次）。</summary>
	private int _lastAppliedSpinReserve = -1;

	/// <summary>2026-10-10 第三轮（竖向）：内容表（标题区 + 参数卡片 + 按钮行）—— 它的高度由
	/// <see cref="ResizeToContent" /> 补到"自然高度"与"视口高"中的较大者（多出来的给参数卡片那一行）。</summary>
	private TableLayoutPanel _contentTable;

	/// <summary>2026-10-10 第三轮（竖向）：参数卡片（Dock=Fill，吃内容表里 Percent=100 那一行的全部余高）。</summary>
	private TableLayoutPanel _paramTable;

	/// <summary>2026-10-10 第三轮（竖向）：底部按钮行（内容表最后一行、AutoSize）—— 自然高度算式要用它。</summary>
	private FlowLayoutPanel _buttonRow;

	private int _minContentWidth;

	private bool _syncing;

	private bool _syncingModes;
	// （GPU 偏移 UI）：数字框→滑块同步时抑制反向回写（数字框可超出滑块 ±500 且保留真实值）
	private bool _sliderSyncFromNumeric;

	private bool _refreshingHardwareState;
	// （ 避免递归/重复日志； 值本身仍会正常登记、正常下发）。
	private bool _plLinking;

	private Panel _scrollHost;

	private int _laidOutContentHeight;

	private RSlider _pl1;

	private RSlider _pl2;

	private RSlider _pl4;

	private RSlider _tcc;
	// （判据 = PerfModeCapabilities.UsesSmuForCustomPower，与真实下发分支同源；见 ctor 注释）。
	private RSlider _stapmTime;
	private RSlider _smuSlowLimit;
	private RNumericUpDown _smuSlowLimitVal;
	private RSlider _smuSlowTime;
	private RNumericUpDown _smuSlowTimeVal;
	private RSlider _smuFastTime;
	private RNumericUpDown _smuFastTimeVal;

	private RNumericUpDown _stapmTimeVal;

	private Label _stapmTimeLabel;

	private RSlider _tgp;
	// <summary> （显卡功耗上限 · 本机 NVML）：只有能读到有效约束时才创建 </summary>
	private RSlider _gpuPowerLimit;

	private RNumericUpDown _gpuPowerLimitVal;

	private RSlider _db;

	private RSlider _coreOc;

	private RSlider _memOc;

	private RNumericUpDown _pl1Val;

	private RNumericUpDown _pl2Val;

	private RNumericUpDown _pl4Val;

	private RNumericUpDown _tccVal;

	private RNumericUpDown _tgpVal;

	private RNumericUpDown _dbVal;

	private RNumericUpDown _coreOcVal;

	private RNumericUpDown _memOcVal;

	private RSlider _fanSwitchSpeed;

	private RNumericUpDown _fanSwitchSpeedVal;

	private RCheckBox _dbChk;

	private RCheckBox _tccChk;

	private RCheckBox _ocChk;

	private RCheckBox _fanSwitchSpeedChk;

	private bool _gpuOcCoreAdjustable;

	private bool _gpuOcMemoryAdjustable;

	private bool _gpuOcArmed;

	private FlowLayoutPanel _gpuOcAdminRow;

	/// <summary>2026-10-09：核心/显存偏移是否有「改了但没点应用」的值（true 时不允许自动下发）。</summary>
	private bool _gpuOcDirty;

	/// <summary>2026-10-09：核心偏移超阈值的红色风险提示行（默认隐藏）。</summary>
	private TableLayoutPanel? _coreOcRiskRow;

	/// <summary>2026-10-09：显存偏移超阈值的红色风险提示行（默认隐藏）。</summary>
	private TableLayoutPanel? _memOcRiskRow;

	private RComboBox _powerModeCombo;

	private RComboBox _planCombo;

	private RComboBox _boostCombo;

	private RComboBox _fanBoostCombo;

	private RComboBox _refreshCombo;

	private TableLayoutPanel _fanBoostRow;

	private TableLayoutPanel _refreshRow;

	private RComboBox _modeCombo;

	// 2026-10-04: firmware-profile bypass (plain B). When on, the form displays a
	// 30-series firmware profile instead of an LM custom mode.
	private bool _firmwareMode;

	private int _firmwareVisualMode;

	private int _firmwareProfileIndex;

	private string? _firmwareProfileName;

	// 2026-10-05: re-entrancy guard. BindFirmwareProfile -> LoadAppSide -> control events
	// -> Queue -> FlushPendingAsync -> SelectFirmwareTargetAsync could loop back here and
	// froze the whole UI. Never allow a nested bind.
	// 2026-10-09（面板统一「显式应用」）：Queue 已不再触发下发，这条环路的中间段（FlushPendingAsync）
	// 换成了点【应用】才走的 ApplyPendingAsync；守卫本身原样保留（BindFirmwareProfile 仍可能被重入）。
	private bool _bindingFirmwareProfile;

	/// <summary>2026-10-05: read-only view of the bound firmware target (for SettingsForm).</summary>
	internal bool IsFirmwareMode => _firmwareMode;

	internal int FirmwareVisualMode => _firmwareVisualMode;

	internal int FirmwareProfileIndex => _firmwareProfileIndex;

	private RButton _addButton;

	private RButton _renameButton;

	private RButton _deleteButton;

	private FlowLayoutPanel _nameRow;

	private TextBox _nameBox;

	private string _modeId = PerfModeDefinition.CustomId(1);

	private Label _status;

	private FlowLayoutPanel _statusRow;

	private Label _routeHint;

	/// <summary>2026-10-09：底部最左的主按钮「应用」——整面板唯一的下发入口（沿用 RButton 主按钮样式）。</summary>
	private RButton _applyButton;

	private RButton _restoreButton;

	private RButton _fanCurveButton;

	// 2026-10-05 step7A (upstream 0.290.6 A-28): CPU runtime tuning entry.
	private RButton _cpuTuningButton;

	private Label _powerWallStatus;

	// ⭐⭐ 2026-10-16（"未设置"标记 · 用户指导「⭐ 开源的都有方案的不是吗」）：
	//   ⚠️ 宝龙达协议对功耗墙**只写不读**（⭐ 功能号 23 ⚠️）⇒ ⭐ 机器上的值我们**读不回来** ✅
	//   ⇒ ⭐ 那就 ⛔ **不许编一个数**（⭐ 原来那组 45/65/95 是我们自己拼的 ⚠️）✅
	//   ⇒ ⭐ 没下发过的行 ⇒ ⭐ 标题追加「（未设置）」（⭐ 见 `UnsetMark` ✅）
	//   ⚠️ 为什么放标题而不是数字框：⭐ `NumericUpDown` **拒绝非数字文本**
	//      （⭐ 本机探针实测：⭐ 赋 `Text = "未设置"` 被忽略、⭐ 值仍是原数字 ❌）
	//      ⇒ ⭐ 标题是唯一能显示文字的既有位置 ✅；⭐ `MakeLabel` 会把**最终文本**记进 `_rowLabels`
	//      ⇒ ⭐ 标题列宽**自动容纳**它、⛔ 不会裁字 ✅（⭐ 代价：⭐ 标题列随之变宽，⭐ 如实记录 ✅）
	private Label _pl1Label;

	private Label _pl2Label;

	private Label _tccLabel;

	/// <summary>⭐「未设置」标记（⭐ 见 <see cref="_pl1Label" /> 的注释 ✅）。</summary>
	private const string UnsetMark = "（未设置）";

	/// <summary>
	/// ⭐⭐ 2026-10-16（"未设置"）：⭐ 功耗墙三行的**完整语义**（⭐ 悬浮提示用 ✅）。
	/// <para>⚠️ 如实说明：⭐ 宝龙达协议对功耗墙**只写不读** ⚠️ ⇒ ⭐「未设置」的含义是
	/// **"本次运行我们还没下发过"**，⛔ **不是**"机器上没设过" ✅ —— ⭐ 机器上到底是多少，我们读不回来 ⚠️。</para>
	/// </summary>
	private const string PowerWallUnsetHint = "本机协议读不回功耗墙的当前值（只写不读）。标题带「（未设置）」= 本次运行还没下发过，不代表机器上没设过。拖动滑块并点【应用】后，这里显示的就是我们下发的值。";

	private readonly System.Windows.Forms.Timer _powerWallTimer = new System.Windows.Forms.Timer
	{
		Interval = 2000
	};

	private int _customChangedQueued;

	internal static string SwitchPendingText => Strings.SwitchPending;

	internal static string SwitchUnconfirmedText => Strings.SwitchUnconfirmed;

	internal static string GcuDisconnectedText => Strings.GcuDisconnectedShort;

	internal static string FanTableTimeoutText => Strings.FanTableNotReady;
	// 但它的**功耗墙/温度墙/GPU 偏移/显卡功耗上限都能真下发**（52d66da / d03da38 已接）——
	internal static string LocalApplyText => Strings.PerfModeHintLocalApply;

	internal string EditingModeId => _modeId;

	internal static Action? RestartAsAdminOverride { get; set; }

	private int D(int value)
	{
		return ResponsiveLayout.LogicalToDevice(this, value);
	}

	private void SyncStatusRow()
	{
		if (_statusRow != null && _status != null)
		{
			bool flag = !string.IsNullOrEmpty(_status.Text);
			bool flag2 = _powerWallStatus != null && !string.IsNullOrEmpty(_powerWallStatus.Text);
			_status.Visible = flag;
			bool flag3 = flag | flag2;
			if (_statusRow.Visible != flag3)
			{
				_statusRow.Visible = flag3;
			}
		}
	}

	public CustomModeForm()
	{
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		Text = Strings.PerfEditorTitle;
		FormBorderStyle = FormBorderStyle.Sizable;
		MaximizeBox = false;
		// 2026-10-13（用户原话「bug自定义窗口最小化后安不出来,能不能隐藏最小化和放大按钮 只保留关闭按钮」）：
		// **只留【关闭】**。本窗是 AddOwnedForm 的非模态子窗（SettingsForm.cs:8332 / 8379），实测窗口
		// ex 样式 0x00050100 —— **没有 WS_EX_APPWINDOW** ⇒ 最小化后任务栏上不会出现它自己的按钮；
		// 而两条"再打开"入口都以 Visible 为闸（SettingsForm.cs:8334 / 8381），最小化时 Visible 仍为 true
		// ⇒ 点了没反应/被 Hide，全程无人把 WindowState 置回 Normal ⇒ 这就是"唤不回来"。拔掉最小化按钮
		// 即从源头去掉这条路径（WinForms 的 MinimizeBox=false 会连 WS_MINIMIZEBOX 一起去掉）。
		// ⚠️ FormBorderStyle 仍是 Sizable：用户只要求藏两颗按钮（窗口仍可拖边缩放），而且面板的
		// 宽度自适应重排（RewidthSliderRows / 滑块越宽越长）依赖可缩放宽度，⛔ 不动。
		MinimizeBox = false;
		StartPosition = FormStartPosition.Manual;
		ClientSize = new Size(D(820), D(200));
		InitTheme(setDPI: true);
		// 打开自定义模式窗口时落一行**只读判据**日志（去重）
		if (!Program.UiAuditMode)
		{
			MechrevoLite.Hardware.WujieSmuGate.LogPredicate();
		}		// ⭐⭐ 2026-10-19（机主：无界 APU 改好功耗后重启不会自动应用）：
		// 打开自定义窗口时也**核对一次**（幂等：一致跳过、不一致后台重放本机 SMU； 不阻塞 UI、 不发 GCU 命令）
		Shown += async (object? _, EventArgs _) =>
		{
			if (Program.UiAuditMode || !((Program.hw?.WujieSmuApu ?? false) || (Program.hw?.UsesBaolongda ?? false)))
			{
				return;
			}
			try
			{
				Program.settingsForm?.RefreshDeviceCapabilities();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[perfmode] 无界 APU 开窗恢复：能力刷新失败（已吞掉，继续）：" + ex.Message);
			}
			PerfModeService perf = PerfModeService.Instance;
			if (perf == null)
			{
				return;
			}
			try
			{
				await perf.ReconcileForStartupAsync();
				await perf.ReplayLocalProfileOnStartupAsync();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[perfmode] 无界 APU 开窗恢复失败（已吞掉、不重试）：" + ex.Message);
			}
		};
		Panel panel = (_scrollHost = new Panel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true,
			BackColor = UiVisualStyle.Window
		});
		Controls.Add(panel);
		TableLayoutPanel tableLayoutPanel = (_contentTable = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			// 2026-10-10 第三轮（竖向，用户原话：「窗口拉高底下空一大块」）：内容表**不再自己量高**
			// （AutoSize=false），高度由 ResizeToContent 补到"自然高度 / 视口高"里较大的那个；
			// 参数卡片那一行已改成 Percent=100 ⇒ 多出来的高度全落在卡片上（⛔ 不在按钮下面留死区）。
			AutoSize = false,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			Padding = new Padding(D(12), D(8), D(12), D(8)),
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Window
		});
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		panel.Controls.Add(tableLayoutPanel);
		int rowCount = 0;
		FlowLayoutPanel modeFlow = new FlowLayoutPanel
		{
			Name = "modeRow",
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = true,
			BackColor = UiVisualStyle.Window,
			Margin = new Padding(0, 0, 0, D(6))
		};
		modeFlow.Controls.Add(new Label
		{
			Text = Strings.PerfModeLabel,
			AutoSize = true,
			ForeColor = UiVisualStyle.Muted,
			Margin = new Padding(0, D(6), D(8), 0)
		});
		_modeCombo = new RComboBox
		{
			Name = "modeCombo",
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = D(190),
			Margin = new Padding(0, D(2), D(8), 0)
		};
		_modeCombo.SelectedIndexChanged += async (object? _, EventArgs _) =>
		{
			// 2026-10-05: in firmware-profile mode this combo is a disabled label; it must
			// never switch modes. Without this the combo sync moved the user to another
			// mode the moment the editor was opened.
			if (!_syncingModes && !_firmwareMode && _modeCombo.SelectedItem is ModeItem modeItem)
			{
				await SelectModeAsync(modeItem.Id);
			}
		};
		modeFlow.Controls.Add(_modeCombo);
		_addButton = MakeModeButton("buttonModeNew", Strings.PerfModeNew, AddCustomModeAsync);
		_renameButton = MakeModeButton("buttonModeRename", Strings.PerfModeRename, () =>
		{
			BeginRename();
			return Task.CompletedTask;
		});
		_deleteButton = MakeModeButton("buttonModeDelete", Strings.PerfModeDelete, DeleteCustomModeAsync);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(modeFlow, 0, rowCount++);
		// ⇒ PerfModeCapabilities.SupportsFirmwareSlot = !UsesBaolongda = false ⇒ 切固件档必然失败
		_nameRow = new FlowLayoutPanel
		{
			Name = "modeNameRow",
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			BackColor = UiVisualStyle.Window,
			Margin = new Padding(0, 0, 0, D(6)),
			Visible = false
		};
		_nameRow.Controls.Add(new Label
		{
			Text = Strings.ProfileNameLabel,
			AutoSize = true,
			ForeColor = UiVisualStyle.Muted,
			Margin = new Padding(0, D(6), D(8), 0)
		});
		_nameBox = new TextBox
		{
			Name = "textModeName",
			Width = D(150),
			MaxLength = 12,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = UiVisualStyle.Input,
			ForeColor = UiVisualStyle.Text,
			Margin = new Padding(0, D(3), D(6), 0)
		};
		_nameBox.KeyDown += async (object? _, KeyEventArgs e) =>
		{
			if (e.KeyCode == Keys.Escape)
			{
				e.SuppressKeyPress = true;
				EndRename();
			}
			else if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
				await SaveNameAsync();
			}
		};
		_nameRow.Controls.Add(_nameBox);
		RButton rButton = new RButton
		{
			Name = "buttonModeNameSave",
			Text = Strings.PerfModeNameSave,
			AutoSize = true,
			Margin = new Padding(0, 0, D(4), 0),
			BackColor = UiVisualStyle.SurfaceRaised,
			ForeColor = UiVisualStyle.Text,
			BorderColor = UiVisualStyle.Border,
			Cursor = Cursors.Hand
		};
		rButton.Click += async (object? _, EventArgs _) =>
		{
			await SaveNameAsync();
		};
		_nameRow.Controls.Add(rButton);
		RButton rButton2 = new RButton
		{
			Name = "buttonModeNameCancel",
			Text = Strings.PerfModeNameCancel,
			AutoSize = true,
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.SurfaceRaised,
			ForeColor = UiVisualStyle.Text,
			BorderColor = UiVisualStyle.Border,
			Cursor = Cursors.Hand
		};
		rButton2.Click += (object? _, EventArgs _) =>
		{
			EndRename();
		};
		_nameRow.Controls.Add(rButton2);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(_nameRow, 0, rowCount++);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Name = "modeStatusRow",
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = true,
			BackColor = UiVisualStyle.Window,
			Margin = new Padding(0, 0, 0, D(4)),
			Visible = false
		};
		_status = new Label
		{
			Name = "labelModeEditorStatus",
			Text = "",
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Margin = Padding.Empty
		};
		flowLayoutPanel.Controls.Add(_status);
		_statusRow = flowLayoutPanel;
		_status.TextChanged += (object? _, EventArgs _) =>
		{
			SyncStatusRow();
		};
		_powerWallStatus = new Label
		{
			Name = "labelPowerWallVerdict",
			Text = "",
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Margin = new Padding(D(8), 0, 0, 0),
			Visible = false
		};
		flowLayoutPanel.Controls.Add(_powerWallStatus);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, rowCount++);
		_routeHint = new Label
		{
			Name = "labelModeRouteHint",
			Text = "",
			AutoSize = true,
			MaximumSize = new Size(D(480), 0),
			ForeColor = UiVisualStyle.Muted,
			Margin = new Padding(0, 0, 0, D(6)),
			Font = UiVisualStyle.Font(8.5f)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(_routeHint, 0, rowCount++);
		TableLayoutPanel table = (_paramTable = new TableLayoutPanel
		{
			Name = "paramTable",
			// 2026-10-10 第三轮（竖向）：参数卡片由 Dock=Top 改 Dock=Fill —— 它所在的那一行是
			// Percent=100，窗口拉高时整片余高都进这张卡片（列表区域填满"标题区 与 按钮行 之间"）。
			Dock = DockStyle.Fill,
			AutoSize = false,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			BackColor = UiVisualStyle.Surface,
			Padding = new Padding(D(10), D(8), D(10), D(8)),
			Margin = new Padding(0, 0, 0, D(6))
		});
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		int tableRow = 0;
		List<TableLayoutPanel> paramRows = new List<TableLayoutPanel>();
		MechrevoHw hw = Program.hw;
		_powerModeCombo = new RComboBox
		{
			Name = "powerModeCombo",
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		_powerModeCombo.Items.AddRange(Strings.ModeTuneUnchanged, Strings.ModeTunePowerEfficiency, Strings.ModeTunePowerBalanced, Strings.ModeTunePowerPerformance);
		_powerModeCombo.SelectedIndex = 0;
		_powerModeCombo.SelectedIndexChanged += (object? _, EventArgs _) =>
		{
			if (!_syncingModes && _powerModeCombo.SelectedIndex >= 0)
			{
				int? power = ((_powerModeCombo.SelectedIndex == 0) ? ((int?)null) : new int?(_powerModeCombo.SelectedIndex - 1));
				if (power.HasValue)
				{
					SelectPlan(null);
				}
				// 2026-10-09：这一行**仍然**是「显式应用」（点【应用】才保存并下发）；只是把句柄留下，
				// 好让「电源计划」那一行在选中非平衡方案时能把这条还没应用的互斥改动摘掉。
				Func<PerfModeSettings, PerfModeSettings> powerModeEdit = (PerfModeSettings s) => s with
				{
					WindowsPowerMode = power,
					PowerPlanGuid = ((!power.HasValue || PerfModeApplyPlanner.IsBalancedPlan(s.PowerPlanGuid)) ? s.PowerPlanGuid : null)
				};
				_pendingPowerModeEdit = powerModeEdit;
				QueueEdit(powerModeEdit);
			}
		};
		AddComboRow(Strings.ModeTunePowerMode, _powerModeCombo);
		_planCombo = new RComboBox
		{
			Name = "planCombo",
			DropDownStyle = ComboBoxStyle.DropDownList,
			DisplayMember = "Key"
		};
		_planCombo.Items.Add(new KeyValuePair<string, string>(Strings.ModeTuneUnchanged, ""));
		foreach (var (value, key) in WinPowerPlan.GetPlans())
		{
			_planCombo.Items.Add(new KeyValuePair<string, string>(key, value));
		}
		_planCombo.SelectedIndex = 0;
		// 2026-10-09（用户拍板「电源计划回退旧行为」）：⭐ 只有这一行恢复旧版 18.6 的「选中即生效」——
		// 选中就立刻下发到系统并落盘（旧版 CustomModeForm.cs:255-293 的 SetActivePlan + SetBoost + 写键），
		// 不再走 QueueEdit / 等【应用】。其余行（电源模式 / 睿频 / 风扇增强 / 刷新率 / 滑块开关）保持
		// 用户此前要求的「显式应用」，一个字都没动。
		_planCombo.SelectedIndexChanged += async (object? _, EventArgs _) =>
		{
			if (!_syncingModes && _planCombo.SelectedIndex >= 0)
			{
				await ApplyPowerPlanRowAsync();
			}
		};
		AddComboRow(Strings.PowerPlan, _planCombo);
		_boostCombo = new RComboBox
		{
			Name = "boostCombo",
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		_boostCombo.Items.Add(Strings.ModeTuneUnchanged);
		(string, int)[] boostModes = WinPowerPlan.BoostModes;
		for (int num = 0; num < boostModes.Length; num++)
		{
			string item = boostModes[num].Item1;
			_boostCombo.Items.Add(item);
		}
		_boostCombo.SelectedIndex = 0;
		_boostCombo.SelectedIndexChanged += (object? _, EventArgs _) =>
		{
			if (!_syncingModes && _boostCombo.SelectedIndex >= 0)
			{
				int? boost = ((_boostCombo.SelectedIndex == 0) ? ((int?)null) : new int?(_boostCombo.SelectedIndex - 1));
				QueueEdit((PerfModeSettings s) => s with
				{
					CpuBoost = boost
				});
			}
		};
		AddComboRow(Strings.BoostMode, _boostCombo);
		// （机主：显示条件 =「当前自定义档**实际走 SMU 下发**」，不是"机型是 Gen30"）
		bool wujieApu = hw?.WujieSmuApu ?? false;
		bool smuPowerControls = false;
		if (hw != null && (hw.IsBaolongda30Series || wujieApu))
		{
			smuPowerControls = PerfModeCapabilities.UsesSmuForCustomPower(hw.UsesBaolongda, hw.IsBaolongda30Series, SmuAvailability.IsReady, wujieApu);
			if (smuPowerControls && wujieApu)
			{
				MechrevoLite.Hardware.WujieSmuGate.LogAllowed();
			}
			if (!smuPowerControls)
			{
				Logger.WriteLine("[smu] 不可用 ⇒ 隐藏 STAPM 时间窗行（原因：" + SmuAvailability.Reason + "）");
			}
		}
		if (smuPowerControls)
		{
			// ⭐ 只读：先从 SMU PM 表读一次当前 PL1/PL2/温度墙/时间窗（顺便把本地记忆填成**真实当前值**，
			//   这样标题不再显示「（未设置）」、下发基线也是真值）。读失败也继续：下面按"未设置"如实显示。
			//   ⚠️ `requireStableStapmTime: true` ⇒ STAPM 时间窗必须**连续两次读数一致**才算"读得到"
			//      （真机教训：部分 PM 表版本上这个字段是动态量、会跳）⇒ 不稳定按「未设置」显示（行照建）。
			hw.RefreshBaolongda30SeriesSmuPower(force: true, requireStableStapmTime: true);
		}
		(int, int, int, bool) tuple2 = DeviceRange(hw?.Pl1Minimum ?? (-1), hw?.Pl1Maximum ?? (-1), hw?.Pl1 ?? 0);
		(int, int, int, bool) tuple3 = DeviceRange(hw?.Pl2Minimum ?? (-1), hw?.Pl2Maximum ?? (-1), hw?.Pl2 ?? 0);
		if (wujieApu && smuPowerControls)
		{
			// ⭐⭐ 无界 APU（机主口径）：PL1/PL2 上限统一 90W、下限沿用 15W ⇒ 行可调、写入范围同一口径 ✅
			int wjPl1 = Math.Clamp((hw.Pl1 > 0) ? hw.Pl1 : MechrevoLite.Hardware.WujieSmuGate.PlMinWatts, MechrevoLite.Hardware.WujieSmuGate.PlMinWatts, MechrevoLite.Hardware.WujieSmuGate.PlMaxWatts);
			int wjPl2 = Math.Clamp((hw.Pl2 > 0) ? hw.Pl2 : MechrevoLite.Hardware.WujieSmuGate.PlMinWatts, MechrevoLite.Hardware.WujieSmuGate.PlMinWatts, MechrevoLite.Hardware.WujieSmuGate.PlMaxWatts);
			tuple2 = (MechrevoLite.Hardware.WujieSmuGate.PlMinWatts, MechrevoLite.Hardware.WujieSmuGate.PlMaxWatts, wjPl1, true);
			tuple3 = (MechrevoLite.Hardware.WujieSmuGate.PlMinWatts, MechrevoLite.Hardware.WujieSmuGate.PlMaxWatts, wjPl2, true);
		}
		(RSlider, RNumericUpDown) tuple4 = AddSliderRow(PowerWallRowLabel(smuPowerControls ? Strings.SmuStapmLimitLabel : Strings.CpuPl1, hw?.BaolongdaPl1Unset ?? false), tuple2.Item1, tuple2.Item2, tuple2.Item3, (int v) =>
		{
			QueuePowerLimit("PL1", v);
		});
		_pl1 = tuple4.Item1;
		_pl1Val = tuple4.Item2;
		_pl1Label = RowLabelOf(_pl1);
		AttachPowerWallUnsetHint(_pl1, _pl1Val, _pl1Label);
		// 本文件 PM 表快照（SmuPowerSnapshot）也只读 Stapm/Fast/Slow 三个功率字段
		// 非 SMU 路径行顺序保持原样：PL1 → PL2
		if (!smuPowerControls)
		{
			tuple4 = AddSliderRow(PowerWallRowLabel(smuPowerControls ? Strings.SmuFastLimitLabel : Strings.CpuPl2, hw?.BaolongdaPl2Unset ?? false), tuple3.Item1, tuple3.Item2, tuple3.Item3, (int v) =>
			{
				QueuePowerLimit("PL2", v);
			});
			_pl2 = tuple4.Item1;
			_pl2Val = tuple4.Item2;
			_pl2Label = RowLabelOf(_pl2);
			AttachPowerWallUnsetHint(_pl2, _pl2Val, _pl2Label);
		}
		if (smuPowerControls)
		{
			// SMU 路径按参考工具的排列：STAPM（PL1）→ 慢速功率 → 慢速时长 → 快速功率（PL2）→ 快速时长
			(int, int, int, bool) slowRange = DeviceRange((hw?.Pl1Minimum ?? 15), (hw?.Pl2Maximum ?? 120), hw?.Pl1 ?? 28);
			tuple4 = AddSliderRow(Strings.SmuSlowLimitLabel, slowRange.Item1, slowRange.Item2, slowRange.Item3, (int v) =>
			{
				Queue("SmuSlowLimit", v.ToString());
			});
			_smuSlowLimit = tuple4.Item1;
			_smuSlowLimitVal = tuple4.Item2;
			tuple4 = AddSliderRow(Strings.SmuSlowTimeLabel, BaolongdaProfile.StapmTimeMinSeconds, BaolongdaProfile.StapmTimeMaxSeconds, 128, (int v) =>
			{
				Queue("SmuSlowTimeSeconds", v.ToString());
			});
			_smuSlowTime = tuple4.Item1;
			_smuSlowTimeVal = tuple4.Item2;
			tuple4 = AddSliderRow(PowerWallRowLabel(smuPowerControls ? Strings.SmuFastLimitLabel : Strings.CpuPl2, hw?.BaolongdaPl2Unset ?? false), tuple3.Item1, tuple3.Item2, tuple3.Item3, (int v) =>
			{
				QueuePowerLimit("PL2", v);
			});
			_pl2 = tuple4.Item1;
			_pl2Val = tuple4.Item2;
			_pl2Label = RowLabelOf(_pl2);
			AttachPowerWallUnsetHint(_pl2, _pl2Val, _pl2Label);
			tuple4 = AddSliderRow(Strings.SmuFastTimeLabel, BaolongdaProfile.StapmTimeMinSeconds, BaolongdaProfile.StapmTimeMaxSeconds, 64, (int v) =>
			{
				Queue("SmuFastTimeSeconds", v.ToString());
			});
			_smuFastTime = tuple4.Item1;
			_smuFastTimeVal = tuple4.Item2;
			// 本平台 SMU 没有 fast-time 命令（UXTU 的 "Fast Boost Duration" 实际写的是 stapm_time）
			// ⇒ 该行直接不显示，避免误导；控件对象保留（不在树上），其它方法访问不会 NRE。
			try
			{
				if (_smuFastTime.Tag is Control fastRow && fastRow.Parent != null)
				{
					fastRow.Parent.Controls.Remove(fastRow);
				}
			}
			catch (Exception exFast)
			{
				Logger.WriteLine("[custom-ui] 移除快速提升持续时间行失败（已吞掉）：" + exFast.Message);
			}
		}
		// 之后：功耗时间窗（STAPM，秒）→ PL4/FPPT → 温度墙
		if (smuPowerControls)
		{
			int currentStapmTime = hw.BaolongdaStapmTimeSeconds;
			bool stapmTimeUnset = currentStapmTime < BaolongdaProfile.StapmTimeMinSeconds
				|| currentStapmTime > BaolongdaProfile.StapmTimeMaxSeconds;
			int stapmTimeStart = stapmTimeUnset ? BaolongdaProfile.StapmTimeMinSeconds : currentStapmTime;
			tuple4 = AddSliderRow(PowerWallRowLabel(Strings.CpuStapmTimeWindow, stapmTimeUnset),
				BaolongdaProfile.StapmTimeMinSeconds, BaolongdaProfile.StapmTimeMaxSeconds, stapmTimeStart, (int v) =>
			{
				Queue("SmuStapmTimeSeconds", v.ToString());
			});
			_stapmTime = tuple4.Item1;
			_stapmTimeVal = tuple4.Item2;
			_stapmTimeLabel = RowLabelOf(_stapmTime);
			// tooltip 正面解释（⛔ 不写"不是 PL2 生效时间"这类否定/对比句）
			AttachPowerWallHint(_stapmTime, _stapmTimeVal, _stapmTimeLabel, Strings.CpuStapmTimeWindowHint);
			if (stapmTimeUnset)
			{
				Logger.WriteLine("[smu] STAPM 时间窗当前值读不到/不稳定 ⇒ 行照建、显示「（未设置）」（不隐藏）");
			}
		}
		// ⇒ 本行在 30 系 SMU 路径上不可调（Pl4Minimum/Maximum 保持 -1 ⇒ ApplyRange 会把整行隐藏）
		(int, int, int, bool) tuple5 = DeviceRange(hw?.Pl4Minimum ?? (-1), hw?.Pl4Maximum ?? (-1), hw?.Pl4 ?? 0);
		tuple4 = AddSliderRow((hw != null && hw.UsesAmdPowerFields) ? Strings.CpuFppt : Strings.CpuPl4, tuple5.Item1, tuple5.Item2, tuple5.Item3, (int v) =>
		{
			QueuePowerLimit("PL4", v);
		});
		_pl4 = tuple4.Item1;
		_pl4Val = tuple4.Item2;
		_tccChk = AddCheckRow(Strings.CpuTempWall, hw?.TccSwitch ?? false, (bool on) =>
		{
			Queue("CpuTccOffsetSwitch", on ? "1" : "0");
			_tcc.Enabled = on;
		});
		(int, int, int, bool) tuple6 = DeviceRange(hw?.TccMinimum ?? (-1), hw?.TccMaximum ?? (-1), hw?.TccTarget ?? 0);
		if (wujieApu && smuPowerControls)
		{
			// ⭐ 无界 APU：温度墙沿用 60–105℃（与 SMU tctl 命令同一口径）✅
			int wjTcc = Math.Clamp((hw.TccTarget > 0) ? hw.TccTarget : MechrevoLite.Hardware.WujieSmuGate.TccMinCelsius, MechrevoLite.Hardware.WujieSmuGate.TccMinCelsius, MechrevoLite.Hardware.WujieSmuGate.TccMaxCelsius);
			tuple6 = (MechrevoLite.Hardware.WujieSmuGate.TccMinCelsius, MechrevoLite.Hardware.WujieSmuGate.TccMaxCelsius, wjTcc, true);
		}
		(_tcc, _tccVal) = AddSliderRow(PowerWallRowLabel(Strings.TempWallValue, hw?.BaolongdaTempWallUnset ?? false), tuple6.Item1, tuple6.Item2, tuple6.Item3, (int v) =>
		{
			Queue("CpuTccOffset", v.ToString());
		});
		_tccLabel = RowLabelOf(_tcc);
		AttachPowerWallUnsetHint(_tcc, _tccVal, _tccLabel);
		_tccChk.Enabled = tuple6.Item4;
		// 「未设置」提示；读不到时保留原来的只读/未设置说明（更贴近用户看到的现象）。
		if (smuPowerControls)
		{
			if (!(hw?.BaolongdaPl1Unset ?? true))
			{
				AttachPowerWallHint(_pl1, _pl1Val, _pl1Label, Strings.CpuPl1Hint);
			}
			if (!(hw?.BaolongdaPl2Unset ?? true))
			{
				AttachPowerWallHint(_pl2, _pl2Val, _pl2Label, Strings.CpuPl2Hint);
			}
			if (!(hw?.BaolongdaTempWallUnset ?? true))
			{
				AttachPowerWallHint(_tcc, _tccVal, _tccLabel, Strings.CpuTempWallHint);
			}
		}
		(int, int, int, bool) tuple8 = DeviceRange(hw?.GpuTgpMinimum ?? (-1), hw?.GpuTgpMaximum ?? (-1), hw?.GpuTgp ?? 0);
		tuple4 = AddSliderRow(Strings.GpuTgp, tuple8.Item1, tuple8.Item2, tuple8.Item3, (int v) =>
		{
			Queue("GpuConfigurableTGPTarget", v.ToString());
		});
		_tgp = tuple4.Item1;
		_tgpVal = tuple4.Item2;
		// · GCU / 老接口：NVML 不是它的下发通道（写了也不生效）⇒ **连控件都不建**（ 不是建后隐藏）；
		if (hw?.UsesBaolongda ?? false)
		{
			if (NvmlHelper.TryGetPowerLimitBounds(out int plMinW, out int plMaxW, out int plCurW) && plMaxW > plMinW)
			{
				int plStart = ((plCurW >= plMinW && plCurW <= plMaxW) ? plCurW : plMaxW);
				var plRow = AddSliderRow(Strings.GpuPowerLimit, plMinW, plMaxW, plStart, (int v) =>
				{
					Queue("GpuPowerLimitW", v.ToString());
				});
				_gpuPowerLimit = plRow.Item1;
				_gpuPowerLimitVal = plRow.Item2;
				Logger.WriteLine($"[gpu-power] UI 显示功耗上限滑块：{plMinW}~{plMaxW} W（当前 {plCurW} W）");
			}
		}
		else if (Logger.TryMarkChanged("gpu-power-non-baolongda-row-skip", "[gpu-power] 非宝龙达（GCU/老接口）⇒ 不构建 NVML 功耗上限行（该机型以厂商 TGP 行为准）"))
		{
			Logger.WriteLine("[gpu-power] 非宝龙达（GCU/老接口）⇒ 不构建 NVML 功耗上限行（该机型以厂商 TGP 行为准）");
		}
		_dbChk = AddCheckRow(Strings.GpuDynamicBoost, hw?.GpuDbSwitch ?? false, (bool on) =>
		{
			Queue("GpuDynamicBoostSwitch", on ? "1" : "0");
			_db.Enabled = on;
		});
		(int, int, int, bool) tuple9 = DeviceRange(hw?.GpuDbMinimum ?? (-1), hw?.GpuDbMaximum ?? (-1), hw?.GpuDb ?? 0);
		(_db, _dbVal) = AddSliderRow(Strings.DynamicBoostValue, tuple9.Item1, tuple9.Item2, tuple9.Item3, (int v) =>
		{
			Queue("GpuDynamicBoost", v.ToString());
		});
		_dbChk.Enabled = tuple9.Item4;
		_fanSwitchSpeedChk = AddCheckRow(Strings.FanSwitchSensitivity, hw?.FanSwitchSpeedEnabled ?? false, (bool on) =>
		{
			Queue("FanSwitchSpeedEnabled", on ? "1" : "0");
			RSlider fanSwitchSpeed = _fanSwitchSpeed;
			bool enabled = (_fanSwitchSpeedVal.Enabled = on);
			fanSwitchSpeed.Enabled = enabled;
		});
		(int, int, int, bool) tuple11 = DeviceRange(hw?.FanSwitchSpeedMinimum ?? (-1), hw?.FanSwitchSpeedMaximum ?? (-1), hw?.FanSwitchSpeed ?? 0);
		(_fanSwitchSpeed, _fanSwitchSpeedVal) = AddSliderRow(Strings.FanSwitchDelay, tuple11.Item1, tuple11.Item2, tuple11.Item3, (int v) =>
		{
			Queue("FanSwitchSpeed", v.ToString());
		});
		_fanSwitchSpeed.SmallChange = (_fanSwitchSpeed.LargeChange = 100);
		_fanSwitchSpeedVal.Increment = 100m;
		_fanSwitchSpeedChk.Enabled = hw?.SupportsFanSwitchSpeed ?? false;
		_ocChk = AddCheckRow(Strings.GpuOverclock, hw?.GpuOverclockEnabled ?? false, (bool on) =>
		{
			_gpuOcArmed = on;
			// 2026-10-10（用户反馈 ④「自定义模式的显卡超频开关状态貌似不能记忆」）：
			// 原来这里**只**把开关状态 Queue 进本档位的设置记录（perf_<档>_ocon，而且必须等用户点
			// 【应用】才落盘）。全局键 gpu_oc —— SettingsForm 那颗同名开关用的、也是开机时唯一会被
			// 读回来的那一把 —— 从来不写 ⇒ 重启后"不记忆"。这里同步写 gpu_oc（**同一把键、同一语义**），
			// 让自定义模式这一行、设置里那一行、以及 ③「上次非正常关机就自动关闭」三处口径完全统一。
			// Flush() 是必要的：AppConfig.Set 只是 2s 防抖落盘，异常关机就会丢掉刚点的这一下。
			AppConfig.Set("gpu_oc", on ? 1 : 0);
			AppConfig.Flush();
			MechrevoLite.Hardware.MechrevoService.GpuOverclockEnabled = on;
			Queue("OverClockingSwitch", on ? "1" : "0");
			SyncGpuOverclockDependents();
		});
		// （机主定稿 UI 形态）：滑块固定 ±500（默认 0 居中）；数字框允许 **NV 范围**
		(int coreMin, int coreMax, int memMin, int memMax) = GpuOffsetUiRange(hw);
		// （机主：两种模具功能不要修坏）：路径自检（默认可见、去重一次）
		if (hw != null)
		{
			bool pathBaolongda = hw.UsesBaolongda;
			bool pathNvidia = hw.DgpuGeneration != DgpuGenerationKind.NoDgpu;
			int pathSwitch = AppConfig.Get("gpu_oc_nvapi_for_gcu", 0);
			string pathMold = pathBaolongda ? "宝龙达" : (pathNvidia ? "同方GCU" : "其它");
			string pathWrite = pathBaolongda ? "NVAPI" : (pathNvidia ? ((pathSwitch != 0) ? "NVAPI兜底" : "厂商字段") : "不进入");
			string pathLog = "[gpu-oc] 路径自检：模具=" + pathMold + " · 写入=" + pathWrite
				+ " · 开关 gpu_oc_nvapi_for_gcu=" + pathSwitch
				+ " · 数字框范围=core=" + coreMin + ".." + coreMax + " mem=" + memMin + ".." + memMax
				+ " · 滑块=core±500 mem=-1000..2000";
			if (Logger.TryMarkChanged("gpu-oc-path-selfcheck", pathLog))
			{
				Logger.WriteError(pathLog);
			}
		}
		int coreCurrent = hw?.EffectiveGpuCoreClockOffset ?? 0;
		int memCurrent = hw?.EffectiveGpuMemoryClockOffset ?? 0;
		(int, int, int, bool) tuple13 = DeviceRange(-500, 500, coreCurrent, allowNegative: true);
		tuple4 = AddSliderRow(Strings.CoreOffset, tuple13.Item1, tuple13.Item2, tuple13.Item3, (int v) =>
		{
			// 2026-10-09：GPU 核心偏移改为「只改数值 + 置脏」，不再拖动即下发；
			// 真正下发只在点【应用】时发生（ApplyPendingAsync —— 与面板里其余改动同一次下发）。
			MarkGpuOffsetDirty();
		}, numericMinOverride: coreMin, numericMaxOverride: coreMax, keepNumericOnSliderClamp: true);
		_coreOc = tuple4.Item1;
		_coreOcVal = tuple4.Item2;
		// 2026-10-09：文案改为「核心偏移过高，有损坏硬件的风险」——不再带占位符，故去掉 string.Format
		// （阈值不写进提示，避免窄窗口长句折行；阈值判定逻辑一字未动，见 CoreOffsetWarnThreshold/SyncOcRiskVisuals）。
		_coreOcRiskRow = MakeOcRiskRow(Strings.GpuCoreOffsetRisk);
		// （实验）：仅负偏移实验生效时显示一行小字提示（资源键， 不硬编码）
		if (hw != null
			&& hw.DgpuGeneration != DgpuGenerationKind.NoDgpu && !(hw.UsesBaolongda))
		{
			_coreOcRiskRow.Controls.Add(new RLabel
			{
				Text = Strings.GpuOcNvapiExperimentHint,
				ForeColor = UiVisualStyle.Warn,
				AutoSize = true,
				Margin = new Padding(0, D(2), 0, 0)
			});
		}
		// （机主定稿）：显存滑块 ±2000（核心仍 ±500）；数字框两者都允许 NV 范围
		(int, int, int, bool) tuple14 = DeviceRange(-1000, 2000, memCurrent, allowNegative: true);
		(_memOc, _memOcVal) = AddSliderRow(Strings.MemoryOffset, tuple14.Item1, tuple14.Item2, tuple14.Item3, (int v) =>
		{
			// 2026-10-09：同上 —— 显存偏移也只在点「应用」时下发。
			MarkGpuOffsetDirty();
		}, numericMinOverride: memMin, numericMaxOverride: memMax, keepNumericOnSliderClamp: true);
		// 2026-10-09：同上——显存提示也不带占位符（VramOffsetWarn 仍只用于下面的变红判定）。
		_memOcRiskRow = MakeOcRiskRow(Strings.VramOffsetRisk);
		ConfigureGpuOffsetSlider(_coreOc, _coreOcVal, CoreOffsetDragStep);
		ConfigureGpuOffsetSlider(_memOc, _memOcVal, VramOffsetDragStep);
		// 2026-10-09（用户要求「gpu行应用删了因为已经一个整个的应用了」）：GPU 超频那一行的行内
		// 「应用」小按钮已删除 —— 整面板只剩底部那一颗主按钮【应用】。GPU 偏移仍走 **同一套**「未应用」
		// 状态（MarkGpuOffsetDirty → _gpuOcDirty + _panelDirty），底部【应用】会把它们一起下发
		// （见 ApplyPendingAsync 里 `if (offsetsPending) fields["GpuCoreClockOffsetOC"/"GpuMemoryClockOffsetOC"]`）。
		// ⚠️ 该行第 3 列（与上方滑块行数字框对齐的那一列）重新变成空列 —— 与「温度墙/动态加速/
		// 风扇灵敏度」三行的勾选框行完全同构（空单元格不画任何东西，行底＝卡片 Surface），不留灰框/空槽。
		_ocChk.Enabled = IsOcCheckboxEnabled(hw, tuple13.Item4 || tuple14.Item4);
		_gpuOcCoreAdjustable = tuple13.Item4;
		_gpuOcMemoryAdjustable = tuple14.Item4;
		SyncGpuOverclockDependents();
		_gpuOcAdminRow = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			BackColor = UiVisualStyle.Surface,
			Margin = new Padding(0, D(2), 0, 0),
			Visible = false
		};
		// 2026-10-09：这一行的 Label 也用 RLabel —— 本行整体在「档位被锁」时会被禁用，
		// 原生 Label 在深色卡片上会被画成近黑色（见 MechrevoLite.UI.RLabel），RLabel 换成主题 Muted。
		_gpuOcAdminRow.Controls.Add(new RLabel
		{
			Text = Strings.OcNeedsAdmin,
			ForeColor = UiVisualStyle.Warn,
			AutoSize = true,
			Margin = new Padding(0, D(6), D(12), D(2))
		});
		RButton rButton3 = new RButton
		{
			Name = "buttonRestartAsAdmin",
			Text = Strings.RestartAsAdmin,
			AutoSize = true,
			Cursor = Cursors.Hand,
			Margin = new Padding(0, 0, 0, 0)
		};
		rButton3.Click += (object? _, EventArgs _) =>
		{
			RestartAsAdmin();
		};
		_gpuOcAdminRow.Controls.Add(rButton3);
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		table.Controls.Add(_gpuOcAdminRow, 0, tableRow++);
		_fanBoostCombo = new RComboBox
		{
			Name = "fanBoostCombo",
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		_fanBoostCombo.Items.AddRange(Strings.ModeTuneUnchanged, Strings.ModeTuneOn, Strings.ModeTuneOff);
		_fanBoostCombo.SelectedIndex = 0;
		_fanBoostCombo.SelectedIndexChanged += (object? _, EventArgs _) =>
		{
			if (!_syncingModes && _fanBoostCombo.SelectedIndex >= 0)
			{
				bool? on = _fanBoostCombo.SelectedIndex switch
				{
					1 => (bool?)true, 
					2 => false, 
					_ => null, 
				};
				QueueEdit((PerfModeSettings s) => s with
				{
					FanBoost = on
				});
			}
		};
		_fanBoostRow = AddComboRow(Strings.ModeTuneFanBoost, _fanBoostCombo);		// ⭐⭐ 2026-10-19（机主截图：无界 14X 自定义窗口暴露 GCU 专属行）：
		// 控件对象保留为"不在树上的占位" ⇒ 其它方法（ApplyRange/Refresh*/Apply*）访问不会 NRE
		bool noDgpu = Program.hw != null && Program.hw.DgpuGeneration == DgpuGenerationKind.NoDgpu;
		if ((Program.hw?.WujieSmuApu ?? false) || noDgpu)
		{
			void DetachRow(Control control)
			{
				if (control == null)
				{
					return;
				}
				Control row = control.Tag as Control;
				if (row != null && row.Parent != null)
				{
					row.Parent.Controls.Remove(row);
				}
				else if (control.Parent != null)
				{
					control.Parent.Controls.Remove(control);
				}
			}
			DetachRow(_pl4);
			DetachRow(_fanSwitchSpeedChk);
			DetachRow(_fanSwitchSpeed);
			DetachRow(_fanBoostRow);
			DetachRow(_ocChk);
			DetachRow(_coreOc);
			DetachRow(_memOc);
			DetachRow(_gpuOcAdminRow);
			DetachRow(_coreOcRiskRow);
			DetachRow(_memOcRiskRow);
			if (Logger.TryMarkChanged("wujie-custom-ui-skip-gcu-rows", "[custom-ui] 无独显机型（无界 APU/核显）：跳过 GCU 专属行 5 项（fppt/fansens/fandelay/fanboost/gpuoc）"))
			{
				Logger.WriteError("[custom-ui] 无独显机型（无界 APU/核显）：跳过 GCU 专属行 5 项（fppt/fansens/fandelay/fanboost/gpuoc）");
			}
		}		_refreshCombo = new RComboBox
		{
			Name = "refreshCombo",
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		_refreshCombo.Items.Add(Strings.ModeTuneUnchanged);
		_refreshCombo.SelectedIndex = 0;
		_refreshCombo.SelectedIndexChanged += (object? _, EventArgs _) =>
		{
			if (!_syncingModes && _refreshCombo.SelectedIndex >= 0)
			{
				int? hz = ((_refreshCombo.SelectedItem is int value2) ? new int?(value2) : ((int?)null));
				QueueEdit((PerfModeSettings s) => s with
				{
					RefreshHz = hz
				});
			}
		};
		_refreshRow = AddComboRow(Strings.ModeTuneRefresh, _refreshCombo);
		// 2026-10-10 第三轮（竖向）：记住参数卡片是第几行 —— 它要改成 Percent=100（吃全部余高）。
		int paramTableRowIndex = rowCount;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(table, 0, rowCount++);
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			AutoSize = true,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			BackColor = UiVisualStyle.Window
		};
		// 2026-10-10 第三轮（竖向）：按钮行也要记下来 —— 内容表的"自然高度"算式里要算它的高与 Margin
		// （见 NaturalContentHeight）。
		_buttonRow = flowLayoutPanel2;
		// 2026-10-09（用户要求「在左边加个应用 修改后点击应用才保存和下发」）：
		// 底部按钮排最左新增这颗**主按钮**「应用」——整面板唯一的下发入口（醒目：沿用 RButton 主按钮样式，
		// 见 ApplyPrimaryButton；下面的「恢复出厂/风扇曲线/CPU 调节」仍是二次按钮）。
		_applyButton = new RButton
		{
			Name = "buttonApplyCustomDetail",
			Text = Strings.Apply,
			Width = D(84),
			Height = D(28),
			Margin = new Padding(0, 0, D(14), 0),
			BackColor = UiVisualStyle.Accent,
			ForeColor = UiVisualStyle.AccentText,
			Cursor = Cursors.Hand,
			Enabled = false
		};
		_applyButton.Click += async (object? _, EventArgs _) =>
		{
			await ApplyPendingAsync();
		};
		flowLayoutPanel2.Controls.Add(_applyButton);
		// 2026-10-09（用户要求「把恢复出厂按钮改小一点」）：120x28 → 88x26，字号收一档，
		// 仍是二次按钮（与「风扇曲线/CPU 调节」同一套配色），只是更矮更窄。
		_restoreButton = new RButton
		{
			Name = "buttonRestoreModeDetail",
			Text = Strings.PerfModeRestore,
			Width = D(88),
			Height = D(26),
			Font = UiVisualStyle.Font(8.5f),
			Margin = new Padding(0, D(1), D(10), 0),
			BackColor = UiVisualStyle.SurfaceRaised,
			ForeColor = UiVisualStyle.Text,
			BorderColor = UiVisualStyle.Border,
			Cursor = Cursors.Hand
		};
		_restoreButton.Click += async (object? _, EventArgs _) =>
		{
			await RestoreDefaultsAsync();
		};
		flowLayoutPanel2.Controls.Add(_restoreButton);
		_fanCurveButton = new RButton
		{
			Name = "buttonFanCurve",
			Text = Strings.FanCurve,
			Width = D(96),
			Height = D(28),
			Margin = new Padding(0, 0, D(12), 0),
			BackColor = UiVisualStyle.SurfaceRaised,
			ForeColor = UiVisualStyle.Text,
			BorderColor = UiVisualStyle.Border,
			Cursor = Cursors.Hand
		};
		_fanCurveButton.Click += async (object? _, EventArgs _) =>
		{
			await OpenFanCurveAsync();
		};
		flowLayoutPanel2.Controls.Add(_fanCurveButton);
		// 2026-10-05 step7A (upstream 0.290.6 A-28): CPU 调节 entry.
		// Upstream shows this button unconditionally; here it is gated on the CPUID capability
		// judgement (same source as CpuInfo.IsSupportedUV()) so a machine with no writable
		// CPU-tuning route does not advertise one. LMECHREVO_FORCE_CPU_TUNING=1 overrides the
		// gate for debugging -- and ONLY the gate: everything inside the window still follows
		// RuntimeCpuTuning.Probe() / CpuTuningOption.CanWrite.
		bool cpuTuningVisible = RuntimeCpuTuning.EntryVisible;
		_cpuTuningButton = new RButton
		{
			Name = "buttonCpuTuning",
			Text = CpuTuningForm.Localized("CpuTuneTitle") + (RuntimeCpuTuning.ForceEntry ? CpuTuningForm.Localized("CpuTuneForcedMark") : ""),
			Visible = cpuTuningVisible,
			Width = D(110),
			Height = D(28),
			Margin = new Padding(0, 0, D(12), 0),
			BackColor = UiVisualStyle.SurfaceRaised,
			ForeColor = UiVisualStyle.Text,
			BorderColor = UiVisualStyle.Border,
			Cursor = Cursors.Hand
		};
		_cpuTuningButton.Click += (object? _, EventArgs _) =>
		{
			using CpuTuningForm cpuTuningForm = new CpuTuningForm();
			cpuTuningForm.ShowDialog(this);
		};
		flowLayoutPanel2.Controls.Add(_cpuTuningButton);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(flowLayoutPanel2, 0, rowCount++);
		tableLayoutPanel.RowCount = rowCount;
		// 2026-10-10 第三轮（竖向，用户原话：「窗口拉高底下空一大块」）：
		// ⭐ 参数卡片那一行从 AutoSize 改成 **Percent=100** —— 内容表被 ResizeToContent 补到视口高时，
		// 多出来的整片高度都归它（卡片 Dock=Fill ⇒ 列表区域真的填满"标题区 与 按钮行 之间"）；
		// 下面那一行按钮仍是 AutoSize ⇒ 它永远紧贴内容表底部（= 窗口底边），⛔ 按钮下方不再留死区。
		tableLayoutPanel.RowStyles[paramTableRowIndex] = new RowStyle(SizeType.Percent, 100f);
		int num3 = 0;
		using (Graphics dc = CreateGraphics())
		{
			using Font font = UiVisualStyle.TempFont(9.5f);
			foreach (string rowLabel in _rowLabels)
			{
				num3 = Math.Max(num3, TextRenderer.MeasureText(dc, rowLabel, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width);
			}
		}
		int num4 = num3 + D(8) + D(18);
		// 2026-10-10：把标题列宽留给 RewidthSliderRows 用（滑块行的第 0 列就是这个 num4）。
		_sliderRowLabelColumnDeviceWidth = num4;
		int num5 = D(104);
		// 2026-10-10 第二轮（用户原话：「修反了 文本框拉长了 滑块反而短了」）：
		// 数字框那一簇又回到**固定窄宽**（D(MinValueInputLogicalWidth) + D(30) + D(27) = 109 逻辑像素；
		// 上一批是 D(78)+D(30)+D(27) = 135 ⇒ 整窗最小宽度曾为此 +D(26)）。本轮把这 +D(26) **撤掉** ——
		// 上面 num5 = 滑块的 104 逻辑像素余地 + 这一簇 109 + 标题列，正是上一批之前的整窗最小宽度算式；
		// 数字框宽度按 96/125/150/175% 实测（见常量注释）刚好放得下本页真实取值 ⇒ 撤掉不会裁字。
		// 窗口变宽时这一簇**不变**，多出来的宽度全部落在第 1 列（Percent=100 的滑块）上 ⇒ 滑块越拉越长。
		int num6 = D(MinValueInputLogicalWidth) + D(30) + D(27);
		// ⚠️ 2026-10-10 第三轮⭐（②）：这只是**首次布局前**给第 2 列的初值；RewidthSliderRows() 会把簇宽改成
		// <c>D(52) − 死条 + D(30) + D(27)</c>（死条 = 隐藏的原生步进按钮预留，本机 175% DPI = 28；
		// 让出来的那份宽度全给滑块），并把数字框控件的 MinimumSize 顶成"列宽 + 死条"。这里保持原值还有一个用处：
		// <see cref="_minContentWidth" />（整窗最小宽度）按偏保守的 D(109) 算 ⇒ 最小宽度不会被算小、不会因此裁字。
		int val = num4 + num5 + num6;
		int val2 = _applyButton.Width + _applyButton.Margin.Horizontal + _restoreButton.Width + _restoreButton.Margin.Horizontal + _fanCurveButton.Width + _fanCurveButton.Margin.Horizontal + (cpuTuningVisible ? (_cpuTuningButton.Width + _cpuTuningButton.Margin.Horizontal) : 0) + D(8);
		int val3 = modeFlow.GetPreferredSize(Size.Empty).Width + D(8);
		_minContentWidth = Math.Max(val, Math.Max(val2, val3)) + tableLayoutPanel.Padding.Horizontal + table.Padding.Horizontal + D(8);
		foreach (TableLayoutPanel item2 in paramRows)
		{
			item2.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, num4);
			item2.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, num6);
		}
		_routeHint.MaximumSize = new Size(Math.Max(D(320), _minContentWidth - tableLayoutPanel.Padding.Horizontal), 0);
		MinimumSize = new Size(_minContentWidth + D(24), D(280));
		ClientSize = new Size(_minContentWidth, ClientSize.Height);
		// 2026-10-09（面板统一「显式应用」）：这里原来是一颗 400ms 的防抖 Timer —— 每一行改一下
		// 就 FlushPendingAsync() 立刻保存+下发。现在整面板改为「改界面 + 置脏，点【应用】才下发」，
		// 防抖定时器连同它的自动下发路径一并删除（下发只剩 ApplyPendingAsync 一个入口）。
		PerfModeService perf = PerfModeService.Instance;
		if (perf != null)
		{
			perf.ModesChanged += OnPerfModesChanged;
			perf.ActiveChanged += OnPerfActiveChanged;
			perf.Applied += OnPerfApplied;
		}
		if (Program.hw != null)
		{
			Program.hw.CustomModeChanged += OnCustomChanged;
		}
		MechrevoHw subscribedHw = Program.hw;
		FormClosed += (object? _, FormClosedEventArgs _) =>
		{
			if (perf != null)
			{
				perf.ModesChanged -= OnPerfModesChanged;
				perf.ActiveChanged -= OnPerfActiveChanged;
				perf.Applied -= OnPerfApplied;
			}
			if (subscribedHw != null)
			{
				subscribedHw.CustomModeChanged -= OnCustomChanged;
			}
			_powerWallTimer.Stop();
			_powerWallTimer.Dispose();
			_tips.Dispose();
		};
		FormClosing += (object? _, FormClosingEventArgs e) =>
		{
			if (e.CloseReason == CloseReason.UserClosing)
			{
				e.Cancel = true;
				Hide();
			}
		};
		_powerWallTimer.Tick += (object? _, EventArgs _) =>
		{
			UpdatePowerWallVerdict();
		};
		VisibleChanged += async (object? _, EventArgs _) =>
		{
			if (!Visible)
			{
				_gpuOcArmed = false;
				// 2026-10-09（面板统一「显式应用」）：关窗（本窗口是 Hide，不销毁）时**不静默下发**
				// 未应用的改动，只按既有惯例在状态行提示一句 + 打一行日志；下次显示会按「已保存的值」重载
				// （见 BindMode → LoadAppSide）。不做任何弹窗。
				if (_panelDirty)
				{
					Logger.WriteLineIfChanged("panel-dirty-hide", "自定义面板关窗时仍有未应用的改动（未下发，未回滚）");
					_status.Text = Strings.PerfModeUnappliedChanges;
					_status.ForeColor = UiVisualStyle.Warn;
				}
			}
			_powerWallTimer.Enabled = Visible && !Program.UiAuditMode;
			if (Visible && !Program.UiAuditMode)
			{
				// 2026-10-09：主按钮「应用」的强调底色会被主题重下发（UiVisualStyle.ApplyTree 的
				// 二次按钮分支 / ControlHelper.Adjust）冲掉，故每次显示时按「主按钮样式」补一次
				// （与 FeedbackForm、FanCurveForm 同一口径；只在色不对时才重下，避免重复建 Font）。
				if (_applyButton.BackColor != UiVisualStyle.Accent)
				{
					UiVisualStyle.ApplyPrimaryButton(_applyButton);
				}
				BindMode(PerfModeService.Instance?.DisplayedModeId ?? _modeId);
				ResizeToContent();
				await RefreshHardwareStateAsync();
			}
		};
		RebuildModeItems();
		BindMode(perf?.DisplayedModeId ?? PerfModeStore.ActiveModeId);
		OnCustomChanged();
		UiVisualStyle.ApplyWindow(this);
		UiVisualStyle.ApplySection(table);
		// 2026-10-09：ApplyWindow/ApplySection 的 ApplyTree 会把「不认识 tag」的 RButton 按二次按钮上色，
		// 故主按钮「应用」的样式必须在这之后下发一次（构造末尾，与 FeedbackForm 同一写法）。
		UiVisualStyle.ApplyPrimaryButton(_applyButton);
		ResizeToContent();
		RCheckBox AddCheckRow(string label, bool on, Action<bool> onChanged)
		{
			TableLayoutPanel tableLayoutPanel2 = MakeRow();
			tableLayoutPanel2.Controls.Add(MakeLabel(label, subtitle: false), 0, 0);
			RCheckBox chk = new RCheckBox
			{
				Text = Strings.EnabledLabel,
				Checked = on,
				ForeColor = UiVisualStyle.Text,
				AutoSize = true,
				Anchor = AnchorStyles.Left,
				Margin = Padding.Empty
			};
			chk.Tag = tableLayoutPanel2;
			chk.CheckedChanged += (object? _, EventArgs _) =>
			{
				if (!_syncing)
				{
					onChanged(chk.Checked);
				}
			};
			tableLayoutPanel2.Controls.Add(chk, 1, 0);
			return chk;
		}
		TableLayoutPanel AddComboRow(string label, RComboBox combo)
		{
			TableLayoutPanel row = MakeRow();
			row.Controls.Add(MakeLabel(label, subtitle: true), 0, 0);
			combo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
			combo.Margin = Padding.Empty;
			combo.Tag = row;
			row.Controls.Add(combo, 1, 0);
			row.SetColumnSpan(combo, 2);
			combo.FontChanged += (object? _, EventArgs _) =>
			{
				SyncRowHeight();
			};
			combo.HandleCreated += (object? _, EventArgs _) =>
			{
				SyncRowHeight();
			};
			SyncRowHeight();
			FitDropDown(combo);
			return row;
			void SyncRowHeight()
			{
				row.Height = Math.Max(row.Height, combo.Height);
			}
		}
		// ⭐ 未设置时给行标题追加「（未设置）」（⭐ 见 `_pl1Label` 的注释 ✅）
		string PowerWallRowLabel(string baseText, bool unset)
		{
			return unset ? (baseText + UnsetMark) : baseText;
		}

		// ⭐⭐ 2026-10-16（"未设置"）：⭐ 行标题（第 0 列）—— `AddSliderRow` 把**行面板**挂在 `bar.Tag` 上 ✅
		//   ⚠️ 故意**不改 `AddSliderRow` 的返回签名**（⭐ 它有 9 个调用点 ⚠️）⇒ ⭐ 从行面板取风险更小 ✅
		Label RowLabelOf(RSlider bar)
		{
			return (bar.Tag as TableLayoutPanel)?.GetControlFromPosition(0, 0) as Label;
		}

		// ⭐⭐ 2026-10-16（"未设置"）：⭐ 把完整语义挂成悬浮提示 ✅（⭐ 零布局代价 ✅）
		void AttachPowerWallUnsetHint(Control bar, Control val, Control label)
		{
			if (bar != null)
			{
				_tips.SetToolTip(bar, PowerWallUnsetHint);
			}
			if (val != null)
			{
				_tips.SetToolTip(val, PowerWallUnsetHint);
			}
			if (label != null)
			{
				_tips.SetToolTip(label, PowerWallUnsetHint);
			}
		}
		// 只在 30 系 SMU 路径、且**该行真的建出来 / 读到了值**时调用（ 不给不存在的行挂提示）。
		void AttachPowerWallHint(Control bar, Control val, Control label, string hint)
		{
			if (bar != null)
			{
				_tips.SetToolTip(bar, hint);
			}
			if (val != null)
			{
				_tips.SetToolTip(val, hint);
			}
			if (label != null)
			{
				_tips.SetToolTip(label, hint);
			}
		}

		(RSlider bar, RNumericUpDown valueInput) AddSliderRow(string label, int min, int max, int value2, Action<int> onChanged, int? numericMinOverride = null, int? numericMaxOverride = null, bool keepNumericOnSliderClamp = false)
		{
			TableLayoutPanel tableLayoutPanel2 = MakeRow();
			tableLayoutPanel2.Controls.Add(MakeLabel(label, subtitle: true), 0, 0);
			RSlider bar = new RSlider
			{
				Minimum = min,
				Maximum = max,
				Value = Math.Clamp(value2, min, max),
				Dock = DockStyle.Fill,
				// 2026-10-10 第四轮⭐（B）：滑块右缘与「数字框」左缘之间留 D(6)。
				// ⚠️ 用**滑块自己的右 Margin**（Dock=Fill ⇒ 滑块的宽 = 第 1 列列宽 − Margin，且仍贴列左缘）
				// ⇒ 只把滑块缩短 D(6)，簇/列/数字框/符按钮的几何与 RewidthSliderRows 的算式完全不动。
				Margin = new Padding(0, 0, D(SliderValueInputGapLogicalWidth), 0)
			};
			bar.Tag = tableLayoutPanel2;
			bar.Enabled = max > min;
			int numericMin = numericMinOverride ?? min;
			int numericMax = numericMaxOverride ?? max;
			if (numericMax < numericMin)
			{
				numericMin = min;
				numericMax = max;
			}
			RNumericUpDown valueInput = new RNumericUpDown
			{
				Minimum = numericMin,
				Maximum = numericMax,
				Value = Math.Clamp(value2, numericMin, numericMax),
				Increment = 1m,
				DecimalPlaces = 0,
				TextAlign = HorizontalAlignment.Right,
				// 2026-10-10 第四轮⭐（C，用户原话：「貌似是文本下框对齐了滑块导致文本框看着高了一点 能不能中间对齐」
				// /「如果是相对于滑块是不是水平居中好一点」）：数字框改成**在行内垂直居中**，参照物 = 滑块的水平中线。
				// ⭐ 根因（%TEMP% 探针**直接实例化本窗体**、175% DPI 实测）：`UpDownBase.SetBoundsCore` 把数字框的
				//   高**钉死**在自己的 `PreferredHeight`（本机 30 设备像素），而 Dock=Fill 把控件摆在单元格**顶部**
				//    ⇒ 行高 46 时它的中心 Y 比"行/滑块中线"高 8 设备像素（≈4.6 逻辑像素）= 用户看到的"看着高了一点"。
				// ⭐ 修法（框架自带的对齐语义，⛔ 不写死像素、不做逐行微调）：Dock 改成 None + Anchor = Left|Right ——
				//    水平仍然"拉满单元格宽"（单元格比 MinimumSize 窄 ⇒ SetBoundsCore 按 MinimumSize 夹宽、X 不动
				//    ⇒ 编辑框右缘仍落在第 0 列右缘，上一批的"感知空白 = D(6)"不退化），
				//    垂直则**既不锚 Top 也不锚 Bottom** ⇒ TableLayoutPanel 按控件自身高度在单元格里**居中**
				//    （与两颗符按钮 Anchor=Left 的居中语义完全同源）。%TEMP% 孤立探针实测：
				//    Dock=Fill → Y = 单元格顶；Anchor=Left|Right → Y = 单元格顶 + (格高−控件高)/2、X 仍 = 单元格左。
				Dock = DockStyle.None,
				Anchor = AnchorStyles.Left | AnchorStyles.Right,
				Margin = Padding.Empty,
				MinimumSize = new Size(D(MinValueInputLogicalWidth), 0),
				Enabled = (max > min)
			};
			valueInput.HideNativeSpinButtons();
			// 2026-10-09（用户原话：「这个框能不能改成纯净的文字框 这个灰色方块删一下」）：
			// `HideNativeSpinButtons()` 只把原生步进按钮**藏**起来，`UpDownBase.PositionControls()`
			// 依然给它们预留 16 设备像素 ⇒ 数字右边那块空条的底色（卡片 Surface 40/40/40）与编辑框
			// （主题 Input 20/20/20）差一格 —— 就是用户截图里的"灰色方块"。UsePlainTextBox() 把编辑框
			// 底色拉平到行底色（色差没了 ⇒ 方块消失），夜间描边保留、但换成最轻一档边色
			// （Border 56/56/56，不再是 BorderStrong 98/98/98）；**不改编辑框宽度** ⇒ 右对齐的数字
			// **原地不动**（用户 2026-10-09 的决定）。数值校验 / ± 按钮 / Enabled / ToolTip /
			// ValueChanged 一律未动（本页只有这里调用它）。
			valueInput.UsePlainTextBox();
			// 2026-10-09（用户原话：「这个文本框能不能恢复默认样式 旁边有个小方块说是 自定义界面里面的」）：
			// ⚠️ 上一段注释里"拉平底色 ⇒ 方块消失"的说法已被实测推翻：拉平底色只让那条空条与行底色**同色**，
			// 真正让它可见的是 RNumericUpDown.OnPaint 末尾那圈圆角描边 —— 它画的是**整个客户区**
			// （`Rectangle(0, 0, Width - 1, Height - 1)`），把"编辑框右边那条空条"整条框了进去，
			// 观感就是"框右边一个空的小方格"（用户截图逐像素实测：数字框的原生方框 36 设备像素、
			// 其右侧同色空条 16 设备像素、外面再套一圈 Border 56/56/56 的圆角线）。
			// 那条空条**删不掉**：`UpDownBase.PositionControls()` 每次布局都按 UpDownButtons.Width 预留
			// （%TEMP% 独立探针实测：UpDownButtons.Visible=false 时编辑框仍是 X=0/W=36；UpDownButtons.Width
			// 置 0 会被框架立刻改回 16；在 Layout 事件里回写编辑框 Bounds 也会被下一次布局覆盖）。
			// 所以本轮只去掉那圈描边：空条底色已与行底色逐分量相同（SyncEditBackColor + OnPaint 的整块填充），
			// 没有框它的线就与卡片融为一体、完全不可见；输入框自身仍保留 UiVisualStyle.ApplyTree 给内嵌
			// 编辑框画的原生 1px 方框（FixedSingle）⇒ 恢复成一只普通文本框，无空方格、无双重边框。
			// ⚠️ 只对本页这 9 个框生效（OutlineMode 是可选开关、默认 Default，其它调用点不受影响）。
			// 2026-10-10（第二轮修订）：上一轮这里写的是 SuppressInputOutline = true（**完全不描边**）。
			// 用户随后报「CPU调节里面负压的文本框框没有了」——说明"一点都不画"过了头（连编辑框都没了边）。
			// 本轮改成三态里的 EditRectOnly：**只描内嵌编辑框那块矩形**，右侧那条 16 设备像素空条落在
			// 描边之外 ⇒ 既有边框、又没有"空的小方格"（两个诉求同时满足）。本页与 CPU 页用同一个模式。
			valueInput.OutlineMode = InputOutlineMode.EditRectOnly;
			// 2026-10-10 第三轮⭐（用户原话：「还是自定义 一个是文本框右边还是空的」，用户拍板 ①+②）：
			// ⭐ 根因（%TEMP% 探针**直接实例化本窗体**、175% DPI 实测，非推断）：数字框控件里有**两条**宽度 ——
			//   控件自己的 Width（92 设备像素）与**看得见的那只编辑框**（UpDownEdit，64 设备像素）；
			//   两者之差 = 28 = 被隐藏的原生步进按钮所占的死条（≡ 16 逻辑像素，<see cref="NativeSpinReserveLogicalWidth" />）。
			//   上一轮把**控件右缘**贴到了 − 列左缘（实测 −.Left − 控件.Right = D(6)），可眼睛看到的是里面那只
			//   编辑框 ⇒ 感知空白 = 死条 + D(6) = 38 设备像素（= 上一轮"看着还是一大块"的原因）。
			// ⭐ 本轮走的正是用户最初那句「**可以把文本框右移**」：簇内第 0 列的宽 = **看得见的框宽**
			//   （= D(52) − 死条），控件再比该列**宽一个死条**（越出单元格，见 RewidthSliderRows）⇒ 编辑框右缘
			//   正好落在第 0 列右缘，− 的左 Margin D(6) 就从那里起算：感知空白 = D(6)。
			// ⚠️ 与 2026-10-09「**不改编辑框宽度、数字原地不动**」的关系（本轮口径，避免以后被误读成违背旧要求）：
			//   ⭐ 编辑框宽度**一个像素都不动**（还是 D(52) − 死条 = 64 设备像素），数字在编辑框**内部**的位置
			//   也一个像素都不动；变的只是"整簇右移一个死条"（= 用户原话「把文本框右移」）⇒ 与旧要求不冲突。
			// ⛔ 死条本身仍未动：不改内嵌编辑框 Bounds、不设 Region、不改 Dock/Anchor。
			// ⚠️ ① 的"两颗符按钮各减 D(16)"已按用户定案的验收口径（感知空白 = D(6) **且** −/+ 间距与改前一致）
			//   **回退**：叠加 ② 后第 0 列就是"看得见的框宽"，− 的 D(6) 正好从框右缘起算；若还留着 −D(16)，
			//   负 Margin 会被 TLP 夹到 0（实测）⇒ 感知空白变 0、−→+ 间距由 8 涨到 14，反而不符合口径。
			RButton decChip = MakeSpinChip("−", valueInput.DownButton, D(6));
			RButton incChip = MakeSpinChip("+", valueInput.UpButton, D(2));
			valueInput.EnabledChanged += (object? _, EventArgs _) =>
			{
				RButton rButton5 = decChip;
				bool enabled3 = (incChip.Enabled = valueInput.Enabled);
				rButton5.Enabled = enabled3;
			};
			RButton rButton4 = decChip;
			bool enabled = (incChip.Enabled = valueInput.Enabled);
			rButton4.Enabled = enabled;
			int lastSent = int.MinValue;
			bar.ValueChanged += (object? _, EventArgs _) =>
			{
				// （crash 修复 + GPU 偏移 UI）：先确保数字框范围包含滑块值再赋值
				if (!_sliderSyncFromNumeric)
				{
					if (bar.Value > valueInput.Maximum)
					{
						valueInput.Maximum = bar.Value;
					}
					else if (bar.Value < valueInput.Minimum)
					{
						valueInput.Minimum = bar.Value;
					}
					int clampedBarValue = Math.Clamp(bar.Value, (int)valueInput.Minimum, (int)valueInput.Maximum);
					try
					{
						if (valueInput.Value != (decimal)clampedBarValue)
						{
							valueInput.Value = clampedBarValue;
						}
					}
					catch (ArgumentOutOfRangeException ex)
					{
						Logger.WriteError("[rslider] ⛔ 值超范围（slider=" + bar.Value + " · numeric="
							+ valueInput.Minimum + "~" + valueInput.Maximum + "）：" + ex.Message);
					}
				}
				if (!_syncing && bar.Value != lastSent)
				{
					lastSent = bar.Value;
					onChanged(bar.Value);
				}
			};
			valueInput.ValueChanged += (object? _, EventArgs _) =>
			{
				int num7 = decimal.ToInt32(valueInput.Value);
				int sliderValue = Math.Clamp(num7, bar.Minimum, bar.Maximum);
				if (bar.Value != sliderValue)
				{
					_sliderSyncFromNumeric = true;
					try
					{
						bar.Value = sliderValue;
					}
					finally
					{
						_sliderSyncFromNumeric = false;
					}
				}
			};
			TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 3,
				RowCount = 1,
				Margin = Padding.Empty,
				BackColor = UiVisualStyle.Surface
			};
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			// 2026-10-10：两颗符按钮的列宽给一个逻辑下限 D(24) —— 100% DPI 下按钮本身是 D(22)，
			// 150% DPI 下 D(24)×1.5 = 36 设备像素 ≥ 按钮的 D(22)×1.5 ≈ 33 ⇒ 高 DPI 下也不会把 − / + 切掉。
			int decColumnWidth = Math.Max(D(30), D(24));
			int incColumnWidth = Math.Max(D(27), D(24));
			// 2026-10-10 第二轮：存下来给 RewidthSliderRows() 用（数字框宽 = 簇宽 − 这两列）。
			_sliderDecColumnDeviceWidth = decColumnWidth;
			_sliderIncColumnDeviceWidth = incColumnWidth;
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, decColumnWidth));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, incColumnWidth));
			tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tableLayoutPanel3.Controls.Add(valueInput, 0, 0);
			tableLayoutPanel3.Controls.Add(decChip, 1, 0);
			tableLayoutPanel3.Controls.Add(incChip, 2, 0);
			// 2026-10-10 第三轮⭐：数字框控件比它所在的列**宽一个死条**（见 AddSliderRow 的说明）⇒
			// 那条死条会伸到 − 按钮底下。两颗符按钮必须压在数字框**之上**：WinForms 给每个控件都设了
			// WS_CLIPSIBLINGS（%TEMP% 实测 NumericUpDown 的 GWL_STYLE = 0x56010000，含 0x04000000）
			// ⇒ 提到前面之后，下层的数字框在自己的重绘里**盖不掉**它们（⛔ 不需要 Region、也不改任何 Bounds）。
			decChip.BringToFront();
			incChip.BringToFront();
			tableLayoutPanel2.Controls.Add(bar, 1, 0);
			tableLayoutPanel2.Controls.Add(tableLayoutPanel3, 2, 0);
			// 2026-10-10 第二轮（用户原话：「修反了 文本框拉长了 滑块反而短了」）：
			// 本行第三列（数字框那一簇）的宽度在构造末尾设为**固定窄宽** D(52)+D(30)+D(27)，窗口变宽时
			// RewidthSliderRows() 也**不再**把它加宽（宽度上限就是"刚好放得下数字框 + 两颗符按钮"）——
			// 于是第 1 列（Percent=100 的滑块）吃掉行里全部余量：窗口越宽滑块越长，数字框一直窄。
			// ⭐ 数字框在这一簇里**贴住 − 那一列**（用户原话：「回归到右边空一大块了 白白改」）：
			// 第 0 列是 Percent=100，簇内多出来的宽度只会落到它身上 ⇒ 靠 RewidthSliderRows() 把
			// valueInput 的宽度钉成 <c>簇宽 − 两符按钮列宽</c>（>= D(52)，夹住不缩），
			// 数字框就**正好填满**第 0 列、右边缘紧邻 −；⛔ 不给它任何固定左偏移，也就不会在它右边留空块。
			// ⛔ 只登记这一行（滑块行）；其它行（开关/下拉/说明/按钮）不进 _sliderRows，一个像素都不动。
			tableLayoutPanel2.Tag = "slider-row";
			_sliderRows.Add(tableLayoutPanel2);
			_sliderValueInputs.Add(valueInput);
			_sliderRowsWithChips.Add(new SliderChipRow(tableLayoutPanel3, decChip, incChip));
			return (bar: bar, valueInput: valueInput);
			RButton MakeSpinChip(string glyph, Action click, int leftMargin)
			{
				RButton rButton5 = new RButton
				{
					// 2026-10-09（灰框方案 C）：数值框右侧的 −/+ 步进按钮**去掉盒子感** ——
					// 只留字形：与所在卡片（Surface）同底、不画自己那圈边框（Borderless 关掉 RButton
					// 的边色绘制），悬停仍有一档底色反馈。新 tag "spin-ghost" 在 UiVisualStyle 里
					// **夜/日两套主题都定义了同一口径**（都取 Surface、都无边框），不再借用 mini-chip
					// 那套「浅底 + 一圈边」（那套是给行内小胶囊按钮用的，换 tag 是为了不牵动它们）。
					Tag = "spin-ghost",
					Text = glyph,
					Anchor = AnchorStyles.Left,
					Margin = new Padding(leftMargin, 0, 0, 0),
					BackColor = UiVisualStyle.Surface,
					ForeColor = UiVisualStyle.Muted,
					Borderless = true,
					Cursor = Cursors.Hand
				};
				Size size = TextRenderer.MeasureText(glyph, rButton5.Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
				int num7 = Math.Max(D(22), Math.Max(size.Width + D(8), size.Height + D(6)));
				rButton5.Size = new Size(num7, num7);
				rButton5.Click += (object? _, EventArgs _) =>
				{
					click();
				};
				// 去掉盒子感之后补一条 ToolTip（否则只能靠字形猜），功能与步长（Increment=1）一字未动。
				_tips.SetToolTip(rButton5, Strings.SpinStepHint);
				return rButton5;
			}
		}
		Label MakeLabel(string text, bool subtitle)
		{
			_rowLabels.Add(text);
			// 2026-10-09（深色模式「不可选时字是黑色的」）：行标题一律用 RLabel —— 它禁用态用主题的
			// UiVisualStyle.Muted 画字；原生 Label 在父容器被禁用时会被 WinForms 画成"变暗 2/3 的底色"
			// （深色卡片 40/40/40 上就是近黑 13/13/13，读不出来），详见 MechrevoLite.UI.RLabel 的注释。
			Label label = new RLabel
			{
				Text = text,
				ForeColor = UiVisualStyle.Text,
				AutoSize = false,
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleLeft,
				Margin = Padding.Empty
			};
			if (subtitle)
			{
				label.Font = UiVisualStyle.Font(9.5f);
			}
			label.Padding = new Padding(D(4), 0, subtitle ? D(8) : 0, 0);
			return label;
		}
		RButton MakeModeButton(string name, string text, Func<Task> click)
		{
			RButton rButton4 = new RButton
			{
				Name = name,
				Text = text,
				AutoSize = true,
				Margin = new Padding(0, 0, D(4), 0),
				BackColor = UiVisualStyle.SurfaceRaised,
				ForeColor = UiVisualStyle.Text,
				BorderColor = UiVisualStyle.Border,
				Cursor = Cursors.Hand
			};
			rButton4.Click += async (object? _, EventArgs _) =>
			{
				await click();
			};
			modeFlow.Controls.Add(rButton4);
			return rButton4;
		}
		TableLayoutPanel MakeRow()
		{
			TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 3,
				RowCount = 1,
				Height = D(26),
				Margin = Padding.Empty,
				BackColor = UiVisualStyle.Surface
			};
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0f));
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0f));
			tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.Controls.Add(tableLayoutPanel2, 0, tableRow++);
			paramRows.Add(tableLayoutPanel2);
			return tableLayoutPanel2;
		}
		// 2026-10-09：核心/显存偏移的风险提示行 —— 紧跟各自滑块行、默认隐藏，超阈值时才显示
		// （与 _gpuOcAdminRow 同一套「加一行到 table」的做法）。AutoSize=false + Dock=Fill 让长文案
		// 在行宽内自动折行，且不参与 _minContentWidth 计算 ⇒ 窗口尺寸与其它行一字不变。
		TableLayoutPanel MakeOcRiskRow(string text)
		{
			TableLayoutPanel tableLayoutPanel2 = MakeRow();
			// 2026-10-09：同上 —— 行标题用 RLabel（本行目前不会被禁用，但保持与其余行同一口径）。
			Label label = new RLabel
			{
				Text = text,
				ForeColor = UiVisualStyle.Danger,
				Font = UiVisualStyle.Font(8.5f),
				AutoSize = false,
				Dock = DockStyle.Fill,
				Height = D(30),
				TextAlign = ContentAlignment.MiddleLeft,
				Margin = Padding.Empty,
				Padding = new Padding(D(4), 0, 0, 0)
			};
			tableLayoutPanel2.Controls.Add(label, 0, 0);
			tableLayoutPanel2.SetColumnSpan(label, 3);
			// ⚠️ 只隐藏「行」（子控件自身的 Visible 位保持 true）——否则行再显示时子控件仍是隐藏的。
			tableLayoutPanel2.Visible = false;
			return tableLayoutPanel2;
		}
		// 2026-10-09：只给核心/显存这两条偏移滑块开「细拖」（每 OffsetDragPixelsPerStep 逻辑像素一个刻度），
		// 并把键盘左右/上下与滚轮的步长对齐到同一刻度；其余滑块 DragStep=0 ⇒ 仍是旧的绝对定位拖法。
		void ConfigureGpuOffsetSlider(RSlider bar, RNumericUpDown valueInput, int step)
		{
			bar.DragStep = step;
			bar.DragStepPixelsLogical = OffsetDragPixelsPerStep;
			// ⚠️ 只改「拖动」这一条路径：键盘方向键/滚轮（SmallChange）与数字框 ±（Increment=1）
			// 仍是最细的 1，便于拖动到大致位置后再微调。
			// 风险呈现跟着数值走（含程序化刷新时的值），只改颜色不改数值。
			bar.ValueChanged += (object? _, EventArgs _) => SyncOcRiskVisuals();
			valueInput.ValueChanged += (object? _, EventArgs _) => SyncOcRiskVisuals();
		}
		// 2026-10-09：原 `AddGpuApplyButton()`（往「GPU 超频」那一行第 3 列塞一颗「应用」小按钮）已按用户
		// 要求删除：整面板只保留底部最左那一颗主按钮【应用】，两者本来就是同一个动作（ApplyPendingAsync）。
		// GPU 偏移的「脏值」标记与下发路径一个字都没删：MarkGpuOffsetDirty() → _gpuOcDirty + MarkPanelDirty()，
		// ApplyPendingAsync 在点【应用】时把两条偏移与其余待应用项放进**同一次**下发。
	}

	private static void FitDropDown(RComboBox combo)
	{
		int num = 0;
		foreach (object item in combo.Items)
		{
			num = Math.Max(num, TextRenderer.MeasureText(combo.GetItemText(item) ?? "", combo.Font).Width);
		}
		combo.DropDownWidth = Math.Max(combo.Width, num + 30);
	}

	private static IReadOnlyList<PerfModeDefinition> ModesSnapshot()
	{
		return PerfModeService.Instance?.Modes ?? PerfModeStore.Load();
	}

	private static PerfModeDefinition? FindMode(string? id)
	{
		if (id != null)
		{
			return ModesSnapshot().FirstOrDefault((PerfModeDefinition m) => string.Equals(m.Id, id, StringComparison.Ordinal));
		}
		return null;
	}

	internal static bool IsOffered(PerfModeDefinition mode, MechrevoHw? hw)
	{
		if (Program.UiAuditMode || hw == null)
		{
			return true;
		}
		if (IsGen40Plus(hw) && mode.Kind != PerfModeKind.Custom)
		{
			return false;
		}
		return mode.Kind switch
		{
			PerfModeKind.SilentTurbo => hw.Capabilities.SilentTurboAvailability == FeatureAvailability.Supported, 
			// 30 系按能力位判定：只有 2 档（安静/平衡）的机器不显示狂暴
			PerfModeKind.Turbo => hw.Capabilities.TurboMode || (hw.FanStatusSeen && hw.DgpuGeneration != MechrevoLite.Gpu.DgpuGenerationKind.Gen30), 
			_ => true, 
		};
	}

	internal static bool IsGen40Plus(MechrevoHw? hw)
	{
		try
		{
			string? generation = AppConfig.GetString("dgpu_generation", null);
			if (!string.IsNullOrWhiteSpace(generation))
			{
				System.Text.RegularExpressions.Match genMatch = System.Text.RegularExpressions.Regex.Match(generation, @"(\d{2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
				if (genMatch.Success && int.TryParse(genMatch.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int gen) && gen >= 40)
				{
					return true;
				}
			}
			using System.Management.ManagementObjectSearcher searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
			foreach (System.Management.ManagementObject managementObject in searcher.Get())
			{
				string? name = managementObject["Name"]?.ToString();
				if (name != null)
				{
					System.Text.RegularExpressions.Match rtxMatch = System.Text.RegularExpressions.Regex.Match(name, @"\bRTX\s*(\d{2,4})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
					if (rtxMatch.Success && int.TryParse(rtxMatch.Groups[1].Value.Substring(0, 2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int rtxGen) && rtxGen >= 40)
					{
						return true;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GPU generation detection failed: " + ex.Message);
		}
		return false;
	}

	/// <summary>2026-10-05: the perf-mode kind backing the firmware profile being edited.</summary>
	private PerfModeKind FirmwareKind()
	{
		return ((_firmwareVisualMode == 2) ? PerfModeKind.Silent : ((_firmwareVisualMode == 1) ? PerfModeKind.Turbo : PerfModeKind.Balanced));
	}

	/// <summary>
	/// 2026-10-05: point the (disabled) mode combo at the firmware mode being edited.
	/// Must run after RebuildModeItems(), which re-selects the old _modeId at its end.
	/// </summary>
	private void SyncFirmwareModeCombo()
	{
		if (!_firmwareMode)
		{
			return;
		}
		string text = PerfModeDefinition.BuiltInId(FirmwareKind());
		bool syncingModes = _syncingModes;
		_syncingModes = true;
		try
		{
			if (_modeCombo.Items.Cast<object>().All((object o) => !(o is ModeItem modeItem) || modeItem.Id != text))
			{
				RebuildModeItems();
			}
			SelectModeItem(text);
		}
		finally
		{
			_syncingModes = syncingModes;
		}
	}

	private void RebuildModeItems()
	{
		bool syncingModes = _syncingModes;
		_syncingModes = true;
		try
		{
			_modeCombo.BeginUpdate();
			_modeCombo.Items.Clear();
			MechrevoHw hw = Program.hw;
			// 编辑自定义档时，下拉只列自定义档（不混入办公/游戏/狂飙）
			bool customOnly = !_firmwareMode && (FindMode(_modeId)?.IsCustom ?? false);
			foreach (PerfModeDefinition item in ModesSnapshot())
			{
				if (customOnly)
				{
					if (!item.IsCustom)
					{
						continue;
					}
				}
				else if (!(_firmwareMode || IsOffered(item, hw) || string.Equals(item.Id, _modeId, StringComparison.Ordinal)))
				{
					continue;
				}
				_modeCombo.Items.Add(new ModeItem(item.Id, PerfModeText.ComboText(item)));
			}
			SelectModeItem(_firmwareMode ? PerfModeDefinition.BuiltInId(FirmwareKind()) : _modeId);
			_modeCombo.EndUpdate();
			FitDropDown(_modeCombo);
		}
		finally
		{
			_syncingModes = syncingModes;
		}
	}

	private void SelectModeItem(string id)
	{
		for (int i = 0; i < _modeCombo.Items.Count; i++)
		{
			if (_modeCombo.Items[i] is ModeItem modeItem && string.Equals(modeItem.Id, id, StringComparison.Ordinal))
			{
				if (_modeCombo.SelectedIndex != i)
				{
					_modeCombo.SelectedIndex = i;
				}
				break;
			}
		}
	}

	internal void BindMode(string id)
	{
		// 2026-10-04: firmware-profile mode owns the UI. These perf-mode event callbacks
		// fire after BindFirmwareProfile and would otherwise reload the custom mode.
		if (_firmwareMode)
		{
			// 2026-10-05: use the authoritative current mode, NOT the id passed in.
			// PerfModeService.DisplayedModeId stayed "balanced" while the machine was actually
			// in silent mode, so mapping from `id` rebound the editor to the wrong mode about
			// 250 ms after every open (the reported "balanced/silent swapped").
			int num = Program.service?.CurrentMode ?? _firmwareVisualMode;
			if (num != _firmwareVisualMode)
			{
				BindFirmwareProfile(num, ModeProfileConfig.Get(num));
			}
			return;
		}
		PerfModeDefinition mode = FindMode(id) ?? ModesSnapshot().FirstOrDefault();
		if ((object)mode == null)
		{
			return;
		}
		bool flag = !string.Equals(_modeId, mode.Id, StringComparison.Ordinal);
		_modeId = mode.Id;
		if (flag)
		{
			_gpuOcArmed = false;
		}
		bool syncingModes = _syncingModes;
		_syncingModes = true;
		try
		{
			// 每次绑定都重建：自定义/内置切换后下拉的过滤范围会变
			RebuildModeItems();
			SelectModeItem(mode.Id);
			Text = Strings.PerfEditorTitle + " · " + PerfModeText.Name(mode);
			IReadOnlyList<PerfModeDefinition> existing = ModesSnapshot();
			_addButton.Enabled = PerfModeCollection.CanAdd(existing);
			_renameButton.Enabled = mode.IsCustom;
			_deleteButton.Enabled = mode.IsCustom && PerfModeCollection.CanRemove(existing, mode.Id);
			if (!mode.IsCustom)
			{
				EndRename();
			}
			UpdateRouteHint(mode);
			LoadAppSide(mode.Settings);
			ApplyPersistedComboOverrides(mode.Id);
			if (!mode.IsCustom)
			{
				ApplyBuiltInSmuReadback();
			}
			// 空白的本机自定义档（如刚新建）显示「未设置」，不沿用上一次应用后的实时值
			if (mode.IsCustom && mode.Settings.IsUntouched
				&& (Program.hw?.UsesBaolongda == true || Program.hw?.WujieSmuApu == true))
			{
				ApplyBaolongdaResetVisuals(Program.hw);
			}
			// 2026-10-09：换模式/档位后屏幕上已是另一份（已保存的）值 ⇒ 未应用的改动作废（不下发、不静默应用）。
			DiscardPendingChanges();
		}
		finally
		{
			_syncingModes = syncingModes;
		}
		SetOptionsEnabled();
		if (IsHandleCreated)
		{
			ResizeToContent();
		}
	}

	/// <summary>
	/// 2026-10-04: open this editor against a 30-series firmware profile.
	/// visualMode uses LM numbering (0=balanced/gaming, 1=turbo, 2=silent/office).
	/// Read-only for now: commits are suppressed until the MQTT write path lands.
	/// </summary>
	/// <summary>30 系「模式 × 配置」在 LM 侧的存储 id（perf_fw{视觉模式}p{配置号}_*）。</summary>
	private static string FirmwareProfileStoreId(int visualMode, int profileIndex)
	{
		return "fw" + visualMode.ToString() + "p" + profileIndex.ToString();
	}

	internal void BindFirmwareProfile(int visualMode, int profileIndex)
	{
		if (_bindingFirmwareProfile)
		{
			Logger.WriteLine("BindFirmwareProfile: re-entrant call ignored");
			return;
		}
		_bindingFirmwareProfile = true;
		_firmwareMode = true;
		_firmwareVisualMode = visualMode;
		// 2026-10-05: track the built-in id too -- OnPerfActiveChanged only calls
		// BindMode() when the displayed id differs from _modeId, so leaving _modeId
		// stale meant mode switches never reached us.
		_modeId = PerfModeDefinition.BuiltInId((visualMode == 2) ? PerfModeKind.Silent : ((visualMode == 1) ? PerfModeKind.Turbo : PerfModeKind.Balanced));
		_firmwareProfileIndex = Math.Clamp(profileIndex, 0, VendorModeDefaults.MaxProfileCount - 1);
		PerfModeKind kind = visualMode switch
		{
			2 => PerfModeKind.Silent,
			1 => PerfModeKind.Turbo,
			_ => PerfModeKind.Balanced,
		};
		// 2026-10-05: diagnostics for the reported "balanced/silent shown swapped" issue.
		Logger.WriteLine($"BindFirmwareProfile: visualMode={visualMode} kind={kind} " +
			$"current={Program.service?.CurrentMode} comboId={PerfModeDefinition.BuiltInId(kind)} " +
			$"label={(visualMode == 2 ? Strings.Silent : (visualMode == 1 ? Strings.Turbo : Strings.Balanced))}");
		bool syncingModes = _syncingModes;
		_syncingModes = true;
		try
		{
			// 2026-10-05: also read the profile's user-facing name (CustomizeName).
			// 2026-10-05: name comes from LM's own store, not from the profile JSON.
			_firmwareProfileName = ModeProfileConfig.GetName(_firmwareVisualMode, _firmwareProfileIndex);
			UpdateFirmwareTitle();
			PerfModeSettings storedProfile = PerfModeStore.LoadSettings(FirmwareProfileStoreId(_firmwareVisualMode, _firmwareProfileIndex));
			LoadAppSide(storedProfile.IsUntouched
				? VendorModeDefaults.ComposeProfile(kind, Program.hw, _firmwareProfileIndex)
				: storedProfile);
			// 2026-10-09：固件档位切换同样作废未应用的改动（不下发）。
			DiscardPendingChanges();
		}
		finally
		{
			_syncingModes = syncingModes;
		}
		_addButton.Enabled = false;
		_deleteButton.Enabled = false;
		// 2026-10-04: restore-default / fan-curve left ENABLED per user request.
		SetOptionsEnabled();
		// 2026-10-04: SetOptionsEnabled() re-enables these according to the mode kind,
		// so re-apply the locks AFTER it -- and lock the mode combo too.
		_modeCombo.Enabled = false;
		_addButton.Enabled = false;
		_deleteButton.Enabled = false;
		// 2026-10-05: rename IS meaningful for a firmware profile (CustomizeName), and
		// nothing else enables it in this mode because BindMode is skipped.
		_renameButton.Enabled = true;
		// 2026-10-05: sync the mode combo LAST -- RebuildModeItems() runs in between and
			// would otherwise leave the combo on the previously selected mode.
		SyncFirmwareModeCombo();
		if (IsHandleCreated)
		{
			ResizeToContent();
		}
		_bindingFirmwareProfile = false;
	}

	private void SetOptionsEnabled()
	{
		MechrevoHw? hw = Program.hw;
		PerfModeDefinition? mode = FindMode(_modeId);
		// 2026-10-04: firmware-profile mode edits a *preset* firmware config, which is the
		// normal 30-series behaviour. Keep the Gen40+ preset lock for regular LM use.
		// ⭐⭐ 2026-10-17（宝龙达 · 机主反馈「滑块是灰的」）：⭐ 这条锁的**原始理由**是
		//   「Gen40+ 的预设档由固件（GCU）持有，改 LM 里的值不会生效、还会被固件顶回去」⚠️
		//   ⇒ ⭐ 宝龙达**没有 GCU** ⇒ 这个理由不成立；而且它的 PL/温度墙**能走 WMI 23 真下发**
		//      （见 `PerfModeCapabilities.SupportsWmiPowerFields`）⇒ ⭐ 不该再锁 ✅
		//   ⚠️ 加 `hw.IsGcuConnected` 判据：GCU 机型（含 Gen40+）**逐位不变** ✅
		bool locked = hw != null && hw.IsGcuConnected && IsGen40Plus(hw) && (mode == null || !mode.IsCustom) && !_firmwareMode;
		_optionsLocked = locked;
		foreach (Control control in OptionControls())
		{
			if (control != null)
			{
				control.Enabled = !locked;
			}
		}
		// 2026-10-09：上面是按「锁定」统一开关；「应用」按钮的可用性还要叠加「是否有未应用的改动」。
		SyncApplyButtons();
	}

	/// <summary>
	/// 2026-10-09（非自定义档下「风扇增强 / 屏幕刷新率」被灰掉）：本列表是<i>硬件参数锁</i>的名单 ——
	/// 锁只对「由固件档位（GCU）持有的硬件参数」成立，**不包括应用侧设置**。
	/// 「风扇增强」「屏幕刷新率」两行属于后者：它们的值是 <see cref="PerfModeSettings" /> 的
	/// FanBoost / RefreshHz 这类覆盖值，下发走 <see cref="PerfModeApplyPlanner" /> 里独立的
	/// ApplyFanBoost / ApplyRefreshRate 步骤（见 PerfModeApplyPlanner.Build 末尾），与**从不进本列表**的
	/// 「Windows 电源模式 / 电源计划 / 睿频」三行同一类。此前把 <c>_fanBoostCombo/_refreshCombo</c>
	/// （以及它们所在的行面板 <c>_fanBoostRow/_refreshRow</c>；行面板一旦禁用会连行标题 Label 一起被
	/// WinForms 画成近黑色，见 <see cref="RLabel" />）放进这里，于是 Gen40+ 机型的非自定义档
	/// （<c>_optionsLocked</c>）里这两行被整行禁用 ⇒ 用户报的「非自定义模式下这两个不该灰色」。
	/// 现按同一口径移出：硬件参数行照旧锁定，应用侧行照旧可改、可点【应用】下发。
	/// </summary>
	private Control[] OptionControls()
	{
		return new Control[]
		{
			_pl1, _pl2, _pl4, _tcc, _tgp, _db, _coreOc, _memOc, _fanSwitchSpeed,
			// （30 系 · SMU 功耗时间窗）：本机没有该行时为 null（下面的循环会跳过 null）
			_stapmTime,
			_pl1Val, _pl2Val, _pl4Val, _tccVal, _tgpVal, _dbVal, _coreOcVal, _memOcVal, _fanSwitchSpeedVal,
			_stapmTimeVal,
			_dbChk, _tccChk, _ocChk, _fanSwitchSpeedChk,
			_fanCurveButton, _gpuOcAdminRow,
			_applyButton
		};
	}

	private void UpdateRouteHint(PerfModeDefinition mode)
	{
		// —— 在宝龙达上**两句都不成立**（它根本没有固件自定义档，却**能**真下发本机后端）❌
		if ((Program.hw?.UsesBaolongda == true) || (Program.hw?.WujieSmuApu == true))
		{
			string baolongdaHint = LocalApplyText;
			if (_routeHint.Text != baolongdaHint)
			{
				_routeHint.Text = baolongdaHint;
			}
			return;
		}
		int? num = PerfModeService.Instance?.SlotOf(mode.Id);
		string text;
		if (!mode.IsCustom)
		{
			if (!mode.IsEmulatedBuiltIn)
			{
				text = Strings.PerfModeHintVendor;
			}
			else
			{
				string perfModeHintEmulated = Strings.PerfModeHintEmulated;
				string arg;
				if (num.HasValue)
				{
					int valueOrDefault = num.GetValueOrDefault();
					arg = (valueOrDefault + 1).ToString();
				}
				else
				{
					arg = "?";
				}
				text = string.Format(perfModeHintEmulated, arg);
			}
		}
		else if (num.HasValue)
		{
			int valueOrDefault2 = num.GetValueOrDefault();
			text = string.Format(Strings.PerfModeHintCustom, valueOrDefault2 + 1);
		}
		else
		{
			text = Strings.PerfModeHintCustomUnassigned;
		}
		string text2 = text;
		if (_routeHint.Text != text2)
		{
			_routeHint.Text = text2;
		}
	}

	/// <summary>内置档：本机配置机型从 SMU 快照回填「慢速功率/慢速时长」；读不到显示 0。</summary>
	private void ApplyBuiltInSmuReadback()
	{
		MechrevoHw hw = Program.hw;
		if (hw == null || (!hw.WujieSmuApu && !hw.IsBaolongda30Series))
		{
			return;
		}
		try
		{
			hw.RefreshBaolongda30SeriesSmuPower(force: true);
		}
		catch
		{
		}
		PawnIO.SmuPowerSnapshot snap = hw.BaolongdaSmuSnapshot;
		if (snap == null && hw.WujieSmuApu)
		{
			try
			{
				snap = MechrevoLite.Mode.ModeControl.SharedSmu?.GetPowerSnapshot();
			}
			catch
			{
			}
		}
		bool syncing = _syncing;
		_syncing = true;
		try
		{
			ApplySmuSnapshotValue(_smuSlowLimit, _smuSlowLimitVal, snap?.Slow);
			ApplySmuSnapshotValue(_smuSlowTime, _smuSlowTimeVal, snap?.SlowTimeSeconds);
			Logger.WriteLine("[custom-ui] 内置档 SMU 回填：slow=" + (snap?.Slow.ToString("F1") ?? "读不到") + "W · slow_time=" + (snap?.SlowTimeSeconds?.ToString() ?? "读不到") + "s");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[custom-ui] SMU 回填失败（慢速行显示 0）：" + ex.Message);
		}
		finally
		{
			_syncing = syncing;
		}
	}

	private static void ApplySmuSnapshotValue(RSlider slider, RNumericUpDown box, float? value)
	{
		if (slider == null)
		{
			return;
		}
		try
		{
			int v = (value.HasValue && value.Value > 0f) ? (int)Math.Round(value.Value) : 0;
			int lo = (int)slider.Minimum;
			int hi = (int)slider.Maximum;
			int shown = Math.Min(hi, Math.Max(lo, v));
			slider.Value = shown;
			if (box != null)
			{
				decimal boxShown = Math.Min(box.Maximum, Math.Max(box.Minimum, (decimal)v));
				box.Value = boxShown;
			}
		}
		catch
		{
		}
	}

	private void LoadAppSide(PerfModeSettings s)
	{
		RComboBox powerModeCombo = _powerModeCombo;
		int? windowsPowerMode = s.WindowsPowerMode;
		int valueOrDefault;
		if (windowsPowerMode.HasValue)
		{
			valueOrDefault = windowsPowerMode.GetValueOrDefault();
			switch (valueOrDefault)
			{
			case 0:
			case 1:
			case 2:
				goto IL_0029;
			}
		}
		int selectedIndex = 0;
		goto IL_002c;
		IL_0076:
		RComboBox boostCombo;
		int selectedIndex2;
		boostCombo.SelectedIndex = selectedIndex2;
		RComboBox fanBoostCombo = _fanBoostCombo;
		bool? fanBoost = s.FanBoost;
		int selectedIndex3 = (fanBoost.HasValue ? ((fanBoost == true) ? 1 : 2) : 0);
		fanBoostCombo.SelectedIndex = selectedIndex3;
		RebuildRefreshItems(s.RefreshHz);
		return;
		IL_0029:
		selectedIndex = valueOrDefault + 1;
		goto IL_002c;
		IL_002c:
		powerModeCombo.SelectedIndex = selectedIndex;
		SelectPlan(s.PowerPlanGuid);
		boostCombo = _boostCombo;
		windowsPowerMode = s.CpuBoost;
		if (windowsPowerMode.HasValue)
		{
			int valueOrDefault2 = windowsPowerMode.GetValueOrDefault();
			if (valueOrDefault2 + 1 < _boostCombo.Items.Count)
			{
				selectedIndex2 = valueOrDefault2 + 1;
				goto IL_0076;
			}
		}
		selectedIndex2 = 0;
		goto IL_0076;
	}

	/// <summary>按持久化档值回显模式下拉（电源模式/电源计划/睿频/风扇增强/刷新率）。</summary>
	private void ApplyPersistedComboOverrides(string modeId)
	{
		PerfModeSettings persisted = PerfModeStore.LoadSettings(modeId);
		bool syncingModes = _syncingModes;
		_syncingModes = true;
		try
		{
			int? power = persisted.WindowsPowerMode;
			if (power.HasValue && power.Value >= 0 && power.Value <= 2 && _powerModeCombo.Items.Count > power.Value + 1)
			{
				_powerModeCombo.SelectedIndex = power.Value + 1;
			}
			if (!string.IsNullOrWhiteSpace(persisted.PowerPlanGuid))
			{
				SelectPlan(persisted.PowerPlanGuid);
			}
			int? boost = persisted.CpuBoost;
			if (boost.HasValue && boost.Value + 1 < _boostCombo.Items.Count)
			{
				_boostCombo.SelectedIndex = boost.Value + 1;
			}
			if (persisted.FanBoost.HasValue)
			{
				_fanBoostCombo.SelectedIndex = (persisted.FanBoost.Value ? 1 : 2);
			}
			if (persisted.RefreshHz.HasValue)
			{
				RebuildRefreshItems(persisted.RefreshHz);
			}
		}
		finally
		{
			_syncingModes = syncingModes;
		}
	}

	private void SelectPlan(string? guid)
	{
		bool syncingModes = _syncingModes;
		_syncingModes = true;
		try
		{
			int selectedIndex = 0;
			if (Guid.TryParse(guid, out var result))
			{
				for (int i = 1; i < _planCombo.Items.Count; i++)
				{
					if (_planCombo.Items[i] is KeyValuePair<string, string> keyValuePair && Guid.TryParse(keyValuePair.Value, out var result2) && result2 == result)
					{
						selectedIndex = i;
						break;
					}
				}
				if (selectedIndex == 0)
				{
					selectedIndex = _planCombo.Items.Add(new KeyValuePair<string, string>(Strings.PowerPlanUnavailable, result.ToString()));
				}
			}
			_planCombo.SelectedIndex = selectedIndex;
		}
		finally
		{
			_syncingModes = syncingModes;
		}
	}

	private void SelectPowerMode(int? power)
	{
		bool syncingModes = _syncingModes;
		_syncingModes = true;
		try
		{
			RComboBox powerModeCombo = _powerModeCombo;
			int valueOrDefault;
			if (power.HasValue)
			{
				valueOrDefault = power.GetValueOrDefault();
				switch (valueOrDefault)
				{
				case 0:
				case 1:
				case 2:
					goto IL_0030;
				}
			}
			int selectedIndex = 0;
			goto IL_0033;
			IL_0033:
			powerModeCombo.SelectedIndex = selectedIndex;
			return;
			IL_0030:
			selectedIndex = valueOrDefault + 1;
			goto IL_0033;
		}
		finally
		{
			_syncingModes = syncingModes;
		}
	}

	/// <summary>
	/// 2026-10-09（用户原话「电源计划那个先回退 因为新的调不出高性能 省电和卓越性能」）：
	/// ⭐「电源计划」这一行恢复旧版 18.6 的**选中即生效**（旧版 CustomModeForm.cs:255-293）：
	/// 先 SetActivePlan 立即下发到系统，再 SetBoost 确认睿频，最后才落盘；失败就不落盘。
	/// 落盘走新引擎的键（perf_customN_plan / perf_customN_boost，由 PerfModeService 统一写，不碰旧键），
	/// 且用 applyHardware:false —— 系统侧刚才已经由本行直接下发，不必再让 Runner 把整档重下发一遍
	/// （也就不会因为这一次改方案而写固件/EC）。其余行仍是「显式应用」。
	/// 失败语义照旧版：方案没确认 ⇒ 一个键都不落盘、控件回到该档已保存值，提示「电源计划未确认，未保存」；
	/// 方案确认了但睿频没确认 ⇒ 只落方案键，提示「已确认，睿频未确认」；两者都确认才落睿频键。
	/// </summary>
	private async Task ApplyPowerPlanRowAsync()
	{
		if (IsDisposed)
		{
			return;
		}
		string? guid = GetPowerPlanGuid(_planCombo.SelectedItem);
		bool nonBalanced = guid != null && !PerfModeApplyPlanner.IsBalancedPlan(guid);
		int boostIndex = _boostCombo.SelectedIndex;
		int? boost = ((boostIndex > 0) ? new int?(boostIndex - 1) : ((int?)null));
		if (nonBalanced)
		{
			SelectPowerMode(null);
		}
		bool planConfirmed = ((guid == null) ? true : WinPowerPlan.SetActivePlan(guid));
		bool boostConfirmed = planConfirmed && (!boost.HasValue || WinPowerPlan.SetBoost(boost.Value));
		if (!planConfirmed)
		{
			Logger.WriteLine($"Custom mode {_modeId} power plan not confirmed, nothing saved: {guid}");
			SelectPlan(FindMode(_modeId)?.Settings.PowerPlanGuid);
			_status.Text = Strings.PowerPlanUnconfirmedNotSaved;
			_status.ForeColor = UiVisualStyle.Danger;
			return;
		}
		PerfModeService perf = PerfModeService.Instance;
		if (perf == null)
		{
			_status.Text = Strings.ParamsSaveFailed;
			_status.ForeColor = UiVisualStyle.Danger;
			return;
		}
		// 电源计划与电源模式互斥（选非平衡方案 ⇒ 该档 overlay 置空）：把这一行刚登记、还没应用的
		// 电源模式改动一起摘掉，免得稍后点【应用】时它又把刚设好的方案清回 null。
		if (nonBalanced && _pendingPowerModeEdit != null && _pendingEdits.Remove(_pendingPowerModeEdit))
		{
			_pendingPowerModeEdit = null;
			_panelDirty = _pending.Count > 0 || _pendingEdits.Count > 0 || _gpuOcDirty;
			SyncApplyButtons();
		}
		bool saveBoost = boostConfirmed && boost.HasValue;
		await _saveLock.WaitAsync();
		try
		{
			await perf.UpdateAsync(_modeId, (PerfModeSettings s) => s with
			{
				PowerPlanGuid = guid,
				WindowsPowerMode = (nonBalanced ? ((int?)null) : s.WindowsPowerMode),
				CpuBoost = (saveBoost ? boost : s.CpuBoost)
			}, "editor-power-plan", applyHardware: false);
			if (IsDisposed)
			{
				return;
			}
			if (!boostConfirmed)
			{
				int? savedBoost = FindMode(_modeId)?.Settings.CpuBoost;
				bool syncingModes = _syncingModes;
				_syncingModes = true;
				try
				{
					_boostCombo.SelectedIndex = ((savedBoost.HasValue && savedBoost.Value + 1 < _boostCombo.Items.Count) ? (savedBoost.Value + 1) : 0);
				}
				finally
				{
					_syncingModes = syncingModes;
				}
				Logger.WriteLine($"Custom mode {_modeId} power plan saved, turbo boost not confirmed: {guid}");
				_status.Text = Strings.PowerPlanConfirmedTurboUnconfirmed;
				_status.ForeColor = UiVisualStyle.Warn;
				return;
			}
			Logger.WriteLine($"Custom mode {_modeId} power plan applied & saved: plan={guid ?? "(unset)"} boost={(boost.HasValue ? boost.Value.ToString() : "(unset)")}");
			_status.Text = Strings.PowerPlanTurboConfirmed;
			_status.ForeColor = UiVisualStyle.Ok;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Power plan row apply failed: " + ex.Message);
			_status.Text = Strings.ParamsSaveFailed;
			_status.ForeColor = UiVisualStyle.Danger;
		}
		finally
		{
			_saveLock.Release();
		}
	}

	private void RebuildRefreshItems(int? selectedHz)
	{
		bool syncingModes = _syncingModes;
		_syncingModes = true;
		try
		{
			IReadOnlyList<int> source = Program.hw?.HzList ?? Array.Empty<int>();
			List<object> list = new List<object> { Strings.ModeTuneUnchanged };
			foreach (int item in from hz in source.Where((int hz) => hz > 0).Distinct()
				orderby hz
				select hz)
			{
				list.Add(item);
			}
			if (selectedHz.HasValue && selectedHz.GetValueOrDefault() > 0 && !list.OfType<int>().Contains(selectedHz.Value))
			{
				list.Add(selectedHz.Value);
			}
			if (list.Count != _refreshCombo.Items.Count || !list.Select((object o, int i) => object.Equals(o, _refreshCombo.Items[i])).All((bool x) => x))
			{
				_refreshCombo.BeginUpdate();
				_refreshCombo.Items.Clear();
				foreach (object item2 in list)
				{
					_refreshCombo.Items.Add(item2);
				}
				_refreshCombo.EndUpdate();
			}
			int selectedIndex = 0;
			if (selectedHz.HasValue && selectedHz.GetValueOrDefault() > 0)
			{
				for (int num = 1; num < _refreshCombo.Items.Count; num++)
				{
					if (_refreshCombo.Items[num] is int num2 && num2 == selectedHz.Value)
					{
						selectedIndex = num;
						break;
					}
				}
			}
			_refreshCombo.SelectedIndex = selectedIndex;
		}
		finally
		{
			_syncingModes = syncingModes;
		}
	}

	internal async Task<bool> SelectModeAsync(string id)
	{
		// 2026-10-09（面板统一「显式应用」）：切档**不再**先把未应用的改动下发（原来是
		// `_debounce.Stop(); await FlushPendingAsync();` —— 等于替用户点了一次应用）。
		// 现在：① 未点【应用】的临时改动一律作废，只提示 + 记日志，**不静默下发**；
		// ② 切档本身由下面的 ActivateModeAsync 按「该档**已保存**的值」**强制全量下发一次**。
		// 2026-10-09（用户第二次明确要求「我还是要求切自定义的时候下发一次」）：切档**一律强制全量下发一次** ——
		// ActivateModeAsync 传 `forceParameters: true`，走 PerfModeService.ActivateLockedAsync 里既有的
		// `forced rewrite` 分支（`plan = plan with { NeedsParameterWrite = true, Reason = "forced rewrite" }`），
		// 于是**即使 slot hit 也会把这批字段真正重写一遍**（含两条风扇曲线），不再"只切槽"。
		// 下发链一字未换：仍是 PerfModeApplyPlanner → PerfModeRunner → MechrevoService.SetCustomDetail
		//（所以 (B) 逐字段埋点 `issuing WriteFirmwareField` 与 (C) 的超时/重发照旧生效、未被绕过）。
		// ⚠️ 与"开窗即下发"无关：本方法**只**由「编辑器模式下拉改选」与「主界面【自定义】按钮」两个
		// **用户显式动作**调用；打开/显示编辑器窗口走的是 BindMode（只读绑定），不经过这里。
		if (_panelDirty)
		{
			Logger.WriteLineIfChanged("panel-dirty-switch", "切档丢弃未应用的改动（没有下发）→ " + id);
			_status.Text = Strings.PerfModeUnappliedChanges;
			_status.ForeColor = UiVisualStyle.Warn;
		}
		DiscardPendingChanges();
		BindMode(id);
		// （它必然失败并把界面回弹到别的档，用户看到"选了又跳回去"）
		if (Program.hw?.UsesBaolongda ?? false)
		{
			// 选中哪一档就把「当前档」切到哪一档（否则后续刷新会按 ActiveModeId 绑回第一个自定义档）
			PerfModeService perfBd = PerfModeService.Instance;
			perfBd?.ActivateLocalProfile(id);
			// 切档后强制从存储重读该档配置并刷新一次界面（避免界面停留在上一档的值）
			perfBd?.ReloadModesFromStore();
			BindMode(id);
			_status.Text = Strings.PerfModeLocalProfileSwitched;
			_status.ForeColor = UiVisualStyle.Muted;
			Logger.WriteLine("[perfmode] 宝龙达：模式下拉切到 " + id + "（本机配置，无固件档可切；已重读配置并刷新界面）");
			return true;
		}
		// （机主：无界 APU 点自定义后模式仍显示「平衡」+ 红字）：与宝龙达同款「本机配置」分支 ——
		if (Program.hw?.WujieSmuApu ?? false)
		{
			PerfModeService perf = PerfModeService.Instance;
			perf?.ActivateLocalProfile(id);
			// 切档后强制从存储重读该档配置并刷新一次界面（避免界面停留在上一档的值）
			perf?.ReloadModesFromStore();
			BindMode(id);
			_status.Text = Strings.PerfModeLocalProfileSwitched;
			_status.ForeColor = UiVisualStyle.Muted;
			Logger.WriteLine("[perfmode] 无界 APU：模式下拉切到 " + id + "（本机配置，无固件档可切；已重读配置并刷新界面）");
			return true;
		}
		bool ok = await ActivateModeAsync(id);
		if (ok)
		{
			await RefreshHardwareStateAsync();
		}
		return ok;
	}

	/// <summary>
	/// 2026-10-09：显式切档 = **按该档已保存的值强制全量下发一次**（`forceParameters: true`）。
	/// 调用者只有两个用户显式入口：编辑器里的「模式」下拉改选、主界面【自定义】按钮
	/// （SettingsForm.customModeForm.SelectModeAsync）。打开编辑器窗口**不**走这里。
	/// </summary>
	internal async Task<bool> ActivateModeAsync(string id)
	{
		PerfModeService instance = PerfModeService.Instance;
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.IsConnected || instance == null)
		{
			_status.Text = GcuDisconnectedText;
			_status.ForeColor = UiVisualStyle.Danger;
			return false;
		}
		_gpuOcArmed = false;
		_status.Text = SwitchPendingText;
		_status.ForeColor = UiVisualStyle.Warn;
		// 2026-10-09（用户要求，可观测）：切档＝强制全量下发一次。这一行是**意图**埋点
		//（`forceParameters=true`），后面 PerfModeService 既有的 `PerfModes: activate <id> (<reason>)
		// route=… slot=… write=True steps=N` 与 PerfModeRunner 的 `issuing WriteFirmwareField: …`
		// 是**执行**埋点 —— 三行连起来即可一眼确认"这次切档真的把字段重写了一遍"。
		PerfModeDefinition? switchTarget = FindMode(id);
		Logger.WriteLine($"PerfModes: switch-to-custom apply (forceParameters=true) mode={id} reason=editor kind={(switchTarget == null ? "?" : switchTarget.Kind.ToString())} signature={(switchTarget == null ? "?" : switchTarget.Settings.FirmwareSignature())} —— 按该档已保存的值全量重写一次（含风扇曲线）");
		PerfApplyOutcome perfApplyOutcome = await instance.ActivateAsync(id, "editor", forceParameters: true);
		if (IsDisposed)
		{
			return false;
		}
		if ((object)perfApplyOutcome == null)
		{
			return false;
		}
		ShowOutcome(perfApplyOutcome);
		return perfApplyOutcome.ModeSwitched;
	}

	private async Task AddCustomModeAsync()
	{
		PerfModeService instance = PerfModeService.Instance;
		if (instance != null)
		{
			PerfModeDefinition perfModeDefinition = FindMode(_modeId);
			string arg = (((object)perfModeDefinition != null) ? PerfModeText.Name(perfModeDefinition) : "");
			// 宝龙达：新建不继承当前档数据（否则连续新建会一路复制成第一个自定义档的样子）
			string seedFrom = (Program.hw?.UsesBaolongda == true) ? null : _modeId;
			PerfModeDefinition perfModeDefinition2 = instance.AddCustom(seedFrom);
			if ((object)perfModeDefinition2 == null)
			{
				_status.Text = string.Format(Strings.PerfModeLimitReached, 12);
				_status.ForeColor = UiVisualStyle.Warn;
				return;
			}
			RebuildModeItems();
			_status.Text = string.Format(Strings.PerfModeCreated, PerfModeText.Name(perfModeDefinition2), arg);
			_status.ForeColor = UiVisualStyle.Ok;
			await SelectModeAsync(perfModeDefinition2.Id);
		}
	}

	/// <summary>2026-10-05: title for the firmware-profile editor, including the custom name.</summary>
	private void UpdateFirmwareTitle()
	{
		string text = ((_firmwareVisualMode == 2) ? Strings.Silent : ((_firmwareVisualMode == 1) ? Strings.Turbo : Strings.Balanced));
		string text2 = Strings.PerfEditorTitle + " \u00b7 " + text + " \u00b7 " + Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1);
		if (!string.IsNullOrWhiteSpace(_firmwareProfileName))
		{
			text2 = text2 + " \u00b7 " + _firmwareProfileName;
		}
		Text = text2;
		// 2026-10-05: the profile line lives in the hint label -- _status belongs to
		// operation results and would overwrite it as soon as anything is applied.
		_routeHint.Text = Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1)
			+ (string.IsNullOrWhiteSpace(_firmwareProfileName) ? "" : (" \u00b7 " + _firmwareProfileName))
			+ " \u00b7 " + Strings.PerfModeHintVendor;
	}

	/// <summary>
	/// 2026-10-05: save the custom name of the firmware profile being edited.
	/// Same rule as the parameters: select (mode, profile) first, then write.
	/// </summary>
	private async Task SaveFirmwareProfileNameAsync()
	{
		string text = _nameBox.Text.Trim();
		if (text.Length > 12)
		{
			_status.Text = string.Format(Strings.PerfModeNameTooLong, 12);
			_status.ForeColor = UiVisualStyle.Danger;
			return;
		}
		if (text.Length == 0)
		{
			_status.Text = Strings.ProfileNameNotConfirmed;
			_status.ForeColor = UiVisualStyle.Warn;
			return;
		}
		// 2026-10-05: the GCU cannot persist a name for a preset profile, so store it in LM.
		ModeProfileConfig.SetName(_firmwareVisualMode, _firmwareProfileIndex, text);
		bool flag = true;
		_firmwareProfileName = text;
		EndRename();
		UpdateFirmwareTitle();
		_status.Text = (flag ? Strings.ProfileNameSaved : Strings.ProfileNameNotConfirmed);
		_status.ForeColor = (flag ? UiVisualStyle.Ok : UiVisualStyle.Warn);
	}

	private void BeginRename()
	{
		// 2026-10-05: firmware profiles are named too (CustomizeName), so allow rename there.
		if (_firmwareMode)
		{
			_nameBox.Text = (_firmwareProfileName ?? "");
			_nameRow.Visible = true;
			ResizeToContent();
			_nameBox.Focus();
			_nameBox.SelectAll();
			return;
		}
		PerfModeDefinition perfModeDefinition = FindMode(_modeId);
		if ((object)perfModeDefinition != null && perfModeDefinition.IsCustom)
		{
			_nameBox.Text = PerfModeText.Name(perfModeDefinition);
			_nameRow.Visible = true;
			ResizeToContent();
			_nameBox.Focus();
			_nameBox.SelectAll();
		}
	}

	private void EndRename()
	{
		if (_nameRow.Visible)
		{
			_nameRow.Visible = false;
			if (IsHandleCreated)
			{
				ResizeToContent();
			}
		}
	}

	private async Task SaveNameAsync()
	{
		if (_firmwareMode)
		{
			await SaveFirmwareProfileNameAsync();
			return;
		}
		PerfModeService perf = PerfModeService.Instance;
		if (perf != null)
		{
			PerfModeDefinition mode = FindMode(_modeId);
			if ((object)mode != null && mode.IsCustom)
			{
				string text = _nameBox.Text.Trim();
				if (text.Length > 12)
				{
					_status.Text = string.Format(Strings.PerfModeNameTooLong, 12);
					_status.ForeColor = UiVisualStyle.Danger;
					return;
				}
				perf.Rename(mode.Id, (text.Length == 0) ? null : text);
				EndRename();
				BindMode(mode.Id);
				bool flag = await perf.PushSlotNameAsync(mode.Id);
				if (!IsDisposed)
				{
					_status.Text = ((flag || perf.ActiveModeId != mode.Id) ? Strings.ProfileNameSaved : Strings.ProfileNameNotConfirmed);
					_status.ForeColor = ((flag || perf.ActiveModeId != mode.Id) ? UiVisualStyle.Ok : UiVisualStyle.Warn);
				}
				return;
			}
		}
		EndRename();
	}

	private async Task DeleteCustomModeAsync()
	{
		PerfModeService instance = PerfModeService.Instance;
		if (instance == null)
		{
			return;
		}
		PerfModeDefinition perfModeDefinition = FindMode(_modeId);
		if ((object)perfModeDefinition == null || !perfModeDefinition.IsCustom)
		{
			return;
		}
		if (!PerfModeCollection.CanRemove(ModesSnapshot(), perfModeDefinition.Id))
		{
			_status.Text = Strings.PerfModeKeepOneCustom;
			_status.ForeColor = UiVisualStyle.Warn;
			return;
		}
		string arg = PerfModeText.Name(perfModeDefinition);
		if (MessageBox.Show(this, string.Format(Strings.PerfModeDeletePrompt, arg), "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
		{
			// 2026-10-09（面板统一「显式应用」）：删档前把未应用的临时改动丢掉（不静默下发）。
			DiscardPendingChanges();
			if (instance.RemoveCustom(perfModeDefinition.Id))
			{
				RebuildModeItems();
				BindMode(instance.DisplayedModeId);
				_status.Text = string.Format(Strings.PerfModeDeleted, arg);
				_status.ForeColor = UiVisualStyle.Ok;
				await Task.CompletedTask;
			}
		}
	}
	// 40 系 WMI 23 关闭自定义 / 30 系如实说明 SMU 无恢复命令 / GPU 偏移 0/0 / NVML 功耗上限默认值）；
	private async Task RestoreDefaultsAsync()
	{
		PerfModeService instance = PerfModeService.Instance;
		if (instance == null)
		{
			return;
		}
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.IsConnected)
		{
			_status.Text = GcuDisconnectedText;
			_status.ForeColor = UiVisualStyle.Danger;
			return;
		}
		PerfModeDefinition mode = FindMode(_modeId);
		if ((object)mode == null)
		{
			return;
		}
		string name = PerfModeText.Name(mode);
		if (MessageBox.Show(this, string.Format(Strings.PerfModeRestorePrompt, name), "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			return;
		}
		// 2026-10-09（面板统一「显式应用」）：恢复出厂是最后的兜底 —— 它只丢掉未应用的临时改动，
		// 然后走既有的 PerfModeService.ResetAsync（原有的一键恢复路径），不在这里下发自定义字段。
		DiscardPendingChanges();
		_gpuOcArmed = false;
		_status.Text = Strings.Restoring;
		_status.ForeColor = UiVisualStyle.Muted;
		bool flag;
		Exception resetException = null;
		try
		{
			flag = await instance.ResetAsync(mode.Id);
		}
		catch (Exception ex)
		{
			// ⭐ 异常原文进日志（⛔ 不吞），状态行如实失败 ✅
			resetException = ex;
			flag = false;
			Logger.WriteError("[reset] 自定义档恢复出厂**异常**（如实上报，未吞掉）：" + ex);
		}
		// **不论自定义档配置复位是否成功，风扇都按机主要求复位到默认**（ 不因前面失败就跳过）
		await ResetFanAfterFactoryResetAsync(hw, instance);
		if (IsDisposed)
		{
			return;
		}
		if (resetException != null)
		{
			_status.Text = string.Format(Strings.PerfModeRestoreFailed, name);
			_status.ForeColor = UiVisualStyle.Danger;
			ResizeToContent();
			return;
		}
		try
		{
			BindMode(mode.Id);
			OnCustomChanged();
			// ⭐⭐ 关键：宝龙达的这些行按"硬件回读"刷新（清不掉/旧值）⇒ 必须**显式压回默认显示** ✅
			//   ⚠️ 只在**恢复确认成功**（`flag`）时压回：40 系功能号 23 关闭写若未被固件接受，
			//      界面仍显示硬件真实值（⛔ 不拿"看起来已重置"骗人），状态行红字如实说明 ✅
			//   ⚠️ 非宝龙达（GCU）⇒ 不调用 ⇒ 行为逐位不变 ✅
			if ((hw.UsesBaolongda || hw.WujieSmuApu) && flag)
			{
				ApplyBaolongdaResetVisuals(hw);
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteError("[reset] 恢复出厂后刷新界面失败（如实上报，未吞掉）：" + ex2);
		}
		_status.Text = string.Format(flag ? Strings.PerfModeRestoreDone : Strings.PerfModeRestoreFailed, name);
		// （30 系 · SMU）： SMU 功耗墙**没有"恢复默认"命令**、写入是运行时状态 ⇒
		if (flag && hw.UsesBaolongda && hw.IsBaolongda30Series)
		{
			_status.Text += Strings.PerfModeRestoreSmuVolatileNote;
		}
		_status.ForeColor = (flag ? UiVisualStyle.Ok : UiVisualStyle.Danger);
		ResizeToContent();
		// ⭐ 机主要求的一行日志（值 = 重置后的实际显示状态；默认日志级别可见）✅
		LogCustomResetResult(hw);
	}
	// （机主：「恢复出厂设置」不生效）： 恢复出厂后**显式重置界面控件** 。
	private void ApplyBaolongdaResetVisuals(MechrevoHw hw)
	{
		if (hw == null || !(hw.UsesBaolongda || hw.WujieSmuApu))
		{
			return;
		}
		bool syncing = _syncing;
		_syncing = true;
		try
		{
			// ① 功耗墙三项 ⇒ 回到「未设置」显示位（标题补回「（未设置）」）
			ResetPowerWallRowVisual(_pl1, _pl1Val, _pl1Label, Strings.CpuPl1);
			ResetPowerWallRowVisual(_pl2, _pl2Val, _pl2Label, Strings.CpuPl2);
			ResetPowerWallRowVisual(_tcc, _tccVal, _tccLabel, Strings.TempWallValue);
			if (_tccChk != null)
			{
				_tccChk.Checked = false;
			}
			if (_tcc != null)
			{
				_tcc.Enabled = false;
			}
			// ② GPU 超频 ⇒ 关；核心/显存偏移 ⇒ 0（重置时已用 NVAPI 写回 0/0）
			_gpuOcArmed = false;
			if (_ocChk != null)
			{
				_ocChk.Checked = false;
			}
			if (_coreOc != null)
			{
				ApplyRange(_coreOc, _coreOcVal, _coreOc.Minimum, _coreOc.Maximum, 0, allowNegative: true);
			}
			if (_memOc != null)
			{
				ApplyRange(_memOc, _memOcVal, _memOc.Minimum, _memOc.Maximum, 0, allowNegative: true);
			}
			_gpuOcDirty = false;
			SyncGpuOverclockDependents();
			// ③ 显卡功耗上限 ⇒ 按 NVML 回读的当前值显示（重置时已写回驱动默认值）
			if (_gpuPowerLimit != null && NvmlHelper.TryGetPowerLimitBounds(out int minW, out int maxW, out int curW) && maxW > minW)
			{
				ApplyRange(_gpuPowerLimit, _gpuPowerLimitVal, minW, maxW, curW);
			}
		}
		finally
		{
			_syncing = syncing;
		}
	}
	// （机主：「恢复出厂设置」不生效）： 把一行功耗墙控件压回「未设置」显示位 。
	private void ResetPowerWallRowVisual(RSlider bar, RNumericUpDown valueInput, Label label, string baseText)
	{
		if (bar == null)
		{
			return;
		}
		ApplyRange(bar, valueInput, bar.Minimum, bar.Maximum, bar.Minimum);
		// ⭐ 构造期标题按"当时是否未设置"确定；重置后要**补回**标记（只减不增的旧纪律在这里必须破例，
		//   否则重置后标题还停在"已下发"的短文案，会被误读成"仍然设置着"）—— 标题列宽按最长标题算过 ⇒ 不会裁 ✅
		if (label != null && !label.Text.EndsWith(UnsetMark, StringComparison.Ordinal))
		{
			label.Text = baseText + UnsetMark;
		}
		// ⭐ 30 系曾把提示换成"SMU 语义" ⇒ 重置后恢复成「未设置」的完整语义 ✅
		_tips.SetToolTip(bar, PowerWallUnsetHint);
		if (valueInput != null)
		{
			_tips.SetToolTip(valueInput, PowerWallUnsetHint);
		}
		if (label != null)
		{
			_tips.SetToolTip(label, PowerWallUnsetHint);
		}
	}
	// （机主：「恢复出厂设置」不生效）： 恢复出厂后落**机主要求的那一行**日志
	private void LogCustomResetResult(MechrevoHw hw)
	{
		string pl1 = ShowResetPower(hw?.Pl1Adjustable ?? false, hw?.BaolongdaPl1Unset ?? false, hw?.Pl1 ?? 0);
		string pl2 = ShowResetPower(hw?.Pl2Adjustable ?? false, hw?.BaolongdaPl2Unset ?? false, hw?.Pl2 ?? 0);
		string tcc = ((hw != null && hw.TccAdjustable && !hw.BaolongdaTempWallUnset && hw.TccTarget > 0)
			? hw.TccTarget.ToString(CultureInfo.InvariantCulture) + "℃"
			: "未设置");
		string gpuOc = ((_ocChk != null && _ocChk.Checked) ? "开" : "关");
		bool baolongda = hw != null && hw.UsesBaolongda;
		// ⭐ 机主要求核查的第三点：⭐ 配置是否真的写回 `PerfModeStore`（`perf_<档>_*` ≡ 原 `mode_tune_*`）
		//   并落盘 —— 这里**直读配置**复核（⛔ 不拿内存里的对象冒充）✅
		//   ⚠️ 只对宝龙达输出：GCU 机型恢复后 `CaptureSlotAsync` 会按固件回读重填档位参数（既有语义），
		//      那不是"残留"，⛔ 不该在这里误报 ✅
		string storeSuffix = string.Empty;
		if (baolongda)
		{
			string storeState;
			try
			{
				PerfModeSettings persisted = PerfModeStore.LoadSettings(_modeId);
				storeState = persisted.IsUntouched ? "已清空并落盘" : "⚠️ 仍有残留";
			}
			catch (Exception ex)
			{
				storeState = "⚠️ 回读失败：" + ex.GetType().Name;
			}
			storeSuffix = " · 本机配置=" + storeState;
		}
		Logger.WriteError($"[reset] 自定义档已恢复默认 ⇒ PL1={pl1} PL2={pl2} 温度墙={tcc} GPU 超频={gpuOc}"
			+ storeSuffix
			+ (baolongda ? "（本机后端：SMU / WMI 23；不写固件自定义档）" : ""));
		static string ShowResetPower(bool adjustable, bool unset, int value)
		{
			return (adjustable && !unset && value > 0) ? value.ToString(CultureInfo.InvariantCulture) : "未设置";
		}
	}
	// （机主：恢复出厂时**风扇转速/曲线表**没恢复，**强冷也要复位**）
	private async Task ResetFanAfterFactoryResetAsync(MechrevoHw hw, PerfModeService instance)
	{
		if (hw == null || instance == null || !hw.UsesBaolongda)
		{
			return;
		}
		try
		{
			instance.ResetBaolongdaFanToFirmwareDefault();
		}
		catch (Exception ex)
		{
			Logger.WriteError("[reset] 风扇恢复默认**异常**（如实上报，未吞掉）：" + ex);
		}
		await RefreshFanUiAfterFactoryResetAsync();
	}
	// <para> 刷新失败只记日志， 不影响"恢复出厂"本身 。</para>
	private async Task RefreshFanUiAfterFactoryResetAsync()
	{
		// ① 更多开关 / 磁贴（风扇强冷 = 关）
		try
		{
			SettingsForm settingsForm = Program.settingsForm;
			if (settingsForm != null && !settingsForm.IsDisposed)
			{
				if (settingsForm.InvokeRequired)
				{
					settingsForm.BeginInvoke(settingsForm.RefreshQuickSwitchStatesNow);
				}
				else
				{
					settingsForm.RefreshQuickSwitchStatesNow();
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[reset] 刷新「更多开关/磁贴」失败（已吞掉，不影响恢复）：" + ex.Message);
		}
		// ② 风扇曲线窗口（若开着）：按 store 真值重开
		try
		{
			FanCurveForm[] openCurveForms = OwnedForms.OfType<FanCurveForm>()
				.Where((FanCurveForm f) => f != null && !f.IsDisposed && f.Visible)
				.ToArray();
			if (openCurveForms.Length == 0)
			{
				return;
			}
			foreach (FanCurveForm fanCurveForm in openCurveForms)
			{
				try
				{
					fanCurveForm.Close();
				}
				catch (Exception ex2)
				{
					Logger.WriteLine("[reset] 关闭旧风扇曲线窗口失败（已吞掉）：" + ex2.Message);
				}
			}
			if (!IsDisposed)
			{
				await OpenFanCurveAsync();
				Logger.WriteError("[reset] 风扇曲线窗口已按默认状态重开（启用软件闭环=未勾选；闸门未放开时入口继续隐藏）");
			}
		}
		catch (Exception ex3)
		{
			Logger.WriteError("[reset] 重开风扇曲线窗口失败（已吞掉，不影响恢复）：" + ex3);
		}
	}
	// 打开风扇曲线窗口时**后台**触发一次风扇闸门只读验证 + 启动清理（进程内缓存一次； 不阻塞 UI）。
	private void ScheduleDeferredFanInit(string trigger)
	{
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.UsesBaolongda || Interlocked.Exchange(ref _deferredFanInitScheduled, 1) != 0)
		{
			return;
		}
		_ = Task.Run(() =>
		{
			long start = Environment.TickCount64;
			try
			{
				hw.RunDeferredFanStartupCleanup();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[init] 按需初始化：风扇闸门/清理失败（已吞掉，不影响 UI）：" + ex.Message);
			}
			Logger.WriteError("[init] 按需初始化：风扇闸门（触发=" + trigger + " · 耗时 " + (Environment.TickCount64 - start) + " ms）");
			try
			{
				SettingsForm form = Program.settingsForm;
				if (form != null && !form.IsDisposed && form.IsHandleCreated)
				{
					form.BeginInvoke((Action)(() =>
					{
						if (!form.IsDisposed)
						{
							form.RefreshQuickSwitchStatesNow();
						}
					}));
				}
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("[init] 按需初始化后刷新 UI 失败（已吞掉）：" + ex2.Message);
			}
		});
	}

	private async Task OpenFanCurveAsync()
	{
		PerfModeService perf = PerfModeService.Instance;
		// 风扇闸门只读验证/启动清理改为**纯按需 + 后台**：打开风扇曲线窗口时才触发（进程内缓存一次）；
		ScheduleDeferredFanInit("打开风扇曲线窗口");
		// （宝龙达 · 自定义档"不能用"）： 没有 GCU ⇒ EnsureFirmwareSlotAsync 必然失败
		if (perf != null && !_firmwareMode && !(Program.hw?.UsesBaolongda ?? false))
		{
			PerfModeDefinition mode = FindMode(_modeId);
			if ((object)mode != null && mode.Route == PerfModeRoute.BuiltIn)
			{
				MechrevoHw hw = Program.hw;
				if (hw == null || !hw.IsConnected)
				{
					_status.Text = GcuDisconnectedText;
					_status.ForeColor = UiVisualStyle.Danger;
					return;
				}
				_status.Text = string.Format(Strings.PerfModeFanCurveNeedsSlot, PerfModeText.Name(mode));
				_status.ForeColor = UiVisualStyle.Warn;
				PerfApplyOutcome perfApplyOutcome = await perf.EnsureFirmwareSlotAsync(mode.Id);
				if (IsDisposed)
				{
					return;
				}
				PerfModeDefinition? perfModeDefinition = perf.Find(mode.Id);
				if ((object)perfModeDefinition == null || perfModeDefinition.Route != PerfModeRoute.FirmwareSlot)
				{
					_status.Text = Strings.PerfModeNoSlotSeed;
					_status.ForeColor = UiVisualStyle.Danger;
					return;
				}
				BindMode(mode.Id);
				if ((object)perfApplyOutcome == null || !perfApplyOutcome.ModeSwitched)
				{
					ShowOutcome(perfApplyOutcome);
					return;
				}
			}
		}
		FanCurveForm fanCurveForm = new FanCurveForm();
		// 2026-10-10（用户反馈 ②「自定义模式风扇表不能保存」）：把**正在编辑的档位**显式交给曲线窗口。
		// 原来它只依赖 PerfModeService.Active（即"当时活动的档位就是正在编辑的档位"这个假设，见上面
		// 2297-2299 的注释）；一旦当时活动的档是别的档，曲线就只写 EC、一个配置键都不写 ⇒ 用户看到
		// "保存不了"。用户诊断包里 perf_active=turbo 而编辑的是 custom1，正是这种情形。
		fanCurveForm.TargetModeId = _modeId;
		// 2026-10-15（bugfix-fan-logo bug①，round2 用户改案「保存 = 保存 + 自动应用一次」）：
		// 曲线窗口每轮写完（回读确认/失败）都会回调 OnFanCurveSavedFromCurveWindow：
		// 成功 ⇒ owner 用**最新落盘值**为基线 ActivateAsync(forceParameters:true) 强制重发一次（自动应用），
		///  保存即生效、且即便随后有面板旧快照路径也不会覆盖（基线已取最新值）；失败 ⇒ 状态行可见红字，⛔ 不静默。
		// 面板上其它（非曲线）改动照旧走【应用】；【应用】变成"修复型重应用"按钮（见 SyncApplyButtons）。
		// 订阅在 AddOwnedForm 之前；窗体由 owner 管生命周期，无泄漏。
		fanCurveForm.FanCurveSaved += OnFanCurveSavedFromCurveWindow;
		AddOwnedForm(fanCurveForm);
		ResponsiveLayout.ShowAdjacentTo(fanCurveForm, this);
	}

	/// <summary>
	/// 2026-10-09（面板统一「显式应用」）：组合框那类「应用侧」改动**只登记 + 置脏**，不即时下发。
	/// 原来这里直接调 CommitAsync → PerfModeService.UpdateAsync，改一下下拉就立刻保存并下发一次；
	/// 现在整面板只有点【应用】（ApplyPendingAsync）才下发，登记顺序＝用户操作顺序。
	/// </summary>
	private void QueueEdit(Func<PerfModeSettings, PerfModeSettings> edit)
	{
		_pendingEdits.Add(edit);
		MarkPanelDirty();
	}

	/// <summary>2026-10-09：把登记在案的「应用侧」改动按顺序叠到设置上（纯函数，无副作用）。</summary>
	internal static PerfModeSettings ApplyEdits(PerfModeSettings settings, IReadOnlyList<Func<PerfModeSettings, PerfModeSettings>> edits)
	{
		PerfModeSettings perfModeSettings = settings;
		foreach (Func<PerfModeSettings, PerfModeSettings> edit in edits)
		{
			perfModeSettings = edit(perfModeSettings);
		}
		return perfModeSettings;
	}

	/// <summary>
	/// 2026-10-09（面板统一「显式应用」）：**整面板唯一的下发入口** —— 点【应用】才把这一批
	/// 「未应用的改动」保存到档位并一次性下发，走的全是既有路径：
	/// 自定义档 = PerfModeService.UpdateAsync（→ PerfModeApplyPlanner → PerfModeRunner → GCU）；
	/// 固件档位 = 参数字段仍走 MechrevoService.SetCustomDetail（沿用「先确认 (模式,档位) 再写」的既有口径）。
	/// 不新增任何命令。
	///
	/// 失败保底（2026-10-09）：① 某一项失败时其余项照旧下发完（既有 Runner 逐项执行、不中断）；
	/// ② 失败的那一批**原样留在待应用集合里** ⇒ 状态行列出失败项（ShowOutcome →
	/// Strings.PerfModeOutcomeFailedItems），且【应用】保持可用、可以再点一次；
	/// ③ **绝不静默回滚**：失败后不做任何"把界面改回回读值"的动作，只如实显示。
	/// </summary>
	private async Task ApplyPendingAsync()
	{
		// 2026-10-09（非自定义档的「应用侧」行可应用）：<c>_optionsLocked</c>（Gen40+ 预设档的硬件参数锁）
		// **不再**把整颗【应用】挡在门外 —— 电源模式 / 电源计划 / 睿频 / 风扇增强 / 刷新率这些应用侧行
		// 在该状态下仍可改，也必须能保存+下发（它们不在 OptionControls() 名单里，见那里的说明）。
		// 硬件参数行在锁定时是禁用的 ⇒ Queue() 根本点不出来，故这里不可能有硬件字段；万一有（历史脏值），
		// 就照旧不下发、停在界面上等用户处理，与改动前"锁定档位一律不发"同一口径。
		if (IsDisposed || !_panelDirty || (_optionsLocked && (_pending.Count > 0 || _gpuOcDirty)))
		{
			return;
		}
		if (_firmwareMode && !await SelectFirmwareTargetAsync())
		{
			// 目标 (模式, 档位) 没能确认：这一批不发（沿用既有口径），待应用状态保留、等用户重试。
			_status.Text = Strings.ParamsReverted;
			_status.ForeColor = UiVisualStyle.Danger;
			SyncApplyButtons();
			return;
		}
		await _saveLock.WaitAsync();
		PerfModeService perf = PerfModeService.Instance;
		try
		{
			Dictionary<string, string> fields = new Dictionary<string, string>(_pending);
			List<Func<PerfModeSettings, PerfModeSettings>> edits = new List<Func<PerfModeSettings, PerfModeSettings>>(_pendingEdits);
			// 2026-10-15（bugfix-fan-logo bug①，fan-curve-baseline）：下发前把两条风扇曲线基线刷成
			// **最新落盘值**（BuildFanCurveBaselineEdits ⇒ PerfModeStore.LoadSettings 直读 config）。
			// 背景：曲线窗口保存走 FlushPendingAsync（写 EC + 回读确认 + RecordFanCurves），从不过面板
			// 的 _pending；而 ActivateAsync(forceParameters=true) 的全量重写按"该档已保存设置"发曲线，
			// 若不刷新就会用 PerfModeService 缓存的旧快照覆盖刚保存的曲线（诊断包实证 fancpu=默认基线）。
			// round2：这段抽成 BuildFanCurveBaselineEdits，保存后的**自动应用**（ApplyFanCurveAutoApplyAsync）
			// 复用同一份口径。⚠️ 只重写 CpuFanDuty/GpuFanDuty 两个字段，其余语义零改动。
			if (perf != null)
			{
				Func<PerfModeSettings, PerfModeSettings> baselineEdit = BuildFanCurveBaselineEdits(perf, _modeId);
				if ((object)baselineEdit != null)
				{
					edits.Add(baselineEdit);
				}
			}
			bool offsetsPending = _gpuOcDirty;
			if (offsetsPending)
			{
				// GPU 核心/显存偏移的值就活在滑块上：只在「应用」这一瞬间读一次。
				fields["GpuCoreClockOffsetOC"] = _coreOcVal.Value.ToString();
				fields["GpuMemoryClockOffsetOC"] = _memOcVal.Value.ToString();
			}
			_pending.Clear();
			_pendingEdits.Clear();
			_pendingPowerModeEdit = null;
			if (fields.TryGetValue("OverClockingSwitch", out string? ocValue) && ocValue == "1" && !fields.ContainsKey("GpuCoreClockOffsetOC") && !fields.ContainsKey("GpuMemoryClockOffsetOC"))
			{
				// 2026-10-09：沿用既有口径 —— 只把超频开关打开、两个偏移都还是 0（或不可调）时，
				// 这一组不发（硬件上没有任何可写值），提示「已开启，设置频率偏移后生效」；
				// 与旧代码的区别只有一点：**其余待应用项照发**（旧代码是 continue 把整批一起丢掉）。
				if (_gpuOcCoreAdjustable)
				{
					fields["GpuCoreClockOffsetOC"] = _coreOcVal.Value.ToString();
				}
				if (_gpuOcMemoryAdjustable)
				{
					fields["GpuMemoryClockOffsetOC"] = _memOcVal.Value.ToString();
				}
				if ((!_gpuOcCoreAdjustable || _coreOcVal.Value == 0) && (!_gpuOcMemoryAdjustable || _memOcVal.Value == 0))
				{
					fields.Remove("OverClockingSwitch");
					fields.Remove("GpuCoreClockOffsetOC");
					fields.Remove("GpuMemoryClockOffsetOC");
					_status.Text = Strings.PerfModeOcArmed;
					_status.ForeColor = UiVisualStyle.Muted;
				}
			}
			// 把 **UI 控件值 → 待下发字段** 的真实映射落一行（默认级别可见）
			if ((Program.hw?.UsesBaolongda ?? false)
				&& (fields.ContainsKey("OverClockingSwitch") || fields.ContainsKey("GpuCoreClockOffsetOC") || fields.ContainsKey("GpuMemoryClockOffsetOC")))
			{
				string ocOnText = fields.TryGetValue("OverClockingSwitch", out string ocOnValue) ? ocOnValue : "(未改)";
				string coreFieldText = fields.TryGetValue("GpuCoreClockOffsetOC", out string coreFieldValue) ? coreFieldValue : "(未改)";
				string memFieldText = fields.TryGetValue("GpuMemoryClockOffsetOC", out string memFieldValue) ? memFieldValue : "(未改)";
				Logger.WriteError("[gpu-oc] 应用请求（UI→字段）：开关=" + ocOnText + " · core=" + coreFieldText + " · mem=" + memFieldText
					+ " · 数字框 core=" + _coreOcVal.Value + " mem=" + _memOcVal.Value + "（滑块 core=" + _coreOc.Value + " mem=" + _memOc.Value + "）"
					+ " · 偏移可调 core=" + _gpuOcCoreAdjustable + " mem=" + _gpuOcMemoryAdjustable
					+ " · 偏移脏=" + offsetsPending);
			}
			PerfApplyOutcome? outcome = null;
			bool confirmed = true;
			bool applied = false;
			bool statusFromFields = false;
			try
			{
				if (_firmwareMode)
				{
					if (perf != null && edits.Count > 0)
					{
						_status.Text = Strings.ModeTuneApplying;
						_status.ForeColor = UiVisualStyle.Muted;
						PerfModeSettings storedBase = PerfModeStore.LoadSettings(FirmwareProfileStoreId(_firmwareVisualMode, _firmwareProfileIndex));
						if (!storedBase.IsUntouched)
						{
							PerfModeStore.SaveSettings(_modeId, storedBase);
						}
						outcome = await perf.UpdateAsync(_modeId, (PerfModeSettings s) => ApplyEdits(s, edits), "editor");
						PerfModeStore.SaveSettings(FirmwareProfileStoreId(_firmwareVisualMode, _firmwareProfileIndex), PerfModeStore.LoadSettings(_modeId));
						applied = true;
					}
					MechrevoService? service = Program.service;
					if (service != null && fields.Count > 0)
					{
						_status.Text = Strings.ModeTuneApplying;
						_status.ForeColor = UiVisualStyle.Muted;
						bool flag = await service.SetCustomDetail(fields);
						if (IsDisposed)
						{
							return;
						}
						_status.Text = (flag ? Strings.ParamsConfirmed : Strings.ParamsReverted);
						_status.ForeColor = (flag ? UiVisualStyle.Ok : UiVisualStyle.Danger);
						confirmed = flag;
						applied = true;
						statusFromFields = true;
					}
				}
				else if (perf != null && (edits.Count > 0 || fields.Count > 0))
				{
					_status.Text = Strings.ModeTuneApplying;
					_status.ForeColor = UiVisualStyle.Muted;
					outcome = await perf.UpdateAsync(_modeId, (PerfModeSettings s) => ApplyFields(ApplyEdits(s, edits), fields), "editor");
					applied = true;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Performance mode apply failed: " + ex.Message);
				_status.Text = Strings.ParamsSaveFailed;
				_status.ForeColor = UiVisualStyle.Danger;
				confirmed = false;
			}
			if (IsDisposed)
			{
				return;
			}
			if (!applied && (fields.Count > 0 || edits.Count > 0))
			{
				// 没有任何可用下发路径（例如 PerfModeService 还没建起来）：这一批**不丢**，保留待应用、可重试。
				confirmed = false;
				_status.Text = Strings.ParamsSaveFailed;
				_status.ForeColor = UiVisualStyle.Danger;
			}
			if (outcome != null)
			{
				// 既有结果呈现：失败时就是「没生效：<失败项列表>」，且 tooltip 里逐项列出（ShowOutcome）。
				confirmed = !outcome.AnyFailed;
				ShowOutcome(outcome);
			}
			else if (applied && !statusFromFields)
			{
				// UpdateAsync 返回 null ⇒ 这一档不是当前档（已保存、切到该档时生效）或与现值一致。
				if (perf != null && !string.Equals(perf.ActiveModeId, _modeId, StringComparison.Ordinal))
				{
					_status.Text = Strings.PerfModeSavedInactive;
					_status.ForeColor = UiVisualStyle.Muted;
				}
				else
				{
					_status.Text = Strings.ParamsConfirmed;
					_status.ForeColor = UiVisualStyle.Ok;
				}
			}
			if (confirmed)
			{
				_gpuOcDirty = false;
			}
			else
			{
				// 失败保底：把没成功的这一批原样放回待应用集合（值就是控件当前值），【应用】可再点一次。
				foreach (KeyValuePair<string, string> item in fields)
				{
					_pending[item.Key] = item.Value;
				}
				_pendingEdits.InsertRange(0, edits);
				_gpuOcDirty |= offsetsPending;
			}
			_panelDirty = _pending.Count > 0 || _pendingEdits.Count > 0 || _gpuOcDirty;
			SyncApplyButtons();
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Performance mode apply failed: " + ex2.Message);
			_status.Text = Strings.ParamsSaveFailed;
			_status.ForeColor = UiVisualStyle.Danger;
		}
		finally
		{
			_saveLock.Release();
		}
	}

	private void ShowOutcome(PerfApplyOutcome? outcome)
	{
		if ((object)outcome == null || IsDisposed)
		{
			return;
		}
		bool amd = Program.hw?.UsesAmdPowerFields ?? false;
		bool flag = outcome.Steps.Any((PerfApplyStepOutcome s) =>
		{
			PerfApplyStepKind kind = s.Step.Kind;
			return (kind == PerfApplyStepKind.SwitchBuiltIn || kind == PerfApplyStepKind.SwitchFirmwareSlot) ? true : false;
		});
		PerfModeDefinition perfModeDefinition = FindMode(outcome.ModeId);
		string arg = (((object)perfModeDefinition != null) ? PerfModeText.Name(perfModeDefinition) : "");
		// 不掩盖真实失败： 有失败项 ⇒  仍走下面 AnyFailed 分支（ 红字 ）。
		bool firmwareSlotNotWritable = ((object)perfModeDefinition != null)
			&& perfModeDefinition.Route == PerfModeRoute.FirmwareSlot
			&& ((Program.hw?.UsesBaolongda ?? false) || (Program.hw?.WujieSmuApu ?? false));
		if (firmwareSlotNotWritable && !outcome.AnyFailed)
		{
			// （机主截图：灰字「设置保存到本机并立即应用…」重复两行）
			bool wujieLocalUi = Program.hw?.WujieSmuApu ?? false;
			_status.Text = wujieLocalUi ? Strings.ParamsConfirmed : LocalApplyText;
			_status.ForeColor = wujieLocalUi ? UiVisualStyle.Ok : UiVisualStyle.Warn;
			_tips.SetToolTip(_status, PerfModeText.Details(outcome, amd));
			ResizeToContent();
			// ⭐ 默认「仅错误」级别可见（⭐ WriteError ✅）；⭐ 用状态翻转去重 ⇒ ⭐ 不会随每次
			//   懒验证/重绘刷屏（⭐ 只有"从别的情形变成这种"时才落一行 ✅）。
			if (Logger.TryMarkChanged("perf-custom-firmware-slot-unsupported", "PerfModes: " + outcome.ModeId + " 本机后端（宝龙达 / 无界 APU）⇒ 本机配置已应用（SMU / WMI 23 / NVAPI / NVML），固件自定义档不写；已按后端如实提示"))
			{
				Logger.WriteError("PerfModes: " + outcome.ModeId + " 本机后端（宝龙达 / 无界 APU）⇒ 本机配置已应用（SMU / WMI 23 / NVAPI / NVML），固件自定义档不写；已按后端如实提示");
			}
			return;
		}
		if (flag && !outcome.ModeSwitched)
		{
			_status.Text = Strings.PerfModeSwitchFailed;
			_status.ForeColor = UiVisualStyle.Danger;
		}
		else if (outcome.AnyFailed)
		{
			_status.Text = string.Format(Strings.PerfModeOutcomeFailedItems, PerfModeText.FailedItems(outcome, amd));
			_status.ForeColor = UiVisualStyle.Danger;
		}
		else
		{
			if (outcome.Steps.Count == 0)
			{
				return;
			}
			string text = PerfModeText.Summary(outcome);
			// (b)（2026-10-09，用户原话「OK 按你说的」）：懒验证把"已下发·待确认"升级成"已确认"后会带着新结果回到这里，
			// 汇总里的「待确认 N 项」自动减一、tooltip 逐项文案也一起刷新（红字消失）；
			// 只有**期限（30s）到了仍读到对不上**的那类步骤才在末尾追加一句「可能没生效」（Warn 色，不是失败红）。
			string text2 = PerfModeText.StaleNote(outcome);
			_status.Text = (flag ? (string.Format(Strings.PerfModeSwitched, arg) + " · " + text) : text) + text2;
			_status.ForeColor = ((text2.Length == 0) ? UiVisualStyle.Ok : UiVisualStyle.Warn);
		}
		_tips.SetToolTip(_status, PerfModeText.Details(outcome, amd));
		ResizeToContent();
	}

	private void OnPerfModesChanged()
	{
		// 2026-10-04: firmware-profile mode owns the UI. These perf-mode event callbacks
		// fire after BindFirmwareProfile and would otherwise reload the custom mode.
		if (_firmwareMode)
		{
			return;
		}
		if (IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(OnPerfModesChanged);
			return;
		}
		RebuildModeItems();
		PerfModeDefinition perfModeDefinition = FindMode(_modeId);
		if ((object)perfModeDefinition == null)
		{
			return;
		}
		bool syncingModes = _syncingModes;
		_syncingModes = true;
		try
		{
			IReadOnlyList<PerfModeDefinition> existing = ModesSnapshot();
			_addButton.Enabled = PerfModeCollection.CanAdd(existing);
			_deleteButton.Enabled = perfModeDefinition.IsCustom && PerfModeCollection.CanRemove(existing, perfModeDefinition.Id);
			UpdateRouteHint(perfModeDefinition);
		}
		finally
		{
			_syncingModes = syncingModes;
		}
	}

	private async void OnPerfActiveChanged()
	{
		if (IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(OnPerfActiveChanged);
			return;
		}
		PerfModeService instance = PerfModeService.Instance;
		if (instance == null || !Visible)
		{
			return;
		}
		string displayedModeId = instance.DisplayedModeId;
		// 2026-10-05: in firmware-profile mode do not let a non-empty _pending batch swallow
		// the mode-change refresh -- that is what made quick switching feel unresponsive.
		bool localMachine = (Program.hw?.WujieSmuApu ?? false) || (Program.hw?.UsesBaolongda ?? false);
		if (!string.Equals(displayedModeId, _modeId, StringComparison.Ordinal) && (_pending.Count == 0 || _firmwareMode || localMachine))
		{
			BindMode(displayedModeId);
			await RefreshHardwareStateAsync();
			return;
		}
		PerfModeDefinition perfModeDefinition = FindMode(_modeId);
		if ((object)perfModeDefinition != null)
		{
			UpdateRouteHint(perfModeDefinition);
		}
		if (_pending.Count == 0)
		{
			await RefreshHardwareStateAsync();
		}
	}

	private void OnPerfApplied(PerfApplyOutcome outcome)
	{
		if (IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(() =>
			{
				OnPerfApplied(outcome);
			});
		}
		else if (Visible && string.Equals(outcome.ModeId, _modeId, StringComparison.Ordinal))
		{
			// (b)（2026-10-09）：懒验证的"升级"会在下发后几秒重新抛一次 Applied。若用户这时已经开始改新的值
			//（面板又有未应用改动 _panelDirty），不要用这条**旧结果**把"待应用"提示覆盖掉。
			if (!outcome.LazyUpgrade || !_panelDirty)
			{
				ShowOutcome(outcome);
			}
		}
	}

	private void ResizeToContent()
	{
		if (IsDisposed)
		{
			return;
		}
		ResponsiveLayout.PerformLayoutTree(this);
		bool flag = false;
		if (ClientSize.Width < _minContentWidth)
		{
			ClientSize = new Size(_minContentWidth, ClientSize.Height);
			flag = true;
		}
		// 2026-10-10 第三轮（竖向，用户原话：「窗口拉高底下空一大块」）：
		// ① 窗口高度只"长到够放内容"，**不再**把用户手动拉高的高度压回内容高
		//    （原来这里按 ResponsiveLayout.VisibleContentBottom 双向对齐，拉高之后任何一次刷新都会把它缩回去）；
		// ② 再把内容表的高度补到"自然高度 / 视口高"里的较大者 —— 参数卡片那一行是 Percent=100
		//    ⇒ 多出来的整片高度都进卡片（列表区域填满"标题区 与 按钮行 之间"），
		//    而按钮行是最后一行 AutoSize ⇒ 它永远贴着内容表底边（= 窗口底边，⛔ 按钮下方不再留死区）。
		// ⚠️ 自然高度必须另算：卡片会随窗口拉高而拉高，拿"看得见的内容底边"当自然高会一路棘轮上去（窗口再也缩不回）。
		int natural = NaturalContentHeight();
		int chrome = Math.Max(0, Height - ClientSize.Height);
		int minClientHeight = Math.Max(natural, MinimumSize.Height - chrome);
		if (ClientSize.Height < minClientHeight)
		{
			ClientSize = new Size(ClientSize.Width, minClientHeight);
			flag = true;
		}
		if (natural != _laidOutContentHeight || flag)
		{
			_laidOutContentHeight = natural;
			ResponsiveLayout.PerformLayoutTree(this);
		}
		int viewport = ((_scrollHost != null && _scrollHost.ClientSize.Height > 0) ? _scrollHost.ClientSize.Height : 0);
		int wantedContentHeight = Math.Max(natural, viewport);
		if (_contentTable != null && _contentTable.Height != wantedContentHeight)
		{
			_contentTable.Height = wantedContentHeight;
			ResponsiveLayout.PerformLayoutTree(this);
		}
		// 2026-10-10（用户原话：「文本框右边很空 可以把文本框右移 然后滑块可以拉长一点」）：
		// 窗口的**真实**可用宽度到这里才算定下来（上面可能刚把 ClientSize 抬到 _minContentWidth），
		// 所以滑块行那一簇的宽度在这一步按实测行宽重排（见 RewidthSliderRows 的注释）。
		RewidthSliderRows();
	}

	/// <summary>
	/// 2026-10-10 第三轮⭐（②）：量出"数字框控件**内部**被隐藏的原生步进按钮占掉的那条死条"的宽（设备像素）。
	/// <list type="bullet">
	///   <item>量法（只读几何，⛔ 不改任何 Bounds）：拿第一只**已排好版**的数字框，取
	///     <c>控件.Width − 内嵌编辑框(UpDownEdit).Width</c>（本机实测 96 DPI = 16、150% = 24、175% = 28，
	///     ≡ 16 逻辑像素 × scale）；量不到就退回 <c>Controls[0]</c>（被隐藏的原生步进按钮）自己的宽；
	///     再量不到才退回 <see cref="D" />(<see cref="NativeSpinReserveLogicalWidth" />) 的估算值 ——
	///     ⛔ 绝不写死设备像素常量。</item>
	///   <item>⚠️ 返回值在"控件宽 = 第 0 列宽 + 死条"之后仍然成立（编辑框宽 = 控件宽 − 死条 是框架自己排的），
	///     所以每次重排量到的都是同一个数 ⇒ 不会来回抖（幂等判断里已带上它）。</item>
	/// </list>
	/// </summary>
	private int MeasureSpinReserve()
	{
		foreach (Control input in _sliderValueInputs)
		{
			if (input.Width > 0)
			{
				foreach (Control child in input.Controls)
				{
					if (child is TextBox edit && edit.Width > 0 && input.Width > edit.Width)
					{
						return input.Width - edit.Width;
					}
				}
			}
			if (input.Controls.Count > 0 && input.Controls[0].Width > 0)
			{
				return input.Controls[0].Width;
			}
		}
		return D(NativeSpinReserveLogicalWidth);
	}

	/// <summary>
	/// 2026-10-10 第三轮（竖向）：内容**自然高度**（设备像素）= 标题区 + 参数卡片按内容要高 + 卡片下边距
	/// + 按钮行（高 + 上下 Margin）+ 内容表下内边距。
	/// <list type="bullet">
	///   <item>量法：<c>_paramTable.Top</c>（卡片在内容表里的位置 = 上内边距 + 标题区各行，已由布局排好、只读）
	///     + 卡片自己的 <c>GetPreferredSize</c>（TableLayoutPanel 按行列算，与它当前**被拉多高无关**）
	///     + 卡片下 Margin + 按钮行（<c>_buttonRow.Height + Margin.Vertical</c>）+ 内容表下 Padding。</item>
	///   <item>⚠️ 按钮行那一项不能漏：漏了算出来的自然高度会比内容矮 ~D(55)，卡片就会被压到比它自己的内容还矮
	///     （%TEMP% 实测：卡片 801 &lt; 卡片 preferred 856 ⇒ 末几行被裁）——本机 175% DPI 下正确值
	///     140+856+10+55+14 = 1075，与改动前 <c>ResponsiveLayout.VisibleContentBottom</c> 的实测值逐像素相同。</item>
	///   <item>⚠️ 不能用 <c>ResponsiveLayout.VisibleContentBottom(_scrollHost)</c>：卡片现在会随窗口拉高而拉高，
	///     那个量法会把"被拉高的卡片"当成自然高度 ⇒ 窗口再也缩不回去（棘轮）。</item>
	///   <item>⚠️ 只读几何：不改任何 Bounds / 列宽 / 可见性。</item>
	/// </list>
	/// </summary>
	private int NaturalContentHeight()
	{
		if (_contentTable == null || _paramTable == null || _buttonRow == null)
		{
			return _laidOutContentHeight;
		}
		int cardPreferred = _paramTable.GetPreferredSize(new Size(Math.Max(1, _paramTable.ClientSize.Width), 0)).Height;
		int buttonRowOuter = _buttonRow.Height + _buttonRow.Margin.Vertical;
		return _paramTable.Top + cardPreferred + _paramTable.Margin.Bottom + buttonRowOuter + _contentTable.Padding.Bottom;
	}

	/// <summary>
	/// 2026-10-10 第二轮（用户原话：「修反了 文本框拉长了 滑块反而短了」）：
	/// 把 AddSliderRow 那 9 行按该行**真实可用宽度**重排，规则是"**余量全给滑块**"：
	/// <list type="number">
	///   <item>量出这一行的真实可用宽度 W：<c>_scrollHost.ClientSize.Width</c>（垂直滚动条占掉的那部分
	///     已经被 WinForms 从 ClientSize 里扣掉）；量不到（窗口还没显形）就退回
	///     <see cref="_minContentWidth" /> —— 那时算出来正好等于整窗最小宽度下的分配，⛔ 不猜、也不会抖。</item>
	///   <item>第 2 列（「数字框 + − + +」这一簇）= **固定窄宽**
	///     <see cref="ValueInputMinClusterLogicalWidth" />（= 数字框 52 + 符按钮 30 + 27 逻辑像素），
	///     **不再**把余量分给它 ⇒ 窗口变宽时数字框一个像素都不长。</item>
	///   <item>第 1 列是 Percent=100 ⇒ 它自动吃掉
	///     <c>W − 标题列 − 这一簇</c> 的全部宽度（⚠️ 2026-10-10 第四轮起再扣掉滑块自己的右 Margin
	///     <see cref="SliderValueInputGapLogicalWidth" />，见那条常量的说明）。⭐ 于是"窗口越宽滑块越长"，
	///     而 − / + 两列（第 2 列内部的两列，D(24) 防裁下限）始终钉在这一簇的右端、跟着行右缘走。</item>
	///   <item>⭐ 数字框**贴住 −**（2026-10-10 第三轮，用户拍板 ①+② 里的 ②）：⭐ 关键事实（%TEMP% 探针
	///     **直接实例化本窗体**、175% DPI 实测）：数字框控件里有**两条**宽度 —— 控件自己的 Width（92 设备像素）与
	///     <b>看得见的那只编辑框</b>（<c>UpDownEdit</c>，64 设备像素）；两者之差 = 28 =
	///     <see cref="NativeSpinReserveLogicalWidth" /> 逻辑像素（被隐藏的原生步进按钮所占的死条）。
	///     ⇐ 上一轮把"控件右缘"贴到 − 的左边（实测 <c>−.Left − 控件.Right = D(6)</c>）**看着仍空一大块**，
	///     因为眼睛看到的是里面那只编辑框：<c>−.Left − 编辑框.Right</c> = 死条 + D(6) = 38 设备像素。
	///     ⭐ 本轮：簇内第 0 列 = <c>D(52) − 死条</c>（= **看得见的框宽**），数字框控件 = 这一列 **+ 死条**
	///     （<c>MinimumSize</c> 顶开 Dock=Fill 的单元格；%TEMP% 实测有效、不抖、簇改宽后仍成立）
	///     ⇒ 编辑框右缘正好落在第 0 列右缘，而 − 的左 Margin D(6) 就从那里起算 ⇒
	///     <c>−.Left − 编辑框.Right = D(6)</c>（= 既有的行内间距）。
	///     ⚠️ 死条本身一个像素都没动（不改内嵌编辑框 Bounds / 不设 Region / 不改 Dock / 不改 −/+ 列宽），
	///     它只是被挪到 − 按钮底下（<see cref="AddSliderRow" /> 已把两颗符按钮 BringToFront + 实测
	///     NumericUpDown 带 WS_CLIPSIBLINGS ⇒ 下层盖不掉上层）。
	///     ⚠️ 与 2026-10-09「不改编辑框宽度、数字原地不动」的口径：编辑框宽度与数字在框内的位置**都不变**，
	///     变的是"整簇右移一个死条"（用户原话「可以把文本框右移」）⇒ 不冲突。</item>
	///   <item>⚠️ 兜底：只有"某行 W 窄到连 [标题列 + 滑块下限 + 簇宽] 都放不下"时才会动第 0 列
	///     （把标题列压到 <c>min(实测标题列宽, W − 滑块下限 − 簇宽)</c>），保住"滑块不低于
	///     <see cref="SliderMinLogicalWidth" />、数字框不被压窄、− / + 不截断"；正常窗口下这一步不生效。</item>
	/// </list>
	/// ⚠️ 单位：全部按**逻辑像素**算，最后统一经 <see cref="D" /> 换算 —— 与构造末尾给行设列宽同一口径
	/// （100/125/150/175% 一致），⛔ 不硬编码设备像素。
	/// ⚠️ 幂等：算出来的宽度与当前值相同就返回；改列宽会再触发布局，用 <see cref="_rewidthing" /> 挡重入。
	/// ⚠️ 每次调用都**重新算**（不在首次调用时"定死"，否则窗口还没显形/DPI 变化时会把下限写成定值）。
	/// ⚠️ 只动这 9 行的第 0/2 列；其它行不在 <see cref="_sliderRows" /> 里，一个像素不碰。
	/// </summary>
	private void RewidthSliderRows()
	{
		if (_sliderRows.Count == 0 || _minContentWidth <= 0 || _sliderRowLabelColumnDeviceWidth <= 0 || _rewidthing)
		{
			return;
		}
		// 滚动宿主的 ClientSize 已经把垂直滚动条扣掉了；拿不到就退回整窗最小宽度。
		int hostWidth = ((_scrollHost != null && _scrollHost.ClientSize.Width > 0) ? _scrollHost.ClientSize.Width : _minContentWidth);
		int rowWidth = Math.Max(hostWidth, _minContentWidth);
		// 2026-10-10 第三轮⭐（②）：先量那条"隐藏在数字框控件内部"的死条（设备像素；量不到就按
		// NativeSpinReserveLogicalWidth 逻辑像素估，最坏也只是回到上一轮的几何，⛔ 不会把框算窄）。
		int reserve = MeasureSpinReserve();
		// ⭐ 簇 = 「看得见的框」+ − 列 + + 列（死条不算在簇里 ⇒ 它让出来的那份宽度全归滑块）。
		int visibleBoxWidth = Math.Max(D(24), D(MinValueInputLogicalWidth) - reserve);
		int cluster = visibleBoxWidth + _sliderDecColumnDeviceWidth + _sliderIncColumnDeviceWidth;
		// 兜底：把标题列夹到 [0, 实测标题列宽]，保证 滑块 ≥ 滑块下限 且 簇不被压窄。
		int labelCap = Math.Max(0, rowWidth - D(SliderMinLogicalWidth) - cluster);
		int label = Math.Min(_sliderRowLabelColumnDeviceWidth, labelCap);
		if (label == _lastSliderRowLabelDeviceWidth && cluster == _lastSliderClusterWidth && reserve == _lastAppliedSpinReserve)
		{
			return;
		}
		_lastSliderRowLabelDeviceWidth = label;
		_lastSliderClusterWidth = cluster;
		_lastAppliedSpinReserve = reserve;
		_rewidthing = true;
		try
		{
			// 2026-10-10 第三轮⭐（②）：数字框控件的宽 = **看得见的那只编辑框**的宽 + 死条 ——
			// 控件因此比它所在的第 0 列宽一个死条（越出单元格、盖住旁边那条死条所在的位置），
			// 编辑框右缘正好落在第 0 列右缘 ⇒ −（它的左 Margin 从列右缘起算）与看得见的框之间
			// 只剩 D(6) 行内间距（用户原话：「文本框右边还是空的」⇒ 本轮真正贴上）。
			int valueInputWidth = visibleBoxWidth + reserve;
			foreach (TableLayoutPanel row in _sliderRows)
			{
				// 2026-10-10 第二轮：第 2 列保持定宽（数字框不变宽），第 0 列只在"窗口窄到放不下"时才收窄；
				// 第 1 列是 Percent=100 ⇒ 剩下的宽度它全吃 —— 滑块长度 = 行宽 − 标题列 − 这一簇。
				row.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, cluster);
				if (!Math.Equals(row.ColumnStyles[0].Width, (float)label))
				{
					row.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, label);
				}
			}
			foreach (SliderChipRow chipRow in _sliderRowsWithChips)
			{
				// − / + 两列：既保住 D(24) 防裁下限，也保证高 DPI 下取整不会把按钮切掉。
				int decWidth = Math.Max(D(24), _sliderDecColumnDeviceWidth);
				int incWidth = Math.Max(D(24), _sliderIncColumnDeviceWidth);
				if (!Math.Equals(chipRow.Cluster.ColumnStyles[1].Width, (float)decWidth))
				{
					chipRow.Cluster.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, decWidth);
				}
				if (!Math.Equals(chipRow.Cluster.ColumnStyles[2].Width, (float)incWidth))
				{
					chipRow.Cluster.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, incWidth);
				}
			}
			foreach (Control valueInput in _sliderValueInputs)
			{
				// 2026-10-10 第三轮⭐（②）：控件的宽 = 第 0 列宽 + 死条。它是 Dock=Fill，所以真正起作用的是
				// **MinimumSize**（WinForms 的 SetBoundsCore 会按 MinimumSize 夹住布局给的宽，%TEMP% 实测：
				// 64 宽单元格 + MinimumSize=92 ⇒ 控件 92、内嵌编辑框 64、不抖）——⛔ 不改 Dock/Anchor/Bounds 语义。
				if (valueInput.MinimumSize.Width != valueInputWidth)
				{
					valueInput.MinimumSize = new Size(valueInputWidth, 0);
				}
				if (valueInput.Width != valueInputWidth)
				{
					valueInput.Width = valueInputWidth;
				}
			}
			ResponsiveLayout.PerformLayoutTree(this);
		}
		finally
		{
			_rewidthing = false;
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		ResizeToContent();
	}

	private void UpdatePowerWallVerdict()
	{
		if (Program.UiAuditMode)
		{
			return;
		}
		// 2026-10-09（NVML 原生崩溃修复）：这一 tick 原本直接 SampleLocalPower()，
		// 采样链（LHM → Gpu.NvidiaGpu.Update → NVML）抛出的任何异常都会穿过 WinForms 消息循环把进程带走。
		// 现在整条链在最外层再兜一次（内层另有 try/catch + 句柄自愈 + 降级）。
		try
		{
			HardwareControl.SampleLocalPower();
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("powerwall-sample", $"功耗墙采样失败（已跳过本次判定，不影响 UI）：{ex.GetType().Name} {ex.Message}", 30000);
		}
		PowerWallVerdict powerWallVerdict;
		string text2;
		if (HardwareControl.LocalPowerSamplingUnavailable)
		{
			// (c) 降级：本地功耗采样已停用 → 明确显示「不可用」，不显示任何 watts 数字（也不沿用旧值）。
			powerWallVerdict = PowerWallVerdict.Unknown;
			text2 = PowerWallVerifier.UnavailableText;
			Logger.WriteLineIfChanged("powerwall-unavailable", "PowerWall: " + HardwareControl.LocalPowerSamplingStateText);
		}
		else
		{
			powerWallVerdict = HardwareControl.powerWall.Evaluate(DateTime.Now);
			text2 = (((uint)(powerWallVerdict - 1) > 1u) ? "" : HardwareControl.powerWall.Describe(DateTime.Now));
		}
		if (_powerWallStatus.Text != text2)
		{
			_powerWallStatus.Text = text2;
			if (powerWallVerdict == PowerWallVerdict.NotEnforced)
			{
				Logger.WriteLine("PowerWall: " + text2);
			}
		}
		_powerWallStatus.Visible = text2.Length > 0;
		SyncStatusRow();
		_powerWallStatus.ForeColor = ((powerWallVerdict == PowerWallVerdict.NotEnforced) ? UiVisualStyle.Warn : UiVisualStyle.Muted);
	}

	/// <summary>
	/// 2026-10-09（面板统一「显式应用」）：滑块 / 开关这类「固件字段」改动**只登记 + 置脏**，不即时下发。
	/// 原来这里是 `_pending[field]=value; _debounce.Start();` —— 400ms 后 FlushPendingAsync 立刻保存并下发一次；
	/// 现在只有点【应用】（ApplyPendingAsync）才下发，登记的值就是「待保存并下发的值」。
	/// </summary>
	private void Queue(string field, string value)
	{
		_pending[field] = value;
		MarkPanelDirty();
	}
	// PL 三行的**唯一登记入口** —— 先登记本次改动，再做联动（ 联动改出来的另一项也会走这里登记
	private void QueuePowerLimit(string field, int value)
	{
		Queue(field, value.ToString());
		if (_plLinking)
		{
			return;
		}
		LinkPowerLimits(field);
	}
	// （机主：PL2 / PL4 不应低于 PL1）： 界面联动 —— 把被联动的那几个滑块**一起改掉** 。
	private void LinkPowerLimits(string changedField)
	{
		if (_pl1 == null || _pl2 == null)
		{
			return;
		}
		MechrevoHw hw = Program.hw;
		int? pl1 = KnownPowerLimitValue("PL1", _pl1, hw?.Pl1 ?? 0);
		int? pl2 = KnownPowerLimitValue("PL2", _pl2, hw?.Pl2 ?? 0);
		// ⭐ 无 PL4 的机型（宝龙达）⇒ `_pl4` 行不可调 ⇒ 这里自然是 null ⇒ 约束自动忽略 PL4 ✅
		int? pl4 = (_pl4 == null) ? ((int?)null) : KnownPowerLimitValue("PL4", _pl4, hw?.Pl4 ?? 0);
		PowerLimitOrder.Field? changed = changedField switch
		{
			"PL1" => PowerLimitOrder.Field.Pl1,
			"PL2" => PowerLimitOrder.Field.Pl2,
			"PL4" => PowerLimitOrder.Field.Pl4,
			_ => (PowerLimitOrder.Field?)null
		};
		PowerLimitOrder.Range range1 = (_pl1.Maximum > _pl1.Minimum)
			? new PowerLimitOrder.Range(_pl1.Minimum, _pl1.Maximum)
			: PowerLimitOrder.Range.Unbounded;
		PowerLimitOrder.Range range2 = (_pl2.Maximum > _pl2.Minimum)
			? new PowerLimitOrder.Range(_pl2.Minimum, _pl2.Maximum)
			: PowerLimitOrder.Range.Unbounded;
		PowerLimitOrder.Range range4 = ((_pl4 != null && _pl4.Maximum > _pl4.Minimum))
			? new PowerLimitOrder.Range(_pl4.Minimum, _pl4.Maximum)
			: PowerLimitOrder.Range.Unbounded;
		if (!PowerLimitOrder.TryLink(changed, pl1, pl2, pl4, range1, range2, range4,
			out int? linkedPl1, out int? linkedPl2, out int? linkedPl4,
			out IReadOnlyList<PowerLimitOrder.Change> changes, out string reason))
		{
			// ⭐ 极端情形（⭐ 三行范围互相冲突）⇒ 保持原值、如实说明；⛔ 不生成违规组合 ✅
			Logger.WriteError("[pl] 联动：⚠️ 当前三行范围无法同时满足 PL1 ≤ PL2 ≤ PL4 ⇒ 本次不联动（保持原值）");
			return;
		}
		if (changes == null || changes.Count == 0)
		{
			return;
		}
		_plLinking = true;
		try
		{
			// ⭐ 程序化改值仍会走各自的 onChanged ⇒ 会把"被联动后的值"正常登记进 `_pending` ✅
			//   （⭐ `_plLinking` 只拦住"再发起一次联动"，⛔ 不拦登记）✅
			if (linkedPl1.HasValue && _pl1.Value != linkedPl1.Value)
			{
				_pl1.Value = linkedPl1.Value;
			}
			if (linkedPl2.HasValue && _pl2.Value != linkedPl2.Value)
			{
				_pl2.Value = linkedPl2.Value;
			}
			if (linkedPl4.HasValue && _pl4 != null && _pl4.Value != linkedPl4.Value)
			{
				_pl4.Value = linkedPl4.Value;
			}
		}
		finally
		{
			_plLinking = false;
		}
		// ⭐ 机主要求的那一行日志（⭐ 例：`[pl] 联动：PL1 120→100（因 PL2=100）`）✅
		//   ⚠️ 界面这一次联动只是"改了待应用的值"；真正下发时还会由校验层/单键兜底再记一条
		//      **带下发结果**的 `[pl] 联动`（那条用 WriteError ⇒ 默认级别可见）✅
		Logger.WriteLine(PowerLimitOrder.Describe(changes, reason));
	}
	// 界面某一行 PL 的"当前已知值" ——  本次已登记（待应用， 以用户刚改的为准）
	private int? KnownPowerLimitValue(string field, RSlider bar, int hwValue)
	{
		if (bar == null || bar.Maximum <= bar.Minimum)
		{
			return null;
		}
		if (_pending.TryGetValue(field, out string text) && int.TryParse(text, out int pending))
		{
			return pending;
		}
		return (hwValue > 0) ? hwValue : (int?)null;
	}

	internal static bool IsOcCheckboxEnabled(MechrevoHw? hw, bool rangesAdjustable)
	{
		int num;
		if (hw != null && hw.GpuOverclockWritable)
		{
			MechrevoDeviceCapabilities capabilities = hw.Capabilities;
			if (capabilities != null)
			{
				num = (capabilities.OverclockSettings ? 1 : 0);
				goto IL_001e;
			}
		}
		num = 0;
		goto IL_001e;
		IL_001e:
		return (byte)((uint)num & (rangesAdjustable ? 1u : 0u)) != 0;
	}

	/// <summary>
	/// 2026-10-04: make (mode, profile) the active operating profile before writing
	/// its parameters. SetMode() itself waits for OperatingMode to settle.
	/// </summary>
	private async Task<bool> SelectFirmwareTargetAsync()
	{
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.IsConnected)
		{
			return false;
		}
		// 2026-10-05: NEVER switch the working mode here. SetMode() performs a real mode
		// switch, and combined with "the editor follows mode changes" that produced a
		// feedback loop where the machine appeared to jump between modes. The editor is
		// only ever supposed to edit the CURRENT mode (user decision: the profile list
		// follows the active mode), so a mismatch means "re-target, do not write".
		int num2 = Program.service?.CurrentMode ?? _firmwareVisualMode;
		if (num2 != _firmwareVisualMode)
		{
			// Do NOT call BindFirmwareProfile() here: that closed a feedback loop
			// (see the re-entrancy guard above). The editor follows mode changes through
			// BindMode instead; this path just refuses to write into a foreign profile.
			Logger.WriteLine($"FirmwareProfile: target visualMode={_firmwareVisualMode} != active {num2}; write skipped");
			return false;
		}
		int num = ((_firmwareVisualMode == 1) ? 3 : ((_firmwareVisualMode == 2) ? 2 : 1));
		string text = "Mode" + num + "_Profile" + (_firmwareProfileIndex + 1);
		// 2026-10-05: NO SetMode() here. It performs a REAL mode switch, and when the
		// editor target and the active mode disagreed the user got moved to another mode
		// ("I opened the config editor in silent mode and it switched me to balanced").
		// The editor only edits the active mode, and the active profile already equals the
		// dropdown selection (choosing a config switches to it). Read-only verify below.
		Logger.WriteLine($"FirmwareProfile: verifying {text} (visualMode={_firmwareVisualMode})");
		// Poll instead of a fixed delay: the firmware takes an unpredictable time to
		// load the profile, and writing before that lands in the wrong profile.
		for (int i = 0; i < 20; i++)
		{
			if (hw.ProfileName == text)
			{
				Logger.WriteLine($"FirmwareProfile: confirmed {text} after {i * 100}ms");
				return true;
			}
			await Task.Delay(100);
		}
		Logger.WriteLine($"FirmwareProfile: readback ProfileName={hw.ProfileName} expected={text} MISMATCH - write aborted");
		return false;
	}

	/// <summary>
	/// 2026-10-15（bugfix-fan-logo bug①，round2 用户改案「保存 = 保存 + 自动应用一次」）：
	/// 曲线窗口每轮写完的回调（<paramref name="confirmed"/> = 回读确认是否一致）。
	/// • confirmed=true：曲线已落盘（= 最新落盘值）⇒ **自动应用一次**：ActivateAsync(forceParameters:true)
	///   以最新落盘值为曲线基线（BuildFanCurveBaselineEdits）强制重发到 EC —— 保存即生效；
	///   这一次重发是**幂等保护**：FlushPendingAsync 已把同一份曲线写进 EC，重发的是同一个值，
	///   目的是对冲"随后任何旧快照全量重写路径"，并让结果在状态行可见。
	/// • confirmed=false：保存失败（回读不一致/写抛异常）⇒ 状态行可见红字 + 日志，⛔ 不静默、不下发。
	/// </summary>
	private void OnFanCurveSavedFromCurveWindow(bool confirmed)
	{
		if (IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			try
			{
				BeginInvoke((Action<bool>)OnFanCurveSavedFromCurveWindow, confirmed);
			}
			catch (ObjectDisposedException)
			{
			}
			return;
		}
		if (!confirmed)
		{
			_status.Text = Strings.SaveUnconfirmedRetry;
			_status.ForeColor = UiVisualStyle.Danger;
			Logger.WriteLine("[fan] 曲线自动应用跳过：保存未确认（回读不一致或写失败）——见曲线窗口状态行");
			return;
		}
		// ⭐⭐ 2026-10-18（机主：保存才算数）：⭐ 保存动作本身已经应用（宝龙达 SaveBaolongdaFanCurve 内立即闭环 / 同方保存时固件上传），
		//   ⛔ 这里不再做第二次 auto-apply（避免拖一次保存触发两轮全量重写）✅
		_status.Text = Strings.FanCurveSavedAndEnabled;
		_status.ForeColor = UiVisualStyle.Muted;
		Logger.WriteLine("[fan] 曲线：保存回调（保存动作已应用，⛔ 不再二次 auto-apply）");
	}

	/// <summary>
	/// round2：保存成功后的**自动应用**。用最新落盘值为基线（BuildFanCurveBaselineEdits ⇒
	/// PerfModeStore.LoadSettings 直读 config），ActivateAsync(forceParameters:true) 全量重发一次。
	/// 失败 ⇒ 状态行可见红字 + Logger，成功 ⇒ Ok 文案。与本轮点【应用】共走 _saveLock，不并发。
	/// </summary>
	private async Task ApplyFanCurveAutoApplyAsync()
	{
		PerfModeService perf = PerfModeService.Instance;
		MechrevoHw hw = Program.hw;
		if (IsDisposed || (object)perf == null || hw == null || !hw.IsConnected)
		{
			return;
		}
		PerfModeDefinition target = perf.Find(_modeId);
		if ((object)target == null)
		{
			Logger.WriteLine("[fan] 曲线自动应用跳过：目标档位不存在 " + _modeId);
			return;
		}
		await _saveLock.WaitAsync();
		try
		{
			_status.Text = Strings.ModeTuneApplying;
			_status.ForeColor = UiVisualStyle.Muted;
			Logger.WriteLine("[fan] 曲线自动应用：ActivateAsync(forceParameters=true) mode=" + target.Id + "（基线=最新落盘值，见 BuildFanCurveBaselineEdits）");
			PerfApplyOutcome outcome = await perf.ActivateAsync(target.Id, "fan-curve auto-apply", forceParameters: true);
			if (IsDisposed)
			{
				return;
			}
			if ((object)outcome == null || !outcome.ModeSwitched)
			{
				_status.Text = Strings.ParamsReverted;
				_status.ForeColor = UiVisualStyle.Danger;
				Logger.WriteLine("[fan] 曲线自动应用失败（ModeSwitched=false）：曲线窗口已确认 EC 值，配置基线=最新落盘值；可点【应用】重试");
				return;
			}
			_status.Text = Strings.ConfirmedAt + " " + DateTime.Now.ToString("HH:mm:ss");
			_status.ForeColor = UiVisualStyle.Ok;
			Logger.WriteLine("[fan] 曲线自动应用完成：mode=" + target.Id);
		}
		catch (Exception ex)
		{
			if (!IsDisposed)
			{
				_status.Text = Strings.ParamsSaveFailed;
				_status.ForeColor = UiVisualStyle.Danger;
			}
			Logger.WriteLine("[fan] 曲线自动应用异常: " + ex.GetType().Name + " " + ex.Message);
		}
		finally
		{
			_saveLock.Release();
		}
	}

	/// <summary>
	/// 2026-10-09（面板统一「显式应用」）：登记「有未应用的改动」—— 只点亮【应用】并打一行去重日志，
	/// **不下发**。所有行的改动（滑块 / 开关 / 下拉 / GPU 偏移）都汇到这里。
	/// </summary>
	private void MarkPanelDirty()
	{
		if (!_panelDirty)
		{
			_panelDirty = true;
			Logger.WriteLineIfChanged("panel-dirty", "自定义面板有未应用的改动（只改界面，点【应用】才保存并下发）");
		}
		SyncApplyButtons();
	}

	/// <summary>
	/// 2026-10-09（面板统一「显式应用」）：丢弃「未应用的改动」——切档 / 重新绑定档位 / 关窗重开 /
	/// 恢复出厂 / 删档时调用。**既不静默下发、也不静默回滚**：界面马上会按「已保存的值」重载，
	/// 提示由调用方负责（SelectModeAsync / VisibleChanged）。
	/// </summary>
	private void DiscardPendingChanges()
	{
		if (_panelDirty)
		{
			Logger.WriteLineIfChanged("panel-dirty-discard", $"未应用的改动被丢弃（未下发）：字段 {_pending.Count} 项 / 应用侧 {_pendingEdits.Count} 项 / GPU 偏移 {_gpuOcDirty}");
		}
		_pending.Clear();
		_pendingEdits.Clear();
		_pendingPowerModeEdit = null;
		_gpuOcDirty = false;
		_panelDirty = false;
		SyncApplyButtons();
	}

	/// <summary>
	/// 2026-10-09：「应用」按钮在「有未应用的改动」时可用；应用成功后回到禁用（已应用）态。
	/// 2026-10-09（非自定义档的应用侧行）：<c>_optionsLocked</c> **不再**一律禁用【应用】——
	/// 锁定档里只有硬件参数行是禁用的，应用侧那几行（电源模式/电源计划/睿频/风扇增强/刷新率）仍可改，
	/// 必须能应用；若真有硬件字段/GPU 偏移待应用（锁定态下不可达），才保持禁用、不静默丢弃。
	/// 2026-10-15（bugfix-fan-logo bug①，round2 用户改案）：风扇曲线**保存即自动应用**，不再走【应用】；
	/// 面板上没有其它待应用改动时【应用】也保持可用 —— 语义升级为「修复型重应用当前面板状态」：
	/// 无 pending 时点它 = 以最新落盘值为基线全量重发一次（幂等，BuildFanCurveBaselineEdits 保证曲线
	/// 基线是最新落盘值，⛔ 不会覆盖刚保存的曲线）。_panelDirty 仍只统计"非风扇曲线"的待应用改动
	/// （曲线本来就不进 _pending/_pendingEdits —— 曲线只走曲线窗口的保存路径）。
	/// </summary>
	private void SyncApplyButtons()
	{
		if (IsDisposed)
		{
			return;
		}
		// round2：面板无待应用改动（!_panelDirty）时也保持可用 = "修复型重应用"入口；
		// 锁定档守卫照旧（锁定态下若真有硬件字段/GPU 偏移待应用，仍禁用）。
		bool canApply = !_optionsLocked || (_pending.Count == 0 && !_gpuOcDirty);
		if (_applyButton != null)
		{
			_applyButton.Enabled = canApply;
		}
		// 2026-10-09（用户要求）：GPU 超频行的行内「应用」已删 —— 整面板只剩底部这颗主按钮，
		// 它同时也带 GPU 偏移（_gpuOcDirty 已并入 _panelDirty，见 ApplyPendingAsync）。
	}

	/// <summary>
	/// 2026-10-09（面板统一「显式应用」）：把「未应用的改动」的值重新写回控件 —— 只在
	/// <see cref="_panelDirty" /> 时、紧跟在一次硬件回读同步（OnCustomChanged）之后调用。
	/// 理由：以前每行改一下 400ms 内就下发，回读几乎撞不上用户的手；现在改动可以停很久，
	/// 一次回读就会把用户刚改好的值抹掉（GPU 偏移此前用 _gpuOcDirty 单独护着，现在整面板统一护）。
	/// ⚠️ 只写控件值（_syncing=true ⇒ 不会反过来触发 onChanged），不 Queue、不下发。
	/// </summary>
	private void RestorePendingControlValues()
	{
		foreach (KeyValuePair<string, string> item in _pending)
		{
			if (!int.TryParse(item.Value, out int value))
			{
				continue;
			}
			switch (item.Key)
			{
			case "PL1":
				RestoreSliderValue(_pl1, _pl1Val, value);
				break;
			case "PL2":
				RestoreSliderValue(_pl2, _pl2Val, value);
				break;
			// （30 系 · SMU 功耗时间窗）：行不存在（_stapmTime == null）时跳过（ 不凭空补控件）。
			case "SmuStapmTimeSeconds":
				if (_stapmTime != null)
				{
					RestoreSliderValue(_stapmTime, _stapmTimeVal, value);
				}
				break;
			case "PL4":
				RestoreSliderValue(_pl4, _pl4Val, value);
				break;
			case "CpuTccOffsetSwitch":
				_tccChk.Checked = (value == 1);
				_tcc.Enabled = (value == 1);
				break;
			case "CpuTccOffset":
				RestoreSliderValue(_tcc, _tccVal, value);
				break;
			case "GpuConfigurableTGPTarget":
				RestoreSliderValue(_tgp, _tgpVal, value);
				break;
			case "GpuDynamicBoostSwitch":
				_dbChk.Checked = (value == 1);
				_db.Enabled = (value == 1);
				break;
			case "GpuDynamicBoost":
				RestoreSliderValue(_db, _dbVal, value);
				break;
			case "FanSwitchSpeedEnabled":
				_fanSwitchSpeedChk.Checked = (value == 1);
				_fanSwitchSpeed.Enabled = (value == 1);
				_fanSwitchSpeedVal.Enabled = (value == 1);
				break;
			case "FanSwitchSpeed":
				RestoreSliderValue(_fanSwitchSpeed, _fanSwitchSpeedVal, value);
				break;
			case "OverClockingSwitch":
				_ocChk.Checked = (value == 1);
				break;
			}
		}
	}

	private static void RestoreSliderValue(RSlider bar, RNumericUpDown valueInput, int value)
	{
		// 2026-10-09：滑块与数字框各自的范围未必完全一致（ApplyRange 只会把滑块的范围"放宽"），
		// 故两边都按当前范围夹一次再写，绝不把越界值塞进控件（否则控件自己会抛
		// 「"x" 的值对于 "Value" 无效」——那是 ApplyRange 那边的既有问题，与本方法无关）。
		int num = value;
		if (bar.Minimum <= bar.Maximum)
		{
			num = Math.Clamp(num, bar.Minimum, bar.Maximum);
		}
		decimal num2 = valueInput.Minimum;
		decimal num3 = valueInput.Maximum;
		if (num2 <= num3)
		{
			num = (int)Math.Clamp((decimal)num, num2, num3);
		}
		if (bar.Value != num)
		{
			bar.Value = num;
		}
		if (valueInput.Value != (decimal)num)
		{
			valueInput.Value = num;
		}
	}


	internal static PerfModeSettings ApplyFields(PerfModeSettings settings, IReadOnlyDictionary<string, string> fields)
	{
		ArgumentNullException.ThrowIfNull(settings, "settings");
		ArgumentNullException.ThrowIfNull(fields, "fields");
		PerfModeSettings perfModeSettings = settings;
		foreach (var (text3, s) in fields)
		{
			if (int.TryParse(s, out var result))
			{
				perfModeSettings = text3 switch
				{
					"PL1" => perfModeSettings with
					{
						Pl1 = result
					}, 
					"PL2" => perfModeSettings with
					{
						Pl2 = result
					}, 
					// （30 系 · SMU 功耗时间窗）：键 = SmuStapmTimeSeconds，单位秒；
					"SmuStapmTimeSeconds" => perfModeSettings with
					{
						StapmTimeSeconds = result
					}, 
					"PL4" => perfModeSettings with
					{
						Pl4 = result
					}, 
					"CpuTccOffsetSwitch" => perfModeSettings with
					{
						TccOn = (result == 1)
					}, 
					"CpuTccOffset" => perfModeSettings with
					{
						TccTarget = result
					}, 
					"GpuConfigurableTGPTarget" => perfModeSettings with
					{
						GpuTgp = result
					}, 
					"GpuDynamicBoostSwitch" => perfModeSettings with
					{
						GpuDynamicBoostOn = (result == 1)
					}, 
					"GpuPowerLimitW" => perfModeSettings with
			{
				GpuPowerLimitW = new int?(result)
			},
			"GpuDynamicBoost" => perfModeSettings with
					{
						GpuDynamicBoost = result
					}, 
					"FanSwitchSpeedEnabled" => perfModeSettings with
					{
						FanSwitchSpeedOn = (result == 1)
					}, 
					"FanSwitchSpeed" => perfModeSettings with
					{
						FanSwitchSpeedMs = result
					}, 
					"OverClockingSwitch" => perfModeSettings with
					{
						GpuOverclockOn = (result == 1)
					}, 
					"GpuCoreClockOffsetOC" => perfModeSettings with
					{
						GpuCoreOffset = result
					}, 
					"GpuMemoryClockOffsetOC" => perfModeSettings with
					{
						GpuMemoryOffset = result
					}, 
					_ => perfModeSettings, 
				};
			}
		}
		return perfModeSettings;
	}

	/// <summary>
	/// 2026-10-15（bugfix-fan-logo bug①，round2）：构造"把曲线基线刷成**最新落盘值**"的 edit 函数。
	/// 直读 PerfModeStore.LoadSettings（config 的 perf_<id>_fancpu/_fangpu），绕开 PerfModeService 的
	/// 模式缓存；无落盘曲线（两侧都空）⇒ 返回 null（没有可刷的）。ApplyPendingAsync 与
	/// ApplyFanCurveAutoApplyAsync（保存后自动应用）共用这一份口径 ⇒ 两条路径都⛔不会用旧快照覆盖曲线。
	/// ⚠️ 只动 CpuFanDuty/GpuFanDuty，其余字段语义零改动。
	/// </summary>
	// round2 fixup：不用 `T?` 注解（项目未开 #nullable，避免新增 CS8632），用 (object)==null 惯用法。
	private static Func<PerfModeSettings, PerfModeSettings> BuildFanCurveBaselineEdits(PerfModeService perf, string modeId)
	{
		PerfModeDefinition baselineMode = perf.Find(modeId);
		if ((object)baselineMode == null)
		{
			return null;
		}
		PerfModeSettings persisted = PerfModeStore.LoadSettings(baselineMode.Id);
		if (persisted.CpuFanDuty == null && persisted.GpuFanDuty == null)
		{
			return null;
		}
		Logger.WriteLine($"[fan] 曲线基线取最新落盘值：cpu={(persisted.CpuFanDuty == null ? "(无)" : string.Join(',', persisted.CpuFanDuty))} gpu={(persisted.GpuFanDuty == null ? "(无)" : string.Join(',', persisted.GpuFanDuty))}");
		return (PerfModeSettings s) => s with
		{
			CpuFanDuty = persisted.CpuFanDuty,
			GpuFanDuty = persisted.GpuFanDuty
		};
	}
	// 宝龙达 UI 近似范围，只是让滑块可操作， 不是硬件范围；写入仍以回读为准）；显存不另造兜底
	internal static (int CoreMin, int CoreMax, int MemMin, int MemMax) GpuOffsetUiRange(MechrevoHw hw)
	{
		// 滑块本身固定 -500..500（见构造点），数字框可超出 ±500 并保留真实值
		int coreMin = -900;
		int coreMax = 2000;
		int memMin = VramOffsetNegativeFloor;
		int memMax = 2000;
		string source = "兜底";
		if (NvapiPStates.TryGetP0OffsetRange(out int nvCoreMin, out int nvCoreMax, out int nvMemMin, out int nvMemMax))
		{
			coreMin = Math.Max(nvCoreMin, -900);
			coreMax = Math.Min(nvCoreMax, 2000);
			memMin = Math.Max(nvMemMin, VramOffsetNegativeFloor);
			memMax = Math.Min(nvMemMax, 2000);
			source = "NVAPI";
		}
		else
		{
			int vendorCoreMin = hw?.GpuCoreOffsetUserMinimum ?? (-1);
			int vendorCoreMax = hw?.GpuCoreOffsetUserMaximum ?? (-1);
			int vendorMemMin = hw?.GpuMemoryOffsetUserMinimum ?? (-1);
			int vendorMemMax = hw?.GpuMemoryOffsetUserMaximum ?? (-1);
			if (vendorCoreMax > vendorCoreMin)
			{
				coreMin = Math.Max(vendorCoreMin, -900);
				coreMax = Math.Min(vendorCoreMax, 2000);
				source = "厂商";
			}
			if (vendorMemMax > vendorMemMin)
			{
				memMin = Math.Max(vendorMemMin, VramOffsetNegativeFloor);
				memMax = Math.Min(vendorMemMax, 2000);
			}
		}
		if (coreMax <= coreMin)
		{
			coreMin = -900;
			coreMax = 2000;
		}
		if (memMax <= memMin)
		{
			memMin = VramOffsetNegativeFloor;
			memMax = 2000;
		}
		string floorLog = "[gpu-oc] 显存偏移下限：" + VramOffsetNegativeFloor + "（GCU 固件实测边界；超过写不进去）";
		if (Logger.TryMarkChanged("gpu-oc-vram-negative-floor", floorLog))
		{
			Logger.WriteError(floorLog);
		}
		string uiLog = "[gpu-oc] 偏移 UI：slider=-500..500 · numeric=core=" + coreMin + ".." + coreMax
			+ " mem=" + memMin + ".." + memMax + "（来源=" + source + "）";
		if (Logger.TryMarkChanged("gpu-oc-offset-ui-range", uiLog))
		{
			Logger.WriteError(uiLog);
		}
		return (CoreMin: coreMin, CoreMax: coreMax, MemMin: memMin, MemMax: memMax);
	}

	/// <summary>⭐ 刷新时应用 GPU 偏移 UI 范围：滑块固定 ±500，数字框用 NV 范围，各自 clamp（⛔ 不互相覆盖真实值）✅</summary>
	private static bool ApplyGpuOffsetUiRange(RSlider bar, RNumericUpDown valueInput, int numericMin, int numericMax, int current, int sliderMin, int sliderMax)
	{
		bar.Minimum = sliderMin;
		bar.Maximum = sliderMax;
		valueInput.Minimum = numericMin;
		valueInput.Maximum = numericMax;
		int real = Math.Clamp(current, numericMin, numericMax);
		valueInput.Value = real;
		bar.Value = Math.Clamp(real, bar.Minimum, bar.Maximum);
		return numericMax > numericMin;
	}

	private static (int Min, int Max, int Value, bool Adjustable) DeviceRange(int minimum, int maximum, int current, bool allowNegative = false)
	{
		if ((allowNegative || minimum >= 0) && maximum > minimum)
		{
			return (Min: minimum, Max: maximum, Value: Math.Clamp(current, minimum, maximum), Adjustable: true);
		}
		if (Program.UiAuditMode)
		{
			return (Min: 0, Max: 100, Value: Math.Clamp(current, 0, 100), Adjustable: true);
		}
		int num = Math.Max(0, current);
		return (Min: num, Max: num, Value: num, Adjustable: false);
	}

	internal static string? GetPowerPlanGuid(object? selectedItem)
	{
		if (!(selectedItem is KeyValuePair<string, string> keyValuePair) || !Guid.TryParse(keyValuePair.Value, out var result))
		{
			return null;
		}
		return result.ToString();
	}

	internal async Task<bool> RefreshHardwareStateAsync()
	{
		if (_refreshingHardwareState)
		{
			return false;
		}
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.IsConnected)
		{
			_status.Text = GcuDisconnectedText;
			_status.ForeColor = UiVisualStyle.Danger;
			return false;
		}
		_refreshingHardwareState = true;
		try
		{
			await Task.Run((Func<bool>)hw.EnsureDirectGpuOverclock);
			if (!(await hw.RequestCustomModePageStateAsync()))
			{
				// （机主：删掉顶部红字「本机无 GCU，硬件状态页不可读」）
				if (hw.UsesBaolongda)
				{
					_status.Text = string.Empty;
					_status.ForeColor = UiVisualStyle.Muted;
					SyncStatusRow();
				}
				else
				{
					_status.Text = FanTableTimeoutText;
					_status.ForeColor = UiVisualStyle.Danger;
				}
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Custom mode status refresh failed: " + ex.Message);
			// （机主：删掉顶部红字「本机无 GCU，硬件状态页不可读」）
			if (hw.UsesBaolongda)
			{
				_status.Text = string.Empty;
				_status.ForeColor = UiVisualStyle.Muted;
				SyncStatusRow();
			}
			else
			{
				_status.Text = FanTableTimeoutText;
				_status.ForeColor = UiVisualStyle.Danger;
			}
			return false;
		}
		finally
		{
			_refreshingHardwareState = false;
			OnCustomChanged();
		}
	}

	private static void SetRowVisible(Control control, bool visible, Control? value = null)
	{
		if (control.Tag is Control control2)
		{
			control2.Visible = visible;
		}
		if (value != null)
		{
			value.Visible = visible;
		}
	}

	private static bool ApplyRange(RSlider bar, RNumericUpDown valueInput, int minimum, int maximum, int current, bool allowNegative = false)
	{
		(int, int, int, bool) tuple = DeviceRange(minimum, maximum, current, allowNegative);
		// 两边范围先各自扩到包含目标值 ⇒ 赋值不会越界；最后落到最终范围再 clamp 一次
		bar.Minimum = Math.Min(bar.Minimum, tuple.Item1);
		bar.Maximum = Math.Max(bar.Maximum, tuple.Item2);
		valueInput.Minimum = Math.Min(valueInput.Minimum, tuple.Item1);
		valueInput.Maximum = Math.Max(valueInput.Maximum, tuple.Item2);
		int clampedValue = Math.Clamp(tuple.Item3, tuple.Item1, tuple.Item2);
		bar.Value = Math.Clamp(clampedValue, bar.Minimum, bar.Maximum);
		valueInput.Value = Math.Clamp(clampedValue, (int)valueInput.Minimum, (int)valueInput.Maximum);
		bar.Minimum = tuple.Item1;
		bar.Maximum = tuple.Item2;
		bar.Value = Math.Clamp(clampedValue, tuple.Item1, tuple.Item2);
		valueInput.Minimum = tuple.Item1;
		valueInput.Maximum = tuple.Item2;
		valueInput.Value = Math.Clamp(clampedValue, tuple.Item1, tuple.Item2);
		bar.Enabled = tuple.Item4;
		valueInput.Enabled = tuple.Item4;
		SetRowVisible(bar, tuple.Item4, valueInput);
		return tuple.Item4;
	}

	private void SyncGpuOverclockDependents()
	{
		bool flag = _ocChk.Checked && _ocChk.Enabled;
		RSlider coreOc = _coreOc;
		bool enabled = (_coreOcVal.Enabled = flag && _gpuOcCoreAdjustable);
		coreOc.Enabled = enabled;
		RSlider memOc = _memOc;
		enabled = (_memOcVal.Enabled = flag && _gpuOcMemoryAdjustable);
		memOc.Enabled = enabled;
		// 2026-10-09：可调整状态一变，「应用」按钮的可用性与红/不红的呈现都要跟着重算。
		SyncApplyButtons();
		SyncOcRiskVisuals();
	}

	/// <summary>2026-10-09：核心偏移的风险阈值 —— 50 系 +200，其它（含代际未知）按更严的 +150。</summary>
	private static int CoreOffsetWarnThreshold()
	{
		// 与 MechrevoHw.DgpuGeneration 同源（同一个 GpuGenerationProvider），硬件没连上时也能判；
		// 代际未知/无独显一律落到更严的 150。
		return (GpuGenerationProvider.Current().Generation == DgpuGenerationKind.Gen50) ? CoreOffsetWarnGen50 : CoreOffsetWarnDefault;
	}

	/// <summary>
	/// 2026-10-09：GPU 偏移的数值改动只在这里「置脏」——**不 Queue、不下发**。
	/// 2026-10-09（面板统一「显式应用」）：它同时点亮整面板的【应用】（_panelDirty）；
	/// 两个偏移值在 ApplyPendingAsync 里、点【应用】的那一瞬间才从滑块上读一次。
	/// </summary>
	private void MarkGpuOffsetDirty()
	{
		_gpuOcDirty = true;
		Logger.WriteLineIfChanged("gpu-oc-dirty", $"GPU 偏移已改动待应用: core={_coreOc.Value} memory={_memOc.Value}（未下发，等「应用」）");
		MarkPanelDirty();
	}

	/// <summary>
	/// 2026-10-09（面板统一「显式应用」）：这三件事已经并入新的统一路径，故删掉：
	/// ① SyncGpuApplyButton → <see cref="SyncApplyButtons" />（底部主按钮与行内小按钮同一判据）；
	/// ② ClearGpuOffsetDirty → <see cref="DiscardPendingChanges" />（连 _pending/_pendingEdits 一起作废）；
	/// ③ ApplyGpuOverclockOffsets → <see cref="ApplyPendingAsync" />（GPU 偏移不再单独下发，
	///    而是和面板里其余改动同一次下发；值仍在点【应用】时才从滑块读）。
	/// </summary>

	/// <summary>
	/// 2026-10-09：按当前数值刷新两条偏移滑块的风险呈现（只改颜色/提示，**不改任何下发值**）：
	/// 核心 &gt; 阈值（50 系 +200、其它 +150，未知按 150）或显存 &gt; +1000 ⇒ 滑块强调色换成语义「危险红」
	/// <see cref="UiVisualStyle.Danger" />、数值与行标题也转红，并显示一行风险提示；否则全部还原。
	/// </summary>
	private void SyncOcRiskVisuals()
	{
		if (IsDisposed || _coreOc == null || _memOc == null || _coreOcVal == null || _memOcVal == null)
		{
			return;
		}
		// （机主：显存 >+1000 红字提示）：风险判据统一用**实际生效值 = 数字框值**
		int coreRiskValue = (int)_coreOcVal.Value;
		int memRiskValue = (int)_memOcVal.Value;
		int coreThreshold = CoreOffsetWarnThreshold();
		bool coreRisk = coreRiskValue > coreThreshold;
		bool memRisk = memRiskValue > VramOffsetWarn;
		bool flag = ApplyOcRiskVisual(_coreOc, _coreOcVal, _coreOcRiskRow, coreRisk);
		bool flag2 = ApplyOcRiskVisual(_memOc, _memOcVal, _memOcRiskRow, memRisk);
		string riskLog = "[gpu-oc] 风险提示：core=" + coreRiskValue + "（阈值 " + coreThreshold + "） · mem=" + memRiskValue
			+ "（阈值 " + VramOffsetWarn + "） ⇒ core提示=" + coreRisk + " mem提示=" + memRisk;
		if (Logger.TryMarkChanged("gpu-oc-risk-hint", riskLog))
		{
			Logger.WriteError(riskLog);
		}
		if ((flag || flag2) && _minContentWidth > 0)
		{
			ResizeToContent();
		}
	}

	/// <summary>2026-10-09：返回「提示行的可见性是否发生变化」（变化才需要重算窗口高度）。</summary>
	private static bool ApplyOcRiskVisual(RSlider bar, RNumericUpDown valueInput, TableLayoutPanel? riskRow, bool risk)
	{
		Color color = (risk ? UiVisualStyle.Danger : UiVisualStyle.Text);
		bar.AccentOverride = (risk ? UiVisualStyle.Danger : ((Color?)null));
		valueInput.ForeColor = color;
		// 数字本身是内嵌编辑子控件画的（ApplyTree 给父子都下发 ForeColor），故两边都要改。
		foreach (Control control in valueInput.Controls)
		{
			control.ForeColor = color;
		}
		if (bar.Tag is TableLayoutPanel tableLayoutPanel && tableLayoutPanel.GetControlFromPosition(0, 0) is Label label)
		{
			label.ForeColor = color;
		}
		bool result = false;
		if (riskRow != null && riskRow.Visible != risk)
		{
			riskRow.Visible = risk;
			result = true;
		}
		return result;
	}

	private void RestartAsAdmin()
	{
		if (!NativeMethods.HasFreshUserInput())
		{
			Logger.WriteLine("Restart-as-admin ignored: no fresh user input.");
			return;
		}
		Action restartAsAdminOverride = RestartAsAdminOverride;
		if (restartAsAdminOverride != null)
		{
			restartAsAdminOverride();
			return;
		}
		Logger.WriteLine("Restart-as-admin requested by user (GPU overclock requires elevation).");
		ProcessHelper.RunAsAdmin();
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（"未设置"）：⭐ 该行**已下发过** ⇒ ⭐ 把标题里的「（未设置）」去掉 ✅。
	/// <para>⚠️ <b>只减不增</b>：⭐ 标题列宽是建窗体时按**最长**标题算好的
	/// （⭐ 见 <c>_pl1Label</c> 的注释 ⚠️）⇒ ⭐ 变短一定安全、⭐ 变长会**裁字** ❌
	/// ⇒ ⭐ 所以这里**只移除**标记、⛔ 绝不添加（⭐ 标记只由构造期按当时状态决定 ✅）。</para>
	/// </summary>
	private void SyncUnsetMark(Label label, bool unset)
	{
		if (label == null || unset)
		{
			return;
		}
		string text = label.Text;
		if (text != null && text.EndsWith(UnsetMark, StringComparison.Ordinal))
		{
			label.Text = text.Substring(0, text.Length - UnsetMark.Length);
		}
	}

	private void OnCustomChanged()
	{
		try
		{
			if (IsDisposed)
			{
				return;
			}
			if (InvokeRequired)
			{
				if (Interlocked.Exchange(ref _customChangedQueued, 1) == 0)
				{
					BeginInvoke(() =>
					{
						Interlocked.Exchange(ref _customChangedQueued, 0);
						OnCustomChanged();
					});
				}
				return;
			}
			MechrevoHw hw = Program.hw;
			if (hw == null)
			{
				return;
			}
			_syncing = true;
			// 2026-10-09（面板统一「显式应用」）：面板里有「改了但没点应用」的值时，这一次硬件回读
			// **不留下痕迹** —— 下面照旧按回读值同步（可见性/可用性/范围都跟着更新），但在 _syncing 块
			// 的最后把待应用的值原样写回控件（RestorePendingControlValues）。
			// 原来只有 GPU 偏移用 _gpuOcDirty 单独护着；现在整面板统一护。
			bool keepPending = _panelDirty;
			try
			{
				ApplyRange(_pl1, _pl1Val, hw.Pl1Minimum, hw.Pl1Maximum, hw.Pl1);
				ApplyRange(_pl2, _pl2Val, hw.Pl2Minimum, hw.Pl2Maximum, hw.Pl2);
				// （写成功后 NoteBaolongdaSmuStapmTimeApplied 会把它更新成刚下发的值；
				if (_stapmTime != null)
				{
					int stapmTimeNow = hw.BaolongdaStapmTimeSeconds;
					if (stapmTimeNow > 0)
					{
						ApplyRange(_stapmTime, _stapmTimeVal, BaolongdaProfile.StapmTimeMinSeconds,
							BaolongdaProfile.StapmTimeMaxSeconds, stapmTimeNow);
						// 「（未设置）」只减不增（列宽是建窗时算好的）
						SyncUnsetMark(_stapmTimeLabel, unset: false);
					}
				}
				// ⭐⭐ 2026-10-16（"未设置"）：⭐ 值一旦下发过 ⇒ ⭐ 把标题里的「（未设置）」**去掉** ✅
				//   ⚠️ **只减不增** —— ⭐ 标题列宽是**建窗体时**按最长标题算好的（⭐ 见 `_pl1Label` 的注释 ⚠️）
				//      ⇒ ⭐ 变短安全、⛔ 变长会裁字 ⇒ ⭐ 本方法只允许移除标记、⛔ 绝不添加 ✅
				SyncUnsetMark(_pl1Label, hw.BaolongdaPl1Unset);
				SyncUnsetMark(_pl2Label, hw.BaolongdaPl2Unset);
				ApplyRange(_pl4, _pl4Val, hw.Pl4Minimum, hw.Pl4Maximum, hw.Pl4);
				bool flag = ApplyRange(_tcc, _tccVal, hw.TccMinimum, hw.TccMaximum, hw.TccTarget);
				SyncUnsetMark(_tccLabel, hw.BaolongdaTempWallUnset);
				bool enabled = ApplyRange(_tgp, _tgpVal, hw.GpuTgpMinimum, hw.GpuTgpMaximum, hw.GpuTgp);
				bool flag2 = ApplyRange(_db, _dbVal, hw.GpuDbMinimum, hw.GpuDbMaximum, hw.GpuDb);
				bool supportsGpuOverclock = hw.SupportsGpuOverclock;
				bool gpuOverclockRequiresElevation = hw.GpuOverclockRequiresElevation;
				bool flag3 = supportsGpuOverclock && !gpuOverclockRequiresElevation;
				bool num = hw.GpuOverclockEnabled & supportsGpuOverclock;
				// 2026-10-10（用户反馈 ④「自定义模式的显卡超频开关状态貌似不能记忆」）：第三项
				// AppConfig.Get("gpu_oc") 就是"**记忆下来的**开关状态"—— 实时硬件回读（num）与本次
				// 会话内的手动态（_gpuOcArmed）都答不上来时（例：刚重启、驱动侧 direct 后端还没起来、
				// 或 GCU 的状态流里 OverClockingSwitch 粘滞），按记下来的那一份显示，而不是一律显示"关"。
				bool flag4 = (num || _gpuOcArmed || AppConfig.Is("gpu_oc")) & supportsGpuOverclock;
				if (num)
				{
					// 2026-10-09：核心/显存有「改了还没点应用」的值时，不要被一次硬件状态刷新拉回回读值
					// （否则用户刚拖好的值会被悄悄抹掉）；其余滑块/开关的 ApplyRange 路径一字未动。
					if (!_gpuOcDirty)
					{
						(int refreshCoreMin, int refreshCoreMax, int refreshMemMin, int refreshMemMax) = GpuOffsetUiRange(hw);
						_gpuOcCoreAdjustable = ApplyGpuOffsetUiRange(_coreOc, _coreOcVal, refreshCoreMin, refreshCoreMax, hw.EffectiveGpuCoreClockOffset, -500, 500);
						_gpuOcMemoryAdjustable = ApplyGpuOffsetUiRange(_memOc, _memOcVal, refreshMemMin, refreshMemMax, hw.EffectiveGpuMemoryClockOffset, -1000, 2000);
						// ⭐ 面板刷新后也要重新评估风险提示（数字框值可能已被更新）✅
						SyncOcRiskVisuals();
					}
				}
				else if (_gpuOcArmed & supportsGpuOverclock)
				{
					_gpuOcCoreAdjustable = hw.GpuCoreOffsetAdjustable;
					_gpuOcMemoryAdjustable = hw.GpuMemoryOffsetAdjustable;
				}
				bool flag5 = ApplyRange(_fanSwitchSpeed, _fanSwitchSpeedVal, hw.FanSwitchSpeedMinimum, hw.FanSwitchSpeedMaximum, hw.FanSwitchSpeed);
				SetRowVisible(_tccChk, flag);
				SetRowVisible(_dbChk, flag2);
				SetRowVisible(_ocChk, supportsGpuOverclock);
				SetRowVisible(_fanSwitchSpeedChk, hw.SupportsFanSwitchSpeed);
				SetRowVisible(_fanSwitchSpeed, hw.SupportsFanSwitchSpeed, _fanSwitchSpeedVal);
				_fanSwitchSpeedChk.Checked = hw.FanSwitchSpeedEnabled;
				_fanSwitchSpeedChk.Enabled = hw.SupportsFanSwitchSpeed;
				if (!keepPending)
				{
					PerfModeSettings persistedFan = PerfModeStore.LoadSettings(_modeId);
					if (persistedFan.FanSwitchSpeedOn.HasValue)
					{
						_fanSwitchSpeedChk.Checked = persistedFan.FanSwitchSpeedOn.Value;
					}
					if (persistedFan.FanSwitchSpeedMs.HasValue && hw.SupportsFanSwitchSpeed)
					{
						_fanSwitchSpeed.Value = persistedFan.FanSwitchSpeedMs.Value;
					}
				}
				RSlider fanSwitchSpeed = _fanSwitchSpeed;
				bool fanSwitchOn = hw.SupportsFanSwitchSpeed && _fanSwitchSpeedChk.Checked;
				bool enabled2 = (_fanSwitchSpeedVal.Enabled = fanSwitchOn & flag5);
				fanSwitchSpeed.Enabled = enabled2;
				_tgp.Enabled = enabled;
				_dbChk.Checked = hw.GpuDbSwitch;
				_dbChk.Enabled = flag2;
				_db.Enabled = hw.GpuDbSwitch & flag2;
				_tccChk.Checked = hw.TccSwitch;
				_tccChk.Enabled = flag;
				_tcc.Enabled = hw.TccSwitch & flag;
				// 宝龙达只写不读；同方 GCU 以 LM 本地保存值为准（更新/GCU 重装后不依赖 GCU 里的档位文件）
				bool gcuGen40 = hw.IsGcuConnected && (hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40
					|| hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30);
				if ((hw.UsesBaolongda || gcuGen40) && !keepPending)
				{
					PerfModeSettings persisted = PerfModeStore.LoadSettings(_modeId);
					bool localUnset = persisted.IsUntouched && (hw.UsesBaolongda || hw.WujieSmuApu);
					if (localUnset)
					{
						// 空白本地自定义档显示「未设置」，不沿用上一次应用后的实时硬件值
						ResetPowerWallRowVisual(_pl1, _pl1Val, _pl1Label, Strings.CpuPl1);
						ResetPowerWallRowVisual(_pl2, _pl2Val, _pl2Label, Strings.CpuPl2);
						ResetPowerWallRowVisual(_tcc, _tccVal, _tccLabel, Strings.TempWallValue);
						_tccChk.Checked = false;
						if (_tcc != null)
						{
							_tcc.Enabled = false;
						}
					}
					else
					{
						ApplyRange(_pl1, _pl1Val, hw.Pl1Minimum, hw.Pl1Maximum, persisted.Pl1 ?? hw.Pl1);
						ApplyRange(_pl2, _pl2Val, hw.Pl2Minimum, hw.Pl2Maximum, persisted.Pl2 ?? hw.Pl2);
						ApplyRange(_pl4, _pl4Val, hw.Pl4Minimum, hw.Pl4Maximum, persisted.Pl4 ?? hw.Pl4);
						if (persisted.TccOn.HasValue)
						{
							_tccChk.Checked = persisted.TccOn.Value;
							_tcc.Enabled = persisted.TccOn.Value & flag;
						}
						ApplyRange(_tcc, _tccVal, hw.TccMinimum, hw.TccMaximum, persisted.TccTarget ?? hw.TccTarget);
					}
				}
			// 温度墙行排障：把判据原样记一行（Intel 机型能否调以本行为准）
			Logger.WriteLineIfChanged("tcc-ui", "[tcc] intel=" + CpuVendor.IsIntel
				+ " statusSeen=" + hw.TccStatusSeen + " switch=" + hw.TccSwitch
				+ " min=" + hw.TccMinimum + " max=" + hw.TccMaximum
				+ " target=" + hw.TccTarget + " adjustable=" + hw.TccAdjustable);
				_ocChk.Checked = flag3 & flag4;
				// 宝龙达/同方 GCU 40 系：GPU 超频开关按本档保存值回显
				if ((hw.UsesBaolongda || gcuGen40) && !keepPending)
				{
					PerfModeSettings persistedOc = PerfModeStore.LoadSettings(_modeId);
					if (persistedOc.IsUntouched && (hw.UsesBaolongda || hw.WujieSmuApu))
					{
						_ocChk.Checked = false;
						if (_coreOc != null)
						{
							ApplyRange(_coreOc, _coreOcVal, _coreOc.Minimum, _coreOc.Maximum, 0, allowNegative: true);
						}
						if (_memOc != null)
						{
							ApplyRange(_memOc, _memOcVal, _memOc.Minimum, _memOc.Maximum, 0, allowNegative: true);
						}
					}
					else if (persistedOc.GpuOverclockOn.HasValue)
					{
						_ocChk.Checked = persistedOc.GpuOverclockOn.Value;
					}
				}
				_ocChk.Enabled = flag3 && hw.Capabilities.OverclockSettings;
				if (_gpuOcAdminRow != null)
				{
					_gpuOcAdminRow.Visible = supportsGpuOverclock & gpuOverclockRequiresElevation;
				}
				SyncGpuOverclockDependents();
				_fanBoostRow.Visible = Program.UiAuditMode || hw.SupportsFanBoost;
				_refreshRow.Visible = Program.UiAuditMode || hw.SupportsDisplayRefresh;
				if (_refreshRow.Visible && !keepPending)
				{
					// 2026-10-09：刷新率下拉的重建也走「有未应用改动就不动它」，否则用户刚选的档会被回读值顶掉。
					PerfModeDefinition perfModeDefinition = FindMode(_modeId);
					if ((object)perfModeDefinition != null)
					{
						RebuildRefreshItems(perfModeDefinition.Settings.RefreshHz);
					}
				}
				if (keepPending)
				{
					// 把「未应用的改动」原样写回控件（只写值，不 Queue、不下发、不回滚）。
					RestorePendingControlValues();
				}
				ResizeToContent();
			}
			finally
			{
				_syncing = false;
			}
			SetOptionsEnabled();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Performance mode editor refresh failed: " + ex.Message);
		}
	}
}

/// <summary>
/// 2026-10-10 第二轮：滑块行里「数字框 + − +」那一簇的内层容器与两颗符按钮。
/// 只用来在 <c>RewidthSliderRows()</c> 里钉住"数字框填满簇内第 0 列 ⇒ 右边缘紧贴 −"，
/// 以及"− / + 两列不低于 D(24) 防裁下限"。⛔ 与本页其它行无关。
/// </summary>
internal sealed record SliderChipRow(TableLayoutPanel Cluster, Control DecChip, Control IncChip);

