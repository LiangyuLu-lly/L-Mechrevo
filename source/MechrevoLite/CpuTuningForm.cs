// 本窗口的 CO / 固定频率 / FMax 通路走 RuntimeCpuTuning -> RyzenSmuService 的派生命令表；
// 来源与许可见 source\PawnIO\RyzenSmuService.cs 头部、THIRD-PARTY-NOTICES.md 与 ATTRIBUTION-CO-ADDENDUM.md。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;
using MechrevoLite.UI;
using PawnIO;

namespace MechrevoLite;

// 2026-10-05 step7A: ported from upstream 0.290.6 (清单 A-28 / A-32).
// Debug addition: when LMECHREVO_FORCE_CPU_TUNING is on, the window title carries a visible
// "forced display" mark plus a red notice line. Nothing else changes: button/option enablement
// still comes exclusively from RuntimeCpuTuning.Probe() / CpuTuningOption.CanWrite.
public sealed class CpuTuningForm : RForm
{
	// ==========================================================================================
	// 2026-10-10 flat-rows（用户原话：「那AMD限制频率是FMax 英特尔是ratio 那是不是完全可以放一页里面 不做下拉」
	//   之后又追问「为什么下拉还在」）：**选项下拉已删除**，每个选项**独占一行**、一页全见、各自可编辑。
	//
	//   每个"通用数值选项"一行：`<选项标签 (单位)>  <数值框>  <该行「应用」>  <该行回读>`
	//     * `_optionRows`    = 行宿主（放在原下拉的位置）；
	//     * `_optionRowList` = 这些行的引用（UpdateSelection 逐行按**该行自己的能力**置灰 + 刷回读）；
	//     * 行是**按探针给的选项集合**建的（数据驱动）：Intel 探针只给 intel-*、AMD 探针只给 amd-*
	//       ⇒ "每行只在自己的平台出现"由**数据本身**保证（不再需要一个"当前选中项"的概念）。
	//
	//   被"专门块"接管的选项**不**在这里建行（各有自己的行/按钮，见下）：
	//     * `amd-co`     -> 负压表块（`_perCoreTitle/_perCoreGrid/_perCoreInfo/_coButtons`）；
	//     * `amd-clock`  -> 频率块第 1 行（`_clockRow`，固定全核频率）；
	//     * `intel-ratio`-> 频率块第 2 行（`_ratioRow`，核心最大超频倍频）。
	// ==========================================================================================
	private readonly FlowLayoutPanel _optionRows;

	private readonly List<OptionRow> _optionRowList = new List<OptionRow>();

	// 一行的四件套（可变字段：**同一个 id 第二次探到只刷新能力/范围**，⛔ 不重建控件、⛔ 不覆盖用户手改的数值）。
	private sealed class OptionRow
	{
		internal CpuTuningOption Setting;

		internal FlowLayoutPanel Row;

		internal RNumericUpDown Input;

		internal RButton Apply;

		internal Label Readback;
	}

	private readonly RNumericUpDown _value = new RNumericUpDown
	{
		Name = "cpuTuneValue"
	};

	// 2026-10-09 cpupage-uifix（用户要求④）：**每块自带按钮**。
	//   * `_coButtons`  = 负压表自己的「应用」「重置」（紧挨着表，页面底部不再混着它们）；
	//   * `_clockApply` = 固定全核频率那一块自己的「应用」（既有 amd-clock/SetOcClock 通路）；
	//   * FMax 那一行有自己的输入框 + 自己的「应用」（`_fmax` / `_fmaxApply`，见下方字段区）；
	//     2026-10-10 fmax 起已接上 PBO 邮箱通路，按**能力分四态**（可写 / 只读 / 不支持 / 可写不可回读，
	//     见 RyzenSmuService.ClassifyFMax 与 UpdateSelection）—— ⛔ 不再"永远置灰"；
	//     ⚠️ 改前那一版是构造函数里的局部变量、没有 PBO 通路，那句说法已作废。
	private readonly FlowLayoutPanel _coButtons;

	// 2026-10-10 percorenolog（用户原话：「全核负压框删掉」）：**「全核降压值 (CO)」这一整行已删除**
	// （原 `_coAllRow` + `_coAll` 数值框 + 它的建行/显隐/取值/写入分支）。
	//   * CO 的**唯一**输入口从此是负压表（每核 Curve Optimizer 那一格一格的输入框）；
	//   * 触发条件是"只要支持就开"：凡 SMU 参考里有每核写命令的代号，每核表都可编辑
	//     （见 RyzenSmuService.CurvePerCoreCommand / RuntimeCpuTuning.Probe 的 PerCoreWrite）；
	//   * 历史配置键 `cpu_co_all` 仍然被读取（「读取」在没有每核写的代号上会用它走一次既有
	//     SetCoAll），但**页面上不再有任何控件写它** —— ⛔ 不留空壳界面。

	private readonly FlowLayoutPanel _clockRow;

	private readonly RButton _clockApply;

	// 2026-10-10 flat-rows：这一行自己的**回读**（固定频率没有独立回读，照实写"不可回读（该平台没有
	// 回读命令）"那句 —— 文案来源仍是选项自己的 `ReadbackNoteKey`，⛔ 不新造键、⛔ 不伪造读数）。
	private readonly Label _clockReadback;

	// ==========================================================================================
	// 2026-10-10 flat-rows（用户原话：「AMD限制频率是FMax 英特尔是ratio 那是不是完全可以放一页里面」）：
	// **Intel 的频率行** = 核心最大超频倍频（x），放在 AMD 的 FMax 行**同一个位置**（`_fmaxRow` 之前）
	// —— 两个平台各自只亮一条，视觉上就是"同一个频率行，按平台换内容"。
	//   * 构造与 `_clockRow` 同族：标签 + 数值框 + 行内「应用」+ 行内回读；
	//   * 驱动数据 = 探针给出的 `intel-ratio`（`_ratioOption`）；**混合架构**（P 核 + E 核）上探针
	//     `IsHybrid()==true` 时**不产出**这条选项 ⇒ 本行 `Visible=false`、整行收成 0 高（⛔ 不留空行）；
	//   * ⛔ 不新增任何写入通路：走既有 `RuntimeCpuTuning.Apply("intel-ratio", 值)`。
	// ==========================================================================================
	private readonly FlowLayoutPanel _ratioRow;

	private readonly RNumericUpDown _ratio;

	private readonly RButton _ratioApply;

	private readonly Label _ratioReadback;

	// 最近一次探针给出的 intel-ratio（null = 本机/本架构没有这条选项 ⇒ 频率行整行不出现）。
	private CpuTuningOption? _ratioOption;

	// Intel 倍频框只"首次探到"填一次，之后不再被探针刷掉用户手改的值（与 `_clockFilled` 同一个用意）。
	private bool _ratioFilled;

	// 2026-10-10 flat-rows：频率块**那一行**的说明 —— 说清「FMax = PBO 加速上限（MHz）」与
	// 「固定全核频率 = 把所有核心锁在这个频率」**不是同一件事**（用户原话：「FMax和全核频率也是重复的」）。
	// ⚠️ 只在 AMD 且固定频率行真的出现时显示（Intel 没有这个歧义，不占版面）。
	private readonly Label _freqNote;

	// ==========================================================================================
	// 2026-10-10 fmax: FMax（PBO 最大频率）那一行自己的控件。
	// 改前输入框与「应用」是构造函数里的**局部变量**，所以永远置灰、没有写入通路；现在提升为字段，
	// 好让 UpdateSelection 按能力（RyzenSmuService.ClassifyFMax）门控：
	//   * ReadWrite        -> 数值框 + 「应用」可用（写后回读校验 + 报固件状态码）；
	//   * ReadOnly         -> 两个都禁用，但**照常显示回读值** + 一行"本机只支持读"；
	//   * WriteNoReadback  -> 可写（ShimadaPeak），但明说"无法回读校验"；
	//   * None             -> 整行禁用 + 明说"本机不支持 FMax"。
	// 2026-10-10 fmax-persist（用户决定：「增加一个保存和读取按钮 可以保存当前的负压值和Fmax
	//   点读取就读回来 点重置都是恢复默认」+「开机自动应用可以自动应用」）：
	//   ⚠️ 改前这里写的是"FMax **不持久化、不开机自动应用**，也**不参与「重置」**" —— 用户已经
	//      明确决定放开这三条，所以：FMax 走 cpu_fmax_saved / cpu_fmax_baseline（与 CO 同一份
	//      AppConfig、同一套按机型作用域），能写时才参与「保存/读取/重置/开机自动应用」。
	//      `_hasChanges` / `HasChanges` 的语义**仍未改变**（FMax 不在 `_changed` 里，理由见
	//      RuntimeCpuTuning.ApplyFMax 的注释）。
	// ==========================================================================================
	private readonly RNumericUpDown _fmax;

	private readonly RButton _fmaxApply;

	private readonly Label _fmaxNote;

	// 2026-10-10 intel-fmax：页面顶部那行说明（改前是构造函数里的局部 `scopeNotice`）。
	// ⚠️ 它是 `_paragraphs` 成员（宽度随窗口自适应），字段化只为了按平台换文案。
	private readonly Label _scopeNotice;

	// 2026-10-10 intel-fmax：FMax 那一行**整个** FlowLayoutPanel 的引用（改前是构造函数里的局部变量）。
	// ⚠️ 与 `_clockRow` 同一套做法（行也得是字段，才好在 UpdateSelection 里按平台显隐）。
	// ⛔ 字段化只为了"整行显隐"；行内的标签/数值框/「应用」三件套的构造、取值、事件一字未动。
	private readonly FlowLayoutPanel _fmaxRow;

	// 2026-10-10 fmax-persist：整页设置的「保存 / 读取」两个按钮。
	// 保存 = 把"当前的 CO 值 + 当前 FMax"写进既有配置（cpu_co_* / cpu_fmax_saved），**只写配置、不碰 SMU**；
	// 读取 = 把它们读回来并应用（CO 走既有 Apply / ApplyPerCore，FMax 走既有 ApplyFMax：写 + 回读校验）。
	// ⚠️ 门控与本页其它按钮同源：忙碌 / 测试夹具 / 审计模式下置灰；本机两侧都不可写也置灰 ——
	//    这样"只读/不支持 FMax 的机器"连按钮都点不到，更不会打开 PawnIO（见 UpdateSelection）。
	private readonly RButton _snapshotSave;

	private readonly RButton _snapshotLoad;

	// 2026-10-10 intel-fmax：上面两个按钮所在的那一行（同上，字段化只为整行按平台显隐）。
	private readonly FlowLayoutPanel _snapshotRow;

	// 最近一次探针的 FMax 结论（能力 + **只读**回读）。null = 还没探到 / 本次探针没走到 SMU。
	private CpuFMaxProbe? _fmaxProbe;

	// 最近一次探针的状态键。FMax 那一行在"连 SMU 都没打开"（缺驱动 / 非 AMD / 探针失败）时
	// 用**真正的原因**说明，而不是硬说"本机不支持 FMax"。
	private string _probeStatusKey = "CpuTuneProbePrompt";

	private readonly Label _current = new Label
	{
		Name = "cpuTuneCurrent",
		AutoSize = true
	};

	private readonly Label _status = new Label
	{
		Name = "cpuTuneStatus",
		AutoSize = true
	};

	// 2026-10-09 cpupage-uifix（用户要求②）：模式 → 可见性映射要用的两个引用（改前一个是匿名 Paragraph、
	// 一个是构造函数里的局部变量，没法按模式显隐）。
	private readonly Label _perCoreTitle;

	private readonly Label _autoApplyWarning;

	// 2026-10-09 cpupage-uifix（用户要求③）：底部那一大段技术细节收进这个可折叠区，页面上只留一行摘要。
	private readonly RCollapseGroup _detailsGroup;

	private readonly Label _detail = new Label
	{
		Name = "cpuTuneDetail",
		AutoSize = true
	};

	// 2026-10-09 cpupage-uifix：探针结果里两条关键选项（按 Id 存下来，各块各用各的）。
	private CpuTuningOption? _coOption;

	private CpuTuningOption? _clockOption;

	// 固定全核频率的输入框只"首次探针后"填一次，之后不再被探针刷掉用户手改的值。
	private bool _clockFilled;

	private readonly RButton _probeButton;

	private readonly RButton _applyButton;

	// 2026-10-05 uifix（用户要求①）：这是**唯一**的清零按钮 —— 旧的「恢复」改名「重置」，
	// 行为 = 旧「重置（每核写 0）」+ 旧「恢复」的合并（逐核写 0 + 全核 SetCoAll(0) + 退出固定频率）。
	private readonly RButton _resetButton;

	private readonly RButton _installButton;

	// 2026-10-05 uifix（用户要求③）：把警告里"其余细节"移到 tooltip，版面上只留 1~2 行。
	private readonly ToolTip _tips = new ToolTip();

	// 2026-10-05 uifix（用户要求④）：保底机制的状态行。
	// 只有"上次非正常关机、本次没有自动应用"时才显示一行，平时完全不占版面。
	private readonly Label _guardStatus = new Label
	{
		Name = "cpuTuneGuardStatus",
		AutoSize = true,
		ForeColor = UiVisualStyle.Danger,
		Visible = false
	};

	// ==========================================================================================
	// 2026-10-05 percore-product: 每核 CO 面板。
	//   用户原话：「可以写进产品了 点重置是每个核心写0 然后允许开机自动应用和保存超频设置」
	//   组成：N 个每核输入框（N = 实读到的每核值个数，8 核机 = 8）+ 一行"进入前实读"说明
	//         + 逐核应用 / 重置（每核写 0）/ 保存设置 三个按钮 + 开机自动应用开关（默认关）。
	//
	//   ⚠️ 门控：这一整组只在"选中的是 amd-co AND CanWrite AND PerCoreWrite"时才可编辑 ——
	//      PerCoreWrite 由探针给出（只有桌面 Vermeer 为真），本机 R7 APU 恒为假 -> 整组置灰。
	// ==========================================================================================
	private readonly TableLayoutPanel _perCoreGrid;

	private readonly List<RNumericUpDown> _perCoreInputs = new List<RNumericUpDown>();

	private readonly Label _perCoreInfo = new Label
	{
		Name = "cpuTunePerCoreInfo",
		AutoSize = true
	};

	// 2026-10-05 uifix（用户要求①）：每核区不再有自己的按钮排 —— 动作只剩底部那一排。
	// 「逐核应用」并进「应用」，「保存设置」并进「应用」（应用成功即保存），
	// 「重置（每核写 0）」并进底部改名后的「重置」。
	private readonly RCheckBox _autoApply;

	private readonly List<Label> _paragraphs = new List<Label>();

	private bool _busy;

	private bool _hasChanges;

	private readonly CpuTuningProbe? _fixture;

	// 2026-10-05 percore-product: 外层滚动面板（内容变长后，WinForms 打开时会自动滚到刚加进去的
	// 控件那一段 —— 用户第一眼会看不到"回读/每核输入框"。这里显式把初始位置拉回顶部）。
	private Panel? _scrollHost;

	// 进入窗口时（首次探针后）实读到的每核值。只存在内存里，给"进入前是多少"的提示与
	// 「重置」的二次确认文案用。null = 当时读不到（该平台没有回读命令 / 读取失败）。
	private List<int>? _perCoreBaseline;

	// 每核输入框只"首次进入时"按实读值填一次，之后不再被探针刷新覆盖掉用户的手改。
	private bool _perCoreFilled;

	// 程序化设置开机自动应用开关时抑制 CheckedChanged 里的确认弹窗。
	private bool _syncingAutoApply;

	private int _perCoreCount;

	// 2026-10-05 step7A-fix2: 探针报回来的条目里有一条或多条 CanWrite:false（如 Zen4 APU R7 上的
	// amd-co 被 SKU 层级锁掉）。缓存下来，选中它时在回读行给出"不可用"的原因。
	private readonly List<CpuTuningOption> _unavailable = new List<CpuTuningOption>();

	// 最近一次探针的 Detail 原文（用于从里面摘出每个不可用选项的原因）。
	private string _probeDetail = "";

	internal static string Localized(string key)
	{
		return Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;
	}

	// ==========================================================================================
	// 2026-10-10 intel-fmax（用户原话：「新bug 英特尔机型显示了Fmax」）：本窗口的平台判据。
	//
	// ⛔ 不新造探测：**逐字复用仓库既有那一条** —— `CpuInfo.IsAMD`（PawnIO\CpuInfo.cs，CPUID 厂商串
	//    一次性算出来的只读静态字段）。它正是本窗口数据源 `RuntimeCpuTuning.Probe()` 用来决定
	//    "走 AMD/SMU 分支还是 Intel 分支"的那一个判据（RuntimeCpuTuning.cs:166 `if (CpuInfo.IsAMD)`），
	//    所以"门控用的判据"与"数据源用的判据"**同一个**，不会出现两边打架。
	//
	// 为什么必须按平台而不是按能力：Intel 分支下探针**永远**给 FMaxCapability.None（_fmax 恒为 null，
	// 见 RuntimeCpuTuning.Probe 的非 AMD 返回值），而"能力 = None"同时也会出现在"AMD 机器但没装
	// PawnIO 驱动"上 —— 只按能力判会把"AMD 缺驱动"误伤成"不显示"。故平台判据必须前置。
	//
	// ⚠️ 本方法**只决定"显不显示"**：FMax 的四态（ReadWrite / ReadOnly / WriteNoReadback / None）、
	//    CO / 固定频率的写入路径与所有既有门控一字未改（AMD 上走的分支与改前逐字相同）。
	// ==========================================================================================
	private static bool AmdTuningPlatform()
	{
		return CpuInfo.IsAMD;
	}

	// ==========================================================================================
	// 2026-10-10 intel（⑦B，用户原话「intel功能合并」）：平台分流。
	//
	// 改前是"是 AMD / 否"两分（`AmdTuningPlatform()`），非 AMD 一律按"什么都没有"处理。合并
	// Intel 通路之后，本窗口要按**三种**平台分流：
	//   * AMD   -> 显示 AMD 那一套（CO 表 / 每核 / 固定全核频率 / FMax / 保存读取 / 开机自动应用）；
	//   * Intel -> 显示 Intel 那一套（**选项下拉 + 数值框 + 回读行 + 应用/重置**，选项来自
	//              探针给出的 intel-core / intel-cache / intel-ratio），AMD 专属行全部收起；
	//   * 其它  -> 只有"检测能力"那一行可用（探针会回 CpuTuneUnsupported），AMD 专属行同样收起。
	//
	// ⚠️ 判据与数据源**同一个**：`RuntimeCpuTuning.Probe()` 就是按 `CpuInfo.IsAMD` 分流的
	//    （AMD 分支 / `IsIntel()` 分支 / 未知厂商），这里只用**同一个 CPUID 事实**决定显隐，
	//    ⛔ 不新造探测、⛔ 不看能力键（缺驱动时 Intel 也会回 CpuTuneProbeFailed，按能力判会误伤）。
	// ⚠️ 本方法只回答"平台上该显示哪一套"；真正的可写性永远来自 CpuTuningOption.CanWrite。
	// ==========================================================================================
	private static bool IntelTuningPlatform()
	{
		// 与 RuntimeCpuTuning.IsIntel() 同一判据（CPUID 厂商串 GenuineIntel），逐字复用不另写。
		return !CpuInfo.IsAMD && RuntimeCpuTuning.IsIntel();
	}

	// 2026-10-09 cpupage-uifix：逻辑像素 → 设备像素（与 CustomModeForm / FanCurveForm / RgbForm 同一个入口）。
	// 本页新增/移动的宽度一律走这里，⛔ 不硬编码设备像素 —— 100/125/150/175% 下框与文字都不该被挤变形。
	private int D(int value)
	{
		return ResponsiveLayout.LogicalToDevice(this, value);
	}

	// 2026-10-05 percore-product: 把外层滚动面板拉回顶部（只在"打开窗口/首次填充"时调用，
	// 之后用户自己滚动不会被抢）。
	// 2026-10-05 uifix: 布局与"活动控件滚进视野"在这之后还会把面板再滚下去一次（实测取证里
	// 第一屏停在每核输入框那一段），所以在消息循环的下一轮补拉一次 —— 同一个入口，只补一次，
	// 不会在用户开始滚动之后抢滚动条。
	private void ScrollToTop()
	{
		if (_scrollHost == null || !_scrollHost.IsHandleCreated)
		{
			return;
		}
		_scrollHost.AutoScrollPosition = new Point(0, 0);
		try
		{
			_scrollHost.BeginInvoke((Action)delegate
			{
				Panel? host = _scrollHost;
				if (host != null && host.IsHandleCreated && !host.IsDisposed)
				{
					host.AutoScrollPosition = new Point(0, 0);
				}
			});
		}
		catch (Exception ex)
		{
			Logger.WriteLine("CpuTuningForm deferred scroll-to-top failed: " + ex.Message);
		}
	}

	public CpuTuningForm()
		: this(null)
	{
	}

	internal CpuTuningForm(CpuTuningProbe? fixture)
	{
		_fixture = fixture;
		_hasChanges = fixture == null && !Program.UiAuditMode && RuntimeCpuTuning.Instance.HasChanges;
		Text = Localized("CpuTuneTitle") + (RuntimeCpuTuning.ForceEntry ? Localized("CpuTuneForcedMark") : "");
		// 2026-10-05 percore-product: 加了每核区以后内容更长，窗口给高一点（仍在 720 高视口的
		// 可用区内：ResponsiveLayout 会按视口再收敛），其余仍是 AutoScroll 兜底。
		ClientSize = new Size(600, 560);
		MinimumSize = new Size(420, 260);
		StartPosition = FormStartPosition.CenterParent;
		MaximizeBox = false;
		// 2026-10-13（用户原话「都改掉不要最小化」）：只留【关闭】—— 藏掉最小化按钮。
		// 本窗由 CustomModeForm 以 ShowDialog(this) 模态弹出（CustomModeForm.cs:937-938）⇒ 它一旦最小化，
		// 宿主进禁用态、模态循环也退不出 ⇒ 这是最该拔掉最小化按钮的一个。
		// MinimizeBox=false 会连 WS_MINIMIZEBOX 一起去掉（WinForms 由该属性决定该样式位）。
		// ⚠️ FormBorderStyle 本来就没显式设过（沿用 Form 默认 Sizable）：用户只要藏按钮，⛔ 不动它。
		MinimizeBox = false;
		InitTheme(setDPI: true);
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true
		};
		TableLayoutPanel root = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			ColumnCount = 1,
			Padding = new Padding(14)
		};
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		panel.Controls.Add(root);
		Controls.Add(panel);
		_scrollHost = panel;
		Add(Paragraph(fixture?.Cpu ?? CpuInfo.Name));
		if (RuntimeCpuTuning.ForceEntry)
		{
			Add(Warning(Localized("CpuTuneForcedNotice")));
		}
		Label scopeNotice = Paragraph(Localized("CpuTuneScope"));
		scopeNotice.Name = "cpuTuneScope";
		Add(scopeNotice);
		// 2026-10-10 intel-fmax：整行提到字段上（`UpdateSelection` 按平台换文案），⛔ 构造内容未变。
		_scopeNotice = scopeNotice;
		// 2026-10-10 flat-rows（用户原话：「那是不是完全可以放一页里面 不做下拉」/「为什么下拉还在」）：
		// **选项下拉已删除**，这里换成"每个选项一行"的宿主 —— 行由 `ApplyProbe` 按探针结果建
		// （见 `SyncOptionRows`），每行自带标签 / 数值框 / 「应用」/ 回读。
		// ⚠️ 位置仍是原下拉那一行：用户第一眼看到的就是**所有可写项**，不需要"先选一项才能编辑"。
		_optionRows = new FlowLayoutPanel
		{
			Name = "cpuTuneOptionRows",
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Fill,
			// TopDown + 不换行 = "每个选项一行"；行自己的宽度由本面板给（于是行内的回读能按真实宽度换行）。
			// ⚠️ 改前这里是嵌套 AutoSize TableLayoutPanel：本机实测它会把**第二行**按宽度 0 量一次并缓存
			//    （行高 212px、真实内容 74px）⇒ 整页多出一条 130px 的空白带。换成 FlowLayoutPanel 后
			//    行高由"内容 + 父给宽度"直接算，与同页其它整行控件（`_coButtons` / `_ratioRow`）同一套机制。
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false
		};
		Add(_optionRows);
		_paragraphs.Add(_current);
		Add(_current);
		// ==========================================================================================
		// 负压表（每核 CO 表）+ 它自己的「应用」「重置」。
		// 2026-10-09 cpupage-uifix（用户要求②）：显隐是**两条独立规则**，唯一落点在 UpdateSelection：
		//   * 规则 A（按模式隐藏）：模式 = 固定全核频率 -> 整块隐藏（表 / 应用 / 自动应用 / 警告）；
		//   * 规则 B（按能力置灰）：硬件不支持（CanWrite / PerCoreWrite 为假）-> 整块**保留**、照旧置灰，
		//     并照旧给出"不可用 + 原因"（本机 R7 就是这条）。
		// ==========================================================================================
		_perCoreTitle = Paragraph(Localized("CpuTunePerCoreTitle"));
		_perCoreTitle.Name = "cpuTunePerCoreTitle";
		Add(_perCoreTitle);
		_perCoreGrid = new TableLayoutPanel
		{
			Name = "cpuTunePerCoreGrid",
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Fill,
			ColumnCount = 4,
			RowCount = 0
		};
		for (int column = 0; column < 4; column++)
		{
			// 奇数列（0/2）是"核 N"标签，偶数列是数值框 —— 都按内容自动定宽。
			_perCoreGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		}
		Add(_perCoreGrid);
		Add(_perCoreInfo);
		// 2026-10-10 percorenolog（用户原话：「全核负压框删掉」）：这里原本是
		// `_coAllRow`（"全核降压值 (CO)"标签 + `_coAll` 数值框）—— **整块已删除**，
		// 连同它的建行 / 显隐（`allCoreOnly`）/ 取值 / 写入分支。
		// CO 的唯一输入口 = 上面那张每核表；没有每核写命令的代号由 `_perCoreInfo`
		// 用 `CpuTunePerCoreCodenameOnly` 如实说明（⛔ 不再留一个空的"全核"框）。
		// 2026-10-09 cpupage-uifix（用户要求④）：负压表的「应用」「重置」**紧挨着负压表**（不再和页面底部的
		// 「检测能力」混成一排）。⚠️「重置」不跟着模式隐藏 —— 它同时是"退出固定频率"的**唯一**入口
		//（ResetAll 里的 DisableOc），锁频模式下藏掉就没人能退出锁频了。
		_coButtons = new FlowLayoutPanel
		{
			Name = "cpuTuneCoButtons",
			AutoSize = true,
			Dock = DockStyle.Fill,
			WrapContents = true
		};
		_applyButton = Button(_coButtons, "CpuTuneApply", "cpuTuneApply");
		// 2026-10-05 uifix：Name 仍是 cpuTuneReset（验收按 Name 取证），文案变成「重置」（无括号）。
		_resetButton = Button(_coButtons, "CpuTuneResetAllCores", "cpuTuneReset");
		Add(_coButtons);
		_autoApply = new RCheckBox
		{
			Name = "cpuTuneAutoApply",
			AutoSize = true,
			Text = Localized("CpuTuneAutoApply")
		};
		Add(_autoApply);
		// 2026-10-05 uifix（用户要求③）：警告压到 1~2 行，说清三件事（每次开机真的写入 / 只对本次
		// 开机有效 / 不确定别开），其余细节全部搬到 tooltip。
		// 2026-10-09 cpupage-uifix：文案**保留不动**（写入后果是安全提示，精简不动它），只把控件留引用，
		// 好让固定频率模式下跟着每核区一起隐藏。
		_autoApplyWarning = Warning(Localized("CpuTuneAutoApplyWarning"));
		_autoApplyWarning.Name = "cpuTuneAutoApplyWarning";
		_tips.SetToolTip(_autoApplyWarning, Localized("CpuTuneAutoApplyWarningTip"));
		_tips.SetToolTip(_autoApply, Localized("CpuTuneAutoApplyWarningTip"));
		Add(_autoApplyWarning);
		// ==========================================================================================
		// 固定全核频率（实验）(MHz) —— 一块**自己的**输入 + 自己的「应用」+ 自己的回读。
		// 2026-10-10 flat-rows（用户原话：「FMax和全核频率也是重复的」）：它**不再跟"模式"走**
		// （下拉已删除）—— 显隐只由**能力**决定：探针给出 `amd-clock` 且 CanWrite 才出现（见 UpdateSelection），
		// 与 FMax 并列在"频率块"里，并在下面用一行说明讲清两者不是同一件事。
		// ⚠️ 走的是既有通路 RuntimeCpuTuning.Apply("amd-clock") -> RyzenSmuService.SetOcClock，
		//    没有新增任何 SMU 命令/参数（写入语义一字未改）。
		// ==========================================================================================
		_clockRow = new FlowLayoutPanel
		{
			Name = "cpuTuneClockRow",
			AutoSize = true,
			Dock = DockStyle.Fill,
			WrapContents = true,
			Visible = false
		};
		Label clockLabel = new Label
		{
			Name = "cpuTuneClockLabel",
			Text = Localized("CpuTuneClockLabel"),
			AutoSize = true,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 6, 8, 0)
		};
		_value.Width = D(110);
		_value.Anchor = AnchorStyles.Left;
		_value.Margin = new Padding(0, 2, 10, 0);
		_value.UsePlainTextBox();
		// 2026-10-10：同 `_coAll`（见上方注释）—— 只描编辑区、不描右侧空条。
		_value.OutlineMode = InputOutlineMode.EditRectOnly;
		_clockRow.Controls.Add(clockLabel);
		_clockRow.Controls.Add(_value);
		_clockApply = Button(_clockRow, "CpuTuneApply", "cpuTuneClockApply");
		// 2026-10-10 flat-rows：这一行自己的回读（固定频率**没有独立回读**，照实写"不可回读"）。
		_clockReadback = new Label
		{
			Name = "cpuTuneClockReadback",
			AutoSize = true,
			ForeColor = UiVisualStyle.Muted,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 6, 0, 0)
		};
		_clockRow.Controls.Add(_clockReadback);
		Add(_clockRow);
		// ==========================================================================================
		// 2026-10-10 flat-rows：**Intel 的频率行**（核心最大超频倍频 x）—— 放在 FMax 行**同一个位置**
		// （紧挨着它上面），两个平台各自只亮一条 ⇒ 视觉上就是"同一个频率行，按平台换内容"。
		//   * 构造与 `_clockRow` 同族：标签 + 数值框 + 行内「应用」+ 行内回读；
		//   * 驱动数据 = 探针给出的 `intel-ratio`（`_ratioOption`）：**混合架构上探针不给这条**
		//     （IsHybrid()==true，见 RuntimeCpuTuning.Probe 的 `!IsHybrid()` 门控）⇒ 本行 Visible=false、
		//     整行收成 0 高（⛔ 不留空行，用户 255HX 那台就属于这一种）；
		//   * ⛔ 只接既有通路口 `RuntimeCpuTuning.Apply("intel-ratio", 值)`，⛔ 不新增任何 MSR 命令/参数。
		// ==========================================================================================
		_ratioRow = new FlowLayoutPanel
		{
			Name = "cpuTuneRatioRow",
			AutoSize = true,
			Dock = DockStyle.Fill,
			WrapContents = true,
			Visible = false
		};
		Label ratioLabel = new Label
		{
			Name = "cpuTuneRatioLabel",
			Text = Localized("CpuTuneRatio") + " (x)",
			AutoSize = true,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 6, 8, 0)
		};
		_ratio = new RNumericUpDown
		{
			Name = "cpuTuneRatio",
			Width = D(110),
			Minimum = 8m,
			Maximum = 64m,
			DecimalPlaces = 0,
			Value = 8m,
			Enabled = false,
			Margin = new Padding(0, 2, 10, 0)
		};
		_ratio.UsePlainTextBox();
		_ratio.OutlineMode = InputOutlineMode.EditRectOnly;
		_ratioRow.Controls.Add(ratioLabel);
		_ratioRow.Controls.Add(_ratio);
		_ratioApply = Button(_ratioRow, "CpuTuneApply", "cpuTuneRatioApply");
		_ratioApply.Enabled = false;
		// ⚠️ 用丢弃参数 `(_, _)` 而不是 `(object? _, EventArgs _)`：本工程未开 <Nullable>，写 `?`
		//    只会多一条 CS8632（本文件这类警告已经很多，本轮不新增）。
		_ratioApply.Click += async (_, _) =>
		{
			if (_ratioOption != null)
			{
				decimal ratioValue = _ratio.Value;
				string ratioId = _ratioOption.Id;
				await RunAsync(() => RuntimeCpuTuning.Instance.Apply(ratioId, ratioValue), restore: false);
			}
		};
		_ratioReadback = new Label
		{
			Name = "cpuTuneRatioReadback",
			AutoSize = true,
			ForeColor = UiVisualStyle.Muted,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 6, 0, 0)
		};
		_ratioRow.Controls.Add(_ratioReadback);
		Add(_ratioRow);
		// ==========================================================================================
		// FMax（PBO 最大频率）—— 用户要求新增的一行 + 一个**独立的**「应用」按钮。
		// 2026-10-10 fmax：**已接上 PBO 邮箱命令**（RyzenSmuService.GetBoostLimitFrequency /
		//    SetBoostLimitFrequencyAllCores，走 RSMU/PSMU 邮箱 = LM 既有通道），并按代号分类能力
		//    （RyzenSmuService.ClassifyFMax）：
		//      ReadWrite        -> 数值框 + 「应用」（写后回读校验 + 报固件状态码）；
		//      ReadOnly         -> 两个都禁用，但**照常显示回读值**（用户决定"不支持平台只读取"）；
		//      WriteNoReadback  -> 可写，但明说无法回读校验（ShimadaPeak）；
		//      None             -> 整行禁用 + 明说本机不支持。
		//    ⛔ 不接"固定全核频率"通路（FMax = PBO 上限，与锁频是两回事）。
		//    * 持久化 / 自动应用：**后来按用户要求放开了** —— 「保存」把当前 CO + FMax 写进配置
		//      （cpu_fmax_saved），「读取」读回来并应用，「重置」写回 cpu_fmax_baseline，
		//      开机还会按机型自动应用（见下方 fmax-persist 段 + RuntimeCpuTuning.ApplyFMax）。
		//      ⚠️ 更正说明：这里早先写的是"⛔ 不自动应用、不持久化（用户明确要求）"——那与
		//      2026-10-10 fmax-persist 之后用户的要求正好相反，故按实际代码改写，勿再照旧说法理解。
		// ==========================================================================================
		FlowLayoutPanel fmaxRow = new FlowLayoutPanel
		{
			Name = "cpuTuneFMaxRow",
			AutoSize = true,
			Dock = DockStyle.Fill,
			WrapContents = true
		};
		// 2026-10-10 intel-fmax：整行提到字段上（`UpdateSelection` 按平台显隐），⛔ 构造内容未变。
		_fmaxRow = fmaxRow;
		Label fmaxLabel = new Label
		{
			Name = "cpuTuneFMaxLabel",
			Text = Localized("CpuTuneFMaxLabel"),
			AutoSize = true,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 6, 8, 0)
		};
		_fmax = new RNumericUpDown
		{
			Name = "cpuTuneFMax",
			Width = D(110),
			Minimum = 0m,
			Maximum = 10000m,
			DecimalPlaces = 0,
			Value = 0m,
			Enabled = false,
			Margin = new Padding(0, 2, 10, 0)
		};
		// 2026-10-09：只换外观。2026-10-10 fmax：`Enabled` 现在由 UpdateSelection 按**能力**决定
		// （ReadWrite 才可编辑），不再是"永远 false"；`UsePlainTextBox` 的外观保持不变。
		_fmax.UsePlainTextBox();
		// 2026-10-10：只描编辑区、不描右侧空条（与 `_value` / 每核框同一套外观约定）。
		_fmax.OutlineMode = InputOutlineMode.EditRectOnly;
		fmaxRow.Controls.Add(fmaxLabel);
		fmaxRow.Controls.Add(_fmax);
		_fmaxApply = Button(fmaxRow, "CpuTuneApply", "cpuTuneFMaxApply");
		_fmaxApply.Enabled = false;
		// 2026-10-10 fmax：「应用」= 写一条 FMax 邮箱命令 + **回读校验**
		// （RuntimeCpuTuning.ApplyFMax）。未通过能力门控时 ApplyFMaxAsync 直接返回，
		// 一条 SMU 命令都不发；结果与固件状态码一律落到 `_status` 行，绝不静默失败。
		_fmaxApply.Click += async (object? _, EventArgs _) =>
		{
			await ApplyFMaxAsync();
		};
		Add(fmaxRow);
		_fmaxNote = Paragraph(Localized("CpuTuneFMaxUnsupported"));
		_fmaxNote.Name = "cpuTuneFMaxNote";
		Add(_fmaxNote);
		// 2026-10-10 flat-rows：频率块的**去重说明**（用户原话：「FMax和全核频率也是重复的」）。
		// ⚠️ 只在 AMD 且"固定全核频率"那一行真的出现时显示 —— 那时页面上确实同时有两条频率输入，
		//    必须说清：FMax = PBO 加速上限（MHz），固定全核频率 = 把所有核心锁在这个频率（另一条 SMU 命令）。
		//    Intel 上这两条一个都不在（倍频行是另一回事），本行 `Visible=false`、不占版面。
		_freqNote = Paragraph(Localized("CpuTuneFreqNote"));
		_freqNote.Name = "cpuTuneFreqNote";
		_freqNote.Visible = false;
		Add(_freqNote);
		// ==========================================================================================
		// 2026-10-10 fmax-persist（用户决定②）：整页设置的「保存 / 读取」。
		//   * 位置：紧挨 FMax 那一行之后 —— 它存/取的就是"CO 表 + FMax"这一整份设置；
		//   * 不新增任何 SMU 命令：保存只写配置（SaveSettings / SaveFMax），读取复用既有
		//     ApplyPerCore / Apply("amd-co") / ApplyFMax（后者自带写后回读校验与固件状态码）；
		//   * 「重置」= 恢复默认：CO 照旧（逐核 0 + 全核兜底），FMax 写回 cpu_fmax_baseline；
		//     没有记录到原值时**不猜值**，只在状态行里明说（见 AppendFMaxResetStatus）。
		// ==========================================================================================
		FlowLayoutPanel snapshotRow = new FlowLayoutPanel
		{
			Name = "cpuTuneSnapshotRow",
			AutoSize = true,
			Dock = DockStyle.Fill,
			WrapContents = true
		};
		// 2026-10-10 intel-fmax：整行提到字段上（`UpdateSelection` 按平台显隐），⛔ 构造内容未变。
		_snapshotRow = snapshotRow;
		_snapshotSave = Button(snapshotRow, "CpuTuneSnapshotSave", "cpuTuneSnapshotSave");
		_snapshotLoad = Button(snapshotRow, "CpuTuneSnapshotLoad", "cpuTuneSnapshotLoad");
		_snapshotSave.Enabled = false;
		_snapshotLoad.Enabled = false;
		// 「保存」：只写配置（绝不碰 SMU），状态行说清存了哪些值。
		_snapshotSave.Click += (object? _, EventArgs _) => SaveSnapshot();
		// 「读取」：把存下的 CO / FMax 读回来并应用（走既有写入路径，含回读与固件状态码）。
		_snapshotLoad.Click += async (object? _, EventArgs _) =>
		{
			await LoadSnapshotAsync();
		};
		Add(snapshotRow);
		// 页面级动作只剩「检测能力」（+ 缺驱动时的「安装缺失驱动」）—— 用户明确说它不属于某一块。
		FlowLayoutPanel buttons = new FlowLayoutPanel
		{
			AutoSize = true,
			Dock = DockStyle.Fill,
			WrapContents = true
		};
		_probeButton = Button(buttons, "CpuTuneProbe", "cpuTuneProbe");
		_installButton = Button(buttons, "CpuTuneInstallDriver", "cpuTuneInstallDriver");
		_installButton.Click += async (object? _, EventArgs _) =>
		{
			await InstallDriverAsync();
		};
		// 2026-10-05 uifix（用户要求④）：保底机制状态行 —— 只在"上次非正常关机"时可见。
		_paragraphs.Add(_guardStatus);
		Add(_guardStatus);
		_paragraphs.Add(_status);
		Add(_status);
		// 2026-10-09（用户反馈：FMax 那条提示文字下面空得太多）：根因不是提示自己的行距（= `Add` 的
		// 12 设备像素，与全页一致），而是**空文本的 `_status` 也占一整行** —— AutoSize 空 Label 仍有字高
		// （本机实测 20 设备像素）+ 它自己的 12 设备像素行距 ⇒ 提示与下面那行风险提示之间实测 44 设备
		// 像素（其余相邻行一律 12；`_guardStatus` 因为是 Visible=false 已经被折叠，不占位）。
		// 与 `_guardStatus` 同一套做法：没话可说就整行收起来，有字自动占位。
		// ⚠️ 只改这一行的占位：`_status` 的全部文案与写入时机（探针清空 / 应用/重置结果 / 驱动安装结果）
		// 一字未改，也没有新增/删除任何控件。
		_status.TextChanged += (_, _) => _status.Visible = _status.Text.Length > 0;
		_status.Visible = _status.Text.Length > 0;
		// 2026-10-09 cpupage-uifix（用户要求③）：风险提示**保留可见**（"从小幅调整开始…"是写入后果的安全提示，
		// 只是不再和底部那一大段技术细节挤在一起）。
		Label riskNotice = Paragraph(Localized("CpuTuneRisk"));
		riskNotice.Name = "cpuTuneRisk";
		Add(riskNotice);
		LinkLabel linkLabel = new LinkLabel
		{
			Text = Localized("CpuTuneDriverLink"),
			AutoSize = true
		};
		linkLabel.LinkClicked += (object? _, LinkLabelLinkClickedEventArgs _) =>
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "https://pawnio.eu/",
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				_status.Text = ex.Message;
				Logger.WriteLine("PawnIO download page: " + ex);
			}
		};
		Add(linkLabel);
		// 2026-10-09 cpupage-uifix（用户要求③）：底部那一大段技术细节（命令路径 / 回读能力 / 被锁原因 +
		// 逐条"不可用: 原因"）全部收进这个折叠区，页面上只留折叠区标题右侧那一行摘要。
		// 展开状态由 RCollapseGroup 自己按 configKey 记（默认折叠）。
		_detailsGroup = new RCollapseGroup(Localized("CpuTuneDetailsTitle"), UiGlyph.Kind.Info, "cpu_tune_details_open", defaultExpanded: false);
		TableLayoutPanel detailHost = new TableLayoutPanel
		{
			Name = "cpuTuneDetailHost",
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = 0
		};
		detailHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		_paragraphs.Add(_detail);
		detailHost.Controls.Add(_detail, 0, detailHost.RowCount++);
		_detailsGroup.SetContent(detailHost);
		// ==========================================================================================
		// 2026-10-10 intel-fix（用户原话：「Intel 版本的界面…底部一大块空白」/ 审计 30 条全指向它）：
		// `RCollapseGroup` 自己是 `AutoSize = GrowAndShrink` 的 `Panel`，而 `SettingsForm` 那边是由宿主
		// **显式给宽**的；本页把它直接 `Add` 进根 TableLayoutPanel（Percent 100 列）却**没有 Dock**，
		// 于是它的宽度一直留在构造时的 0 —— 行高照占（折叠时 = 页眉那一行，审计里因为强制展开 = 整块
		// 技术详情的高度），**宽度 0 ⇒ 一个字都画不出来** ⇒ 视觉上就是页面底部一条空白带（AMD/Intel
		// 一模一样，属既有缺陷，与本轮 Intel 修复无关）。
		// 修法 = 与同一页其它整行控件（`_coButtons` / `_clockRow` / `_fmaxRow` / `snapshotRow`）同一个
		// 写法：`Dock = Fill` ⇒ 列宽由 TableLayoutPanel 给，行高仍按 AutoSize 收敛。
		// ⛔ 只加一条布局属性：折叠/展开逻辑、摘要文案、_detail 的文案与 `_paragraphs` 自适应宽度全未动。
		// ==========================================================================================
		_detailsGroup.Dock = DockStyle.Fill;
		Add(_detailsGroup);
		// 页面级动作只剩「检测能力」（+ 缺驱动时的「安装缺失驱动」），放在最底部。
		Add(buttons);
		_probeButton.Click += async (object? _, EventArgs _) =>
		{
			await ProbeAsync();
		};
		_applyButton.Click += async (object? _, EventArgs _) =>
		{
			// 2026-10-09 cpupage-uifix（用户要求④）：这个「应用」**只属于负压表**（紧挨着表）。
			// 2026-10-10 percorenolog（用户原话：「全核负压框删掉」）：**全核 CO 那一分支已删除** ——
			// 页面上不再有"全核降压值"输入口，所以这颗按钮只做一件事：逐核写入每核表里的值
			// （`ApplyPerCore`）。不可编辑时它本来就置灰（`_applyButton.Enabled` 要求
			// `idle && CanWrite`），`PerCoreEditable()` 为假时这里**直接返回**，一条命令都不发。
			// ⚠️ 「重置」（`_resetButton` -> `ResetAll` -> `ResetAllCores`）仍然是那些
			// "没有每核写命令"的代号上唯一的全核 CO 出口（它走既有 SetCoAll(0)），语义未改。
			// 2026-10-10 flat-rows：Intel 的三个选项现在**各有各的一行、各有各的「应用」**
			// （通用行由 `BuildOptionRow` 建、倍频行是 `_ratioRow`）⇒ 这里**不再**需要"选中项通用通路"
			// 那条分支：本按钮只在负压表块出现（`_applyButton.Visible = coBlock`）。
			if (PerCoreEditable())
			{
				await ApplyPerCoreAsync();
			}
		};
		// 2026-10-09 cpupage-uifix（用户要求④）：固定全核频率**自己的**「应用」—— 走的还是既有通路
		// （RuntimeCpuTuning.Apply("amd-clock") -> RyzenSmuService.SetOcClock），没有新增命令/参数。
		_clockApply.Click += async (object? _, EventArgs _) =>
		{
			if (_clockOption == null)
			{
				return;
			}
			decimal value = _value.Value;
			await RunAsync(() => RuntimeCpuTuning.Instance.Apply(_clockOption.Id, value), restore: false);
		};
		// 2026-10-05 uifix（用户要求①②）：全窗口唯一的清零按钮。
		// 二次确认（列出"将写入的值"与"当前基线值"，安全信息不能砍）-> ResetAll
		//   = 逐核写 0 + 全核 SetCoAll(0) + 退出固定频率 + 清空保存的每核值。
		_resetButton.Click += async (object? _, EventArgs _) =>
		{
			if (_busy || _fixture != null || Program.UiAuditMode)
			{
				return;
			}
			if (!ConfirmReset())
			{
				return;
			}
			// 2026-10-10 fmax-persist：「重置」现在也把 FMax 恢复为**记录到的原值**。写之前的两个事实
			// 先记下来，好在结果里分清"本来就已是原值"与"这次真的写回去了"（RunAsync 之后 `_fmaxProbe`
			// 已经是**重置后**的探针结果，单看它分不出来）。
			int? fmaxBaselineBefore = RuntimeCpuTuning.FMaxBaselineValue;
			decimal? fmaxCurrentBeforeRaw = _fmaxProbe?.Current;
			int? fmaxCurrentBefore = fmaxCurrentBeforeRaw.HasValue ? (int)fmaxCurrentBeforeRaw.Value : ((int?)null);
			CpuTuningResult? result = await RunAsync(RuntimeCpuTuning.Instance.ResetAll, restore: true, successKey: "CpuTuneResetDone");
			if (result is { Success: true })
			{
				// 输入框跟着清零。**不动 `_perCoreBaseline`** —— 它记的是"进入窗口时实读到的值"，
				// 是这次操作的历史事实（每核区说明行的字面意思就是"进入时实读"），清成 null 会
				// 让说明行变成"不可回读"，那是假话。
				foreach (RNumericUpDown input in _perCoreInputs)
				{
					input.Value = Math.Clamp(0m, input.Minimum, input.Maximum);
				}
			}
			if (result != null)
			{
				AppendFMaxResetStatus(fmaxBaselineBefore, fmaxCurrentBefore);
			}
		};
		_autoApply.CheckedChanged += (object? _, EventArgs _) =>
		{
			if (_syncingAutoApply || _busy || _fixture != null || Program.UiAuditMode)
			{
				return;
			}
			if (_autoApply.Checked)
			{
				// 打开 = "开机后真的会写 SMU"，必须二次确认（明确告知会写入哪些值 + 当前基线）。
				if (MessageBox.Show(this, AutoApplyConfirmText(), "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
				{
					_syncingAutoApply = true;
					_autoApply.Checked = false;
					_syncingAutoApply = false;
					return;
				}
			}
			RuntimeCpuTuning.SetAutoApply(_autoApply.Checked);
			// 打开开关时把**当前输入框里的值**一并保存 —— 否则"看到的值"与"开机重放的值"会不是同一份。
			if (_autoApply.Checked)
			{
				SavePerCoreSettings();
				// 2026-10-10 fmax-persist：FMax 同理 —— 开机自动应用重放的是 cpu_fmax_saved，
				// 所以打开开关时把**当前 FMax 框里的值**也存下来（只写配置；FMax 不可写时不存）。
				SaveFMaxBoxIfWritable();
			}
			_status.Text = string.Format(Localized("CpuTuneSaved"), Localized(_autoApply.Checked ? "CpuTuneAutoApplyOn" : "CpuTuneAutoApplyOff"));
			UpdateSelection();
		};
		FormClosing += (object? _, FormClosingEventArgs e) =>
		{
			if (_busy)
			{
				e.Cancel = true;
			}
		};
		root.SizeChanged += (object? _, EventArgs _) =>
		{
			int width = Math.Max(200, root.ClientSize.Width - root.Padding.Horizontal);
			foreach (Label paragraph in _paragraphs)
			{
				paragraph.MaximumSize = new Size(width, 0);
			}
		};
		Shown += async (object? _, EventArgs _) =>
		{
			// 内容比窗口高时 WinForms 会自动滚到"最后加进去的控件"那一段；显式拉回顶部，
			// 让用户第一眼看到的是 CPU/选项/当前回读/每核输入框，而不是底部按钮。
			ScrollToTop();
			if (_fixture == null && !Program.UiAuditMode)
			{
				await ProbeAsync();
			}
			ScrollToTop();
		};
		ApplyProbe(fixture ?? new CpuTuningProbe(CpuInfo.Name, "CpuTuneProbePrompt", "", Array.Empty<CpuTuningOption>()));
		UiVisualStyle.ApplyWindow(this);
		void Add(Control control)
		{
			control.Margin = new Padding(0, 0, 0, 12);
			root.Controls.Add(control, 0, root.RowCount++);
		}
		// 2026-10-10 flat-rows：按钮工厂提到**方法**上（`RowButton`），这样构造函数里的局部 `Button(...)`
		// 与动态建行用的 `BuildOptionRow(...)` 共用**同一份**实现（同一个 Name/Text/尺寸/边距/光标）。
		RButton Button(FlowLayoutPanel host, string key, string name)
		{
			return RowButton(host, key, name);
		}

		Label Paragraph(string text)
		{
			Label label = new Label
			{
				Text = text,
				AutoSize = true,
				ForeColor = UiVisualStyle.Muted
			};
			_paragraphs.Add(label);
			return label;
		}
		Label Warning(string text)
		{
			Label label = new Label
			{
				Text = text,
				AutoSize = true,
				ForeColor = UiVisualStyle.Danger
			};
			_paragraphs.Add(label);
			return label;
		}
	}

	internal void ApplyProbe(CpuTuningProbe probe)
	{
		_probeDetail = probe.Detail;
		_probeStatusKey = probe.StatusKey;
		// 2026-10-10 fmax: FMax（PBO 最大频率）能力 + **只读**回读结论（探针里就已经读过了，
		// 这里只接结果，**绝不**再发一条命令）。
		_fmaxProbe = probe.FMax;
		_installButton.Visible = probe.StatusKey == "CpuTuneDriverMissing";
		_installButton.Enabled = !_busy && _fixture == null && !Program.UiAuditMode;
		_unavailable.Clear();
		foreach (CpuTuningOption option in probe.Options)
		{
			if (!option.CanWrite)
			{
				// 不可用选项**照样铺成一行**（只是整行置灰 + 在回读里给出原因）——用户要求
				// "单独置灰/隐藏并给出原因"；它不会解锁任何写入路径（Apply 的守卫仍只看 CanWrite）。
				_unavailable.Add(option);
			}
		}
		// ==========================================================================================
		// 2026-10-10 flat-rows（用户原话：「那是不是完全可以放一页里面 不做下拉」/「为什么下拉还在」）：
		// **不再有"默认选中项"这回事** —— 每个"通用数值选项"各占一行、各自可编辑（`SyncOptionRows`），
		// 一页全见。所以这里删掉了改前的 `_option.Items.Add(...)` + `SelectedIndex = ...` 那一段
		// （连同 `_option` 下拉控件本身）；`ReadbackText` / 不可用原因 / 摘要等文案来源一字未改。
		// ==========================================================================================
		SyncOptionRows(probe.Options);
		string text = Localized(probe.StatusKey) + ((probe.Detail.Length == 0) ? "" : (Environment.NewLine + probe.Detail));
		if (_unavailable.Count > 0)
		{
			// 逐一列出不可用的选项及其原因，说明区里必须看得到（用户验收要求）。
			foreach (CpuTuningOption item in _unavailable)
			{
				text += Environment.NewLine + Localized(item.LabelKey) + " " + Localized("CpuTuneOptionUnavailable") + ": " + UnavailableReason(item);
			}
		}
		// 2026-10-09 cpupage-uifix（用户要求③）：这一段技术细节**全部收进折叠区**（默认折叠）；
		// 页面上只留折叠区标题右边那一行摘要（例如「Phoenix · SMU 0x4C6500 · 部分选项被本机 SKU 锁定」）。
		// ⚠️ 只把文字搬家：`_status` 仍是"操作结果"那一行（探针时清空，应用/重置的结果照旧写它）。
		_detail.Text = text;
		_status.Text = "";
		_detailsGroup.Summary = SummaryText(probe);
		// 2026-10-05 percore-product: 首次探针后把**当前实读的每核值**填进每核输入框，并把这份
		// "进入前的值"记在内存里（「重置」的二次确认要引用它）。之后不再自动刷新，免得覆盖用户手改。
		CpuTuningOption? curve = probe.Options.FirstOrDefault((CpuTuningOption o) => o.Id == "amd-co");
		// 2026-10-09 cpupage-uifix（用户要求④）：两条关键选项按 Id 存下来 —— 负压表用 amd-co，
		// 「固定全核频率」那一块用 amd-clock（各自的范围/启用一律来自自己这一条，与下拉选谁无关）。
		_coOption = curve;
		_clockOption = probe.Options.FirstOrDefault((CpuTuningOption o) => o.Id == "amd-clock");
		// 2026-10-10 flat-rows：频率块的第 2 行 = Intel 的倍频（`intel-ratio`）。**混合架构上探针不产出
		// 这条选项** ⇒ 这里拿到 null ⇒ 整行不出现（⛔ 不留空行、⛔ 不置灰占版面）。
		_ratioOption = probe.Options.FirstOrDefault((CpuTuningOption o) => o.Id == "intel-ratio");
		if (!_perCoreFilled && curve != null)
		{
			FillPerCoreInputs(curve);
			_perCoreFilled = true;
			ScrollToTop();
		}
		// 开机自动应用开关：按配置回显（默认关 —— 配置里没有这个键就是关）。
		_syncingAutoApply = true;
		_autoApply.Checked = RuntimeCpuTuning.PerCoreAutoApplyEnabled;
		_syncingAutoApply = false;
		UpdateSelection();
	}

	// ==========================================================================================
	// 2026-10-10 flat-rows：**行内按钮的唯一工厂**（构造函数里的局部 `Button(...)` 现在只是转调它）。
	//   逐字沿用改前那一份（`Name` / 本地化 `Text` / AutoSize / Height 30 / 右边距 10 / 手型光标），
	//   ⛔ 没有新增/改变任何按钮属性；提为方法只是为了让动态建的选项行也能用同一份实现。
	// ==========================================================================================
	private RButton RowButton(FlowLayoutPanel host, string key, string name)
	{
		RButton rButton = new RButton
		{
			Name = name,
			Text = Localized(key),
			AutoSize = true,
			Height = 30,
			Margin = new Padding(0, 0, 10, 0),
			Cursor = Cursors.Hand
		};
		host.Controls.Add(rButton);
		return rButton;
	}

	// ==========================================================================================
	// 2026-10-10 flat-rows：**把每个"通用数值选项"铺成自己的一行**（用户原话：「那是不是完全可以放一页
	//   里面 不做下拉」）。
	//
	//   判据（"这个 id 有没有被专门块接管"）：
	//     * `amd-co`      -> 负压表块（`_perCoreTitle/_perCoreGrid/_perCoreInfo/_coButtons`）；
	//     * `amd-clock`   -> 频率块第 1 行（`_clockRow`，固定全核频率）；
	//     * `intel-ratio` -> 频率块第 2 行（`_ratioRow`，核心最大超频倍频）。
	//     其余（Intel 的 intel-core / intel-cache，以及将来任何新选项）在这里各占一行。
	//   ⚠️ 行**只建一次**：同一个 id 第二次探到只刷新范围 / 能力 / 回读，**不覆盖用户已经手改的数值**
	//      （与 `_clockFilled` 同一个用意）。`Setting` 用**最新一次探针**的那一条（CanWrite 可能因
	//      固件拒绝而在本会话里翻成 false，`UpdateSelection` 就按它置灰）。
	//   ⚠️ 平台门控不在这里：探针本身就只产出本平台的选项（AMD 只给 amd-*、Intel 只给 intel-*）
	//      ⇒ "每行只在自己的平台出现"由**数据**保证（这也让审计夹具喂什么就画什么）。
	// ==========================================================================================
	private void SyncOptionRows(IReadOnlyList<CpuTuningOption> options)
	{
		foreach (CpuTuningOption option in options)
		{
			if (option.Id == "amd-co" || option.Id == "amd-clock" || option.Id == "intel-ratio")
			{
				continue;
			}
			OptionRow row = _optionRowList.FirstOrDefault((OptionRow r) => r.Setting.Id == option.Id);
			if (row == null)
			{
				row = BuildOptionRow(option);
				_optionRowList.Add(row);
				_optionRows.Controls.Add(row.Row);
			}
			row.Setting = option;
			row.Input.Minimum = option.Minimum;
			row.Input.Maximum = option.Maximum;
			row.Input.Value = Math.Clamp(row.Input.Value, option.Minimum, option.Maximum);
		}
	}

	// 一行的构造：`<标签 (单位)>  <数值框>  <「应用」>  <回读>`。
	//   * 标签/数值框/「应用」三件套与 `_clockRow` / `_fmaxRow` 逐字同族（同一个 `Button()` 工厂、
	//     同一套 `UsePlainTextBox + EditRectOnly` 外观）；
	//   * 回读标签进 `_paragraphs`（⇒ 随窗口宽度自动换行，长原因不会把行撑出横向滚动条）；
	//   * 「应用」= 只对这一行的数值框生效，走既有 `RuntimeCpuTuning.Apply(这一行的 id, 值)`：
	//     ⛔ 不新增命令、⛔ 不新增参数、⛔ 不落盘（Intel 侧没有 cpu_co_* / cpu_fmax_* 这套配置）。
	private OptionRow BuildOptionRow(CpuTuningOption option)
	{
		FlowLayoutPanel row = new FlowLayoutPanel
		{
			Name = "cpuTuneOptionRow_" + option.Id,
			AutoSize = true,
			// ⚠️ `Dock=Top` 而不是 `Fill`：本页实测，嵌套 AutoSize TableLayoutPanel 里的 `Dock=Fill`
			//    AutoSize FlowLayoutPanel 会被**按宽度 0 量一次**并把这个高度缓存下来（第二行的
			//    preferred height 被量成 206px，实际内容只有 74px）⇒ 整页多出一条空白带。
			//    `Dock=Top` 时宽度由父容器直接给（不再是"先量宽度"），行高就按真实宽度算回来。
			Dock = DockStyle.Top,
			WrapContents = true
		};
		Label label = new Label
		{
			Name = "cpuTuneOptionLabel_" + option.Id,
			Text = Localized(option.LabelKey) + " (" + option.Unit + ")",
			AutoSize = true,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 6, 8, 0)
		};
		RNumericUpDown input = new RNumericUpDown
		{
			Name = "cpuTuneValue_" + option.Id,
			Width = D(110),
			Minimum = option.Minimum,
			Maximum = option.Maximum,
			DecimalPlaces = 0,
			Value = Math.Clamp(decimal.Round(option.Current ?? 0m), option.Minimum, option.Maximum),
			Enabled = false,
			Margin = new Padding(0, 2, 10, 0)
		};
		input.UsePlainTextBox();
		input.OutlineMode = InputOutlineMode.EditRectOnly;
		row.Controls.Add(label);
		row.Controls.Add(input);
		RButton apply = RowButton(row, "CpuTuneApply", "cpuTuneApply_" + option.Id);
		apply.Enabled = false;
		string optionId = option.Id;
		RNumericUpDown box = input;
		// ⚠️ 同 `_ratioApply`：用丢弃参数，不新增 CS8632。
		apply.Click += async (_, _) =>
		{
			decimal value = box.Value;
			await RunAsync(() => RuntimeCpuTuning.Instance.Apply(optionId, value), restore: false);
		};
		Label readback = new Label
		{
			Name = "cpuTuneReadback_" + option.Id,
			AutoSize = true,
			ForeColor = UiVisualStyle.Muted,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 6, 0, 0)
		};
		row.Controls.Add(readback);
		_paragraphs.Add(readback);
		return new OptionRow
		{
			Setting = option,
			Row = row,
			Input = input,
			Apply = apply,
			Readback = readback
		};
	}

	// 一行尾部那条回读（文案来源与 `_current` 完全同一套：`ReadbackText` + 不可用原因）。
	//   * 可写：只给「回读: …」；
	//   * 不可写：先给「不可用: 原因」再给回读 —— 与本页既有约定一致（能不能读与能不能写是两把锁）。
	private string OptionRowReadbackText(CpuTuningOption option)
	{
		string text = ReadbackText(option);
		if (option.CanWrite)
		{
			return text;
		}
		return Localized("CpuTuneOptionUnavailable") + ": " + UnavailableReason(option) + Environment.NewLine + text;
	}

	// ==========================================================================================
	// 2026-10-05 percore-product: 每核面板的构建 / 填充 / 门控 / 三个动作。
	// ==========================================================================================
	private void EnsurePerCoreGrid(int coreCount)
	{
		if (_perCoreCount == coreCount && _perCoreInputs.Count == coreCount)
		{
			return;
		}
		_perCoreCount = coreCount;
		_perCoreGrid.SuspendLayout();
		_perCoreGrid.Controls.Clear();
		_perCoreInputs.Clear();
		int rows = Math.Max(1, (coreCount + 1) / 2);
		_perCoreGrid.RowCount = rows;
		_perCoreGrid.RowStyles.Clear();
		for (int row = 0; row < rows; row++)
		{
			_perCoreGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			// 左列核号 = row，右列核号 = row + rows（8 核 = 左 0..3 / 右 4..7）。
			for (int side = 0; side < 2; side++)
			{
				int core = row + side * rows;
				if (core >= coreCount)
				{
					continue;
				}
				Label label = new Label
				{
					Text = string.Format(Localized("CpuTuneCoreLabel"), core),
					AutoSize = true,
					TextAlign = ContentAlignment.MiddleLeft,
					Margin = new Padding((side == 0) ? 0 : 14, 6, 6, 0)
				};
				RNumericUpDown input = new RNumericUpDown
				{
					Name = "cpuTuneCore" + core,
					Width = D(78),
					Minimum = -30m,
					Maximum = 0m,
					DecimalPlaces = 0,
					Value = 0m,
					Margin = new Padding(0, 2, 0, 2)
				};
				// 2026-10-09（用户要求）：每核框同样走「纯净文字框」—— 顺带把它右边那对**仍然可见的**
				// 原生上下箭头藏掉（`UsePlainTextBox()` 内部会调 `HideNativeSpinButtons()`，本页此前
				// 只有这几只框没藏）；键盘上下键 / 滚轮 / 直接输入 / Minimum-Maximum 校验一字未改。
				input.UsePlainTextBox();
				// 2026-10-10（用户原话：「CPU调节里面负压的文本框框没有了」——指的就是每核这一排）：
				// 上一轮这里开的是 `SuppressInputOutline = true`（一点都不画）⇒ 这排框只剩数字。
				// 本轮改成 `EditRectOnly`：只描内嵌编辑框那块矩形（同上一条外观约定），
				// 右侧 16 设备像素空条不被框住 ⇒ 边框可见、也没有"空的小方格"。
				// ⚠️ 每核一只框，本处一个调用点全部生效；取值/范围/校验/写入路径一字未动。
				input.OutlineMode = InputOutlineMode.EditRectOnly;
				_perCoreGrid.Controls.Add(label, side * 2, row);
				_perCoreGrid.Controls.Add(input, side * 2 + 1, row);
				_perCoreInputs.Add(input);
			}
		}
		_perCoreGrid.ResumeLayout();
	}

	// 每核输入框的初值 = 本次开机的实读值（用户要求"首次进入时把当前每核读值填进输入框"）。
	// 读不到时退回已保存的设置（再退回 0），并在说明行里标明"不可回读"。
	private void FillPerCoreInputs(CpuTuningOption curve)
	{
		int count = curve.CoreReadback?.Count ?? RuntimeCpuTuning.PerCoreCapacity;
		if (count <= 0)
		{
			return;
		}
		EnsurePerCoreGrid(count);
		IReadOnlyList<int?> saved = RuntimeCpuTuning.LoadSavedPerCore(count);
		for (int core = 0; core < count && core < _perCoreInputs.Count; core++)
		{
			int value;
			if (curve.CoreReadback != null && core < curve.CoreReadback.Count)
			{
				value = curve.CoreReadback[core];
			}
			else
			{
				value = saved[core] ?? 0;
			}
			RNumericUpDown input = _perCoreInputs[core];
			input.Minimum = curve.Minimum;
			input.Maximum = curve.Maximum;
			input.Value = Math.Clamp((decimal)value, curve.Minimum, curve.Maximum);
		}
		_perCoreBaseline = curve.CoreReadback?.ToList();
	}

	// 每核那一组（输入框/逐核应用/重置/保存/自动应用开关）能不能编辑。
	// 两个条件缺一不可：SMU 覆盖层允许写（CanWrite）、该代号有已批准的每核写路径（PerCoreWrite，
	// 只有桌面 Vermeer）。本机 R7 APU 第一条就是 false -> 整组置灰。
	// 2026-10-10 flat-rows：改前这里还有一个"下拉选中的是 amd-co"的前提。下拉已删除；负压表块
	// （含每核表）**只由 `_coOption != null` 决定显隐**、`_coOption` 按定义就是 amd-co 那一条 ⇒
	// 去掉那个前提**不改变任何一台机器上的取值**（本机仍是 false：CanWrite=false）。
	private bool PerCoreEditable()
	{
		return !_busy && _fixture == null && !Program.UiAuditMode
			&& _coOption is { CanWrite: true, PerCoreWrite: true };
	}

	// "保存超频设置" = **只写配置文件，不碰 SMU**。
	// 每核值只在"每核可编辑"时从输入框取 —— 否则保留已保存的那一份，绝不把"回读值"当成用户设置存进去。
	// 2026-10-05 uifix：「保存设置」按钮已并进「应用」，这里不再需要 quiet 开关
	// （真正落盘的日志由 RuntimeCpuTuning.SaveSettings 统一打印）。
	// 2026-10-10 flat-rows：同 `PerCoreEditable` —— 改前"下拉选中 amd-co 才存"这一条随下拉一起删掉；
	// `SaveCoValues` 自己仍要求 `_coOption.CanWrite`（不可写的机器上照旧一个字节都不写）。
	private bool SavePerCoreSettings()
	{
		if (_coOption == null)
		{
			return false;
		}
		return SaveCoValues();
	}

	// 2026-10-10 fmax-persist：CO 那一份的落盘实现（= 改前 SavePerCoreSettings 的正文，**落盘语义一字未改**）。
	//   * 每核值只在"每核可编辑"时从输入框取；否则保留已保存的那一份 —— **绝不把回读值当用户设置存进去**；
	//   * 全核值：2026-10-10 percorenolog 起**恒传 0** —— 用户要求删掉"全核负压框"，页面上已无
	//     `_coAll` 控件，所以"当前全核值"这件事不存在了。历史键 `cpu_co_all` 仍然被写（写 0），
	//     以免旧配置里残留的旧全核值在"没有每核写的代号"上被「读取」重放出来（那会是个幽灵值）；
	//   * 只写配置，不碰 SMU。
	private bool SaveCoValues()
	{
		// 能写 CO 才谈得上"保存 CO 设置"（不可写的机器上输入框里显示的是**回读值**，
		// 把它当用户设置存下去就是撒谎 —— 这正是改前 SavePerCoreSettings 一直在守的规矩）。
		if (_coOption == null || !_coOption.CanWrite)
		{
			return false;
		}
		int coreCount = (_perCoreCount > 0) ? _perCoreCount : RuntimeCpuTuning.PerCoreCapacity;
		List<int> values;
		if (PerCoreEditable() && _perCoreInputs.Count == coreCount)
		{
			values = _perCoreInputs.Select((RNumericUpDown n) => (int)n.Value).ToList();
		}
		else
		{
			values = RuntimeCpuTuning.LoadSavedPerCore(coreCount).Select((int? v) => v ?? 0).ToList();
		}
		RuntimeCpuTuning.SaveSettings(values, 0);
		return true;
	}

	// 2026-10-10 fmax-persist：FMax 那一份的落盘（只写配置）。能写 FMax 才存 ——
	// 只读/不支持的机器上"保存 FMax"毫无意义（「读取」永远写不回去），存了只会误导。
	private bool SaveFMaxBoxIfWritable()
	{
		if (!FMaxCapabilityWritable())
		{
			return false;
		}
		return RuntimeCpuTuning.SaveFMax((int)_fmax.Value);
	}

	// ==========================================================================================
	// 2026-10-10 fmax-persist（用户决定②）：「保存」按钮。
	//   保存 = 把**当前的 CO 值 + 当前 FMax** 写进既有配置（cpu_co_* / cpu_fmax_saved）；
	//   ⛔ 只写配置，**一条 SMU 命令都不发**（与 SaveSettings 同一套做法）。
	//   状态行说清存了什么、跳过了什么（跳过就写明原因，不静默）。
	// ==========================================================================================
	private void SaveSnapshot()
	{
		if (_busy || _fixture != null || Program.UiAuditMode)
		{
			return;
		}
		int coreCount = (_perCoreCount > 0) ? _perCoreCount : RuntimeCpuTuning.PerCoreCapacity;
		List<string> saved = new List<string>();
		List<string> skipped = new List<string>();
		if (SaveCoValues())
		{
			List<int> coValues = RuntimeCpuTuning.LoadSavedPerCore(coreCount).Select((int? v) => v ?? 0).ToList();
			saved.Add(string.Format(Localized("CpuTuneSnapshotCoPerCore"), string.Join(", ", coValues)));
			// 2026-10-10 percorenolog：⛔ 不再有"全核 CO {0}"这一句 —— 全核负压框已按用户要求删除，
			// 页面上没有全核值可存（SaveSettings 现在恒写 0），报一个 0 只会误导。
		}
		else
		{
			skipped.Add(string.Format(Localized("CpuTuneSnapshotCoNotSaved"), Localized("CpuTuneSnapshotCoNotWritable")));
		}
		if (SaveFMaxBoxIfWritable())
		{
			saved.Add(string.Format(Localized("CpuTuneSnapshotFMax"), $"{(int)_fmax.Value:0}"));
		}
		else
		{
			skipped.Add(string.Format(Localized("CpuTuneSnapshotFMaxNotSaved"), FMaxSkipReason()));
		}
		string head = string.Format(Localized(saved.Count > 0 ? "CpuTuneSnapshotSaved" : "CpuTuneSnapshotNothing"),
			string.Join("; ", saved));
		if (skipped.Count > 0)
		{
			head += Environment.NewLine + string.Format(Localized("CpuTuneSnapshotSkipped"), string.Join("; ", skipped));
		}
		_status.Text = head;
	}

	// FMax 为什么没被保存/应用 —— 用**能力那一句**（只读 / 不支持 / 缺驱动…），不硬编码。
	private string FMaxSkipReason()
	{
		return (_fmaxProbe == null) ? Localized(_probeStatusKey) : FMaxCapabilityLine();
	}

	// ==========================================================================================
	// 2026-10-10 fmax-persist（用户决定②）：「读取」按钮 = 把保存过的 CO / FMax 读回来**并应用**。
	//   * CO：每核表存在且本机每核可写 -> 既有 ApplyPerCore；否则全核值存在且 CO 可写 -> 既有
	//     Apply("amd-co")；两者都不行就**跳过并说明原因**（绝不硬写）；
	//   * FMax：走既有 ApplyFMax（写 + 回读校验 + 固件状态码）；能力不含写路径 -> 跳过并说明；
	//   * 全部在既有的 RunAsync 里跑：它负责冻结界面、重跑一次探针（于是回读行/说明行自动刷新），
	//     并把结果写进 `_status`；本方法随后用**本地化文案**重组那一行，把"应用了什么 / 跳过了什么"说清。
	//   * ⛔ 只读 / 不支持的机器上：ApplyFMax 的能力守卫在发命令之前就拒绝，一条命令都不发。
	// ==========================================================================================
	private async Task LoadSnapshotAsync()
	{
		if (_busy || _fixture != null || Program.UiAuditMode)
		{
			return;
		}
		int coreCount = (_perCoreCount > 0) ? _perCoreCount : RuntimeCpuTuning.PerCoreCapacity;
		IReadOnlyList<int?> savedPerCore = RuntimeCpuTuning.LoadSavedPerCore(coreCount);
		int? savedAll = RuntimeCpuTuning.LoadSavedAllCore();
		int? savedFMax = RuntimeCpuTuning.LoadSavedFMax();
		bool anyCoSaved = savedPerCore.Any((int? v) => v.HasValue) || savedAll.HasValue;
		if (!anyCoSaved && !savedFMax.HasValue)
		{
			// 配置里什么都没有：不写、不探，只把事实说出来。
			_status.Text = Localized("CpuTuneSnapshotEmptyConfig");
			return;
		}
		List<int> coValues = savedPerCore.Select((int? v) => v ?? 0).ToList();
		bool perCoreReady = PerCoreWriteCapable();
		bool allCoreReady = _coOption is { CanWrite: true };
		bool fmaxReady = FMaxCapabilityWritable();
		// CO 走哪一条：0 = 不应用，1 = 每核表，2 = 全核值。
		int coPath = (savedPerCore.Any((int? v) => v.HasValue) && perCoreReady) ? 1
			: ((savedAll.HasValue && allCoreReady) ? 2 : 0);
		int coAll = savedAll ?? 0;
		bool fmaxApply = savedFMax.HasValue && fmaxReady;
		int fmaxValue = savedFMax ?? 0;
		// 2026-10-10 fmax-persist：状态行必须按**真实结果**分档，绝不能按"打算做什么"分档
		// （那会把"SMU 拒绝了 / SMU 没初始化"写成"已应用"）。这三个表在 lambda 里填，
		// await 之后由本方法读 —— 跨线程读的是 Task 完成后的值，没有竞态。
		List<string> appliedParts = new List<string>();
		List<string> failedParts = new List<string>();
		CpuTuningResult? result = await RunAsync(delegate
		{
			try
			{
				List<string> notes = new List<string>();
				bool anyAttempt = false;
				bool anyFailed = false;
				bool allVerified = true;
				if (coPath == 1)
				{
					anyAttempt = true;
					CpuTuningResult co = RuntimeCpuTuning.Instance.ApplyPerCore(coValues);
					anyFailed |= !co.Success;
					allVerified &= co.Verified;
					(co.Success ? appliedParts : failedParts).Add(string.Format(Localized("CpuTuneSnapshotCoPerCore"), string.Join(", ", coValues)));
					notes.Add($"CO per-core [{string.Join(", ", coValues)}]: {co.Detail}");
				}
				else if (coPath == 2)
				{
					anyAttempt = true;
					CpuTuningResult co = RuntimeCpuTuning.Instance.Apply("amd-co", coAll);
					anyFailed |= !co.Success;
					allVerified &= co.Verified;
					(co.Success ? appliedParts : failedParts).Add(string.Format(Localized("CpuTuneSnapshotCoAll"), coAll));
					notes.Add($"CO all-core {coAll}: {co.Detail}");
				}
				else
				{
					notes.Add("CO not applied: " + (!anyCoSaved ? "no saved CO values" : "the CO write path is unavailable on this machine (not writable / no per-core write)"));
				}
				if (fmaxApply)
				{
					anyAttempt = true;
					CpuTuningResult fmax = RuntimeCpuTuning.Instance.ApplyFMax(fmaxValue);
					anyFailed |= !fmax.Success;
					allVerified &= fmax.Verified;
					(fmax.Success ? appliedParts : failedParts).Add(string.Format(Localized("CpuTuneSnapshotFMax"), $"{fmaxValue:0}"));
					notes.Add($"FMax {fmaxValue} MHz: {fmax.Detail}");
				}
				else
				{
					notes.Add("FMax not applied: " + (!savedFMax.HasValue ? "no saved FMax value" : "this codename has no FMax write path"));
				}
				return new CpuTuningResult(anyAttempt && !anyFailed, anyAttempt && allVerified, string.Join(Environment.NewLine, notes));
			}
			catch (Exception ex)
			{
				// 与既有 ApplyFMaxAsync 同级的兜底：异常绝不让它冒到 UI 线程外。
				Logger.WriteLine("CPU snapshot load failed: " + ex);
				return new CpuTuningResult(Success: false, Verified: false, ex.Message);
			}
		}, restore: false, successKey: "CpuTuneSnapshotLoaded");
		if (result == null)
		{
			return;
		}
		// 每核输入框跟着"读回来的值"更新 —— **只在真的每核可写时**：否则那些框显示的是
		// 「进入时实读」，被保存值覆盖会让用户以为 CPU 现在就是这个值（那是假话）。
		if (coPath == 1 && PerCoreWriteCapable())
		{
			for (int core = 0; core < coValues.Count && core < _perCoreInputs.Count; core++)
			{
				RNumericUpDown input = _perCoreInputs[core];
				input.Value = Math.Clamp((decimal)coValues[core], input.Minimum, input.Maximum);
			}
		}
		// 状态行：**真的应用成功的** / **试了但没成功的** / **因为能力不可用而没试的**，三档分开写
		// （跳过的原因用本地化能力文案，失败的那一档后面紧接着一行英文技术详情/固件状态码）。
		List<string> statusLines = new List<string>();
		if (appliedParts.Count > 0)
		{
			statusLines.Add(string.Format(Localized("CpuTuneSnapshotLoaded"), string.Join("; ", appliedParts)));
		}
		if (failedParts.Count > 0)
		{
			statusLines.Add(string.Format(Localized("CpuTuneSnapshotFailed"), string.Join("; ", failedParts)));
		}
		List<string> notAttempted = new List<string>();
		if (coPath == 0)
		{
			notAttempted.Add(string.Format(Localized("CpuTuneSnapshotCoSkipped"), Localized("CpuTuneSnapshotCoNotWritable")));
		}
		if (!fmaxApply && savedFMax.HasValue)
		{
			notAttempted.Add(string.Format(Localized("CpuTuneSnapshotFMaxSkipped"), FMaxSkipReason()));
		}
		if (statusLines.Count == 0)
		{
			statusLines.Add(string.Format(Localized("CpuTuneSnapshotNothingApplied"), string.Join("; ", notAttempted)));
		}
		else if (notAttempted.Count > 0)
		{
			statusLines.Add(string.Format(Localized("CpuTuneSnapshotNotApplied"), string.Join("; ", notAttempted)));
		}
		_status.Text = string.Join(Environment.NewLine, statusLines) + Environment.NewLine + result.Detail;
	}

	// ==========================================================================================
	// 2026-10-10 fmax-persist（用户决定③「点重置都是恢复默认」）：
	// 「重置」之后把 FMax 那一半的结论**用本地化文案**补一行（ResetAll 的 Detail 是英文技术文本）。
	// 只在能力含写路径时补 —— 只读 / 不支持的机器上「重置」根本不碰 FMax，补一行只会是噪音。
	// baselineBefore / currentBefore = **写之前**的两个事实（RunAsync 之后 `_fmaxProbe` 已经是重置后的了）：
	//   没有原值                 -> 明说"没有记录到原值，不猜值"（用户要求）；
	//   写之前就已经等于原值     -> "已是原值，未写入"；
	//   否则回读等于原值         -> "已恢复为上次读到的原值"；
	//   回读对不上               -> "未能恢复"（详情在上一行的技术文本里）。
	// ==========================================================================================
	private void AppendFMaxResetStatus(int? baselineBefore, int? currentBefore)
	{
		if (!FMaxCapabilityWritable())
		{
			return;
		}
		string line;
		if (!baselineBefore.HasValue)
		{
			line = Localized("CpuTuneFMaxResetNoBaseline");
		}
		else if (currentBefore.HasValue && currentBefore.Value == baselineBefore.Value)
		{
			line = string.Format(Localized("CpuTuneFMaxResetAlready"), $"{baselineBefore.Value:0}");
		}
		else
		{
			decimal? currentAfterRaw = _fmaxProbe?.Current;
			int? currentAfter = currentAfterRaw.HasValue ? (int)currentAfterRaw.Value : ((int?)null);
			line = (currentAfter.HasValue && currentAfter.Value == baselineBefore.Value)
				? string.Format(Localized("CpuTuneFMaxResetRestored"), $"{baselineBefore.Value:0}")
				: string.Format(Localized("CpuTuneFMaxResetFailed"), $"{baselineBefore.Value:0}");
		}
		_status.Text = _status.Text + Environment.NewLine + line;
	}

	// 「读取」用：本机有没有**每核写**路径（判据与 `PerCoreEditable` 同一把尺子）。
	// ⚠️ **只看能力，不看下拉框选了谁** —— 下拉只是"现在在看哪一块"，不该决定"读回来的每核值走哪条路"：
	//    否则用户把下拉切到「固定全核频率」再点「读取」，每核 profile 就会被全核 SetCoAll 悄悄盖掉。
	private bool PerCoreWriteCapable()
	{
		return !_busy && _fixture == null && !Program.UiAuditMode
			&& _coOption is { CanWrite: true, PerCoreWrite: true };
	}

	// 2026-10-10 percorenolog（用户要求「失败不静默」「不一致则用【写前基线】全量回写」）：
	// 每核写入的界面收口。
	//   * 失败**不静默**：`RunAsync` 已经把 `CpuTuneFailed`（= "调节失败，请查看详情"）写在状态行第一行，
	//     这里再补一句**明确的英文技术结论**（"已按写前基线回退" / "有核写失败且回退未确认"），
	//     并给一句可执行的下一步（点「重置」）；
	//   * 只有 `Success: true` 才落盘保存（成功才保存，失败不写配置 —— 与改前同一把尺子）；
	//   * ⚠️ 这里**不发任何 SMU 命令**：回退与重试全部在 `RuntimeCpuTuning.ApplyPerCore` 里完成。
	private async Task ApplyPerCoreAsync()
	{
		if (!PerCoreEditable())
		{
			return;
		}
		List<int> values = _perCoreInputs.Select((RNumericUpDown n) => (int)n.Value).ToList();
		CpuTuningResult? result = await RunAsync(() => RuntimeCpuTuning.Instance.ApplyPerCore(values), restore: false);
		if (result == null)
		{
			return;
		}
		if (result.Success)
		{
			// 应用成功 -> 一并保存（用户要求"保存超频设置"）。
			SavePerCoreSettings();
			return;
		}
		// 失败：把"到底回退了没有"如实补在状态行末尾（英文技术结论，与 Detail 同一风格）。
		string tail;
		if (result.Detail.Contains("rolled back every core to the pre-write baseline: confirmed", StringComparison.Ordinal))
		{
			tail = "Per-core CO write did NOT take effect: every core was written back to the pre-write baseline"
				+ " (verified by readback). Nothing changed; click Reset if you want to clear the curve explicitly.";
		}
		else if (result.Detail.Contains("rolled back every core to the pre-write baseline: NOT confirmed", StringComparison.Ordinal))
		{
			tail = "Per-core CO write did NOT take effect and the write-back to the pre-write baseline could NOT be"
				+ " confirmed by readback. Click Reset (or re-apply the values you want) to make the CPU state explicit.";
		}
		else if (result.Detail.Contains("NOT rolled back", StringComparison.Ordinal))
		{
			tail = "Per-core CO write failed and there is no complete pre-write baseline on this codename, so nothing was"
				+ " written back (no value was guessed). The CPU may keep a partial change; click Reset to force all cores to 0.";
		}
		else if (!result.Verified)
		{
			tail = "Per-core CO write was accepted by the SMU but could not be verified: this codename has no per-core read"
				+ " command, so the values in the boxes are not confirmed to be in effect.";
		}
		else
		{
			tail = "Per-core CO write failed; see the technical detail above.";
		}
		_status.Text = _status.Text + Environment.NewLine + tail;
	}

	// 「重置」按钮的二次确认（用户要求①③）：
	//   * 「重置」= 逐核写 0 + 全核 SetCoAll(0) 兜底 + 退出固定频率 + 清空保存的每核值；
	//   * 文案必须带**将写入的值**与**当前基线值** —— 这是安全信息，不随"精简"一起砍。
	// 这台台式机实测的写前基线就是 8 核全 -15（用户原本就有运行时偏移），点一下重置就会清掉。
	private bool ConfirmReset()
	{
		return MessageBox.Show(this, ResetConfirmText(), Localized("CpuTuneResetConfirmTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
	}

	// 「重置」二次确认的正文（拆成纯字符串方法：取证工具可以只读取到它，**不必也不许**弹窗）。
	// 2026-10-10 fmax-persist：能写 FMax 时把**将写回的 FMax 原值**也列进"将写入"里；
	// 没有记录到原值就明写"FMax 原值未知（不会写入）"—— 确认框不能说"会恢复 FMax"却什么都不写。
	internal string ResetConfirmText()
	{
		string target = (_perCoreInputs.Count > 0)
			? string.Join(", ", _perCoreInputs.Select((RNumericUpDown n) => "0"))
			: string.Join(", ", Enumerable.Repeat("0", Math.Max(1, RuntimeCpuTuning.PerCoreCapacity)));
		if (FMaxCapabilityWritable())
		{
			int? baseline = RuntimeCpuTuning.FMaxBaselineValue;
			target += "; " + (baseline.HasValue
				? string.Format(Localized("CpuTuneSnapshotFMax"), $"{baseline.Value:0}")
				: Localized("CpuTuneFMaxBaselineUnknownShort"));
		}
		return string.Format(Localized("CpuTuneResetConfirm"), target, BaselineText());
	}

	// 「开机自动应用」二次确认的正文（将写入的值 + 当前基线值都必须在里面）。
	// 2026-10-10 fmax-persist：开关同时管 CO 与 FMax，所以这里也要列出 FMax 那一份
	// （= 打开开关时会**一并保存**的当前 FMax 框里的值）。
	internal string AutoApplyConfirmText()
	{
		string values = (_perCoreInputs.Count > 0)
			? string.Join(", ", _perCoreInputs.Select((RNumericUpDown n) => ((int)n.Value).ToString()))
			: string.Join(", ", RuntimeCpuTuning.LoadSavedPerCore(RuntimeCpuTuning.PerCoreCapacity).Select((int? v) => (v ?? 0).ToString()));
		if (FMaxCapabilityWritable())
		{
			values += "; " + string.Format(Localized("CpuTuneSnapshotFMax"), $"{(int)_fmax.Value:0}");
		}
		return string.Format(Localized("CpuTuneAutoApplyConfirm"), values, BaselineText());
	}

	// "进入时实读"的基线（读不到就明说读不到，绝不用 0 假装）。
	private string BaselineText()
	{
		return (_perCoreBaseline == null)
			? Localized("CpuTunePerCoreBaselineUnknown")
			: string.Join(", ", _perCoreBaseline.Select((int v) => v.ToString()));
	}

	// 不可用原因：probe 把 SKU 层拒绝原文放在 Detail 里（例如
	// "Phoenix; SMU 0x4C6500; amd-co unavailable: R7 tier does not expose UV (only R9); ..."），
	// 这里把 amd-co 那一段摘出来贴到选项旁边；摘不到就回退成整条 Detail。
	private string UnavailableReason(CpuTuningOption option)
	{
		string detail = _probeDetail;
		string marker = option.Id + " unavailable:";
		int num = detail.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return (detail.Length == 0) ? Localized("CpuTuneNoReadback") : detail;
		}
		string text = detail.Substring(num + marker.Length).Trim();
		int num2 = text.IndexOf(';');
		if (num2 >= 0)
		{
			text = text.Substring(0, num2).Trim();
		}
		return text;
	}

	// 2026-10-09 cpupage-uifix（用户要求③）：页面上那一行摘要（折叠区标题右侧）。
	// 全部由**探针原文里已有的片段** + **既有结论**拼出，⛔ 不新增任何判定：
	//   SetCoAll / tier / PerCoreWrite 的结论一字未改，这里只是把 Detail 里前两段（代号 / SMU 版本）
	//   摘出来，再按 CanWrite / PerCoreWrite 的既有结果补一句状态。
	private string SummaryText(CpuTuningProbe probe)
	{
		if (probe.Options.Count == 0)
		{
			// 没有可选项（如缺驱动）：摘要直接用状态句，别拼一个没意义的代号。
			return Localized(probe.StatusKey);
		}
		List<string> parts = new List<string>();
		string tag = SummaryTag(probe.Detail);
		if (tag.Length > 0)
		{
			parts.Add(tag);
		}
		bool anyWritable = probe.Options.Any((CpuTuningOption o) => o.CanWrite);
		bool anyLocked = probe.Options.Any((CpuTuningOption o) => !o.CanWrite);
		if (!anyLocked)
		{
			parts.Add(Localized("CpuTuneSummaryReady"));
		}
		else
		{
			parts.Add(Localized(anyWritable ? "CpuTuneSummaryPartial" : "CpuTuneSummaryLocked"));
		}
		// 2026-10-10 percorenolog：这句话现在的含义收窄成**真的没有每核写命令**（或 SKU 没放行）——
		// ⛔ 不再是"未实测/未批准"的意思。判据与负压表内部的 `PerCoreInfoText` 同一把尺子。
		CpuTuningOption? curve = probe.Options.FirstOrDefault((CpuTuningOption o) => o.Id == "amd-co");
		if (curve != null && curve.CanWrite && !curve.PerCoreWrite)
		{
			parts.Add(Localized("CpuTuneSummaryPerCoreOff"));
		}
		return string.Join(" · ", parts);
	}

	// 摘 Detail 的前两段：代号（第一段）+ SMU 版本段（"SMU 0x…"）—— 例如 "Phoenix · SMU 0x4C6500"。
	// 摘不到 SMU 段就只留代号；代号过长时截断加省略号（宁可短，也不让摘要把折叠区标题挤变形）。
	private static string SummaryTag(string detail)
	{
		if (detail.Length == 0)
		{
			return "";
		}
		string[] parts = detail.Split(';');
		string codename = parts[0].Trim();
		if (codename.Length > 28)
		{
			codename = codename.Substring(0, 28).TrimEnd() + "…";
		}
		foreach (string part in parts)
		{
			string trimmed = part.Trim();
			if (trimmed.StartsWith("SMU ", StringComparison.OrdinalIgnoreCase))
			{
				return codename + " · " + trimmed;
			}
		}
		return codename;
	}

	private void UpdateSelection()
	{
		bool idle = !_busy && _fixture == null && !Program.UiAuditMode;
		// ==========================================================================================
		// 2026-10-10 intel-fmax（用户原话：「新bug 英特尔机型显示了Fmax」）：
		// **"哪些控件在非 AMD 平台上不出现"的唯一落点**（与下面"按模式隐藏/按能力置灰"同一处写法）。
		//   * 判据 = AmdTuningPlatform()（= 既有 CpuInfo.IsAMD，理由见它的注释）；
		//   * 被收起的控件**一律先 Visible=false**，AMD 上再逐个放行 —— 分支只会有一条成立；
		//   * ⛔ 只碰 Visible / 文案，⛔ 不碰任何 Enabled、取值、事件与写入路径。
		// 布局：这些控件都是本页根 TableLayoutPanel（AutoSize + Dock=Top）里**独占一行**的，
		// 行高走 TableLayoutPanel 的默认 AutoSize ⇒ 控件 Visible=false 时该行高度自动收成 0、
		// **不留空白带**（与本页既有的 `_guardStatus` / `_status` 空文本折叠、
		// SettingsForm.ApplyChargeSliderVisibility 收起限充滑块行同一个道理；那一处是 Absolute 行高
		// 才必须手写 RowStyles 32↔0，本页是 AutoSize 行，无需改 RowStyles）。
		// ==========================================================================================
		bool amdTuning = AmdTuningPlatform();
		// 2026-10-10 intel（⑦B）：Intel 平台分流（理由见 IntelTuningPlatform 的注释）。
		// ⛔ 与 amdTuning 互斥：Intel 上 amdTuning 恒为 false，所以下面每一条 AMD 专属行都自动收起。
		bool intelTuning = IntelTuningPlatform();
		_probeButton.Enabled = idle;
		_installButton.Enabled = idle;
		// ==========================================================================================
		// 2026-10-10 flat-rows（用户原话：「那是不是完全可以放一页里面 不做下拉」/「为什么下拉还在」）：
		// **"模式"这个概念随下拉一起消失**，显隐只剩两条规则，唯一落点仍是本方法：
		//   规则 A「按平台」：AMD 专属块（负压表 / 固定全核频率 / FMax / 保存读取 / 开机自动应用）
		//     只在 `amdTuning` 上出现；Intel 专属行（倍频）由**数据**（探针给不给 intel-ratio）决定；
		//   规则 B「按能力」：硬件不支持就**照旧显示、照旧置灰**并给出"不可用 + 原因"（用户要求，
		//     本机 R7 的 amd-co 就是这条：CanWrite=false，负压表整块灰着但看得见原因）。
		// ⚠️ 通用选项行**根本不需要平台判断** —— 行是按探针给的选项集合建的（AMD 探针只给 amd-*、
		//    Intel 探针只给 intel-*）⇒ 每行天然只出现在自己的平台上。
		// 布局：所有行都是根 TableLayoutPanel 的 AutoSize 行，控件 Visible=false 时行高自动收成 0、
		//    **不留空白带**（本页既有做法，见 `_guardStatus` / `_status`）。
		// ==========================================================================================
		foreach (OptionRow optionRow in _optionRowList)
		{
			// 每行按**自己那一条选项**的能力置灰 + 刷回读（标签 / 数值框 / 「应用」/ 回读 四件套）。
			optionRow.Apply.Enabled = idle && optionRow.Setting.CanWrite;
			optionRow.Input.Enabled = idle && optionRow.Setting.CanWrite;
			optionRow.Readback.Text = OptionRowReadbackText(optionRow.Setting);
		}
		// 一条通用行都没有（本机 AMD：amd-co 走负压表块、amd-clock 走频率块）⇒ 整个行宿主收起，
		// 那一行高度收成 0（⛔ 不留一条空行）。
		_optionRows.Visible = _optionRowList.Count > 0;
		// 负压表块（amd-co 那一条）= 只由"探针给不给 amd-co"决定显隐（下拉与"模式"都已删除）。
		//   ⚠️ 与改前**等价**：改前它 = coMode（下拉选中 amd-co），而 `ApplyProbe` 的默认选中项
		//      **优先取"有逐核回读值"的那一条**（本机 R7 就是 amd-co）⇒ 打开窗口第一眼同样是这一块。
		bool coBlock = _coOption != null;
		bool perCore = PerCoreEditable();
		_perCoreTitle.Visible = coBlock;
		_perCoreGrid.Visible = coBlock;
		_perCoreInfo.Visible = coBlock;
		// 负压表块自己的回读行（`_current`）= amd-co 那一条（含"不可用 + 原因"，与每行的回读同一套文案）。
		_current.Visible = coBlock;
		// 负压表的「应用」：只属于这一块（每核可编辑是其子集，走同一个按钮）。
		_applyButton.Visible = coBlock;
		// 2026-10-10 intel-fmax：`&& amdTuning` = 平台前提（非 AMD 上 coBlock 恒为假，这一道是显式兜底）。
		// ⚠️ 下面 `_autoApply.Visible` 在改前被赋值两次（前面一条 = coMode && amdTuning，后面一条 =
		//    amdTuning）——**后面那条覆盖了前面那条**，即实际生效的一直是"只看平台"。这里按**实际生效
		//    的行为**只留一条赋值（去掉那句死代码），行为逐字不变。
		_autoApply.Enabled = perCore;
		foreach (RNumericUpDown input in _perCoreInputs)
		{
			input.Enabled = perCore;
		}
		// 负压表的「应用」：该 SKU 能写 CO 才可用（每核可编辑是其子集，走的是同一个按钮）。
		// ==========================================================================================
		// 2026-10-10 flat-rows：判据回到**这一块自己的那一条选项**（`_coOption` = amd-co）。
		//   ⚠️ 与本页改前逐字同源（改前就是 `idle && _coOption != null && _coOption.CanWrite`）：
		//     中途那版"按当前选中项（任意 id）使能"是为"下拉 + Intel 通用通路"服务的，而 Intel 的
		//     三个选项现在**各有各的一行、各有各的「应用」**（通用行由 `BuildOptionRow` 建、倍频行是
		//     `_ratioRow`）⇒ 这颗按钮不再需要认识别的 id，也不会再出现"Intel 上点不动"的问题。
		// ⛔ 真正的写入守卫仍在 RuntimeCpuTuning.Apply（不看 UI：选项不存在 / CanWrite=false /
		//    越界 / 非整数都在发命令之前拒掉）。
		// ==========================================================================================
		_applyButton.Enabled = idle && _coOption != null && _coOption.CanWrite;
		// 2026-10-10 percorenolog（用户原话：「全核负压框删掉」）：`_coAllRow` 那一整块（"全核降压值 (CO)"
		// 标签 + `_coAll` 数值框 + 它的显隐/取值/写入）**已删除** ——
		//   * 这里原来算的 `allCoreOnly = CanWrite && !PerCoreWrite` 也随之删除；
		//   * CO 的唯一输入口是每核表（`_perCoreTitle/_perCoreGrid`），没有每核写命令的代号由
		//     `_perCoreInfo`（= `PerCoreInfoText`）如实说明，⛔ 不留空壳；
		//   * `cpu_co_all` 这个历史键仍被 SaveSettings 写 0（见 `SaveCoValues`），
		//     「读取」在没有每核写的代号上仍会用它走一次既有 `Apply("amd-co")`（兼容旧配置）。
		// 2026-10-05 uifix（用户要求①）：唯一的「重置」按钮的启用判定 = 两件旧事取并集（每核可编辑 /
		// 本会话有过变更）。两个都不成立（本机 R7：amd-co 不可写 + 无变更）-> 保持禁用，验收要求如此。
		// 2026-10-10 fmax-persist：再加上第三件 —— FMax 那边有需要恢复的东西（本会话写过 FMax，
		// 或记录到的原值与当前回读不同）。只读 / 不支持 FMax 的机器上这一项恒为 false，
		// 所以上面那句"本机 R7 保持禁用"的验收在 FMax 不可写时**一字未变**。
		_resetButton.Enabled = idle && (perCore || _hasChanges || RuntimeCpuTuning.Instance.FMaxRestorePending);
		// 2026-10-10 intel-fmax：「重置」= 清零全部核心 CO + 退出固定频率 + 把 FMax 恢复为原值，
		// **三件都是 AMD 专属** ⇒ 非 AMD 平台上整颗按钮不显示（`_coButtons` 里另一颗「应用」由
		// coMode 门控，非 AMD 上同样不显示；两颗都收起时 `_coButtons` 这一行同样收成 0 高）。
		// ⚠️ 它的 Enabled 判定一字未改（AMD 上仍与改前逐字同源），这里只多一个可见性前提。
		// 2026-10-10 intel（⑦B）：Intel 上**要显示**这颗按钮 —— 它的动作是 RuntimeCpuTuning.ResetAll，
		//   而 ResetAll 的 Intel 分支 = "写回探针捕获的基线"（domain 0/2 各一次，回读校验），
		//   也就是 Intel 侧**唯一**的恢复入口。Intel 的 CO/FMax 那几段在 ResetAll 里本来就会被
		//   `_fmax == null` / `_changed` 不含 amd-* 两条前提自动跳过，不会误发 SMU 命令。
		//   ⚠️ 只有"本会话真的动过 Intel 选项"（_hasChanges）时才可能亮 —— 与 AMD 侧同一把尺子。
		_resetButton.Visible = amdTuning || intelTuning;
		// 页面顶部说明行：文案里点了「CO 与 FMax / 每核 CO / 固定频率」，全是 AMD 措辞 ⇒
		// 非 AMD 平台换成本页在那种机器上**真正会发生的事**。
		// 2026-10-10 intel（⑦B）：Intel 与"未知厂商"要分开说 —— Intel 上是"有调节能力，但固件仍可能
		// 拒绝降压"（键名沿用作者 resx 里的 CpuTuneIntelReady，⛔ 不新造键）；
		// 未知厂商/非 x64 仍然用 CpuTuneScopeNonAmd（只读探测，不写任何硬件）。
		_scopeNotice.Text = Localized(amdTuning ? "CpuTuneScope" : (intelTuning ? "CpuTuneIntelReady" : "CpuTuneScopeNonAmd"));
		// ==========================================================================================
		// 频率块（用户原话：「AMD限制频率是FMax 英特尔是ratio 那是不是完全可以放一页里面
		//   不做下拉」+「FMax和全核频率也是重复的」）：**同一个位置，按平台换内容**。
		//   根 TLP 里按顺序是 固定全核频率(AMD) -> 倍频(Intel) -> FMax(AMD) -> 两条说明；
		//   每个平台只亮自己那几条，隐藏的行由 AutoSize 收成 0 高（⛔ 不留空行、⛔ 不置灰占版面）。
		//   * AMD「固定全核频率」：**去重后仍保留**（只读核对结论见文件头/CpuInfo.cs：这条只按代号判据
		//     放行、`CanWrite` 恒为 true；本机实测记录同样是 1000..4500 MHz CanWrite=true）⇒
		//     显隐判据 = 探针给出这条 **且** CanWrite（⛔ 不再跟"模式"走；不可写就整行不出现）；
		//   * Intel「倍频」：探针给不给 `intel-ratio` 决定 —— **混合架构不给 ⇒ 整行不出现**；
		//   * AMD「FMax」：平台 + 能力四态，一字未动（见下面那一段）。
		//   * 说明行 `_freqNote`：只有 AMD 且固定频率行真的出现时才显示（那时页面上同时有两条频率输入，
		//     必须说清"一个是加速上限、一个是锁频"）。
		// ==========================================================================================
		_clockRow.Visible = amdTuning && _clockOption != null && _clockOption.CanWrite;
		_clockApply.Enabled = idle && _clockOption != null && _clockOption.CanWrite;
		_clockReadback.Text = ((_clockOption == null) ? "" : ReadbackText(_clockOption));
		if (_clockOption != null)
		{
			_value.Minimum = _clockOption.Minimum;
			_value.Maximum = _clockOption.Maximum;
			if (!_clockFilled)
			{
				// 与改前逐字同源：`Current ?? 0` 再按该选项范围 clamp（固定频率没有独立回读，故落到下限）。
				_value.Value = Math.Clamp(decimal.Round(_clockOption.Current ?? 0m), _clockOption.Minimum, _clockOption.Maximum);
				_clockFilled = true;
			}
			else
			{
				_value.Value = Math.Clamp(_value.Value, _clockOption.Minimum, _clockOption.Maximum);
			}
			_value.Enabled = idle && _clockOption.CanWrite;
		}
		else
		{
			_value.Enabled = false;
		}
		// Intel 的频率行（倍频）：**这一行在不在，完全由探针数据决定**（混合架构上探针不产出
		// intel-ratio ⇒ `_ratioOption` 为 null ⇒ 整行 `Visible=false`）。
		_ratioRow.Visible = _ratioOption != null;
		_ratioApply.Enabled = idle && _ratioOption != null && _ratioOption.CanWrite;
		if (_ratioOption != null)
		{
			_ratio.Minimum = _ratioOption.Minimum;
			_ratio.Maximum = _ratioOption.Maximum;
			if (!_ratioFilled)
			{
				// 与 `_clockOption` 同一套：`Current ?? 0` 再按该选项范围 clamp（倍频的回读值就是 Current）。
				_ratio.Value = Math.Clamp(decimal.Round(_ratioOption.Current ?? 0m), _ratioOption.Minimum, _ratioOption.Maximum);
				_ratioFilled = true;
			}
			else
			{
				_ratio.Value = Math.Clamp(_ratio.Value, _ratioOption.Minimum, _ratioOption.Maximum);
			}
			_ratio.Enabled = idle && _ratioOption.CanWrite;
			_ratioReadback.Text = OptionRowReadbackText(_ratioOption);
		}
		else
		{
			_ratio.Enabled = false;
			_ratioReadback.Text = "";
		}
		// 频率块的"去重"说明：FMax（PBO 加速上限，MHz）与固定全核频率（把所有核心锁在这个频率）
		// 不是同一件事 —— 只在两条**同时**出现在页面上时才需要说，Intel / 隐藏了固定频率行时不占版面。
		_freqNote.Visible = amdTuning && _clockOption != null && _clockOption.CanWrite;
		// ==========================================================================================
		// 2026-10-10 fmax: FMax（PBO 最大频率）那一行。
		//   * 可见性**不跟模式走**（它一直是独立一行，与改前一致），只按**能力**启用/置灰；
		//   * 能力 = 探针给的 FMaxCapability（代号分类器的结果），**UI 自己不做任何判定**：
		//       ReadWrite / WriteNoReadback -> 数值框 + 「应用」可用；
		//       ReadOnly / None             -> 两个都禁用（ReadOnly 仍然显示回读值）；
		//   * 说明行 = 能力一句话 + 回读值（能读的代号一定有；读失败也明说，绝不用 0 假装）。
		// ==========================================================================================
		FMaxCapability fmaxCapability = _fmaxProbe?.Capability ?? FMaxCapability.None;
		bool fmaxWritable = idle && FMaxWritable();
		// 2026-10-10 intel-fmax（⭐ 本次修复的正题 · 用户原话「新bug 英特尔机型显示了Fmax」）：
		// FMax = **AMD 专属**（PBO / Precision Boost Overdrive 是 AMD SMU 邮箱功能；实现见
		// RyzenSmuService.ResolveFMax / GetBoostLimitFrequency / SetBoostLimitFrequencyAllCores），
		// 所以**整个 FMax 行**（行内标签 + 数值框 + 行内独立的「应用」按钮 = `_fmaxRow` 一个
		// FlowLayoutPanel 连同它的三个子控件）与它下面那行 FMax 说明（`_fmaxNote`）在
		// **非 AMD 平台上不显示** —— 用户原话就是"不该显示"，故取**隐藏**而不是"置灰+提示"。
		//   * 隐藏后行高由根 TableLayoutPanel 的 AutoSize 自动收成 0 ⇒ **不留空白带**（详见
		//     UpdateSelection 开头那段布局说明）；
		//   * AMD 上完全不变：下面的 Enabled / 回读值 / 说明文案 / tooltip 与改前逐字相同，
		//     FMax 的**四态**（ReadWrite / ReadOnly / WriteNoReadback / None）一个都没动。
		_fmaxRow.Visible = amdTuning;
		_fmaxNote.Visible = amdTuning;
		_fmaxApply.Enabled = fmaxWritable;
		_fmax.Enabled = fmaxWritable;
		if (_fmaxProbe is { Current: not null })
		{
			_fmax.Value = Math.Clamp(decimal.Round(_fmaxProbe.Current.Value), _fmax.Minimum, _fmax.Maximum);
		}
		_fmaxNote.Text = FMaxNoteText();
		// 只有"本机根本不支持 FMax / 连能力都没探到"才用危险色；ReadOnly 是**正常的受支持状态**
		// （用户决定"不支持平台只读取"），沿用改前那种普通说明色。
		_fmaxNote.ForeColor = (fmaxCapability == FMaxCapability.None) ? UiVisualStyle.Danger : UiVisualStyle.Muted;
		string fmaxTip = FMaxCapabilityLine();
		_tips.SetToolTip(_fmax, fmaxTip);
		_tips.SetToolTip(_fmaxApply, fmaxTip);
		// ==========================================================================================
		// 2026-10-10 fmax-persist：整页「保存 / 读取」的启用判定。
		//   * 与页面其它按钮同源：忙碌 / 测试夹具 / 审计模式一律置灰（审计截图也因此在两种状态下都
		//     拍得到，且**永远不会**在这里打开 PawnIO）；
		//   * 本机两侧都不可写（CO 不可写 + FMax 只读/不支持/还没探到）-> 置灰：没有可保存/可应用的东西，
		//     这正是"只读机器上连按钮都点不到"的那一条。
		// ==========================================================================================
		bool coWritable = _coOption is { CanWrite: true };
		bool snapshotIdle = idle && amdTuning && (coWritable || fmaxWritable);
		_snapshotSave.Enabled = snapshotIdle;
		_snapshotLoad.Enabled = snapshotIdle;
		// 2026-10-10 intel-fmax：非 AMD 平台**整行不显示** —— 「保存 / 读取」存取的就是
		// "CO 表 + FMax"（两条腿都是 AMD 专属：cpu_co_* / cpu_fmax_saved），Intel 上无可存可读之物。
		// ⚠️ 只收起这一行；按钮语义、落盘键、RunAsync 路径一字未改（AMD 上 Enabled 判定与改前同源）。
		_snapshotRow.Visible = amdTuning;
		// 「开机自动应用保存的 CO 值与 FMax」开关同理：它的文案与实现都只覆盖 CO + FMax，
		// 非 AMD 平台上没有任何东西可重放 ⇒ 一并收起（同样只 Visible，"开着"的存储值不做任何改写）。
		_autoApply.Visible = amdTuning;
		_autoApplyWarning.Visible = amdTuning;
		_tips.SetToolTip(_snapshotSave, Localized("CpuTuneSnapshotSaveTip"));
		_tips.SetToolTip(_snapshotLoad, Localized("CpuTuneSnapshotLoadTip"));
		UpdateGuardStatus();
		// 每核区的说明行跟着**负压表那条选项**（amd-co）走（与"模式"无关；"模式"已随下拉删除）。
		_perCoreInfo.Text = ((_coOption == null) ? "" : PerCoreInfoText(_coOption));
		_perCoreInfo.ForeColor = perCore ? UiVisualStyle.Muted : UiVisualStyle.Danger;
		// 负压表块自己的回读行（`_current`）：文案来源与每一行的回读**完全同一套**
		// （`OptionRowReadbackText` = 「回读: …」或「不可用: 原因 + 回读: …」），⛔ 不改任何判定。
		_current.Text = ((_coOption == null) ? "" : OptionRowReadbackText(_coOption));
	}

	// 2026-10-05 uifix（用户要求④）：保底机制的状态行。
	// 显示条件 = 「开机自动应用」开关为开 **且** 本次开机判定为"上次非正常关机"。
	//   * 开关是关的 -> 本来就不会自动写入，没什么可提示的（不打扰用户），只写日志；
	//   * 平时完全不占版面（这也是"不能变成开机弹窗轰炸"的一部分：窗口里最多一行，绝不弹窗）。
	// 判定读的是 CpuCoAutoApplyGuard.UiVerdict（本进程判定优先，其次读本次开机存下的结论）——
	// **只读，不消耗任何标记**，所以"打开窗口"这个动作本身不会影响判定。
	private void UpdateGuardStatus()
	{
		bool show = !Program.UiAuditMode
			&& _fixture == null
			&& RuntimeCpuTuning.PerCoreAutoApplyEnabled
			&& CpuCoAutoApplyGuard.UiVerdict == CpuCoShutdownVerdict.Abnormal;
		_guardStatus.Visible = show;
		if (!show)
		{
			return;
		}
		_guardStatus.Text = Localized("CpuTuneGuardSkipped");
		_tips.SetToolTip(_guardStatus, Localized("CpuTuneGuardSkippedTip"));
	}

	// 每核区说明行的文案（三种"不可用"的原因分得开，用户验收要求"不可用 + 原因"）。
	private string PerCoreInfoText(CpuTuningOption setting)
	{
		if (setting.Id != "amd-co")
		{
			return Localized("CpuTunePerCoreNotCurve");
		}
		if (!setting.CanWrite)
		{
			return Localized("CpuTunePerCoreBlocked") + ": " + UnavailableReason(setting);
		}
		if (!setting.PerCoreWrite)
		{
			return Localized("CpuTunePerCoreCodenameOnly");
		}
		return string.Format(Localized("CpuTunePerCoreBaseline"), BaselineText());
	}

	// ==========================================================================================
	// 2026-10-10 fmax: FMax 那一行的门控 / 文案（三个纯展示函数，便于单独核对）。
	// ==========================================================================================

	// FMax 的能力里是否含写路径（**只看能力**，不看待机/夹具状态 —— 说明行、确认文案、夹具判定都要用它）。
	private bool FMaxCapabilityWritable()
	{
		FMaxCapability capability = _fmaxProbe?.Capability ?? FMaxCapability.None;
		return capability == FMaxCapability.ReadWrite || capability == FMaxCapability.WriteNoReadback;
	}

	// FMax 能不能写。三条缺一不可：探针给出了能力、能力含写路径（ReadWrite / WriteNoReadback）、
	// 界面空闲（非忙碌 / 非测试夹具 / 非审计模式）。
	// ⚠️ 这是**展示层**的门控；真正的写入守卫在 RuntimeCpuTuning.ApplyFMax 里（那一层不看 UI）。
	private bool FMaxWritable()
	{
		if (_busy || _fixture != null || Program.UiAuditMode)
		{
			return false;
		}
		return FMaxCapabilityWritable();
	}

	// 「应用」：写 FMax + 回读校验。走的是既有 RunAsync（它会重跑一次 Probe，
	// 所以回读行会在写后刷新成新的实读值）。
	// 2026-10-10 fmax：成功文案要**分档**说清楚 —— RunAsync 只有"已校验 / 已接受"两种，
	// 而"已接受但回读没确认"（值不一致 / 读失败）必须显式说出来，不能混进"已接受"里蒙过去。
	private async Task ApplyFMaxAsync()
	{
		if (!FMaxWritable())
		{
			return;
		}
		decimal value = _fmax.Value;
		CpuTuningResult? result = await RunAsync(() => RuntimeCpuTuning.Instance.ApplyFMax(value), restore: false);
		if (result is not { Success: true, Verified: false })
		{
			return;
		}
		// ShimadaPeak：根本没有回读命令 -> 用"无法回读校验"那句（与说明行同一句）。
		// 其余（值不一致 / 回读失败）：用"写入被接受但未能确认"那句。
		string key = (_fmaxProbe?.Capability == FMaxCapability.WriteNoReadback) ? "CpuTuneFMaxNoReadback" : "CpuTuneFMaxUnverified";
		_status.Text = Localized(key) + Environment.NewLine + result.Detail;
	}

	// 能力那一句（只有一句，给 tooltip 用；完整说明见 FMaxNoteText）。
	private string FMaxCapabilityLine()
	{
		CpuFMaxProbe? fmax = _fmaxProbe;
		if (fmax == null)
		{
			// 连能力都没探到（缺驱动 / 非 AMD / 探针失败）：说**真正的原因**，不硬说"本机不支持 FMax"。
			return Localized(_probeStatusKey);
		}
		switch (fmax.Capability)
		{
		case FMaxCapability.ReadWrite:
			return Localized("CpuTuneFMaxWriteNote");
		case FMaxCapability.WriteNoReadback:
			return Localized("CpuTuneFMaxNoReadback");
		case FMaxCapability.ReadOnly:
			return Localized("CpuTuneFMaxReadOnly");
		default:
			return Localized("CpuTuneFMaxUnsupported");
		}
	}

	// FMax 说明行（能力一句 + 回读值 + **原值**）。
	//   * 能读的代号（ReadWrite / ReadOnly）一定带一行回读：读到就给值 + 原始 response[0]，
	//     读失败就明说读失败 —— **绝不用 0 假装读到**；
	//   * WriteNoReadback（ShimadaPeak）/ None：没有回读可言，只给能力说明；
	//   * 2026-10-10 fmax-persist：本机能写 FMax 时再加一行**原值**（cpu_fmax_baseline）——
	//     口径照实写成「上次读到的原值」，⛔ 绝不写成"出厂默认"（用户可能先用别的工具改过）。
	//     没有记录到就明说"未知"，因为「重置」在这种机器上**不会**猜一个值写进去。
	//     测试夹具 / 审计模式下不读真实配置（保证截图可复现），这一行不显示。
	private string FMaxNoteText()
	{
		CpuFMaxProbe? fmax = _fmaxProbe;
		List<string> lines = new List<string> { FMaxCapabilityLine() };
		if (fmax != null && (fmax.Capability == FMaxCapability.ReadWrite || fmax.Capability == FMaxCapability.ReadOnly))
		{
			lines.Add(fmax.Current.HasValue
				? string.Format(Localized("CpuTuneFMaxReadback"), $"{fmax.Current.Value:0}", $"0x{fmax.RawResponse:X8}")
				: Localized("CpuTuneFMaxReadFailed"));
		}
		if (_fixture == null && !Program.UiAuditMode && FMaxCapabilityWritable())
		{
			int? baseline = RuntimeCpuTuning.FMaxBaselineValue;
			lines.Add(baseline.HasValue
				? string.Format(Localized("CpuTuneFMaxBaseline"), $"{baseline.Value:0}")
				: Localized("CpuTuneFMaxBaselineUnknown"));
		}
		return string.Join(Environment.NewLine, lines);
	}

	// 2026-10-05 coread: "回读"行的文案（键名沿用既有 CpuTune* 体系，不硬编码中文）。
	//   读到同一个值且非 0 -> "回读: -30 CO（SMU 当前生效值，重启后失效）"
	//   读到 0             -> "回读: 0 CO（未检测到偏移；也可能是本次开机的默认值）"
	//   逐核不一致         -> "回读: 逐核不一致（-5 ~ 0 CO，共 8 核）"，明细在下方说明里
	//   平台无读命令       -> "回读: 不可回读（该平台没有回读命令）"
	//   有命令但读失败     -> "回读: 不可回读（读取失败）"
	// 其它选项（Intel 倍频/电压、amd-clock）保持原样：有 Current 就显示数值，没有就"不可回读"。
	internal static string ReadbackText(CpuTuningOption option)
	{
		string text = Localized("CpuTuneCurrent") + ": ";
		IReadOnlyList<int>? coreReadback = option.CoreReadback;
		if (coreReadback != null && coreReadback.Count > 0)
		{
			int first = coreReadback[0];
			int min = first;
			int max = first;
			bool same = true;
			foreach (int value in coreReadback)
			{
				if (value != first)
				{
					same = false;
				}
				if (value < min)
				{
					min = value;
				}
				if (value > max)
				{
					max = value;
				}
			}
			if (same)
			{
				return text + ((first == 0) ? Localized("CpuTuneReadbackZero") : string.Format(Localized("CpuTuneReadbackValue"), first));
			}
			return text + string.Format(Localized("CpuTuneReadbackMixed"), min, max, coreReadback.Count);
		}
		if (option.Current.HasValue)
		{
			return text + $"{option.Current.Value:F3} {option.Unit}";
		}
		return text + Localized(option.ReadbackNoteKey ?? "CpuTuneNoReadback");
	}

	private async Task ProbeAsync()
	{
		if (_busy)
		{
			return;
		}
		_busy = true;
		UpdateSelection();
		try
		{
			ApplyProbe(await Task.Run((Func<CpuTuningProbe>)RuntimeCpuTuning.Instance.Probe));
		}
		finally
		{
			_busy = false;
			UpdateSelection();
		}
	}

	private async Task InstallDriverAsync()
	{
		if (_busy || _fixture != null || Program.UiAuditMode)
		{
			return;
		}
		_busy = true;
		_installButton.Enabled = false;
		UpdateSelection();
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo("powershell.exe")
			{
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden
			};
			string[] array = new string[5]
			{
				"-NoProfile",
				"-ExecutionPolicy",
				"Bypass",
				"-File",
				Path.Combine(AppContext.BaseDirectory, "Install-PawnIO.ps1")
			};
			foreach (string item in array)
			{
				processStartInfo.ArgumentList.Add(item);
			}
			using Process process = Process.Start(processStartInfo) ?? throw new IOException("Driver installer could not start.");
			await process.WaitForExitAsync();
			if (process.ExitCode == 3010)
			{
				_status.Text = Localized("CpuTuneDriverRestart");
			}
			else if (process.ExitCode != 0)
			{
				_status.Text = Localized("CpuTuneFailed") + $" ({process.ExitCode})";
			}
			else
			{
				ApplyProbe(await Task.Run((Func<CpuTuningProbe>)RuntimeCpuTuning.Instance.Probe));
			}
		}
		catch (Exception ex)
		{
			_status.Text = ex.Message;
			Logger.WriteLine("PawnIO installation failed: " + ex);
		}
		finally
		{
			_busy = false;
			_installButton.Enabled = true;
			UpdateSelection();
		}
	}

	// 2026-10-05 percore-product: 返回值改为 `CpuTuningResult?` —— 调用方（应用）需要知道"SMU 是否
	// 真的接受了这条命令"才能决定要不要保存设置。null = 这次点击没有执行（忙碌/测试夹具/审计模式）。
	// 2026-10-05 uifix: 新增 successKey —— 「重置」走的是合并后的 ResetAll，成功文案要用
	// CpuTuneResetDone 而不是旧「恢复」的 CpuTuneRestored。
	private async Task<CpuTuningResult?> RunAsync(Func<CpuTuningResult> action, bool restore, string? successKey = null)
	{
		if (_busy || _fixture != null || Program.UiAuditMode)
		{
			return null;
		}
		_busy = true;
		decimal requested = _value.Value;
		UpdateSelection();
		try
		{
			CpuTuningResult result = await Task.Run(action);
			_hasChanges = RuntimeCpuTuning.Instance.HasChanges;
			ApplyProbe(await Task.Run((Func<CpuTuningProbe>)RuntimeCpuTuning.Instance.Probe));
			Label status = _status;
			string key;
			if (result.Success)
			{
				if (successKey != null)
				{
					key = successKey;
				}
				else if (restore)
				{
					key = "CpuTuneRestored";
				}
				else
				{
					key = (result.Verified ? "CpuTuneVerified" : "CpuTuneAccepted");
				}
			}
			else
			{
				key = "CpuTuneFailed";
			}
			status.Text = Localized(key) + ((result.Detail.Length == 0) ? "" : (Environment.NewLine + result.Detail));
			return result;
		}
		finally
		{
			_busy = false;
			UpdateSelection();
			if (!restore)
			{
				_value.Value = Math.Clamp(requested, _value.Minimum, _value.Maximum);
			}
		}
	}
}
