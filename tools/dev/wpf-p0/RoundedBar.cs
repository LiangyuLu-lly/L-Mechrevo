using System.Windows;
using System.Windows.Media;

namespace L_Mechrevo.WpfP0;

internal sealed class RoundedBar : FrameworkElement
{
    private double _value;
    private double _maximum = 100;

    internal double Maximum
    {
        get => _maximum;
        set => _maximum = Math.Max(1, value);
    }

    internal void SetValue(double value, double maximum)
    {
        _value = value;
        _maximum = Math.Max(1, maximum);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = Math.Max(0, ActualWidth);
        double height = Math.Max(1, ActualHeight);
        double radius = height / 2;
        Brush track = (Brush?)Application.Current.Resources["TrackBrush"] ?? Brushes.DimGray;
        Brush accent = (Brush?)Application.Current.Resources["AccentBrush"] ?? Brushes.DeepSkyBlue;
        drawingContext.DrawRoundedRectangle(track, null, new Rect(0, 0, width, height), radius, radius);
        double ratio = Math.Clamp(_value / _maximum, 0, 1);
        if (ratio > 0)
        {
            drawingContext.DrawRoundedRectangle(accent, null, new Rect(0, 0, width * ratio, height), radius, radius);
        }
    }
}
