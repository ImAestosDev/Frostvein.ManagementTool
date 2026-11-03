using System.Globalization;

namespace Fmt.Core;

public sealed record ExpTrack(string Label, string Short, int Level, long Xp, long Load, double GainedPercent, int LevelsGained)
{
    public double Percent => Math.Clamp(Xp * 100.0 / Load, 0, 100);
}

public sealed record ExpRecordRow(string Label, double GainedPercent, double PercentPerHour);

public sealed record ExpRecording(bool Running, TimeSpan Duration, IReadOnlyList<ExpRecordRow> Rows);

public sealed record ExpStats(IReadOnlyList<ExpTrack> Tracks, TimeSpan Elapsed, ExpRecording? Recording = null)
{
    public static readonly ExpStats Empty = new([], TimeSpan.Zero);
}

// Follows level, job or SP and hero progress from "lev" packets.
public sealed class ExpTracker
{
    private const int LevLevel = 1, LevXp = 2, LevJobLevel = 3, LevJobXp = 4, LevLoad = 5, LevJobLoad = 6, LevHeroXp = 9, LevHeroLevel = 10, LevHeroLoad = 11;

    private sealed class Tally(string label, string shortName)
    {
        public readonly string Label = label, Short = shortName;
        public bool Seen;
        public int Level, LevelsGained;
        public long Xp, Load;
        public double GainedPercent, RecordedPercent;
    }

    private readonly Tally[] _tallies = [new("Level", "EXP"), new("Job / SP", "JXP"), new("Heldenlevel", "HXP")];
    private DateTime? _since, _recordStart, _recordEnd;

    public bool IsRecording => _recordStart != null && _recordEnd == null;

    public void Handle(string packet, DateTime now)
    {
        if (!packet.StartsWith("lev ", StringComparison.Ordinal)) return;
        var t = packet.Split(' ');
        if (t.Length <= LevJobLoad) return;
        _since ??= now;
        Update(_tallies[0], t[LevLevel], t[LevXp], t[LevLoad]);
        Update(_tallies[1], t[LevJobLevel], t[LevJobXp], t[LevJobLoad]);
        if (t.Length > LevHeroLoad) Update(_tallies[2], t[LevHeroLevel], t[LevHeroXp], t[LevHeroLoad]);
    }

    private void Update(Tally p, string levelText, string xpText, string loadText)
    {
        if (!int.TryParse(levelText, NumberStyles.None, CultureInfo.InvariantCulture, out var level) || level <= 0
            || !long.TryParse(xpText, NumberStyles.None, CultureInfo.InvariantCulture, out var xp)
            || !long.TryParse(loadText, NumberStyles.None, CultureInfo.InvariantCulture, out var load) || load <= 0) return;

        if (p.Seen)
        {
            // Anything else is a new baseline, e.g. an SP swap or an XP loss.
            var gain = 0.0;
            if (level == p.Level && load == p.Load && xp > p.Xp)
            {
                gain = (xp - p.Xp) * 100.0 / load;
            }
            else if (level == p.Level + 1 && p.Load > p.Xp)
            {
                gain = (p.Load - p.Xp) * 100.0 / p.Load + xp * 100.0 / load;
                p.LevelsGained++;
            }
            p.GainedPercent += gain;
            if (IsRecording) p.RecordedPercent += gain;
        }
        p.Seen = true;
        p.Level = level;
        p.Xp = xp;
        p.Load = load;
    }

    public void Reset(DateTime now)
    {
        foreach (var p in _tallies)
        {
            p.GainedPercent = 0;
            p.LevelsGained = 0;
        }
        _since = now;
    }

    // A new recording replaces the last result.
    public void StartRecording(DateTime now)
    {
        foreach (var p in _tallies) p.RecordedPercent = 0;
        _recordStart = now;
        _recordEnd = null;
    }

    public void StopRecording(DateTime now)
    {
        if (IsRecording) _recordEnd = now;
    }

    public ExpStats Stats(DateTime now)
    {
        var elapsed = _since is { } since ? now - since : TimeSpan.Zero;
        var tracks = _tallies
            .Where(p => p.Seen)
            .Select(p => new ExpTrack(p.Label, p.Short, p.Level, p.Xp, p.Load, p.GainedPercent, p.LevelsGained))
            .ToList();
        return new ExpStats(tracks, elapsed, Recording(now));
    }

    private ExpRecording? Recording(DateTime now)
    {
        if (_recordStart is not { } start) return null;
        var duration = (_recordEnd ?? now) - start;
        var rows = _tallies
            .Where(p => p.Seen)
            .Select(p => new ExpRecordRow(p.Label, p.RecordedPercent, duration > TimeSpan.Zero ? p.RecordedPercent / duration.TotalHours : 0))
            .ToList();
        return new ExpRecording(IsRecording, duration, rows);
    }
}
