using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace Fmt.App;

public sealed record LoggedPacket(DateTime Time, string Text);

// Reads incoming game traffic from the network card; the client itself stays untouched.
public sealed class PacketCapture : IDisposable
{
    private const string GameClass = "TNosTaleMainF";
    private const int MaxLog = 2000;
    private const int TableRefreshMs = 500;
    private const int AddressFamilyV4 = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const uint StateEstablished = 5;

    private readonly record struct Flow(uint RemoteAddress, ushort RemotePort, ushort LocalPort);

    private readonly object _lock = new();
    private readonly List<Socket> _sockets = [];
    private readonly Dictionary<Flow, TcpStream> _streams = [];
    private readonly Dictionary<uint, DamageMeter> _meters = [];
    private readonly Dictionary<uint, ExpTracker> _exp = [];
    private readonly Dictionary<uint, DropTracker> _drops = [];
    private readonly Dictionary<uint, Session> _sessions = [];
    private readonly Dictionary<uint, KillTracker> _kills = [];
    private readonly Dictionary<uint, ItemUseTracker> _uses = [];
    private readonly IReadOnlySet<int> _watchedItems = ItemCatalog.WatchedVNums(), _watchedCards = ItemCatalog.WatchedCards();
    private readonly Dictionary<uint, string> _lastLev = [];
    private readonly Queue<LoggedPacket> _log = new();
    private readonly Dictionary<long, string> _known;
    private readonly NameBook _names;
    private volatile HashSet<string> _hidden;
    private volatile Dictionary<Flow, uint> _flows = [];
    private Timer? _tableTimer;
    private uint _lastPid;
    private long _logVersion;
    private long _incoming;
    private long _outgoing;
    private long _packets;
    private DateTime _startedAt;

    public PacketCapture(IDictionary<long, string> myCharacters, IEnumerable<string> hidden, NameBook names)
    {
        _names = names;
        _known = new Dictionary<long, string>(myCharacters);
        _hidden = new HashSet<string>(hidden, StringComparer.Ordinal);
    }

    // Raised on a capture thread.
    public event Action<long, string>? OwnCharacterFound;

    public bool IsRunning { get; private set; }
    public string? Error { get; private set; }
    public long LogVersion => Interlocked.Read(ref _logVersion);
    public int Connections => _flows.Count;
    public long Incoming => Interlocked.Read(ref _incoming);
    public long Outgoing => Interlocked.Read(ref _outgoing);
    public long Packets => Interlocked.Read(ref _packets);

    // A connected game without any incoming data means something filters the raw socket.
    public bool LooksBlocked => IsRunning && Connections > 0 && Incoming == 0 && DateTime.Now - _startedAt > BlockedAfter;

    private static readonly TimeSpan BlockedAfter = TimeSpan.FromSeconds(8);
    private const string FirewallRule = "FMT Packet Capture";

    // Inbound allow rule for this program only; returns an error text or null.
    public static string? AllowInFirewall()
    {
        var start = new System.Diagnostics.ProcessStartInfo("netsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "advfirewall", "firewall", "add", "rule", $"name={FirewallRule}", "dir=in", "action=allow", $"program={Environment.ProcessPath}", "enable=yes" })
            start.ArgumentList.Add(arg);
        try
        {
            using var process = System.Diagnostics.Process.Start(start)!;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? null : text.Trim();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return e.Message;
        }
    }

    public static bool IsAdmin
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public void Start()
    {
        if (IsRunning) return;
        Error = null;
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Distinct().ToList();

        foreach (var address in addresses)
        {
            try
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP) { ReceiveBufferSize = 1 << 21 };
                socket.Bind(new IPEndPoint(address, 0));
                socket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), new byte[4]);
                _sockets.Add(socket);
                new Thread(() => ReadLoop(socket)) { IsBackground = true, Name = "FMT capture " + address }.Start();
            }
            catch (SocketException e)
            {
                Error = e.Message;
            }
        }
        if (_sockets.Count == 0)
        {
            Error ??= "Keine aktive Netzwerkverbindung gefunden.";
            return;
        }
        Error = null;
        IsRunning = true;
        _startedAt = DateTime.Now;
        _tableTimer = new Timer(_ => RefreshFlows(), null, 0, TableRefreshMs);
    }

    public void Stop()
    {
        IsRunning = false;
        _tableTimer?.Dispose();
        _tableTimer = null;
        foreach (var socket in _sockets) socket.Dispose();
        _sockets.Clear();
        lock (_lock) _streams.Clear();
    }

    private void ReadLoop(Socket socket)
    {
        var buffer = new byte[65536];
        var output = new List<string>();
        try
        {
            while (true)
            {
                var length = socket.Receive(buffer);
                if (!IpPacket.TryParseTcp(buffer.AsSpan(0, length), out var s) || s.PayloadLength == 0) continue;
                var flow = new Flow(s.SourceAddress, s.SourcePort, s.DestinationPort);
                if (!_flows.TryGetValue(flow, out var pid))
                {
                    if (_flows.ContainsKey(new Flow(s.DestinationAddress, s.DestinationPort, s.SourcePort))) Interlocked.Increment(ref _outgoing);
                    continue;
                }
                Interlocked.Increment(ref _incoming);

                lock (_lock)
                {
                    if (!_streams.TryGetValue(flow, out var stream)) _streams[flow] = stream = new TcpStream();
                    output.Clear();
                    stream.Push(s.Sequence, buffer.AsSpan(s.PayloadOffset, s.PayloadLength), output);
                    foreach (var packet in output) Record(pid, packet);
                }
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            // Closed by Stop.
        }
    }

    // Also the entry point for dev renders.
    public void Feed(uint pid, string packet)
    {
        lock (_lock) Record(pid, packet);
    }

    private void Record(uint pid, string packet)
    {
        var now = DateTime.Now;
        Interlocked.Increment(ref _packets);
        if (!_meters.TryGetValue(pid, out var meter)) _meters[pid] = meter = new DamageMeter(_known, _names);
        meter.Handle(packet, now);
        if (!_exp.TryGetValue(pid, out var exp)) _exp[pid] = exp = new ExpTracker();
        exp.Handle(packet, now);
        if (!_drops.TryGetValue(pid, out var drops)) _drops[pid] = drops = new DropTracker();
        drops.Handle(packet, meter.OwnId);
        if (!_uses.TryGetValue(pid, out var uses)) _uses[pid] = uses = new ItemUseTracker(_watchedItems, _watchedCards);
        uses.Handle(packet, meter.OwnId, now);
        if (!_kills.TryGetValue(pid, out var kills)) _kills[pid] = kills = new KillTracker();
        kills.Handle(packet, meter.OwnId);
        if (packet.StartsWith("lev ", StringComparison.Ordinal)) _lastLev[pid] = packet;
        if (_sessions.TryGetValue(pid, out var session)) session.Handle(packet, now, meter.OwnId);
        RememberOwn(meter);
        _lastPid = pid;

        var space = packet.IndexOf(' ');
        if (_hidden.Contains(space < 0 ? packet : packet[..space])) return;
        _log.Enqueue(new LoggedPacket(now, packet));
        if (_log.Count > MaxLog) _log.Dequeue();
        Interlocked.Increment(ref _logVersion);
    }

    private void RememberOwn(DamageMeter meter)
    {
        if (meter.OwnId is not { } id) return;
        var name = meter.OwnName;
        if (_known.TryGetValue(id, out var saved) && (saved == name || name.StartsWith("Spieler ", StringComparison.Ordinal))) return;
        _known[id] = name;
        OwnCharacterFound?.Invoke(id, name);
    }

    public void SetOwn(uint pid, long id)
    {
        lock (_lock)
        {
            if (Meter(pid) is not { } meter) return;
            meter.SetOwn(id);
            RememberOwn(meter);
        }
    }

    public void SetHidden(IEnumerable<string> headers)
    {
        _hidden = new HashSet<string>(headers, StringComparer.Ordinal);
    }

    public DamageStats Stats(uint pid)
    {
        lock (_lock) return Meter(pid)?.Stats() ?? DamageStats.Empty;
    }

    public void SaveNames(string path)
    {
        try
        {
            lock (_lock) _names.Save(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Stays dirty, the next save tries again.
        }
    }

    public string OwnName(uint pid)
    {
        lock (_lock) return Meter(pid)?.OwnName ?? "";
    }

    public void ResetDamage(uint pid)
    {
        lock (_lock) Meter(pid)?.Reset();
    }

    public ExpStats Exp(uint pid)
    {
        lock (_lock) return ExpOf(pid)?.Stats(DateTime.Now) ?? ExpStats.Empty;
    }

    public void ResetExp(uint pid)
    {
        lock (_lock) ExpOf(pid)?.Reset(DateTime.Now);
    }

    public void StartExpRecording(uint pid)
    {
        lock (_lock) ExpOf(pid)?.StartRecording(DateTime.Now);
    }

    public void StopExpRecording(uint pid)
    {
        lock (_lock) ExpOf(pid)?.StopRecording(DateTime.Now);
    }

    public DropStats Drops(uint pid)
    {
        lock (_lock) return DropsOf(pid)?.Stats() ?? DropStats.Empty;
    }

    public void ResetDrops(uint pid)
    {
        lock (_lock) DropsOf(pid)?.Reset();
    }

    private DropTracker? DropsOf(uint pid) =>
        _drops.TryGetValue(pid, out var drops) || _drops.TryGetValue(_lastPid, out drops) ? drops : null;

    public void StartSession(uint pid)
    {
        lock (_lock)
        {
            var key = _meters.ContainsKey(pid) || _lastPid == 0 ? pid : _lastPid;
            _meters.TryGetValue(key, out var meter);
            _lastLev.TryGetValue(key, out var lev);
            _drops.TryGetValue(key, out var drops);
            var damage = new DamageMeter(_known, _names);
            if (meter?.OwnId is { } own) damage.SetOwn(own);
            if (!_uses.TryGetValue(key, out var uses)) _uses[key] = uses = new ItemUseTracker(_watchedItems, _watchedCards);
            _kills.TryGetValue(key, out var kills);
            _sessions[key] = new Session(DateTime.Now, lev, damage, drops, uses, kills);
        }
    }

    public void EndSession(uint pid)
    {
        lock (_lock) SessionOf(pid)?.End(DateTime.Now);
    }

    public SessionStats? Session(uint pid)
    {
        lock (_lock) return SessionOf(pid)?.Stats(DateTime.Now);
    }

    private Session? SessionOf(uint pid) =>
        _sessions.TryGetValue(pid, out var session) || _sessions.TryGetValue(_lastPid, out session) ? session : null;

    public ItemUseStats Items(uint pid)
    {
        lock (_lock) return UsesOf(pid)?.Counter(DateTime.Now) ?? ItemUseStats.Empty;
    }

    public void ResetItems(uint pid)
    {
        lock (_lock) UsesOf(pid)?.ResetCounter(DateTime.Now);
    }

    private ItemUseTracker? UsesOf(uint pid) =>
        _uses.TryGetValue(pid, out var uses) || _uses.TryGetValue(_lastPid, out uses) ? uses : null;

    public KillStats Kills(uint pid)
    {
        lock (_lock) return KillsOf(pid)?.Stats() ?? KillStats.Empty;
    }

    public void ResetKills(uint pid)
    {
        lock (_lock) KillsOf(pid)?.Reset();
    }

    private KillTracker? KillsOf(uint pid) =>
        _kills.TryGetValue(pid, out var kills) || _kills.TryGetValue(_lastPid, out kills) ? kills : null;

    private ExpTracker? ExpOf(uint pid) =>
        _exp.TryGetValue(pid, out var exp) || _exp.TryGetValue(_lastPid, out exp) ? exp : null;

    private DamageMeter? Meter(uint pid) =>
        _meters.TryGetValue(pid, out var meter) || _meters.TryGetValue(_lastPid, out meter) ? meter : null;

    public List<LoggedPacket> Log()
    {
        lock (_lock) return [.. _log];
    }

    public void ClearLog()
    {
        lock (_lock) _log.Clear();
        Interlocked.Increment(ref _logVersion);
    }

    private void RefreshFlows()
    {
        var games = new HashSet<uint>();
        foreach (var hwnd in Native.TopLevelWindows())
        {
            if (Native.ClassName(hwnd) != GameClass) continue;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            games.Add(pid);
        }
        var flows = ReadTcpTable(games);
        _flows = flows;
        lock (_lock)
        {
            foreach (var closed in _streams.Keys.Where(f => !flows.ContainsKey(f)).ToList()) _streams.Remove(closed);
        }
    }

    private static Dictionary<Flow, uint> ReadTcpTable(HashSet<uint> pids)
    {
        var result = new Dictionary<Flow, uint>();
        if (pids.Count == 0) return result;
        var size = 0;
        GetExtendedTcpTable(0, ref size, false, AddressFamilyV4, TcpTableOwnerPidAll, 0);
        var table = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(table, ref size, false, AddressFamilyV4, TcpTableOwnerPidAll, 0) != 0) return result;
            var count = Marshal.ReadInt32(table);
            var row = table + 4;
            for (var i = 0; i < count; i++, row += Marshal.SizeOf<TcpRow>())
            {
                var r = Marshal.PtrToStructure<TcpRow>(row);
                if (r.State == StateEstablished && pids.Contains(r.Pid))
                    result[new Flow(r.RemoteAddress, Port(r.RemotePort), Port(r.LocalPort))] = r.Pid;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
        return result;
    }

    // Ports sit in network byte order in the low word.
    private static ushort Port(uint raw) => (ushort)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, Pid;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(nint table, ref int size, bool order, int family, int tableClass, uint reserved);

    public void Dispose() => Stop();
}
