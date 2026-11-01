using System.Windows.Interop;

namespace Fmt.App;

public sealed class HotkeyManager : IDisposable
{
    private const uint ModAlt = 1, ModCtrl = 2, ModShift = 4, ModWin = 8, ModNoRepeat = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("FMT Hotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook((nint _, int msg, nint wParam, nint _, ref bool handled) =>
        {
            if (msg == Native.WM_HOTKEY && _actions.TryGetValue((int)wParam, out var action))
            {
                handled = true;
                action();
            }
            return 0;
        });
    }

    // Returns false when the combination is invalid or taken by another program.
    public bool Set(int id, string? combo, Action action)
    {
        Native.UnregisterHotKey(_source.Handle, id);
        _actions.Remove(id);
        if (string.IsNullOrWhiteSpace(combo)) return true;
        if (!TryParse(combo, out var mods, out var key)) return false;
        if (!Native.RegisterHotKey(_source.Handle, id, mods | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(key))) return false;
        _actions[id] = action;
        return true;
    }

    private static bool TryParse(string combo, out uint mods, out Key key)
    {
        mods = 0;
        key = Key.None;
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= ModCtrl; break;
                case "alt": mods |= ModAlt; break;
                case "shift": mods |= ModShift; break;
                case "win": mods |= ModWin; break;
                default:
                    if (!Enum.TryParse(part, true, out key)) return false;
                    break;
            }
        }
        return key != Key.None;
    }

    // Builds "Ctrl+Alt+F" from a key press, or null while only modifiers are held.
    public static string? Describe(Key key, ModifierKeys mods)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return null;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _source.Dispose();
    }
}
