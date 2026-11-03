namespace Fmt.App;

// Asgobas on top, below the two groups of dragons; shared by the tab and the overlay.
public sealed class LandCard : Border
{
    private const string Gold = "Golddrache", Holy = "Heiliger Untoter Drache";
    private const double RowWidth = 240;

    private readonly StackPanel _rows = new() { Width = RowWidth };

    public LandCard()
    {
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 7, 10, 8);
        HorizontalAlignment = HorizontalAlignment.Left;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(CornerRadiusProperty, "CornerSmall");
        Child = _rows;
        Show(LandStats.Empty);
    }

    public void Show(LandStats stats)
    {
        _rows.Children.Clear();
        Asgobas(stats.Asgobas);
        Group(Gold, stats.GoldDragons, 8);
        Group(Holy, stats.HolyDragons, 12);
    }

    private void Asgobas(AsgobasTimer a)
    {
        var (state, brush) = a.Phase switch
        {
            AsgobasPhase.Present => ("Asgobas ist da", "Negative"),
            AsgobasPhase.Waiting => ("Asgobas", "Accent"),
            _ => ("Asgobas", "Muted"),
        };
        var time = a.Phase == AsgobasPhase.Unknown ? "–" : Clock(a.Remaining);
        Ui.AddTo(_rows, Ui.Cols("*,Auto", Ui.T(state, 15, a.Phase == AsgobasPhase.Present ? "Negative" : "Ink", bold: true), Ui.T(time, 15, bold: true)), 0);
        Ui.AddTo(_rows, Ui.Bar(a.Fraction, brush, 10), 3);
    }

    private void Group(string name, IReadOnlyList<DragonSlot> dragons, double gap)
    {
        var first = true;
        foreach (var d in dragons)
        {
            var time = d.Id == null ? "–" : d.Dead ? Clock(d.Remaining) : "Lebt";
            Ui.AddTo(_rows, Ui.Cols("*,Auto", Ui.T(name, 12, d.Id == null ? "Muted" : "Ink", trim: true), Ui.T(time, 12, d.Id == null ? "Muted" : d.Dead ? "Ink" : "Positive", bold: d.Dead)), first ? gap : 4);
            Ui.AddTo(_rows, Ui.Bar(d.Dead ? d.Fraction : d.Id == null ? 0 : 1, d.Dead ? "Accent" : "Positive", 5), 2);
            first = false;
        }
    }

    // A timer past zero counts up: the game does not always keep to its schedule.
    private static string Clock(TimeSpan span)
    {
        var seconds = (int)Math.Ceiling(Math.Abs(span.TotalSeconds));
        return $"{(span < TimeSpan.Zero ? "+" : "")}{seconds / 60}:{seconds % 60:00}";
    }
}

public sealed class LandOverlay : GameOverlay
{
    private readonly LandCard _card = new() { Cursor = Cursors.SizeAll };

    public LandOverlay(AppState state, GameWindowTracker tracker, PacketCapture capture)
        : base(state, tracker, capture, "FMT Land der Lebenden")
    {
        Host(_card);
    }

    // The timers keep running in the background even while the card is hidden.
    protected override bool Enabled =>
        State.S.ShowLandOverlay && (!State.S.LandOverlayOnlyInLand || Capture.Land(GamePid(), State.S.LandOfLife).InLand);

    protected override (double X, double Y) Position
    {
        get => (State.S.LandOverlayX, State.S.LandOverlayY);
        set => (State.S.LandOverlayX, State.S.LandOverlayY) = value;
    }

    protected override void ShowCard() => _card.Show(Capture.Land(GamePid(), State.S.LandOfLife));
}
