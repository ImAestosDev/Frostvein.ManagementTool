using System.Globalization;

namespace Fmt.App;

// Gained percent per track, shared by the overlay and dev renders.
public sealed class ExpCard : Border
{
    private const string Columns = "46,76";
    private const double RowHeight = 24;
    private static readonly CultureInfo German = new("de-DE");
    private static readonly string[] Placeholders = ["EXP", "JXP", "HXP"];

    private readonly StackPanel _rows = new();

    public ExpCard(Action reset)
    {
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 6, 6, 6);
        Cursor = Cursors.SizeAll;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(CornerRadiusProperty, "CornerSmall");
        var button = Ui.IconBtn(Ui.G.Undo, "Zähler zurücksetzen", reset, 12);
        button.Padding = new Thickness(5, 2, 5, 2);
        button.MinHeight = 0;
        button.Margin = new Thickness(6, 0, 0, 0);
        button.VerticalAlignment = VerticalAlignment.Top;
        Child = Ui.Cols("Auto,Auto", _rows, button);
        Show(ExpStats.Empty);
    }

    public void Show(ExpStats stats)
    {
        _rows.Children.Clear();
        if (stats.Tracks.Count == 0)
        {
            foreach (var name in Placeholders) _rows.Children.Add(Row(name, "–"));
            return;
        }
        foreach (var track in stats.Tracks) _rows.Children.Add(Row(track.Short, Percent(track.GainedPercent)));
    }

    private static Grid Row(string name, string value)
    {
        var text = Ui.T(value, 15, bold: true);
        text.TextAlignment = TextAlignment.Right;
        var row = Ui.Cols(Columns, Ui.T(name, 15, "Muted"), text);
        row.Height = RowHeight;
        return row;
    }

    public static string Percent(double value) => value.ToString("0.00", German) + " %";
}

public sealed class ExpOverlay : GameOverlay
{
    private readonly ExpCard _card;

    public ExpOverlay(AppState state, GameWindowTracker tracker, PacketCapture capture)
        : base(state, tracker, capture, "FMT EXP")
    {
        _card = new ExpCard(() => Capture.ResetExp(GamePid()));
        Host(_card);
    }

    protected override bool Enabled => State.S.ShowExpOverlay;

    protected override (double X, double Y) Position
    {
        get => (State.S.ExpOverlayX, State.S.ExpOverlayY);
        set => (State.S.ExpOverlayX, State.S.ExpOverlayY) = value;
    }

    protected override void ShowCard() => _card.Show(Capture.Exp(GamePid()));
}
