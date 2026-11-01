using System.Windows.Threading;

namespace Fmt.App;

// Live schedule; uses the PC clock, which matches the in-game clock.
public sealed class LandOfLifeView : PageView
{
    private static readonly (string Difference, string Exp)[] ExpRows =
    [
        ("-6 bis +4 Level", "100 % (Volle Erfahrung)"),
        ("+5 Level", "90 %"),
        ("+6 Level", "70 %"),
        ("+7 Level", "50 %"),
        ("+8 Level", "40 %"),
        ("+9 Level", "30 %"),
        ("+10 Level", "20 %"),
        ("+11 bis +19 Level", "10 %"),
        ("+20 Level oder mehr", "0 %"),
        ("-7 Level", "90 %"),
        ("-8 Level", "70 %"),
        ("-9 Level", "50 %"),
        ("-10 Level", "30 %"),
        ("-11 bis -19 Level", "10 %"),
        ("-20 Level oder weniger", "0 %"),
    ];

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
        Ui.AddTo(col, Ui.T("EXP nach Level-Unterschied", 14, bold: true), 16);
        Ui.AddTo(col, ExpCard(), 8);
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

    private static FrameworkElement ExpCard()
    {
        var col = new StackPanel();
        col.Children.Add(ExpRow("Level-Unterschied zum Monster", "Erhaltene EXP", "Muted", 12, false));
        foreach (var (difference, exp) in ExpRows)
            col.Children.Add(ExpRow(difference, exp, "Ink", 13, true));
        return Ui.Card(col, 6);
    }

    private static Border ExpRow(string difference, string exp, string brush, double size, bool line)
    {
        var row = new Border
        {
            Child = Ui.Cols("*,*", Ui.T(difference, size, brush), Ui.T(exp, size, brush)),
            Padding = new Thickness(10, 6, 10, 6),
            BorderThickness = new Thickness(0, line ? 1 : 0, 0, 0),
        };
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        return row;
    }
}
