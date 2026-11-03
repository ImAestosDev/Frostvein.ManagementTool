using System.Windows.Threading;

namespace Fmt.App;

// Killed monsters of the active client.
public sealed class MonstersView : PageView
{
    private readonly PanelWindow _shell;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly StackPanel _kills = new();

    public MonstersView(PanelWindow shell)
    {
        _shell = shell;
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public override void Refresh()
    {
        if (_kills.Parent is Border kills) kills.Child = null;
        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        Ui.AddTo(col, Ui.T("Monster", 18, bold: true), 0);
        if (_shell.Capture is not { IsRunning: true } capture)
        {
            Ui.AddTo(col, DamageView.StartCard(_shell, Refresh), 10);
        }
        else
        {
            var reset = Ui.Btn("Zurücksetzen", () => { capture.ResetKills(_shell.GamePid); Update(); }, "Primary", Ui.G.Undo);
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            Ui.AddTo(col, Ui.Card(_kills), 10);
            Ui.AddTo(col, reset, 10);
            Ui.AddTo(col, Ui.T("Gezählt wird, wenn dein Charakter den letzten Treffer landet. Kills von Pets, Partnern und Mitspielern zählen nicht.", 12, "Muted", wrap: true), 10);
            Update();
        }
        Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void Update()
    {
        if (_shell.Capture is { IsRunning: true } capture) FillKills(_kills, capture.Kills(_shell.GamePid));
    }

    public static void FillKills(StackPanel panel, KillStats stats)
    {
        panel.Children.Clear();
        if (stats.Total == 0)
        {
            panel.Children.Add(Ui.T("Noch nichts getötet.", 13, "Muted"));
            return;
        }
        Ui.AddTo(panel, Ui.Cols("*,Auto", Ui.T("Gesamt", 14, bold: true), Ui.T(DamageCard.Number(stats.Total), 18, bold: true)), 0);
        foreach (var monster in stats.Monsters)
            Ui.AddTo(panel, Ui.Cols("*,Auto", Ui.T(ItemCatalog.MonsterName(monster.VNum), 14, trim: true), Ui.T(DamageCard.Number(monster.Count) + "×", 14, bold: true)), 4);
    }
}
