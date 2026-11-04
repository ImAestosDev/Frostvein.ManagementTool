namespace Fmt.App;

// Row of small buttons styled like the game's own, each one switches an overlay on or off.
public sealed class OverlayBarCard : Border
{
    private const double Gap = 2;

    public sealed record Entry(string Glyph, string Name, string Color, Func<Settings, bool> Get, Action<Settings, bool> Set);

    public static readonly Entry[] Entries =
    [
        new(Ui.G.Clock, "Land der Lebenden", "#6FB7E9", s => s.ShowLandOverlay, (s, v) => s.ShowLandOverlay = v),
        new(Ui.G.Session, "Session", "#B79CED", s => s.ShowSessionOverlay, (s, v) => s.ShowSessionOverlay = v),
        new(Ui.G.Drops, "Drops", "#F0D264", s => s.ShowDropOverlay, (s, v) => s.ShowDropOverlay = v),
        new(Ui.G.Exp, "EXP", "#5CC28F", s => s.ShowExpOverlay, (s, v) => s.ShowExpOverlay = v),
        new(Ui.G.Damage, "Damage", "#E5787C", s => s.ShowDamageOverlay, (s, v) => s.ShowDamageOverlay = v),
    ];

    private readonly Settings _settings;
    private readonly Action _changed;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly List<(Entry Entry, Border Slot, TextBlock Glyph)> _slots = [];
    private readonly Border _done;
    private double _builtSize;

    public OverlayBarCard(Settings settings, Action changed)
    {
        _settings = settings;
        _changed = changed;
        BorderThickness = new Thickness(1);
        Background = Brushes.Transparent;
        var check = Ui.Glyph(Ui.G.Check, 13, "Positive");
        _done = Frame(check);
        _done.ToolTip = "Edit Mode beenden";
        _done.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _done.MouseLeftButtonUp += (_, _) =>
        {
            _settings.OverlayBarEdit = false;
            _changed();
        };
        Child = _row;
        Update();
    }

    // Measured from the game's buttons: light outer edge, dark inner edge, near black fill.
    private static Border Frame(UIElement content) => new()
    {
        BorderThickness = new Thickness(1),
        BorderBrush = Ui.Hex("#767676"),
        Background = Ui.Hex("#141414"),
        Cursor = Cursors.Hand,
        SnapsToDevicePixels = true,
        Child = new Border { BorderThickness = new Thickness(1), BorderBrush = Ui.Hex("#2F2F2F"), Child = content },
    };

    private void Rebuild(double size)
    {
        _builtSize = size;
        _row.Children.Clear();
        _slots.Clear();
        Add(_done, size);
        foreach (var entry in Entries)
        {
            var glyph = Ui.Glyph(entry.Glyph, Math.Round(size * 0.55));
            var slot = Frame(glyph);
            slot.ToolTip = entry.Name;
            // Pressing a button never starts a drag of the bar.
            slot.MouseLeftButtonDown += (_, e) => e.Handled = true;
            slot.MouseLeftButtonUp += (_, _) =>
            {
                entry.Set(_settings, !entry.Get(_settings));
                _changed();
            };
            Add(slot, size);
            _slots.Add((entry, slot, glyph));
        }
    }

    private void Add(Border slot, double size)
    {
        slot.Width = slot.Height = size;
        Ui.AddTo(_row, slot, Gap);
    }

    public void Update()
    {
        if (_builtSize != _settings.OverlayBarSize) Rebuild(_settings.OverlayBarSize);
        var edit = _settings.OverlayBarEdit;
        foreach (var (entry, slot, glyph) in _slots)
        {
            var on = entry.Get(_settings);
            glyph.Foreground = on ? Ui.Hex(entry.Color) : Ui.Hex("#5A5A5A");
            slot.ToolTip = $"{entry.Name}: {(on ? "an" : "aus")}";
            // In Edit Mode the whole bar is a handle.
            slot.IsHitTestVisible = !edit;
        }
        _done.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        Cursor = edit ? Cursors.SizeAll : null;
        Background = edit ? Ui.Hex("#55000000") : Brushes.Transparent;
        BorderBrush = edit ? Ui.Hex("#F0D264") : Brushes.Transparent;
    }
}

public sealed class OverlayBar : GameOverlay
{
    private readonly OverlayBarCard _card;

    public OverlayBar(AppState state, GameWindowTracker tracker, PacketCapture capture)
        : base(state, tracker, capture, "FMT Symbole")
    {
        _card = new OverlayBarCard(state.S, () =>
        {
            State.TouchSettings();
            ShowCard();
        });
        Host(_card);
    }

    protected override bool Enabled => State.S.ShowOverlayBar;
    protected override bool CanDrag => State.S.OverlayBarEdit;

    // Unused: the bar keeps pixel distances to the corner, the game's buttons do not scale.
    protected override (double X, double Y) Position { get; set; }

    protected override (int X, int Y) Target(RECT game, RECT own) =>
        (game.Right - (int)State.S.OverlayBarRight - own.Width, game.Bottom - (int)State.S.OverlayBarBottom - own.Height);

    protected override void Dropped(RECT game, RECT own)
    {
        State.S.OverlayBarRight = game.Right - own.Right;
        State.S.OverlayBarBottom = game.Bottom - own.Bottom;
    }

    protected override void ShowCard() => _card.Update();
}
