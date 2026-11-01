using System;
using System.Windows;
using System.Windows.Controls;

namespace L_Mechrevo.WpfP0;

/// <summary>
/// 等宽自适应分段面板：只给可见子项分宽度，隐藏项不占位。
/// </summary>
public sealed class AdaptivePanel : Panel
{
    /// <summary>兼容旧 UniformGrid 代码里的 Columns 赋值；布局本身按可见子项等分。</summary>
    public int Columns { get; set; }

    /// <summary>按内容宽度分配（模式按钮用）；默认 false 保持原来的等宽行为。</summary>
    public bool UseContentWidths { get; set; }

    private readonly System.Collections.Generic.List<double> _contentWidths = new();

    protected override Size MeasureOverride(Size availableSize)
    {
        int visible = CountVisible();
        if (visible == 0)
        {
            return new Size(0, 0);
        }
        double height = 0;
        if (!UseContentWidths)
        {
            double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width / visible;
            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                child.Measure(new Size(width, double.PositiveInfinity));
                height = Math.Max(height, child.DesiredSize.Height);
            }
            return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
        }

        // 先按内容测量，再把内容宽度按可用宽度等比例缩放/填充。
        _contentWidths.Clear();
        double total = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                _contentWidths.Add(0);
                continue;
            }
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Max(1, child.DesiredSize.Width);
            _contentWidths.Add(w);
            total += w;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        double finalWidth = double.IsInfinity(availableSize.Width) ? total : availableSize.Width;
        return new Size(finalWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int visible = CountVisible();
        if (visible == 0)
        {
            return finalSize;
        }
        if (!UseContentWidths)
        {
            double width = finalSize.Width / visible;
            int index = 0;
            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                child.Arrange(new Rect(index * width, 0, width, finalSize.Height));
                index++;
            }
            return finalSize;
        }

        double total = 0;
        foreach (double w in _contentWidths) total += w;
        double scale = total <= 0 ? 1 : finalSize.Width / total;
        // 内容总宽小于可用宽：按比例放大，让按钮铺满；大于可用宽：按比例压缩。
        int childIndex = 0;
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                childIndex++;
                continue;
            }
            double w = (childIndex < _contentWidths.Count ? _contentWidths[childIndex] : 1) * scale;
            if (w < 1) w = 1;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
            childIndex++;
        }
        return finalSize;
    }

    private int CountVisible()
    {
        int count = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility != Visibility.Collapsed)
            {
                count++;
            }
        }
        return count;
    }
}
