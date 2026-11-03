using System.Globalization;

namespace Fmt.Core;

public sealed record ExpTrack(string Label, int Level, long Xp, long Load, long Gained, long LastGain, double GainedPercent, int LevelsGained, long? PerHour, TimeSpan? TimeToLevel)
{
    public double Percent => Math.Clamp(Xp * 100.0 / Load, 0, 100);
}

public sealed record ExpStats(IReadOnlyList<ExpTrack> Tracks, TimeSpan Elapsed, string? LastPacket)
{
    public static readonly ExpStats Empty = new([], TimeSpan.Zero, null);
}

// Follows level, job or SP and hero progress from "lev" packets.
public sealed class ExpTracker
{
    private const int LevLevel = 1, LevXp = 2, LevJobLevel = 3, LevJobXp = 4, LevLoad = 5, LevJobLoad = 6, LevHeroXp = 9, LevHeroLevel = 10, LevHeroLoad = 11;
    private static readonly TimeSpan MinRateTime = TimeSpan.FromMinutes(1);

    private sealed class Tally(string label)
    {
        public readonly string Label = label;
        public bool Seen;
        public int Level, LevelsGained;
        public long Xp, Load, Gained, LastGain;
        public double GainedPercent;

        public void Clear()
        {
            Gained = LastGain = 0;
            GainedPercent = 0;
            LevelsGained = 0;
        }
    }

    private readonly Tally _level = new("Level"), _job = new("Job / SP"), _hero = new("Held");
    private DateTime? _since;
    private string? _lastPacket;

    public void Handle(string packet, DateTime now)
    {
        if (!packet.StartsWith("lev ", StringComparison.Ordinal)) return;
        var t = packet.Split(' ');
        if (t.Length <= LevJobLoad) return;
        _lastPacket = packet;
        _since ??= now;
        Update(_level, t[LevLevel], t[LevXp], t[LevLoad]);
        Update(_job, t[LevJobLevel], t[LevJobXp], t[LevJobLoad]);
        if (t.Length > LevHeroLoad) Update(_hero, t[LevHeroLevel], t[LevHeroXp], t[LevHeroLoad]);
    }

    private static void Update(Tally p, string levelText, string xpText, string loadText)
    {
        if (!int.TryParse(levelText, NumberStyles.None, CultureInfo.InvariantCulture, out var level) || level <= 0
            || !long.TryParse(xpText, NumberStyles.None, CultureInfo.InvariantCulture, out var xp)
            || !long.TryParse(loadText, NumberStyles.None, CultureInfo.InvariantCulture, out var load) || load <= 0) return;

        if (p.Seen)
        {
            var gain = 0L;
            var percent = 0.0;
            if (level == p.Level && load == p.Load && xp > p.Xp)
            {
                gain = xp - p.Xp;
                percent = gain * 100.0 / load;
            }
            else if (level == p.Level + 1 && p.Load > p.Xp)
            {
                gain = p.Load - p.Xp + xp;
                percent = (p.Load - p.Xp) * 100.0 / p.Load + xp * 100.0 / load;
                p.LevelsGained++;
            }
            // Anything else is a new baseline, e.g. an SP swap or an XP loss.
            if (gain > 0)
            {
                p.Gained += gain;
                p.LastGain = gain;
                p.GainedPercent += percent;
            }
        }
        p.Seen = true;
        p.Level = level;
        p.Xp = xp;
        p.Load = load;
    }

    public void Reset(DateTime now)
    {
        _level.Clear();
        _job.Clear();
        _hero.Clear();
        _since = now;
    }

    public ExpStats Stats(DateTime now)
    {
        var elapsed = _since is { } since ? now - since : TimeSpan.Zero;
        var tracks = new[] { _level, _job, _hero }.Where(p => p.Seen).Select(p => ToTrack(p, elapsed)).ToList();
        return new ExpStats(tracks, elapsed, _lastPacket);
    }

    private static ExpTrack ToTrack(Tally p, TimeSpan elapsed)
    {
        long? perHour = null;
        TimeSpan? timeToLevel = null;
        if (elapsed >= MinRateTime && p.Gained > 0)
        {
            perHour = (long)(p.Gained / elapsed.TotalHours);
            var percentPerHour = p.GainedPercent / elapsed.TotalHours;
            var left = Math.Max(0, 100 - p.Xp * 100.0 / p.Load);
            timeToLevel = TimeSpan.FromHours(left / percentPerHour);
        }
        return new ExpTrack(p.Label, p.Level, p.Xp, p.Load, p.Gained, p.LastGain, p.GainedPercent, p.LevelsGained, perHour, timeToLevel);
    }
}
