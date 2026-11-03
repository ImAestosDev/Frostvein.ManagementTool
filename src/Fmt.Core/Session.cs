namespace Fmt.Core;

public sealed record SessionStats(bool Running, TimeSpan Duration, DamageStats Damage, ExpStats Exp, DropStats Drops);

// All counters of one run, separate from the counters in their own tabs.
public sealed class Session
{
    private readonly DamageMeter _damage;
    private readonly ExpTracker _exp = new();
    private readonly DropTracker _drops = new();
    private readonly DateTime _start;
    private DateTime? _end;

    public Session(DateTime now, string? lastLev, DamageMeter damage, DropTracker? liveDrops)
    {
        _start = now;
        _damage = damage;
        // The last known progress is the baseline, so the first kill counts already.
        if (lastLev != null) _exp.Handle(lastLev, now);
        _exp.StartRecording(now);
        if (liveDrops != null) _drops.TakeOpenFrom(liveDrops);
    }

    public bool Running => _end == null;

    public void Handle(string packet, DateTime now, long? ownId)
    {
        if (!Running) return;
        if (ownId is { } own && _damage.OwnId != own) _damage.SetOwn(own);
        _damage.Handle(packet, now);
        _exp.Handle(packet, now);
        _drops.Handle(packet, ownId);
    }

    public void End(DateTime now)
    {
        if (!Running) return;
        _end = now;
        _exp.StopRecording(now);
    }

    public SessionStats Stats(DateTime now) =>
        new(Running, (_end ?? now) - _start, _damage.Stats(), _exp.Stats(now), _drops.Stats());
}
