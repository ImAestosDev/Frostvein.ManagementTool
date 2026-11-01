using System.Windows.Threading;

namespace Fmt.App;

// Live schedule; uses the PC clock, which matches the in-game clock.
public sealed class LandOfLifeView : PageView
{
    private readonly AppState _state;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };

    public LandOfLifeView(AppState state)
    {
        _state = state;
        Content = _scroll;
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public override void Refresh()
    {
        var offset = _scroll.VerticalOffset;
        var s = _state.S.LandOfLife;
        var now = DateTime.Now;
        var slots = WindowSchedule.Slots(s.WindowMinutes, s.MarkerOffsetMinutes);
        var current = WindowSchedule.StateAt(now, slots);

        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        Ui.AddTo(col, Ui.T(s.Name, 18, bold: true), 0);
        Ui.AddTo(col, CurrentCard(s, slots[current.SlotIndex], current, now), 10);
        Ui.AddTo(col, TableCard(s, slots, current.SlotIndex), 10);
        _scroll.Content = col;
        _scroll.ScrollToVerticalOffset(offset);
    }

    private static string Range(ScheduleSlot slot) =>
        $"{WindowSchedule.Clock(slot.StartMinute)} - {WindowSchedule.Clock(slot.EndMinute)}";

    private static string Left(TimeSpan span)
    {
        var minutes = (int)Math.Ceiling(span.TotalMinutes);
        return Duration.Format(minutes);
    }

    private static FrameworkElement CurrentCard(LandOfLifeSettings s, ScheduleSlot slot, ScheduleState state, DateTime now)
    {
        var markerAt = (int)state.NextMarker.TimeOfDay.TotalMinutes;
        var next = $"{s.MarkerLabel} um {WindowSchedule.Clock(markerAt)} · in {Left(state.NextMarker - now)}";
        return Ui.Card(Ui.Col(8,
            Ui.Cols("*,Auto", Ui.T("Aktuelles Fenster", 12, "Muted"), Ui.T($"noch {Left(state.LeftInSlot)}", 12, "Muted")),
            Ui.T(Range(slot), 22, bold: true),
            Ui.Bar(state.SlotProgress),
            Ui.Row(6, Ui.Glyph(Ui.G.Clock, 13, "Accent"), Ui.T(next, 13))));
    }

    private static FrameworkElement TableCard(LandOfLifeSettings s, List<ScheduleSlot> slots, int currentIndex)
    {
        var col = new StackPanel();
        for (var i = 0; i < slots.Count; i++)
        {
            var isCurrent = i == currentIndex;
            var brush = i < currentIndex ? "Muted" : "Ink";
            var marker = Ui.Row(6, Ui.Glyph(Ui.G.Clock, 12, isCurrent ? "Accent" : "Muted"), Ui.T(WindowSchedule.Clock(slots[i].MarkerMinute), 13, brush));
            marker.ToolTip = s.MarkerLabel;
            var row = new Border
            {
                Child = Ui.Cols("*,Auto", Ui.T(Range(slots[i]), 13, brush, bold: isCurrent), marker),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, i == 0 ? 0 : 2, 0, 0),
                BorderThickness = new Thickness(1),
            };
            row.SetResourceReference(Border.CornerRadiusProperty, "CornerSmall");
            if (isCurrent)
            {
                row.SetResourceReference(Border.BackgroundProperty, "Surface2");
                row.SetResourceReference(Border.BorderBrushProperty, "Accent");
            }
            else
            {
                row.BorderBrush = Brushes.Transparent;
            }
            col.Children.Add(row);
        }
        return Ui.Card(col, 6);
    }
}
