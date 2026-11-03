using Fmt.Core;
using Xunit;

namespace Fmt.Tests;

public class SessionTests
{
    private const long Me = 77;
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0);
    private const string Hit = "su 1 77 3 500 240 10 11 12 20 30 1 80 1000 0 0";

    private static Session Start(string? lev = null, DropTracker? live = null) => new(T0, lev, new DamageMeter(), live);

    [Fact]
    public void CountsAllThreeCounters()
    {
        var s = Start("lev 85 1000 40 500 10000 1000 0 0 0 0 0 0");
        s.Handle(Hit, T0, Me);
        s.Handle("lev 85 2000 40 500 10000 1000 0 0 0 0 0 0", T0, Me);
        s.Handle("drop 1012 1 1 1 3 0 77", T0, Me);
        s.Handle("get 1 77 1 0", T0, Me);
        s.Handle("drop 1046 2 1 1 500 0 77", T0, Me);
        s.Handle("get 1 77 2 0", T0, Me);

        var stats = s.Stats(T0.AddMinutes(30));
        Assert.True(stats.Running);
        Assert.Equal(1000, stats.Damage.Own!.Total);
        Assert.Equal(10, stats.Exp.Tracks[0].GainedPercent, 3);
        Assert.Equal(3, Assert.Single(stats.Drops.Items).Amount);
        Assert.Equal(500, stats.Drops.Gold);
    }

    [Fact]
    public void EndFreezesEverything()
    {
        var s = Start("lev 85 0 40 500 10000 1000 0 0 0 0 0 0");
        s.Handle("lev 85 1000 40 500 10000 1000 0 0 0 0 0 0", T0, Me);
        s.End(T0.AddMinutes(30));
        s.Handle(Hit, T0.AddHours(1), Me);
        s.Handle("lev 85 9000 40 500 10000 1000 0 0 0 0 0 0", T0.AddHours(1), Me);

        var stats = s.Stats(T0.AddHours(2));
        Assert.False(stats.Running);
        Assert.Equal(TimeSpan.FromMinutes(30), stats.Duration);
        Assert.Null(stats.Damage.Own);
        Assert.Equal(20, stats.Exp.Recording!.Rows[0].PercentPerHour, 3);
    }

    [Fact]
    public void DoesNotTouchTheLiveCounters()
    {
        var live = new DropTracker();
        live.Handle("drop 1012 1 1 1 3 0 77", Me);
        live.Handle("get 1 77 1 0", Me);
        var s = Start(null, live);
        s.Handle("drop 2282 2 1 1 1 0 77", T0, Me);
        s.Handle("get 1 77 2 0", T0, Me);
        Assert.Equal(1012, Assert.Single(live.Stats().Items).VNum);
        Assert.Equal(2282, Assert.Single(s.Stats(T0).Drops.Items).VNum);
    }

    [Fact]
    public void DropLyingAroundAtStartCountsWhenPickedUp()
    {
        var live = new DropTracker();
        live.Handle("drop 1012 1 1 1 3 0 77", Me);
        var s = Start(null, live);
        s.Handle("get 1 77 1 0", T0, Me);
        Assert.Equal(3, Assert.Single(s.Stats(T0).Drops.Items).Amount);
    }

    [Fact]
    public void KnowsTheOwnCharacterFromTheLiveMeter()
    {
        var s = Start();
        s.Handle(Hit, T0, Me);
        Assert.Equal(Me, s.Stats(T0).Damage.OwnId);
    }
}
