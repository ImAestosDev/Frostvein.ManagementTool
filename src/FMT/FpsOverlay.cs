using System.Globalization;

namespace Fmt.App;

// One small fixed-size row: FPS, 1% low and the average frame time. Fixed widths, so it never jumps.
public sealed class FpsCard : Border
{
    private static readonly CultureInfo German = new("de-DE");

    private readonly TextBlock _fps = Field(30, 14);
    private readonly TextBlock _low = Field(24, 12);
    private readonly TextBlock _ms = Field(34, 12);

    private static TextBlock Field(double width, double size)
    {
        var t = Ui.T("–", size, bold: true);
        t.Width = width;
        t.TextAlignment = TextAlignment.Right;
        return t;
    }

    public FpsCard()
    {
        BorderThickness = new Thickness(1);
        Padding = new Thickness(7, 3, 8, 3);
        Cursor = Cursors.SizeAll;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(CornerRadiusProperty, "CornerSmall");
        _low.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _low.TextAlignment = TextAlignment.Left;
        _ms.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        Child = Ui.Row(0,
            _fps, Label(" FPS"),
            Label("1%"), _low,
            _ms, Label(" ms"));
        Show(FrameStats.None);
    }

    private static TextBlock Label(string text)
    {
        var t = Ui.T(text, 11, "Muted");
        t.Margin = new Thickness(2, 0, 6, 0);
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    public void Show(FrameStats s)
    {
        if (!s.HasData)
        {
            _fps.Text = _low.Text = _ms.Text = "–";
            _fps.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            return;
        }
        _fps.Text = s.Fps.ToString("0", German);
        _fps.SetResourceReference(TextBlock.ForegroundProperty, s.Fps >= 55 ? "Positive" : s.Fps >= 30 ? "Accent" : "Negative");
        _low.Text = s.LowFps.ToString("0", German);
        _ms.Text = s.AvgMs.ToString("0.0", German);
    }
}
public sealed class FpsOverlay : GameOverlay
{
    private readonly FpsCard _card = new();
    private readonly FrameCounter _frames;

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
        _card.Show(_frames.Stats(GamePid()));
    }
}
