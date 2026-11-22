using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Fmt.App;

public readonly record struct FrameStats(double Fps, double AvgMs, double LowFps, double MaxMs, string Source, double[] Recent)
{
    public static readonly FrameStats None = new(0, 0, 0, 0, "", []);
    public bool HasData => Recent.Length > 0;
}

// Counts presented frames per process from the Windows present events (ETW, needs admin).
// Only listens to what Windows reports; nothing is loaded into the game.
public sealed class FrameCounter : IDisposable
{
    private const string SessionName = "FMT-Frames";
    private static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid DxgKrnl = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private const ushort D3D9PresentStart = 1, DxgiPresentStart = 42, KrnlPresentHistory = 215, KrnlPresentHistoryOld = 171;
    private const int Capacity = 2048;

    private static readonly double Tick = 1000.0 / Stopwatch.Frequency;
    private readonly Dictionary<uint, Proc> _procs = [];
    private readonly EventRecordCallback _callback;
    private Thread? _thread;
    private ulong _session, _trace;
    private long _latest;

    // Probe hook: every event, before filtering.
    public Action<uint, Guid, ushort, long>? Raw;

    public FrameCounter() => _callback = OnEvent;

    public bool IsRunning { get; private set; }
    public string? Error { get; private set; }

    public bool Start()
    {
        if (IsRunning) return true;
        var props = Properties();
        try
        {
            var rc = StartTraceW(out _session, SessionName, props);
            if (rc == 183) // a stale session from a crashed FMT
            {
                ControlTraceW(0, SessionName, props, 1);
                Marshal.FreeHGlobal(props);
                props = Properties();
                rc = StartTraceW(out _session, SessionName, props);
            }
            if (rc != 0) return Fail(rc == 5 ? "FPS-Messung braucht Admin-Rechte." : $"FPS-Messung startet nicht (Fehler {rc}).");
        }
        finally { Marshal.FreeHGlobal(props); }

        // Level 4 = informational; the present events live there.
        Enable(D3D9, 0x2);
        Enable(Dxgi, 0x1);
        // Verified live: these keywords deliver the present history events; ~2000 events/s per client is cheap.
        Enable(DxgKrnl, 0x1 | 0x8000000);

        var log = Marshal.AllocHGlobal(LogFileSize);
        try
        {
            for (var i = 0; i < LogFileSize; i += 4) Marshal.WriteInt32(log, i, 0);
            var name = Marshal.StringToHGlobalUni(SessionName);
            Marshal.WriteIntPtr(log, 8, name);
            Marshal.WriteInt32(log, 28, 0x100 | 0x1000 | 0x10000000); // real time, raw QPC, event record
            Marshal.WriteIntPtr(log, 424, Marshal.GetFunctionPointerForDelegate(_callback));
            _trace = OpenTraceW(log);
            Marshal.FreeHGlobal(name);
        }
        finally { Marshal.FreeHGlobal(log); }
        if (_trace == ulong.MaxValue)
        {
            Stop();
            return Fail($"FPS-Messung startet nicht (Fehler {Marshal.GetLastWin32Error()}).");
        }

        IsRunning = true;
        Error = null;
        _thread = new Thread(() => ProcessTrace([_trace], 1, 0, 0)) { IsBackground = true, Name = "FMT frames" };
        _thread.Start();
        return true;
    }

    private bool Fail(string error)
    {
        Error = error;
        return false;
    }

    private void Enable(Guid provider, ulong keywords) => EnableTraceEx2(_session, ref provider, 1, 4, keywords, 0, 0, 0);

    public void Stop()
    {
        if (_session != 0)
        {
            var props = Properties();
            ControlTraceW(_session, null, props, 1);
            Marshal.FreeHGlobal(props);
            _session = 0;
        }
        if (_trace != 0 && _trace != ulong.MaxValue) CloseTrace(_trace);
        _trace = 0;
        _thread?.Join(2000);
        _thread = null;
        IsRunning = false;
    }

    public void Dispose() => Stop();

    // Frame times of the last few seconds, measured up to the newest event Windows delivered.
    public FrameStats Stats(uint pid, double seconds = 5)
    {
        Proc? p;
        lock (_procs) _procs.TryGetValue(pid, out p);
        if (p == null) return FrameStats.None;
        var end = Interlocked.Read(ref _latest);
        lock (p)
        {
            var source = p.Best(end);
            if (source < 0) return FrameStats.None;
            var ring = p.Rings[source];
            var from = end - (long)(seconds * Stopwatch.Frequency);
            var times = ring.Since(from);
            if (times.Count < 2) return FrameStats.None;
            var deltas = new double[times.Count - 1];
            for (var i = 1; i < times.Count; i++) deltas[i - 1] = (times[i] - times[i - 1]) * Tick;
            var span = (end - times[0]) * Tick;
            var fps = (times.Count - 1) * 1000.0 / Math.Max(span, 1);
            var avg = deltas.Average();
            var sorted = deltas.OrderByDescending(d => d).ToArray();
            var worst = sorted.Take(Math.Max(1, sorted.Length / 100)).Average();
            var recent = deltas.Length > 120 ? deltas[^120..] : deltas;
            return new FrameStats(fps, avg, 1000.0 / worst, sorted[0], SourceNames[source], recent);
        }
    }

    public IReadOnlyList<uint> Processes()
    {
        lock (_procs) return [.. _procs.Keys];
    }

    private static readonly string[] SourceNames = ["D3D9", "DXGI", "Vulkan/Kernel"];

    // EVENT_HEADER: ProcessId @12, TimeStamp @16, ProviderId @24, EventDescriptor.Id @40.
    private void OnEvent(nint record)
    {
        var pid = (uint)Marshal.ReadInt32(record, 12);
        var time = Marshal.ReadInt64(record, 16);
        var provider = Marshal.PtrToStructure<Guid>(record + 24);
        var id = (ushort)Marshal.ReadInt16(record, 40);
        Raw?.Invoke(pid, provider, id, time);

        int source;
        if (provider == D3D9 && id == D3D9PresentStart) source = 0;
        else if (provider == Dxgi && id == DxgiPresentStart) source = 1;
        else if (provider == DxgKrnl && (id == KrnlPresentHistory || id == KrnlPresentHistoryOld)) source = 2;
        else return;

        if (time > Interlocked.Read(ref _latest)) Interlocked.Exchange(ref _latest, time);
        Proc? p;
        lock (_procs)
        {
            if (!_procs.TryGetValue(pid, out p)) _procs[pid] = p = new Proc();
        }
        lock (p) p.Rings[source].Add(time);
    }

    private sealed class Proc
    {
        public readonly Ring[] Rings = [new(), new(), new()];

        // The API the game presents with wins over the kernel view of the same frame.
        public int Best(long now)
        {
            var recent = now - 2 * Stopwatch.Frequency;
            for (var i = 0; i < Rings.Length; i++)
                if (Rings[i].Last >= recent) return i;
            return -1;
        }
    }

    private sealed class Ring
    {
        private readonly long[] _items = new long[Capacity];
        private int _next, _count;

        public long Last => _count == 0 ? 0 : _items[(_next - 1 + Capacity) % Capacity];

        public void Add(long time)
        {
            _items[_next] = time;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        public List<long> Since(long from)
        {
            var list = new List<long>();
            for (var i = _count; i > 0; i--)
            {
                var t = _items[(_next - i + Capacity) % Capacity];
                if (t >= from) list.Add(t);
            }
            return list;
        }
    }

    // EVENT_TRACE_PROPERTIES (120 bytes on x64) followed by room for the session name.
    private const int PropsSize = 120 + 1024;
    private const int LogFileSize = 448; // EVENT_TRACE_LOGFILEW on x64

    private static nint Properties()
    {
        var p = Marshal.AllocHGlobal(PropsSize);
        for (var i = 0; i < PropsSize; i += 4) Marshal.WriteInt32(p, i, 0);
        Marshal.WriteInt32(p, 0, PropsSize);    // Wnode.BufferSize
        Marshal.WriteInt32(p, 40, 1);           // Wnode.ClientContext: QPC timestamps
        Marshal.WriteInt32(p, 44, 0x00020000);  // Wnode.Flags: WNODE_FLAG_TRACED_GUID
        Marshal.WriteInt32(p, 48, 4);           // BufferSize in KB: small buffers reach us sooner
        Marshal.WriteInt32(p, 64, 0x100);       // LogFileMode: real time
        Marshal.WriteInt32(p, 68, 1);           // FlushTimer: 1 s
        Marshal.WriteInt32(p, 116, 120);        // LoggerNameOffset
        return p;
    }

    private delegate void EventRecordCallback(nint record);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int StartTraceW(out ulong handle, string name, nint properties);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int ControlTraceW(ulong handle, string? name, nint properties, uint code);

    [DllImport("advapi32.dll")]
    private static extern int EnableTraceEx2(ulong handle, ref Guid provider, uint code, byte level, ulong any, ulong all, uint timeout, nint parameters);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern ulong OpenTraceW(nint logfile);

    [DllImport("advapi32.dll")]
    private static extern int ProcessTrace(ulong[] handles, uint count, nint start, nint end);

    [DllImport("advapi32.dll")]
    private static extern int CloseTrace(ulong handle);
}
