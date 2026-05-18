using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace NetworkWatch.Services;

/// <summary>
/// Per-process TCP traffic via ETW (no administrator required).
/// </summary>
internal sealed class EtwTrafficProvider : IDisposable
{
    private static readonly Guid TcpIpProviderGuid = new("7DD42A49-5329-4832-8DFD-43D979153A88");

    private readonly object _lock = new();
    private readonly Dictionary<int, (ulong In, ulong Out)> _totals = new();
    private TraceEventSession? _session;
    private Thread? _thread;
    private bool _disposed;

    public bool IsActive { get; private set; }

    public void Start()
    {
        if (_disposed)
            return;

        try
        {
            var sessionName = $"NetworkWatch-{Environment.ProcessId}";
            _session = new TraceEventSession(sessionName)
            {
                StopOnDispose = true,
                BufferSizeMB = 32
            };

            _session.EnableProvider(
                TcpIpProviderGuid,
                TraceEventLevel.Informational,
                ulong.MaxValue);

            var source = _session.Source;
            source.Dynamic.All += OnTraceEvent;

            _thread = new Thread(() =>
            {
                try
                {
                    source.Process();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ETW processing stopped: {ex.Message}");
                }
            })
            {
                IsBackground = true,
                Name = "NetworkWatch-Etw"
            };
            _thread.Start();
            IsActive = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ETW session failed: {ex.Message}");
            Dispose();
        }
    }

    private void OnTraceEvent(TraceEvent traceEvent)
    {
        if (traceEvent.ProviderName is not "Microsoft-Windows-TCPIP")
            return;

        var pid = GetPid(traceEvent);
        if (pid <= 0)
            return;

        var size = GetSize(traceEvent);
        if (size == 0)
            return;

        switch (traceEvent.EventName)
        {
            case "TcpIpRecv":
            case "TcpIpRecvIPV6":
                Add(pid, size, 0);
                break;
            case "TcpIpSend":
            case "TcpIpSendIPV6":
                Add(pid, 0, size);
                break;
        }
    }

    private static int GetPid(TraceEvent traceEvent)
    {
        if (traceEvent.PayloadByName("PID") is int pid)
            return pid;
        if (traceEvent.PayloadByName("PID") is uint pidU)
            return (int)pidU;
        return traceEvent.ProcessID;
    }

    private static ulong GetSize(TraceEvent traceEvent)
    {
        if (traceEvent.PayloadByName("size") is int size)
            return (ulong)size;
        if (traceEvent.PayloadByName("size") is uint sizeU)
            return sizeU;
        return 0;
    }

    private void Add(int pid, ulong bytesIn, ulong bytesOut)
    {
        lock (_lock)
        {
            _totals.TryGetValue(pid, out var current);
            _totals[pid] = (current.In + bytesIn, current.Out + bytesOut);
        }
    }

    public Dictionary<int, (double DownloadRate, double UploadRate)> ComputeRates(
        Dictionary<int, (ulong In, ulong Out)> previous,
        double elapsedSeconds)
    {
        var rates = new Dictionary<int, (double DownloadRate, double UploadRate)>();
        var elapsed = Math.Max(elapsedSeconds, 0.001);

        lock (_lock)
        {
            foreach (var (pid, current) in _totals)
            {
                previous.TryGetValue(pid, out var prev);
                var down = current.In >= prev.In ? (current.In - prev.In) / elapsed : 0;
                var up = current.Out >= prev.Out ? (current.Out - prev.Out) / elapsed : 0;
                if (down > 0 || up > 0)
                    rates[pid] = (down, up);
                previous[pid] = current;
            }

            foreach (var pid in previous.Keys.Where(pid => !_totals.ContainsKey(pid)).ToList())
                previous.Remove(pid);
        }

        return rates;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        IsActive = false;

        try
        {
            _session?.Dispose();
        }
        catch
        {
            // ignored
        }

        _session = null;
    }
}
