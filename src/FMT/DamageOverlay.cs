using System.Globalization;

namespace Fmt.App;

// Compact player list, shared by the overlay and dev renders.
public sealed class DamageCard : Border
{
    private const string LargeColumns = "30,160,116,96", SmallColumns = "20,100,84,70";
    private const double LargeRow = 24, SmallRow = 17;
    private static readonly CultureInfo German = new("de-DE");
    private static readonly (string Label, DamageMode Mode)[] Modes =
        [("Raid", DamageMode.Raid), ("Single", DamageMode.Single), ("Group", DamageMode.Group)];

    private readonly StackPanel _rows = new();
    private readonly StackPanel _settings = new();
    private readonly Action _reset;
    private readonly Action<DamageMode> _setMode;
    private readonly Action _toggleSize;
    private List<PlayerDamage>? _shown;
    private DamageMode? _shownMode;
    private bool? _shownSmall;

    public DamageCard(Action reset, Action<DamageMode> setMode, Action toggleSize)
    {
        _reset = reset;
        _setMode = setMode;
        _toggleSize = toggleSize;
        BorderThickness = new Thickness(1);
        Cursor = Cursors.SizeAll;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(CornerRadiusProperty, "CornerSmall");
        Show(DamageStats.Empty, DamageMode.Single, false);
    }

    public void Show(DamageStats stats, DamageMode mode, bool small)
    {
        var table = stats.Table(mode);
        if (mode != _shownMode || small != _shownSmall) BuildSettings(mode, small);
        if (_shown != null && mode == _shownMode && small == _shownSmall && _shown.SequenceEqual(table)) return;
        _shown = table;
        _shownMode = mode;
        _shownSmall = small;

        // Every slot of the mode is always there, so the table size follows the mode.
        var size = small ? 12 : 15;
        _rows.Children.Clear();
        _rows.Children.Add(Row("", "Spieler", "Schaden", "Best Hit", "Muted", small ? 10 : 12, false, small));
        for (var i = 0; i < DamageStats.Slots(mode); i++)
        {
            _rows.Children.Add(i < table.Count
                ? Row($"{i + 1}.", table[i].Name, Number(table[i].Total), Number(table[i].Best), table[i].IsOwn ? "Accent" : "Ink", size, table[i].IsOwn, small)
                : Row($"{i + 1}.", "–", "–", "–", "Muted", size, false, small));
        }
    }

    // Large: column left of the table. Small: slim row above it.
    private void BuildSettings(DamageMode active, bool small)
    {
        (_settings.Parent as Panel)?.Children.Remove(_settings);
        (_rows.Parent as Panel)?.Children.Remove(_rows);
        _settings.Children.Clear();
        _settings.Orientation = small ? Orientation.Horizontal : Orientation.Vertical;
        _settings.Width = small ? double.NaN : 76;
        _settings.Margin = small ? new Thickness(0, 0, 0, 3) : new Thickness(0, 0, 12, 0);
        Padding = small ? new Thickness(8, 4, 8, 5) : new Thickness(8);

        foreach (var (label, mode) in Modes)
            Ui.AddTo(_settings, Text(label, () => _setMode(mode), mode == active ? "Primary" : "Ghost", small), small ? 2 : 4);
        if (small)
        {
            Ui.AddTo(_settings, Icon(Ui.G.Undo, "Zähler zurücksetzen", _reset), 2);
            Ui.AddTo(_settings, Icon(Ui.G.Grow, "Zähler groß machen", _toggleSize), 2);
            Child = Ui.Col(0, _settings, _rows);
        }
        else
        {
            Ui.AddTo(_settings, Ui.Btn("Reset", _reset, "Ghost", Ui.G.Undo, "Zähler zurücksetzen"), 4);
            Ui.AddTo(_settings, Ui.Btn("Klein", _toggleSize, "Ghost", Ui.G.Shrink, "Zähler klein machen"), 4);
            Child = Ui.Cols("Auto,*", _settings, _rows);
        }
    }

    private static Button Text(string label, Action click, string style, bool small)
    {
        var button = Ui.Btn(label, click, style);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        if (small)
        {
            button.Padding = new Thickness(6, 1, 6, 1);
            button.MinHeight = 0;
            ((TextBlock)button.Content).FontSize = 11;
        }
        return button;
    }

    private static Button Icon(string glyph, string tip, Action click)
    {
        var button = Ui.IconBtn(glyph, tip, click, 11);
        button.Padding = new Thickness(5, 1, 5, 1);
        button.MinHeight = 0;
        return button;
    }

    private static Grid Row(string place, string name, string total, string best, string brush, double size, bool bold, bool small)
    {
        var right1 = Ui.T(total, size, brush, bold);
        var right2 = Ui.T(best, size, brush, bold);
        right1.TextAlignment = right2.TextAlignment = TextAlignment.Right;
        var row = Ui.Cols(small ? SmallColumns : LargeColumns, Ui.T(place, size, "Muted"), Ui.T(name, size, brush, bold, trim: true), right1, right2);
        row.Height = small ? SmallRow : LargeRow;
        return row;
    }

    public static string Number(long value) => value.ToString("N0", German);
}

public sealed class DamageOverlay : GameOverlay
{
    private readonly DamageCard _card;

    public DamageOverlay(AppState state, GameWindowTracker tracker, PacketCapture capture)
        : base(state, tracker, capture, "FMT Damage")
    {
        _card = new DamageCard(() => Capture.ResetDamage(GamePid()), mode =>
        {
            State.S.DamageMode = mode;
            State.Touch();
            ShowCard();
        }, () =>
        {
            State.S.DamageCounterSmall = !State.S.DamageCounterSmall;
            State.Touch();
            ShowCard();
        });
        Host(_card);
    }

    protected override bool Enabled => State.S.ShowDamageOverlay;

    protected override (double X, double Y) Position
    {
        get => (State.S.DamageOverlayX, State.S.DamageOverlayY);
        set => (State.S.DamageOverlayX, State.S.DamageOverlayY) = value;
    }

    protected override void ShowCard() => _card.Show(Capture.Stats(GamePid()), State.S.DamageMode, State.S.DamageCounterSmall);
}
