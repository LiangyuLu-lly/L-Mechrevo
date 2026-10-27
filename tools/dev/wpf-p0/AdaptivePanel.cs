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

    protected override Size MeasureOverride(Size availableSize)
    {
        int visible = CountVisible();
        if (visible == 0)
        {
            return new Size(0, 0);
        }
        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width / visible;
        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }
            child.Measure(new Size(width, double.PositiveInfinity));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int visible = CountVisible();
        if (visible == 0)
        {
            return finalSize;
        }
        double width = finalSize.Width / visible;
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }
            child.Arrange(new Rect(index * width, 0, width, finalSize.Height));
            index++;
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
