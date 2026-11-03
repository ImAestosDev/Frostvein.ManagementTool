using Fmt.Core;
using Xunit;

namespace Fmt.Tests;

public class ItemUseTests
{
    private const long Me = 77;
    private const int Potion = 1242, Angel = 2329, Other = 1012, Blessing = 229;
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0);

    private static ItemUseTracker New() => new(new HashSet<int> { Potion, Angel }, new HashSet<int> { Blessing });

    private static int Used(ItemUseTracker t, int vnum, DateTime now) => t.Used(now).GetValueOrDefault(vnum);

    [Fact]
    public void ShrinkingStackCountsAfterTheMoveWindow()
    {
        var t = New();
        t.Handle($"inv 1 3.{Potion}.50.0 4.{Other}.5.0", Me, T0);
        t.Handle($"ivn 1 3.{Potion}.49.0", Me, T0);
        Assert.Equal(0, Used(t, Potion, T0));
        t.Handle($"ivn 1 3.{Potion}.48.0", Me, T0.AddSeconds(5));
        Assert.Equal(2, Used(t, Potion, T0.AddSeconds(10)));
        Assert.True(t.InventoryKnown);
    }

    [Fact]
    public void LastItemOfAStackCounts()
    {
        var t = New();
        t.Handle($"inv 2 7.{Angel}.1.0", Me, T0);
        t.Handle("ivn 2 7.0.0.0.0", Me, T0);
        Assert.Equal(1, Used(t, Angel, T0.AddSeconds(5)));
    }

    [Fact]
    public void MovingAStackCountsNothing()
    {
        var t = New();
        t.Handle($"inv 1 3.{Potion}.50.0", Me, T0);
        t.Handle("ivn 1 3.0.0.0.0", Me, T0);
        t.Handle($"ivn 1 9.{Potion}.50.0", Me, T0);
        Assert.Equal(0, Used(t, Potion, T0.AddSeconds(5)));
    }

    [Fact]
    public void MovingOntoANewSlotFirstCountsNothing()
    {
        var t = New();
        t.Handle($"inv 1 3.{Potion}.50.0", Me, T0);
        t.Handle($"ivn 1 9.{Potion}.50.0", Me, T0);
        t.Handle("ivn 1 3.0.0.0.0", Me, T0);
        Assert.Equal(0, Used(t, Potion, T0.AddSeconds(5)));
    }

    [Fact]
    public void UseRightAfterTheGainWindowCountsAgain()
    {
        var t = New();
        t.Handle($"inv 1 3.{Potion}.50.0", Me, T0);
        t.Handle($"ivn 1 3.{Potion}.60.0", Me, T0);
        t.Handle($"ivn 1 3.{Potion}.59.0", Me, T0.AddSeconds(5));
        Assert.Equal(1, Used(t, Potion, T0.AddSeconds(10)));
    }

    [Fact]
    public void OtherItemsAndGainsCountNothing()
    {
        var t = New();
        t.Handle($"inv 1 3.{Potion}.50.0 4.{Other}.5.0", Me, T0);
        t.Handle($"ivn 1 4.{Other}.1.0", Me, T0);
        t.Handle($"ivn 1 3.{Potion}.60.0", Me, T0);
        Assert.Empty(t.Used(T0.AddSeconds(5)));
    }

    [Fact]
    public void UnknownSlotOnlySetsTheBaseline()
    {
        var t = New();
        t.Handle($"ivn 1 3.{Potion}.50.0", Me, T0);
        Assert.False(t.InventoryKnown);
        t.Handle($"ivn 1 3.{Potion}.49.0", Me, T0);
        Assert.Equal(1, Used(t, Potion, T0.AddSeconds(5)));
    }

    [Fact]
    public void BuffRunsDownAndEnds()
    {
        var t = New();
        t.Handle($"bf 1 77 0.{Blessing}.36000 90", Me, T0);
        Assert.Equal(TimeSpan.FromMinutes(50), Assert.Single(t.Buffs(T0.AddMinutes(10))).Remaining);
        t.Handle($"bf 1 77 0.{Blessing}.0 90", Me, T0.AddMinutes(11));
        Assert.Empty(t.Buffs(T0.AddMinutes(11)));
    }

    [Fact]
    public void ExpiredOtherAndUnwatchedBuffsStayOut()
    {
        var t = New();
        t.Handle($"bf 1 99 0.{Blessing}.36000 90", Me, T0);
        t.Handle("bf 1 77 0.555.36000 90", Me, T0);
        Assert.Empty(t.Buffs(T0));
        t.Handle($"bf 1 77 0.{Blessing}.100 90", Me, T0);
        Assert.Empty(t.Buffs(T0.AddSeconds(11)));
    }

    [Fact]
    public void SessionOnlyCountsItsOwnTime()
    {
        var t = New();
        t.Handle($"inv 1 3.{Potion}.50.0", Me, T0);
        t.Handle($"ivn 1 3.{Potion}.49.0", Me, T0);
        var start = T0.AddSeconds(5);
        var s = new Session(start, null, new DamageMeter(), null, t);
        t.Handle($"ivn 1 3.{Potion}.47.0", Me, start);
        var end = start.AddSeconds(5);
        s.End(end);
        t.Handle($"ivn 1 3.{Potion}.40.0", Me, end);

        var stats = s.Stats(end.AddSeconds(5));
        Assert.Equal(2, Assert.Single(stats.Used).Count);
        Assert.Empty(stats.Buffs);
    }

    private static ItemUseTracker Restored(int amount)
    {
        var t = New();
        t.Restore([new SavedStack("1", 3, Potion, amount)]);
        return t;
    }

    [Fact]
    public void RestoredInventoryCountsTheFirstUse()
    {
        var t = Restored(50);
        Assert.True(t.InventoryKnown);
        t.Handle($"ivn 1 3.{Potion}.49.0", Me, T0);
        Assert.Equal(1, Used(t, Potion, T0.AddSeconds(5)));
    }

    [Fact]
    public void RestoredLastItemCounts()
    {
        var t = Restored(1);
        t.Handle("ivn 1 3.0.0.0.0", Me, T0);
        Assert.Equal(1, Used(t, Potion, T0.AddSeconds(5)));
    }

    [Fact]
    public void OutdatedSlotOnlyUpdatesTheBaseline()
    {
        var t = Restored(50);
        t.Handle($"ivn 1 3.{Potion}.30.0", Me, T0);
        Assert.Equal(0, Used(t, Potion, T0.AddSeconds(5)));
        t.Handle($"ivn 1 3.{Potion}.29.0", Me, T0.AddSeconds(5));
        Assert.Equal(1, Used(t, Potion, T0.AddSeconds(10)));
    }

    [Fact]
    public void OutdatedEmptySlotCountsNothing()
    {
        var t = Restored(50);
        t.Handle("ivn 1 3.0.0.0.0", Me, T0);
        Assert.Equal(0, Used(t, Potion, T0.AddSeconds(5)));
    }

    [Fact]
    public void OtherCharacterForgetsTheInventory()
    {
        var t = New();
        t.Handle($"inv 1 3.{Potion}.50.0", Me, T0);
        t.Handle("mv 3 1 1 1 1", 99, T0);
        Assert.False(t.InventoryKnown);
        Assert.Equal(99, t.Owner);
        t.Handle($"ivn 1 3.{Potion}.49.0", 99, T0);
        Assert.Equal(0, Used(t, Potion, T0.AddSeconds(5)));
    }

    [Fact]
    public void InventoryBookSurvivesARestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "fmt-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var t = New();
            t.Handle($"inv 1 3.{Potion}.50.0", Me, T0);
            Assert.True(t.InventoryChanged);
            var book = new InventoryBook();
            book.Set(Me, t.ExportInventory());
            Assert.False(t.InventoryChanged);
            book.Save(path);

            Assert.True(InventoryBook.Load(path).TryGet(Me, out var stacks));
            var later = New();
            later.Restore(stacks);
            later.Handle($"ivn 1 3.{Potion}.49.0", Me, T0);
            Assert.Equal(1, Used(later, Potion, T0.AddSeconds(5)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CounterResetLeavesSessionsAlone()
    {
        var t = New();
        t.Handle($"inv 1 3.{Potion}.50.0", Me, T0);
        var s = new Session(T0, null, new DamageMeter(), null, t);
        t.Handle($"ivn 1 3.{Potion}.48.0", Me, T0);
        var later = T0.AddSeconds(5);
        Assert.Equal(2, Assert.Single(t.Counter(later).Used).Count);

        t.ResetCounter(later);
        Assert.Empty(t.Counter(later).Used);
        Assert.Equal(2, Assert.Single(s.Stats(later).Used).Count);

        t.Handle($"ivn 1 3.{Potion}.47.0", Me, later);
        Assert.Equal(1, Assert.Single(t.Counter(later.AddSeconds(5)).Used).Count);
    }

    [Theory]
    [InlineData("Ancelloan Segen (Event)", true)]
    [InlineData("Angriffstrank (TA)", true)]
    [InlineData("Großes Eisblumenöl", true)]
    [InlineData("Eisblumenkristall", false)]
    [InlineData("Erhöhte Schutzengel-Einsparung", false)]
    public void WatchListMatchesVariants(string name, bool expected) => Assert.Equal(expected, WatchedItems.Contains(name));
}
