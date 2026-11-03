using System.Globalization;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Fmt.App;

// Compact player list, shared by the overlay and dev renders.
public sealed class DamageCard : Border
{
    private const string RowColumns = "30,160,116,96";
    private const double RowHeight = 24;
    private static readonly CultureInfo German = new("de-DE");
    private static readonly (string Label, DamageMode Mode)[] Modes =
        [("Raid", DamageMode.Raid), ("Single", DamageMode.Single), ("Group", DamageMode.Group)];

    private readonly StackPanel _rows = new();
    private readonly StackPanel _settings = new() { Width = 76, Margin = new Thickness(0, 0, 12, 0) };
    private readonly Action _reset;
    private readonly Action<DamageMode> _setMode;
    private List<PlayerDamage>? _shown;
    private DamageMode? _shownMode;

    public DamageCard(Action reset, Action<DamageMode> setMode)
    {
        _reset = reset;
        _setMode = setMode;
        Padding = new Thickness(8);
        BorderThickness = new Thickness(1);
        Cursor = Cursors.SizeAll;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(CornerRadiusProperty, "CornerSmall");
        Child = Ui.Cols("Auto,*", _settings, _rows);
        Show(DamageStats.Empty, DamageMode.Single);
    }

    public void Show(DamageStats stats, DamageMode mode)
    {
        var table = stats.Table(mode);
        if (mode != _shownMode) BuildSettings(mode);
        if (_shown != null && mode == _shownMode && _shown.SequenceEqual(table)) return;
        _shown = table;
        _shownMode = mode;

        // Every slot of the mode is always there, so the table size follows the mode.
        _rows.Children.Clear();
        _rows.Children.Add(Row("", "Spieler", "Schaden", "Best Hit", "Muted", 12, false));
        for (var i = 0; i < DamageStats.Slots(mode); i++)
        {
            _rows.Children.Add(i < table.Count
                ? Row($"{i + 1}.", table[i].Name, Number(table[i].Total), Number(table[i].Best), table[i].IsOwn ? "Accent" : "Ink", 15, table[i].IsOwn)
                : Row($"{i + 1}.", "–", "–", "–", "Muted", 15, false));
        }
    }

    private void BuildSettings(DamageMode active)
    {
        _settings.Children.Clear();
        foreach (var (label, mode) in Modes)
            Ui.AddTo(_settings, Stretch(Ui.Btn(label, () => _setMode(mode), mode == active ? "Primary" : "Ghost")), 4);
        Ui.AddTo(_settings, Stretch(Ui.Btn("Reset", _reset, "Ghost", Ui.G.Undo, "Zähler zurücksetzen")), 4);
    }

    private static Button Stretch(Button button)
    {
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        return button;
    }

    private static Grid Row(string place, string name, string total, string best, string brush, double size, bool bold)
    {
        var right1 = Ui.T(total, size, brush, bold);
        var right2 = Ui.T(best, size, brush, bold);
        right1.TextAlignment = right2.TextAlignment = TextAlignment.Right;
        var row = Ui.Cols(RowColumns, Ui.T(place, size, "Muted"), Ui.T(name, size, brush, bold, trim: true), right1, right2);
        row.Height = RowHeight;
        return row;
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

        _card = new DamageCard(() => _capture.ResetDamage(GamePid()), mode =>
        {
            _state.S.DamageMode = mode;
            _state.Touch();
            _card!.Show(_capture.Stats(GamePid()), mode);
        });
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
            _card.Show(_capture.Stats(GamePid()), _state.S.DamageMode);
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
