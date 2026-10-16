using System;
using System.Drawing;
using System.Windows.Forms;

namespace MechrevoLite.UI;

internal sealed class RCollapseGroup : BufferedPanel
{
	private readonly Panel _content;

	private readonly Label _arrow;

	private readonly Label _summary;

	private readonly Label _titleLabel;

	/// <summary>标题行右侧那块"余量列"（Percent 100）所在的表格 —— 见 <see cref="SetHeaderRight" />。</summary>
	private readonly TableLayoutPanel _header;

	private readonly string _configKey;

	public bool Expanded { get; private set; }

	public string Summary
	{
		get
		{
			return _summary.Text;
		}
		set
		{
			_summary.Text = value;
		}
	}

	public event EventHandler? Toggled;

	public RCollapseGroup(string title, UiGlyph.Kind icon, string configKey, bool defaultExpanded = true, bool showDivider = false)
	{
		_configKey = configKey;
		Name = "collapseGroup_" + configKey;
		DoubleBuffered = true;
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		Margin = Padding.Empty;
		Padding = Padding.Empty;
		BackColor = UiVisualStyle.Window;
		_content = new Panel
		{
			Name = Name + "_content",
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		_arrow = new Label
		{
			Name = Name + "_arrow",
			Text = (defaultExpanded ? "▾" : "▸"),
			AutoSize = false,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleCenter,
			Margin = Padding.Empty,
			ForeColor = UiVisualStyle.Muted,
			Font = UiVisualStyle.Font(9f)
		};
		_titleLabel = new Label
		{
			Name = Name + "_title",
			Text = title,
			AutoSize = true,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = Padding.Empty,
			ForeColor = UiVisualStyle.Text,
			Font = UiVisualStyle.Font(9f, FontStyle.Bold)
		};
		BufferedTableLayoutPanel bufferedTableLayoutPanel = new BufferedTableLayoutPanel
		{
			Name = Name + "_header",
			Dock = DockStyle.Top,
			ColumnCount = 4,
			RowCount = 1,
			Height = D(30),
			Margin = Padding.Empty,
			Padding = new Padding(D(2), D(1), D(2), D(1)),
			BackColor = UiVisualStyle.Window,
			Cursor = Cursors.Hand
		};
		_header = bufferedTableLayoutPanel;
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(18)));
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(24)));
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		PictureBox pictureBox = new PictureBox
		{
			Name = Name + "_icon",
			SizeMode = PictureBoxSizeMode.Zoom,
			BackColor = UiVisualStyle.Window,
			Margin = Padding.Empty
		};
		_summary = new Label
		{
			Name = Name + "_summary",
			Text = "",
			AutoSize = false,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleRight,
			Margin = Padding.Empty,
			ForeColor = UiVisualStyle.Muted,
			Font = UiVisualStyle.Font(8.5f)
		};
		bufferedTableLayoutPanel.Controls.Add(_arrow, 0, 0);
		bufferedTableLayoutPanel.Controls.Add(pictureBox, 1, 0);
		bufferedTableLayoutPanel.Controls.Add(_titleLabel, 2, 0);
		bufferedTableLayoutPanel.Controls.Add(_summary, 3, 0);
		UiVisualStyle.ApplyGlyph(pictureBox, icon);
		bufferedTableLayoutPanel.AccessibleName = title;
		bufferedTableLayoutPanel.AccessibleRole = AccessibleRole.PushButton;
		bufferedTableLayoutPanel.Click += (object? _, EventArgs _) =>
		{
			Toggle();
		};
		foreach (Control control in bufferedTableLayoutPanel.Controls)
		{
			control.Click += (object? _, EventArgs _) =>
			{
				Toggle();
			};
		}
		Panel value = new Panel
		{
			Name = Name + "_divider",
			Dock = DockStyle.Top,
			Height = 1,
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Border,
			Visible = showDivider
		};
		Controls.Add(_content);
		Controls.Add(bufferedTableLayoutPanel);
		Controls.Add(value);
		Expanded = defaultExpanded;
		_content.Visible = Expanded;
		if (Program.UiAuditMode)
		{
			Expanded = true;
			_content.Visible = true;
			_arrow.Text = "▾";
		}
		int D(int value2)
		{
			return ResponsiveLayout.LogicalToDevice(this, value2);
		}
	}

	public void SetContent(Control content)
	{
		content.Dock = DockStyle.Top;
		_content.Controls.Add(content);
	}

	/// <summary>
	/// 2026-10-11（用户原话「1通道 0开启删掉 把自动关闭时间放这个位置 然后下拉列表可以再缩窄一点点」）：
	/// 把一颗控件放进**标题行右端那块余量格**（原摘要 <see cref="Summary" /> 所在的那一列、右对齐），
	/// 供"卡片标题行右端要放真实控件（字段标签 + 下拉框）"的卡片使用。
	/// 用法与几何口径：
	///   · 只改**列 3** 的占用者（<see cref="DockStyle.Fill" /> ⇒ 控件自己决定内部怎么排）；
	///   · 原来的摘要 Label 不再添加 ⇒ <see cref="Summary" /> 的 setter 对本卡片变成空操作（不显示、不占位）；
	///   · 标题行的行高（<c>D(30)</c>）、列样式、点击折叠语义一律未动。
	/// </summary>
	public void SetHeaderRight(Control content)
	{
		_header.Controls.Remove(_summary);
		content.Dock = DockStyle.Fill;
		content.Margin = Padding.Empty;
		_header.Controls.Add(content, 3, 0);
	}

	public void Toggle()
	{
		SuspendLayout();
		_content.SuspendLayout();
		try
		{
			Expanded = !Expanded;
			_content.Visible = Expanded;
			_arrow.Text = (Expanded ? "▾" : "▸");
			if (!Program.UiAuditMode)
			{
				AppConfig.Set(_configKey, Expanded ? 1 : 0);
			}
		}
		finally
		{
			_content.ResumeLayout(performLayout: true);
			ResumeLayout(performLayout: true);
		}
		Toggled?.Invoke(this, EventArgs.Empty);
	}
}
