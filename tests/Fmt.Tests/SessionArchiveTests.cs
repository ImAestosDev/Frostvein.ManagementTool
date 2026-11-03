using Fmt.Core;
using Xunit;

namespace Fmt.Tests;

public class SessionArchiveTests
{
    private const long Me = 77;
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0);
    private const string Hit = "su 1 77 3 500 240 10 11 12 20 30 0 0 1000 0 0";

    private static Session Played(DateTime start)
    {
        var s = new Session(start, "lev 85 0 40 500 10000 1000 0 0 0 0 0 0", new DamageMeter(), null);
        s.Handle(Hit, start, Me);
        s.Handle("lev 85 1000 40 500 10000 1000 0 0 0 0 0 0", start, Me);
        s.Handle("drop 1012 1 1 1 3 0 77", start, Me);
        s.Handle("get 1 77 1 0", start, Me);
        s.Handle("drop 1046 2 1 1 500 0 77", start, Me);
        s.Handle("get 1 77 2 0", start, Me);
        return s;
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "fmt-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void SnapshotCarriesAllCounters()
    {
        var s = Played(T0);
        s.End(T0.AddMinutes(30));
        var saved = s.Snapshot(T0.AddMinutes(30));
        Assert.True(saved.Finished);
        Assert.Equal(T0, saved.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(30), saved.Duration);
        Assert.Equal(1000, saved.OwnDamage);
        Assert.Equal(1, saved.TotalKills);
        Assert.Equal(500, saved.Gold);
        Assert.Equal(3, Assert.Single(saved.Items).Amount);
        Assert.Equal(20, saved.Exp[0].PercentPerHour, 3);
    }

    [Fact]
    public void SurvivesSavingAndLoading()
    {
        var path = TempFile();
        try
        {
            var archive = new SessionArchive();
            var s = Played(T0);
            s.End(T0.AddMinutes(30));
            archive.Add(s.Snapshot(T0.AddMinutes(30)));
            archive.Save(path);

            var loaded = Assert.Single(SessionArchive.Load(path).NewestFirst());
            Assert.Equal(1000, loaded.OwnDamage);
            Assert.Equal(TimeSpan.FromMinutes(30), loaded.Duration);
            Assert.Equal(500, loaded.Gold);
            Assert.Equal("Level", loaded.Exp[0].Label);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RunningSnapshotGetsReplacedByTheFinalOne()
    {
        var archive = new SessionArchive();
        var s = Played(T0);
        archive.Add(s.Snapshot(T0.AddMinutes(1)));
        Assert.False(archive.NewestFirst()[0].Finished);
        s.End(T0.AddMinutes(2));
        archive.Add(s.Snapshot(T0.AddMinutes(2)));
        Assert.True(Assert.Single(archive.NewestFirst()).Finished);
    }

    [Fact]
    public void EmptySessionIsNotKept()
    {
        var archive = new SessionArchive();
        var s = new Session(T0, null, new DamageMeter(), null);
        s.End(T0.AddSeconds(2));
        archive.Add(s.Snapshot(T0.AddSeconds(2)));
        Assert.Empty(archive.NewestFirst());
        Assert.False(archive.Dirty);
    }

    [Fact]
    public void NewestComesFirst()
    {
        var archive = new SessionArchive();
        archive.Add(Played(T0).Snapshot(T0));
        archive.Add(Played(T0.AddDays(1)).Snapshot(T0.AddDays(1)));
        Assert.Equal(T0.AddDays(1), archive.NewestFirst()[0].StartedAt);
    }

    [Fact]
    public void DamagedFileIsKeptAside()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "{ kaputt");
            Assert.Empty(SessionArchive.Load(path).NewestFirst());
            Assert.Equal("{ kaputt", File.ReadAllText(path + ".bad"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bad");
        }
    }
}
