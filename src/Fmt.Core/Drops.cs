using System.Globalization;

namespace Fmt.Core;

public sealed record DroppedItem(int VNum, long Amount);

public sealed record DropStats(IReadOnlyList<DroppedItem> Items, long Gold)
{
    public static readonly DropStats Empty = new([], 0);
}

// Counts what the own character picks up: "drop" announces it, "get" hands it over.
public sealed class DropTracker
{
    public const int GoldVNum = 1046;
    private const int DropVNum = 1, DropId = 2, DropAmount = 5, DropOwner = 7;
    private const int GetKind = 1, GetPicker = 2, GetId = 3;
    private const string PlayerPicker = "1", PetPicker = "2";
    private const int MaxOpen = 5000;

    private readonly record struct OpenDrop(int VNum, long Amount, long? Owner);

    private readonly Dictionary<long, OpenDrop> _open = [];
    private readonly List<int> _order = [];
    private readonly Dictionary<int, long> _items = [];
    private long _gold;

    public void Handle(string packet, long? ownId)
    {
        if (packet.StartsWith("drop ", StringComparison.Ordinal)) HandleDrop(packet);
        else if (packet.StartsWith("get ", StringComparison.Ordinal)) HandleGet(packet, ownId);
    }

    private void HandleDrop(string packet)
    {
        var t = packet.Split(' ');
        if (t.Length <= DropAmount
            || !int.TryParse(t[DropVNum], NumberStyles.None, CultureInfo.InvariantCulture, out var vnum)
            || !long.TryParse(t[DropId], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || !long.TryParse(t[DropAmount], NumberStyles.None, CultureInfo.InvariantCulture, out var amount)) return;
        long? owner = t.Length > DropOwner && long.TryParse(t[DropOwner], NumberStyles.None, CultureInfo.InvariantCulture, out var o) ? o : null;
        // Drops others picked up never get a matching "get" here.
        if (_open.Count >= MaxOpen) _open.Clear();
        _open[id] = new OpenDrop(vnum, amount, owner);
    }

    private void HandleGet(string packet, long? ownId)
    {
        var t = packet.Split(' ');
        if (t.Length <= GetId || !long.TryParse(t[GetId], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return;
        if (!_open.Remove(id, out var drop) || ownId is not { } own) return;
        var mine = t[GetKind] switch
        {
            PlayerPicker => t[GetPicker] == own.ToString(CultureInfo.InvariantCulture),
            // A pet carries no owner of its own, only the drop does.
            PetPicker => drop.Owner == own,
            _ => false,
        };
        if (!mine) return;

        if (drop.VNum == GoldVNum)
        {
            _gold += drop.Amount;
            return;
        }
        if (!_items.ContainsKey(drop.VNum)) _order.Add(drop.VNum);
        _items[drop.VNum] = _items.GetValueOrDefault(drop.VNum) + drop.Amount;
    }

    public void TakeOpenFrom(DropTracker other)
    {
        foreach (var (id, drop) in other._open) _open[id] = drop;
    }

    public void Reset()
    {
        _order.Clear();
        _items.Clear();
        _gold = 0;
    }

    public DropStats Stats() => new(_order.Select(v => new DroppedItem(v, _items[v])).ToList(), _gold);
}
