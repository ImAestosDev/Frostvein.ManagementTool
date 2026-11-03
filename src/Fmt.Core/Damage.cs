using System.Globalization;
using System.Text.Json;

namespace Fmt.Core;

public sealed record PlayerDamage(long Id, string Name, bool IsOwn, long Total, long Best, int BestSkill, int Hits, int Misses, double PerSecond);

public enum DamageMode { Single, Group, Raid }

public sealed record DamageStats(long? OwnId, IReadOnlyList<PlayerDamage> Players, IReadOnlySet<long>? GroupIds = null)
{
    public static readonly DamageStats Empty = new(null, []);

    public static int Slots(DamageMode mode) => mode switch
    {
        DamageMode.Raid => 20,
        DamageMode.Group => 3,
        _ => 1,
    };

    public PlayerDamage? Own => Players.FirstOrDefault(p => p.IsOwn);
    public long MapTotal => Players.Sum(p => p.Total);

    // The table for a mode, best first; the own row always keeps a slot.
    public List<PlayerDamage> Table(DamageMode mode)
    {
        if (mode == DamageMode.Single) return Own is { } me ? [me] : [];
        var slots = Slots(mode);
        var pool = mode == DamageMode.Group && GroupIds is { Count: > 0 } group
            ? Players.Where(p => p.IsOwn || group.Contains(p.Id))
            : Players;
        var table = pool.Take(slots).ToList();
        if (Own is { } own && !table.Contains(own))
        {
            if (table.Count == slots) table.RemoveAt(slots - 1);
            table.Add(own);
        }
        return table;
    }
}

// Character ids never change, so a name once seen stays valid across sessions.
public sealed class NameBook
{
    private readonly Dictionary<long, string> _names = [];

    public bool Dirty { get; private set; }

    public void Set(long id, string name)
    {
        if (name.Length == 0 || (_names.TryGetValue(id, out var old) && old == name)) return;
        _names[id] = name;
        Dirty = true;
    }

    public bool TryGet(long id, out string name) => _names.TryGetValue(id, out name!);

    public static NameBook Load(string path)
    {
        var book = new NameBook();
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<long, string>>(File.ReadAllText(path));
            if (saved != null)
            {
                foreach (var (id, name) in saved) book._names[id] = name;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Names come back as soon as players are seen again.
        }
        return book;
    }

    public void Save(string path)
    {
        if (!Dirty) return;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_names));
        File.Move(tmp, path, true);
        Dirty = false;
    }
}

// Counts the hits of every player on the map from "su" packets.
public sealed class DamageMeter(IReadOnlyDictionary<long, string>? known = null, NameBook? names = null)
{
    private const string PlayerType = "1";
    private const int HitModeMiss = 2;
    private const int CInfoName = 1, CInfoId = 6;
    private const int InType = 1, InName = 2, InId = 4;
    private const int CondType = 1, CondId = 2;
    private const int SpkId = 2, SpkName = 4;
    private const string GroupTag = "1";
    private const int GroupId = 1, GroupName = 4, FriendName = 3, FamilyName = 2;
    private const int SuAttackerType = 1, SuAttacker = 2, SuTargetType = 3, SuTarget = 4, SuSkill = 5, SuDamage = 13, SuHitMode = 14;

    private sealed class Tally
    {
        public long Total, Best;
        public int BestSkill, Hits, Misses;
        public DateTime First, Last;
    }

    private readonly Dictionary<long, Tally> _players = [];
    private readonly NameBook _names = names ?? new NameBook();
    private readonly HashSet<long> _group = [];

    public long? OwnId { get; private set; }
    public string OwnName => OwnId is { } id ? NameOf(id) : "";

    public void Handle(string packet, DateTime now)
    {
        if (packet.StartsWith("su ", StringComparison.Ordinal)) HandleHit(packet, now);
        else if (packet.StartsWith("in 1 ", StringComparison.Ordinal)) HandleIn(packet);
        else if (packet.StartsWith("c_info ", StringComparison.Ordinal)) HandleInfo(packet);
        else if (packet.StartsWith("cond 1 ", StringComparison.Ordinal)) HandleCond(packet);
        else if (packet.StartsWith("spk 1 ", StringComparison.Ordinal)) HandleChat(packet);
        else if (packet.StartsWith("pinit ", StringComparison.Ordinal)) HandleGroup(packet);
        else if (packet.StartsWith("finit ", StringComparison.Ordinal)) HandleLists(packet, 0, FriendName, null);
        else if (packet.StartsWith("gmbr ", StringComparison.Ordinal)) HandleLists(packet, 0, FamilyName, null);
    }

    private void HandleChat(string packet)
    {
        var t = packet.Split(' ');
        if (t.Length > SpkName && Id(t[SpkId], out var id)) _names.Set(id, t[SpkName]);
    }

    private void HandleGroup(string packet)
    {
        HandleLists(packet, GroupId, GroupName, GroupTag);
        _group.Clear();
        foreach (var entry in packet.Split(' ').Skip(1))
        {
            var parts = entry.Split('|');
            if (parts.Length > GroupId && parts[0] == GroupTag && Id(parts[GroupId], out var id)) _group.Add(id);
        }
    }

    // Group, friend and family lists carry "id|...|name" entries.
    private void HandleLists(string packet, int idPart, int namePart, string? tag)
    {
        foreach (var entry in packet.Split(' ').Skip(1))
        {
            var parts = entry.Split('|');
            if (parts.Length <= Math.Max(idPart, namePart) || (tag != null && parts[0] != tag)) continue;
            if (Id(parts[idPart], out var id)) _names.Set(id, parts[namePart]);
        }
    }

    // Manual choice from the player list.
    public void SetOwn(long id) => OwnId = id;

    private static bool Id(string text, out long id) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);

    private void HandleInfo(string packet)
    {
        var t = packet.Split(' ');
        if (t.Length <= CInfoId || !Id(t[CInfoId], out var id)) return;
        OwnId = id;
        _names.Set(id, t[CInfoName]);
    }

    private void HandleIn(string packet)
    {
        var t = packet.Split(' ');
        if (t.Length > InId && t[InType] == PlayerType && Id(t[InId], out var id)) _names.Set(id, t[InName]);
    }

    // Only the own character gets this packet, so it works without a map change.
    private void HandleCond(string packet)
    {
        if (OwnId != null) return;
        var t = packet.Split(' ');
        if (t.Length > CondId && t[CondType] == PlayerType && Id(t[CondId], out var id)) OwnId = id;
    }

    private void HandleHit(string packet, DateTime now)
    {
        var t = packet.Split(' ');
        if (t.Length <= SuHitMode || t[SuAttackerType] != PlayerType || !Id(t[SuAttacker], out var attacker)) return;
        // Buffs and heals target the caster.
        if (t[SuTargetType] == PlayerType && t[SuTarget] == t[SuAttacker]) return;
        if (!long.TryParse(t[SuDamage], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var damage)) return;
        int.TryParse(t[SuHitMode], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var mode);
        if (mode != HitModeMiss && damage <= 0) return;

        if (OwnId == null && known?.ContainsKey(attacker) == true) OwnId = attacker;
        if (!_players.TryGetValue(attacker, out var p)) _players[attacker] = p = new Tally();
        if (mode == HitModeMiss)
        {
            p.Misses++;
            return;
        }

        if (p.Hits == 0) p.First = now;
        p.Last = now;
        p.Hits++;
        p.Total += damage;
        if (damage > p.Best)
        {
            p.Best = damage;
            int.TryParse(t[SuSkill], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out p.BestSkill);
        }
    }

    public void Reset() => _players.Clear();

    private string NameOf(long id)
    {
        if (_names.TryGet(id, out var name)) return name;
        return known != null && known.TryGetValue(id, out var saved) && saved.Length > 0 ? saved : $"Spieler {id}";
    }

    public DamageStats Stats()
    {
        var players = _players
            .Select(pair =>
            {
                var p = pair.Value;
                var seconds = Math.Max(1, (p.Last - p.First).TotalSeconds);
                return new PlayerDamage(pair.Key, NameOf(pair.Key), pair.Key == OwnId, p.Total, p.Best, p.BestSkill, p.Hits, p.Misses, p.Hits == 0 ? 0 : p.Total / seconds);
            })
            .OrderByDescending(p => p.Total).ThenBy(p => p.Id)
            .ToList();
        return new DamageStats(OwnId, players, _group.ToHashSet());
    }
}
