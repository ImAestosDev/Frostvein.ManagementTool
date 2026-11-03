using System.Windows.Threading;

namespace Fmt.App;

// One run with all counters on one page; the tabs of the single counters stay untouched.
public sealed class SessionView : PageView
{
    private readonly AppState _state;
    private readonly PanelWindow _shell;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly TextBlock _time = Ui.T("", 13, "Muted");
    private readonly StackPanel _players = new();
    private readonly StackPanel _exp = new();
    private readonly StackPanel _kills = new();
    private readonly StackPanel _used = new();
    private readonly StackPanel _buffs = new();
    private readonly TextBlock _usedHint = Ui.T("", 12, "Muted", wrap: true);
    private FrameworkElement? _buffsCard;
    private FrameworkElement? _results;
    private Button? _start, _end;
    private DropCard? _drops;
    private List<PlayerDamage>? _shownPlayers;
    private DamageMode _builtMode;

    public SessionView(AppState state, PanelWindow shell)
    {
        _state = state;
        _shell = shell;
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public override void Refresh()
    {
        (_time.Parent as Panel)?.Children.Remove(_time);
        if (_players.Parent is Border players) players.Child = null;
        if (_exp.Parent is Border exp) exp.Child = null;
        if (_kills.Parent is Border kills) kills.Child = null;
        if (_used.Parent is Border used) used.Child = null;
        if (_buffs.Parent is Border buffs) buffs.Child = null;
        (_usedHint.Parent as Panel)?.Children.Remove(_usedHint);
        _shownPlayers = null;

        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        Ui.AddTo(col, Ui.T("Session", 18, bold: true), 0);
        if (_shell.Capture is not { IsRunning: true } capture)
        {
            Ui.AddTo(col, DamageView.StartCard(_shell, Refresh), 10);
        }
        else
        {
            _start = Ui.Btn("Start", () => { capture.StartSession(_shell.GamePid); Update(); }, "Primary");
            _end = Ui.Btn("Beenden", () => { capture.EndSession(_shell.GamePid); Update(); }, "Primary");
            Ui.AddTo(col, Ui.Card(Ui.Cols("*,Auto,Auto", _time, _start, _end)), 10);
            Ui.AddTo(col, Ui.Check("Kleines Overlay über dem Spiel", _state.S.ShowSessionOverlay, x => { _state.S.ShowSessionOverlay = x; _state.TouchSettings(); }), 10);

            _builtMode = _state.S.DamageMode;
            var mode = Ui.Combo(
                [("Single Mode: nur ich", DamageMode.Single), ("Group Mode: bis zu 3 Plätze", DamageMode.Group), ("Raid Mode: bis zu 20 Plätze", DamageMode.Raid)],
                _state.S.DamageMode,
                v =>
                {
                    _state.S.DamageMode = v;
                    _builtMode = v;
                    _state.Touch();
                    _shownPlayers = null;
                    Update();
                });
            _drops = new DropCard(null);
            var results = new StackPanel();
            Ui.AddTo(results, Ui.T("Damage", 14, bold: true), 0);
            Ui.AddTo(results, mode, 8);
            Ui.AddTo(results, Ui.Card(_players, 6), 8);
            Ui.AddTo(results, Ui.T("EXP", 14, bold: true), 16);
            Ui.AddTo(results, Ui.Card(_exp), 8);
            Ui.AddTo(results, Ui.T("Drops", 14, bold: true), 16);
            Ui.AddTo(results, _drops, 8);
            Ui.AddTo(results, Ui.T("Monster", 14, bold: true), 16);
            Ui.AddTo(results, Ui.Card(_kills), 8);
            Ui.AddTo(results, Ui.T("Benutzte Items", 14, bold: true), 16);
            Ui.AddTo(results, Ui.Card(_used), 8);
            Ui.AddTo(results, _usedHint, 6);
            _buffsCard = ItemsView.BuffsSection(_buffs);
            Ui.AddTo(results, _buffsCard, 16);
            _results = results;
            Ui.AddTo(col, results, 14);
            Update();
        }
        Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void Update()
    {
        if (_shell.Capture is not { IsRunning: true } capture) return;
        // The overlay can switch the mode too.
        if (_builtMode != _state.S.DamageMode)
        {
            Refresh();
            return;
        }
        var stats = capture.Session(_shell.GamePid);
        var running = stats is { Running: true };
        if (_start != null) _start.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        if (_end != null) _end.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (_results != null) _results.Visibility = stats == null ? Visibility.Collapsed : Visibility.Visible;
        if (stats == null)
        {
            _time.Text = "Start lässt Damage, EXP und Drops zusammen loslaufen.";
            return;
        }
        _time.Text = (running ? "Läuft seit " : "Ergebnis nach ") + ExpView.Clock(stats.Duration);
        if (stats.TimeIsUp) _time.Text = SessionCard.Warning + " · " + _time.Text;
        FillPlayers(stats.Damage);
        FillExp(stats);
        _drops?.Show(stats.Drops);
        MonstersView.FillKills(_kills, stats.Kills);
        ItemsView.FillUsed(_used, stats.Used);
        ItemsView.FillHint(_usedHint, stats.InventoryKnown);
        ItemsView.FillBuffs(_buffs, _buffsCard, stats.Buffs);
    }

    private void FillPlayers(DamageStats damage)
    {
        var table = damage.Table(_state.S.DamageMode);
        if (_shownPlayers != null && _shownPlayers.SequenceEqual(table)) return;
        _shownPlayers = table;
        _players.Children.Clear();
        if (table.Count == 0)
        {
            var empty = Ui.T("Noch kein Schaden gezählt.", 13, "Muted");
            empty.Margin = new Thickness(10, 6, 10, 6);
            _players.Children.Add(empty);
            return;
        }
        _players.Children.Add(DamageView.PlayerRow("", "Spieler", "Schaden", "Anteil", "Best Hit", "Muted", 12, false, null));
        var total = Math.Max(1, table.Sum(p => p.Total));
        var place = 0;
        foreach (var p in table)
        {
            place++;
            _players.Children.Add(DamageView.PlayerRow($"{place}.", p.Name, DamageCard.Number(p.Total), $"{p.Total * 100.0 / total:0} %", DamageCard.Number(p.Best), p.IsOwn ? "Accent" : "Ink", 13, p.IsOwn, null));
        }
    }

    private void FillExp(SessionStats stats)
    {
        _exp.Children.Clear();
        if (stats.Exp.Recording is not { Rows.Count: > 0 } recording)
        {
            _exp.Children.Add(Ui.T("Warte auf EXP.", 13, "Muted"));
            return;
        }
        Ui.AddTo(_exp, ExpView.RecordRow("", "Erhalten", stats.Running ? "" : "Pro Stunde", "Muted", 12, false), 0);
        foreach (var row in recording.Rows)
            Ui.AddTo(_exp, ExpView.RecordRow(row.Label, "+" + ExpCard.Percent(row.GainedPercent), stats.Running ? "" : ExpCard.Percent(row.PercentPerHour), "Ink", 14, !stats.Running), 4);
    }
}
