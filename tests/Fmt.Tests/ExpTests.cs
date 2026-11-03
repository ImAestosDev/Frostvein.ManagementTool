using Fmt.Core;
using Xunit;

namespace Fmt.Tests;

public class ExpTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0);

    private static string Lev(int level, long xp, long load, int job = 40, long jobXp = 500, long jobLoad = 1000) =>
        $"lev {level} {xp} {job} {jobXp} {load} {jobLoad} 100000 50 0 0 0 0";

    private static ExpTrack Track(ExpTracker t, string label, DateTime now) =>
        t.Stats(now).Tracks.First(x => x.Label == label);

    [Fact]
    public void FirstPacketIsOnlyTheBaseline()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 2500, 10000), T0);
        var level = Track(t, "Level", T0);
        Assert.Equal(85, level.Level);
        Assert.Equal(25, level.Percent);
        Assert.Equal(0, level.Gained);
    }

    [Fact]
    public void AddsUpGainsInXpAndPercent()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 2500, 10000), T0);
        t.Handle(Lev(85, 2600, 10000), T0);
        t.Handle(Lev(85, 3500, 10000), T0);
        var level = Track(t, "Level", T0);
        Assert.Equal(1000, level.Gained);
        Assert.Equal(900, level.LastGain);
        Assert.Equal(10, level.GainedPercent, 3);
    }

    [Fact]
    public void LevelUpCountsTheRestOfTheOldLevel()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 9000, 10000), T0);
        t.Handle(Lev(86, 500, 20000), T0);
        var level = Track(t, "Level", T0);
        Assert.Equal(86, level.Level);
        Assert.Equal(1500, level.Gained);
        Assert.Equal(1, level.LevelsGained);
        Assert.Equal(12.5, level.GainedPercent, 3);
    }

    [Fact]
    public void SpSwapAndXpLossCountNothing()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 5000, 10000, 40, 500, 1000), T0);
        t.Handle(Lev(85, 5000, 10000, 40, 900, 3000), T0);
        t.Handle(Lev(85, 4000, 10000, 12, 10, 200), T0);
        Assert.Equal(0, Track(t, "Job / SP", T0).Gained);
        Assert.Equal(0, Track(t, "Level", T0).Gained);
        Assert.Equal(40, Track(t, "Level", T0).Percent);
    }

    [Fact]
    public void RateAndTimeToLevelNeedAMinute()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 0, 10000), T0);
        t.Handle(Lev(85, 2500, 10000), T0.AddSeconds(30));
        Assert.Null(Track(t, "Level", T0.AddSeconds(30)).PerHour);

        var level = Track(t, "Level", T0.AddHours(1));
        Assert.Equal(2500, level.PerHour);
        Assert.Equal(TimeSpan.FromHours(3), level.TimeToLevel);
    }

    [Fact]
    public void ResetKeepsTheCurrentProgress()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 2500, 10000), T0);
        t.Handle(Lev(85, 3500, 10000), T0);
        t.Reset(T0.AddHours(1));
        var level = Track(t, "Level", T0.AddHours(1));
        Assert.Equal(0, level.Gained);
        Assert.Equal(35, level.Percent);
        t.Handle(Lev(85, 3600, 10000), T0.AddHours(1));
        Assert.Equal(100, Track(t, "Level", T0.AddHours(1)).Gained);
    }

    [Theory]
    [InlineData("lev")]
    [InlineData("lev 85 x 40 500 10000 1000")]
    [InlineData("lev 85 100 40 500 0 0")]
    [InlineData("levx 85 100 40 500 10000 1000")]
    public void BadPacketsAreIgnored(string packet)
    {
        var t = new ExpTracker();
        t.Handle(packet, T0);
        Assert.DoesNotContain(t.Stats(T0).Tracks, x => x.Label == "Level");
    }

    [Fact]
    public void HeroOnlyShowsOnceUnlocked()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 0, 10000), T0);
        Assert.DoesNotContain(t.Stats(T0).Tracks, x => x.Label == "Held");
        t.Handle("lev 85 0 40 500 10000 1000 100000 50 7000 12 28000 0", T0);
        Assert.Equal(25, Track(t, "Held", T0).Percent);
    }
}
