using System.Globalization;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Fmt.App;

// Compact player list, shared by the overlay and dev renders.
public sealed class DamageCard : Border
{
    private const int MaxRows = 5;
    private const string RowColumns = "100,84,70";
    private static readonly CultureInfo German = new("de-DE");

    private readonly StackPanel _rows = new();
    private DamageStats? _shown;

    public DamageCard(Action reset)
    {
        Padding = new Thickness(10, 5, 4, 6);
        BorderThickness = new Thickness(1);
        Cursor = Cursors.SizeAll;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(CornerRadiusProperty, "CornerSmall");
        var button = Ui.IconBtn(Ui.G.Undo, "Zähler zurücksetzen", reset, 12);
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new Thickness(6, 0, 0, 0);
        Child = Ui.Cols("*,Auto", _rows, button);
        Show(DamageStats.Empty);
    }

    public void Show(DamageStats stats)
    {
        if (_shown != null && Same(_shown, stats)) return;
        _shown = stats;
        _rows.Children.Clear();
        _rows.Children.Add(Row("Spieler", "Schaden", "Best Hit", "Muted", 10, false));
        foreach (var p in stats.Players.Take(MaxRows))
            _rows.Children.Add(Row(p.Name, Number(p.Total), Number(p.Best), p.IsOwn ? "Accent" : "Ink", 12, p.IsOwn));
        if (stats.Players.Count == 0) _rows.Children.Add(Ui.T("Noch kein Schaden", 12, "Muted"));
    }

    private static bool Same(DamageStats a, DamageStats b) => a.OwnId == b.OwnId && a.Players.SequenceEqual(b.Players);

    private static Grid Row(string name, string total, string best, string brush, double size, bool bold)
    {
        var right1 = Ui.T(total, size, brush, bold);
        var right2 = Ui.T(best, size, brush, bold);
        right1.TextAlignment = right2.TextAlignment = TextAlignment.Right;
        return Ui.Cols(RowColumns, Ui.T(name, size, brush, bold, trim: true), right1, right2);
    }

    public static string Number(long value) => value.ToString("N0", German);
}

// Small always visible counter over the game; never takes the focus.
public sealed class DamageOverlay : Window
{
    private readonly AppState _state;
    private readonly GameWindowTracker _tracker;
    private readonly PacketCapture _capture;
    private readonly DamageCard _card;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private nint _hwnd;
    private bool _dragging;

    public DamageOverlay(AppState state, GameWindowTracker tracker, PacketCapture capture)
    {
        _state = state;
        _tracker = tracker;
        _capture = capture;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Title = "FMT Damage";

        _card = new DamageCard(() => _capture.ResetDamage(GamePid()));
        _card.MouseLeftButtonDown += OnPress;
        Content = _card;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        };
        SizeChanged += (_, _) => Place();
        _timer.Tick += (_, _) =>
        {
            Refresh();
            _card.Show(_capture.Stats(GamePid()));
        };
        _timer.Start();
    }

    private uint GamePid()
    {
        Native.GetWindowThreadProcessId(_tracker.Target, out var pid);
        return pid;
    }

    public void Refresh()
    {
        var wanted = _capture.IsRunning && _state.S.ShowDamageOverlay && _tracker.ShowOverlay;
        if (wanted && !IsVisible) Show();
        else if (!wanted && IsVisible) Hide();
        Place();
    }

    private void Place()
    {
        if (_hwnd == 0 || _dragging || !IsVisible || !_tracker.IsVisible) return;
        var game = _tracker.Rect;
        Native.GetWindowRect(_hwnd, out var own);
        var x = game.Left + (int)(Math.Clamp(_state.S.DamageOverlayX, 0, 1) * game.Width);
        var y = game.Top + (int)(Math.Clamp(_state.S.DamageOverlayY, 0, 1) * game.Height);
        x = Math.Clamp(x, game.Left, Math.Max(game.Left, game.Right - own.Width));
        y = Math.Clamp(y, game.Top, Math.Max(game.Top, game.Bottom - own.Height));
        Native.SetWindowPos(_hwnd, 0, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        DragMove();
        _dragging = false;
        Native.GetWindowRect(_hwnd, out var after);
        var game = _tracker.Rect;
        if (game.Width <= 0 || game.Height <= 0) return;
        _state.S.DamageOverlayX = (double)(after.Left - game.Left) / game.Width;
        _state.S.DamageOverlayY = (double)(after.Top - game.Top) / game.Height;
        _state.Touch();
    }
}
