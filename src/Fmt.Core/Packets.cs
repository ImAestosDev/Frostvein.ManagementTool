using System.Globalization;
using System.Text;

namespace Fmt.Core;

// Server to client world traffic needs no session key.
public static class WorldDecoder
{
    private const string Table = " -.0123456789";

    public static string Decode(ReadOnlySpan<byte> frame)
    {
        var text = new StringBuilder(frame.Length * 2);
        var i = 0;
        while (i < frame.Length)
        {
            var head = frame[i++];
            var left = head & 0x7F;
            if ((head & 0x80) != 0)
            {
                // Packed run: every byte holds two characters of a small table.
                while (left > 0 && i < frame.Length)
                {
                    var b = frame[i++];
                    AppendNibble(text, b >> 4);
                    left--;
                    if (left == 0) break;
                    AppendNibble(text, b & 0x0F);
                    left--;
                }
            }
            else
            {
                while (left > 0 && i < frame.Length)
                {
                    text.Append((char)(frame[i++] ^ 0xFF));
                    left--;
                }
            }
        }
        return text.ToString();
    }

    private static void AppendNibble(StringBuilder text, int nibble)
    {
        if (nibble >= 1 && nibble <= Table.Length) text.Append(Table[nibble - 1]);
    }

    // Login traffic and cut off frames decode to garbage.
    public static bool LooksLikePacket(string text)
    {
        if (text.Length == 0 || !char.IsAsciiLetterOrDigit(text[0]) && text[0] is not ('$' or '#' or '%')) return false;
        foreach (var c in text)
        {
            if (c < 0x20 && c != '\t') return false;
        }
        return true;
    }
}

// Puts TCP segments back in order and cuts the stream at the 0xFF packet end.
public sealed class TcpStream
{
    private const int MaxPending = 256;
    private const int MaxFrame = 1 << 20;

    private readonly Dictionary<uint, byte[]> _pending = [];
    private readonly List<byte> _frame = [];
    private uint _next;
    private bool _started;
    private bool _skipToFrameEnd = true;

    public void Push(uint seq, ReadOnlySpan<byte> payload, List<string> output)
    {
        if (payload.IsEmpty) return;
        if (!_started)
        {
            _started = true;
            _next = seq;
        }

        var distance = (int)(seq - _next);
        if (distance > 0)
        {
            _pending[seq] = payload.ToArray();
            if (_pending.Count > MaxPending) Resync(seq, payload, output);
            return;
        }
        if (-distance >= payload.Length) return;

        Append(payload[-distance..], output);
        while (_pending.Remove(_next, out var waiting)) Append(waiting, output);
    }

    // A lost segment never arrives: drop the broken packet and go on.
    private void Resync(uint seq, ReadOnlySpan<byte> payload, List<string> output)
    {
        _pending.Clear();
        _frame.Clear();
        _skipToFrameEnd = true;
        _next = seq;
        Append(payload, output);
    }

    private void Append(ReadOnlySpan<byte> bytes, List<string> output)
    {
        _next += (uint)bytes.Length;
        foreach (var b in bytes)
        {
            if (b != 0xFF)
            {
                _frame.Add(b);
                if (_frame.Count > MaxFrame)
                {
                    _frame.Clear();
                    _skipToFrameEnd = true;
                }
                continue;
            }
            if (!_skipToFrameEnd)
            {
                var text = WorldDecoder.Decode(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_frame));
                if (WorldDecoder.LooksLikePacket(text)) output.Add(text);
            }
            _skipToFrameEnd = false;
            _frame.Clear();
        }
    }
}

public readonly record struct TcpSegment(uint SourceAddress, uint DestinationAddress, ushort SourcePort, ushort DestinationPort, uint Sequence, int PayloadOffset, int PayloadLength);

public static class IpPacket
{
    private const int ProtocolTcp = 6;

    // Addresses keep network byte order, like the Windows TCP table.
    public static bool TryParseTcp(ReadOnlySpan<byte> packet, out TcpSegment segment)
    {
        segment = default;
        if (packet.Length < 20 || packet[0] >> 4 != 4 || packet[9] != ProtocolTcp) return false;
        var ipHeader = (packet[0] & 0x0F) * 4;
        var total = Math.Min((packet[2] << 8) | packet[3], packet.Length);
        if (ipHeader < 20 || total < ipHeader + 20) return false;

        var tcp = packet[ipHeader..total];
        var tcpHeader = (tcp[12] >> 4) * 4;
        if (tcpHeader < 20 || tcpHeader > tcp.Length) return false;

        var source = BitConverter.ToUInt32(packet.Slice(12, 4));
        var destination = BitConverter.ToUInt32(packet.Slice(16, 4));
        var sequence = (uint)((tcp[4] << 24) | (tcp[5] << 16) | (tcp[6] << 8) | tcp[7]);
        segment = new TcpSegment(source, destination, (ushort)((tcp[0] << 8) | tcp[1]), (ushort)((tcp[2] << 8) | tcp[3]), sequence, ipHeader + tcpHeader, tcp.Length - tcpHeader);
        return true;
    }
}

public sealed record DamageStats(long? CharacterId, string Name, long Total, long Best, int BestSkill, int Hits, int Misses, double PerSecond)
{
    public static readonly DamageStats Empty = new(null, "", 0, 0, 0, 0, 0, 0);
}

// Counts the own character's hits from "su" packets.
public sealed class DamageMeter
{
    private const string PlayerType = "1";
    private const int HitModeMiss = 2;
    private const int CInfoName = 1, CInfoId = 6;
    private const int SuAttackerType = 1, SuAttacker = 2, SuTargetType = 3, SuTarget = 4, SuSkill = 5, SuDamage = 13, SuHitMode = 14;

    private long? _characterId;
    private string _name = "";
    private long _total;
    private long _best;
    private int _bestSkill;
    private int _hits;
    private int _misses;
    private DateTime _first;
    private DateTime _last;

    public void Handle(string packet, DateTime now)
    {
        if (packet.StartsWith("c_info ", StringComparison.Ordinal)) HandleInfo(packet);
        else if (packet.StartsWith("su ", StringComparison.Ordinal)) HandleHit(packet, now);
    }

    private void HandleInfo(string packet)
    {
        var t = packet.Split(' ');
        if (t.Length <= CInfoId || !long.TryParse(t[CInfoId], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return;
        _characterId = id;
        _name = t[CInfoName];
    }

    private void HandleHit(string packet, DateTime now)
    {
        if (_characterId is not { } own) return;
        var t = packet.Split(' ');
        if (t.Length <= SuHitMode || t[SuAttackerType] != PlayerType) return;
        if (!long.TryParse(t[SuAttacker], NumberStyles.None, CultureInfo.InvariantCulture, out var attacker) || attacker != own) return;
        // Buffs and heals target the caster.
        if (t[SuTargetType] == PlayerType && t[SuTarget] == t[SuAttacker]) return;
        if (!long.TryParse(t[SuDamage], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var damage)) return;
        int.TryParse(t[SuHitMode], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var mode);

        if (mode == HitModeMiss)
        {
            _misses++;
            return;
        }
        if (damage <= 0) return;

        if (_hits == 0) _first = now;
        _last = now;
        _hits++;
        _total += damage;
        if (damage > _best)
        {
            _best = damage;
            int.TryParse(t[SuSkill], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _bestSkill);
        }
    }

    public void Reset()
    {
        _total = _best = 0;
        _bestSkill = _hits = _misses = 0;
    }

    public DamageStats Stats()
    {
        var seconds = Math.Max(1, (_last - _first).TotalSeconds);
        return new DamageStats(_characterId, _name, _total, _best, _bestSkill, _hits, _misses, _hits == 0 ? 0 : _total / seconds);
    }
}
