using System.Text.Json.Serialization;

namespace Fmt.Core;

public enum FieldType { Text, LongText, Number, Gold, Minutes, Bool, Select, Formula, Euro }
public enum Recurrence { Daily, Weekly }
public enum DockMode { InsideRight, InsideLeft, OutsideRight, OutsideLeft, Free }

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N")[..10];
}

public sealed class FieldDef
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "Feld";
    public FieldType Type { get; set; } = FieldType.Text;
    public List<string> Options { get; set; } = [];
    public string Formula { get; set; } = "";
    public bool FormulaIsGold { get; set; } = true;
    public bool ShowInList { get; set; } = true;
    public bool Required { get; set; }
    public string DefaultValue { get; set; } = "";

    // New entries start with the latest entry's value of this field.
    public string? PrefillFrom { get; set; }
}

public sealed class Category
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "Neue Kategorie";
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "#6FB7E9";
    public bool Visible { get; set; } = true;
    public string? GoldFieldId { get; set; }

    // Number field added up in the page summary, e.g. NosTaler.
    public string? SumFieldId { get; set; }

    // Off for ledgers that would double count activity gold.
    public bool CountsInTotal { get; set; } = true;
    public List<FieldDef> Fields { get; set; } = [];
}

public sealed class Entry
{
    public string Id { get; set; } = Ids.New();
    public string CategoryId { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Character { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DailyTask
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string Note { get; set; } = "";
    public Recurrence Recurrence { get; set; } = Recurrence.Daily;
    public bool PerCharacter { get; set; } = true;
    public bool Active { get; set; } = true;
    public DateOnly CreatedDay { get; set; }
}

public sealed class TaskCheck
{
    public string TaskId { get; set; } = "";
    public string Period { get; set; } = "";
    public string Character { get; set; } = "";
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ThemeSettings
{
    public string Preset { get; set; } = "Frostvein";
    public string Background { get; set; } = "#0E1620";
    public string Surface { get; set; } = "#17222F";
    public string Surface2 { get; set; } = "#223040";
    public string Border { get; set; } = "#2E4054";
    public string Text { get; set; } = "#E6EEF5";
    public string Muted { get; set; } = "#8EA3B5";
    public string Accent { get; set; } = "#6FB7E9";
    public string Positive { get; set; } = "#5CC28F";
    public string Negative { get; set; } = "#E5787C";
    public double Opacity { get; set; } = 0.96;
    public double Scale { get; set; } = 1.0;
    public double Radius { get; set; } = 8;
    public bool Compact { get; set; }
}

public sealed class Settings
{
    // Below the game clock in the top right corner.
    public const double DefaultPillX = 0.9072, DefaultPillY = 0.0365;

    public DockMode Dock { get; set; } = DockMode.InsideRight;
    public double PanelWidth { get; set; } = 600;
    public double PanelHeightPercent { get; set; } = 82;
    public double FreeX { get; set; } = 0.6;
    public double FreeY { get; set; } = 0.1;
    public double FreeWidth { get; set; } = 600;
    public double FreeHeight { get; set; } = 760;
    public bool ShowPill { get; set; } = true;
    public bool AutoUpdate { get; set; } = true;
    public bool CaptureEnabled { get; set; }
    public bool ShowDamageOverlay { get; set; } = true;
    public DamageMode DamageMode { get; set; } = DamageMode.Single;
    public Dictionary<long, string> MyCharacters { get; set; } = [];
    public List<string> PacketLogHidden { get; set; } = ["su", "mv", "stat", "bf", "pst", "eff"];
    public bool PacketLogItemsOnly { get; set; }
    public double DamageOverlayX { get; set; } = 0.02;
    public double DamageOverlayY { get; set; } = 0.12;
    public LandOfLifeSettings LandOfLife { get; set; } = new();
    public double PillX { get; set; } = DefaultPillX;
    public double PillY { get; set; } = DefaultPillY;
    public bool SidebarIconsOnly { get; set; }
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+F";
    public string QuickHotkey { get; set; } = "Ctrl+Alt+N";
    public int DayResetHour { get; set; }
    public string ActiveCharacter { get; set; } = "Main";
    public string LastPage { get; set; } = "dashboard";
    public string? LastCategoryId { get; set; }
    public List<string> DashboardWidgets { get; set; } = ["stats", "dailies", "chart", "open", "breakdown", "recent"];
    public ThemeSettings Theme { get; set; } = new();
}

public sealed class AppData
{
    public int Version { get; set; } = Store.CurrentVersion;
    public Settings Settings { get; set; } = new();
    public List<string> Characters { get; set; } = ["Main"];
    public List<Category> Categories { get; set; } = [];
    public List<Entry> Entries { get; set; } = [];
    public List<DailyTask> Tasks { get; set; } = [];
    public List<TaskCheck> Checks { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<Category> VisibleCategories => Categories.Where(c => c.Visible);

    public Category? FindCategory(string? id) => Categories.FirstOrDefault(c => c.Id == id);
}
