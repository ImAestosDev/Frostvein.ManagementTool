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
    private FrameworkElement? _firewall;

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
            Ui.AddTo(col, Ui.Card(Ui.Col(2, Ui.T("Schaden gesamt", 12, "Muted"), _total)), 10);
            Ui.AddTo(col, new UniformRow(10,
                Ui.Card(Ui.Col(2, Ui.T("Best Hit", 12, "Muted"), _best, _bestSkill)),
                Ui.Card(Ui.Col(2, Ui.T("Pro Sekunde", 12, "Muted"), _perSecond)),
                Ui.Card(Ui.Col(2, Ui.T("Treffer", 12, "Muted"), _hits, _misses))), 10);
            Ui.AddTo(col, Ui.Row(0, reset, stop), 10);
            Ui.AddTo(col, overlay, 10);
            Ui.AddTo(col, _status, 10);
            Ui.AddTo(col, _diagnostics, 4);
            _firewall = FirewallCard();
            Ui.AddTo(col, _firewall, 10);
            Update();
        }
        Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // The value blocks survive page rebuilds.
    private void Detach()
    {
        foreach (var t in new[] { _total, _best, _bestSkill, _perSecond, _hits, _misses, _status, _diagnostics })
            (t.Parent as Panel)?.Children.Remove(t);
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
            _state.Notify(error ?? "Firewall-Regel angelegt. Kämpf kurz, dann sollten Pakete ankommen.");
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
        _diagnostics.Text = $"Spielverbindungen {capture.Connections} · eingehend {capture.Incoming} · ausgehend {capture.Outgoing} · erkannte Pakete {capture.Packets}";
        if (_firewall != null) _firewall.Visibility = capture.LooksBlocked ? Visibility.Visible : Visibility.Collapsed;
        var s = capture.Stats(_shell.GamePid);
        _total.Text = DamageCard.Number(s.Total);
        _best.Text = DamageCard.Number(s.Best);
        _bestSkill.Text = s.Best > 0 ? $"Skill {s.BestSkill}" : "";
        _perSecond.Text = DamageCard.Number((long)s.PerSecond);
        _hits.Text = DamageCard.Number(s.Hits);
        _misses.Text = s.Misses > 0 ? $"{s.Misses} verfehlt" : "";
        _status.Text = s.CharacterId == null
            ? "Noch kein Charakter erkannt. Wechsle einmal die Map, dann zählt FMT deine Treffer."
            : $"Zählt für {s.Name}. Eigene Treffer zählen, Pets und Partner nicht.";
    }
}
