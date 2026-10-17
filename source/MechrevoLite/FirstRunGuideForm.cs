using System;
using System.Drawing;
using System.Windows.Forms;
using MechrevoLite.Gpu;
using MechrevoLite.Properties;
using MechrevoLite.UI;
using MechrevoLite.Usage;

namespace MechrevoLite;

public sealed class FirstRunGuideForm : RForm
{
	// 2026-10-05「核显模式显示策略」：第 4 步的回答。
	// 2026-10-09（igpugate）：键仍是 igpu_only_ui，但值域扩成 auto/show/hide（见 Gpu\IgpuOnlyUiMode.cs）——
	// 落盘规则改为：
	//   有   -> igpu_only_ui = show（用户明确说本机有这一档 = 与引导文案一致的"强制显示"）
	//   没有 -> igpu_only_ui = hide（强制隐藏）
	//   不确定 / 未选 / 直接关闭（X、Esc） -> igpu_only_ui = auto
	//        （fail-safe 从"写 show"改为"交给硬件判据"：auto 在**读不到**硬件结论时依然会退回旧的
	//         "先显示、等回答"行为，所以不会把能用的机器藏掉；而硬件明确说不支持时才会隐藏 ——
	//         这正是本批要的行为。写 show 会绕过硬件判据，等于让"不确定"变成"强制显示"。）
	// 无论哪种都置 igpu_only_asked = 1。
	private const string ChoiceYes = "yes";

	private const string ChoiceNo = "no";

	private const string ChoiceUnknown = "unknown";

	private RButton? _igpuOnlyYesButton;

	private RButton? _igpuOnlyNoButton;

	private RButton? _igpuOnlyUnknownButton;

	private string _igpuOnlyChoice = ChoiceUnknown;

	/// <summary>
	/// ⭐ 2026-10-17（用户要求 · 宝龙达没有核显模式）：「首次提示的核显模式只给同方开放」
	/// ⇒ 宝龙达机型上第 4 步（「你的电脑有「集显模式」这一档吗?」）**整步不建**。
	/// <para>⚠️ 三处联动，缺一不可：① 不建第 4 步控件（RowCount 跟着减 1）②
	/// <see cref="PersistIgpuOnlyChoice" /> 直接 return（不写 <c>igpu_only_ui</c> / <c>igpu_only_asked</c>）
	/// ③ 判据只对**完整引导**生效（<paramref name="igpuOnlyStepOnly" /> 的单问模式在宝龙达下
	/// 压根不会被调用，见 SettingsForm.ShowIgpuOnlyGuideIfNeeded 的宝龙达排除）。</para>
	/// <para>⚠️ 能力层依据：宝龙达 WMI 功能号 9 <c>GpuMode</c> 只有 <c>Hybrid=0</c> / <c>Discrete=1</c>，
	/// **没有 iGPU-only 这一档**。⛔ 非宝龙达逐位不变（本字段恒 false）。</para>
	/// </summary>
	private readonly bool _skipIgpuOnlyStep;

	// 2026-10-09（telemetryguide）：第 5 步「匿名使用统计与匿名日志」的勾选框。
	// 它与「设置 → 隐私与反馈」里那个开关**读写同一个 config 键** usage_telemetry（缺省 1），
	// 初始勾选态取自同一个读取点 UsageTelemetry.Enabled —— 所以两边永远显示同一个值，不会各写各的。
	// _telemetryInitial 记住打开引导时的值：关闭时只有"用户确实改过"才落盘，
	// 没动过（含 X / Esc / 直接关掉引导）一个字节都不写，既不会偷偷开启也不会偷偷关闭。
	private RCheckBox? _telemetryCheck;

	private bool _telemetryInitial;

	// 2026-10-05：单问模式（igpuOnlyStepOnly）的紧凑客户区宽度。
	// 这里写的是 96 基线逻辑像素，随后由 ResponsiveLayout.ScaleFrom96 按 DPI 统一缩放，
	// 与文件里其它尺寸（Padding/RowStyle 等）同一套写法。
	private const int CompactWidth = 420;

	// 2026-10-05（第二次收紧）：单问模式的高度**不再写死**。
	// 客户区高度 = 「副标题 + 步骤块 + 按钮行」三段内容的实测高度之和，由
	// ApplyCompactIgpuOnlyLayout() 收口（中排是 Percent 100 的承载行，只要客户区高
	// 等于三段之和，中排就刚好等于步骤块高度，中间不留空白）。
	// 这个常量只作为「量不到内容时的初始/兜底高度」，目标量级 175 逻辑像素。
	private const int CompactHeight = 175;

	private Control? _compactHeader;

	private Control? _compactSteps;

	// 中排（Percent 100 的承载行）：单问模式下它的客户区高减去步骤块高就是那块纵向空白。
	private Control? _compactBody;

	private Control? _compactFooter;

	// 单问模式「贴合内容高度」的重入/幂等开关（ApplyCompactIgpuOnlyLayout 会触发布局，
	// 布局又回调收口，用它避免递归）。
	private bool _compactFitting;

	public FirstRunGuideForm()
		: this(igpuOnlyStepOnly: false)
	{
	}

	/// <param name="igpuOnlyStepOnly">
	/// true = 只显示「核显模式」这一个问题（用于升级用户：onboarding_version 已是 1，
	/// 看不到完整首次引导，但仍需问一次）。
	/// </param>
	public FirstRunGuideForm(bool igpuOnlyStepOnly)
	{
		// ⭐⭐ 2026-10-17（用户要求 · 宝龙达没有核显模式）：
		//   完整引导里**跳过第 4 步**（「你的电脑有「集显模式」这一档吗?」）。
		//   ⚠️ 单问模式（igpuOnlyStepOnly=true）不适用 —— 那个窗口存在的唯一目的就是问这一问，
		//     而它在宝龙达下压根不会被打开（SettingsForm.ShowIgpuOnlyGuideIfNeeded 已挡掉）。
		//   ⚠️ Program.hw 为 null（后端未就绪）时按非宝龙达处理（保守：维持原行为）。
		_skipIgpuOnlyStep = !igpuOnlyStepOnly && (Program.hw?.UsesBaolongda ?? false);
		Text = (igpuOnlyStepOnly ? Strings.IgpuOnlyGuideTitle : Strings.FirstRun);
		StartPosition = FormStartPosition.CenterParent;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		// 2026-10-05：单问模式原先沿用完整引导的 580×290，弹出来占了近半屏。
		// 现在按「紧凑单问小对话框」定尺寸：420 宽逻辑像素（96 基线，随 DPI 缩放），
		// 高由 ApplyCompactIgpuOnlyLayout() 按实测内容收口（约 175~180）。
		// 完整引导（4 步）尺寸保持原样，不受影响。
		ClientSize = (igpuOnlyStepOnly ? new Size(CompactWidth, CompactHeight) : new Size(580, 500));
		MinimumSize = (igpuOnlyStepOnly ? new Size(CompactWidth, CompactHeight) : new Size(540, 430));
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		InitTheme(setDPI: true);
		// 关闭（含 X / Esc / 点按钮）时统一落盘：对话框自身负责，两个触发入口不必重复实现。
		FormClosed += (object? _, FormClosedEventArgs _) =>
		{
			PersistIgpuOnlyChoice();
			// 2026-10-09（telemetryguide）：第 5 步的选择也在关闭时统一落盘（与「核显模式」那一问同一时机，
			// 但读写的键完全独立，互不影响）。单问模式（igpuOnlyStepOnly）没有这一行控件 -> 直接返回。
			PersistTelemetryChoice();
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		// 中排是承载行：完整引导让它吃掉剩余高度并由 AutoScroll 兜底（行为保持不变）；
		// 单问模式下 ApplyCompactIgpuOnlyLayout() 会把客户区高度设成「三段内容实测之和」，
		// 于是这行就恰好等于步骤块高度 —— 不会再空出 44 像素的空白。
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, igpuOnlyStepOnly ? 46f : 58f));
		Controls.Add(tableLayoutPanel);
		// 标题区：完整引导保留「开始使用 L-Mechrevo」大标题 + 引言；
		// 单问模式窗口标题已经是「核显模式显示设置」，这里去掉重复的大标题，
		// 只留一行极小的辅助说明（回答后仍可修改），把高度让给问题本身。
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			ColumnCount = 1,
			RowCount = (igpuOnlyStepOnly ? 1 : 2),
			Padding = (igpuOnlyStepOnly ? new Padding(16, 8, 16, 2) : new Padding(24, 16, 24, 8)),
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Surface
		};
		if (igpuOnlyStepOnly)
		{
			tableLayoutPanel2.Controls.Add(new Label
			{
				Text = Strings.IgpuOnlyGuideIntro,
				AutoSize = true,
				Font = UiVisualStyle.Font(8.5f),
				ForeColor = UiVisualStyle.Muted,
				Margin = Padding.Empty
			}, 0, 0);
		}
		else
		{
			tableLayoutPanel2.Controls.Add(new Label
			{
				Text = Strings.FirstRunWelcome,
				AutoSize = true,
				Font = UiVisualStyle.Font(15f, FontStyle.Bold),
				ForeColor = UiVisualStyle.Text,
				Margin = Padding.Empty
			}, 0, 0);
			tableLayoutPanel2.Controls.Add(new Label
			{
				Text = Strings.FirstRunIntro,
				AutoSize = true,
				Font = UiVisualStyle.Font(9f),
				ForeColor = UiVisualStyle.Muted,
				Margin = new Padding(0, 4, 0, 0)
			}, 0, 1);
		}
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			// 单问模式：内容由 ApplyCompactIgpuOnlyLayout() 保证「刚好放得下」，
			// 不需要滚动（AutoScroll 留着反而可能因 1~2 像素误差冒出滚动条）。
			AutoScroll = !igpuOnlyStepOnly,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = (igpuOnlyStepOnly ? 1 : (_skipIgpuOnlyStep ? 4 : 5)),
			Padding = (igpuOnlyStepOnly ? new Padding(12, 4, 12, 4) : new Padding(16, 12, 16, 12)),
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		int stepRow = 0;
		if (!igpuOnlyStepOnly)
		{
			tableLayoutPanel3.Controls.Add(CreateStep("1", Strings.FirstRunStep1Title, Strings.FirstRunStep1Body), 0, stepRow++);
			tableLayoutPanel3.Controls.Add(CreateStep("2", Strings.FirstRunStep2Title, Strings.FirstRunStep2Body), 0, stepRow++);
			tableLayoutPanel3.Controls.Add(CreateStep("!", Strings.FirstRunStep3Title, Strings.FirstRunStep3Body, warning: true), 0, stepRow++);
		}
		// 第 4 步：问法必须「具体可查」，不用「支持纯核显模式吗」这类术语（§3 ①）
		// 单问模式用短正文（FirstRunStep4BodyShort，仍含「BIOS 或官方控制台」与
		// 「集显模式 / 核显模式 / iGPU Only」关键词）；完整引导原文一字不动。
		// ⭐ 2026-10-17：宝龙达机型**整步不建**（_skipIgpuOnlyStep，见字段注释）——
		//   没有这一档的机器不该被问，也不该把答案写进 igpu_only_ui / igpu_only_asked。
		if (!_skipIgpuOnlyStep)
		{
			tableLayoutPanel3.Controls.Add(CreateStep("?", Strings.FirstRunStep4Title, igpuOnlyStepOnly ? Strings.FirstRunStep4BodyShort : Strings.FirstRunStep4Body, warning: false, CreateIgpuOnlyChoiceRow(igpuOnlyStepOnly), compact: igpuOnlyStepOnly), 0, stepRow);
		}
		// 2026-10-09（telemetryguide）：第 5 步 —— 「匿名使用统计与匿名日志」（用户要求"这个开关应该在引导时候说明"）。
		// 只加在**完整**首次引导里：单问模式是给升级用户补问「核显模式」那一问的，
		// 与它无关，也不加（那一问的控件、语义与判据一字未动，双弹修复逻辑也不受影响）。
		if (!igpuOnlyStepOnly)
		{
			// ⭐ 2026-10-17：第 4 步被跳过时它没占用 stepRow，第 5 步直接落在当前行；
			//   否则先推进一行（第 4 步占用了 stepRow 那一行）。
			if (!_skipIgpuOnlyStep)
			{
				stepRow++;
			}
			tableLayoutPanel3.Controls.Add(CreateStep("5", Strings.FirstRunStep5Title, Strings.FirstRunStep5Body, warning: false, CreateTelemetryChoiceRow()), 0, stepRow);
		}
		panel.Controls.Add(tableLayoutPanel3);
		_compactSteps = (igpuOnlyStepOnly ? tableLayoutPanel3 : null);
		_compactBody = (igpuOnlyStepOnly ? panel : null);
		tableLayoutPanel.Controls.Add(panel, 0, 1);
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			Padding = (igpuOnlyStepOnly ? new Padding(12, 6, 12, 6) : new Padding(16, 8, 16, 8)),
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Surface
		};
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Button button = CreateButton(Strings.Later);
		button.DialogResult = DialogResult.Cancel;
		Button button2 = CreateButton(igpuOnlyStepOnly ? Strings.Ok : Strings.GoToSystemPage);
		button2.DialogResult = DialogResult.OK;
		UiVisualStyle.ApplyPrimaryButton(button2);
		tableLayoutPanel4.Controls.Add(button, 0, 0);
		tableLayoutPanel4.Controls.Add(button2, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel4, 0, 2);
		AcceptButton = button2;
		CancelButton = button;
		UiVisualStyle.ApplyWindow(this);
		tableLayoutPanel2.BackColor = UiVisualStyle.Surface;
		tableLayoutPanel4.BackColor = UiVisualStyle.Surface;
		UiVisualStyle.ApplyPrimaryButton(button2);
		ResponsiveLayout.ScaleFrom96(this, this);
		if (igpuOnlyStepOnly)
		{
			// 单问模式：尺寸收口必须放在 DPI 缩放之后（与 DonateForm 同一套路：
			// 先缩放再按实测内容定尺寸），否则量到的是 96 基线的旧值。
			_compactHeader = tableLayoutPanel2;
			_compactFooter = tableLayoutPanel4;
			ApplyCompactIgpuOnlyLayout();
			// 字体度量（GDI+ 按设备 DPI 换算点值）与正文折行在窗体创建之后还可能变一次
			// （高 DPI 缩放、语言/主题字体切换）。这里在任何一次布局之后重新贴合一遍，
			// 保证「客户区高 = 内容高」，而不是只在构造时量一次的近似值。
			// 幂等：高度已经贴合时 CompactRefitOnLayout() 直接返回，不会反复触发布局。
			Layout += CompactRefitOnLayout;
		}
	}

	/// <summary>布局后重新贴合（幂等：已经贴合就什么都不做）。</summary>
	private void CompactRefitOnLayout(object? sender, LayoutEventArgs e)
	{
		if (_compactFitting || _compactBody == null || _compactSteps == null || _compactHeader == null || _compactFooter == null)
		{
			return;
		}
		if (CompactContentHeight() == ClientSize.Height)
		{
			return;
		}
		ApplyCompactIgpuOnlyLayout();
	}

	/// <summary>
	/// 2026-10-05（第二次收紧）：单问模式的尺寸收口（只作用于 igpuOnlyStepOnly，完整引导不走这里）。
	/// 目标客户区 = 紧凑宽度 × 「副标题 + 步骤块 + 按钮行」三段内容的**实测高度之和**：
	/// 中排是 Percent 100 的承载行，客户区高度一旦等于三段之和，中排就恰好等于步骤块高度，
	/// 中间不再有空白，底部按钮行也紧贴内容。
	/// 旧实现写的是 Max(客户区现有高, 内容高)，而客户区初始高固定 230 逻辑像素，
	/// 于是中排永远空出「230 - 内容高」（100% DPI 实测 44 逻辑像素）—— 就是用户看到的那块空白。
	/// 折行（正文宽度变了）与 DPI 缩放会反过来影响高度，所以循环收敛（最多 6 次），
	/// 收敛后再兜底一次：宁可长一点也不裁切正文。
	/// </summary>
	private void ApplyCompactIgpuOnlyLayout()
	{
		if (_compactFitting)
		{
			return;
		}
		_compactFitting = true;
		try
		{
			ApplyCompactIgpuOnlySize();
		}
		finally
		{
			_compactFitting = false;
		}
	}

	private void ApplyCompactIgpuOnlySize()
	{
		// 先解除构造时设的 MinimumSize(420×175)：否则收口过程中 ClientSize 会被它顶住，
		// 高度压不到「刚好等于内容」（高 DPI 下会残留几像素空白）。收口结束后再重设。
		MinimumSize = Size.Empty;
		int width = Math.Max(ClientSize.Width, ResponsiveLayout.LogicalToDevice(this, CompactWidth));
		if (ClientSize.Width != width)
		{
			ClientSize = new Size(width, ClientSize.Height);
		}
		int height = Math.Max(ClientSize.Height, ResponsiveLayout.LogicalToDevice(this, CompactHeight));
		for (int pass = 0; pass < 6; pass++)
		{
			ResponsiveLayout.PerformLayoutTree(this);
			int needed = CompactContentHeight();
			if (needed <= 0 || needed == height)
			{
				break;
			}
			height = needed;
			ClientSize = new Size(width, height);
		}
		ResponsiveLayout.PerformLayoutTree(this);
		int settled = CompactContentHeight();
		if (settled > height)
		{
			height = settled;
			ClientSize = new Size(width, height);
			ResponsiveLayout.PerformLayoutTree(this);
		}
		int frameWidth = Math.Max(0, Width - ClientSize.Width);
		int frameHeight = Math.Max(0, Height - ClientSize.Height);
		MinimumSize = new Size(width + frameWidth, height + frameHeight);
		MaximumSize = new Size(width + frameWidth + width / 2, height + frameHeight + height / 2);
	}

	/// <summary>单问模式三段内容的实测高度之和（副标题 + 步骤块 + 按钮行）。</summary>
	private int CompactContentHeight()
	{
		return (_compactHeader?.Height ?? 0) + (_compactSteps?.Height ?? 0) + (_compactFooter?.Height ?? 0);
	}

	/// <summary>「有 / 没有 / 不确定」三选一（本工程既有风格：RButton 组 + Activated 单选）。</summary>
	private Control CreateIgpuOnlyChoiceRow(bool compact = false)
	{
		int rowHeight = (compact ? 26 : 30);
		BufferedTableLayoutPanel row = new BufferedTableLayoutPanel
		{
			Name = "igpuOnlyChoiceRow",
			ColumnCount = 3,
			RowCount = 1,
			Height = rowHeight,
			MinimumSize = new Size(0, rowHeight),
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			Margin = new Padding(0, compact ? 4 : 8, 0, 0),
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
		row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
		row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
		row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		_igpuOnlyYesButton = CreateChoiceButton(Strings.FirstRunStep4Yes, ChoiceYes);
		_igpuOnlyNoButton = CreateChoiceButton(Strings.FirstRunStep4No, ChoiceNo);
		_igpuOnlyUnknownButton = CreateChoiceButton(Strings.FirstRunStep4Unknown, ChoiceUnknown);
		// 本工程既有的「单选组」做法：分段按钮 + Activated 高亮
		UiVisualStyle.ApplySegmentGroup(_igpuOnlyYesButton, _igpuOnlyNoButton, _igpuOnlyUnknownButton);
		row.Controls.Add(_igpuOnlyYesButton, 0, 0);
		row.Controls.Add(_igpuOnlyNoButton, 1, 0);
		row.Controls.Add(_igpuOnlyUnknownButton, 2, 0);
		// 默认选中「不确定」= 保持显示（fail-safe，§3 ②）
		SetIgpuOnlyChoice(ChoiceUnknown);
		return row;
	}

	/// <summary>
	/// 2026-10-09（telemetryguide）：第 5 步的「匿名使用统计与匿名日志」勾选行。
	/// 与「设置 → 隐私与反馈」里那个开关是**同一个开关**：控件文案直接复用设置页的
	/// Strings.UsageTelemetryToggle，初始勾选态取自 UsageTelemetry.Enabled
	/// （= AppConfig["usage_telemetry"]，缺省 1）—— 与设置对话框的 SyncTelemetryCheck() 是同一个读取点，
	/// 所以两边显示的永远是同一个值。落盘也走同一个写入口 UsageTelemetry.SetEnabled()
	/// （见 PersistTelemetryChoice），不存在两套写法。
	/// </summary>
	private Control CreateTelemetryChoiceRow()
	{
		_telemetryInitial = UsageTelemetry.Enabled;
		_telemetryCheck = new RCheckBox
		{
			Name = "checkFirstRunTelemetry",
			Text = Strings.UsageTelemetryToggle,
			Checked = _telemetryInitial,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			BackColor = UiVisualStyle.Window,
			ForeColor = UiVisualStyle.Text,
			Margin = new Padding(0, 4, 0, 0),
			AccessibleName = Strings.UsageTelemetryToggle,
			AccessibleDescription = Strings.UsageTelemetryTip
		};
		return _telemetryCheck;
	}

	/// <summary>
	/// 2026-10-09（telemetryguide）：关闭引导时落盘「匿名使用统计与匿名日志」这一项。
	/// 规则（对应用户要求「不选 / 直接关掉引导 ⇒ 保持默认，不要偷偷开启」）：
	///   * 单问模式（没有这个控件）→ 什么都不做；
	///   * 审计/截图模式 → 不写配置（与 PersistIgpuOnlyChoice 同一保护）；
	///   * 勾选态与打开引导时**一样**（用户没动过，含 X / Esc / 「稍后」）→ 一个字节都不写，保持现状与默认值；
	///   * 只有用户真的改了，才调用 UsageTelemetry.SetEnabled() 写 usage_telemetry（设置页那个开关用的就是它）。
	/// </summary>
	private void PersistTelemetryChoice()
	{
		if (Program.UiAuditMode || _telemetryCheck == null)
		{
			return;
		}
		if (_telemetryCheck.Checked == _telemetryInitial)
		{
			return;
		}
		UsageTelemetry.SetEnabled(_telemetryCheck.Checked);
		AppConfig.Flush();
		Logger.WriteLine($"First-run guide usage telemetry choice: usage_telemetry={(_telemetryCheck.Checked ? 1 : 0)} (same key as the Settings switch)");
	}

	private RButton CreateChoiceButton(string text, string choice)
	{
		RButton button = new RButton
		{
			Name = "buttonIgpuOnly" + choice,
			Text = text,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleCenter,
			Cursor = Cursors.Hand,
			Font = UiVisualStyle.Font(9f),
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			AccessibleName = text
		};
		button.Click += (object? _, EventArgs _) =>
		{
			SetIgpuOnlyChoice(choice);
		};
		return button;
	}

	private void SetIgpuOnlyChoice(string choice)
	{
		_igpuOnlyChoice = choice;
		if (_igpuOnlyYesButton != null)
		{
			_igpuOnlyYesButton.Activated = string.Equals(choice, ChoiceYes, StringComparison.Ordinal);
		}
		if (_igpuOnlyNoButton != null)
		{
			_igpuOnlyNoButton.Activated = string.Equals(choice, ChoiceNo, StringComparison.Ordinal);
		}
		if (_igpuOnlyUnknownButton != null)
		{
			_igpuOnlyUnknownButton.Activated = string.Equals(choice, ChoiceUnknown, StringComparison.Ordinal);
		}
	}

	private void PersistIgpuOnlyChoice()
	{
		// ⭐⭐ 2026-10-17（宝龙达没有核显模式）：第 4 步整步没建 ⇒ 这一问不存在 ⇒ **绝不落盘**。
		//   ⚠️ 这不是"写了再忽略"，而是**根本不写**：若写了 `igpu_only_asked=1`，
		//     用户以后换到同方机器就永远不再问这一问（隐性数据损坏）。
		//   ⚠️ `igpu_only_ui` 同理不写 —— 宝龙达没有这一档，写 show/hide 都会误导设置页判据。
		//   ⚠️ 非宝龙达逐位不变：_skipIgpuOnlyStep 恒 false，走下面的原逻辑。
		if (_skipIgpuOnlyStep)
		{
			return;
		}
		// 审计/截图模式（UiAuditRunner 也会 new 本窗体）不得写配置
		if (Program.UiAuditMode)
		{
			return;
		}
		// 2026-10-09（igpugate）：三态取值；必须走 IgpuOnlyUiMode.Set —— 它会同时置迁移标记，
		// 否则刚写的 show 会被当成老值（老装机语义）按 auto 解读。
		string ui = (string.Equals(_igpuOnlyChoice, ChoiceNo, StringComparison.Ordinal) ? IgpuOnlyUiMode.Hide : (string.Equals(_igpuOnlyChoice, ChoiceYes, StringComparison.Ordinal) ? IgpuOnlyUiMode.Show : IgpuOnlyUiMode.Auto));
		IgpuOnlyUiMode.Set(ui);
		AppConfig.Set("igpu_only_asked", 1);
		// 2026-10-06 D-1：顺便记下这句话有多确定（落盘规则与键名一字未动，只是多记一个态度）。
		// 「有 / 没有」= 明确表态，不再需要猜；「不确定」/未选/X、Esc = 未表态 —— 那样的机器上
		// 按钮会带一句「可试一次」的提示，并在用户真试过一次后记住结果（见 Gpu\IgpuOnlyAttempt.cs）。
		if (string.Equals(_igpuOnlyChoice, ChoiceYes, StringComparison.Ordinal))
		{
			IgpuOnlyAttempt.RememberAnswer(show: true);
		}
		else if (string.Equals(_igpuOnlyChoice, ChoiceNo, StringComparison.Ordinal))
		{
			IgpuOnlyAttempt.RememberAnswer(show: false);
		}
		else
		{
			IgpuOnlyAttempt.RememberUnknownAnswer();
		}
		AppConfig.Flush();
		Logger.WriteLine($"iGPU-only UI guide answer={_igpuOnlyChoice} igpu_only_ui={ui} igpu_only_asked=1");
	}

	private static Control CreateStep(string marker, string title, string description, bool warning = false, Control? extra = null, bool compact = false)
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Top;
		tableLayoutPanel.AutoSize = true;
		tableLayoutPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
		tableLayoutPanel.ColumnCount = 2;
		tableLayoutPanel.RowCount = 2;
		tableLayoutPanel.Margin = new Padding(0, 0, 0, compact ? 4 : 8);
		tableLayoutPanel.Padding = (compact ? new Padding(4) : new Padding(4, 8, 4, 8));
		tableLayoutPanel.BackColor = UiVisualStyle.Window;
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Label control = new Label
		{
			Text = marker,
			AutoSize = false,
			Size = (compact ? new Size(24, 24) : new Size(28, 28)),
			TextAlign = ContentAlignment.MiddleCenter,
			Font = UiVisualStyle.Font(9.5f, FontStyle.Bold),
			ForeColor = (warning ? UiVisualStyle.Danger : UiVisualStyle.AccentText),
			BackColor = (warning ? UiVisualStyle.SurfaceRaised : UiVisualStyle.Accent),
			Margin = new Padding(0, 0, 8, 0)
		};
		tableLayoutPanel.SetRowSpan(control, 2);
		tableLayoutPanel.Controls.Add(control, 0, 0);
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = title,
			AutoSize = true,
			Dock = DockStyle.Fill,
			Font = UiVisualStyle.Font(9.5f, FontStyle.Bold),
			ForeColor = (warning ? UiVisualStyle.Danger : UiVisualStyle.Text),
			Margin = Padding.Empty
		}, 1, 0);
		Label desc = new Label
		{
			Text = description,
			AutoSize = false,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			Font = UiVisualStyle.Font(8.5f),
			ForeColor = UiVisualStyle.Muted,
			Margin = new Padding(0, compact ? 2 : 4, 0, 0)
		};
		desc.Resize += (object? _, EventArgs _) =>
		{
			ReflowDescription();
		};
		desc.FontChanged += (object? _, EventArgs _) =>
		{
			ReflowDescription();
		};
		tableLayoutPanel.Controls.Add(desc, 1, 1);
		if (extra != null)
		{
			tableLayoutPanel.RowCount = 3;
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutPanel.Controls.Add(extra, 1, 2);
		}
		return tableLayoutPanel;
		void ReflowDescription()
		{
			int width = Math.Max(1, desc.Width - desc.Padding.Horizontal);
			desc.Height = TextRenderer.MeasureText(description, desc.Font, new Size(width, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak).Height;
		}
	}

	private static Button CreateButton(string text)
	{
		return new RButton
		{
			Text = text,
			Dock = DockStyle.Fill,
			Margin = new Padding(4, 0, 4, 0),
			Cursor = Cursors.Hand,
			BorderRadius = 4,
			Secondary = true
		};
	}
}
