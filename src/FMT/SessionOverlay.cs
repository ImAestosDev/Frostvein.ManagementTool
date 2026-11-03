namespace Fmt.App;

// Quick start and stop of a session, with the warning once its time is up.
public sealed class SessionCard : Border
{
    public const string Warning = "Session ist vorbei";

    private readonly TextBlock _time = Ui.T("", 15, bold: true);
    private readonly TextBlock _warning = Ui.T(Warning, 15, "Negative", bold: true);
    private readonly Button _start;
    private readonly Button _end;
    private readonly Button _reset;

    public SessionCard(Action start, Action end, Action reset)
    {
        _reset = Ui.IconBtn(Ui.G.Undo, "Session zurücksetzen (sie bleibt im Verlauf)", reset, 12);
        _reset.Padding = new Thickness(5, 2, 5, 2);
        _reset.MinHeight = 0;
        _reset.Margin = new Thickness(4, 0, 0, 0);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 6, 6, 6);
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(CornerRadiusProperty, "CornerSmall");
        _start = Ui.Btn("Start", start, "Primary");
        _end = Ui.Btn("Beenden", end, "Primary");
        _start.Margin = _end.Margin = new Thickness(10, 0, 0, 0);
        _warning.Margin = new Thickness(0, 4, 4, 0);
        Child = Ui.Col(0, Ui.Cols("Auto,Auto,Auto,Auto", _time, _start, _end, _reset), _warning);
        Show(null);
    }

    public void Show(SessionStats? stats)
    {
        var running = stats is { Running: true };
        _start.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        _end.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _reset.Visibility = stats == null ? Visibility.Collapsed : Visibility.Visible;
        _warning.Visibility = stats is { TimeIsUp: true } ? Visibility.Visible : Visibility.Collapsed;
        _time.Text = stats == null ? "Session" : ExpView.Clock(stats.Duration);
    }
}

public sealed class SessionOverlay : GameOverlay
{
    private readonly SessionCard _card;

    public SessionOverlay(AppState state, GameWindowTracker tracker, PacketCapture capture)
        : base(state, tracker, capture, "FMT Session")
    {
        _card = new SessionCard(
            () => { Capture.StartSession(GamePid()); ShowCard(); },
            () => { Capture.EndSession(GamePid()); ShowCard(); },
            () => { Capture.ResetSession(GamePid()); ShowCard(); })
        {
            Cursor = Cursors.SizeAll,
        };
        Host(_card);
    }

    protected override bool Enabled => State.S.ShowSessionOverlay;

    protected override (double X, double Y) Position
    {
        get => (State.S.SessionOverlayX, State.S.SessionOverlayY);
        set => (State.S.SessionOverlayX, State.S.SessionOverlayY) = value;
    }

    protected override void ShowCard() => _card.Show(Capture.Session(GamePid()));
}
