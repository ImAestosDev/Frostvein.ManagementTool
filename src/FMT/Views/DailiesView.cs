using System.Windows.Controls.Primitives;

namespace Fmt.App;

public sealed class DailiesView(AppState state) : PageView
{
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private bool _editing;
    private Recurrence? _focusAdd;

    public override void Refresh()
    {
        Content = _scroll;
        var offset = _scroll.VerticalOffset;
        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        Ui.AddTo(col, Header(), 0);
        Ui.AddTo(col, History(), 10);
        Ui.AddTo(col, Section("Täglich", Recurrence.Daily), 10);
        Ui.AddTo(col, Section("Wöchentlich", Recurrence.Weekly), 10);
        _scroll.Content = col;
        _scroll.ScrollToVerticalOffset(offset);
    }

    private FrameworkElement Header()
    {
        var today = state.Today;
        var reset = today.AddDays(1).ToDateTime(new TimeOnly(state.S.DayResetHour, 0)) - DateTime.Now;
        var streak = Stats.Streak(state.Data, state.Character, today);
        var info = $"Reset in {(int)reset.TotalHours}h {reset.Minutes}m";
        if (state.Data.Characters.Count > 1) info = $"{state.Character} · {info}";
        var left = Ui.Col(1, Ui.T("Dailies", 18, bold: true), Ui.T(info, 12, "Muted"));
        var edit = Ui.Btn(_editing ? "Fertig" : "Bearbeiten", () => { _editing = !_editing; Refresh(); }, _editing ? "Primary" : null, _editing ? null : Ui.G.Edit);
        var flame = streak > 0 ? Ui.Row(4, Ui.Glyph(Ui.G.Flame, 13), Ui.T($"{streak} Tage Serie", 12)) : null;
        return Ui.Cols("*,Auto,Auto", left, flame, new ContentControl { Content = edit, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
    }

    // One cell per day; stronger accent means more of that day was finished.
    private FrameworkElement History()
    {
        const int days = 21;
        var today = state.Today;
        var grid = new UniformGrid { Rows = 1, Columns = days };
        for (var i = days - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var (done, total) = Stats.Progress(state.Data, state.Character, day, Recurrence.Daily);
            var cell = new Border { Height = 18, Margin = new Thickness(1.5, 0, 1.5, 0), CornerRadius = new CornerRadius(3) };
            if (total == 0 || done == 0) cell.SetResourceReference(Border.BackgroundProperty, "Surface2");
            else
            {
                var accent = Ui.Res("Accent") as SolidColorBrush;
                cell.Background = new SolidColorBrush(accent!.Color) { Opacity = 0.3 + 0.7 * done / total };
            }
            cell.ToolTip = $"{day:dd.MM.}: {done}/{total}";
            grid.Children.Add(cell);
        }
        return grid;
    }

    private FrameworkElement Section(string title, Recurrence recurrence)
    {
        var d = state.Data;
        var today = state.Today;
        var tasks = d.Tasks.Where(t => t.Recurrence == recurrence && (_editing || t.Active)).ToList();
        var (done, total) = Stats.Progress(d, state.Character, today, recurrence);
        var col = Ui.Col(8,
            Ui.Cols("*,Auto", Ui.T(title, 13, bold: true), Ui.T($"{done}/{total}", 13, bold: true)),
            Ui.Bar(total == 0 ? 0 : (double)done / total));

        if (_editing)
        {
            foreach (var t in tasks) col.Children.Add(EditRow(t, tasks));
            Ui.AddTo(col, AddRow(recurrence), 6);
        }
        else
        {
            foreach (var group in tasks.GroupBy(t => t.Group))
            {
                if (group.Key.Length > 0)
                {
                    var head = Ui.T(group.Key, 11, "Muted", bold: true);
                    head.Margin = new Thickness(0, 4, 0, 0);
                    Ui.AddTo(col, head, 6);
                }
                foreach (var t in group) col.Children.Add(TaskRow(t));
            }
            if (tasks.Count == 0) col.Children.Add(Ui.T("Noch nichts angelegt. Über Bearbeiten fügst du Aufgaben hinzu.", 12, "Muted", wrap: true));
        }
        return Ui.Card(col);
    }

    private FrameworkElement TaskRow(DailyTask t)
    {
        var done = Stats.IsDone(state.Data, t, state.Character, state.Today);
        var name = Ui.T(t.Name, 13, done ? "Muted" : "Ink", wrap: true);
        if (done) name.TextDecorations = TextDecorations.Strikethrough;
        UIElement label = t.Note.Length > 0 ? Ui.Col(0, name, Ui.T(t.Note, 11, "Muted", wrap: true)) : name;
        var check = new CheckBox { IsChecked = done, Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        check.Click += (_, _) => state.Mutate(() => Stats.SetDone(state.Data, t, state.Character, state.Today, check.IsChecked == true));
        return check;
    }

    private FrameworkElement EditRow(DailyTask t, List<DailyTask> siblings)
    {
        var name = Ui.Input(t.Name, v => { t.Name = v; state.Touch(); }, "Aufgabe");
        var group = Ui.Input(t.Group, v => { t.Group = v; state.Touch(); }, "Gruppe");
        var note = Ui.Input(t.Note, v => { t.Note = v; state.Touch(); }, "Notiz");
        var index = siblings.IndexOf(t);
        var up = Ui.IconBtn(Ui.G.Up, "Nach oben", () => Move(t, siblings, -1));
        var down = Ui.IconBtn(Ui.G.Down, "Nach unten", () => Move(t, siblings, 1));
        up.IsEnabled = index > 0;
        down.IsEnabled = index < siblings.Count - 1;
        var delete = Ui.IconBtn(Ui.G.Delete, "Löschen", () => state.Mutate(() =>
        {
            state.Data.Tasks.RemoveAll(x => x.Id == t.Id);
            state.Data.Checks.RemoveAll(x => x.TaskId == t.Id);
        }, $"\"{t.Name}\" gelöscht"));
        var recurrence = Ui.Combo([("Täglich", Recurrence.Daily), ("Wöchentlich", Recurrence.Weekly)], t.Recurrence,
            v => state.Mutate(() => t.Recurrence = v));
        var perChar = Ui.Check("Pro Charakter", t.PerCharacter, v => state.Mutate(() => t.PerCharacter = v));
        var active = Ui.Check("Aktiv", t.Active, v => state.Mutate(() => t.Active = v));
        name.Margin = new Thickness(0, 0, 6, 0);
        group.Margin = new Thickness(0, 0, 6, 0);
        recurrence.Margin = new Thickness(0, 0, 12, 0);
        perChar.Margin = new Thickness(0, 0, 12, 0);
        var box = Ui.Col(6,
            Ui.Cols("2*,*,Auto,Auto,Auto", name, group, up, down, delete),
            note,
            Ui.Cols("120,Auto,Auto", recurrence, perChar, active));
        var frame = new Border { Child = box, Padding = new Thickness(0, 6, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "Line");
        return frame;
    }

    private void Move(DailyTask t, List<DailyTask> siblings, int delta)
    {
        var other = siblings[siblings.IndexOf(t) + delta];
        state.Mutate(() =>
        {
            var all = state.Data.Tasks;
            int a = all.IndexOf(t), b = all.IndexOf(other);
            (all[a], all[b]) = (all[b], all[a]);
        });
    }

    private FrameworkElement AddRow(Recurrence recurrence)
    {
        var box = Ui.Input("", null, "Neue Aufgabe, Enter zum Anlegen");
        void Add()
        {
            var name = box.Text.Trim();
            if (name.Length == 0) return;
            _focusAdd = recurrence;
            state.Mutate(() => state.Data.Tasks.Add(new DailyTask { Name = name, Recurrence = recurrence, CreatedDay = state.Today }));
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Add();
        };
        if (_focusAdd == recurrence)
        {
            _focusAdd = null;
            box.Loaded += (_, _) => box.Focus();
        }
        var add = Ui.Btn("Hinzufügen", Add, glyph: Ui.G.Add);
        add.Margin = new Thickness(6, 0, 0, 0);
        return Ui.Cols("*,Auto", box, add);
    }
}
