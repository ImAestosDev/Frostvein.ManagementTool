using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Fmt.App;

// Small always-on badge inside the game window; click opens the panel, drag moves it.
public sealed class PillWindow : Window
{
    private readonly AppState _state;
    private readonly GameWindowTracker _tracker;
    private readonly Action _toggle;
    private readonly TextBlock _dailies = Ui.T("", 15);
    private readonly TextBlock _gold = Ui.T("", 15, "Muted", bold: true);
    private nint _hwnd;
    private bool _dragging;

    public PillWindow(AppState state, GameWindowTracker tracker, Action toggle)
    {
        _state = state;
        _tracker = tracker;
        _toggle = toggle;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Title = "FMT Pill";

        var logo = new Image { Source = Ui.Logo, Width = 20, Height = 20 };
        var content = Ui.Row(8, logo, Ui.T("FMT", 15, bold: true), _dailies, _gold);
        var root = new Border { Child = content, Padding = new Thickness(12, 6, 14, 6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        root.SetResourceReference(Border.BackgroundProperty, "Bg");
        root.SetResourceReference(Border.BorderBrushProperty, "Line");
        root.ToolTip = "Klick: FMT öffnen · Ziehen: verschieben";
        root.MouseLeftButtonDown += OnPress;
        Content = root;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        };
        SizeChanged += (_, _) => Place();
    }


    public void Refresh()
    {
        var (done, total) = Stats.Progress(_state.Data, _state.Character, _state.Today, Recurrence.Daily);
        _dailies.Text = total > 0 ? $"{done}/{total}" : "";
        var net = Stats.Net(_state.Data, _state.Today, _state.Today);
        _gold.Text = Gold.FormatSigned(net);
        _gold.SetResourceReference(TextBlock.ForegroundProperty, net > 0 ? "Positive" : net < 0 ? "Negative" : "Muted");

        var wanted = _state.S.ShowPill && _tracker.ShowOverlay;
        if (wanted && !IsVisible) Show();
        else if (!wanted && IsVisible) Hide();
        Place();
    }

    private void Place()
    {
        if (_hwnd == 0 || _dragging || !_tracker.IsVisible) return;
        var game = _tracker.Rect;
        Native.GetWindowRect(_hwnd, out var own);
        var x = game.Left + (int)(Math.Clamp(_state.S.PillX, 0, 1) * game.Width) - own.Width / 2;
        var y = game.Top + (int)(Math.Clamp(_state.S.PillY, 0, 1) * game.Height);
        x = Math.Clamp(x, game.Left, Math.Max(game.Left, game.Right - own.Width));
        y = Math.Clamp(y, game.Top, Math.Max(game.Top, game.Bottom - own.Height));
        Native.SetWindowPos(_hwnd, 0, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        Native.GetWindowRect(_hwnd, out var before);
        _dragging = true;
        DragMove();
        _dragging = false;
        Native.GetWindowRect(_hwnd, out var after);

        if (Math.Abs(after.Left - before.Left) + Math.Abs(after.Top - before.Top) < 4)
        {
            _toggle();
            return;
        }
        var game = _tracker.Rect;
        if (game.Width <= 0) return;
        _state.S.PillX = (after.Left + after.Width / 2.0 - game.Left) / game.Width;
        _state.S.PillY = (double)(after.Top - game.Top) / game.Height;
        _state.Touch();
        Place();
    }
}
