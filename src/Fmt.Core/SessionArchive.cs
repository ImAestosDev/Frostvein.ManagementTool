using System.Text.Json;

namespace Fmt.Core;

public sealed record SavedSession(
    string Id,
    string Character,
    DateTime StartedAt,
    TimeSpan Duration,
    bool TimeIsUp,
    bool Finished,
    List<PlayerDamage> Players,
    List<ExpRecordRow> Exp,
    List<DroppedItem> Items,
    long Gold,
    List<MonsterKills> Kills,
    List<UsedItem> Used)
{
    public long OwnDamage => Players.FirstOrDefault(p => p.IsOwn)?.Total ?? 0;
    public int TotalKills => Kills.Sum(k => k.Count);

    public bool HasContent =>
        Players.Count > 0 || Exp.Any(e => e.GainedPercent > 0) || Items.Count > 0 || Gold > 0 || Kills.Count > 0 || Used.Count > 0;
}

// Every session ever run, kept next to the other data files.
public sealed class SessionArchive
{
    private readonly List<SavedSession> _sessions = [];

    public bool Dirty { get; private set; }

    public void Add(SavedSession session)
    {
        var index = _sessions.FindIndex(s => s.Id == session.Id);
        if (index >= 0) _sessions[index] = session;
        else if (session.HasContent) _sessions.Add(session);
        else return;
        Dirty = true;
    }

    public List<SavedSession> NewestFirst() => _sessions.OrderByDescending(s => s.StartedAt).ToList();

    public static SessionArchive Load(string path)
    {
        var archive = new SessionArchive();
        if (!File.Exists(path)) return archive;
        try
        {
            var saved = JsonSerializer.Deserialize<List<SavedSession>>(File.ReadAllText(path));
            if (saved != null) archive._sessions.AddRange(saved);
        }
        catch (JsonException)
        {
            // Keeps the damaged file instead of overwriting it with the next save.
            File.Move(path, path + ".bad", true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The next start tries again.
        }
        return archive;
    }

    public void Save(string path)
    {
        if (!Dirty) return;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_sessions));
        File.Move(tmp, path, true);
        Dirty = false;
    }
}
