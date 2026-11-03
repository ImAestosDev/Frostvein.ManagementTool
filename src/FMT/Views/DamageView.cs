using System.Windows.Threading;

namespace Fmt.App;

// Damage counter of the active client, fed by the packet capture.
public sealed class DamageView : PageView
{
    private readonly AppState _state;
    private readonly PanelWindow _shell;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly TextBlock _total = Ui.T("0", 30, bold: true);
    private readonly TextBlock _best = Ui.T("0", 22, bold: true);
    private readonly TextBlock _bestSkill = Ui.T("", 12, "Muted");
    private readonly TextBlock _perSecond = Ui.T("0", 22, bold: true);
    private readonly TextBlock _hits = Ui.T("0", 22, bold: true);
    private readonly TextBlock _misses = Ui.T("", 12, "Muted");
    private readonly TextBlock _status = Ui.T("", 12, "Muted", wrap: true);
    private readonly TextBlock _diagnostics = Ui.T("", 12, "Muted", wrap: true);
    private readonly StackPanel _players = new();
    private readonly TextBlock _listTitle = Ui.T("", 14, bold: true);
    private FrameworkElement? _firewall;
    private FrameworkElement? _list;
    private List<PlayerDamage>? _shownPlayers;
    private long? _shownOwn;
    private DamageMode _builtMode;

    public DamageView(AppState state, PanelWindow shell)
    {
        _state = state;
        _shell = shell;
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public override void Refresh()
    {
        Detach();
        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        Ui.AddTo(col, Ui.T("Damage Counter", 18, bold: true), 0);
        var capture = _shell.Capture;
        if (capture is not { IsRunning: true })
        {
            Ui.AddTo(col, StartCard(capture), 10);
        }
        else
        {
            var reset = Ui.Btn("Zurücksetzen", () => { capture.ResetDamage(_shell.GamePid); Update(); }, "Primary", Ui.G.Undo);
            var stop = Ui.Btn("Mitlesen stoppen", () => { _shell.StopCapture?.Invoke(); Refresh(); }, "Ghost");
            stop.Margin = new Thickness(6, 0, 0, 0);
            var overlay = Ui.Check("Kleiner Zähler über dem Spiel", _state.S.ShowDamageOverlay, v => { _state.S.ShowDamageOverlay = v; _state.TouchSettings(); });
            _builtMode = _state.S.DamageMode;
            var mode = Ui.Combo(
                [("Single Mode: nur ich", DamageMode.Single), ("Group Mode: bis zu 3 Plätze", DamageMode.Group), ("Raid Mode: bis zu 20 Plätze", DamageMode.Raid)],
                _state.S.DamageMode,
                v =>
                {
                    _state.S.DamageMode = v;
                    _builtMode = v;
                    _state.Touch();
                    Update();
                });
            Ui.AddTo(col, mode, 10);
            Ui.AddTo(col, Ui.Card(Ui.Col(2, Ui.T("Dein Schaden", 12, "Muted"), _total)), 10);
            Ui.AddTo(col, new UniformRow(10,
                Ui.Card(Ui.Col(2, Ui.T("Dein Best Hit", 12, "Muted"), _best, _bestSkill)),
                Ui.Card(Ui.Col(2, Ui.T("Pro Sekunde", 12, "Muted"), _perSecond)),
                Ui.Card(Ui.Col(2, Ui.T("Treffer", 12, "Muted"), _hits, _misses))), 10);
            Ui.AddTo(col, Ui.Row(0, reset, stop), 10);
            _list = Ui.Col(8, _listTitle, Ui.Card(_players, 6));
            Ui.AddTo(col, _list, 14);
            Ui.AddTo(col, overlay, 10);
            Ui.AddTo(col, _status, 10);
            Ui.AddTo(col, _diagnostics, 4);
            _firewall = FirewallCard();
            Ui.AddTo(col, _firewall, 10);
            _shownPlayers = null;
            Update();
        }
        Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // The value blocks survive page rebuilds.
    private void Detach()
    {
        foreach (var t in new[] { _total, _best, _bestSkill, _perSecond, _hits, _misses, _status, _diagnostics, _listTitle })
            (t.Parent as Panel)?.Children.Remove(t);
        if (_players.Parent is Border b) b.Child = null;
    }

    private FrameworkElement StartCard(PacketCapture? capture)
    {
        var start = Ui.Btn("Mitlesen starten", () => { _shell.StartCapture?.Invoke(); Refresh(); }, "Primary");
        start.HorizontalAlignment = HorizontalAlignment.Left;
        return Ui.Card(Ui.Col(10,
            Ui.T("FMT liest dafür die eingehenden Spielpakete an der Netzwerkkarte mit. Der Client wird nicht verändert.", 13, wrap: true),
            Ui.T("Windows fragt dabei nach Admin-Rechten, danach startet FMT einmal neu. Ob das Mitlesen gegen die Regeln des Spiels verstößt, liegt in deiner Verantwortung.", 12, "Muted", wrap: true),
            capture?.Error is { } error ? Ui.T(error, 12, "Negative", wrap: true) : null,
            start));
    }

    private FrameworkElement FirewallCard()
    {
        var allow = Ui.Btn("Firewall-Regel anlegen", () =>
        {
            var error = PacketCapture.AllowInFirewall();
            _state.Notify(error ?? "Firewall-Regel angelegt. Starte FMT neu, dann sollten Pakete ankommen.");
        }, "Primary");
        allow.HorizontalAlignment = HorizontalAlignment.Left;
        var card = Ui.Card(Ui.Col(8,
            Ui.T("Es kommen keine Spieldaten an", 13, bold: true),
            Ui.T("NosTale ist verbunden, aber Windows liefert FMT keine eingehenden Daten. Meist blockiert das die Firewall. Die Regel erlaubt eingehenden Verkehr nur für FMT, du kannst sie jederzeit in der Windows-Firewall löschen.", 12, "Muted", wrap: true),
            allow));
        card.Visibility = Visibility.Collapsed;
        return card;
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
        _diagnostics.Text = $"Spielverbindungen {capture.Connections} · eingehend {capture.Incoming} · ausgehend {capture.Outgoing} · erkannte Pakete {capture.Packets}";
        if (_firewall != null) _firewall.Visibility = capture.LooksBlocked ? Visibility.Visible : Visibility.Collapsed;

        var s = capture.Stats(_shell.GamePid);
        var own = s.Own;
        _total.Text = DamageCard.Number(own?.Total ?? 0);
        _best.Text = DamageCard.Number(own?.Best ?? 0);
        _bestSkill.Text = own is { Best: > 0 } ? $"Skill {own.BestSkill}" : "";
        _perSecond.Text = DamageCard.Number((long)(own?.PerSecond ?? 0));
        _hits.Text = DamageCard.Number(own?.Hits ?? 0);
        _misses.Text = own is { Misses: > 0 } ? $"{own.Misses} verfehlt" : "";
        _status.Text = s.OwnId == null
            ? "Dein Charakter ist noch nicht erkannt. Klick in der Liste bei dir auf „Das bin ich“, das merkt sich FMT."
            : $"Dein Charakter: {(own?.Name ?? capture.OwnName(_shell.GamePid))}. Gezählt werden Spieler, keine Pets und Partner.";
        FillPlayers(capture, s);
    }

    private void FillPlayers(PacketCapture capture, DamageStats s)
    {
        var mode = _state.S.DamageMode;
        // Single needs the list only while the own character is unknown.
        var pickOwn = mode == DamageMode.Single && s.OwnId == null;
        if (_list != null) _list.Visibility = mode == DamageMode.Single && !pickOwn ? Visibility.Collapsed : Visibility.Visible;
        _listTitle.Text = pickOwn ? "Wer bist du?" : mode == DamageMode.Group ? "Gruppe" : "Raid";
        var table = pickOwn ? s.Players.ToList() : s.Table(mode);

        if (_shownPlayers != null && _shownOwn == s.OwnId && _shownPlayers.SequenceEqual(table)) return;
        _shownPlayers = table;
        _shownOwn = s.OwnId;
        _players.Children.Clear();
        if (table.Count == 0)
        {
            var empty = Ui.T("Noch kein Schaden gezählt.", 13, "Muted");
            empty.Margin = new Thickness(10, 6, 10, 6);
            _players.Children.Add(empty);
            return;
        }
        _players.Children.Add(PlayerRow("", "Spieler", "Schaden", "Anteil", "Best Hit", "Muted", 12, false, null));
        var total = Math.Max(1, table.Sum(p => p.Total));
        var place = 0;
        foreach (var p in table)
        {
            place++;
            // Reacts on press: the list rebuilds while people keep hitting.
            FrameworkElement? mark = null;
            if (s.OwnId == null)
            {
                var me = Ui.T("Das bin ich", 12, "Accent");
                me.Cursor = Cursors.Hand;
                me.Margin = new Thickness(8, 0, 0, 0);
                me.MouseLeftButtonDown += (_, e) => { e.Handled = true; capture.SetOwn(_shell.GamePid, p.Id); Update(); };
                mark = me;
            }
            var share = $"{p.Total * 100.0 / total:0} %";
            _players.Children.Add(PlayerRow($"{place}.", p.Name, DamageCard.Number(p.Total), share, DamageCard.Number(p.Best), p.IsOwn ? "Accent" : "Ink", 13, p.IsOwn, mark));
        }
    }

    private static Border PlayerRow(string place, string name, string total, string share, string best, string brush, double size, bool bold, FrameworkElement? mark)
    {
        var totalText = Ui.T(total, size, brush, bold);
        var shareText = Ui.T(share, size, brush, bold);
        var bestText = Ui.T(best, size, brush, bold);
        totalText.TextAlignment = shareText.TextAlignment = bestText.TextAlignment = TextAlignment.Right;
        return new Border
        {
            Padding = new Thickness(10, 5, 10, 5),
            Child = Ui.Cols("26,*,Auto,96,56,84", Ui.T(place, size, "Muted"), Ui.T(name, size, brush, bold, trim: true), mark, totalText, shareText, bestText),
        };
    }
}
