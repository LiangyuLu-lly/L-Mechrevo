using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace L_Mechrevo.WpfP0;

/// <summary>
/// 托盘菜单渲染器（2026-10-26 · P0-5 迁移）。
/// <para>由旧 WinForms UI 的 <c>CustomMenuRenderer</c> 等价迁入 WPF 项目内，
/// ⛔ 不再依赖旧 UI 命名空间（旧 UI 最终删除时托盘菜单不受影响）。</para>
/// <para>视觉逐项对齐迁移前的观感：主题底色/前景色（由 <see cref="TrayIconService.SetTheme" /> 写在
/// ContextMenuStrip 的 BackColor/ForeColor 上，本渲染器只读取）、内嵌 HarmonyOS 字体、
/// hover/选中行、打勾、禁用项、分隔线、背景与图像边距。</para>
/// <para>⚠️ 旧渲染器本身是**平角**（每行用整块矩形填充，没有圆角）—— 迁移保持平角，不引入 WPF 主题圆角，
/// 以免视觉变化；若后续需要圆角菜单，应作为独立视觉调整任务。</para>
/// </summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    /// <summary>字体缓存锁（与旧 <c>UiVisualStyle.Font</c> 一样：同一 (字号,字重,单位) 进程内复用同一 Font 实例）。</summary>
    private static readonly object FontCacheLock = new();

    private static readonly Dictionary<(int SizeKey, FontStyle Style, GraphicsUnit Unit), Font> FontCache = new();

    /// <summary>内嵌字体集合：与旧 <c>UiVisualStyle</c> 一致，从输出目录 Fonts/ 读取（字体未安装也能用）。</summary>
    private static readonly PrivateFontCollection EmbeddedFonts = new();

    private static readonly Lazy<FontFamily> DefaultFontFamily =
        new(LoadFontFamily, LazyThreadSafetyMode.ExecutionAndPublication);

    internal TrayMenuRenderer()
        : base(new ProfessionalColorTable())
    {
    }

    /// <summary>
    /// 菜单字体（等价旧 <c>UiVisualStyle.Font(9f)</c>）：优先内嵌 HarmonyOS Sans SC（输出目录 Fonts/），
    /// 其次系统已安装的同名字体，最后回退 Microsoft YaHei UI。
    /// </summary>
    internal static Font MenuFont(float size, FontStyle style = FontStyle.Regular)
    {
        int sizeKey = (int)Math.Round(size * 10f);
        (int SizeKey, FontStyle Style, GraphicsUnit Unit) key = (sizeKey, style, GraphicsUnit.Point);
        lock (FontCacheLock)
        {
            if (FontCache.TryGetValue(key, out Font? cached))
            {
                return cached;
            }
            Font created = new(DefaultFontFamily.Value, sizeKey / 10f, style, GraphicsUnit.Point);
            FontCache[key] = created;
            return created;
        }
    }

    /// <summary>读取内嵌字体（与旧 <c>UiVisualStyle.LoadEmbeddedFonts</c> 同口径：Fonts/ 两个 ttf，按族名包含 "HarmonyOS Sans SC" 匹配）。</summary>
    private static FontFamily LoadFontFamily()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
            foreach (string name in new[] { "HarmonyOS_Sans_SC_Regular.ttf", "HarmonyOS_Sans_SC_Bold.ttf" })
            {
                string file = Path.Combine(dir, name);
                if (File.Exists(file))
                {
                    EmbeddedFonts.AddFontFile(file);
                }
            }
            FontFamily? embedded = EmbeddedFonts.Families.FirstOrDefault(
                family => family.Name.Contains("HarmonyOS Sans SC", StringComparison.Ordinal));
            if (embedded != null)
            {
                return embedded;
            }
        }
        catch
        {
            // 内嵌字体加载失败（文件缺失/损坏）⇒ 走下面的回退，⛔ 不影响菜单可用性
        }
        try
        {
            return new FontFamily("HarmonyOS Sans SC");
        }
        catch
        {
            return new FontFamily("Microsoft YaHei UI");
        }
    }

    /// <summary>等价旧 <c>UiVisualStyle.OutlinePen(Graphics, ...)</c>：按绘制 DC 的 DPI 把逻辑线宽换成设备像素（最小 1px，AwayFromZero 取整）。</summary>
    private static Pen CreateOutlinePen(Graphics graphics, Color color, float logicalWidth, float minDevicePx, bool inset)
    {
        float dpi = Math.Max(96f, graphics.DpiX);
        float deviceWidth = Math.Max(minDevicePx, (float)Math.Round(logicalWidth * dpi / 96f, MidpointRounding.AwayFromZero));
        Pen pen = new(color, deviceWidth);
        if (inset && deviceWidth > 1f)
        {
            pen.Alignment = PenAlignment.Inset;
        }
        return pen;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        // 与旧实现逐字一致：整行矩形（平角）；hover/选中且可用 ⇒ SystemColors.Highlight；否则菜单底色。
        Rectangle rect = new(0, 0, e.Item.Width, e.Item.Height);
        Color backgroundColor = e.Item.Owner?.BackColor ?? SystemColors.Menu;
        if (e.Item.Selected && e.Item.Enabled)
        {
            using SolidBrush highlight = new(SystemColors.Highlight);
            e.Graphics.FillRectangle(highlight, rect);
            return;
        }
        using SolidBrush background = new(backgroundColor);
        e.Graphics.FillRectangle(background, rect);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // 与旧实现一致：勾选标记手绘 "✓"（不用默认位图），颜色跟随选中/前景色。
        if (e.Item is ToolStripMenuItem { Checked: not false })
        {
            Color foreColor = e.Item.Selected ? SystemColors.HighlightText : (e.Item.Owner?.ForeColor ?? SystemColors.MenuText);
            Rectangle bounds = new(0, 0, e.ImageRectangle.Right + e.ImageRectangle.X, e.Item.Height);
            TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, bounds, foreColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // 与旧实现一致：禁用项 = 前景色 130/255 透明度；选中 = HighlightText；否则菜单前景色。
        if (!e.Item.Enabled)
        {
            e.TextColor = Color.FromArgb(130, e.Item.Owner?.ForeColor ?? SystemColors.MenuText);
        }
        else if (e.Item.Selected)
        {
            e.TextColor = SystemColors.HighlightText;
        }
        else
        {
            e.TextColor = e.Item.Owner?.ForeColor ?? SystemColors.MenuText;
        }
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int middle = e.Item.Height / 2;
        Color baseColor = e.ToolStrip?.ForeColor ?? SystemColors.MenuText;
        using Pen pen = CreateOutlinePen(e.Graphics, Color.FromArgb(50, baseColor), 1f, 1f, inset: false);
        e.Graphics.DrawLine(pen, 0, middle, e.Item.Width, middle);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using SolidBrush background = new(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(background, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // 与旧实现一致：图像边距区域用菜单底色填充（不画勾选列背景）。
        using SolidBrush background = new(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(background, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // 与旧实现一致：不画菜单边框。
    }
}
