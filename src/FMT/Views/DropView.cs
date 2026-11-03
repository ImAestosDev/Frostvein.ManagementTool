using System.Windows.Threading;

namespace Fmt.App;

// Picked up items and gold of the active client.
public sealed class DropView : PageView
{
    private readonly AppState _state;
    private readonly PanelWindow _shell;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly TextBlock _status = Ui.T("", 12, "Muted", wrap: true);
    private DropCard? _card;

    public DropView(AppState state, PanelWindow shell)
    {
        _state = state;
        _shell = shell;
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public override void Refresh()
    {
        (_status.Parent as Panel)?.Children.Remove(_status);
        _card = null;
        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        Ui.AddTo(col, Ui.T("Drop Tracker", 18, bold: true), 0);
        if (_shell.Capture is not { IsRunning: true } capture)
        {
            Ui.AddTo(col, DamageView.StartCard(_shell, Refresh), 10);
        }
        else
        {
            _card = new DropCard(() => { capture.ResetDrops(_shell.GamePid); Update(); });
            Ui.AddTo(col, _card, 10);
            Ui.AddTo(col, Ui.Check("Kleiner Tracker über dem Spiel", _state.S.ShowDropOverlay, v => { _state.S.ShowDropOverlay = v; _state.TouchSettings(); }), 10);
            Ui.AddTo(col, _status, 10);
            Update();
        }
        Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void Update()
    {
        if (_shell.Capture is not { IsRunning: true } capture) return;
        _card?.Show(capture.Drops(_shell.GamePid));
        _status.Text = capture.Stats(_shell.GamePid).OwnId == null
            ? "Dein Charakter ist noch nicht erkannt, deshalb zählt noch nichts. Wechsel einmal die Map."
            : "Gezählt wird, was du oder dein Pet aufhebt. Fahr über ein Item für den Namen.";
    }
}
