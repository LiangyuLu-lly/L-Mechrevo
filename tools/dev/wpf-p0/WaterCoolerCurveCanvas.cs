using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using L_Mechrevo.WpfP0.Services;

namespace L_Mechrevo.WpfP0;

/// <summary>水冷风扇曲线画布：温度 30..100℃，占空比 0..100%。</summary>
public sealed class WaterCoolerCurveCanvas : FrameworkElement
{
    private List<WaterCoolerCurvePoint> _points = WaterCoolerCurve.DefaultPoints.ToList();
    private int _dragIndex = -1;

    internal event Action? Changed;

    public WaterCoolerCurveCanvas()
    {
        Height = 190;
        MinHeight = 150;
        Cursor = Cursors.Hand;
        ClipToBounds = true;
        Focusable = true;
    }

    internal IReadOnlyList<WaterCoolerCurvePoint> Points => _points.Select(p => p with { }).ToArray();

    internal void Load(IReadOnlyList<WaterCoolerCurvePoint> points)
    {
        _points = (points ?? WaterCoolerCurve.DefaultPoints)
            .OrderBy(p => p.Temp)
            .Select(p => new WaterCoolerCurvePoint(Math.Clamp(p.Temp, 30, 100), Math.Clamp(p.FanDuty, 0, 100)))
            .ToList();
        if (_points.Count < 2)
        {
            _points = WaterCoolerCurve.DefaultPoints.ToList();
        }
        InvalidateVisual();
    }

    private (double left, double top, double width, double height) Plot()
    {
        return (44, 16, Math.Max(10, ActualWidth - 60), Math.Max(10, ActualHeight - 46));
    }

    private Point PointAt(int index, (double left, double top, double width, double height) plot)
    {
        double x = plot.left + (Math.Clamp(_points[index].Temp, 30, 100) - 30) * plot.width / 70.0;
        double y = plot.top + plot.height * (100 - Math.Clamp(_points[index].FanDuty, 0, 100)) / 100.0;
        return new Point(x, y);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var plot = Plot();
        bool dark = AppTheme.IsDark;
        Color back = dark ? Color.FromArgb(0x88, 0x16, 0x1B, 0x23) : Color.FromArgb(0xF2, 0xF5, 0xF7, 0xFA);
        Color grid = dark ? Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x26, 0x00, 0x00, 0x00);
        Color border = dark ? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x44, 0x00, 0x00, 0x00);
        Color label = dark ? Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xAA, 0x1B, 0x20, 0x29);
        Color fill = dark ? Color.FromArgb(0xCC, 0x16, 0x1B, 0x23) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        dc.DrawRectangle(new SolidColorBrush(back), null, new Rect(0, 0, ActualWidth, ActualHeight));
        Pen gridPen = new(new SolidColorBrush(grid), 1);
        Pen borderPen = new(new SolidColorBrush(border), 1);
        for (int i = 0; i <= 4; i++)
        {
            double y = plot.top + plot.height * i / 4.0;
            dc.DrawLine(gridPen, new Point(plot.left, y), new Point(plot.left + plot.width, y));
            DrawText(dc, (100 - i * 25) + "%", 3, y - 7, 10, label);
        }
        for (int i = 0; i <= 5; i++)
        {
            double x = plot.left + plot.width * i / 5.0;
            dc.DrawLine(gridPen, new Point(x, plot.top), new Point(x, plot.top + plot.height));
            DrawText(dc, (30 + i * 14) + "°", x - 8, plot.top + plot.height + 8, 9, label);
        }
        dc.DrawRectangle(null, borderPen, new Rect(plot.left, plot.top, plot.width, plot.height));
        if (_points.Count > 0)
        {
            StreamGeometry geo = new();
            using (StreamGeometryContext ctx = geo.Open())
            {
                for (int i = 0; i < _points.Count; i++)
                {
                    Point p = PointAt(i, plot);
                    if (i == 0)
                    {
                        ctx.BeginFigure(p, false, false);
                    }
                    else
                    {
                        ctx.LineTo(p, true, false);
                    }
                }
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), 2), geo);
        }
        Brush pointBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
        for (int i = 0; i < _points.Count; i++)
        {
            Point p = PointAt(i, plot);
            dc.DrawEllipse(new SolidColorBrush(fill), new Pen(pointBrush, 2), p, 5, 5);
            if (_dragIndex == i)
            {
                DrawText(dc, _points[i].Temp + "° " + _points[i].FanDuty + "%", p.X - 30, p.Y - 26, 10, label);
            }
        }
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Color color)
    {
        FormattedText ft = new(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            new SolidColorBrush(color),
            1.0);
        dc.DrawText(ft, new Point(x, y));
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        var plot = Plot();
        Point pos = e.GetPosition(this);
        int best = -1;
        double bestDistance = 20 * 20;
        for (int i = 0; i < _points.Count; i++)
        {
            Point p = PointAt(i, plot);
            double dx = p.X - pos.X;
            double dy = p.Y - pos.Y;
            double d = dx * dx + dy * dy;
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        if (best >= 0)
        {
            _dragIndex = best;
            Focus();
            CaptureMouse();
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_dragIndex < 0 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var plot = Plot();
        Point pos = e.GetPosition(this);
        int temp = (int)Math.Round(30 + (pos.X - plot.left) * 70.0 / plot.width, MidpointRounding.AwayFromZero);
        int duty = (int)Math.Round((plot.top + plot.height - pos.Y) * 100.0 / plot.height, MidpointRounding.AwayFromZero);
        temp = Math.Clamp(temp, 30, 100);
        if (_dragIndex > 0)
        {
            temp = Math.Max(_points[_dragIndex - 1].Temp + 2, temp);
        }
        if (_dragIndex < _points.Count - 1)
        {
            temp = Math.Min(_points[_dragIndex + 1].Temp - 2, temp);
        }
        duty = Math.Clamp(duty, 0, 100);
        WaterCoolerCurvePoint updated = new(temp, duty);
        if (updated == _points[_dragIndex])
        {
            e.Handled = true;
            return;
        }
        _points[_dragIndex] = updated;
        e.Handled = true;
        Changed?.Invoke();
        InvalidateVisual();
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            e.Handled = true;
            ReleaseMouseCapture();
            Changed?.Invoke();
            InvalidateVisual();
        }
    }
}
