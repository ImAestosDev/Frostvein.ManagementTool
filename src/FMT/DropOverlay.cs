using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;

namespace Fmt.App;

// Inventory style grid of the picked up items with the gold below; shared by page and overlay.
public sealed class DropCard : Border
{
    private const int Columns = 6, MinRows = 5;
    private const double SlotSize = 42, IconSize = 32;

    private readonly UniformGrid _slots = new() { Columns = Columns };
    private readonly TextBlock _gold = Ui.T("0", 15, bold: true);
    private List<DroppedItem>? _shown;

    public DropCard(Action reset)
    {
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8);
        HorizontalAlignment = HorizontalAlignment.Left;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(CornerRadiusProperty, "CornerSmall");

        _gold.Foreground = Ui.Hex("#F0D264");
        var coin = new Image { Source = ItemCatalog.Icon(DropTracker.GoldVNum), Width = 24, Height = 24, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Gold" };
        var button = Ui.IconBtn(Ui.G.Undo, "Drops zurücksetzen", reset, 12);
        button.Padding = new Thickness(5, 2, 5, 2);
        button.MinHeight = 0;
        var goldRow = Ui.Cols("Auto,*,Auto,Auto", button, null, _gold, coin);
        goldRow.Margin = new Thickness(0, 6, 0, 0);
        Child = Ui.Col(0, _slots, goldRow);
        Show(DropStats.Empty);
    }

    public void Show(DropStats stats)
    {
        _gold.Text = DamageCard.Number(stats.Gold);
        // Rebuilds only on change, a hovered tooltip would close otherwise.
        if (_shown != null && _shown.SequenceEqual(stats.Items)) return;
        _shown = [.. stats.Items];

        _slots.Children.Clear();
        foreach (var item in stats.Items) _slots.Children.Add(Slot(item));
        var rows = Math.Max(MinRows, (stats.Items.Count + Columns - 1) / Columns);
        while (_slots.Children.Count < rows * Columns) _slots.Children.Add(Slot(null));
    }

    private static Border Slot(DroppedItem? item)
    {
        var slot = new Border
        {
            Width = SlotSize,
            Height = SlotSize,
            Margin = new Thickness(1.5),
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1),
            BorderBrush = Ui.Hex("#7E93AB"),
            Background = new LinearGradientBrush(Ui.ColorOf("#54657A"), Ui.ColorOf("#3E4C5E"), 90),
        };
        if (item == null) return slot;

        var amount = Ui.T(DamageCard.Number(item.Amount), 11, bold: true);
        amount.Foreground = Brushes.White;
        amount.HorizontalAlignment = HorizontalAlignment.Right;
        amount.VerticalAlignment = VerticalAlignment.Bottom;
        amount.Margin = new Thickness(0, 0, 2, 0);
        amount.Effect = new DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Color = Colors.Black, Opacity = 1 };
        var cell = new Grid();
        cell.Children.Add(new Image { Source = ItemCatalog.Icon(item.VNum), Width = IconSize, Height = IconSize });
        cell.Children.Add(amount);
        slot.Child = cell;
        slot.ToolTip = $"{ItemCatalog.Name(item.VNum)} × {DamageCard.Number(item.Amount)}";
        return slot;
    }
}

public sealed class DropOverlay : GameOverlay
{
    private readonly DropCard _card;

    public DropOverlay(AppState state, GameWindowTracker tracker, PacketCapture capture)
        : base(state, tracker, capture, "FMT Drops")
    {
        _card = new DropCard(() => Capture.ResetDrops(GamePid())) { Cursor = Cursors.SizeAll };
        Host(_card);
    }

    protected override bool Enabled => State.S.ShowDropOverlay;

    protected override (double X, double Y) Position
    {
        get => (State.S.DropOverlayX, State.S.DropOverlayY);
        set => (State.S.DropOverlayX, State.S.DropOverlayY) = value;
    }

    protected override void ShowCard() => _card.Show(Capture.Drops(GamePid()));
}
