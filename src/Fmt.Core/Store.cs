using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fmt.Core;

// JSON file with atomic, flushed writes and daily backups, so a BSOD cannot eat the data.
public sealed class Store
{
    private const int UndoLimit = 30;
    private const int BackupsKept = 30;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Stack<string> _undo = new();

    public string FilePath { get; }
    public string BackupDirectory { get; }
    public AppData Data { get; private set; } = new();
    public string? LoadWarning { get; private set; }
    public bool CanUndo => _undo.Count > 0;

    public Store(string filePath)
    {
        FilePath = filePath;
        BackupDirectory = Path.Combine(Path.GetDirectoryName(filePath)!, "backups");
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Frostvein", "FMT", "data.json");

    public void Load()
    {
        Directory.CreateDirectory(BackupDirectory);
        if (!File.Exists(FilePath))
        {
            Data = Defaults.Create(DateOnly.FromDateTime(DateTime.Now));
            Save();
            return;
        }
        try
        {
            Data = Read(FilePath);
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidDataException)
        {
            // Keep the damaged file untouched, then fall back to the newest good copy.
            var keep = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(FilePath, keep, true);
            Data = RestoreNewest() ?? Defaults.Create(DateOnly.FromDateTime(DateTime.Now));
            LoadWarning = $"Datendatei war beschädigt und wurde aus dem Backup geladen. Kopie: {Path.GetFileName(keep)}";
        }
    }

    private static AppData Read(string path) =>
        JsonSerializer.Deserialize<AppData>(File.ReadAllText(path, Encoding.UTF8), Json)
        ?? throw new InvalidDataException("Leere Datendatei");

    private AppData? RestoreNewest()
    {
        var candidates = new[] { FilePath + ".bak" }
            .Concat(Directory.GetFiles(BackupDirectory, "data-*.json").OrderDescending());
        foreach (var path in candidates.Where(File.Exists))
        {
            try { return Read(path); }
            catch (Exception e) when (e is JsonException or IOException or InvalidDataException) { }
        }
        return null;
    }

    public void Save()
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Data, Json));
        var tmp = FilePath + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes);
            fs.Flush(true);
        }
        if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak");
        else File.Move(tmp, FilePath);
        DailyBackup();
    }

    private void DailyBackup()
    {
        var target = Path.Combine(BackupDirectory, $"data-{DateTime.Now:yyyy-MM-dd}.json");
        if (File.Exists(target)) return;
        File.Copy(FilePath, target);
        foreach (var old in Directory.GetFiles(BackupDirectory, "data-*.json").OrderDescending().Skip(BackupsKept))
            File.Delete(old);
    }

    public void PushUndo()
    {
        _undo.Push(JsonSerializer.Serialize(Data, Json));
        if (_undo.Count <= UndoLimit) return;
        var keep = _undo.Take(UndoLimit).Reverse().ToList();
        _undo.Clear();
        keep.ForEach(_undo.Push);
    }

    public bool Undo()
    {
        if (!_undo.TryPop(out var snapshot)) return false;
        Data = JsonSerializer.Deserialize<AppData>(snapshot, Json)!;
        Save();
        return true;
    }
}

public static class CsvExport
{
    public static string Build(AppData data, Category category)
    {
        var sb = new StringBuilder();
        var fields = category.Fields;
        var head = new List<string> { "Datum", "Charakter" };
        head.AddRange(fields.Select(f => f.Name));
        sb.AppendLine(string.Join(';', head.Select(Quote)));
        foreach (var e in data.Entries.Where(e => e.CategoryId == category.Id).OrderBy(e => e.Date).ThenBy(e => e.CreatedUtc))
        {
            var row = new List<string> { e.Date.ToString("dd.MM.yyyy"), e.Character };
            row.AddRange(fields.Select(f => Cell(category, e, f)));
            sb.AppendLine(string.Join(';', row.Select(Quote)));
        }
        return sb.ToString();
    }

    // Exact gold values, so spreadsheets can calculate with them.
    private static string Cell(Category c, Entry e, FieldDef f)
    {
        var goldLike = f.Type == FieldType.Gold || (f.Type == FieldType.Formula && f.FormulaIsGold);
        if (!goldLike) return Calc.Display(c, e, f);
        var v = Calc.Numeric(c, e, f);
        return v is null ? "" : decimal.Round(v.Value, 0).ToString("0");
    }

    private static string Quote(string s) =>
        s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
