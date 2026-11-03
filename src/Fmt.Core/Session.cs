namespace Fmt.Core;

public sealed record SessionStats(bool Running, TimeSpan Duration, DamageStats Damage, ExpStats Exp, DropStats Drops, IReadOnlyList<UsedItem> Used, IReadOnlyList<ActiveBuff> Buffs, bool InventoryKnown, KillStats Kills)
{
    public static readonly TimeSpan Limit = new(1, 0, 15);

    public bool TimeIsUp => Running && Duration >= Limit;
}

// All counters of one run, separate from the counters in their own tabs.
public sealed class Session
{
    private readonly DamageMeter _damage;
    private readonly ExpTracker _exp = new();
    private readonly DropTracker _drops = new();
    private readonly KillTracker _kills = new();
    private readonly ItemUseTracker? _uses;
    private readonly Dictionary<int, int> _usedAtStart;
    private readonly DateTime _start;
    private Dictionary<int, int>? _usedAtEnd;
    private DateTime? _end;

    public Session(DateTime now, string? lastLev, DamageMeter damage, DropTracker? liveDrops, ItemUseTracker? uses = null, KillTracker? liveKills = null)
    {
        _start = now;
        _damage = damage;
        _uses = uses;
        _usedAtStart = uses?.Used(now) ?? [];
        // The last known progress is the baseline, so the first kill counts already.
        if (lastLev != null) _exp.Handle(lastLev, now);
        _exp.StartRecording(now);
        if (liveDrops != null) _drops.TakeOpenFrom(liveDrops);
        if (liveKills != null) _kills.TakeKnownFrom(liveKills);
    }

    public bool Running => _end == null;

    public void Handle(string packet, DateTime now, long? ownId)
    {
        if (!Running) return;
        if (ownId is { } own && _damage.OwnId != own) _damage.SetOwn(own);
        _damage.Handle(packet, now);
        _exp.Handle(packet, now);
        _drops.Handle(packet, ownId);
        _kills.Handle(packet, ownId);
    }

    public void End(DateTime now)
    {
        if (!Running) return;
        _end = now;
        _exp.StopRecording(now);
        _usedAtEnd = _uses?.Used(now) ?? [];
    }

    public SessionStats Stats(DateTime now)
    {
        var used = ItemUseTracker.Since(_usedAtEnd ?? _uses?.Used(now) ?? [], _usedAtStart);
        IReadOnlyList<ActiveBuff> buffs = Running && _uses != null ? _uses.Buffs(now) : [];
        return new SessionStats(Running, (_end ?? now) - _start, _damage.Stats(), _exp.Stats(now), _drops.Stats(), used, buffs, _uses?.InventoryKnown ?? false, _kills.Stats());
    }
}
