using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Fmt.App;

// Ping to the server the game client is connected to. Finds the client's TCP connection and pings that
// address every two seconds (plain ICMP echo, nothing goes through the game connection).
public sealed class GamePing : IDisposable
{
    private readonly Timer _timer;
    private readonly Dictionary<uint, uint> _servers = new(); // pid -> server address
    private readonly Dictionary<uint, long> _ms = new();      // server address -> last ping, -1 = no answer
    private readonly object _lock = new();
    private uint _pid;
    private int _busy;

    public GamePing() => _timer = new Timer(_ => Tick(), null, 0, 2000);

    // Last ping in ms for the given client, null while unknown.
    public long? For(uint pid)
    {
        lock (_lock)
        {
            _pid = pid;
            if (!_servers.TryGetValue(pid, out var server) || !_ms.TryGetValue(server, out var ms) || ms < 0) return null;
            return ms;
        }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            uint pid;
            lock (_lock) pid = _pid;
            if (pid == 0) return;
            var server = ServerOf(pid);
            lock (_lock)
            {
                if (server == 0) { _servers.Remove(pid); return; }
                _servers[pid] = server;
            }
            using var ping = new Ping();
            var reply = ping.Send(new IPAddress(server), 1000);
            lock (_lock) _ms[server] = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
        }
        catch (Exception e) when (e is PingException or InvalidOperationException) { }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    // Remote address of the client's established connection (the world server), 0 if none.
    private static uint ServerOf(uint pid)
    {
        var size = 0;
        GetExtendedTcpTable(0, ref size, false, 2, 5, 0);
        var table = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(table, ref size, false, 2, 5, 0) != 0) return 0;
            var count = Marshal.ReadInt32(table);
            var row = table + 4;
            for (var i = 0; i < count; i++, row += Marshal.SizeOf<TcpRow>())
            {
                var r = Marshal.PtrToStructure<TcpRow>(row);
                if (r.State == 5 && r.Pid == pid && r.RemoteAddress != 0x0100007F) return r.RemoteAddress;
            }
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, Pid;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(nint table, ref int size, bool order, int family, int tableClass, uint reserved);

    public void Dispose() => _timer.Dispose();
}
