using System.Windows.Threading;

namespace Fmt.App;

// Live list of incoming packets with a text filter.
public sealed class PacketLogView : PageView
{
    private readonly PanelWindow _shell;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly ListBox _list = new() { Margin = new Thickness(14, 8, 12, 12) };
    private readonly TextBox _filter;
    private readonly TextBlock _count = Ui.T("", 12, "Muted");
    private long _shownVersion = -1;
    private bool _paused;

    public PacketLogView(PanelWindow shell)
    {
        _shell = shell;
        _filter = Ui.Input("", _ => Fill(true), "Filtern, z. B. su oder c_info");
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        _list.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _list.FontSize = 12;
        _list.SetResourceReference(ForegroundProperty, "Ink");
        _timer.Tick += (_, _) => Fill(false);
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public override void Refresh()
    {
        if (_shell.Capture is not { IsRunning: true })
        {
            var hint = Ui.T("Das Mitlesen läuft noch nicht. Starte es im Tab Damage.", 13, "Muted", wrap: true);
            hint.Margin = new Thickness(14, 12, 12, 0);
            Content = Ui.Col(0, hint);
            return;
        }

        var pause = Ui.Check("Pause", _paused, v => { _paused = v; Fill(true); });
        pause.Margin = new Thickness(8, 0, 0, 0);
        var copy = Ui.Btn("Kopieren", CopyShown, "Ghost", Ui.G.Copy);
        var clear = Ui.Btn("Leeren", () => { _shell.Capture?.ClearLog(); Fill(true); }, "Ghost", Ui.G.Delete);
        var title = Ui.Col(2, Ui.T("Packetlogger", 18, bold: true), _count);
        (_count.Parent as Panel)?.Children.Remove(_count);
        title.Children.Add(_count);
        (_filter.Parent as Panel)?.Children.Remove(_filter);

        var head = Ui.Col(8, Ui.Cols("*,Auto,Auto", title, copy, clear), Ui.Cols("*,Auto", _filter, pause));
        head.Margin = new Thickness(14, 12, 12, 0);
        DockPanel.SetDock(head, Dock.Top);
        var dock = new DockPanel();
        dock.Children.Add(head);
        (_list.Parent as Panel)?.Children.Remove(_list);
        dock.Children.Add(_list);
        Content = dock;
        Fill(true);
    }

    private List<string> Lines()
    {
        var q = _filter.Text.Trim();
        return (_shell.Capture?.Log() ?? [])
            .Where(p => q.Length == 0 || p.Text.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Time:HH:mm:ss}  {p.Text}")
            .ToList();
    }

    private void Fill(bool force)
    {
        if (_shell.Capture is not { } capture || (_paused && !force)) return;
        if (!force && capture.LogVersion == _shownVersion) return;
        _shownVersion = capture.LogVersion;
        var lines = Lines();
        _list.ItemsSource = lines;
        _count.Text = $"{lines.Count} eingehende Pakete";
        if (!_paused && lines.Count > 0) _list.ScrollIntoView(lines[^1]);
    }

    private void CopyShown()
    {
        try { Clipboard.SetText(string.Join(Environment.NewLine, Lines())); }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }
}
