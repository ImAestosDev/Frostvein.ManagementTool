namespace Fmt.App;

public sealed class DashboardView(AppState state, PanelWindow shell) : PageView
{
    public static readonly (string Id, string Name)[] Widgets =
    [
        ("stats", "Gold-Übersicht"), ("dailies", "Dailies"), ("chart", "Goldverlauf"),
        ("open", "Offene Posten"), ("breakdown", "Aufteilung"), ("recent", "Letzte Einträge"),
    ];

    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    public override void Refresh()
    {
        Content = _scroll;
        var offset = _scroll.VerticalOffset;
        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        foreach (var id in state.S.DashboardWidgets)
        {
            var widget = id switch
            {
                "stats" => StatsWidget(),
                "dailies" => DailiesWidget(),
                "chart" => ChartWidget(),
                "open" => OpenWidget(),
                "breakdown" => BreakdownWidget(),
                "recent" => RecentWidget(),
                _ => null,
            };
            Ui.AddTo(col, widget, 10);
        }
        if (col.Children.Count == 0)
            Ui.AddTo(col, Ui.T("Alle Widgets sind ausgeblendet. Unter Anpassen kannst du sie wieder einschalten.", 13, "Muted", wrap: true), 6);
        _scroll.Content = col;
        _scroll.ScrollToVerticalOffset(offset);
    }

    private static FrameworkElement Tile(string label, long value)
    {
        var number = Ui.T(Gold.FormatSigned(value), 22, bold: true);
        number.ToolTip = Gold.FormatFull(value) + " Gold";
        return Ui.Card(Ui.Col(2, Ui.T(label, 12, "Muted"), number), 11);
    }

    private FrameworkElement StatsWidget()
    {
        var d = state.Data;
        var today = state.Today;
        return new UniformRow(10,
            Tile("Heute", Stats.Net(d, today, today)),
            Tile("Diese Woche", Stats.Net(d, Stats.WeekStart(today), today)),
            Tile("Dieser Monat", Stats.Net(d, Stats.MonthStart(today), today)));
    }

    private FrameworkElement DailiesWidget()
    {
        var d = state.Data;
        var today = state.Today;
        var character = state.Character;
        var (done, total) = Stats.Progress(d, character, today, Recurrence.Daily);
        var (wDone, wTotal) = Stats.Progress(d, character, today, Recurrence.Weekly);
        var streak = Stats.Streak(d, character, today);

        var col = Ui.Col(8,
            Ui.Cols("*,Auto", Ui.T("Dailies", 13, bold: true),
                Ui.Row(10,
                    streak > 0 ? Ui.Row(4, Ui.Glyph(Ui.G.Flame, 12), Ui.T($"{streak} Tage", 12, "Muted")) : null,
                    Ui.T($"{done}/{total}", 13, bold: true))),
            Ui.Bar(total == 0 ? 0 : (double)done / total));

        var open = d.Tasks.Where(t => t.Active && t.CreatedDay <= today && !Stats.IsDone(d, t, character, today))
            .OrderBy(t => t.Recurrence).Take(5).ToList();
        if (total > 0 && done == total && wDone == wTotal)
            Ui.AddTo(col, new ContentControl { Content = Ui.T("Alles erledigt.", 13, "Muted"), Margin = new Thickness(0, 2, 0, 0) }, 6);
        foreach (var t in open)
        {
            var label = t.Recurrence == Recurrence.Weekly ? t.Name + "  (Woche)" : t.Name;
            var check = Ui.Check(label, false, _ => state.Mutate(() => Stats.SetDone(state.Data, t, state.Character, state.Today, true)));
            check.Margin = new Thickness(0, 2, 0, 0);
            Ui.AddTo(col, check, 6);
        }
        return Clickable(Ui.Card(col), () => shell.ShowPage("dailies"));
    }

    private static FrameworkElement Clickable(Border card, Action click)
    {
        card.Cursor = Cursors.Hand;
        card.MouseLeftButtonUp += (_, e) =>
        {
            // Checkboxes inside the card keep their own click.
            if (e.OriginalSource is DependencyObject source && FindCheck(source)) return;
            click();
        };
        return card;
    }

    private static bool FindCheck(DependencyObject? o)
    {
        for (; o != null; o = o is Visual ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is CheckBox or Button) return true;
        return false;
    }

    private FrameworkElement ChartWidget()
    {
        var series = Stats.DailySeries(state.Data, state.Today, 21);
        var col = Ui.Col(8, Ui.T("Goldverlauf · 21 Tage", 13, bold: true));
        if (series.All(v => v == 0))
            Ui.AddTo(col, Ui.T("Noch keine Golddaten. Sobald du Einträge schreibst, erscheint hier dein Verlauf.", 12, "Muted", wrap: true), 6);
        else
            Ui.AddTo(col, new GoldChart(series, state.Today), 6);
        return Ui.Card(col);
    }

    private FrameworkElement? OpenWidget()
    {
        var d = state.Data;
        var open = d.Entries
            .Select(e => (Entry: e, Category: d.FindCategory(e.CategoryId)))
            .Where(x => x.Category != null && Calc.IsOpen(x.Category, x.Entry))
            .OrderByDescending(x => x.Entry.Date).ToList();
        if (open.Count == 0) return null;

        long Bound(Category c, Entry e)
        {
            var f = c.Fields.FirstOrDefault(x => x.Type == FieldType.Gold && Calc.Numeric(c, e, x) != null);
            return f == null ? 0 : (long)Calc.Numeric(c, e, f)!.Value;
        }

        var bound = open.Sum(x => Bound(x.Category!, x.Entry));
        var col = Ui.Col(6, Ui.Cols("*,Auto", Ui.T($"Offene Posten · {open.Count}", 13, bold: true), Ui.T($"{Gold.Format(bound)} gebunden", 12, "Muted")));
        foreach (var (e, c) in open.Take(5))
            Ui.AddTo(col, EntryLine(c!, e, Ui.T(Gold.Format(Bound(c!, e)), 13, "Muted")), 6);
        return Ui.Card(col);
    }

    private FrameworkElement? BreakdownWidget()
    {
        var today = state.Today;
        var rows = Stats.ByCategory(state.Data, Stats.MonthStart(today), today);
        if (rows.Count == 0) return null;
        var max = rows.Max(r => Math.Abs(r.Gold));
        var col = Ui.Col(8, Ui.T("Aufteilung · dieser Monat", 13, bold: true));
        foreach (var (c, gold) in rows)
        {
            var bar = new Border { Height = 6, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(0, 3, 3, 0), Background = Ui.Hex(c.Color) };
            var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
            host.Children.Add(bar);
            var share = (double)Math.Abs(gold) / max;
            host.SizeChanged += (_, e) => bar.Width = Math.Max(2, e.NewSize.Width * share);
            var value = Ui.T(Gold.FormatSigned(gold), 12);
            value.Margin = new Thickness(8, 0, 0, 0);
            Ui.AddTo(col, Ui.Cols("92,*,62", Ui.T(c.Name, 12, trim: true), host, value), 6);
            value.HorizontalAlignment = HorizontalAlignment.Right;
        }
        return Ui.Card(col);
    }

    private FrameworkElement? RecentWidget()
    {
        var d = state.Data;
        var recent = d.Entries.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedUtc).Take(6).ToList();
        if (recent.Count == 0) return null;
        var col = Ui.Col(6, Ui.T("Letzte Einträge", 13, bold: true));
        foreach (var e in recent)
        {
            var c = d.FindCategory(e.CategoryId);
            if (c == null) continue;
            var gold = Calc.GoldValue(c, e);
            UIElement right = gold is { } g ? Ui.GoldText(g) : Ui.T(Calc.IsOpen(c, e) ? "offen" : "", 12, "Muted");
            Ui.AddTo(col, EntryLine(c, e, right), 6);
        }
        return Ui.Card(col);
    }

    private FrameworkElement EntryLine(Category c, Entry e, UIElement right)
    {
        var icon = Ui.GlyphColored(c.Icon, c.Color, 13);
        icon.Width = 20;
        var date = Ui.T(e.Date == state.Today ? "heute" : e.Date.ToString("dd.MM."), 11, "Muted");
        date.Margin = new Thickness(8, 0, 10, 0);
        var row = Ui.Cols("Auto,*,Auto,Auto", icon, Ui.T(Calc.Title(c, e), 13, trim: true), date, right);
        row.Background = Brushes.Transparent;
        row.Cursor = Cursors.Hand;
        row.MouseLeftButtonUp += (_, _) => shell.OpenEntry(e);
        return row;
    }
}
