using Fmt.Core;
using Xunit;

namespace Fmt.Tests;

public class DropTests
{
    private const long Me = 77;

    [Fact]
    public void CountsOnlyWhatIPickUp()
    {
        var t = new DropTracker();
        t.Handle("drop 1012 10 20 30 3 0 77", Me);
        t.Handle("drop 1012 11 20 30 5 0 77", Me);
        t.Handle("get 1 77 10 0", Me);
        t.Handle("get 1 99 11 0", Me);
        var item = Assert.Single(t.Stats().Items);
        Assert.Equal(1012, item.VNum);
        Assert.Equal(3, item.Amount);
    }

    [Fact]
    public void StacksTheSameItemInPickupOrder()
    {
        var t = new DropTracker();
        foreach (var (vnum, id) in new[] { (2282, 1), (1012, 2), (2282, 3) })
        {
            t.Handle($"drop {vnum} {id} 1 1 2 0 -1", Me);
            t.Handle($"get 1 77 {id} 0", Me);
        }
        var items = t.Stats().Items;
        Assert.Equal([2282, 1012], items.Select(i => i.VNum));
        Assert.Equal(4, items[0].Amount);
    }

    [Fact]
    public void GoldGoesToTheGoldTotal()
    {
        var t = new DropTracker();
        t.Handle("drop 1046 1 1 1 5000 0 -1", Me);
        t.Handle("get 1 77 1 0", Me);
        Assert.Equal(5000, t.Stats().Gold);
        Assert.Empty(t.Stats().Items);
    }

    [Fact]
    public void PetPickupCountsOnlyForMyDrops()
    {
        var t = new DropTracker();
        t.Handle("drop 1012 1 1 1 1 0 77", Me);
        t.Handle("drop 1012 2 1 1 1 0 99", Me);
        t.Handle("get 2 3071 1 0", Me);
        t.Handle("get 2 3071 2 0", Me);
        Assert.Equal(1, Assert.Single(t.Stats().Items).Amount);
    }

    [Fact]
    public void AGetCountsOnce()
    {
        var t = new DropTracker();
        t.Handle("drop 1012 1 1 1 1 0 -1", Me);
        t.Handle("get 1 77 1 0", Me);
        t.Handle("get 1 77 1 0", Me);
        Assert.Equal(1, Assert.Single(t.Stats().Items).Amount);
    }

    [Fact]
    public void UnknownCharacterCountsNothing()
    {
        var t = new DropTracker();
        t.Handle("drop 1012 1 1 1 1 0 -1", null);
        t.Handle("get 1 77 1 0", null);
        Assert.Empty(t.Stats().Items);
    }

    [Fact]
    public void ResetClearsItemsAndGold()
    {
        var t = new DropTracker();
        t.Handle("drop 1012 1 1 1 1 0 -1", Me);
        t.Handle("get 1 77 1 0", Me);
        t.Handle("drop 1046 2 1 1 9 0 -1", Me);
        t.Handle("get 1 77 2 0", Me);
        t.Reset();
        Assert.Empty(t.Stats().Items);
        Assert.Equal(0, t.Stats().Gold);
    }

    [Theory]
    [InlineData("drop")]
    [InlineData("drop x 1 1 1 1 0 -1")]
    [InlineData("get 1 77")]
    [InlineData("get 1 77 5 0")]
    [InlineData("dropx 1012 1 1 1 1 0 -1")]
    public void BadPacketsAreIgnored(string packet)
    {
        var t = new DropTracker();
        t.Handle(packet, Me);
        Assert.Empty(t.Stats().Items);
    }
}
