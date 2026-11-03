using System.Globalization;

namespace Fmt.App;

// Every saved session: a list first, one click shows all its counters.
public sealed class SessionHistoryView : PageView
{
    private static readonly CultureInfo German = new("de-DE");

    private readonly PanelWindow _shell;
    private SavedSession? _selected;

    public SessionHistoryView(PanelWindow shell) => _shell = shell;

    public override void Refresh()
    {
        var col = new StackPanel { Margin = new Thickness(14, 12, 12, 16) };
        if (_selected is { } session) BuildDetail(col, session);
        else BuildList(col);
        Content = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public override bool HandleEscape()
    {
        if (_selected == null) return false;
        _selected = null;
        Refresh();
        return true;
    }

    private void BuildList(StackPanel col)
    {
        Ui.AddTo(col, Ui.T("Verlauf", 18, bold: true), 0);
        var sessions = _shell.Capture?.SavedSessions() ?? [];
        if (sessions.Count == 0)
        {
            Ui.AddTo(col, Ui.Card(Ui.T("Noch keine Session gespeichert.", 13, "Muted")), 10);
            return;
        }
        foreach (var session in sessions) Ui.AddTo(col, Row(session), 8);
    }

    private Border Row(SavedSession s)
    {
        var card = Ui.Card(Ui.Col(3,
            Ui.Cols("*,Auto", Ui.T(Title(s), 14, bold: true, trim: true), Ui.T(ExpView.Clock(s.Duration), 14, bold: true)),
            Ui.T($"{DamageCard.Number(s.OwnDamage)} Schaden  ·  {DamageCard.Number(s.TotalKills)} Kills  ·  {DamageCard.Number(s.Gold)} Gold", 12, "Muted", trim: true)));
        card.Cursor = Cursors.Hand;
        card.MouseLeftButtonUp += (_, _) => Open(s);
        return card;
    }

    public void Open(SavedSession session)
    {
        _selected = session;
        Refresh();
    }

    private static string Title(SavedSession s)
    {
        var date = s.StartedAt.ToString("dd.MM.yyyy HH:mm", German);
        return s.Character.Length > 0 ? $"{date}  ·  {s.Character}" : date;
    }

    private void BuildDetail(StackPanel col, SavedSession s)
    {
        var back = Ui.Btn("Zurück", () => HandleEscape(), "Ghost", Ui.G.Back);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        Ui.AddTo(col, back, 0);
        Ui.AddTo(col, Ui.T(Title(s), 18, bold: true), 8);
        var state = s.Finished ? (s.TimeIsUp ? "Automatisch beendet" : "Beendet") : "Abgebrochen, FMT wurde nicht sauber beendet";
        Ui.AddTo(col, Ui.T($"Dauer {ExpView.Clock(s.Duration)}  ·  {state}", 12, "Muted"), 2);

        var players = new StackPanel();
        SessionView.FillTable(players, new DamageStats(null, s.Players).Table(DamageMode.Raid));
        Ui.AddTo(col, Ui.T("Damage", 14, bold: true), 14);
        Ui.AddTo(col, Ui.Card(players, 6), 8);

        var exp = new StackPanel();
        SessionView.FillExpRows(exp, s.Exp, false);
        Ui.AddTo(col, Ui.T("EXP", 14, bold: true), 16);
        Ui.AddTo(col, Ui.Card(exp), 8);

        var drops = new DropCard(null);
        drops.Show(new DropStats(s.Items, s.Gold));
        Ui.AddTo(col, Ui.T("Drops", 14, bold: true), 16);
        Ui.AddTo(col, drops, 8);

        var kills = new StackPanel();
        MonstersView.FillKills(kills, new KillStats(s.Kills, s.TotalKills));
        Ui.AddTo(col, Ui.T("Monster", 14, bold: true), 16);
        Ui.AddTo(col, Ui.Card(kills), 8);

        var used = new StackPanel();
        ItemsView.FillUsed(used, s.Used);
        Ui.AddTo(col, Ui.T("Benutzte Items", 14, bold: true), 16);
        Ui.AddTo(col, Ui.Card(used), 8);
    }
}
