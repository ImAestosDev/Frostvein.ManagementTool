using System.Globalization;

namespace Fmt.Core;

public sealed record UsedItem(int VNum, int Count);

public sealed record ActiveBuff(int CardId, TimeSpan Remaining);

// Names of the items and buffs worth following; variants share the base name.
public static class WatchedItems
{
    private static readonly HashSet<string> Names =
    [
        "Göttlicher Heiltrank", "Göttlicher Manatrank", "Göttlicher Erholungstrank", "Cylloan Quellwasser",
        "Riesiger Erholungstrank", "Dampf-Heiltrank", "Dampf-Manatrank", "Dampf-Erholungstrank",
        "Drachenheiltrank", "Drachenmanatrank", "Drachenerholungstrank",
        "Partner-Schutzengel", "NosMate-Schutzengel", "Mächtiger Partner-Schutzengel", "Mächtiger NosMate-Schutzengel",
        "Feenverstärker", "Ancelloan Segen", "Segen des Schutzengels", "Segen des Seelensteins",
        "Eisblumenöl", "Großes Eisblumenöl", "Hitzewiderstandstrank", "Großer Hitzewiderstandstrank",
        "Erfahrungstrank", "Angriffstrank", "Verteidigungstrank", "Energietrank",
    ];

    public static string BaseName(string name)
    {
        foreach (var suffix in new[] { " (Event)", " (TA)" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return name[..^suffix.Length];
        }
        return name;
    }

    public static bool Contains(string name) => Names.Contains(BaseName(name));
}

// Follows main and etc inventory plus own buffs. The game sends no "item used" packet, so a shrinking stack counts.
public sealed class ItemUseTracker(IReadOnlySet<int> items, IReadOnlySet<int> cards)
{
    private const string MainInventory = "1", EtcInventory = "2";
    private const int BuffOwner = 2, BuffData = 3;
    private const double TicksPerSecond = 10;
    // Moving a stack shows up as gone and back again.
    private static readonly TimeSpan MoveWindow = TimeSpan.FromSeconds(1.5);

    private sealed record Pending(int VNum, DateTime At)
    {
        public int Count { get; set; }
    }

    private readonly record struct Stack(int VNum, int Amount);

    private readonly Dictionary<(string Type, int Slot), Stack> _slots = [];
    private readonly Dictionary<int, int> _used = [];
    private readonly List<Pending> _pending = [];
    private readonly List<Pending> _gains = [];
    private readonly Dictionary<int, DateTime> _buffs = [];

    public bool InventoryKnown { get; private set; }

    public void Handle(string packet, long? ownId, DateTime now)
    {
        Flush(now);
        if (packet.StartsWith("inv ", StringComparison.Ordinal)) HandleFull(packet);
        else if (packet.StartsWith("ivn ", StringComparison.Ordinal)) HandleChange(packet, now);
        else if (packet.StartsWith("bf 1 ", StringComparison.Ordinal)) HandleBuff(packet, ownId, now);
    }

    private static bool Tracked(string type) => type is MainInventory or EtcInventory;

    private static bool TryEntry(string entry, out int slot, out Stack stack)
    {
        stack = default;
        var parts = entry.Split('.');
        slot = 0;
        if (parts.Length < 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            || !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var vnum)
            || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)) return false;
        stack = new Stack(vnum, amount);
        return true;
    }

    private void HandleFull(string packet)
    {
        var t = packet.Split(' ');
        if (t.Length < 2 || !Tracked(t[1])) return;
        foreach (var key in _slots.Keys.Where(k => k.Type == t[1]).ToList()) _slots.Remove(key);
        foreach (var entry in t.Skip(2))
        {
            if (TryEntry(entry, out var slot, out var stack) && stack.VNum > 0 && stack.Amount > 0) _slots[(t[1], slot)] = stack;
        }
        InventoryKnown = true;
    }

    private void HandleChange(string packet, DateTime now)
    {
        var t = packet.Split(' ');
        if (t.Length < 3 || !Tracked(t[1]) || !TryEntry(t[2], out var slot, out var next)) return;
        var key = (t[1], slot);
        var empty = next.VNum <= 0 || next.Amount <= 0;
        var known = _slots.TryGetValue(key, out var old);
        if (empty) _slots.Remove(key);
        else _slots[key] = next;

        if (known && (empty || old.VNum != next.VNum)) Lost(old.VNum, old.Amount, now);
        else if (known && next.Amount < old.Amount) Lost(old.VNum, old.Amount - next.Amount, now);

        if (!empty)
        {
            if (known && old.VNum == next.VNum && next.Amount > old.Amount) Gained(next.VNum, next.Amount - old.Amount, now);
            // An unknown slot is only a baseline when the inventory is unknown too.
            else if ((!known && InventoryKnown) || (known && old.VNum != next.VNum)) Gained(next.VNum, next.Amount, now);
        }
    }

    private void Lost(int vnum, int count, DateTime now)
    {
        if (!items.Contains(vnum)) return;
        count = Offset(_gains, vnum, count);
        if (count > 0) _pending.Add(new Pending(vnum, now) { Count = count });
    }

    private void Gained(int vnum, int count, DateTime now)
    {
        if (!items.Contains(vnum)) return;
        count = Offset(_pending, vnum, count);
        if (count > 0) _gains.Add(new Pending(vnum, now) { Count = count });
    }

    // Both orders of a move cancel out: gone then back, or back then gone.
    private static int Offset(List<Pending> open, int vnum, int count)
    {
        foreach (var entry in open.Where(p => p.VNum == vnum))
        {
            var cancelled = Math.Min(count, entry.Count);
            entry.Count -= cancelled;
            count -= cancelled;
        }
        open.RemoveAll(p => p.Count == 0);
        return count;
    }

    private void Flush(DateTime now)
    {
        foreach (var pending in _pending.Where(p => now - p.At >= MoveWindow))
            _used[pending.VNum] = _used.GetValueOrDefault(pending.VNum) + pending.Count;
        _pending.RemoveAll(p => now - p.At >= MoveWindow);
        _gains.RemoveAll(p => now - p.At >= MoveWindow);
    }

    private void HandleBuff(string packet, long? ownId, DateTime now)
    {
        var t = packet.Split(' ');
        if (t.Length <= BuffData || ownId is not { } own || t[BuffOwner] != own.ToString(CultureInfo.InvariantCulture)) return;
        var data = t[BuffData].Split('.');
        if (data.Length < 3
            || !int.TryParse(data[1], NumberStyles.None, CultureInfo.InvariantCulture, out var card)
            || !long.TryParse(data[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ticks)
            || !cards.Contains(card)) return;
        if (ticks > 0) _buffs[card] = now.AddSeconds(ticks / TicksPerSecond);
        else _buffs.Remove(card);
    }

    public Dictionary<int, int> Used(DateTime now)
    {
        Flush(now);
        return new Dictionary<int, int>(_used);
    }

    public List<ActiveBuff> Buffs(DateTime now) =>
        _buffs.Where(b => b.Value > now).Select(b => new ActiveBuff(b.Key, b.Value - now)).OrderBy(b => b.Remaining).ToList();
}
