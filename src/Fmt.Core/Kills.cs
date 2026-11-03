using System.Globalization;

namespace Fmt.Core;

public sealed record MonsterKills(int VNum, int Count);

public sealed record KillStats(IReadOnlyList<MonsterKills> Monsters, int Total)
{
    public static readonly KillStats Empty = new([], 0);
}

// Counts the monsters the own character finishes off: the last "su" hit reports them dead.
public sealed class KillTracker
{
    public const int UnknownMonster = -1;
    private const string PlayerType = "1", MonsterType = "3", DeadFlag = "0";
    private const int InVNum = 2, InId = 3;
    private const int SuAttackerType = 1, SuAttacker = 2, SuTargetType = 3, SuTarget = 4, SuAlive = 11;

    private readonly Dictionary<long, int> _monsters = [];
    private readonly HashSet<long> _dead = [];
    private readonly Dictionary<int, int> _kills = [];

    public void Handle(string packet, long? ownId)
    {
        if (packet.StartsWith("in 3 ", StringComparison.Ordinal)) HandleIn(packet);
        else if (packet.StartsWith("su 1 ", StringComparison.Ordinal)) HandleHit(packet, ownId);
    }

    private void HandleIn(string packet)
    {
        var t = packet.Split(' ');
        if (t.Length <= InId
            || !int.TryParse(t[InVNum], NumberStyles.None, CultureInfo.InvariantCulture, out var vnum)
            || !long.TryParse(t[InId], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return;
        _monsters[id] = vnum;
        // A respawn may reuse the id.
        _dead.Remove(id);
    }

    private void HandleHit(string packet, long? ownId)
    {
        var t = packet.Split(' ');
        if (t.Length <= SuAlive || ownId is not { } own
            || t[SuAttackerType] != PlayerType || t[SuAttacker] != own.ToString(CultureInfo.InvariantCulture)
            || t[SuTargetType] != MonsterType || t[SuAlive] != DeadFlag
            || !long.TryParse(t[SuTarget], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return;
        // Multi hit skills report the same death several times.
        if (!_dead.Add(id)) return;
        var vnum = _monsters.GetValueOrDefault(id, UnknownMonster);
        _kills[vnum] = _kills.GetValueOrDefault(vnum) + 1;
    }

    public void TakeKnownFrom(KillTracker other)
    {
        foreach (var (id, vnum) in other._monsters) _monsters[id] = vnum;
        foreach (var id in other._dead) _dead.Add(id);
    }

    public void Reset() => _kills.Clear();

    public KillStats Stats() => new(
        _kills.Select(k => new MonsterKills(k.Key, k.Value)).OrderByDescending(k => k.Count).ThenBy(k => k.VNum).ToList(),
        _kills.Values.Sum());
}
