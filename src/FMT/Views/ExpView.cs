using System.Globalization;
using System.Windows.Threading;

namespace Fmt.App;

// Live level, job and hero progress of the active client.
public sealed class ExpView : PageView
{
    private static readonly CultureInfo German = new("de-DE");

    private readonly PanelWindow _shell;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel _tracks = new();
    private readonly TextBlock _status = Ui.T("", 12, "Muted", wrap: true);
    private readonly TextBlock _last = Ui.T("", 12, "Muted", wrap: true);

    public ExpView(PanelWindow shell)
    {
        _shell = shell;
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public override void Refresh()
    {
        (_status.Parent as Panel)?.Children.Remove(_status);
        (_last.Parent as Panel)?.Children.Remove(_last);
        (_tracks.Parent as Panel)?.Children.Remove(_tracks);
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
            Ui.AddTo(col, _status, 10);
            Ui.AddTo(col, _last, 4);
            Update();
        }
        Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // The reset button stays outside: a rebuild during a click would swallow it.
    private void Update()
    {
        if (_shell.Capture is not { IsRunning: true } capture) return;
        var stats = capture.Exp(_shell.GamePid);
        _tracks.Children.Clear();
        foreach (var track in stats.Tracks) Ui.AddTo(_tracks, TrackCard(track), 10);
        _status.Text = stats.Tracks.Count == 0
            ? "Warte auf EXP. Die Werte erscheinen nach dem nächsten Kill oder Mapwechsel."
            : $"Läuft seit {Time(stats.Elapsed)}. „Erhalten“ zählt ab dann.";
        _last.Text = stats.LastPacket is { } packet ? "Letztes Paket: " + packet : "";
    }

    private static Border TrackCard(ExpTrack t)
    {
        var levels = t.LevelsGained > 0 ? $"  ·  +{t.LevelsGained} Level" : "";
        var header = Ui.Cols("*,Auto", Ui.T($"{t.Label} {t.Level}", 13, "Muted"), Ui.T(Percent(t.Percent), 22, bold: true));
        return Ui.Card(Ui.Col(8,
            header,
            Ui.Bar(t.Percent / 100, height: 6),
            Ui.T($"{DamageCard.Number(t.Xp)} / {DamageCard.Number(t.Load)}", 12, "Muted"),
            new UniformRow(10,
                Stat("Erhalten", "+" + DamageCard.Number(t.Gained), "+" + Percent(t.GainedPercent) + levels),
                Stat("Letzter Gewinn", t.LastGain > 0 ? "+" + DamageCard.Number(t.LastGain) : "–", ""),
                Stat("Pro Stunde", t.PerHour is { } rate ? DamageCard.Number(rate) : "–", t.TimeToLevel is { } left ? "Level in " + Time(left) : ""))));
    }

    private static StackPanel Stat(string label, string value, string note) =>
        Ui.Col(1, Ui.T(label, 12, "Muted"), Ui.T(value, 15, bold: true, trim: true), note.Length > 0 ? Ui.T(note, 12, "Muted", trim: true) : null);

    private static string Percent(double value) => value.ToString("0.00", German) + " %";

    private static string Time(TimeSpan span)
    {
        if (span.TotalDays >= 100) return "über 100 Tagen";
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{Math.Max(span.Minutes, 0)} min";
    }
}
