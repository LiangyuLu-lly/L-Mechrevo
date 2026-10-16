using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MechrevoLite.Properties;
using MechrevoLite.UI;

namespace MechrevoLite;

/// <summary>
/// 2026-10-10 licence（GPLv3 §0/§5d「Appropriate Legal Notices 必须在界面里可达」）：
/// 「关于 L-Mechrevo —— 开源许可与致谢」窗口。
///
/// 入口：SettingsForm 的版本号标签（LabelVersion_Click）—— **单击**开本窗口，
/// **Ctrl+单击**仍是原来的「测试反馈」弹窗（旧入口不丢）。
///
/// 形态照 source\MechrevoLite\DonateForm.cs：RForm 基类 + TableLayoutPanel +
/// UiVisualStyle 配色 + ResponsiveLayout 缩放。
///
/// 行数据来自 ATTRIBUTION-PATCH-PLAN.md §2.4：项目名 / 许可 / URL 三个字段是**语言中立**的
/// 字面量，所有句子一律走 Strings.AboutLicenses*（两个 resx 各 20 个键）。
/// 著作权人行（AboutLicensesCopyright）已定稿为「Copyright (C) 2026 L-Mechrevo contributors」
/// （zh-CN：版权所有 (C) 2026 L-Mechrevo contributors），与根 LICENSE 及 install_script.iss 的
/// AppCopyright 口径一致；本窗口原样显示该 resx 值，占位已清除。
/// </summary>
public class AboutLicensesForm : RForm
{
	/// <summary>一行 = 项目名 + 许可 + 用途说明（用途说明末尾接 URL）。</summary>
	private readonly record struct Entry(string Name, string Licence, string Url, string Use);

	// 派生自（GPL-3.0）×3 → 随本程序分发的二进制 ×2 → 协议参考（未复制代码）×7：前 3+2+7 行与计划 §2.4 的表
	// 逐行一致，第 2 行（G-Helper 的 AMD SMU 层）是 2026-10-10 co-attribution 按 ATTRIBUTION-CO-ADDENDUM.md §6 新增的。
	// 2026-10-10 intel（⑦B）：`PawnIO.Modules IntelMSR.bin` 因 Intel 调校通路合并回来而**加进"随程序分发"组**
	// （原本被记为"已不再分发"的历史条目，见 THIRD-PARTY-NOTICES.md §1.2 B4）。
	// 2026-10-10 归属修正：`irusanov/SMUDebugTool` 从本组**移到** References —— 它自有树里**没有任何 SMU 命令表**
	// （引擎是同作者的 `ZenStates-Core` 子模块）⇒ 对本项目只是**协议/行为参考、无复制表达**；派生义务落在
	// `ZenStates-Core` 那一行（S2）。两者同为 GPL-3.0-only，许可义务本身无差别，改的只是分组与文案的准确性。
	private static readonly Entry[] Derived =
	{
		new Entry("G-Helper", "GPL-3.0", "https://github.com/seerge/g-helper", Strings.AboutLicensesUseGHelper),
		new Entry("G-Helper — AMD SMU layer (app/Pawn/RyzenSmu.cs)", "GPL-3.0", "https://github.com/seerge/g-helper", Strings.AboutLicensesUseGHelperSmu),
		new Entry("irusanov/ZenStates-Core", "GPL-3.0", "https://github.com/irusanov/ZenStates-Core", Strings.AboutLicensesUseZenStatesCore)
	};

	private static readonly Entry[] Shipped =
	{
		new Entry("PawnIO.Modules RyzenSMU.bin", "LGPL-2.1-or-later", "https://github.com/namazso/PawnIO.Modules", Strings.AboutLicensesUsePawnIOModules),
		// 2026-10-10 intel（⑦B，用户原话「intel功能合并」）：Intel 调校通路合并回来之后，
		// LMechrevo.IntelMSR.bin 与 RyzenSMU.bin 一样**随程序集内嵌分发**（同一上游、同一份
		// LGPL-2.1-or-later，所以这一行用的是同一个 URL 与同一份 LICENSES\LGPL-2.1.txt），
		// 因此它从"历史条目"回到本组（见 THIRD-PARTY-NOTICES.md §1.2 B4）。
		new Entry("PawnIO.Modules IntelMSR.bin", "LGPL-2.1-or-later", "https://github.com/namazso/PawnIO.Modules", Strings.AboutLicensesUsePawnIOIntelMsr),
		// 2026-10-11（息屏改造）：usbmmidd_v2 虚拟显示器驱动随本程序分发（<程序目录>\usbmmidd_v2），
		// 用于「息屏」功能挂载虚拟屏并强切视频信号（真息屏不睡眠）。第三方版权归 Amyuni Technologies Inc.，
		// 按驱动自带 usbmmidd_v2\License.txt 的 Amyuni 许可分发（其第 2 条要求在产品文档中鸣谢）。
		new Entry("usbmmidd_v2 — virtual display driver", "Amyuni (see usbmmidd_v2\\\\License.txt)", "https://www.amyuni.com/", Strings.AboutLicensesUseUsbmmidd)
	};

	private static readonly Entry[] References =
	{
		new Entry("leogx9r/ryzen_smu", "GPL-2.0", "https://github.com/leogx9r/ryzen_smu", Strings.AboutLicensesUseRyzenSmu),
		new Entry("FlyGoat/RyzenAdj", "LGPL-3.0", "https://github.com/FlyGoat/RyzenAdj", Strings.AboutLicensesUseRyzenAdj),
		new Entry("JamesCJ60/Universal-x86-Tuning-Utility", "GPL-3.0", "https://github.com/JamesCJ60/Universal-x86-Tuning-Utility", Strings.AboutLicensesUseUxtu),
		new Entry("kitsunyan/intel-undervolt", "GPL-3.0", "https://github.com/kitsunyan/intel-undervolt", Strings.AboutLicensesUseIntelUndervolt),
		new Entry("tomups/watercooler-manager", "GPL-3.0", "https://github.com/tomups/watercooler-manager", Strings.AboutLicensesUseWatercooler),
		new Entry("irusanov/SMUDebugTool", "GPL-3.0", "https://github.com/irusanov/SMUDebugTool", Strings.AboutLicensesUseSmudbg),
	// 2026-10-10（限充门控持久化 · 用户要求「UI 层标明源码作者」）：MIT 移植实现，放本组末尾。
	new Entry("Leo-stro/mechrevo-charge-limit", "MIT", "https://github.com/Leo-stro/mechrevo-charge-limit", Strings.AboutLicensesUseMechrevoChargeLimit),
	// 2026-10-11（息屏改造 · 用户要求 UI 层鸣谢）：息屏（不睡眠）的虚拟显示器方案参考实现，MIT，放本组末尾。
	new Entry("IYATT-yx/NullSignal", "MIT", "https://github.com/IYATT-yx/NullSignal", Strings.AboutLicensesUseNullSignal)
	};

	public AboutLicensesForm()
	{
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		Text = Strings.AboutLicensesTitle;
		FormBorderStyle = FormBorderStyle.Sizable;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.CenterParent;
		ClientSize = new Size(820, 660);
		MinimumSize = new Size(620, 420);
		InitTheme(setDPI: true);

		// 行数较多（3+1+6 组），窗口矮时靠 AutoScroll 兜住，不裁掉任何一条许可。
		Panel scroll = new Panel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true,
			BackColor = BackColor
		};
		TableLayoutPanel root = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = 0,
			Padding = new Padding(24, 20, 24, 20),
			BackColor = BackColor
		};
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		scroll.Controls.Add(root);
		Controls.Add(scroll);

		// ---- 标题 + 说明 + GPLv3 §0/§5d 要求的著作权行与无担保声明 ----
		AddTitle(root, Strings.AboutLicensesTitle);
		AddParagraph(root, Strings.AboutLicensesIntro, UiVisualStyle.Text);
		// 2026-10-12 署名（本次改动）：本窗口此前**只列第三方代码**，完全不提本程序自己的原项目与原作者。
		// 新增一段「原项目」说明（中英双语走 AboutLicensesOrigin），位置就在 Intro 之后、著作权行之前：
		// 该段逐字给出原项目地址 https://github.com/LiangyuLu-lly/L-Mechrevo/tree/v0.290.6 ，
		// 并写明本版由 xiaohan（xmsjx）维护。改动极小（1 个 resx 键 + 1 次 AddParagraph），故一并做了。
		AddParagraph(root, Strings.AboutLicensesOrigin, UiVisualStyle.Text);
		AddParagraph(root, Strings.AboutLicensesCopyright, UiVisualStyle.Muted);
		AddParagraph(root, Strings.AboutLicensesNoWarranty, UiVisualStyle.Text);

		// ---- 三个分组 ----
		AddGroup(root, Strings.AboutLicensesDerivedHeading, Derived);
		AddGroup(root, Strings.AboutLicensesShippedHeading, Shipped);
		AddGroup(root, Strings.AboutLicensesReferenceHeading, References);

		// ---- 底部：打开 LICENSES 文件夹 + 关闭 ----
		TableLayoutPanel actions = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 3,
			RowCount = 1,
			Margin = new Padding(0, 16, 0, 0),
			BackColor = BackColor
		};
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Button openFolder = new Button
		{
			Text = Strings.AboutLicensesOpenFolder,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			MinimumSize = new Size(150, 32),
			Margin = new Padding(0, 0, 8, 0)
		};
		UiVisualStyle.ApplySecondaryButton(openFolder);
		openFolder.Click += (object? _, EventArgs _) => OpenLicensesFolder();
		Button close = new Button
		{
			Text = Strings.AboutLicensesClose,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			MinimumSize = new Size(96, 32),
			DialogResult = DialogResult.OK
		};
		UiVisualStyle.ApplyPrimaryButton(close);
		actions.Controls.Add(openFolder, 0, 0);
		actions.Controls.Add(close, 2, 0);
		AddRow(root, actions);

		AcceptButton = close;
		CancelButton = close;

		// ---- 主题 / 高 DPI（与 DonateForm 同序：ApplyWindow → LayoutTree → ScaleFrom96 → LayoutTree）----
		UiVisualStyle.ApplyWindow(this);
		scroll.BackColor = UiVisualStyle.Window;
		root.BackColor = UiVisualStyle.Window;
		actions.BackColor = UiVisualStyle.Window;
		ResponsiveLayout.PerformLayoutTree(this);
		ResponsiveLayout.ScaleFrom96(this, this);
		ResponsiveLayout.PerformLayoutTree(this);
	}

	private static void AddTitle(TableLayoutPanel root, string text)
	{
		Label label = new Label
		{
			Text = text,
			Font = UiVisualStyle.Font(15f, FontStyle.Bold),
			ForeColor = UiVisualStyle.Text,
			AutoSize = true,
			Dock = DockStyle.Top,
			UseMnemonic = false,
			Margin = new Padding(0, 0, 0, 10)
		};
		UiVisualStyle.ApplyTitle(label, 15f);
		AddRow(root, label);
	}

	private static void AddParagraph(TableLayoutPanel root, string text, Color color)
	{
		Label label = new Label
		{
			Text = text,
			Font = UiVisualStyle.Font(9.5f),
			ForeColor = color,
			AutoSize = true,
			Dock = DockStyle.Top,
			UseMnemonic = false,
			MaximumSize = new Size(700, 0),
			Margin = new Padding(0, 0, 0, 8)
		};
		AddRow(root, label);
	}

	private static void AddGroup(TableLayoutPanel root, string heading, Entry[] entries)
	{
		Label label = new Label
		{
			Text = heading,
			Font = UiVisualStyle.Font(11f, FontStyle.Bold),
			ForeColor = UiVisualStyle.Text,
			AutoSize = true,
			Dock = DockStyle.Top,
			UseMnemonic = false,
			Margin = new Padding(0, 14, 0, 6)
		};
		AddRow(root, label);

		TableLayoutPanel table = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 3,
			RowCount = entries.Length,
			Margin = new Padding(0),
			BackColor = UiVisualStyle.Window
		};
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260f));
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		for (int i = 0; i < entries.Length; i++)
		{
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Entry entry = entries[i];
			table.Controls.Add(Cell(entry.Name, UiVisualStyle.Text, new Padding(0, 3, 10, 3)), 0, i);
			table.Controls.Add(Cell(entry.Licence, UiVisualStyle.Muted, new Padding(0, 3, 10, 3)), 1, i);
			table.Controls.Add(UseCell(entry), 2, i);
		}
		AddRow(root, table);
	}

	/// <summary>用途说明 + URL —— URL 用 LinkLabel 原样显示（可点，是这次整改的重点）。</summary>
	private static Control UseCell(Entry entry)
	{
		TableLayoutPanel cell = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = 2,
			Margin = new Padding(0, 3, 0, 3),
			BackColor = UiVisualStyle.Window
		};
		cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		cell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		cell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Label use = new Label
		{
			Text = entry.Use,
			Font = UiVisualStyle.Font(9f),
			ForeColor = UiVisualStyle.Text,
			AutoSize = true,
			Dock = DockStyle.Top,
			UseMnemonic = false,
			MaximumSize = new Size(340, 0),
			Margin = new Padding(0)
		};
		LinkLabel link = new LinkLabel
		{
			Text = entry.Url,
			Font = UiVisualStyle.Font(9f),
			AutoSize = true,
			Dock = DockStyle.Top,
			UseMnemonic = false,
			Margin = new Padding(0, 2, 0, 0)
		};
		// 样式由 UiVisualStyle.ApplyWindow → ControlHelper 统一给（夜间四档链接色，见 StyleLinkLabel），
		// 这里只挂点击行为；日志与 CpuTuningForm 的 PawnIO 链接同一形态。
		link.LinkClicked += (object? _, LinkLabelLinkClickedEventArgs _) => Open(entry.Url, "About licences link: ");
		cell.Controls.Add(use, 0, 0);
		cell.Controls.Add(link, 0, 1);
		return cell;
	}

	private static Label Cell(string text, Color color, Padding margin)
	{
		return new Label
		{
			Text = text,
			Font = UiVisualStyle.Font(9f),
			ForeColor = color,
			AutoSize = true,
			Dock = DockStyle.Top,
			UseMnemonic = false,
			Margin = margin
		};
	}

	private static void AddRow(TableLayoutPanel root, Control control)
	{
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.Controls.Add(control, 0, root.RowCount);
		root.RowCount++;
	}

	private static void Open(string target, string logPrefix)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = target,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			Logger.WriteLine(logPrefix + target + " -> " + ex.Message);
		}
	}

	/// <summary>打开程序目录下的 LICENSES\；找不到就退化成一条提示 + 日志（不静默失败）。</summary>
	private void OpenLicensesFolder()
	{
		string folder = Path.Combine(AppContext.BaseDirectory, "LICENSES");
		try
		{
			if (Directory.Exists(folder))
			{
				Open(folder, "About licences folder: ");
				return;
			}
			Logger.WriteLine("About licences folder missing: " + folder);
			string fallback = Path.Combine(AppContext.BaseDirectory, "LICENSES", "LICENSE");
			MessageBox.Show(this, string.Format(Strings.AboutLicensesFolderMissing, fallback), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("About licences folder fail: " + ex.Message);
			MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}
}
