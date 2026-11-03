namespace Fmt.Core;

public sealed record SessionStats(bool Running, TimeSpan Duration, DamageStats Damage, ExpStats Exp, DropStats Drops, IReadOnlyList<UsedItem> Used, IReadOnlyList<ActiveBuff> Buffs, bool InventoryKnown, KillStats Kills, bool TimeIsUp);

// All counters of one run, separate from the counters in their own tabs.
public sealed class Session
{
    public static readonly TimeSpan Limit = new(1, 0, 15);

    private readonly DamageMeter _damage;
    private readonly ExpTracker _exp = new();
    private readonly DropTracker _drops = new();
    private readonly KillTracker _kills = new();
    private readonly ItemUseTracker? _uses;
    private readonly Dictionary<int, int> _usedAtStart;
    private readonly DateTime _start;
    private Dictionary<int, int>? _usedAtEnd;
    private DateTime? _end;
    private bool _timeIsUp;

    public string Id { get; } = Ids.New();
    public bool Archived { get; set; }

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

    // Ends on the exact second, however late a packet or the display notices it.
    public void CheckLimit(DateTime now)
    {
        if (!Running || now - _start < Limit) return;
        _timeIsUp = true;
        Stop(_start + Limit);
    }

    public void Handle(string packet, DateTime now, long? ownId)
    {
        CheckLimit(now);
        if (!Running) return;
        if (ownId is { } own && _damage.OwnId != own) _damage.SetOwn(own);
        _damage.Handle(packet, now);
        _exp.Handle(packet, now);
        _drops.Handle(packet, ownId);
        _kills.Handle(packet, ownId);
    }

    public void End(DateTime now)
    {
        CheckLimit(now);
        if (Running) Stop(now);
    }

    private void Stop(DateTime now)
    {
        _end = now;
        _exp.StopRecording(now);
        _usedAtEnd = _uses?.Used(now) ?? [];
    }

    public SavedSession Snapshot(DateTime now)
    {
        var s = Stats(now);
        return new SavedSession(Id, _damage.OwnId == null ? "" : _damage.OwnName, _start, s.Duration, s.TimeIsUp, !s.Running,
            [.. s.Damage.Players], [.. s.Exp.Recording?.Rows ?? []], [.. s.Drops.Items], s.Drops.Gold, [.. s.Kills.Monsters], [.. s.Used]);
    }

    public SessionStats Stats(DateTime now)
    {
        CheckLimit(now);
        var used = ItemUseTracker.Since(_usedAtEnd ?? _uses?.Used(now) ?? [], _usedAtStart);
        IReadOnlyList<ActiveBuff> buffs = Running && _uses != null ? _uses.Buffs(now) : [];
        return new SessionStats(Running, (_end ?? now) - _start, _damage.Stats(), _exp.Stats(now), _drops.Stats(), used, buffs, _uses?.InventoryKnown ?? false, _kills.Stats(), _timeIsUp);
    }
}
