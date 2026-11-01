using Fmt.Core;

namespace Fmt.Tests;

public class GoldTests
{
    [Theory]
    [InlineData("50kk", 50_000_000)]
    [InlineData("50 KK", 50_000_000)]
    [InlineData("1,5kk", 1_500_000)]
    [InlineData("1.5kk", 1_500_000)]
    [InlineData("1,25kkk", 1_250_000_000)]
    [InlineData("500k", 500_000)]
    [InlineData("1.000.000", 1_000_000)]
    [InlineData("1,000", 1_000)]
    [InlineData("1.250kk", 1_250_000)]
    [InlineData("1.234,5kk", 1_234_500_000)]
    [InlineData("-5kk", -5_000_000)]
    [InlineData("12345", 12_345)]
    [InlineData("2mio", 2_000_000)]
    public void Parses(string input, long expected) => Assert.Equal(expected, Gold.Parse(input));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("kk")]
    [InlineData("5xx")]
    [InlineData("99999999999kkkk")]
    public void Rejects(string input) => Assert.Null(Gold.Parse(input));

    [Theory]
    [InlineData(50_000_000, "50kk")]
    [InlineData(1_500_000, "1,5kk")]
    [InlineData(-2_500, "-2,5k")]
    [InlineData(1_250_000_000, "1,25kkk")]
    [InlineData(999, "999")]
    public void Formats(long input, string expected) => Assert.Equal(expected, Gold.Format(input));
}

public class DurationTests
{
    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("1h 30m", 90)]
    [InlineData("2h", 120)]
    [InlineData("45min", 45)]
    public void Parses(string input, int expected) => Assert.Equal(expected, Duration.Parse(input));

    [Fact]
    public void RejectsGarbage() => Assert.Null(Duration.Parse("bald"));
}

public class FormulaTests
{
    private static decimal? Eval(string f, decimal? a = 10, decimal? b = 4) =>
        Formula.Evaluate(f, n => n.ToLowerInvariant() == "a" ? a : b);

    [Theory]
    [InlineData("{A} - {B}", 6)]
    [InlineData("{A} + {B} * 2", 18)]
    [InlineData("({A} + {B}) * 2", 28)]
    [InlineData("-{A} + 1", -9)]
    [InlineData("{A} / {B} * 60", 150)]
    [InlineData("1,5 * 2", 3)]
    public void Calculates(string f, double expected) => Assert.Equal((decimal)expected, Eval(f));

    [Fact]
    public void EmptyReferenceGivesEmptyResult() => Assert.Null(Eval("{A} - {B}", b: null));

    [Fact]
    public void DivisionByZeroGivesEmptyResult() => Assert.Null(Eval("{A} / {B}", b: 0));

    [Fact]
    public void ValidateFindsProblems()
    {
        string[] names = ["A", "B"];
        Assert.Null(Formula.Validate("{A} - {b}", names));
        Assert.Contains("Unbekanntes Feld", Formula.Validate("{A} - {C}", names));
        Assert.NotNull(Formula.Validate("{A} -", names));
        Assert.NotNull(Formula.Validate("({A}", names));
    }

    [Fact]
    public void SelfReferenceDoesNotHang()
    {
        var f = new FieldDef { Name = "X", Type = FieldType.Formula, Formula = "{X} + 1" };
        var c = new Category { Fields = [f] };
        Assert.Null(Calc.Numeric(c, new Entry(), f));
    }
}

public class CalcTests
{
    private static readonly DateOnly Day = new(2026, 11, 2);

    private static (AppData Data, Category Trading) Setup()
    {
        var d = Defaults.Create(Day);
        return (d, d.Categories.First(c => c.Name == "Trading"));
    }

    private static Entry Trade(Category c, DateOnly day, string? buy, string? sell)
    {
        var e = new Entry { CategoryId = c.Id, Date = day };
        if (buy != null) e.Values[c.Fields.First(f => f.Name == "Einkauf").Id] = Gold.Parse(buy).ToString()!;
        if (sell != null) e.Values[c.Fields.First(f => f.Name == "Verkauf").Id] = Gold.Parse(sell).ToString()!;
        return e;
    }

    [Fact]
    public void OpenTradeDoesNotCountAsLoss()
    {
        var (d, c) = Setup();
        var open = Trade(c, Day, "50kk", null);
        d.Entries.Add(open);
        d.Entries.Add(Trade(c, Day, "50kk", "70kk"));
        Assert.True(Calc.IsOpen(c, open));
        Assert.Equal(20_000_000, Stats.Net(d, Day, Day));
    }

    [Fact]
    public void GoldBookStaysOutOfTotals()
    {
        var (d, _) = Setup();
        var book = d.Categories.First(c => c.Name == "Gold pro Tag");
        var e = new Entry { CategoryId = book.Id, Date = Day };
        e.Values[book.Fields[0].Id] = "100";
        e.Values[book.Fields[1].Id] = "900";
        d.Entries.Add(e);
        Assert.Equal(800, Calc.GoldValue(book, e));
        Assert.Equal(0, Stats.Net(d, Day, Day));
    }

    [Fact]
    public void WeekStartsMonday()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), Stats.WeekStart(new DateOnly(2026, 11, 1)));
        Assert.Equal(new DateOnly(2026, 11, 2), Stats.WeekStart(new DateOnly(2026, 11, 2)));
    }

    [Fact]
    public void ResetHourShiftsGameDay() =>
        Assert.Equal(new DateOnly(2026, 11, 1), Stats.GameDay(new DateTime(2026, 11, 2, 3, 0, 0), 6));

    [Fact]
    public void ChecksArePerCharacterAndStreakWorks()
    {
        var d = new AppData();
        var t = new DailyTask { Name = "x", CreatedDay = Day.AddDays(-5) };
        d.Tasks.Add(t);
        Stats.SetDone(d, t, "A", Day, true);
        Assert.True(Stats.IsDone(d, t, "A", Day));
        Assert.False(Stats.IsDone(d, t, "B", Day));
        Stats.SetDone(d, t, "A", Day.AddDays(-1), true);
        Assert.Equal(2, Stats.Streak(d, "A", Day));
        Stats.SetDone(d, t, "A", Day, false);
        Assert.Equal(1, Stats.Streak(d, "A", Day));
    }

    [Fact]
    public void NewTaskDoesNotBreakOldStreakDays()
    {
        var d = new AppData();
        var old = new DailyTask { Name = "alt", CreatedDay = Day.AddDays(-5) };
        d.Tasks.Add(old);
        d.Tasks.Add(new DailyTask { Name = "neu", CreatedDay = Day });
        Stats.SetDone(d, old, "A", Day.AddDays(-1), true);
        Assert.Equal(1, Stats.Streak(d, "A", Day));
    }

    [Fact]
    public void WeeklyCheckHoldsAllWeek()
    {
        var d = new AppData();
        var t = new DailyTask { Name = "w", Recurrence = Recurrence.Weekly, CreatedDay = Day };
        Stats.SetDone(d, t, "A", Day, true);
        Assert.True(Stats.IsDone(d, t, "A", Day.AddDays(6)));
        Assert.False(Stats.IsDone(d, t, "A", Day.AddDays(7)));
    }
}

public sealed class StoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fmt-test").FullName;
    private string DataFile => Path.Combine(_dir, "data.json");

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void RoundTripsAndKeepsEnums()
    {
        var a = new Store(DataFile);
        a.Load();
        a.Data.Characters.Add("Twink");
        a.Save();
        var b = new Store(DataFile);
        b.Load();
        Assert.Contains("Twink", b.Data.Characters);
        Assert.Contains("\"Formula\"", File.ReadAllText(DataFile));
    }

    [Fact]
    public void CorruptFileIsKeptAndRestoredFromBackup()
    {
        var a = new Store(DataFile);
        a.Load();
        a.Data.Characters.Add("Gerettet");
        a.Save();
        a.Save();
        File.WriteAllText(DataFile, "{ kaputt");
        var b = new Store(DataFile);
        b.Load();
        Assert.NotNull(b.LoadWarning);
        Assert.Contains("Gerettet", b.Data.Characters);
        Assert.Single(Directory.GetFiles(_dir, "data.json.corrupt-*"));
    }

    [Fact]
    public void UndoRestoresSnapshot()
    {
        var a = new Store(DataFile);
        a.Load();
        var count = a.Data.Categories.Count;
        a.PushUndo();
        a.Data.Categories.Clear();
        a.Save();
        Assert.True(a.Undo());
        Assert.Equal(count, a.Data.Categories.Count);
    }


    [Theory]
    [InlineData(0.5, 0.004, Settings.DefaultPillX, Settings.DefaultPillY)]
    [InlineData(0.9, 0.036, Settings.DefaultPillX, Settings.DefaultPillY)]
    [InlineData(0.9072, 0.0327, Settings.DefaultPillX, Settings.DefaultPillY)]
    [InlineData(0.2, 0.3, 0.2, 0.3)]
    public void OldBadgePositionMovesOnlyWhenUntouched(double x, double y, double expectedX, double expectedY)
    {
        var a = new Store(DataFile);
        a.Load();
        a.Data.Version = 1;
        a.Data.Settings.PillX = x;
        a.Data.Settings.PillY = y;
        a.Save();
        var b = new Store(DataFile);
        b.Load();
        Assert.Equal(expectedX, b.Data.Settings.PillX);
        Assert.Equal(expectedY, b.Data.Settings.PillY);
        Assert.Equal(Store.CurrentVersion, b.Data.Version);
    }
    [Fact]
    public void CsvHasExactGoldAndQuotes()
    {
        var d = Defaults.Create(new DateOnly(2026, 11, 2));
        var c = d.Categories.First(x => x.Name == "Trading");
        var e = new Entry { CategoryId = c.Id, Date = new DateOnly(2026, 11, 2), Character = "Main" };
        e.Values[c.Fields[0].Id] = "Item; mit \"Quote\"";
        e.Values[c.Fields[2].Id] = "50000000";
        e.Values[c.Fields[3].Id] = "70000000";
        d.Entries.Add(e);
        var csv = CsvExport.Build(d, c);
        Assert.Contains("\"Item; mit \"\"Quote\"\"\"", csv);
        Assert.Contains("20000000", csv);
    }
}

public class UpdateFeedTests
{
    private const string Url = "https://github.com/ImAestosDev/Frostvein.ManagementTool/releases/download/v1.2.0/FMT.exe";
    private static readonly Version Current = new(1, 1, 0, 0);

    private static string Release(string tag = "v1.2.0", string name = "FMT.exe", string url = Url, string digest = "\"digest\": \"sha256:ABC\",", string extra = "") =>
        $$"""{ "tag_name": "{{tag}}", {{extra}} "assets": [ { "name": "other.zip", "browser_download_url": "x" }, { "name": "{{name}}", {{digest}} "browser_download_url": "{{url}}" } ] }""";

    [Fact]
    public void FindsNewerRelease()
    {
        var u = UpdateFeed.Parse(Release(), Current);
        Assert.NotNull(u);
        Assert.Equal(new Version(1, 2, 0), u.Version);
        Assert.Equal(Url, u.DownloadUrl);
        Assert.Equal("ABC", u.Sha256);
    }

    [Theory]
    [InlineData("v1.1.0")]
    [InlineData("v1.0.9")]
    [InlineData("kaputt")]
    public void IgnoresSameOlderOrBadVersions(string tag) => Assert.Null(UpdateFeed.Parse(Release(tag), Current));

    [Fact]
    public void IgnoresMissingAsset() => Assert.Null(UpdateFeed.Parse(Release(name: "Setup.exe"), Current));

    [Fact]
    public void IgnoresForeignDownloadUrl() =>
        Assert.Null(UpdateFeed.Parse(Release(url: "https://example.com/FMT.exe"), Current));

    [Fact]
    public void RequiresDigest() => Assert.Null(UpdateFeed.Parse(Release(digest: ""), Current));

    [Fact]
    public void IgnoresPrerelease() => Assert.Null(UpdateFeed.Parse(Release(extra: "\"prerelease\": true,"), Current));

    [Theory]
    [InlineData("")]
    [InlineData("{ kaputt")]
    [InlineData("[]")]
    public void SurvivesBadJson(string json) => Assert.Null(UpdateFeed.Parse(json, Current));

    [Fact]
    public void HashCheckDetectsChanges()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "abc");
            Assert.True(UpdateFeed.MatchesHash(file, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            Assert.False(UpdateFeed.MatchesHash(file, "00"));
        }
        finally { File.Delete(file); }
    }
}

public class WindowScheduleTests
{
    private static readonly List<ScheduleSlot> Table = WindowSchedule.Slots(120, 60);

    [Fact]
    public void MatchesInGameTable()
    {
        Assert.Equal(12, Table.Count);
        Assert.Equal("0:00", WindowSchedule.Clock(Table[0].StartMinute));
        Assert.Equal("1:00", WindowSchedule.Clock(Table[0].MarkerMinute));
        Assert.Equal("22:00", WindowSchedule.Clock(Table[11].StartMinute));
        Assert.Equal("0:00", WindowSchedule.Clock(Table[11].EndMinute));
        Assert.Equal("23:00", WindowSchedule.Clock(Table[11].MarkerMinute));
    }

    [Fact]
    public void FindsCurrentWindowAndNextMarker()
    {
        var s = WindowSchedule.StateAt(new DateTime(2026, 11, 1, 14, 30, 0), Table);
        Assert.Equal(7, s.SlotIndex);
        Assert.Equal(TimeSpan.FromMinutes(90), s.LeftInSlot);
        Assert.Equal(0.25, s.SlotProgress);
        Assert.Equal(new DateTime(2026, 11, 1, 15, 0, 0), s.NextMarker);
    }

    [Fact]
    public void MarkerRollsOverMidnight()
    {
        var s = WindowSchedule.StateAt(new DateTime(2026, 11, 1, 23, 30, 0), Table);
        Assert.Equal(11, s.SlotIndex);
        Assert.Equal(new DateTime(2026, 11, 2, 1, 0, 0), s.NextMarker);
    }

    [Fact]
    public void ExactBoundariesBelongToTheNextWindow()
    {
        Assert.Equal(1, WindowSchedule.StateAt(new DateTime(2026, 11, 1, 2, 0, 0), Table).SlotIndex);
        Assert.Equal(new DateTime(2026, 11, 1, 3, 0, 0), WindowSchedule.StateAt(new DateTime(2026, 11, 1, 1, 0, 0), Table).NextMarker);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5000, 9000)]
    [InlineData(7, 50)]
    public void BadSettingsStillCoverTheDay(int window, int offset)
    {
        var slots = WindowSchedule.Slots(window, offset);
        Assert.Equal(0, slots[0].StartMinute);
        Assert.Equal(1440, slots[^1].EndMinute);
        Assert.All(slots, x => Assert.InRange(x.MarkerMinute, x.StartMinute, x.EndMinute - 1));
    }
}
