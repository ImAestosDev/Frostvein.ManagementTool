using System.IO;
using System.Windows.Threading;

namespace Fmt.App;

// Single source of truth; views mutate through here so saving and refresh stay in one place.
public sealed class AppState
{
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public Store Store { get; }
    public AppData Data => Store.Data;
    public Settings S => Data.Settings;
    public DateOnly Today => Stats.GameDay(DateTime.Now, S.DayResetHour);

    public string Character
    {
        get
        {
            if (!Data.Characters.Contains(S.ActiveCharacter)) S.ActiveCharacter = Data.Characters.FirstOrDefault() ?? "";
            return S.ActiveCharacter;
        }
    }

    public event Action? DataChanged;
    public event Action? StructureChanged;
    public event Action? SettingsChanged;
    public event Action<string, bool>? Toast;

    public AppState(Store store)
    {
        Store = store;
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
    }

    public void Mutate(Action change, string? undoMessage = null)
    {
        if (undoMessage != null) Store.PushUndo();
        change();
        Save();
        DataChanged?.Invoke();
        StructureChanged?.Invoke();
        if (undoMessage != null) Toast?.Invoke(undoMessage, true);
    }

    // For live edits such as typing or sliders: no view rebuild, delayed write.
    public void Touch()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void TouchStructure()
    {
        Touch();
        StructureChanged?.Invoke();
    }

    public void TouchSettings()
    {
        Touch();
        SettingsChanged?.Invoke();
    }

    public void Undo()
    {
        if (!Store.Undo()) return;
        DataChanged?.Invoke();
        StructureChanged?.Invoke();
        SettingsChanged?.Invoke();
        Toast?.Invoke("Rückgängig gemacht", false);
    }

    public void Notify(string message) => Toast?.Invoke(message, false);

    public void Flush()
    {
        if (!_saveTimer.IsEnabled) return;
        _saveTimer.Stop();
        Save();
    }

    private void Save()
    {
        try
        {
            Store.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Data stays in memory, the next save tries again.
            Toast?.Invoke($"Speichern fehlgeschlagen: {e.Message}", false);
        }
    }
}
