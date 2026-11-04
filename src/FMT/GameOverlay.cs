using System.Windows.Interop;
using System.Windows.Threading;

namespace Fmt.App;

// Small always visible card over the game; never takes the focus.
public abstract class GameOverlay : Window
{
    private readonly GameWindowTracker _tracker;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private nint _hwnd;
    private bool _dragging;

    protected GameOverlay(AppState state, GameWindowTracker tracker, PacketCapture capture, string title)
    {
        State = state;
        Capture = capture;
        _tracker = tracker;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        SetResourceReference(FontFamilyProperty, "UiFont");
        Title = title;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        };
        SizeChanged += (_, _) => Place();
        _timer.Tick += (_, _) =>
        {
            Refresh();
            ShowCard();
        };
        _timer.Start();
    }

    protected AppState State { get; }
    protected PacketCapture Capture { get; }
    protected abstract bool Enabled { get; }
    protected abstract (double X, double Y) Position { get; set; }
    protected abstract void ShowCard();

    protected void Host(FrameworkElement card)
    {
        card.MouseLeftButtonDown += OnPress;
        Content = card;
    }

    protected uint GamePid()
    {
        Native.GetWindowThreadProcessId(_tracker.Target, out var pid);
        return pid;
    }

    public void Refresh()
    {
        var wanted = Capture.IsRunning && Enabled && _tracker.ShowOverlay;
        if (wanted && !IsVisible) Show();
        else if (!wanted && IsVisible) Hide();
        Place();
    }

    private void Place()
    {
        if (_hwnd == 0 || _dragging || !IsVisible || !_tracker.IsVisible) return;
        var game = _tracker.Rect;
        Native.GetWindowRect(_hwnd, out var own);
        var (x, y) = Target(game, own);
        x = Math.Clamp(x, game.Left, Math.Max(game.Left, game.Right - own.Width));
        y = Math.Clamp(y, game.Top, Math.Max(game.Top, game.Bottom - own.Height));
        Native.SetWindowPos(_hwnd, 0, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    protected virtual bool CanDrag => true;

    protected virtual (int X, int Y) Target(RECT game, RECT own)
    {
        var (px, py) = Position;
        return (game.Left + (int)(Math.Clamp(px, 0, 1) * game.Width), game.Top + (int)(Math.Clamp(py, 0, 1) * game.Height));
    }

    protected virtual void Dropped(RECT game, RECT own) =>
        Position = ((double)(own.Left - game.Left) / game.Width, (double)(own.Top - game.Top) / game.Height);

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (!CanDrag) return;
        _dragging = true;
        DragMove();
        _dragging = false;
        Native.GetWindowRect(_hwnd, out var after);
        var game = _tracker.Rect;
        if (game.Width <= 0 || game.Height <= 0) return;
        Dropped(game, after);
        State.Touch();
    }
}
