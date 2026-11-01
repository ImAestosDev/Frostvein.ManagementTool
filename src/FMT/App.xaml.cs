using System.IO;
using System.Threading;

namespace Fmt.App;

public partial class App : Application
{
    private const string MutexName = @"Local\Frostvein.FMT";
    private const string SignalName = @"Local\Frostvein.FMT.Toggle";
    private const int HotkeyToggle = 1, HotkeyQuick = 2;
    public const string WaitForArgument = "--wait-for";
    public const string OpenArgument = "--open";

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private RegisteredWaitHandle? _signalWait;
    private GameWindowTracker? _tracker;
    private HotkeyManager? _hotkeys;
    private Tray? _tray;
    private PanelWindow? _panel;
    private PillWindow? _pill;
    private AppState? _state;
    private Updater? _updater;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        var dataIndex = Array.IndexOf(args, "--data");
        var dataPath = dataIndex >= 0 && dataIndex + 1 < args.Length ? Path.GetFullPath(args[dataIndex + 1]) : Store.DefaultPath;

#if DEBUG
        var shotsIndex = Array.IndexOf(args, "--shots");
        if (shotsIndex >= 0)
        {
            Shots.Run(dataPath, args[shotsIndex + 1]);
            return;
        }
#endif

        // After an update the old process is still shutting down and holds the instance lock.
        var waitIndex = Array.IndexOf(args, WaitForArgument);
        if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out var oldPid)) WaitForExit(oldPid);

        // A second start just toggles the running instance.
        _mutex = new Mutex(true, MutexName, out var isFirst);
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        if (!isFirst)
        {
            _signal.Set();
            Shutdown();
            return;
        }

        var store = new Store(dataPath);
        store.Load();
        _state = new AppState(store);
        ThemeManager.Apply(_state.S.Theme);

        _tracker = new GameWindowTracker();
        _panel = new PanelWindow(_state, _tracker);
        _updater = new Updater(_state, () => !_panel.IsOpen, Quit);
        _panel.Updater = _updater;
        _panel.Quit = Quit;
        _pill = new PillWindow(_state, _tracker, () => _panel.Toggle());
        _tray = new Tray(() => _panel.Toggle(), () => _panel.ShowPage("settings"), Quit);
        _hotkeys = new HotkeyManager();

        _state.SettingsChanged += () =>
        {
            ThemeManager.Apply(_state.S.Theme);
            _panel.ApplySettings();
            _pill.Refresh();
        };
        _state.DataChanged += () => _pill.Refresh();
        _panel.HotkeysChanged += RegisterHotkeys;

        _tracker.TargetChanged += OnGameMoved;
        _tracker.Moved += OnGameMoved;
        _tracker.VisibilityChanged += OnGameMoved;
        _tracker.Start();

        RegisterHotkeys();
        _pill.Refresh();
        _panel.ApplySettings();

        _signalWait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) => Dispatcher.BeginInvoke(() => _panel.Toggle()), null, -1, false);

        if (store.LoadWarning != null) _panel.ShowAndNotify(store.LoadWarning);
        if (args.Contains(OpenArgument) || (_tracker.Target == 0 && waitIndex < 0)) _panel.Open();
#if !DEBUG
        _updater.Start();
#endif
    }


    private static void WaitForExit(int pid)
    {
        try { System.Diagnostics.Process.GetProcessById(pid).WaitForExit(5000); }
        catch (ArgumentException) { }
    }

    private void OnGameMoved()
    {
        _panel!.SyncWithGame();
        _panel.Place();
        _pill!.Refresh();
    }

    private void RegisterHotkeys()
    {
        var s = _state!.S;
        var okToggle = _hotkeys!.Set(HotkeyToggle, s.ToggleHotkey, () => _panel!.Toggle());
        var okQuick = _hotkeys.Set(HotkeyQuick, s.QuickHotkey, () => _panel!.QuickEntry());
        if (!okToggle) _panel!.ShowAndNotify($"Hotkey {s.ToggleHotkey} ist belegt oder ungültig.");
        if (!okQuick) _panel!.ShowAndNotify($"Hotkey {s.QuickHotkey} ist belegt oder ungültig.");
    }

    private void Quit()
    {
        _state?.Flush();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _state?.Flush();
        _signalWait?.Unregister(null);
        _updater?.Dispose();
        _hotkeys?.Dispose();
        _tracker?.Dispose();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

public sealed class Tray : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;

    public Tray(Action toggle, Action settings, Action quit)
    {
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/fmt.ico"));
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(res.Stream),
            Text = "FMT – Frostvein Management Tool",
            Visible = true,
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(),
        };
        _icon.ContextMenuStrip.Items.Add("Öffnen / Schließen", null, (_, _) => toggle());
        _icon.ContextMenuStrip.Items.Add("Anpassen", null, (_, _) => settings());
        _icon.ContextMenuStrip.Items.Add("Beenden", null, (_, _) => quit());
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) toggle();
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
