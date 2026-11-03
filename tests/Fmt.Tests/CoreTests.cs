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
    public void MeterCountsEveryPlayerFromTheFirstHit()
    {
        var m = new DamageMeter();
        var t = new DateTime(2026, 11, 3, 12, 0, 0);
        // Nothing known yet: hits still count.
        m.Handle("su 1 77 3 500 240 10 11 12 20 30 1 80 1000 0 0", t);
        m.Handle("su 1 77 3 500 251 10 11 12 20 30 1 80 5000 3 0", t.AddSeconds(4));
        m.Handle("su 1 77 3 500 240 10 11 12 20 30 1 80 0 2 0", t);
        m.Handle("su 1 99 3 500 240 10 11 12 20 30 1 80 90000 0 0", t);
        // Monsters and self buffs do not.
        m.Handle("su 3 77 1 77 240 10 11 12 20 30 1 80 90000 0 0", t);
        m.Handle("su 1 77 1 77 240 10 11 12 20 30 1 100 90000 0 0", t);

        var before = m.Stats();
        Assert.Null(before.OwnId);
        Assert.Equal([99L, 77L], before.Players.Select(p => p.Id));
        Assert.Equal("Spieler 77", before.Players[1].Name);

        m.Handle("in 1 Mitspieler - 99 20 30 2 0 0 0", t);
        m.Handle("c_info Aestos - -1 -1 - 77 0 0 0 0 1 0 0 0 0 0 0 0", t);

        var s = m.Stats();
        Assert.Equal("Mitspieler", s.Players[0].Name);
        var own = s.Own!;
        Assert.Equal("Aestos", own.Name);
        Assert.Equal(6000, own.Total);
        Assert.Equal(5000, own.Best);
        Assert.Equal(251, own.BestSkill);
        Assert.Equal(2, own.Hits);
        Assert.Equal(1, own.Misses);
        Assert.Equal(1500, own.PerSecond);
        Assert.Equal(96000, s.MapTotal);

        m.Reset();
        Assert.Empty(m.Stats().Players);
        Assert.Equal(77, m.Stats().OwnId);
    }

    [Fact]
    public void NamesComeFromChatAndListsAndSurviveRestart()
    {
        var t = new DateTime(2026, 11, 3, 12, 0, 0);
        var path = Path.Combine(Path.GetTempPath(), $"fmt-names-{Guid.NewGuid():N}.json");
        try
        {
            var book = new NameBook();
            var m = new DamageMeter(null, book);
            m.Handle("spk 1 11 5 Chatter hallo zusammen", t);
            m.Handle("pinit 2 1|12|1|99|Gruppe|5|0|1|0|45|0|0|0 2|900|0|50|Pet|-1|319|0|0", t);
            m.Handle("finit 13|0|1|Freund", t);
            m.Handle("gmbr 0 14|3|Familie|90|1|0|0|1|20", t);
            foreach (var id in new[] { 11, 12, 13, 14 })
                m.Handle($"su 1 {id} 3 500 240 10 11 12 20 30 1 80 100 0 0", t);
            Assert.Equal(["Chatter", "Gruppe", "Freund", "Familie"], m.Stats().Players.Select(p => p.Name));

            book.Save(path);
            Assert.False(book.Dirty);
            var later = new DamageMeter(null, NameBook.Load(path));
            later.Handle("su 1 12 3 500 240 10 11 12 20 30 1 80 100 0 0", t);
            Assert.Equal("Gruppe", later.Stats().Players[0].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NameBookSurvivesDamagedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fmt-names-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ kaputt");
        try
        {
            Assert.False(NameBook.Load(path).TryGet(1, out _));
            Assert.False(NameBook.Load(path + ".missing").TryGet(1, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ModesPickTheirTables()
    {
        var t = new DateTime(2026, 11, 3, 12, 0, 0);
        var m = new DamageMeter();
        m.Handle("c_info Ich - -1 -1 - 500 0 0 0 0 1 0 0 0 0 0 0 0", t);
        m.Handle("su 1 500 3 9 240 10 11 12 20 30 1 80 10 0 0", t);
        for (var i = 1; i <= 25; i++)
            m.Handle($"su 1 {i} 3 9 240 10 11 12 20 30 1 80 {1000 - i} 0 0", t);

        var s = m.Stats();
        Assert.Equal([500L], s.Table(DamageMode.Single).Select(p => p.Id));

        // Strangers are better here, the own row still keeps the last slot.
        Assert.Equal([1L, 2L, 500L], s.Table(DamageMode.Group).Select(p => p.Id));
        var raid = s.Table(DamageMode.Raid);
        Assert.Equal(20, raid.Count);
        Assert.Equal(500, raid[^1].Id);

        // A known group hides strangers.
        m.Handle("pinit 2 1|500|1|99|Ich|5|0|1|0|45|0|0|0 1|7|2|99|Freund|5|0|1|0|45|0|0|0", t);
        Assert.Equal([7L, 500L], m.Stats().Table(DamageMode.Group).Select(p => p.Id));

        // Leaving the group: back to the best on the map.
        m.Handle("pinit 0", t);
        Assert.Equal(3, m.Stats().Table(DamageMode.Group).Count);

        var nobody = new DamageMeter().Stats();
        Assert.Empty(nobody.Table(DamageMode.Single));
    }

    [Fact]
    public void MeterFindsOwnCharacterWithoutMapChange()
    {
        var t = new DateTime(2026, 11, 3, 12, 0, 0);

        var byCond = new DamageMeter();
        byCond.Handle("cond 2 500 0 0 10", t);
        Assert.Null(byCond.OwnId);
        byCond.Handle("cond 1 77 0 0 10", t);
        Assert.Equal(77, byCond.OwnId);

        var bySavedId = new DamageMeter(new Dictionary<long, string> { [77] = "Aestos" });
        bySavedId.Handle("su 1 99 3 500 240 10 11 12 20 30 1 80 1000 0 0", t);
        Assert.Null(bySavedId.OwnId);
        bySavedId.Handle("su 1 77 3 500 240 10 11 12 20 30 1 80 1000 0 0", t);
        Assert.Equal("Aestos", bySavedId.Stats().Own!.Name);

        var byClick = new DamageMeter();
        byClick.Handle("su 1 77 3 500 240 10 11 12 20 30 1 80 1000 0 0", t);
        byClick.SetOwn(77);
        Assert.Equal(1000, byClick.Stats().Own!.Total);
    }

    private static byte[] PlainFrame(string text) =>
        [(byte)text.Length, .. text.Select(c => (byte)(c ^ 0xFF)), 0xFF];

    [Fact]
    public void DecodesPlainAndPackedRuns()
    {
        // "su " plain, then "1 -9." packed: nibbles 5,1 / 2,13 / 3,0.
        byte[] frame = [3, (byte)('s' ^ 0xFF), (byte)('u' ^ 0xFF), (byte)(' ' ^ 0xFF), 0x85, 0x51, 0x2D, 0x30];
        Assert.Equal("su 1 -9.", WorldDecoder.Decode(frame));
    }

    [Fact]
    public void StreamJoinsSplitAndReorderedSegments()
    {
        byte[] first = PlainFrame("first");
        byte[] a = PlainFrame("alpha");
        byte[] b = PlainFrame("beta");
        byte[] all = [.. first, .. a, .. b];
        var cut1 = first.Length + 2;
        var cut2 = first.Length + a.Length + 3;
        var output = new List<string>();
        var s = new TcpStream();

        s.Push(uint.MaxValue - 2, all.AsSpan(0, cut1), output);
        s.Push(unchecked(uint.MaxValue - 2 + (uint)cut2), all.AsSpan(cut2), output);
        Assert.Empty(output);
        s.Push(unchecked(uint.MaxValue - 2 + (uint)cut1), all.AsSpan(cut1, cut2 - cut1), output);
        // Retransmission must not count twice.
        s.Push(unchecked(uint.MaxValue - 2 + (uint)cut1), all.AsSpan(cut1, cut2 - cut1), output);

        // The first frame may start mid packet, so it is skipped.
        Assert.Equal(["alpha", "beta"], output);
    }

    [Fact]
    public void StreamDropsGarbageFrames()
    {
        var output = new List<string>();
        var s = new TcpStream();
        byte[] data = [.. PlainFrame("x"), 2, 0xFE, 0xFD, 0xFF, .. PlainFrame("ok 1")];
        s.Push(1, data, output);
        Assert.Equal(["ok 1"], output);
    }

    [Fact]
    public void ParsesIpv4TcpSegment()
    {
        byte[] p = new byte[46];
        p[0] = 0x45;
        p[3] = 46;
        p[9] = 6;
        p[12] = 10; p[13] = 0; p[14] = 0; p[15] = 1;
        p[20] = 0x10; p[21] = 0x20;
        p[22] = 0xC0; p[23] = 0x01;
        p[24] = 0; p[25] = 0; p[26] = 1; p[27] = 2;
        p[32] = 0x50;
        Assert.True(IpPacket.TryParseTcp(p, out var s));
        Assert.Equal(0x1020, s.SourcePort);
        Assert.Equal(0xC001, s.DestinationPort);
        Assert.Equal(258u, s.Sequence);
        Assert.Equal(40, s.PayloadOffset);
        Assert.Equal(6, s.PayloadLength);
        Assert.Equal(BitConverter.ToUInt32([10, 0, 0, 1]), s.SourceAddress);

        p[9] = 17;
        Assert.False(IpPacket.TryParseTcp(p, out _));
    }

    [Fact]
    public void MigrationMovesTypedAmountsToEuroField()
    {
        var a = new Store(DataFile);
        a.Load();
        var old = a.Data.Categories.First(c => c.Name == "NosTaler");
        var purpose = new FieldDef { Name = "Wofür", Required = true };
        var count = new FieldDef { Name = "NosTaler", Type = FieldType.Number, Required = true };
        var note = new FieldDef { Name = "Notiz", Type = FieldType.LongText, ShowInList = false };
        old.Fields = [purpose, count, note];
        old.SumFieldId = count.Id;
        Entry Old(string text) => new() { CategoryId = old.Id, Values = { [purpose.Id] = text, [count.Id] = "160" } };
        var paid = Old("100.00€");
        var named = Old("Kostüm");
        a.Data.Entries.AddRange([paid, named]);
        a.Data.Version = 5;
        a.Save();

        var b = new Store(DataFile);
        b.Load();
        var c = b.Data.Categories.First(x => x.Name == "NosTaler");
        Assert.Equal(["Betrag", "Notiz"], c.Fields.Select(f => f.Name));
        var amount = c.Fields[0];
        Assert.Equal(amount.Id, c.SumFieldId);
        var first = b.Data.Entries.First(e => e.Id == paid.Id);
        Assert.Equal(100m, Calc.Numeric(c, first, amount));
        Assert.Equal("100,00 €", Calc.Title(c, first));
        var second = b.Data.Entries.First(e => e.Id == named.Id);
        Assert.Null(Calc.Numeric(c, second, amount));
        Assert.Equal("Kostüm", second.Values[note.Id]);
    }

    [Theory]
    [InlineData("100", "100")]
    [InlineData("100.00€", "100")]
    [InlineData("19,99", "19.99")]
    [InlineData("19.99 €", "19.99")]
    [InlineData("1.000", "1000")]
    [InlineData("1.000,50", "1000.5")]
    [InlineData("1,000.50", "1000.5")]
    [InlineData("-5", "-5")]
    public void ParsesMoney(string text, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Money.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("Kostüm")]
    [InlineData("1,2,3")]
    public void RejectsBadMoney(string text) => Assert.Null(Money.Parse(text));

    [Fact]
    public void FormatsMoneyGerman() => Assert.Equal("1.000,50 €", Money.Format(1000.5m));

    [Fact]
    public void MigrationAddsNewTabsOnce()
    {
        var a = new Store(DataFile);
        a.Load();
        a.Data.Version = 4;
        a.Data.Categories.RemoveAll(c => c.Name is "NosTaler" or "Notizen");
        var count = a.Data.Categories.Count;
        a.Save();

        var b = new Store(DataFile);
        b.Load();
        Assert.Equal(count + 2, b.Data.Categories.Count);
        var nosTaler = b.Data.Categories.First(c => c.Name == "NosTaler");
        Assert.Contains(nosTaler.Fields, f => f.Id == nosTaler.SumFieldId && f.Type == FieldType.Euro);
        Assert.False(nosTaler.CountsInTotal);
        b.Save();

        var again = new Store(DataFile);
        again.Load();
        Assert.Equal(count + 2, again.Data.Categories.Count);
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
