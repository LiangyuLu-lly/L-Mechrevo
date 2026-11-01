using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace L_Mechrevo.WpfP0;

internal sealed class RingGauge : FrameworkElement
{
    private double _value;
    private double _maximum = 100;
    private string _caption = "";

    internal void SetValue(double value, double maximum, string caption)
    {
        _value = value;
        _maximum = Math.Max(1, maximum);
        _caption = caption;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double size = Math.Max(1, Math.Min(ActualWidth, ActualHeight));
        Point center = new(size / 2, size / 2);
        double radius = Math.Max(1, size / 2 - 9);
        Brush track = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
        Brush accent = (Brush?)Application.Current.Resources["AccentBrush"] ?? Brushes.DeepSkyBlue;
        Pen trackPen = new(track, 8);
        Pen accentPen = new(accent, 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        double ratio = Math.Clamp(_value / _maximum, 0, 1);
        if (ratio >= 0.999)
        {
            // 100% 时起点和终点重合，单个 ArcTo 不画线；直接画整圆。
            drawingContext.DrawEllipse(null, accentPen, center, radius, radius);
        }
        else if (ratio > 0.001)
        {
            double startAngle = -90;
            double endAngle = -90 + 360 * ratio;
            Point start = PointOnCircle(center, radius, startAngle);
            Point end = PointOnCircle(center, radius, endAngle);
            StreamGeometry geometry = new();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(start, false, false);
                context.ArcTo(end, new Size(radius, radius), 0, ratio > 0.5,
                    SweepDirection.Clockwise, true, false);
            }
            drawingContext.DrawGeometry(null, accentPen, geometry);
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        string valueText = Math.Round(_value).ToString(CultureInfo.InvariantCulture);
        FormattedText value = new(valueText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            20, (Brush?)Application.Current.Resources["TextBrush"] ?? Brushes.White, pixelsPerDip);
        FormattedText caption = new(_caption, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            10, (Brush?)Application.Current.Resources["MutedBrush"] ?? Brushes.Gray, pixelsPerDip);
        drawingContext.DrawText(value, new Point(center.X - value.Width / 2, center.Y - value.Height / 2 - 2));
        drawingContext.DrawText(caption, new Point(center.X - caption.Width / 2, center.Y + value.Height / 2 - 1));
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        double radians = angle * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
