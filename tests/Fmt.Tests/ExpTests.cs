using Fmt.Core;
using Xunit;

namespace Fmt.Tests;

public class ExpTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0);

    private static string Lev(int level, long xp, long load, int job = 40, long jobXp = 500, long jobLoad = 1000) =>
        $"lev {level} {xp} {job} {jobXp} {load} {jobLoad} 100000 50 0 0 0 0";

    private static ExpTrack Track(ExpTracker t, string shortName, DateTime now) =>
        t.Stats(now).Tracks.First(x => x.Short == shortName);

    [Fact]
    public void FirstPacketIsOnlyTheBaseline()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 2500, 10000), T0);
        var level = Track(t, "EXP", T0);
        Assert.Equal(85, level.Level);
        Assert.Equal(25, level.Percent);
        Assert.Equal(0, level.GainedPercent);
    }

    [Fact]
    public void AddsUpGainedPercent()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 2500, 10000), T0);
        t.Handle(Lev(85, 2600, 10000), T0);
        t.Handle(Lev(85, 3500, 10000), T0);
        Assert.Equal(10, Track(t, "EXP", T0).GainedPercent, 3);
    }

    [Fact]
    public void LevelUpCountsTheRestOfTheOldLevel()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 9000, 10000), T0);
        t.Handle(Lev(86, 500, 20000), T0);
        var level = Track(t, "EXP", T0);
        Assert.Equal(86, level.Level);
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
        Assert.Equal(0, Track(t, "JXP", T0).GainedPercent);
        Assert.Equal(0, Track(t, "EXP", T0).GainedPercent);
        Assert.Equal(40, Track(t, "EXP", T0).Percent);
    }

    [Fact]
    public void ResetKeepsTheCurrentProgress()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 2500, 10000), T0);
        t.Handle(Lev(85, 3500, 10000), T0);
        t.Reset(T0.AddHours(1));
        var level = Track(t, "EXP", T0.AddHours(1));
        Assert.Equal(0, level.GainedPercent);
        Assert.Equal(35, level.Percent);
        t.Handle(Lev(85, 3600, 10000), T0.AddHours(1));
        Assert.Equal(1, Track(t, "EXP", T0.AddHours(1)).GainedPercent, 3);
    }

    [Fact]
    public void RecordingOnlyCountsBetweenStartAndStop()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 1000, 10000), T0);
        t.Handle(Lev(85, 2000, 10000), T0);
        t.StartRecording(T0);
        t.Handle(Lev(85, 2500, 10000), T0.AddMinutes(10));
        t.StopRecording(T0.AddMinutes(30));
        t.Handle(Lev(85, 9000, 10000), T0.AddHours(5));

        var recording = t.Stats(T0.AddHours(5)).Recording!;
        Assert.False(recording.Running);
        Assert.Equal(TimeSpan.FromMinutes(30), recording.Duration);
        var level = recording.Rows.First(r => r.Label == "Level");
        Assert.Equal(5, level.GainedPercent, 3);
        Assert.Equal(10, level.PercentPerHour, 3);
    }

    [Fact]
    public void ResetDoesNotTouchARunningRecording()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 0, 10000), T0);
        t.StartRecording(T0);
        t.Handle(Lev(85, 1000, 10000), T0);
        t.Reset(T0);
        var recording = t.Stats(T0.AddHours(1)).Recording!;
        Assert.True(recording.Running);
        Assert.Equal(10, recording.Rows[0].PercentPerHour, 3);
    }

    [Fact]
    public void NewRecordingReplacesTheLastResult()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 0, 10000), T0);
        t.StartRecording(T0);
        t.Handle(Lev(85, 1000, 10000), T0);
        t.StopRecording(T0.AddHours(1));
        t.StartRecording(T0.AddHours(2));
        Assert.Equal(0, t.Stats(T0.AddHours(3)).Recording!.Rows[0].GainedPercent);
    }

    [Fact]
    public void RecordingWithoutTimeHasNoAverage()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 0, 10000), T0);
        Assert.Null(t.Stats(T0).Recording);
        t.StartRecording(T0);
        Assert.Equal(0, t.Stats(T0).Recording!.Rows[0].PercentPerHour);
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
        Assert.DoesNotContain(t.Stats(T0).Tracks, x => x.Short == "EXP");
    }

    // Line from the official server.
    [Fact]
    public void ReadsARealPacket()
    {
        var t = new ExpTracker();
        t.Handle("lev 99 0 99 0 2140719000 5991794 1643242 172 3639674 68 178874511 0", T0);
        var hero = Track(t, "HXP", T0);
        Assert.Equal("Heldenlevel", hero.Label);
        Assert.Equal(68, hero.Level);
        Assert.Equal(2.03, hero.Percent, 2);
        Assert.Equal(99, Track(t, "JXP", T0).Level);
    }

    [Fact]
    public void HeroOnlyShowsOnceUnlocked()
    {
        var t = new ExpTracker();
        t.Handle(Lev(85, 0, 10000), T0);
        Assert.DoesNotContain(t.Stats(T0).Tracks, x => x.Short == "HXP");
    }
}
