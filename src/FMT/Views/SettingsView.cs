using System.Diagnostics;
using System.IO;
using System.Windows.Controls.Primitives;

namespace Fmt.App;

public sealed class SettingsView(AppState state, Action hotkeysChanged, Updater? updater) : PageView
{
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    public override void Refresh()
    {
        Content = _scroll;
        var offset = _scroll.VerticalOffset;
        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        Ui.AddTo(col, Ui.T("Einstellungen", 18, bold: true), 0);
        Ui.AddTo(col, StartCard(), 10);
        Ui.AddTo(col, ColorsCard(), 10);
        Ui.AddTo(col, LookCard(), 10);
        Ui.AddTo(col, PositionCard(), 10);
        Ui.AddTo(col, DashboardCard(), 10);
        Ui.AddTo(col, LandOfLifeCard(), 10);
        Ui.AddTo(col, HotkeyCard(), 10);
        Ui.AddTo(col, CharactersCard(), 10);
        Ui.AddTo(col, DataCard(), 10);
        Ui.AddTo(col, UpdateCard(), 10);
        _scroll.Content = col;
        _scroll.ScrollToVerticalOffset(offset);
    }

    private static Border Section(string title, params UIElement?[] children)
    {
        var col = Ui.Col(10, Ui.T(title, 13, bold: true));
        foreach (var c in children) Ui.AddTo(col, c, 10);
        return Ui.Card(col);
    }

    private FrameworkElement Slider(string label, double min, double max, double value, Func<double, string> format, Action<double> changed)
    {
        var text = Ui.T(format(value), 12, "Muted");
        var s = new Slider { Minimum = min, Maximum = max, Value = value };
        s.ValueChanged += (_, e) =>
        {
            text.Text = format(e.NewValue);
            changed(e.NewValue);
            state.TouchSettings();
        };
        return Ui.Col(2, Ui.Cols("*,Auto", Ui.T(label, 12, "Muted"), text), s);
    }

    private FrameworkElement StartCard()
    {
        var check = Ui.Check("Beim Windows-Start automatisch starten", Autostart.IsEnabled, v =>
        {
            if (Autostart.Set(v) is { } error) state.Notify(error);
            Refresh();
        });
        return Section("Start",
            check,
            Ui.T("FMT startet dann im Hintergrund, ohne das Fenster zu öffnen, und hängt sich an NosTale, sobald das Spiel läuft.", 12, "Muted", wrap: true),
            PacketCapture.IsAdmin
                ? null
                : Ui.T("Liest FMT mit, fragt Windows beim Start nach Admin-Rechten. Aktivierst du den Autostart bei laufendem Mitlesen, entfällt diese Abfrage.", 12, "Muted", wrap: true));
    }

    private FrameworkElement PositionCard()
    {
        var s = state.S;
        var dock = Ui.Combo(
            [("Im Spiel rechts", DockMode.InsideRight), ("Im Spiel links", DockMode.InsideLeft), ("Neben dem Spiel rechts", DockMode.OutsideRight),
             ("Neben dem Spiel links", DockMode.OutsideLeft), ("Frei (dort, wohin du es ziehst)", DockMode.Free)],
            s.Dock, v => { s.Dock = v; state.TouchSettings(); });
        return Section("Position am Spielfenster",
            Ui.Labeled("Andocken", dock),
            Ui.T("Tipp: Am Kopf ziehen verschiebt das Fenster frei, die rechte untere Ecke ändert die Größe.", 12, "Muted", wrap: true),
            Slider("Breite", 420, 900, s.PanelWidth, v => $"{v:0} px", v => s.PanelWidth = v),
            Slider("Höhe", 40, 100, s.PanelHeightPercent, v => $"{v:0} %", v => s.PanelHeightPercent = v),
            Ui.Check("Kleine FMT-Plakette im Spiel anzeigen", s.ShowPill, v => { s.ShowPill = v; state.TouchSettings(); }),
            Ui.Check("Seitenleiste nur mit Icons", s.SidebarIconsOnly, v => { s.SidebarIconsOnly = v; state.TouchStructure(); }));
    }

    private FrameworkElement LookCard()
    {
        var t = state.S.Theme;
        return Section("Darstellung",
            Slider("Deckkraft", 0.3, 1, t.Opacity, v => $"{v * 100:0} %", v => t.Opacity = v),
            Slider("Größe", 0.7, 1.6, t.Scale, v => $"{v * 100:0} %", v => t.Scale = v),
            Slider("Ecken", 0, 16, t.Radius, v => $"{v:0} px", v => t.Radius = v),
            Ui.Check("Kompakte Listen", t.Compact, v => state.Mutate(() => t.Compact = v)));
    }

    private FrameworkElement ColorsCard()
    {
        var t = state.S.Theme;
        var presets = new WrapPanel();
        foreach (var name in ThemeManager.Presets.Keys)
        {
            var p = ThemeManager.Presets[name];
            var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Ui.Hex(p.Accent) };
            var b = new Button
            {
                Content = Ui.Row(6, swatch, Ui.T(name, 12)),
                Margin = new Thickness(0, 0, 6, 6),
            };
            if (t.Preset == name) b.SetResourceReference(BorderBrushProperty, "Accent");
            b.Click += (_, _) =>
            {
                ThemeManager.ApplyPreset(t, name);
                state.TouchSettings();
                Refresh();
            };
            presets.Children.Add(b);
        }

        var tokens = new (string Label, Func<string> Get, Action<string> Set)[]
        {
            ("Hintergrund", () => t.Background, v => t.Background = v),
            ("Karte", () => t.Surface, v => t.Surface = v),
            ("Karte hell", () => t.Surface2, v => t.Surface2 = v),
            ("Linien", () => t.Border, v => t.Border = v),
            ("Text", () => t.Text, v => t.Text = v),
            ("Text leise", () => t.Muted, v => t.Muted = v),
            ("Akzent", () => t.Accent, v => t.Accent = v),
            ("Gewinn", () => t.Positive, v => t.Positive = v),
            ("Verlust", () => t.Negative, v => t.Negative = v),
        };
        var grid = new UniformGrid { Columns = 3 };
        foreach (var (label, get, set) in tokens)
        {
            var swatch = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Background = Ui.Hex(get()), Margin = new Thickness(0, 0, 6, 0) };
            var box = Ui.Input(get());
            box.TextChanged += (_, _) =>
            {
                if (!Ui.IsColor(box.Text)) return;
                set(box.Text);
                t.Preset = "Eigenes";
                swatch.Background = Ui.Hex(box.Text);
                state.TouchSettings();
            };
            var cell = Ui.Col(3, Ui.T(label, 11, "Muted"), Ui.Cols("Auto,*", swatch, box));
            cell.Margin = new Thickness(0, 0, 8, 8);
            grid.Children.Add(cell);
        }
        return Section("Aussehen", presets, grid);
    }

    private FrameworkElement DashboardCard()
    {
        var s = state.S;
        var col = new StackPanel();
        var ordered = s.DashboardWidgets
            .Concat(DashboardView.Widgets.Select(w => w.Id).Where(id => !s.DashboardWidgets.Contains(id)))
            .ToList();
        foreach (var id in ordered)
        {
            var name = DashboardView.Widgets.FirstOrDefault(w => w.Id == id).Name ?? id;
            var on = s.DashboardWidgets.Contains(id);
            var index = s.DashboardWidgets.IndexOf(id);
            var check = Ui.Check(name, on, v => state.Mutate(() =>
            {
                if (v) s.DashboardWidgets.Add(id);
                else s.DashboardWidgets.Remove(id);
            }));
            var up = Ui.IconBtn(Ui.G.Up, "Nach oben", () => state.Mutate(() => Swap(s.DashboardWidgets, index, index - 1)), 12);
            var down = Ui.IconBtn(Ui.G.Down, "Nach unten", () => state.Mutate(() => Swap(s.DashboardWidgets, index, index + 1)), 12);
            up.IsEnabled = on && index > 0;
            down.IsEnabled = on && index < s.DashboardWidgets.Count - 1;
            col.Children.Add(Ui.Cols("*,Auto,Auto", check, up, down));
        }
        return Section("Übersicht: Widgets und Reihenfolge", col);
    }

    private static void Swap(List<string> list, int a, int b) => (list[a], list[b]) = (list[b], list[a]);

    private FrameworkElement LandOfLifeCard()
    {
        var s = state.S.LandOfLife;
        TextBox Minutes(int value, Action<int> set, int min, int max)
        {
            var box = Ui.Input(value.ToString());
            box.TextChanged += (_, _) =>
            {
                if (!int.TryParse(box.Text, out var v) || v < min || v > max) return;
                set(v);
                state.Touch();
            };
            return box;
        }
        var name = Ui.Input(s.Name, v => { if (v.Trim().Length == 0) return; s.Name = v.Trim(); state.TouchStructure(); });
        var label = Ui.Input(s.MarkerLabel, v => { s.MarkerLabel = v; state.Touch(); });
        var length = Ui.Labeled("Fensterlänge in Minuten", Minutes(s.WindowMinutes, v => s.WindowMinutes = v, 15, 1440));
        var offset = Ui.Labeled("Icon-Zeit nach Fensterstart, Minuten", Minutes(s.MarkerOffsetMinutes, v => s.MarkerOffsetMinutes = v, 0, 1439));
        var gold = Ui.Labeled("Golddrache: Respawn in Minuten", Minutes(s.GoldDragonMinutes, v => s.GoldDragonMinutes = v, 1, 240));
        var holy = Ui.Labeled("Heiliger Untoter Drache: Respawn in Minuten", Minutes(s.HolyDragonMinutes, v => s.HolyDragonMinutes = v, 1, 240));
        return Section("Land der Lebenden",
            Ui.Check("Tab anzeigen", s.Visible, v => { s.Visible = v; state.TouchStructure(); }),
            new UniformRow(8, Ui.Labeled("Name", name), Ui.Labeled("Name der Icon-Zeit", label)),
            new UniformRow(8, length, offset),
            new UniformRow(8, gold, holy));
    }

    private FrameworkElement HotkeyCard()
    {
        var s = state.S;
        return Section("Hotkeys",
            HotkeyBox("FMT öffnen / schließen", s.ToggleHotkey, v => s.ToggleHotkey = v),
            HotkeyBox("Schnelleintrag (letzte Kategorie)", s.QuickHotkey, v => s.QuickHotkey = v),
            Ui.T("Ins Feld klicken und die neue Kombination drücken. Entf entfernt den Hotkey.", 12, "Muted", wrap: true));
    }

    private FrameworkElement HotkeyBox(string label, string value, Action<string> set)
    {
        var box = new TextBox { Text = value, IsReadOnly = true, IsReadOnlyCaretVisible = false, Cursor = Cursors.Hand, Tag = "Kein Hotkey" };
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Delete or Key.Back)
            {
                box.Text = "";
                set("");
            }
            else if (key == Key.Escape || key == Key.Tab)
            {
                e.Handled = key != Key.Tab;
                return;
            }
            else
            {
                var combo = HotkeyManager.Describe(key, Keyboard.Modifiers);
                if (combo == null) return;
                box.Text = combo;
                set(combo);
            }
            state.Touch();
            hotkeysChanged();
        };
        return Ui.Labeled(label, box);
    }

    private FrameworkElement CharactersCard()
    {
        var col = new StackPanel();
        foreach (var name in state.Data.Characters.ToList())
        {
            var box = Ui.Input(name);
            box.Margin = new Thickness(0, 0, 4, 4);
            box.LostFocus += (_, _) => RenameCharacter(name, box.Text.Trim());
            var delete = Ui.IconBtn(Ui.G.Delete, "Entfernen (Einträge bleiben erhalten)", () => state.Mutate(() => state.Data.Characters.Remove(name), $"{name} entfernt"));
            delete.IsEnabled = state.Data.Characters.Count > 1;
            col.Children.Add(Ui.Cols("*,Auto", box, delete));
        }
        var add = Ui.Input("", null, "Neuer Charakter, Enter zum Anlegen");
        add.KeyDown += (_, e) =>
        {
            var name = add.Text.Trim();
            if (e.Key != Key.Enter || name.Length == 0 || state.Data.Characters.Contains(name)) return;
            state.Mutate(() => state.Data.Characters.Add(name));
        };
        col.Children.Add(add);
        return Section("Charaktere", Ui.T("Dailies werden pro Charakter abgehakt. Ab zwei Charakteren erscheint oben die Auswahl.", 12, "Muted", wrap: true), col);
    }

    private void RenameCharacter(string oldName, string newName)
    {
        if (newName.Length == 0 || newName == oldName || state.Data.Characters.Contains(newName)) return;
        state.Mutate(() =>
        {
            var d = state.Data;
            d.Characters[d.Characters.IndexOf(oldName)] = newName;
            foreach (var e in d.Entries.Where(e => e.Character == oldName)) e.Character = newName;
            foreach (var c in d.Checks.Where(c => c.Character == oldName)) c.Character = newName;
            if (d.Settings.ActiveCharacter == oldName) d.Settings.ActiveCharacter = newName;
        });
    }

    private FrameworkElement? UpdateCard()
    {
        if (updater == null) return null;
        var status = Ui.T(updater.Status, 12, "Muted", wrap: true);
        void Show() => status.Text = updater.Status;
        updater.StatusChanged += Show;
        status.Unloaded += (_, _) => updater.StatusChanged -= Show;
        var auto = Ui.Check("Automatisch aktualisieren", state.S.AutoUpdate, v => { state.S.AutoUpdate = v; state.Touch(); });
        var check = Ui.Btn("Jetzt prüfen", () => _ = updater.CheckAsync());
        check.HorizontalAlignment = HorizontalAlignment.Left;
        return Section($"Updates · Version {Updater.CurrentVersionText}", auto, status, check);
    }

    private FrameworkElement DataCard()
    {
        var s = state.S;
        var hours = Enumerable.Range(0, 24).Select(h => ($"{h:00}:00 Uhr", h));
        var reset = Ui.Combo(hours, s.DayResetHour, v => state.Mutate(() => s.DayResetHour = v));
        var folder = Ui.Btn("Datenordner öffnen", () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(state.Store.FilePath)}\"") { UseShellExecute = true }), glyph: Ui.G.Folder);
        return Section("Tag und Daten",
            Ui.Labeled("Neuer Spieltag beginnt um", reset),
            Ui.T($"Gespeichert in {state.Store.FilePath}. Jeder Tag bekommt automatisch ein Backup, 30 Stück bleiben erhalten.", 12, "Muted", wrap: true),
            Ui.Row(0, folder));
    }
}
