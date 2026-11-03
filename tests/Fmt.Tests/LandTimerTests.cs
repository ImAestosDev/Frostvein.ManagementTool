using Fmt.Core;
using Xunit;

namespace Fmt.Tests;

public class LandTimerTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0);
    private static readonly TimeSpan Gold = TimeSpan.FromMinutes(5), Holy = TimeSpan.FromMinutes(10);

    private static LandStats Stats(LandTracker t, DateTime now) => t.Stats(now, Gold, Holy);

    private static string In(int vnum, long id) => $"in 3 {vnum} {id} 10 10 2 100 100 0 0 0 -1 1 0";

    private static string Kill(long id, long attacker = 99) => $"su 1 {attacker} 3 {id} 240 10 11 12 20 30 0 0 500 0 0";

    [Theory]
    [InlineData("msgi 0 2568 0 0 0 0 0")]
    [InlineData("msgi 0 2570 0 0 0 0 0")]
    [InlineData("msgi 2568")]
    public void SpawnCountsDownOneMinute(string packet)
    {
        var t = new LandTracker();
        t.Handle(packet, T0);
        var a = Stats(t, T0.AddSeconds(15)).Asgobas;
        Assert.Equal(AsgobasPhase.Present, a.Phase);
        Assert.Equal(TimeSpan.FromSeconds(45), a.Remaining);
        Assert.Equal(0.75, a.Fraction, 3);
    }

    [Theory]
    [InlineData("msgi 0 2566 0 0 0 0 0")]
    [InlineData("msgi 0 2572 0 0 0 0 0")]
    public void DespawnCountsDownFourMinutes(string packet)
    {
        var t = new LandTracker();
        t.Handle(packet, T0);
        var a = Stats(t, T0.AddMinutes(1)).Asgobas;
        Assert.Equal(AsgobasPhase.Waiting, a.Phase);
        Assert.Equal(TimeSpan.FromMinutes(3), a.Remaining);
    }

    [Fact]
    public void EarlySpawnResetsAtOnce()
    {
        var t = new LandTracker();
        t.Handle("msgi 0 2566 0 0 0 0 0", T0);
        t.Handle("msgi 0 2568 0 0 0 0 0", T0.AddMinutes(2));
        var a = Stats(t, T0.AddMinutes(2)).Asgobas;
        Assert.Equal(AsgobasPhase.Present, a.Phase);
        Assert.Equal(TimeSpan.FromSeconds(60), a.Remaining);
    }

    [Fact]
    public void LateAsgobasShowsTheDelay()
    {
        var t = new LandTracker();
        t.Handle("msgi 0 2566 0 0 0 0 0", T0);
        var a = Stats(t, T0.AddMinutes(4).AddSeconds(12)).Asgobas;
        Assert.Equal(TimeSpan.FromSeconds(-12), a.Remaining);
        Assert.Equal(0, a.Fraction);
    }

    [Theory]
    [InlineData("msgi 0 623 0 0 0 0 0")]
    [InlineData("msgi 0 1 2568 0 0 0 0")]
    public void OtherMessagesCountNothing(string packet)
    {
        var t = new LandTracker();
        t.Handle(packet, T0);
        Assert.Equal(AsgobasPhase.Unknown, Stats(t, T0).Asgobas.Phase);
    }

    [Fact]
    public void BothVersionsFillTheSameBars()
    {
        var t = new LandTracker();
        t.Handle(In(809, 1), T0);
        t.Handle(In(1481, 2), T0);
        t.Handle(In(1461, 3), T0);
        t.Handle(In(1482, 4), T0);
        t.Handle(In(333, 5), T0);
        var s = Stats(t, T0);
        Assert.Equal(new long?[] { 1, 2, null }, s.GoldDragons.Select(d => d.Id));
        Assert.Equal(new long?[] { 3, 4, null }, s.HolyDragons.Select(d => d.Id));
    }

    [Fact]
    public void AnyKillStartsTheTimerOfThatDragonOnly()
    {
        var t = new LandTracker();
        foreach (var id in new long[] { 3, 1, 2 }) t.Handle(In(809, id), T0);
        t.Handle(Kill(2), T0);
        t.Handle(Kill(2), T0.AddSeconds(30));

        var dragons = Stats(t, T0.AddMinutes(1)).GoldDragons;
        Assert.Equal(new long?[] { 1, 2, 3 }, dragons.Select(d => d.Id));
        Assert.False(dragons[0].Dead);
        Assert.True(dragons[1].Dead);
        Assert.Equal(TimeSpan.FromMinutes(4), dragons[1].Remaining);
        Assert.Equal(0.8, dragons[1].Fraction, 3);
    }

    [Fact]
    public void DieAlsoCountsAndHitsWithoutDeathDoNot()
    {
        var t = new LandTracker();
        t.Handle(In(1461, 1), T0);
        t.Handle(In(1461, 2), T0);
        t.Handle("su 1 99 3 1 240 10 11 12 20 30 1 50 500 0 0", T0);
        t.Handle("die 3 2 3 2", T0);
        var dragons = Stats(t, T0).HolyDragons;
        Assert.False(dragons[0].Dead);
        Assert.True(dragons[1].Dead);
        Assert.Equal(Holy, dragons[1].Remaining);
    }

    [Fact]
    public void RespawnEndsTheTimerAtOnce()
    {
        var t = new LandTracker();
        t.Handle(In(809, 1), T0);
        t.Handle(Kill(1), T0);
        t.Handle(In(809, 1), T0.AddMinutes(2));
        Assert.False(Stats(t, T0.AddMinutes(2)).GoldDragons[0].Dead);
    }

    [Fact]
    public void AnotherChannelReplacesLivingDragonsFirst()
    {
        var t = new LandTracker();
        foreach (var id in new long[] { 1, 2, 3 }) t.Handle(In(809, id), T0);
        t.Handle(Kill(1), T0);
        t.Handle(In(809, 7), T0.AddMinutes(1));
        Assert.Equal(new long?[] { 1, 3, 7 }, Stats(t, T0.AddMinutes(1)).GoldDragons.Select(d => d.Id));
    }

    [Fact]
    public void TimersSurviveLeavingTheArea()
    {
        var t = new LandTracker();
        t.Handle(In(809, 1), T0);
        t.Handle(Kill(1), T0);
        Assert.True(Stats(t, T0).InLand);
        t.Handle("c_map 0 1 1", T0);
        var s = Stats(t, T0.AddMinutes(1));
        Assert.False(s.InLand);
        Assert.Equal(TimeSpan.FromMinutes(4), s.GoldDragons[0].Remaining);
    }

    [Fact]
    public void EmptyStatsHaveAllBars()
    {
        Assert.Equal(3, LandStats.Empty.GoldDragons.Count);
        Assert.Equal(3, LandStats.Empty.HolyDragons.Count);
        Assert.False(LandStats.Empty.InLand);
    }
}
