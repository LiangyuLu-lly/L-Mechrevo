using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MechrevoLite.Properties;
using MechrevoLite.UI;

namespace MechrevoLite;

/// <summary>
/// 2026-10-11（用户拍板「点「＋」可以自定义前面几个磁贴 —— 一个选择面板/弹窗，从可选开关里挑」）：
/// 快捷磁贴的自定义面板。**模态小窗**形态（用户给的两个选项之一「选择面板/弹窗」，取弹窗：
/// 有明确的「确定/取消」，取消不落任何键；就地下拉那种"点一下就改"没有还原路径）。
///
/// 口径：
/// <list type="bullet">
/// <item><b>可选项来源</b>＝「更多开关」的**全部键去掉「睡眠省电」组**（由调用方
/// <c>SettingsForm.QuickTileCandidateKeys()</c> 生成并排序，本窗体不认识任何具体键名）。</item>
/// <item><b>能力过滤（2026-10-18 起：不支持的项直接隐藏，⛔ 不再灰显）</b>：调用方给出的
/// <c>supported</c> 就是**能力位唯一来源**（<c>SettingsForm.QuickTileSupported</c> =
/// <c>hw.SupportsQuickSwitch</c> ∥ <c>StaticQuick</c>；宝龙达 <c>fanboost</c> 的
/// <c>FanCurveVerified</c> 闸门也在 <c>SupportsQuickSwitch</c> 里）⇒ 本窗体**只给 supported 项建行**，
/// 不支持的项一个控件都不建。⚠️ 已保存选择里若含不支持项 ⇒ 它落不到任何行 ⇒ 确定时输出（= 落盘）的
/// 只有可用项（静默剔除，⛔ 不弹窗）。若全部不支持 ⇒ 列表只显示一行说明（⛔ 不灰显、⛔ 不空白）。</item>
/// <item><b>最多 <see cref="MaxSlots" /> 格</b>（= 磁贴行的动作格上限）：勾满之后再勾会被拒绝并提示，
/// ⛔ 不会静默丢掉用户的勾选。</item>
/// <item>2026-10-12（用户原话「快捷开关最少 0 个（全部不选就只显示自定义）」）：**允许 0 选** ——
/// 确定后由调用方写入**显式空集哨兵**（<c>SettingsForm.QuickTilesEmptySentinel</c>），
/// 磁贴行只剩末位那颗「自定义」；⛔ 这里不再把"一个都没勾"当成错误拦下。</item>
/// <item>2026-10-12（用户原话「还有希望可以自行调整顺序」）：**排序**＝每一行右侧两枚小按钮
/// （▲ 上移 / ▼ 下移，首位 ▲、末位 ▼ 自动置灰）。之所以选"上/下移按钮"而不是拖拽：
/// 本面板是<b>模态小窗 + 纵向滚动条</b>（<see cref="Panel" /> 里 AutoScroll），拖拽在这个形态下
/// 需要额外处理自动滚动、命中高亮与"拖到面板外"的边界，且键盘/无障碍没有等价物；
/// 上下移按钮与既有 <c>RButton</c> 视觉同一套（⛔ 未新造控件、未新造颜色），点击一步一移、可预期、可键盘操作。
/// 初始顺序 = <paramref name="selected" /> 里的**存储顺序**在前（这正是上次确定的磁贴顺序），
/// 其余未选中的按可选项顺序追加在后。</item>
/// <item>2026-10-12（用户原话「这个顺序的问题还没改吗」· 用户选定「已勾选的始终连续置顶（未勾选不能插到中间）」）：
/// **分组** —— 列表被切成**两段连续区块**：上面是"已勾选块"（块内顺序 = 用户调出来的相对顺序），
/// 下面全是未勾选行，⛔ **未勾选行不可能出现在两行已勾选之间**。
/// <list type="bullet">
/// <item><b>▲/▼ 只在各自组内移动</b>（<see cref="MoveRow" />）：到组边界就停，按钮同时置灰
/// （见 <see cref="RefreshReorderButtons" />）。</item>
/// <item><b>勾选</b> ⇒ 该行**追加到已勾选块末尾**；<b>取消勾选</b> ⇒ 该行**追加到未勾选组末尾**
/// （未勾选组为空时 = 整表末位）。两者都由 <see cref="ApplyGroupOrder" /> 做**稳定分区**完成，
/// 组内其它行的相对顺序一字不动。</item>
/// <item><b>持久化只写"被勾选"的键</b>（见 <see cref="OnConfirm" /> / <c>SettingsForm.SaveQuickTileKeys</c>）⇒
/// 未勾选行之间的顺序**不参与保存**，重开面板时回到"可选项顺序"（既有设计，见上面的初始顺序规则）。</item>
/// </list></item>
/// <item>⛔ 只读 <paramref name="selected" />、⛔ 不写任何配置 —— 持久化由调用方在
/// <c>DialogResult.OK</c> 之后写 <c>AppConfig["perf_quick_tiles"]</c>（逗号分隔，**顺序即磁贴顺序**）。</item>
/// </list>
/// </summary>
internal sealed class QuickTilePickerForm : RForm
{
	/// <summary>每一行的复选框（下标 = **当前视觉顺序**，排序时与 <see cref="_keys" />/<see cref="_rows" /> 同步交换）。</summary>
	private readonly List<RCheckBox> _boxes = new List<RCheckBox>();

	/// <summary>每一行的开关 key（下标 = 当前视觉顺序）。</summary>
	private readonly List<string> _keys = new List<string>();

	/// <summary>每一行的行容器（下标 = 当前视觉顺序）；▲/▼ 的 Click 靠 <c>_rows.IndexOf</c> 反查下标，
	/// ⛔ 不在 lambda 里捕获下标（排序后捕获的下标就是过期的）。</summary>
	private readonly List<TableLayoutPanel> _rows = new List<TableLayoutPanel>();

	/// <summary>行容器所在的纵向表格（排序 = 清空后按新顺序重挂，⛔ 不 Dispose 任何控件）。</summary>
	private readonly TableLayoutPanel _list;

	private readonly int _maxSlots;

	private readonly Label _status;

	private bool _syncing;

	/// <summary>确定之后的磁贴键序列（= **面板里的视觉顺序**，只含被勾上的项）。</summary>
	internal List<string> SelectedKeys { get; } = new List<string>();

	internal QuickTilePickerForm(IList<string> optionKeys, IList<string> optionTexts, IList<bool> supported, IList<string> selected, int maxSlots)
	{
		_maxSlots = Math.Max(1, maxSlots);
		Text = Strings.QuickTilePickerTitle;
		// 2026-10-12（用户原话「自定义磁贴窗口默认放在主窗口左边行不行」）：手动定位 ——
		// 默认几何在 OnLoad 里按主窗口算（贴主窗口左边，见 PlaceLeftOfOwner）。
		// ⛔ 不再 CenterParent（它会把本窗横在主窗口中间，既不是用户要的位置，也正好压住主窗口滚动区）。
		StartPosition = FormStartPosition.Manual;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		ClientSize = new Size(360, 420);
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		InitTheme(setDPI: true);
		TableLayoutPanel root = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
		Controls.Add(root);
		Label hint = new Label
		{
			Name = "labelQuickTileHint",
			Text = string.Format(Strings.QuickTilePickerHint, _maxSlots),
			AutoSize = true,
			MaximumSize = new Size(328, 0),
			Font = UiVisualStyle.Font(8.5f),
			ForeColor = UiVisualStyle.Muted,
			Margin = new Padding(16, 14, 16, 8)
		};
		root.Controls.Add(hint, 0, 0);
		Panel scroll = new Panel
		{
			Name = "panelQuickTileOptions",
			Dock = DockStyle.Fill,
			AutoScroll = true,
			Margin = Padding.Empty,
			Padding = new Padding(16, 0, 16, 0),
			BackColor = UiVisualStyle.Window
		};
		root.Controls.Add(scroll, 0, 1);
		_list = new TableLayoutPanel
		{
			Name = "tableQuickTileOptions",
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		_list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		scroll.Controls.Add(_list);
		// 2026-10-18（机主：本机不支持的功能项**直接隐藏**，⛔ 不再灰显）：
		//   `supported` 由调用方按**能力位唯一来源**算好（SettingsForm.QuickTileSupported =
		//   hw.SupportsQuickSwitch ∥ StaticQuick；宝龙达 fanboost 的 FanCurveVerified 闸门也在
		//   SupportsQuickSwitch 里）⇒ 本面板只给 supported 项建行，不支持的项**一个控件都不建** ✅
		//   ⚠️ 已保存选择里若含不支持项 ⇒ 它落不到任何行 ⇒ 确定时输出（= 落盘）的只有可用项（静默剔除）✅
		// 2026-10-12（可自行调整顺序）：初始顺序 = 已选中项（按存储顺序）+ 其余可用项（按可选项顺序）。
		List<string> hiddenKeys = new List<string>();
		for (int hiddenIndex = 0; hiddenIndex < optionKeys.Count; hiddenIndex++)
		{
			if (!IsOptionSupported(supported, hiddenIndex) && !Contains(hiddenKeys, optionKeys[hiddenIndex]))
			{
				hiddenKeys.Add(optionKeys[hiddenIndex]);
			}
		}
		// ⭐ 日志（去重）：`[QS] 自定义磁贴：按能力位隐藏 N 项（key=…）` ✅
		if (hiddenKeys.Count > 0)
		{
			Logger.WriteLineIfChanged("qs-tile-hidden",
				$"[QS] 自定义磁贴：按能力位隐藏 {hiddenKeys.Count} 项（key={string.Join(",", hiddenKeys)}）");
		}
		List<int> order = new List<int>();
		if (selected != null)
		{
			for (int s = 0; s < selected.Count; s++)
			{
				for (int i = 0; i < optionKeys.Count; i++)
				{
					if (!order.Contains(i) && IsOptionSupported(supported, i)
						&& string.Equals(optionKeys[i], selected[s], StringComparison.OrdinalIgnoreCase))
					{
						order.Add(i);
						break;
					}
				}
			}
		}
		for (int i2 = 0; i2 < optionKeys.Count; i2++)
		{
			if (!order.Contains(i2) && IsOptionSupported(supported, i2))
			{
				order.Add(i2);
			}
		}
		for (int r = 0; r < order.Count; r++)
		{
			int i3 = order[r];
			string key = optionKeys[i3];
			RCheckBox box = new RCheckBox
			{
				Name = "tile_" + key,
				Text = ((i3 < optionTexts.Count) ? optionTexts[i3] : key),
				Tag = key,
				AutoSize = true,
				Margin = new Padding(0, 3, 0, 3),
				BackColor = UiVisualStyle.Window,
				ForeColor = UiVisualStyle.Text,
				Cursor = Cursors.Hand,
				Checked = Contains(selected, key)
			};
			box.CheckedChanged += OnOptionCheckedChanged;
			TableLayoutPanel row = new TableLayoutPanel
			{
				Name = "rowTile_" + key,
				ColumnCount = 3,
				RowCount = 1,
				Dock = DockStyle.Top,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				BackColor = UiVisualStyle.Window
			};
			row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			row.Controls.Add(box, 0, 0);
			RButton up = CreateReorderButton(row, "▲", Strings.QuickTilePickerMoveUp, -1);
			RButton down = CreateReorderButton(row, "▼", Strings.QuickTilePickerMoveDown, 1);
			row.Controls.Add(up, 1, 0);
			row.Controls.Add(down, 2, 0);
			_keys.Add(key);
			_boxes.Add(box);
			_rows.Add(row);
			_list.Controls.Add(row, 0, r);
		}
		// ⭐ 空列表兜底（机主：全部不支持时**只保留「自定义」**，并显示一行说明；⛔ 不灰显、⛔ 不留空白）：
		//   ⚠️ 这里不建任何勾选框 ⇒ 不存在"能勾但不支持"的项；「确定」仍可按 ⇒ 输出空集
		//   （调用方写显式空集哨兵，磁贴行只剩末位「自定义」）✅
		if (_rows.Count == 0)
		{
			_list.RowCount = 1;
			_list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			_list.Controls.Add(new Label
			{
				Name = "labelQuickTileNoOptions",
				Text = "本机暂无可用的快捷开关",
				AutoSize = true,
				Font = UiVisualStyle.Font(8.5f),
				ForeColor = UiVisualStyle.Muted,
				BackColor = UiVisualStyle.Window,
				Margin = new Padding(0, 10, 0, 10)
			}, 0, 0);
		}
		else
		{
			_list.RowCount = Math.Max(1, _rows.Count);
		}
		Panel footer = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Surface
		};
		root.Controls.Add(footer, 0, 2);
		_status = new Label
		{
			Name = "labelQuickTileStatus",
			Dock = DockStyle.Top,
			Height = 22,
			TextAlign = ContentAlignment.MiddleLeft,
			Font = UiVisualStyle.Font(8.5f),
			ForeColor = UiVisualStyle.Muted,
			Padding = new Padding(16, 0, 16, 0),
			BackColor = UiVisualStyle.Surface
		};
		footer.Controls.Add(_status);
		TableLayoutPanel buttons = new TableLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 32,
			ColumnCount = 2,
			RowCount = 1,
			Padding = new Padding(12, 2, 12, 6),
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Surface
		};
		buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		RButton cancel = CreateButton(Strings.Cancel);
		cancel.Name = "buttonQuickTileCancel";
		cancel.Click += (object sender, EventArgs e) =>
		{
			DialogResult = DialogResult.Cancel;
			Close();
		};
		RButton ok = CreateButton(Strings.Ok);
		ok.Name = "buttonQuickTileOk";
		ok.Click += (object sender, EventArgs e) =>
		{
			OnConfirm();
		};
		UiVisualStyle.ApplyPrimaryButton(ok);
		// 2026-10-12（用户原话「还有取消和确定字体粗细不一致」）：`ApplyPrimaryButton`（UiVisualStyle.cs:993）
		// 给"主按钮"下发的是 `Font(9f, FontStyle.Bold)` —— 那是全应用"默认按钮加粗"的既有强调（有意设计）。
		// 本面板两颗按钮都是 `Secondary=true`，而本构造器末尾的 `ApplyWindow(this)` → `ApplyTree` →
		// `ApplySecondaryButton`（UiVisualStyle.cs:996-1007，调用点 1321）只按 `Secondary` 给底/字色，
		// 会把两颗都收敛成同一个二次按钮面 ⇒ 那份强调在本面板里只剩"确定更粗"这一处，看上去就成了
		// "字体粗细不一致"（用户截图：确定粗、取消常规）。按用户口径**统一为同粗细**：把确定这颗还原成与
		// 取消完全相同的常规 9pt（`ApplyWindow` 给本窗体下发的 `Font(9f)`，同一族同一号）。
		// ⛔ 不动 `ApplyPrimaryButton` 本身 —— 其它对话框（CustomModeForm.cs:1052/1056、FeedbackForm.cs:173/183、
		// FirstRunGuideForm.cs:234/237、FanCurveForm.cs:463/466）都在 `ApplyWindow` **之后**再补一次主按钮样式，
		// 主按钮的强调（accent 底 + 加粗）在那里是完整的，一字未动。
		ok.Font = UiVisualStyle.Font(9f);
		buttons.Controls.Add(cancel, 0, 0);
		buttons.Controls.Add(ok, 1, 0);
		footer.Controls.Add(buttons);
		CancelButton = cancel;
		AcceptButton = ok;
		UiVisualStyle.ApplyWindow(this);
		ResponsiveLayout.ScaleFrom96(this, this);
		// 首/末位的 ▲/▼ 置灰（ScaleFrom96 之后算，位置与尺寸都已定）。
		RefreshReorderButtons();
		RefreshStatus();
	}

	/// <summary>
	/// 一枚"上移/下移"小按钮（▲ / ▼）。⛔ 不捕获下标：点下去时用 <c>_rows.IndexOf(自己所在行)</c> 反查，
	/// 排序之后每次点击都按**当时的**位置算，绝不会出现"按钮记住了旧行号"。
	/// </summary>
	private RButton CreateReorderButton(TableLayoutPanel row, string glyph, string tooltip, int delta)
	{
		RButton button = new RButton
		{
			Name = ((delta < 0) ? "buttonTileUp" : "buttonTileDown"),
			Text = glyph,
			Tag = row,
			AutoSize = false,
			Size = new Size(20, 20),
			Margin = new Padding(2, 3, 0, 3),
			Font = UiVisualStyle.Font(7f),
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.ButtonFace,
			BorderRadius = 4,
			Secondary = true,
			TabStop = false,
			Cursor = Cursors.Hand
		};
		button.AccessibleName = tooltip;
		button.Click += (object sender, EventArgs e) =>
		{
			if (sender is RButton b && b.Tag is TableLayoutPanel owner)
			{
				MoveRow(owner, delta);
			}
		};
		return button;
	}

	/// <summary>
	/// 把 <paramref name="row" /> 在**它自己所属的组内**上移/下移一格：交换三张平行表里的同一对下标，
	/// 再把行按新顺序重挂。⛔ 已勾选行的 ▲/▼ 只走已勾选块，⛔ 未勾选行的 ▲/▼ 只走未勾选组 ——
	/// 越过组边界的移动直接 return（按钮此时本来就是置灰的，这里只是兜底）。
	/// ⛔ 不 Dispose、不重建任何控件（勾选态、置灰态、tooltip 全都原样跟着走）。
	/// </summary>
	private void MoveRow(TableLayoutPanel row, int delta)
	{
		int from = _rows.IndexOf(row);
		if (from < 0)
		{
			return;
		}
		// 组边界：`CountChecked()` 之后是未勾选组的首行（分组不变量的直接推论）。
		int checkedCount = CountChecked();
		bool inCheckedBlock = _boxes[from].Checked;
		int first = (inCheckedBlock ? 0 : checkedCount);
		int last = (inCheckedBlock ? checkedCount : _rows.Count) - 1;
		int to = from + delta;
		if (to < first || to > last)
		{
			return;
		}
		(_rows[from], _rows[to]) = (_rows[to], _rows[from]);
		(_keys[from], _keys[to]) = (_keys[to], _keys[from]);
		(_boxes[from], _boxes[to]) = (_boxes[to], _boxes[from]);
		RelayoutRows();
		RefreshStatus();
	}

	/// <summary>
	/// 2026-10-12（用户选定「已勾选的始终连续置顶（未勾选不能插到中间）」）：三张平行表按**稳定分区**重排 ——
	/// 已勾选的行连续置顶（块内保持各自相对顺序），未勾选的行全部跟在块下面（组内同样保持相对顺序）。
	/// 勾选/取消勾选后调用，保证"未勾选行绝不会出现在两行已勾选之间"。
	/// ⛔ 不 Dispose、不重建控件（与 <see cref="MoveRow" /> 同一套重挂手法）。
	/// </summary>
	private void ApplyGroupOrder()
	{
		List<TableLayoutPanel> rows = new List<TableLayoutPanel>(_rows.Count);
		List<RCheckBox> boxes = new List<RCheckBox>(_boxes.Count);
		List<string> keys = new List<string>(_keys.Count);
		for (int pass = 0; pass < 2; pass++)
		{
			for (int i = 0; i < _rows.Count; i++)
			{
				if (_boxes[i].Checked != (pass == 0))
				{
					continue;
				}
				rows.Add(_rows[i]);
				boxes.Add(_boxes[i]);
				keys.Add(_keys[i]);
			}
		}
		_rows.Clear();
		_rows.AddRange(rows);
		_boxes.Clear();
		_boxes.AddRange(boxes);
		_keys.Clear();
		_keys.AddRange(keys);
		RelayoutRows();
	}

	/// <summary>按 <see cref="_rows" /> 的当前顺序把行重挂到 <see cref="_list" />（⛔ 不 Dispose、不重建）。</summary>
	private void RelayoutRows()
	{
		_list.SuspendLayout();
		try
		{
			_list.Controls.Clear();
			for (int i = 0; i < _rows.Count; i++)
			{
				_list.Controls.Add(_rows[i], 0, i);
			}
		}
		finally
		{
			_list.ResumeLayout(performLayout: true);
		}
		RefreshReorderButtons();
	}

	/// <summary>
	/// ▲/▼ 的置灰规则（2026-10-12 分组之后）—— 移动被限制在各自组内，所以灰的是**组边界**：
	/// <list type="bullet">
	/// <item><b>已勾选块</b>：首行 ▲ 灰、末行 ▼ 灰（块内只剩一行 ⇒ 两颗都灰）；</item>
	/// <item><b>未勾选组</b>：首行 ▲ 灰、末行 ▼ 灰（组内只剩一行 ⇒ 两颗都灰）；</item>
	/// <item>整表首行 = 已勾选块首行（块为空时 = 未勾选组首行）、整表末行 = 未勾选组末行 ⇒
	/// "首行 ▲、末行 ▼ 照旧置灰"是上面两条的自然结论；⛔ 未勾选行的 ▲ 永远不会翻过组边界，
	/// 所以"把未勾选行挪到已勾选行中间"在 UI 上不可达。</item>
	/// </list>
	/// ⛔ 只改 Enabled 与 Cursor 提示，不动任何勾选/顺序。
	/// </summary>
	private void RefreshReorderButtons()
	{
		int checkedCount = CountChecked();
		for (int i = 0; i < _rows.Count; i++)
		{
			bool inCheckedBlock = _boxes[i].Checked;
			int first = (inCheckedBlock ? 0 : checkedCount);
			int last = (inCheckedBlock ? checkedCount : _rows.Count) - 1;
			foreach (Control child in _rows[i].Controls)
			{
				if (child is RButton button && button.Tag is TableLayoutPanel)
				{
					bool up = string.Equals(button.Name, "buttonTileUp", StringComparison.Ordinal);
					bool can = up ? (i > first) : (i < last);
					button.Enabled = can;
					button.Cursor = (can ? Cursors.Hand : Cursors.Default);
				}
			}
		}
	}

	/// <summary>
	/// 2026-10-12（用户原话「自定义磁贴窗口默认放在主窗口左边行不行」）：**默认位置 = 主窗口左边**。
	/// <para>规则：本窗**右边缘**贴在**主窗口左边缘**左侧、留 10 逻辑像素间隙
	/// （<see cref="PlaceSideGapLogical" />，与既有相邻窗 <c>ResponsiveLayout.PlaceAdjacent</c> 同一口径）。
	/// 纵向**顶对齐**（也取 PlaceAdjacent 的口径：兄弟窗都顶对齐主窗口，视觉上一排站齐，
	/// 且不会像垂直居中那样在主窗口很高/很矮时让本窗上下横跳）。</para>
	/// <para>兜底（屏幕感知，三层）：① 左边放不下（会越出工作区左边界）⇒ 退到**主窗口右边**；
	/// ② 右边也放不下 ⇒ 用 <c>Screen.FromControl(主窗口).WorkingArea</c> 把 Left/Top **夹进工作区**
	/// （宁可压住主窗口，也绝不把窗口丢到屏幕外）；③ 之后 <c>RForm.OnShown</c> →
	/// <c>ResponsiveLayout.ConstrainToWorkingArea</c> 还会再夹一次（第二道保险）。</para>
	/// <para>⚠️ 多显示器：工作区取**主窗口所在那块屏**（<c>Screen.FromControl(owner)</c>），
	/// ⛔ 不用 <c>Screen.PrimaryScreen</c>。</para>
	/// </summary>
	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		PlaceLeftOfOwner();
	}

	/// <summary>本窗与主窗口之间留的逻辑像素间隙（保持既有相邻窗的 10 逻辑像素口径）。</summary>
	private const int PlaceSideGapLogical = 10;

	/// <summary>
	/// 把本窗摆到 <see cref="Form.Owner" />（主窗口）左边；放不下就退右边，再不行夹进工作区。
	/// ⛔ 只改 Left/Top，⛔ 不碰尺寸、⛔ 不碰模态行为。
	/// </summary>
	private void PlaceLeftOfOwner()
	{
		Form form = Owner ?? FindForm();
		if (form == null || form.IsDisposed)
		{
			return;
		}
		Rectangle workingArea = Screen.FromControl(form).WorkingArea;
		int gap = ResponsiveLayout.LogicalToDevice(this, PlaceSideGapLogical);
		int left = form.Left - Width - gap;
		if (left < workingArea.Left)
		{
			// 左边空间不够（会跑出屏幕）⇒ 退到主窗口右边；右边也不够时下面统一夹进工作区。
			left = form.Right + gap;
		}
		int x = Math.Clamp(left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - Width));
		int y = Math.Clamp(form.Top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - Height));
		Location = new Point(x, y);
	}

	/// <summary>名字与既有工具方法一致（原实现里 <c>Contains</c> 只服务"初始勾选"）。</summary>
	private static bool Contains(IList<string> source, string key)
	{
		if (source == null)
		{
			return false;
		}
		for (int i = 0; i < source.Count; i++)
		{
			if (string.Equals(source[i], key, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// ⭐ 该可选项是否**本机支持** —— 能力位由调用方按唯一来源算好并传入（见构造器注释：
	/// <c>SettingsForm.QuickTileSupported</c>）；本方法只做数组越界保护（缺项按不支持处理）✅。
	/// </summary>
	private static bool IsOptionSupported(IList<bool> supported, int index)
	{
		return index >= 0 && index < supported.Count && supported[index];
	}

	private static RButton CreateButton(string text)
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

	/// <summary>
	/// 勾选上限的**显性**约束：第 <c>maxSlots + 1</c> 个被勾上时**立刻撤掉这一次勾选**并提示，
	/// ⛔ 不留"看起来勾上了、确定时被悄悄丢掉"的状态。
	/// ⚠️ 下限**不是**约束：0 个勾选 = 合法（用户原话「快捷开关最少 0 个」），见 <see cref="OnConfirm" />。
	/// </summary>
	private void OnOptionCheckedChanged(object sender, EventArgs e)
	{
		if (_syncing)
		{
			return;
		}
		if (CountChecked() > _maxSlots)
		{
			_syncing = true;
			try
			{
				if (sender is RCheckBox box)
				{
					box.Checked = false;
				}
			}
			finally
			{
				_syncing = false;
			}
			_status.ForeColor = UiVisualStyle.Warn;
			_status.Text = string.Format(Strings.QuickTilePickerFull, _maxSlots);
			return;
		}
		// 2026-10-12（用户选定「已勾选的始终连续置顶（未勾选不能插到中间）」）：勾选/取消勾选后立刻归位 ——
		// 勾上 ⇒ 追加到已勾选块末尾；取消 ⇒ 追加到未勾选组末尾（见 ApplyGroupOrder）。
		// ⚠️ 上位"超上限被撤回"那条分支不会走到这里（撤回时 _syncing 挡住重入，且这里只处理"合法的新状态"）。
		ApplyGroupOrder();
		RefreshStatus();
	}

	private int CountChecked()
	{
		int num = 0;
		foreach (RCheckBox box in _boxes)
		{
			if (box.Checked)
			{
				num++;
			}
		}
		return num;
	}

	private void RefreshStatus()
	{
		_status.ForeColor = UiVisualStyle.Muted;
		int num = CountChecked();
		// 2026-10-12（0 个磁贴）：0 选不再是错误，而是一条中性说明（说明"确定之后磁贴行长什么样"）。
		_status.Text = ((num == 0) ? Strings.QuickTilePickerEmpty : (num + " / " + _maxSlots));
	}

	private void OnConfirm()
	{
		SelectedKeys.Clear();
		// 2026-10-12（可自行调整顺序）：输出顺序 = **面板里的视觉顺序**（_boxes/_keys 已随排序同步交换）。
		for (int i = 0; i < _boxes.Count; i++)
		{
			if (_boxes[i].Checked)
			{
				SelectedKeys.Add(_keys[i]);
			}
		}
		if (SelectedKeys.Count > _maxSlots)
		{
			_status.ForeColor = UiVisualStyle.Warn;
			_status.Text = string.Format(Strings.QuickTilePickerFull, _maxSlots);
			return;
		}
		// 2026-10-12（0 个磁贴）：Count == 0 走"显式空集"，由调用方写哨兵 ⇒ 这里不再拦。
		DialogResult = DialogResult.OK;
		Close();
	}
}
