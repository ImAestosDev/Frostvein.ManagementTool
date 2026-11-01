using System.Windows.Threading;

namespace Fmt.App;

// Follows the NosTale window from outside; with several clients it follows the active one.
// The game runs elevated, so Windows refuses cross-process ownership: the overlay is topmost
// and simply hides while another program is in front.
public sealed class GameWindowTracker : IDisposable
{
    // MainWindowHandle is the hidden 0x0 TApplication shell, the game itself is this class.
    private const string GameClass = "TNosTaleMainF";

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Native.WinEventProc _callback;
    private readonly nint _foregroundHook;
    private nint _locationHook;
    private RECT _lastRect;
    private bool _lastVisible;
    private bool _inFront = true;

    public nint Target { get; private set; }
    public RECT Rect => _lastRect;
    public bool IsVisible => Target != 0 && _lastVisible;

    // Game shown and either it or FMT itself has the focus.
    public bool ShowOverlay => IsVisible && _inFront;

    // Target window changed, or was lost.
    public event Action? TargetChanged;
    public event Action? Moved;
    public event Action? VisibilityChanged;

    public GameWindowTracker()
    {
        _callback = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (idObject == 0) Update();
        };
        _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, 0, _callback, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        _poll.Tick += (_, _) => Update();
    }

    public void Start()
    {
        _poll.Start();
        Update();
    }

    private static bool IsGame(nint hwnd) =>
        hwnd != 0 && Native.IsWindowVisible(hwnd) && Native.ClassName(hwnd) == GameClass;

    private void Update()
    {
        var foreground = Native.GetForegroundWindow();
        UpdateInFront(foreground);
        if (foreground != Target && IsGame(foreground)) Attach(foreground);
        else if (Target != 0 && !Native.IsWindow(Target)) Attach(0);

        if (Target == 0)
        {
            var found = Native.TopLevelWindows().FirstOrDefault(IsGame);
            if (found == 0) return;
            Attach(found);
        }

        var visible = !Native.IsIconic(Target) && Native.IsWindowVisible(Target);
        if (visible && Native.GetWindowRect(Target, out var rect) && !rect.Equals(_lastRect) && rect.Width > 0)
        {
            _lastRect = rect;
            Moved?.Invoke();
        }
        if (visible != _lastVisible)
        {
            _lastVisible = visible;
            VisibilityChanged?.Invoke();
        }
    }

    private void UpdateInFront(nint foreground)
    {
        // Foreground is briefly 0 while windows switch.
        if (foreground == 0) return;
        Native.GetWindowThreadProcessId(foreground, out var pid);
        var inFront = foreground == Target || IsGame(foreground) || pid == Environment.ProcessId;
        if (inFront == _inFront) return;
        _inFront = inFront;
        VisibilityChanged?.Invoke();
    }

    private void Attach(nint hwnd)
    {
        if (_locationHook != 0) Native.UnhookWinEvent(_locationHook);
        _locationHook = 0;
        Target = hwnd;
        _lastRect = default;
        _lastVisible = false;
        if (hwnd != 0)
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            _locationHook = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE, 0, _callback, pid, 0, Native.WINEVENT_OUTOFCONTEXT);
        }
        TargetChanged?.Invoke();
    }

    public void Dispose()
    {
        _poll.Stop();
        if (_locationHook != 0) Native.UnhookWinEvent(_locationHook);
        if (_foregroundHook != 0) Native.UnhookWinEvent(_foregroundHook);
    }
}
