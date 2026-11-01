using System.Windows.Controls.Primitives;

namespace Fmt.App;

// Editor for categories and fields: this is how new trackers get built without code.
public sealed class LayoutView(AppState state, PanelWindow shell) : PageView
{
    private static readonly (string Label, FieldType Type)[] Types =
    [
        ("Text", FieldType.Text), ("Langer Text", FieldType.LongText), ("Zahl", FieldType.Number), ("Gold", FieldType.Gold), ("Euro", FieldType.Euro),
        ("Dauer", FieldType.Minutes), ("Ja/Nein", FieldType.Bool), ("Auswahl", FieldType.Select), ("Formel", FieldType.Formula),
    ];

    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private string? _selectedId;
    private string? _confirmDelete;

    private Category? Selected => state.Data.FindCategory(_selectedId) ?? state.Data.Categories.FirstOrDefault();

    public override void Refresh()
    {
        Content = _scroll;
        var offset = _scroll.VerticalOffset;
        if (shell.PageKey == "layout" && _selectedId == null) _selectedId = state.S.LastCategoryId;
        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        Ui.AddTo(col, Ui.T("Kategorien und Felder", 18, bold: true), 0);
        Ui.AddTo(col, CategoryList(), 10);
        if (Selected is { } c)
        {
            _selectedId = c.Id;
            Ui.AddTo(col, CategoryCard(c), 10);
            Ui.AddTo(col, FieldsCard(c), 10);
        }
        _scroll.Content = col;
        _scroll.ScrollToVerticalOffset(offset);
    }

    private FrameworkElement CategoryList()
    {
        var col = new StackPanel();
        var cats = state.Data.Categories;
        for (var i = 0; i < cats.Count; i++)
        {
            var c = cats[i];
            var index = i;
            var name = Ui.Row(8, Ui.GlyphColored(c.Icon, c.Color, 14), Ui.T(c.Name, 13, c.Id == Selected?.Id ? "Ink" : "Muted", bold: c.Id == Selected?.Id));
            var pick = new Button { Style = (Style)Application.Current.Resources["Ghost"], Content = name, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 5, 12, 5) };
            pick.Click += (_, _) => { _selectedId = c.Id; _confirmDelete = null; Refresh(); };
            var visible = Ui.Check("Sichtbar", c.Visible, v => state.Mutate(() => c.Visible = v));
            visible.Margin = new Thickness(0, 0, 6, 0);
            var up = Ui.IconBtn(Ui.G.Up, "Nach oben", () => state.Mutate(() => (cats[index], cats[index - 1]) = (cats[index - 1], cats[index])), 12);
            var down = Ui.IconBtn(Ui.G.Down, "Nach unten", () => state.Mutate(() => (cats[index], cats[index + 1]) = (cats[index + 1], cats[index])), 12);
            up.IsEnabled = i > 0;
            down.IsEnabled = i < cats.Count - 1;
            Ui.AddTo(col, Ui.Cols("*,Auto,Auto,Auto", pick, visible, up, down), 6);
        }
        var add = Ui.Btn("Neue Kategorie", () =>
        {
            var c = new Category { Fields = [new FieldDef { Name = "Name", Required = true }, new FieldDef { Name = "Gold", Type = FieldType.Gold }, new FieldDef { Name = "Notiz", Type = FieldType.LongText, ShowInList = false }] };
            c.GoldFieldId = c.Fields[1].Id;
            _selectedId = c.Id;
            state.Mutate(() => state.Data.Categories.Add(c), "Kategorie angelegt");
        }, glyph: Ui.G.Add);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 8, 0, 0);
        Ui.AddTo(col, add, 6);
        return Ui.Card(col);
    }

    private FrameworkElement CategoryCard(Category c)
    {
        var name = Ui.Input(c.Name, v =>
        {
            c.Name = v;
            state.TouchStructure();
        });
        name.Name = "CategoryName";

        var icons = new WrapPanel();
        foreach (var glyph in Ui.IconChoices)
        {
            var b = new Button { Style = (Style)Application.Current.Resources["Ghost"], Content = Ui.Glyph(glyph, 15, glyph == c.Icon ? "Accent" : "Muted"), Width = 32, Height = 30, Padding = new Thickness(0) };
            b.Click += (_, _) => state.Mutate(() => c.Icon = glyph);
            icons.Children.Add(b);
        }

        var colors = new WrapPanel();
        foreach (var hex in Ui.ColorChoices)
        {
            var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Ui.Hex(hex), BorderThickness = new Thickness(hex.Equals(c.Color, StringComparison.OrdinalIgnoreCase) ? 2 : 0) };
            dot.SetResourceReference(Border.BorderBrushProperty, "Ink");
            var b = new Button { Style = (Style)Application.Current.Resources["Ghost"], Content = dot, Padding = new Thickness(4) };
            b.Click += (_, _) => state.Mutate(() => c.Color = hex);
            colors.Children.Add(b);
        }

        var goldChoices = new List<(string, string?)> { ("Kein Goldwert", null) };
        goldChoices.AddRange(c.Fields
            .Where(f => f.Type is FieldType.Gold || (f.Type == FieldType.Formula && f.FormulaIsGold))
            .Select(f => (f.Name, (string?)f.Id)));
        var gold = Ui.Combo(goldChoices, c.GoldFieldId, v => state.Mutate(() => c.GoldFieldId = v));
        var total = Ui.Check("Zählt in die Gold-Übersicht", c.CountsInTotal, v => state.Mutate(() => c.CountsInTotal = v));

        var entries = state.Data.Entries.Count(e => e.CategoryId == c.Id);
        var confirm = _confirmDelete == c.Id;
        var delete = Ui.Btn(confirm ? $"Wirklich löschen? ({entries} Einträge)" : "Kategorie löschen", () =>
        {
            if (!confirm)
            {
                _confirmDelete = c.Id;
                Refresh();
                return;
            }
            _confirmDelete = null;
            _selectedId = null;
            state.Mutate(() =>
            {
                state.Data.Categories.RemoveAll(x => x.Id == c.Id);
                state.Data.Entries.RemoveAll(x => x.CategoryId == c.Id);
            }, $"\"{c.Name}\" gelöscht");
        }, confirm ? null : "Ghost", Ui.G.Delete);
        delete.HorizontalAlignment = HorizontalAlignment.Left;

        return Ui.Card(Ui.Col(10,
            Ui.Labeled("Name", name),
            Ui.Labeled("Icon", icons),
            Ui.Labeled("Farbe", colors),
            Ui.Labeled("Dieses Feld ist der Goldwert des Eintrags", gold),
            total,
            delete));
    }

    private FrameworkElement FieldsCard(Category c)
    {
        var col = Ui.Col(10, Ui.T("Felder", 13, bold: true));
        for (var i = 0; i < c.Fields.Count; i++) col.Children.Add(FieldRow(c, c.Fields[i], i));
        var add = Ui.Btn("Feld hinzufügen", () => state.Mutate(() => c.Fields.Add(new FieldDef { Name = UniqueName(c, "Feld") })), glyph: Ui.G.Add);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        Ui.AddTo(col, add, 6);
        return Ui.Card(col);
    }

    private static string UniqueName(Category c, string baseName)
    {
        var name = baseName;
        for (var n = 2; c.Fields.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)); n++) name = $"{baseName} {n}";
        return name;
    }

    private FrameworkElement FieldRow(Category c, FieldDef f, int index)
    {
        var name = Ui.Input(f.Name);
        var committed = f.Name;
        name.TextChanged += (_, _) =>
        {
            if (name.Text.Trim().Length == 0) return;
            f.Name = name.Text.Trim();
            state.Touch();
        };
        // References follow a rename once typing is finished.
        name.LostFocus += (_, _) =>
        {
            if (f.Name == committed) return;
            foreach (var other in c.Fields.Where(x => x.Type == FieldType.Formula))
                other.Formula = Formula.RenameReference(other.Formula, committed, f.Name);
            committed = f.Name;
            state.Mutate(() => { });
        };
        name.Margin = new Thickness(0, 0, 6, 0);

        var type = Ui.Combo(Types.Select(t => (t.Label, t.Type)), f.Type, v => state.Mutate(() => f.Type = v));
        var up = Ui.IconBtn(Ui.G.Up, "Nach oben", () => state.Mutate(() => (c.Fields[index], c.Fields[index - 1]) = (c.Fields[index - 1], c.Fields[index])), 12);
        var down = Ui.IconBtn(Ui.G.Down, "Nach unten", () => state.Mutate(() => (c.Fields[index], c.Fields[index + 1]) = (c.Fields[index + 1], c.Fields[index])), 12);
        up.IsEnabled = index > 0;
        down.IsEnabled = index < c.Fields.Count - 1;
        var delete = Ui.IconBtn(Ui.G.Delete, "Feld löschen", () => state.Mutate(() =>
        {
            c.Fields.Remove(f);
            if (c.GoldFieldId == f.Id) c.GoldFieldId = null;
            if (c.SumFieldId == f.Id) c.SumFieldId = null;
            foreach (var other in c.Fields.Where(x => x.PrefillFrom == f.Id)) other.PrefillFrom = null;
        }, $"Feld \"{f.Name}\" gelöscht"), 12);

        var col = Ui.Col(6, Ui.Cols("*,104,Auto,Auto,Auto", name, type, up, down, delete));

        var flags = new StackPanel { Orientation = Orientation.Horizontal };
        var show = Ui.Check("In Liste zeigen", f.ShowInList, v => state.Mutate(() => f.ShowInList = v));
        show.Margin = new Thickness(0, 0, 14, 0);
        flags.Children.Add(show);
        if (f.Type != FieldType.Formula) flags.Children.Add(Ui.Check("Pflichtfeld", f.Required, v => state.Mutate(() => f.Required = v)));
        else flags.Children.Add(Ui.Check("Ergebnis ist Gold", f.FormulaIsGold, v => state.Mutate(() => f.FormulaIsGold = v)));
        Ui.AddTo(col, flags, 6);

        if (f.Type == FieldType.Select)
        {
            var options = Ui.Input(string.Join(", ", f.Options), v =>
            {
                f.Options = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                state.Touch();
            }, "Optionen, mit Komma getrennt");
            Ui.AddTo(col, Ui.Labeled("Auswahlmöglichkeiten", options), 6);
        }
        if (f.Type == FieldType.Formula) col.Children.Add(FormulaEditor(c, f));
        else col.Children.Add(DefaultsRow(c, f));

        var frame = new Border { Child = col, Padding = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0, 0, 0, 1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "Line");
        return frame;
    }

    private FrameworkElement FormulaEditor(Category c, FieldDef f)
    {
        var error = Ui.T("", 11, "Negative", wrap: true);
        void Check()
        {
            var problem = Formula.Validate(f.Formula, c.Fields.Where(x => x != f).Select(x => x.Name));
            error.Text = problem ?? "";
            error.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        }
        var box = Ui.Input(f.Formula, v => { f.Formula = v; Check(); state.Touch(); }, "{Verkauf} - {Einkauf}");
        box.LostFocus += (_, _) => state.Mutate(() => { });
        Check();
        var names = string.Join("  ", c.Fields.Where(x => x != f && x.Type is FieldType.Gold or FieldType.Number or FieldType.Euro or FieldType.Minutes or FieldType.Formula).Select(x => "{" + x.Name + "}"));
        return Ui.Col(3, Ui.Labeled("Formel, rechnet mit + - * / und Klammern", box), error, Ui.T("Verfügbar: " + names, 11, "Muted", wrap: true));
    }

    private FrameworkElement DefaultsRow(Category c, FieldDef f)
    {
        var def = Ui.Input(f.DefaultValue, v => { f.DefaultValue = v; state.Touch(); }, "Standardwert");
        var choices = new List<(string, string?)> { ("Kein Übertrag", null) };
        choices.AddRange(c.Fields.Where(x => x.Type != FieldType.Formula).Select(x => ("Letzter Wert: " + x.Name, (string?)x.Id)));
        var prefill = Ui.Combo(choices, f.PrefillFrom, v => state.Mutate(() => f.PrefillFrom = v));
        prefill.ToolTip = "Neue Einträge übernehmen diesen Wert aus dem letzten Eintrag";
        def.Margin = new Thickness(0, 0, 6, 0);
        return Ui.Cols("*,*", def, prefill);
    }
}
