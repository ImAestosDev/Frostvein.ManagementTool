using System.Globalization;

namespace Fmt.App;

// Net gold per day: thin bars from a zero baseline, hover shows the exact day value.
public sealed class GoldChart : FrameworkElement
{
    private const double AxisHeight = 18, TopRoom = 16, Gap = 2, Corner = 3;
    private static readonly CultureInfo De = new("de-DE");

    private readonly long[] _values;
    private readonly DateOnly _lastDay;
    private int _hover = -1;

    public GoldChart(long[] values, DateOnly lastDay)
    {
        _values = values;
        _lastDay = lastDay;
        Height = 130;
        ClipToBounds = true;
    }

    private double ColumnWidth => ActualWidth / Math.Max(1, _values.Length);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = Math.Clamp((int)(e.GetPosition(this).X / ColumnWidth), 0, _values.Length - 1);
        if (index == _hover) return;
        _hover = index;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var max = _values.Select(v => Math.Max(v, 0)).Max();
        var min = _values.Select(v => Math.Min(v, 0)).Min();
        var range = (double)(max - min);
        var plot = ActualHeight - AxisHeight - TopRoom;
        var zeroY = TopRoom + (range == 0 ? plot : plot * max / range);
        var line = new Pen(Ui.Res("Line"), 1);
        line.Freeze();
        dc.DrawLine(line, new Point(0, zeroY), new Point(ActualWidth, zeroY));

        var width = Math.Min(14, Math.Max(3, ColumnWidth - Gap));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var i = 0; i < _values.Length; i++)
        {
            var cx = ColumnWidth * (i + 0.5);
            if (i == _hover) dc.DrawRectangle(Ui.Res("Surface2"), null, new Rect(ColumnWidth * i, 0, ColumnWidth, ActualHeight - AxisHeight));
            var v = _values[i];
            if (v != 0)
            {
                var h = Math.Max(2, Math.Abs(v) / range * plot);
                DrawBar(dc, cx - width / 2, zeroY, width, v > 0 ? -h : h, Ui.Res(v > 0 ? "Positive" : "Negative"));
            }
            // Mondays and the last day anchor the axis.
            var day = _lastDay.AddDays(i - _values.Length + 1);
            if (day.DayOfWeek == DayOfWeek.Monday || i == _values.Length - 1)
            {
                var label = Text(day.ToString("dd.MM.", De), 10, "Muted", dpi);
                var lx = Math.Clamp(cx - label.Width / 2, 0, Math.Max(0, ActualWidth - label.Width));
                dc.DrawText(label, new Point(lx, ActualHeight - AxisHeight + 3));
            }
        }

        if (_hover >= 0) DrawHover(dc, dpi);
        else if (max > 0)
        {
            var peak = Array.IndexOf(_values, max);
            var label = Text(Gold.Format(max), 10, "Muted", dpi);
            var lx = Math.Clamp(ColumnWidth * (peak + 0.5) - label.Width / 2, 0, Math.Max(0, ActualWidth - label.Width));
            dc.DrawText(label, new Point(lx, 1));
        }
    }

    private void DrawHover(DrawingContext dc, double dpi)
    {
        var day = _lastDay.AddDays(_hover - _values.Length + 1);
        var text = Text($"{day.ToString("ddd dd.MM.", De)}  {Gold.FormatSigned(_values[_hover])}", 12, "Ink", dpi);
        var w = text.Width + 14;
        var x = Math.Clamp(ColumnWidth * (_hover + 0.5) - w / 2, 0, Math.Max(0, ActualWidth - w));
        dc.DrawRoundedRectangle(Ui.Res("Surface"), new Pen(Ui.Res("Line"), 1), new Rect(x, 0, w, text.Height + 6), 4, 4);
        dc.DrawText(text, new Point(x + 7, 3));
    }

    private static void DrawBar(DrawingContext dc, double x, double baseY, double w, double h, Brush fill)
    {
        // Only the data end is rounded, the base sits flat on the zero line.
        var r = Math.Min(Corner, Math.Min(w / 2, Math.Abs(h)));
        var endY = baseY + h;
        var dir = h < 0 ? 1 : -1;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, baseY), true, true);
            c.LineTo(new Point(x, endY + dir * r), false, false);
            c.ArcTo(new Point(x + r, endY), new Size(r, r), 0, false, h < 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, false, false);
            c.LineTo(new Point(x + w - r, endY), false, false);
            c.ArcTo(new Point(x + w, endY + dir * r), new Size(r, r), 0, false, h < 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, false, false);
            c.LineTo(new Point(x + w, baseY), false, false);
        }
        g.Freeze();
        dc.DrawGeometry(fill, null, g);
    }

    private static FormattedText Text(string s, double size, string brush, double dpi) =>
        new(s, De, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Ui.Res(brush), dpi);
}
