using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace L_Mechrevo.WpfP0.Pages;

public partial class CustomPageView : UserControl
{
    public CustomPageView()
    {
        InitializeComponent();
        CustomPage.PreviewMouseWheel += CustomPage_PreviewMouseWheel;
    }

    private void CustomPage_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node != null && !ReferenceEquals(node, CustomPage))
        {
            if (node is ComboBox combo && combo.IsDropDownOpen)
            {
                return;
            }
            if (node is ScrollViewer inner && !ReferenceEquals(inner, CustomPage))
            {
                bool canScrollInner = e.Delta > 0
                    ? inner.VerticalOffset > 0
                    : inner.VerticalOffset < inner.ScrollableHeight;
                if (canScrollInner)
                {
                    return;
                }
            }
            node = VisualTreeHelper.GetParent(node);
        }
        CustomPage.ScrollToVerticalOffset(CustomPage.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
