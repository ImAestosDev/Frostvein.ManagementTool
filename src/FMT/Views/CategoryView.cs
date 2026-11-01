using System.IO;
using Microsoft.Win32;

namespace Fmt.App;

// Generic log page; everything it shows comes from the category's field definitions.
public sealed class CategoryView : PageView
{
    private enum Period { All, Today, Week, Month }
    private enum Sort { Newest, Oldest, GoldDesc, GoldAsc }

    private readonly AppState _state;
    private readonly string _categoryId;
    private readonly PanelWindow _shell;
    private readonly Grid _root = new();
    private readonly DockPanel _main = new();
    private readonly StackPanel _head = new() { Margin = new Thickness(14, 12, 12, 8) };
    private readonly TextBlock _summary = Ui.T("", 12, "Muted");
    private readonly TextBlock _sumLabel = Ui.T("", 13, "Muted");
    private readonly TextBlock _sumValue = Ui.T("", 22, bold: true);
    private readonly ListBox _list = new();
    private readonly TextBox _search;
    private CheckBox? _openToggle;
    private FrameworkElement? _editor;
    private Period _period;
    private Sort _sort;
    private bool _onlyOpen;

    private Category? Category => _state.Data.FindCategory(_categoryId);

    public CategoryView(AppState state, string categoryId, PanelWindow shell)
    {
        _state = state;
        _categoryId = categoryId;
        _shell = shell;
        _search = Ui.Input("", _ => FillList(), "Suchen …");

        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);
        _list.ItemTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(RowHost)) };
        _list.Margin = new Thickness(14, 0, 6, 12);

        DockPanel.SetDock(_head, Dock.Top);
        _main.Children.Add(_head);
        _main.Children.Add(_list);
        _root.Children.Add(_main);
        Content = _root;
        BuildHead();
    }

    private void BuildHead()
    {
        _head.Children.Clear();
        (_search.Parent as Panel)?.Children.Remove(_search);
        (_summary.Parent as Panel)?.Children.Remove(_summary);
        var c = Category;
        if (c == null) return;
        var add = Ui.Btn("Neu", () => OpenEditor(null), "Primary", Ui.G.Add, "Neuer Eintrag (Strg+N)");
        var csv = Ui.IconBtn(Ui.G.Export, "Als CSV exportieren", ExportCsv);
        var layout = Ui.IconBtn(Ui.G.Sliders, "Felder dieser Kategorie anpassen", () => _shell.ShowPage("layout"));
        add.Margin = new Thickness(6, 0, 0, 0);
        var title = Ui.Row(8, Ui.GlyphColored(c.Icon, c.Color, 18), Ui.T(c.Name, 18, bold: true));
        _head.Children.Add(Ui.Cols("*,Auto,Auto,Auto", title, csv, layout, add));

        if (c.Fields.Any(f => f.Id == c.SumFieldId))
        {
            (_sumLabel.Parent as Panel)?.Children.Remove(_sumLabel);
            (_sumValue.Parent as Panel)?.Children.Remove(_sumValue);
            var sumCard = Ui.Card(Ui.Cols("*,Auto", _sumLabel, _sumValue));
            sumCard.Margin = new Thickness(0, 10, 0, 0);
            _head.Children.Add(sumCard);
        }

        var period = Ui.Combo([("Alle", Period.All), ("Heute", Period.Today), ("Woche", Period.Week), ("Monat", Period.Month)], _period, v => { _period = v; FillList(); });
        (string, Sort)[] sorts = c.GoldFieldId != null
            ? [("Neueste", Sort.Newest), ("Älteste", Sort.Oldest), ("Gold hoch", Sort.GoldDesc), ("Gold tief", Sort.GoldAsc)]
            : [("Neueste", Sort.Newest), ("Älteste", Sort.Oldest)];
        var sort = Ui.Combo(sorts, _sort, v => { _sort = v; FillList(); });
        period.Margin = new Thickness(6, 0, 0, 0);
        sort.Margin = new Thickness(6, 0, 0, 0);
        var filters = Ui.Cols("*,84,96", _search, period, sort);
        filters.Margin = new Thickness(0, 10, 0, 0);
        _head.Children.Add(filters);

        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var hasOpenState = c.Fields.FirstOrDefault(f => f.Id == c.GoldFieldId)?.Type == FieldType.Formula;
        _openToggle = hasOpenState ? Ui.Check("Nur offene", _onlyOpen, v => { _onlyOpen = v; FillList(); }) : null;
        if (_openToggle != null)
        {
            DockPanel.SetDock(_openToggle, Dock.Right);
            bottom.Children.Add(_openToggle);
        }
        bottom.Children.Add(_summary);
        _head.Children.Add(bottom);
    }

    public override void Refresh()
    {
        if (Category == null)
        {
            _shell.ShowPage("dashboard");
            return;
        }
        BuildHead();
        FillList();
    }

    private void FillList()
    {
        var c = Category;
        if (c == null) return;
        var scroll = Ui.FindChild<ScrollViewer>(_list);
        var offset = scroll?.VerticalOffset ?? 0;

        var entries = Filtered(c).ToList();
        var items = new List<RowItem>();
        var byDate = _sort is Sort.Newest or Sort.Oldest;
        DateOnly? lastDate = null;
        foreach (var e in entries)
        {
            if (byDate && e.Date != lastDate)
            {
                lastDate = e.Date;
                var day = e.Date;
                var dayGold = entries.Where(x => x.Date == day).Sum(x => Calc.GoldValue(c, x) ?? 0);
                items.Add(new RowItem(() => DayHeader(day, dayGold)));
            }
            items.Add(new RowItem(() => EntryRow(c, e)));
        }
        _list.ItemsSource = items;

        var total = entries.Sum(e => Calc.GoldValue(c, e) ?? 0);
        var goldPart = c.GoldFieldId != null ? $" · Summe {Gold.FormatSigned(total)}" : "";
        _summary.Text = $"{entries.Count} {(entries.Count == 1 ? "Eintrag" : "Einträge")}{goldPart}";
        var sumField = c.Fields.FirstOrDefault(f => f.Id == c.SumFieldId);
        if (sumField != null)
        {
            var sum = entries.Sum(e => Calc.Numeric(c, e, sumField) ?? 0);
            _sumValue.Text = sumField.Type == FieldType.Euro ? Money.Format(sum) : sum.ToString("0.##", new System.Globalization.CultureInfo("de-DE"));
            _sumLabel.Text = _period switch
            {
                Period.Today => "Summe heute",
                Period.Week => "Summe diese Woche",
                Period.Month => "Summe dieser Monat",
                _ => "Gesamtsumme",
            };
        }

        if (items.Count == 0) ShowEmptyHint();
        else _emptyHint?.SetValue(VisibilityProperty, Visibility.Collapsed);
        if (scroll != null && offset > 0) Dispatcher.BeginInvoke(() => Ui.FindChild<ScrollViewer>(_list)?.ScrollToVerticalOffset(offset));
    }

    private TextBlock? _emptyHint;

    private void ShowEmptyHint()
    {
        if (_emptyHint == null)
        {
            _emptyHint = Ui.T("", 13, "Muted", wrap: true);
            _emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyHint.TextAlignment = TextAlignment.Center;
            _emptyHint.Margin = new Thickness(40, 170, 40, 0);
            _emptyHint.VerticalAlignment = VerticalAlignment.Top;
            _emptyHint.IsHitTestVisible = false;
            _root.Children.Add(_emptyHint);
        }
        var hasAny = _state.Data.Entries.Any(e => e.CategoryId == _categoryId);
        _emptyHint.Text = hasAny ? "Nichts gefunden. Ändere Suche oder Filter." : "Noch keine Einträge. Mit „Neu“ legst du den ersten an.";
        _emptyHint.Visibility = Visibility.Visible;
    }

    private IEnumerable<Entry> Filtered(Category c)
    {
        var today = _state.Today;
        var from = _period switch
        {
            Period.Today => today,
            Period.Week => Stats.WeekStart(today),
            Period.Month => Stats.MonthStart(today),
            _ => DateOnly.MinValue,
        };
        var q = _search.Text.Trim();
        var result = _state.Data.Entries.Where(e => e.CategoryId == _categoryId && e.Date >= from);
        if (_onlyOpen) result = result.Where(e => Calc.IsOpen(c, e));
        if (q.Length > 0) result = result.Where(e => Matches(c, e, q));
        return _sort switch
        {
            Sort.Oldest => result.OrderBy(e => e.Date).ThenBy(e => e.CreatedUtc),
            Sort.GoldDesc => result.OrderByDescending(e => Calc.GoldValue(c, e) ?? long.MinValue),
            Sort.GoldAsc => result.OrderBy(e => Calc.GoldValue(c, e) ?? long.MaxValue),
            _ => result.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedUtc),
        };
    }

    private static bool Matches(Category c, Entry e, string q)
    {
        if (e.Character.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        return c.Fields.Any(f => Calc.Display(c, e, f).Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private FrameworkElement DayHeader(DateOnly day, long gold)
    {
        var label = Ui.T(Ui.DateLabel(day, _state.Today), 11, "Muted", bold: true);
        var c = Category;
        var showGold = c?.GoldFieldId != null && gold != 0;
        var row = Ui.Cols("*,Auto", label, showGold ? Ui.GoldText(gold, 11) : null);
        row.Margin = new Thickness(2, 10, 8, 4);
        return row;
    }

    private FrameworkElement EntryRow(Category c, Entry e)
    {
        var compact = _state.S.Theme.Compact;
        var titleField = c.Fields.FirstOrDefault(f => f.Type is FieldType.Text or FieldType.Select && Calc.Display(c, e, f).Length > 0);
        var parts = c.Fields
            .Where(f => f.ShowInList && f != titleField && f.Id != c.GoldFieldId && (titleField != null || f.Id != c.SumFieldId))
            .Select(f => (f.Name, Value: Calc.Display(c, e, f)))
            .Where(p => p.Value.Length > 0)
            .Select(p => OneLine(p.Name, p.Value)).ToList();
        if (_state.Data.Characters.Count > 1 && e.Character.Length > 0) parts.Insert(0, e.Character);

        var left = Ui.Col(1, Ui.T(Calc.Title(c, e), 13, bold: true, trim: true));
        if (parts.Count > 0) left.Children.Add(Ui.T(string.Join("  ·  ", parts), 12, "Muted", trim: true));

        var gold = Calc.GoldValue(c, e);
        UIElement right = gold is { } g
            ? Ui.GoldText(g, 14)
            : Calc.IsOpen(c, e) ? OpenBadge() : Ui.T("", 12);

        var actions = Ui.Row(0,
            Ui.IconBtn(Ui.G.Copy, "Duplizieren", () => Duplicate(e), 12),
            Ui.IconBtn(Ui.G.Delete, "Löschen", () => Delete(c, e), 12));
        actions.Visibility = Visibility.Collapsed;
        actions.Margin = new Thickness(6, 0, 0, 0);

        var grid = Ui.Cols("*,Auto,Auto", left, right, actions);
        var row = new Border { Child = grid, Padding = compact ? new Thickness(10, 4, 8, 4) : new Thickness(10, 8, 8, 8), Margin = new Thickness(0, 0, 8, compact ? 2 : 4), Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
        row.SetResourceReference(Border.BackgroundProperty, "Surface");
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        row.SetResourceReference(Border.CornerRadiusProperty, "CornerSmall");
        row.MouseEnter += (_, _) => { actions.Visibility = Visibility.Visible; row.SetResourceReference(Border.BorderBrushProperty, "Accent"); };
        row.MouseLeave += (_, _) => { actions.Visibility = Visibility.Collapsed; row.SetResourceReference(Border.BorderBrushProperty, "Line"); };
        row.MouseLeftButtonUp += (_, _) => OpenEditor(e);
        return row;
    }

    // Long notes stay on one row in the list.
    private static string OneLine(string name, string value) =>
        $"{name} {value.ReplaceLineEndings(" ")}";

    private static FrameworkElement OpenBadge()
    {
        var b = new Border { Child = Ui.T("offen", 11), Padding = new Thickness(7, 1, 7, 2), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BorderBrushProperty, "Accent");
        return b;
    }

    private void Duplicate(Entry e) => _state.Mutate(() => _state.Data.Entries.Add(new Entry
    {
        CategoryId = e.CategoryId,
        Date = _state.Today,
        Character = _state.Character,
        Values = new Dictionary<string, string>(e.Values),
    }), "Eintrag dupliziert");

    private void Delete(Category c, Entry e) =>
        _state.Mutate(() => _state.Data.Entries.RemoveAll(x => x.Id == e.Id), $"\"{Calc.Title(c, e)}\" gelöscht");

    private void ExportCsv()
    {
        var c = Category;
        if (c == null) return;
        var dialog = new SaveFileDialog { FileName = $"FMT-{c.Name}-{DateTime.Now:yyyy-MM-dd}.csv", Filter = "CSV|*.csv" };
        if (dialog.ShowDialog(_shell) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, CsvExport.Build(_state.Data, c), new System.Text.UTF8Encoding(true));
            _state.Notify("CSV exportiert");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _state.Notify($"Export fehlgeschlagen: {ex.Message}");
        }
    }

    public void OpenEditor(Entry? entry)
    {
        var c = Category;
        if (c == null) return;
        CloseEditor();
        _editor = new EntryEditor(_state, c, entry, CloseEditor, () => OpenEditor(null));
        _root.Children.Add(_editor);
    }

    private void CloseEditor()
    {
        if (_editor != null) _root.Children.Remove(_editor);
        _editor = null;
    }

    public override bool HandleEscape()
    {
        if (_editor == null) return false;
        CloseEditor();
        return true;
    }

}

public sealed record RowItem(Func<FrameworkElement> Build);

// Builds its row lazily, so only visible rows exist.
public sealed class RowHost : ContentControl
{
    public RowHost()
    {
        DataContextChanged += (_, e) => Content = (e.NewValue as RowItem)?.Build();
    }
}
