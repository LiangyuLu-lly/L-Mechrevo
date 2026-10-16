using System;
using MechrevoLite.Properties;using System.Drawing;
using MechrevoLite.Properties;using System.IO;
using MechrevoLite.Properties;using System.Windows.Forms;
using MechrevoLite.Properties;using MechrevoLite.UI;
using MechrevoLite.Properties;
namespace MechrevoLite;

public class DonateForm : RForm
{
	public DonateForm()
	{
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		Text = Strings.DonateTitle;
		// 2026-10-12 r6：标题栏左上角 = 底栏「赞助」按钮那枚字形（Kind.Heart，⛔ 不另画）。
		UiVisualStyle.ApplyWindowGlyph(this, UiGlyph.Kind.Heart);
		FormBorderStyle = FormBorderStyle.Sizable;
		MaximizeBox = false;
		MinimizeBox = false;
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(760, 500);
		MinimumSize = new Size(520, 300);
		InitTheme(setDPI: true);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = 5,
			Padding = new Padding(24, 20, 24, 24),
			BackColor = BackColor
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		Controls.Add(tableLayoutPanel);
		Label label = new Label
		{
			Text = Strings.DonateThanks,
			Font = UiVisualStyle.Font(15f, FontStyle.Bold),
			ForeColor = UiVisualStyle.Text,
			AutoSize = true,
			Dock = DockStyle.Top,
			Margin = new Padding(0, 0, 0, 8)
		};
		tableLayoutPanel.Controls.Add(label, 0, 0);
		Label control = new Label
		{
			Text = Strings.DonateScanHint,
			Font = UiVisualStyle.Font(9.5f),
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Dock = DockStyle.Top,
			Margin = new Padding(0, 0, 0, 16)
		};
		tableLayoutPanel.Controls.Add(control, 0, 1);
		// 2026-10-12 r8（本次改动）：原来是**一个** 2×2（左上/右上 = 原项目；左下/右下 = 本版维护者）。
		// ⚠️ 分组易读性：原来只在**每张码下方**各挂一行 caption，用户截图裁掉上排图后，第一排的 caption
		// 紧贴第二排图片上方，看起来像"这排图就是原项目的" ⇒ 误读。
		// 改成 (a)：**每组上方一个组标题**（MakeSection，row0 居中），组内两张图**不再带各自的小字**；
		// caption 文案逐字沿用 Strings.DonateSource / Strings.DonateSourceMine（⛔ 不改措辞，只改位置：图下 -> 组上）。
		//   上组标题 = 原项目（LiangyuLu-lly）  ↔ qrcode1.jpg（左）/ qrcode2.jpg（右）
		//   下组标题 = 本版维护者 xiaohan（xmsjx） ↔ qrcode3.png（左·微信）/ qrcode4.png（右·支付宝）
		// ⚠️ 若以后要删某一行，⛔ 不要删 RowStyles（AutoSize 表里"没有 RowStyle 的行高 = 0"，会静默丢图）。
		// 2026-10-12 r8：每格 220×220（原 280×280）—— 用户"二维码可以稍微小一点"。
		// 窗口客户区随之从 760×803 收到 760×690（窗口 776×842 -> 776×729），整表自然高度 697 < 690 之外仍全部可见
		// （实测：四张码 + 两行组标题 + 归属段无裁切，见本轮报告；扫码验证见 r8/scan 记录）。
		// Zoom 模式下图片框是固定正方形（见 MakeQr），1280×1280 带白底与静区的 PNG 会**完整按比例**缩到 220×220。

		Control MakeSection(string header, string leftPath, string rightPath, bool firstGroup)
		{
			TableLayoutPanel control3 = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 1,
				RowCount = 2,
				Margin = new Padding(0),
				BackColor = BackColor
			};
			control3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			control3.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			control3.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Label control4 = new Label
			{
				Text = header,
				Font = UiVisualStyle.Font(9.5f, FontStyle.Bold),
				ForeColor = UiVisualStyle.Text,
				AutoSize = true,
				Dock = DockStyle.Top,
				TextAlign = ContentAlignment.MiddleCenter,
				UseMnemonic = false,
				Margin = new Padding(0, 0, 0, 6)
			};
			control3.Controls.Add(control4, 0, 0);
			// 2026-10-12 r9（用户原话「图片居中对齐 现在右边空隙太大了」）：原来是 **Dock.Fill + 两列各 50%**，
			// 两个 220 宽的 PictureBox 在 346 宽的列里各自居中 ⇒ 图片对**相对整窗看是明显偏左**的
			// （上排两张竖版 147/162 宽更明显）。这里把这一对图**定宽**（两列各 230 = 220 图 + 10 贴中缝的 Margin），
			// 真正的位置由构造函数末尾的"居中循环"按 **容器宽度**（含 Margin）算 x 落点 ——
			// ⚠️ 只按"两个控件宽之和"居中会漏掉外侧各 10px 的 Margin，落点固定偏左 12px（本轮实测 102 vs 150）。
			// 组标题仍 Dock.Top 占满整行（TextAlign=MiddleCenter ⇒ 与图片对同一中线）。
			// ⚠️ 用 Absolute 而非 AutoSize：PictureBox 带 Image 时 PreferredSize 未必等于 Size，Absolute 才是确定值。
			TableLayoutPanel control5 = new TableLayoutPanel
			{
				Dock = DockStyle.None,
				Anchor = AnchorStyles.None,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 2,
				RowCount = 1,
				Margin = new Padding(0, firstGroup ? 0 : 16, 0, 0),
				BackColor = BackColor
			};
			control5.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230f));
			control5.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230f));
			control5.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			control5.Controls.Add(MakeQrCell(leftPath, null, left: true), 0, 0);
			control5.Controls.Add(MakeQrCell(rightPath, null, left: false), 1, 0);
			control3.Controls.Add(control5, 0, 1);
			return control3;
		}
		tableLayoutPanel.Controls.Add(MakeSection(Strings.DonateSource, "qrcode1.jpg", "qrcode2.jpg", firstGroup: true), 0, 2);
		tableLayoutPanel.Controls.Add(MakeSection(Strings.DonateSourceMine, "qrcode3.png", "qrcode4.png", firstGroup: false), 0, 3);
		// ---- 归属说明（中英双语，两份 resx 的 DonateAttribution）----
		// 逐字给出原项目地址 https://github.com/LiangyuLu-lly/L-Mechrevo/tree/v0.290.6；
		// 并写明下方两张 = 本版维护者 xiaohan（xmsjx）。
		Label control2 = new Label
		{
			Text = Strings.DonateAttribution,
			Font = UiVisualStyle.Font(9f),
			ForeColor = UiVisualStyle.Muted,
			AutoSize = true,
			Dock = DockStyle.Top,
			UseMnemonic = false,
			MaximumSize = new Size(660, 0),
			Margin = new Padding(0, 16, 0, 0)
		};
		// 2026-10-12 r8：归属段从 row 3 挪到 row 4（row 2、3 让给上面两个"组"容器）。
		// ⚠️ 这一步必须做：AutoSize 表里"没有 RowStyle 的行"行高为 0，正文会整段不可见。
		tableLayoutPanel.Controls.Add(control2, 0, 4);
		UiVisualStyle.ApplyWindow(this);
		tableLayoutPanel.BackColor = UiVisualStyle.Window;
		UiVisualStyle.ApplyTitle(label, 15f);
		ResponsiveLayout.PerformLayoutTree(this);
		ResponsiveLayout.ScaleFrom96(this, this);
		ResponsiveLayout.PerformLayoutTree(this);
		ClientSize = new Size(Math.Max(760, ClientSize.Width), tableLayoutPanel.Height);
		// 2026-10-12 r9（用户原话「图片居中对齐 现在右边空隙太大了」）：
		// 两组图片各自作为整体在"内容区（= 本表客户区 712 宽）"里水平居中。
		// 为什么不用布局引擎的 Anchor/百分比：2×2 拆成两个组容器后，图片对的**外侧 10px Margin 不计入**
		// TableLayoutPanel 的居中算式，落点会固定偏左 12px（本轮实测：左 102 / 右 150）。
		// 所以这里在两张表的 AutoSize 都算完之后，用**容器宽度**（含 Margin）一次性摆正：
		//   x = 父容器客户区宽 / 2 - 网格宽 / 2
		// 上排（两张竖版海报）与下排（两张方码）宽度不同（460 vs 480），各自算各自的 ⇒ 两组都居中。
		// ⚠️ 只改 Location，不动任何 Size / AutoSize / Margin。
		foreach (Control control6 in tableLayoutPanel.Controls)
		{
			if (control6 is TableLayoutPanel { ColumnCount: 2, AutoSize: true } tableLayoutPanel3 && tableLayoutPanel3.Parent != null)
			{
				tableLayoutPanel3.Left = (tableLayoutPanel3.Parent.ClientSize.Width - tableLayoutPanel3.Width) / 2;
			}
		}
		// 2026-10-12 r8：MakeQrCell 只在**外侧**留 10px、内侧贴中缝，内部 PictureBox 固定 220×220（正方形，Zoom 不裁）。
		// caption 传 null = ⛔ 不加下方小字（分组关系改由 MakeSection 的**组标题**表达，见上）。
		// MakeQr 的加载/回退逻辑一字未改，只把边长参数化。
		Control MakeQrCell(string path, string? caption, bool left)
		{
			TableLayoutPanel control2 = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 1,
				RowCount = (caption == null) ? 1 : 2,
				Margin = (left ? new Padding(0, 0, 10, 0) : new Padding(10, 0, 0, 0)),
				BackColor = BackColor
			};
			control2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			// ⚠️ 行序 = 视觉序（Dock.Top 先加者在上）：0 = 图片，1 = 说明（有 caption 时才有）。
			control2.RowStyles.Add(new RowStyle(SizeType.Absolute, 220f));
			control2.Controls.Add(MakeQr(path, left, 220), 0, 0);
			if (caption != null)
			{
				control2.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				Label label2 = new Label
				{
					Text = caption,
					Font = UiVisualStyle.Font(8.5f),
					ForeColor = UiVisualStyle.Muted,
					AutoSize = true,
					Dock = DockStyle.Fill,
					TextAlign = ContentAlignment.TopCenter,
					UseMnemonic = false,
					Margin = new Padding(0, 6, 0, 0)
				};
				control2.Controls.Add(label2, 0, 1);
			}
			return control2;
		}
		static PictureBox MakeQr(string path, bool left, int edge)
		{
			PictureBox pictureBox = new PictureBox
			{
				// ⚠️ 不 Dock.Fill：Dock 会覆盖 Size，正方形就没了；这里固定正方形 + Zoom，
				// 1280×1280 的方形素材缩到 edge×edge 后完整可见、无留白。
				Dock = DockStyle.None,
				Size = new Size(edge, edge),
				Margin = new Padding(0),
				SizeMode = PictureBoxSizeMode.Zoom,
				BackColor = UiVisualStyle.SurfaceRaised,
				BorderStyle = BorderStyle.None
			};
			bool flag = false;
			try
			{
				string text = Path.Combine(AppContext.BaseDirectory, "Resources", path);
				if (File.Exists(text))
				{
					using Image original = Image.FromFile(text);
					pictureBox.Image = new Bitmap(original);
					flag = true;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Donate qr load fail: " + ex.Message);
			}
			if (!flag)
			{
				Label value = new Label
				{
					Dock = DockStyle.Fill,
					TextAlign = ContentAlignment.MiddleCenter,
					Text = Strings.DonateQrMissing,
					ForeColor = UiVisualStyle.Muted,
					BackColor = UiVisualStyle.SurfaceRaised
				};
				pictureBox.Controls.Add(value);
			}
			return pictureBox;
		}
	}
}
