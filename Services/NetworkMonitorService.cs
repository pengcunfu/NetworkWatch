using System.Diagnostics;
using System.Runtime.InteropServices;
using NetworkWatch.Helpers;
using NetworkWatch.Models;
using NetworkWatch.Native;
using static NetworkWatch.Native.IpHelperNative;

namespace NetworkWatch.Services;

public sealed class NetworkMonitorService : IDisposable
{
    private delegate uint IpTableGetter(IntPtr buffer, ref int size, bool order, int family, int tableClass, uint reserved);

    private readonly Dictionary<string, (ulong In, ulong Out)> _previousBytes = new();
    private readonly Dictionary<int, (ulong In, ulong Out)> _previousEtwTotals = new();
    private readonly HashSet<string> _statsInitialized = new();
    private readonly Dictionary<int, (string Name, string? Path)> _processCache = new();
    private readonly EtwTrafficProvider _etw = new();
    private DateTime _lastSample = DateTime.UtcNow;
    private bool _disposed;

    public event Action<MonitorSnapshot>? SnapshotUpdated;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public bool SortByTraffic { get; set; }

    public void Start()
    {
        _lastSample = DateTime.UtcNow;
        _etw.Start();
        _ = RunLoopAsync();
    }

    private async Task RunLoopAsync()
    {
        while (!_disposed)
        {
            try
            {
                var snapshot = CollectSnapshot();
                SnapshotUpdated?.Invoke(snapshot);
            }
            catch
            {
                // Keep polling even if a single sample fails.
            }

            try
            {
                await Task.Delay(PollInterval, CancellationToken.None);
            }
            catch
            {
                break;
            }
        }
    }

    public MonitorSnapshot CollectSnapshot()
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Max((now - _lastSample).TotalSeconds, 0.001);
        _lastSample = now;

        var connections = new List<RawConnection>();
        connections.AddRange(ReadTcpConnections());
        connections.AddRange(ReadUdpConnections());
        connections.AddRange(ReadTcp6Connections());
        connections.AddRange(ReadUdp6Connections());

        var activeKeys = new HashSet<string>();
        var enriched = new List<EnrichedConnection>();
        var statsSuccess = 0;
        var statsEligible = 0;

        foreach (var conn in connections)
        {
            var key = conn.Key;
            activeKeys.Add(key);

            ulong bytesIn = 0;
            ulong bytesOut = 0;
            if (conn.Protocol is "TCP" or "TCP6" && SupportsTrafficStats(conn.StateCode))
            {
                statsEligible++;
                if (TryReadTcpStats(conn, out var statsIn, out var statsOut))
                {
                    statsSuccess++;
                    bytesIn = statsIn;
                    bytesOut = statsOut;
                }
            }

            double downRate = 0;
            double upRate = 0;
            if (_previousBytes.TryGetValue(key, out var prev))
            {
                if (bytesIn >= prev.In)
                    downRate = (bytesIn - prev.In) / elapsed;
                if (bytesOut >= prev.Out)
                    upRate = (bytesOut - prev.Out) / elapsed;
            }

            _previousBytes[key] = (bytesIn, bytesOut);
            enriched.Add(new EnrichedConnection(conn, bytesIn, bytesOut, downRate, upRate));
        }

        PruneStaleKeys(activeKeys);

        var etwRates = _etw.IsActive
            ? _etw.ComputeRates(_previousEtwTotals, elapsed)
            : new Dictionary<int, (double DownloadRate, double UploadRate)>();

        var grouped = enriched
            .GroupBy(c => c.Raw.Pid)
            .Select(g => BuildProcessInfo(g.Key, g.ToList(), etwRates))
            .ToList();

        if (SortByTraffic)
        {
            grouped = grouped
                .OrderByDescending(p => p.DownloadRate + p.UploadRate)
                .ThenByDescending(p => p.ConnectionCount)
                .ToList();
        }

        var isAdmin = AdminHelper.IsRunningAsAdministrator();
        string? hint = null;
        if (statsSuccess == 0 && !_etw.IsActive)
        {
            hint = isAdmin
                ? "流量统计暂不可用，请稍候或重启应用"
                : "点击「管理员模式」可临时提权，以启用完整流量统计";
        }
        else if (_etw.IsActive && statsSuccess == 0)
            hint = "进程流量来自 ETW；单连接速率可能不可用";
        else if (!_etw.IsActive && statsSuccess > 0 && !isAdmin)
            hint = "当前为普通模式；点击「管理员模式」可临时启用 ETW 增强统计";

        return new MonitorSnapshot
        {
            Timestamp = now,
            Processes = grouped,
            TotalConnections = enriched.Count,
            TotalDownloadRate = grouped.Sum(p => p.DownloadRate),
            TotalUploadRate = grouped.Sum(p => p.UploadRate),
            IsElevated = isAdmin,
            TrafficStatsSuccessCount = statsSuccess + etwRates.Count,
            TrafficStatsEligibleCount = Math.Max(statsEligible, grouped.Count(p => p.DownloadRate + p.UploadRate > 0)),
            StatusHint = hint
        };
    }

    private static bool SupportsTrafficStats(uint state) =>
        state is 5 or 6 or 7 or 8 or 9 or 10 or 11;

    private ProcessNetworkInfo BuildProcessInfo(
        int pid,
        List<EnrichedConnection> items,
        Dictionary<int, (double DownloadRate, double UploadRate)> etwRates)
    {
        var (name, path) = ResolveProcess(pid);
        var details = items.Select(c => new ConnectionDetail
        {
            Protocol = c.Raw.Protocol,
            LocalAddress = c.Raw.LocalAddress,
            LocalPort = c.Raw.LocalPort,
            RemoteAddress = c.Raw.RemoteAddress,
            RemotePort = c.Raw.RemotePort,
            State = c.Raw.State,
            BytesIn = c.BytesIn,
            BytesOut = c.BytesOut,
            DownloadRate = c.DownloadRate,
            UploadRate = c.UploadRate
        }).ToList();

        if (SortByTraffic)
        {
            details = details
                .OrderByDescending(d => d.DownloadRate + d.UploadRate)
                .ToList();
        }

        var connDown = details.Sum(d => d.DownloadRate);
        var connUp = details.Sum(d => d.UploadRate);
        etwRates.TryGetValue(pid, out var etw);
        var down = Math.Max(connDown, etw.DownloadRate);
        var up = Math.Max(connUp, etw.UploadRate);

        if (down > connDown || up > connUp)
            DistributeEtwRatesToConnections(details, down - connDown, up - connUp);

        return new ProcessNetworkInfo
        {
            ProcessId = pid,
            ProcessName = name,
            ExecutablePath = path,
            ConnectionCount = details.Count,
            TotalBytesIn = details.Aggregate(0UL, (a, d) => a + d.BytesIn),
            TotalBytesOut = details.Aggregate(0UL, (a, d) => a + d.BytesOut),
            DownloadRate = down,
            UploadRate = up,
            Connections = details
        };
    }

    private static void DistributeEtwRatesToConnections(
        List<ConnectionDetail> details,
        double extraDown,
        double extraUp)
    {
        if (details.Count == 0 || (extraDown <= 0 && extraUp <= 0))
            return;

        var indices = Enumerable.Range(0, details.Count)
            .Where(i => details[i].State is "ESTABLISHED" or "CLOSE_WAIT" or "FIN_WAIT1" or "FIN_WAIT2")
            .ToList();
        if (indices.Count == 0)
            indices = Enumerable.Range(0, details.Count).ToList();

        var shareDown = extraDown / indices.Count;
        var shareUp = extraUp / indices.Count;
        foreach (var i in indices)
        {
            var d = details[i];
            details[i] = new ConnectionDetail
            {
                Protocol = d.Protocol,
                LocalAddress = d.LocalAddress,
                LocalPort = d.LocalPort,
                RemoteAddress = d.RemoteAddress,
                RemotePort = d.RemotePort,
                State = d.State,
                BytesIn = d.BytesIn,
                BytesOut = d.BytesOut,
                DownloadRate = d.DownloadRate + shareDown,
                UploadRate = d.UploadRate + shareUp
            };
        }
    }

    private (string Name, string? Path) ResolveProcess(int pid)
    {
        if (pid == 0)
            return ("System", null);

        if (_processCache.TryGetValue(pid, out var cached))
            return cached;

        try
        {
            using var proc = Process.GetProcessById(pid);
            var path = TryGetExecutablePath(proc);
            var info = (proc.ProcessName, path);
            _processCache[pid] = info;
            return info;
        }
        catch
        {
            var info = ($"PID {pid}", (string?)null);
            _processCache[pid] = info;
            return info;
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private void PruneStaleKeys(HashSet<string> activeKeys)
    {
        var stale = _previousBytes.Keys.Where(k => !activeKeys.Contains(k)).ToList();
        foreach (var key in stale)
        {
            _previousBytes.Remove(key);
            _statsInitialized.Remove(key);
        }
    }

    private bool TryReadTcpStats(RawConnection conn, out ulong bytesIn, out ulong bytesOut)
    {
        bytesIn = 0;
        bytesOut = 0;

        if (!_statsInitialized.Contains(conn.Key))
        {
            TryEnableTcpStats(conn);
            _statsInitialized.Add(conn.Key);
        }

        var rw = new TcpEstatsDataRwV0 { EnableCollection = 1 };
        var rod = new TcpEstatsDataRodV0();
        var rwSize = TcpEstatsDataRwV0Size;
        var rodSize = TcpEstatsDataRodV0Size;

        if (!QueryTcpStats(conn, ref rw, rwSize, ref rod, rodSize))
            return false;

        bytesIn = rod.DataBytesIn;
        bytesOut = rod.DataBytesOut;
        if (bytesIn == 0 && bytesOut == 0)
        {
            bytesIn = rod.ThruBytesReceived;
            bytesOut = rod.ThruBytesAcked;
        }

        return true;
    }

    private static void TryEnableTcpStats(RawConnection conn)
    {
        var rw = new TcpEstatsDataRwV0 { EnableCollection = 1 };
        var rwSize = TcpEstatsDataRwV0Size;

        if (conn.Protocol == "TCP")
        {
            var row = ToTcpRow(conn);
            _ = SetPerTcpConnectionEStats(
                ref row,
                TcpEstatsType.TcpConnectionEstatsData,
                ref rw, 0, rwSize,
                IntPtr.Zero, 0, 0,
                IntPtr.Zero, 0, 0);
        }
        else
        {
            var row6 = ToTcp6Row(conn);
            _ = SetPerTcp6ConnectionEStats(
                ref row6,
                TcpEstatsType.TcpConnectionEstatsData,
                ref rw, 0, rwSize,
                IntPtr.Zero, 0, 0,
                IntPtr.Zero, 0, 0);
        }
    }

    private static bool QueryTcpStats(
        RawConnection conn,
        ref TcpEstatsDataRwV0 rw,
        uint rwSize,
        ref TcpEstatsDataRodV0 rod,
        uint rodSize)
    {
        if (conn.Protocol == "TCP")
        {
            var row = ToTcpRow(conn);
            if (GetPerTcpConnectionEStats(
                    ref row,
                    TcpEstatsType.TcpConnectionEstatsData,
                    ref rw, 0, rwSize,
                    IntPtr.Zero, 0, 0,
                    ref rod, 0, rodSize) != 0)
                return false;
        }
        else
        {
            var row6 = ToTcp6Row(conn);
            if (GetPerTcp6ConnectionEStats(
                    ref row6,
                    TcpEstatsType.TcpConnectionEstatsData,
                    ref rw, 0, rwSize,
                    IntPtr.Zero, 0, 0,
                    ref rod, 0, rodSize) != 0)
                return false;
        }

        return true;
    }

    private static MibTcpRow ToTcpRow(RawConnection conn) => new()
    {
        State = conn.StateCode,
        LocalAddr = conn.LocalAddrV4,
        LocalPort = conn.LocalPortRaw,
        RemoteAddr = conn.RemoteAddrV4,
        RemotePort = conn.RemotePortRaw
    };

    private static MibTcp6Row ToTcp6Row(RawConnection conn) => new()
    {
        LocalAddr = conn.LocalAddrV6 ?? new byte[16],
        LocalScopeId = conn.LocalScopeId,
        LocalPort = conn.LocalPortRaw,
        RemoteAddr = conn.RemoteAddrV6 ?? new byte[16],
        RemoteScopeId = conn.RemoteScopeId,
        RemotePort = conn.RemotePortRaw,
        State = conn.StateCode
    };

    private static IEnumerable<RawConnection> ReadTcpConnections() =>
        ReadTable(
            AfInet,
            TcpTableOwnerPidAll,
            GetExtendedTcpTable,
            Marshal.SizeOf<MibTcpRowOwnerPid>(),
            (ptr, i, size) =>
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(ptr + 4 + i * size);
                var localPort = PortFromNetwork(row.LocalPort);
                var remotePort = PortFromNetwork(row.RemotePort);
                return new RawConnection
                {
                    Key = $"TCP:{row.OwningPid}:{FormatIPv4(row.LocalAddr)}:{localPort}:{FormatIPv4(row.RemoteAddr)}:{remotePort}:{row.State}",
                    Pid = (int)row.OwningPid,
                    Protocol = "TCP",
                    LocalAddress = FormatIPv4(row.LocalAddr),
                    LocalPort = localPort,
                    LocalPortRaw = row.LocalPort,
                    RemoteAddress = FormatIPv4(row.RemoteAddr),
                    RemotePort = remotePort,
                    RemotePortRaw = row.RemotePort,
                    State = TcpStateToString(row.State),
                    StateCode = row.State,
                    LocalAddrV4 = row.LocalAddr,
                    RemoteAddrV4 = row.RemoteAddr
                };
            });

    private static IEnumerable<RawConnection> ReadTcp6Connections() =>
        ReadTable(
            AfInet6,
            TcpTableOwnerPidAll,
            GetExtendedTcpTable,
            Marshal.SizeOf<MibTcp6RowOwnerPid>(),
            (ptr, i, size) =>
            {
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(ptr + 4 + i * size);
                var localPort = PortFromNetwork(row.LocalPort);
                var remotePort = PortFromNetwork(row.RemotePort);
                return new RawConnection
                {
                    Key = $"TCP6:{row.OwningPid}:{FormatIPv6(row.LocalAddr)}:{localPort}:{FormatIPv6(row.RemoteAddr)}:{remotePort}:{row.State}",
                    Pid = (int)row.OwningPid,
                    Protocol = "TCP6",
                    LocalAddress = FormatIPv6(row.LocalAddr),
                    LocalPort = localPort,
                    LocalPortRaw = row.LocalPort,
                    RemoteAddress = FormatIPv6(row.RemoteAddr),
                    RemotePort = remotePort,
                    RemotePortRaw = row.RemotePort,
                    State = TcpStateToString(row.State),
                    StateCode = row.State,
                    LocalAddrV6 = row.LocalAddr,
                    RemoteAddrV6 = row.RemoteAddr,
                    LocalScopeId = row.LocalScopeId,
                    RemoteScopeId = row.RemoteScopeId
                };
            });

    private static IEnumerable<RawConnection> ReadUdpConnections() =>
        ReadTable(
            AfInet,
            UdpTableOwnerPid,
            GetExtendedUdpTable,
            Marshal.SizeOf<MibUdpRowOwnerPid>(),
            (ptr, i, size) =>
            {
                var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(ptr + 4 + i * size);
                var localPort = PortFromNetwork(row.LocalPort);
                return new RawConnection
                {
                    Key = $"UDP:{row.OwningPid}:{FormatIPv4(row.LocalAddr)}:{localPort}",
                    Pid = (int)row.OwningPid,
                    Protocol = "UDP",
                    LocalAddress = FormatIPv4(row.LocalAddr),
                    LocalPort = localPort,
                    RemoteAddress = "*",
                    RemotePort = 0,
                    State = "N/A"
                };
            });

    private static IEnumerable<RawConnection> ReadUdp6Connections() =>
        ReadTable(
            AfInet6,
            UdpTableOwnerPid,
            GetExtendedUdpTable,
            Marshal.SizeOf<MibUdp6RowOwnerPid>(),
            (ptr, i, size) =>
            {
                var row = Marshal.PtrToStructure<MibUdp6RowOwnerPid>(ptr + 4 + i * size);
                var localPort = PortFromNetwork(row.LocalPort);
                return new RawConnection
                {
                    Key = $"UDP6:{row.OwningPid}:{FormatIPv6(row.LocalAddr)}:{localPort}",
                    Pid = (int)row.OwningPid,
                    Protocol = "UDP6",
                    LocalAddress = FormatIPv6(row.LocalAddr),
                    LocalPort = localPort,
                    RemoteAddress = "*",
                    RemotePort = 0,
                    State = "N/A",
                    LocalAddrV6 = row.LocalAddr,
                    LocalScopeId = row.LocalScopeId
                };
            });

    private static List<RawConnection> ReadTable(
        int addressFamily,
        int tableClass,
        IpTableGetter getter,
        int rowSize,
        Func<IntPtr, int, int, RawConnection> mapRow)
    {
        var result = new List<RawConnection>();
        var size = 0;
        _ = getter(IntPtr.Zero, ref size, true, addressFamily, tableClass, 0);

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var ret = getter(buffer, ref size, true, addressFamily, tableClass, 0);
            if (ret != 0)
                return result;

            var count = Marshal.ReadInt32(buffer);
            for (var i = 0; i < count; i++)
                result.Add(mapRow(buffer, i, rowSize));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    public void Dispose()
    {
        _disposed = true;
        _etw.Dispose();
    }

    private sealed class RawConnection
    {
        public required string Key { get; init; }
        public int Pid { get; init; }
        public required string Protocol { get; init; }
        public required string LocalAddress { get; init; }
        public int LocalPort { get; init; }
        public uint LocalPortRaw { get; init; }
        public required string RemoteAddress { get; init; }
        public int RemotePort { get; init; }
        public uint RemotePortRaw { get; init; }
        public required string State { get; init; }
        public uint StateCode { get; init; }
        public uint LocalAddrV4 { get; init; }
        public uint RemoteAddrV4 { get; init; }
        public byte[]? LocalAddrV6 { get; init; }
        public byte[]? RemoteAddrV6 { get; init; }
        public uint LocalScopeId { get; init; }
        public uint RemoteScopeId { get; init; }
    }

    private readonly record struct EnrichedConnection(
        RawConnection Raw,
        ulong BytesIn,
        ulong BytesOut,
        double DownloadRate,
        double UploadRate);
}
