using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Fmt.App;

// Small button under the game clock; click opens the panel, drag moves it.
public sealed class PillWindow : Window
{
    // Same width as the game clock it sits under.
    public const double ClockWidthPixels = 149;
    private const double LogoHeight = 16;

    private readonly AppState _state;
    private readonly GameWindowTracker _tracker;
    private readonly Action _toggle;
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

        var root = BuildButton();
        root.ToolTip = "Klick: FMT öffnen · Ziehen: verschieben";
        root.MouseEnter += (_, _) =>
        {
            root.SetResourceReference(Border.BackgroundProperty, "Surface2");
            root.SetResourceReference(Border.BorderBrushProperty, "Accent");
        };
        root.MouseLeave += (_, _) =>
        {
            root.SetResourceReference(Border.BackgroundProperty, "Bg");
            root.SetResourceReference(Border.BorderBrushProperty, "Line");
        };
        root.MouseLeftButtonDown += OnPress;
        Content = root;
        Loaded += (_, _) => root.Width = ClockWidthPixels / VisualTreeHelper.GetDpi(this).DpiScaleX;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        };
        SizeChanged += (_, _) => Place();
    }

    public static Border BuildButton()
    {
        var logo = new Image { Source = Ui.Logo, Height = LogoHeight, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        var label = Ui.T("FMT", 14, bold: true);
        // Capitals sit below the line box center, this centers them visually.
        label.RenderTransform = new TranslateTransform(0, -1);
        var content = Ui.Row(7, logo, label);
        content.HorizontalAlignment = HorizontalAlignment.Center;
        var root = new Border { Child = content, Padding = new Thickness(6, 4, 6, 4), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        root.SetResourceReference(Border.BackgroundProperty, "Bg");
        root.SetResourceReference(Border.BorderBrushProperty, "Line");
        return root;
    }

    public void Refresh()
    {
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
