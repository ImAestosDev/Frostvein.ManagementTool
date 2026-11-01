using System.Globalization;
using System.Windows.Controls.Primitives;

namespace Fmt.App;

// Sheet over the list: builds its inputs from the category's fields.
public sealed class EntryEditor : Grid
{
    private sealed record Input(FieldDef Field, Func<string?> Read, FrameworkElement Control);

    private readonly AppState _state;
    private readonly Category _category;
    private readonly Entry? _existing;
    private readonly Action _close;
    private readonly Action _next;
    private readonly List<Input> _inputs = [];
    private readonly List<(FieldDef Field, TextBlock Output)> _formulas = [];
    private readonly TextBlock _error = Ui.T("", 12, "Negative", wrap: true);
    private readonly TextBox _date;
    private readonly ComboBox? _character;
    private FrameworkElement? _firstFocus;

    public EntryEditor(AppState state, Category category, Entry? existing, Action close, Action next)
    {
        _state = state;
        _category = category;
        _existing = existing;
        _close = close;
        _next = next;

        var today = state.Today;
        _date = Ui.Input((existing?.Date ?? today).ToString("dd.MM.yyyy"), null, "heute, gestern, 01.11.");
        var form = new StackPanel();
        var title = Ui.Row(8, Ui.GlyphColored(category.Icon, category.Color, 15),
            Ui.T(existing == null ? $"{category.Name}: neuer Eintrag" : $"{category.Name}: bearbeiten", 15, bold: true));
        form.Children.Add(title);

        var meta = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        if (state.Data.Characters.Count > 1)
        {
            _character = new ComboBox();
            foreach (var c in state.Data.Characters) _character.Items.Add(c);
            _character.SelectedItem = existing?.Character is { Length: > 0 } ec && state.Data.Characters.Contains(ec) ? ec : state.Character;
            _character.Margin = new Thickness(5, 0, 0, 0);
            var dateLabel = Ui.Labeled("Datum", _date);
            dateLabel.Margin = new Thickness(0, 0, 5, 0);
            form.Children.Add(new UniformRow(0, dateLabel, Ui.Labeled("Charakter", _character)) { Margin = meta.Margin });
        }
        else
        {
            meta.Children.Add(Ui.Labeled("Datum", _date));
            form.Children.Add(meta);
        }

        var prefill = existing == null ? LatestEntry() : null;
        foreach (var f in category.Fields)
        {
            var row = BuildField(f, existing, prefill);
            row.Margin = new Thickness(0, 10, 0, 0);
            form.Children.Add(row);
        }

        _error.Margin = new Thickness(0, 10, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        form.Children.Add(_error);

        var save = Ui.Btn("Speichern", () => Save(false), "Primary");
        var nextButton = existing == null ? Ui.Btn("Speichern + nächster", () => Save(true), tip: "Strg+Enter") : null;
        var cancel = Ui.Btn("Abbrechen", close, "Ghost");
        if (nextButton != null) nextButton.Margin = new Thickness(6, 0, 0, 0);
        cancel.Margin = new Thickness(6, 0, 0, 0);
        var buttons = Ui.Row(0, save, nextButton, cancel);

        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) };
        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        dock.Children.Add(buttons);
        dock.Children.Add(scroll);

        var sheet = Ui.Card(dock, 14);
        sheet.Margin = new Thickness(10);

        // Solid backing so the list never shows through a translucent theme.
        var scrim = new Border();
        scrim.SetResourceReference(Border.BackgroundProperty, "Surface");
        Children.Add(scrim);
        Children.Add(sheet);

        PreviewKeyDown += OnKey;
        Loaded += (_, _) =>
        {
            Recalculate();
            var target = FocusTarget();
            target?.Focus();
            if (target is TextBox t) t.SelectAll();
        };
    }

    private Entry? LatestEntry() => _state.Data.Entries
        .Where(e => e.CategoryId == _category.Id)
        .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedUtc)
        .FirstOrDefault();

    // Open items jump straight to the missing gold value, e.g. the sale price.
    private FrameworkElement? FocusTarget()
    {
        if (_existing != null && Calc.IsOpen(_category, _existing))
        {
            var missing = _inputs.FirstOrDefault(i => i.Field.Type == FieldType.Gold && string.IsNullOrEmpty(i.Read()));
            if (missing != null) return missing.Control;
        }
        return _firstFocus;
    }

    private string InitialValue(FieldDef f, Entry? existing, Entry? prefill)
    {
        if (existing != null) return existing.Values.GetValueOrDefault(f.Id, "");
        if (f.PrefillFrom != null && prefill != null && prefill.Values.TryGetValue(f.PrefillFrom, out var carried)) return carried;
        return f.DefaultValue;
    }

    private FrameworkElement BuildField(FieldDef f, Entry? existing, Entry? prefill)
    {
        var label = f.Required ? f.Name + " *" : f.Name;
        var raw = InitialValue(f, existing, prefill);
        switch (f.Type)
        {
            case FieldType.Formula:
                var output = Ui.T("–", 15, bold: true);
                _formulas.Add((f, output));
                return Ui.Labeled(label, output);

            case FieldType.Bool:
                var check = new CheckBox { IsChecked = raw == "1", Content = Ui.T(f.Name) };
                check.Click += (_, _) => Recalculate();
                _inputs.Add(new Input(f, () => check.IsChecked == true ? "1" : "0", check));
                _firstFocus ??= check;
                return check;

            case FieldType.Text:
            case FieldType.Select:
                var combo = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true };
                var suggestions = f.Type == FieldType.Select ? f.Options : History(f);
                foreach (var s in suggestions) combo.Items.Add(s);
                combo.Text = raw;
                _inputs.Add(new Input(f, () => combo.Text.Trim(), combo));
                _firstFocus ??= combo;
                return Ui.Labeled(label, combo);

            case FieldType.LongText:
                var area = new TextBox { Text = raw, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, VerticalContentAlignment = VerticalAlignment.Top };
                _inputs.Add(new Input(f, () => area.Text.Trim(), area));
                _firstFocus ??= area;
                return Ui.Labeled(label, area);

            case FieldType.Gold:
                var shown = long.TryParse(raw, out var g) ? Gold.FormatFull(g) : raw;
                var hint = Ui.T("", 11, "Muted");
                hint.Visibility = Visibility.Collapsed;
                var gold = Ui.Input(shown, null, "z. B. 50kk, 1,5kkk, 500k");
                gold.TextChanged += (_, _) =>
                {
                    var p = Gold.Parse(gold.Text);
                    hint.Text = gold.Text.Length == 0 ? "" : p is { } v ? "= " + Gold.FormatFull(v) : "Kein gültiger Goldwert";
                    hint.Visibility = hint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                    Recalculate();
                };
                _inputs.Add(new Input(f, () => gold.Text.Length == 0 ? "" : Gold.Parse(gold.Text)?.ToString(CultureInfo.InvariantCulture), gold));
                _firstFocus ??= gold;
                return Ui.Col(4, Ui.T(label, 12, "Muted"), gold, hint);

            case FieldType.Minutes:
                var minutes = Ui.Input(int.TryParse(raw, out var m) ? Duration.Format(m) : raw, _ => Recalculate(), "z. B. 90, 1:30, 1h 30m");
                _inputs.Add(new Input(f, () => minutes.Text.Length == 0 ? "" : Duration.Parse(minutes.Text)?.ToString(CultureInfo.InvariantCulture), minutes));
                _firstFocus ??= minutes;
                return Ui.Labeled(label, minutes);

            default:
                var shownNumber = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n.ToString("0.##", new CultureInfo("de-DE")) : raw;
                var number = Ui.Input(shownNumber, _ => Recalculate());
                _inputs.Add(new Input(f, () => ParseNumber(number.Text), number));
                _firstFocus ??= number;
                return Ui.Labeled(label, number);
        }
    }

    private static string? ParseNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private List<string> History(FieldDef f) => _state.Data.Entries
        .Where(e => e.CategoryId == _category.Id && e.Values.ContainsKey(f.Id))
        .OrderByDescending(e => e.CreatedUtc)
        .Select(e => e.Values[f.Id])
        .Where(v => v.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(100).ToList();

    private void Recalculate()
    {
        if (_formulas.Count == 0) return;
        var probe = new Entry();
        foreach (var i in _inputs)
            if (i.Read() is { Length: > 0 } v) probe.Values[i.Field.Id] = v;
        foreach (var (f, output) in _formulas)
        {
            var text = Calc.Display(_category, probe, f);
            output.Text = text.Length > 0 ? text : "–";
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Save(_existing == null);
            return;
        }
        if (e.OriginalSource is TextBox { AcceptsReturn: true }) return;
        if (Keyboard.FocusedElement is Button) return;
        if (_inputs.Any(i => i.Control is ComboBox { IsDropDownOpen: true })) return;
        e.Handled = true;
        Save(false);
    }

    private void Save(bool keepOpen)
    {
        if (Ui.ParseDate(_date.Text, _state.Today) is not { } date)
        {
            Fail("Datum nicht erkannt. Beispiele: heute, gestern, 01.11.2026", _date);
            return;
        }
        var values = new Dictionary<string, string>();
        foreach (var i in _inputs)
        {
            var value = i.Read();
            if (value == null)
            {
                Fail($"{i.Field.Name}: Wert nicht erkannt.", i.Control);
                return;
            }
            if (i.Field.Required && value.Length == 0)
            {
                Fail($"{i.Field.Name} ist ein Pflichtfeld.", i.Control);
                return;
            }
            if (value.Length > 0) values[i.Field.Id] = value;
        }

        var character = _character?.SelectedItem as string ?? _state.Character;
        _state.Mutate(() =>
        {
            // New dropdown values become permanent options.
            foreach (var i in _inputs.Where(i => i.Field.Type == FieldType.Select))
            {
                var v = values.GetValueOrDefault(i.Field.Id, "");
                if (v.Length > 0 && !i.Field.Options.Contains(v, StringComparer.OrdinalIgnoreCase)) i.Field.Options.Add(v);
            }
            if (_existing == null)
            {
                _state.Data.Entries.Add(new Entry { CategoryId = _category.Id, Date = date, Character = character, Values = values });
            }
            else
            {
                // Undo may have replaced the objects, so resolve by id.
                var target = _state.Data.Entries.FirstOrDefault(x => x.Id == _existing.Id);
                if (target == null) return;
                target.Date = date;
                target.Character = character;
                target.Values = values;
                target.UpdatedUtc = DateTime.UtcNow;
            }
        }, _existing == null ? "Eintrag gespeichert" : "Änderung gespeichert");

        if (keepOpen) _next();
        else _close();
    }

    private void Fail(string message, FrameworkElement control)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
        control.Focus();
    }
}
