using System.Windows.Threading;

namespace Fmt.App;

// Used items and running buffs of the active client.
public sealed class ItemsView : PageView
{
    private const string UnknownInventory = "FMT kennt dein Inventar noch nicht. Der erste Trank pro Stapel fehlt, bis du einmal neu in deinen Charakter gehst, während FMT mitliest.";

    private readonly PanelWindow _shell;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly StackPanel _used = new();
    private readonly StackPanel _buffs = new();
    private readonly TextBlock _hint = Ui.T("", 12, "Muted", wrap: true);
    private FrameworkElement? _buffsCard;

    public ItemsView(PanelWindow shell)
    {
        _shell = shell;
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public override void Refresh()
    {
        if (_used.Parent is Border used) used.Child = null;
        if (_buffs.Parent is Border buffs) buffs.Child = null;
        (_hint.Parent as Panel)?.Children.Remove(_hint);
        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        Ui.AddTo(col, Ui.T("Item Tracker", 18, bold: true), 0);
        if (_shell.Capture is not { IsRunning: true } capture)
        {
            Ui.AddTo(col, DamageView.StartCard(_shell, Refresh), 10);
        }
        else
        {
            var reset = Ui.Btn("Zurücksetzen", () => { capture.ResetItems(_shell.GamePid); Update(); }, "Primary", Ui.G.Undo);
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            Ui.AddTo(col, Ui.Card(_used), 10);
            Ui.AddTo(col, reset, 10);
            Ui.AddTo(col, _hint, 10);
            _buffsCard = BuffsSection(_buffs);
            Ui.AddTo(col, _buffsCard, 16);
            Update();
        }
        Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void Update()
    {
        if (_shell.Capture is not { IsRunning: true } capture) return;
        var stats = capture.Items(_shell.GamePid);
        FillUsed(_used, stats.Used);
        FillHint(_hint, stats.InventoryKnown);
        FillBuffs(_buffs, _buffsCard, stats.Buffs);
    }

    public static FrameworkElement BuffsSection(StackPanel buffs) =>
        Ui.Col(8, Ui.T("Aktive Segen und Tränke", 14, bold: true), Ui.Card(buffs));

    public static void FillUsed(StackPanel panel, IReadOnlyList<UsedItem> used)
    {
        panel.Children.Clear();
        var rows = used
            .GroupBy(u => WatchedItems.BaseName(ItemCatalog.Name(u.VNum)))
            .Select(g => (Name: g.Key, g.First().VNum, Count: g.Sum(u => u.Count)))
            .OrderByDescending(r => r.Count);
        foreach (var row in rows)
        {
            var count = Ui.T(DamageCard.Number(row.Count) + "×", 14, bold: true);
            Ui.AddTo(panel, Ui.Cols("30,*,Auto", new Image { Source = ItemCatalog.Icon(row.VNum), Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left }, Ui.T(row.Name, 14), count), 4);
        }
        if (panel.Children.Count == 0) panel.Children.Add(Ui.T("Noch nichts benutzt.", 13, "Muted"));
    }

    public static void FillHint(TextBlock hint, bool inventoryKnown)
    {
        hint.Text = inventoryKnown ? "" : UnknownInventory;
        hint.Visibility = inventoryKnown ? Visibility.Collapsed : Visibility.Visible;
    }

    public static void FillBuffs(StackPanel panel, FrameworkElement? section, IReadOnlyList<ActiveBuff> buffs)
    {
        panel.Children.Clear();
        foreach (var buff in buffs)
        {
            var left = Ui.T(ExpView.Clock(buff.Remaining), 14, bold: true);
            Ui.AddTo(panel, Ui.Cols("*,Auto", Ui.T(ItemCatalog.CardName(buff.CardId), 14), left), 4);
        }
        if (section != null) section.Visibility = buffs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
