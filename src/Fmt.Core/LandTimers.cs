using System.Globalization;

namespace Fmt.Core;

public enum AsgobasPhase { Unknown, Present, Waiting }

public sealed record AsgobasTimer(AsgobasPhase Phase, TimeSpan Remaining, TimeSpan Total)
{
    public double Fraction => Total > TimeSpan.Zero ? Math.Clamp(Remaining / Total, 0, 1) : 0;
}

public sealed record DragonSlot(long? Id, bool Dead, TimeSpan Remaining, TimeSpan Total)
{
    public double Fraction => Total > TimeSpan.Zero ? Math.Clamp(Remaining / Total, 0, 1) : 0;
}

public sealed record LandStats(bool InLand, AsgobasTimer Asgobas, IReadOnlyList<DragonSlot> GoldDragons, IReadOnlyList<DragonSlot> HolyDragons)
{
    public static readonly LandStats Empty = Build(false);

    private static LandStats Build(bool inLand)
    {
        var free = Enumerable.Repeat(new DragonSlot(null, false, TimeSpan.Zero, TimeSpan.Zero), LandTracker.DragonsPerKind).ToList();
        return new LandStats(inLand, new AsgobasTimer(AsgobasPhase.Unknown, TimeSpan.Zero, TimeSpan.Zero), free, free);
    }
}

// Asgobas follows the game's messages, any new message overrides the running timer. Dragons run from their death.
public sealed class LandTracker
{
    public const int DragonsPerKind = 3;
    public static readonly TimeSpan PresentTime = TimeSpan.FromSeconds(60), WaitTime = TimeSpan.FromMinutes(4);

    private const int MsgiType = 1, MsgiId = 2;
    private const string SpawnA = "2568", SpawnB = "2570", DespawnA = "2566", DespawnB = "2572";
    private const string MonsterType = "3", DeadFlag = "0";
    private const int InVNum = 2, InId = 3;
    private const int SuTargetType = 3, SuTarget = 4, SuAlive = 11;
    private const int DieTargetType = 3, DieTarget = 4;
    // Both versions of the area, so the names stay the same in either.
    private static readonly HashSet<int> GoldVNums = [809, 1481], HolyVNums = [1461, 1482];

    private sealed class Dragon(long id)
    {
        public readonly long Id = id;
        public DateTime? DiedAt;
        public DateTime SeenAt;
    }

    private readonly List<Dragon> _gold = [], _holy = [];
    private AsgobasPhase _phase;
    private DateTime _since;
    private bool _inLand;

    public void Handle(string packet, DateTime now)
    {
        if (packet.StartsWith("msgi ", StringComparison.Ordinal)) HandleMessage(packet, now);
        else if (packet.StartsWith("in 3 ", StringComparison.Ordinal)) HandleIn(packet, now);
        else if (packet.StartsWith("su ", StringComparison.Ordinal)) HandleHit(packet, now);
        else if (packet.StartsWith("die ", StringComparison.Ordinal)) HandleDie(packet, now);
        else if (packet.StartsWith("c_map ", StringComparison.Ordinal)) _inLand = false;
    }

    private void HandleMessage(string packet, DateTime now)
    {
        var t = packet.Split(' ');
        // Normally "msgi type id ...", the id alone also counts.
        if (t.Length <= MsgiType) return;
        var id = t.Length > MsgiId && IsAsgobas(t[MsgiId]) ? t[MsgiId] : t[MsgiType];
        if (id is SpawnA or SpawnB) Start(AsgobasPhase.Present, now);
        else if (id is DespawnA or DespawnB) Start(AsgobasPhase.Waiting, now);
    }

    private static bool IsAsgobas(string id) => id is SpawnA or SpawnB or DespawnA or DespawnB;

    private void Start(AsgobasPhase phase, DateTime now)
    {
        _phase = phase;
        _since = now;
        _inLand = true;
    }

    private void HandleIn(string packet, DateTime now)
    {
        var t = packet.Split(' ');
        if (t.Length <= InId
            || !int.TryParse(t[InVNum], NumberStyles.None, CultureInfo.InvariantCulture, out var vnum)
            || !long.TryParse(t[InId], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return;
        var kind = GoldVNums.Contains(vnum) ? _gold : HolyVNums.Contains(vnum) ? _holy : null;
        if (kind == null) return;
        _inLand = true;

        var dragon = kind.Find(d => d.Id == id);
        if (dragon == null)
        {
            // A full list means another channel or area: its old dragons make room, living ones first.
            if (kind.Count == DragonsPerKind)
                kind.Remove(kind.OrderBy(d => d.DiedAt != null).ThenBy(d => d.SeenAt).First());
            kind.Add(dragon = new Dragon(id));
        }
        dragon.DiedAt = null;
        dragon.SeenAt = now;
    }

    private void HandleHit(string packet, DateTime now)
    {
        var t = packet.Split(' ');
        if (t.Length > SuAlive && t[SuTargetType] == MonsterType && t[SuAlive] == DeadFlag) Died(t[SuTarget], now);
    }

    private void HandleDie(string packet, DateTime now)
    {
        var t = packet.Split(' ');
        if (t.Length > DieTarget && t[DieTargetType] == MonsterType) Died(t[DieTarget], now);
    }

    private void Died(string target, DateTime now)
    {
        if (!long.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return;
        // The first report counts: multi hit skills repeat the death.
        var dragon = _gold.Find(d => d.Id == id) ?? _holy.Find(d => d.Id == id);
        if (dragon != null) dragon.DiedAt ??= now;
    }

    public LandStats Stats(DateTime now, TimeSpan goldRespawn, TimeSpan holyRespawn)
    {
        var total = _phase == AsgobasPhase.Present ? PresentTime : WaitTime;
        var asgobas = _phase == AsgobasPhase.Unknown
            ? new AsgobasTimer(AsgobasPhase.Unknown, TimeSpan.Zero, TimeSpan.Zero)
            : new AsgobasTimer(_phase, total - (now - _since), total);
        return new LandStats(_inLand, asgobas, Slots(_gold, now, goldRespawn), Slots(_holy, now, holyRespawn));
    }

    private static List<DragonSlot> Slots(List<Dragon> dragons, DateTime now, TimeSpan respawn)
    {
        var slots = dragons.OrderBy(d => d.Id)
            .Select(d => new DragonSlot(d.Id, d.DiedAt != null, d.DiedAt is { } died ? respawn - (now - died) : TimeSpan.Zero, respawn))
            .ToList();
        while (slots.Count < DragonsPerKind) slots.Add(new DragonSlot(null, false, TimeSpan.Zero, TimeSpan.Zero));
        return slots;
    }
}
