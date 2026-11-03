using System.Windows.Threading;

namespace Fmt.App;

// Live level, job and hero progress of the active client.
public sealed class ExpView : PageView
{
    private readonly AppState _state;
    private readonly PanelWindow _shell;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel _tracks = new();
    private readonly StackPanel _recordRows = new();
    private readonly TextBlock _recordTitle = Ui.T("", 13, "Muted");
    private readonly TextBlock _status = Ui.T("", 12, "Muted", wrap: true);
    private Button? _start, _stop;

    public ExpView(AppState state, PanelWindow shell)
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
        (_tracks.Parent as Panel)?.Children.Remove(_tracks);
        (_recordRows.Parent as Panel)?.Children.Remove(_recordRows);
        (_recordTitle.Parent as Panel)?.Children.Remove(_recordTitle);
        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        Ui.AddTo(col, Ui.T("EXP Tracker", 18, bold: true), 0);
        if (_shell.Capture is not { IsRunning: true } capture)
        {
            Ui.AddTo(col, DamageView.StartCard(_shell, Refresh), 10);
        }
        else
        {
            var reset = Ui.Btn("Zurücksetzen", () => { capture.ResetExp(_shell.GamePid); Update(); }, "Primary", Ui.G.Undo);
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            Ui.AddTo(col, _tracks, 10);
            Ui.AddTo(col, reset, 10);
            Ui.AddTo(col, Ui.Check("Kleiner Zähler über dem Spiel", _state.S.ShowExpOverlay, v => { _state.S.ShowExpOverlay = v; _state.TouchSettings(); }), 10);
            Ui.AddTo(col, _status, 10);
            Ui.AddTo(col, Ui.T("Recorder", 14, bold: true), 16);
            Ui.AddTo(col, RecorderCard(capture), 8);
            Update();
        }
        Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private Border RecorderCard(PacketCapture capture)
    {
        _start = Ui.Btn("Start", () => { capture.StartExpRecording(_shell.GamePid); Update(); }, "Primary");
        _stop = Ui.Btn("Beenden", () => { capture.StopExpRecording(_shell.GamePid); Update(); }, "Primary");
        return Ui.Card(Ui.Col(8, Ui.Cols("*,Auto,Auto", _recordTitle, _start, _stop), _recordRows));
    }

    // Buttons stay outside the rebuilt rows: a rebuild during a click would swallow it.
    private void Update()
    {
        if (_shell.Capture is not { IsRunning: true } capture) return;
        var stats = capture.Exp(_shell.GamePid);
        _tracks.Children.Clear();
        foreach (var track in stats.Tracks) Ui.AddTo(_tracks, TrackCard(track), 10);
        _status.Text = stats.Tracks.Count == 0
            ? "Warte auf EXP. Die Werte erscheinen nach dem nächsten Kill oder Mapwechsel."
            : $"Läuft seit {Time(stats.Elapsed)}. „Erhalten“ zählt ab dann.";
        ShowRecording(stats.Recording);
    }

    private void ShowRecording(ExpRecording? recording)
    {
        var running = recording is { Running: true };
        if (_start != null) _start.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        if (_stop != null) _stop.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _recordTitle.Text = recording == null
            ? "Start merkt sich deinen Stand, Beenden rechnet den Stundenschnitt aus."
            : running ? $"Läuft seit {Clock(recording.Duration)}" : $"Ergebnis nach {Clock(recording.Duration)}";

        _recordRows.Children.Clear();
        if (recording == null) return;
        Ui.AddTo(_recordRows, RecordRow("", "Erhalten", running ? "" : "Pro Stunde", "Muted", 12, false), 0);
        foreach (var row in recording.Rows)
        {
            var perHour = running ? "" : ExpCard.Percent(row.PercentPerHour);
            Ui.AddTo(_recordRows, RecordRow(row.Label, "+" + ExpCard.Percent(row.GainedPercent), perHour, "Ink", 14, !running), 4);
        }
    }

    public static Grid RecordRow(string label, string gained, string perHour, string brush, double size, bool bold)
    {
        var gainedText = Ui.T(gained, size, brush);
        var perHourText = Ui.T(perHour, size, brush, bold);
        gainedText.TextAlignment = perHourText.TextAlignment = TextAlignment.Right;
        return Ui.Cols("*,110,110", Ui.T(label, size, brush), gainedText, perHourText);
    }

    private static Border TrackCard(ExpTrack t)
    {
        var levels = t.LevelsGained > 0 ? $"  ·  +{t.LevelsGained} Level" : "";
        return Ui.Card(Ui.Col(8,
            Ui.Cols("*,Auto", Ui.T($"{t.Label} {t.Level}", 13, "Muted"), Ui.T(ExpCard.Percent(t.Percent), 22, bold: true)),
            Ui.Bar(t.Percent / 100, height: 6),
            Ui.T($"Erhalten +{ExpCard.Percent(t.GainedPercent)}{levels}", 13)));
    }

    private static string Time(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{Math.Max(span.Minutes, 0)} min";

    public static string Clock(TimeSpan span) => $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
}
