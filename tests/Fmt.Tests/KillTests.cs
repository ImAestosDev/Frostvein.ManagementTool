using Fmt.Core;
using Xunit;

namespace Fmt.Tests;

public class KillTests
{
    private const long Me = 77;

    private static string Hit(long attacker, long target, int alive, int targetType = 3) =>
        $"su 1 {attacker} {targetType} {target} 240 10 11 12 20 30 {alive} 0 500 0 0";

    [Fact]
    public void CountsOwnKillsPerMonster()
    {
        var t = new KillTracker();
        t.Handle("in 3 333 500 10 10 2 100 100 0 0 0 -1 1 0", Me);
        t.Handle("in 3 333 501 10 10 2 100 100 0 0 0 -1 1 0", Me);
        t.Handle("in 3 444 502 10 10 2 100 100 0 0 0 -1 1 0", Me);
        foreach (var id in new[] { 500, 501, 502 }) t.Handle(Hit(Me, id, 0), Me);

        var stats = t.Stats();
        Assert.Equal(3, stats.Total);
        Assert.Equal(333, stats.Monsters[0].VNum);
        Assert.Equal(2, stats.Monsters[0].Count);
    }

    [Fact]
    public void HitsThatDoNotKillOrFromOthersCountNothing()
    {
        var t = new KillTracker();
        t.Handle("in 3 333 500 10 10 2 100 100 0 0 0 -1 1 0", Me);
        t.Handle(Hit(Me, 500, 1), Me);
        t.Handle(Hit(99, 500, 0), Me);
        t.Handle(Hit(Me, 99, 0, 1), Me);
        Assert.Equal(0, t.Stats().Total);
    }

    [Fact]
    public void MultiHitCountsOnceUntilRespawn()
    {
        var t = new KillTracker();
        t.Handle("in 3 333 500 10 10 2 100 100 0 0 0 -1 1 0", Me);
        t.Handle(Hit(Me, 500, 0), Me);
        t.Handle(Hit(Me, 500, 0), Me);
        Assert.Equal(1, t.Stats().Total);
        t.Handle("in 3 333 500 10 10 2 100 100 0 0 0 -1 1 0", Me);
        t.Handle(Hit(Me, 500, 0), Me);
        Assert.Equal(2, t.Stats().Total);
    }

    [Fact]
    public void MonsterNotSeenYetStaysUnknown()
    {
        var t = new KillTracker();
        t.Handle(Hit(Me, 500, 0), Me);
        Assert.Equal(KillTracker.UnknownMonster, Assert.Single(t.Stats().Monsters).VNum);
    }

    [Fact]
    public void UnknownCharacterCountsNothing()
    {
        var t = new KillTracker();
        t.Handle(Hit(Me, 500, 0), null);
        Assert.Equal(0, t.Stats().Total);
    }

    [Fact]
    public void ResetClearsKills()
    {
        var t = new KillTracker();
        t.Handle(Hit(Me, 500, 0), Me);
        t.Reset();
        Assert.Equal(0, t.Stats().Total);
    }

    [Fact]
    public void SessionKnowsMonstersSeenBeforeItsStart()
    {
        var live = new KillTracker();
        live.Handle("in 3 333 500 10 10 2 100 100 0 0 0 -1 1 0", Me);
        var s = new Session(DateTime.Now, null, new DamageMeter(), null, null, live);
        s.Handle(Hit(Me, 500, 0), DateTime.Now, Me);
        Assert.Equal(333, Assert.Single(s.Stats(DateTime.Now).Kills.Monsters).VNum);
        Assert.Equal(0, live.Stats().Total);
    }
}
