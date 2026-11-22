using System.Diagnostics;
using System.Globalization;
using System.Windows.Shapes;

namespace Fmt.App;

// Frame rate of the tracked client, its frame times as a line, and the other clients below.
public sealed class FpsCard : Border
{
    private const double GraphWidth = 170, GraphHeight = 28;
    private static readonly CultureInfo German = new("de-DE");

    private readonly TextBlock _fps = Ui.T("–", 22, bold: true);
    private readonly TextBlock _source = Ui.T("", 11, "Muted");
    private readonly TextBlock _detail = Ui.T("", 11, "Muted");
    private readonly Polyline _line = new() { StrokeThickness = 1.2 };
    private readonly Line _target = new() { X1 = 0, X2 = GraphWidth, StrokeThickness = 1, StrokeDashArray = [2, 3] };
    private readonly TextBlock _others = Ui.T("", 11, "Muted");

    public FpsCard()
    {
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 5, 10, 7);
        Cursor = Cursors.SizeAll;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(CornerRadiusProperty, "CornerSmall");
        _line.SetResourceReference(Shape.StrokeProperty, "Accent");
        _target.SetResourceReference(Shape.StrokeProperty, "Line");
        var graph = new Canvas { Width = GraphWidth, Height = GraphHeight, ClipToBounds = true };
        graph.Children.Add(_target);
        graph.Children.Add(_line);
        var head = Ui.Cols("Auto,Auto,*", _fps, Ui.T(" FPS", 12, "Muted"), _source);
        _source.HorizontalAlignment = HorizontalAlignment.Right;
        _fps.MinWidth = 44;
        Child = Ui.Col(2, head, _detail, graph, _others);
        Show(FrameStats.None, []);
    }

    public void Show(FrameStats s, IReadOnlyList<(int Number, FrameStats Stats)> others)
    {
        if (!s.HasData)
        {
            _fps.Text = "–";
            _fps.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            _source.Text = "";
            _detail.Text = "Keine Bilder (minimiert?)";
            _line.Points = [];
        }
        else
        {
            _fps.Text = s.Fps.ToString("0", German);
            _fps.SetResourceReference(TextBlock.ForegroundProperty, s.Fps >= 55 ? "Positive" : s.Fps >= 30 ? "Accent" : "Negative");
            _source.Text = s.Source;
            _detail.Text = $"Ø {s.AvgMs.ToString("0.0", German)} ms · 1% {s.LowFps.ToString("0", German)} · max {s.MaxMs.ToString("0", German)} ms";
            Graph(s.Recent);
        }
        _others.Visibility = others.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _others.Text = string.Join("   ", others.Select(o => $"#{o.Number} {(o.Stats.HasData ? o.Stats.Fps.ToString("0", German) : "–")}"));
    }

    // Scale fits the worst frame but never below 33 ms, so a smooth game reads as a flat line.
    private void Graph(double[] ms)
    {
        var top = Math.Max(33.3, ms.Max() * 1.1);
        var points = new PointCollection(ms.Length);
        var step = GraphWidth / Math.Max(1, ms.Length - 1);
        for (var i = 0; i < ms.Length; i++) points.Add(new Point(i * step, GraphHeight - ms[i] / top * GraphHeight));
        _line.Points = points;
        _target.Y1 = _target.Y2 = GraphHeight - 16.7 / top * GraphHeight; // 60 FPS mark
    }
}

public sealed class FpsOverlay : GameOverlay
{
    private readonly FpsCard _card = new();
    private readonly FrameCounter _frames;
    private DateTime _clientsAt;
    private uint[] _clients = [];

    public FpsOverlay(AppState state, GameWindowTracker tracker, PacketCapture capture, FrameCounter frames)
        : base(state, tracker, capture, "FMT FPS")
    {
        _frames = frames;
        Host(_card);
    }

    protected override bool NeedsCapture => false;
    protected override bool Enabled => State.S.ShowFpsOverlay && _frames.IsRunning;

    protected override (double X, double Y) Position
    {
        get => (State.S.FpsOverlayX, State.S.FpsOverlayY);
        set => (State.S.FpsOverlayX, State.S.FpsOverlayY) = value;
    }

    protected override void ShowCard()
    {
        if (!IsVisible) return;
        var pid = GamePid();
        if (DateTime.UtcNow - _clientsAt > TimeSpan.FromSeconds(2))
        {
            _clientsAt = DateTime.UtcNow;
            _clients = [.. Process.GetProcessesByName("NostaleClientX").Select(p => (uint)p.Id).Order()];
        }
        var others = _clients.Select((p, i) => (Pid: p, Number: i + 1)).Where(c => c.Pid != pid)
            .Select(c => (c.Number, _frames.Stats(c.Pid))).ToList();
        _card.Show(_frames.Stats(pid), others);
    }
}
