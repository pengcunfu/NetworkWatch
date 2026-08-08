using System.Diagnostics;
using System.Runtime.InteropServices;
using NetworkWatch.Models;
using NetworkWatch.Native;

namespace NetworkWatch.Services;

/// <summary>
/// 枚举本机所有 TCP/UDP 连接（含归属 PID），用于端口管理视图。
/// 直接复用 <see cref="IpHelperNative"/> 的 P/Invoke，与流量监控相互独立。
/// </summary>
public sealed class PortManagerService
{
    private delegate uint IpTableGetter(IntPtr buffer, ref int size, bool order, int family, int tableClass, uint reserved);

    private readonly Dictionary<int, string> _nameCache = new();

    /// <summary>
    /// 获取当前所有 TCP/UDP(v4+v6) 连接的扁平列表。
    /// </summary>
    public IReadOnlyList<PortConnection> GetConnections()
    {
        var result = new List<PortConnection>();
        result.AddRange(ReadTcp(IpHelperNative.AfInet));
        result.AddRange(ReadTcp(IpHelperNative.AfInet6));
        result.AddRange(ReadUdp(IpHelperNative.AfInet));
        result.AddRange(ReadUdp(IpHelperNative.AfInet6));
        return result;
    }

    private IEnumerable<PortConnection> ReadTcp(int family)
    {
        var isV6 = family == IpHelperNative.AfInet6;
        var rowSize = isV6
            ? Marshal.SizeOf<IpHelperNative.MibTcp6RowOwnerPid>()
            : Marshal.SizeOf<IpHelperNative.MibTcpRowOwnerPid>();

        return ReadTable(family, IpHelperNative.TcpTableOwnerPidAll, IpHelperNative.GetExtendedTcpTable, rowSize, (ptr, i) =>
        {
            if (isV6)
            {
                var row = Marshal.PtrToStructure<IpHelperNative.MibTcp6RowOwnerPid>(ptr + 4 + i * rowSize);
                var localPort = IpHelperNative.PortFromNetwork(row.LocalPort);
                var remotePort = IpHelperNative.PortFromNetwork(row.RemotePort);
                return new PortConnection
                {
                    Protocol = "TCP6",
                    LocalAddress = IpHelperNative.FormatIPv6(row.LocalAddr),
                    LocalPort = localPort,
                    RemoteAddress = IpHelperNative.FormatIPv6(row.RemoteAddr),
                    RemotePort = remotePort,
                    State = IpHelperNative.TcpStateToString(row.State),
                    ProcessId = (int)row.OwningPid,
                    ProcessName = ResolveName((int)row.OwningPid)
                };
            }
            else
            {
                var row = Marshal.PtrToStructure<IpHelperNative.MibTcpRowOwnerPid>(ptr + 4 + i * rowSize);
                var localPort = IpHelperNative.PortFromNetwork(row.LocalPort);
                var remotePort = IpHelperNative.PortFromNetwork(row.RemotePort);
                return new PortConnection
                {
                    Protocol = "TCP",
                    LocalAddress = IpHelperNative.FormatIPv4(row.LocalAddr),
                    LocalPort = localPort,
                    RemoteAddress = IpHelperNative.FormatIPv4(row.RemoteAddr),
                    RemotePort = remotePort,
                    State = IpHelperNative.TcpStateToString(row.State),
                    ProcessId = (int)row.OwningPid,
                    ProcessName = ResolveName((int)row.OwningPid)
                };
            }
        });
    }

    private IEnumerable<PortConnection> ReadUdp(int family)
    {
        var isV6 = family == IpHelperNative.AfInet6;
        var rowSize = isV6
            ? Marshal.SizeOf<IpHelperNative.MibUdp6RowOwnerPid>()
            : Marshal.SizeOf<IpHelperNative.MibUdpRowOwnerPid>();

        return ReadTable(family, IpHelperNative.UdpTableOwnerPid, IpHelperNative.GetExtendedUdpTable, rowSize, (ptr, i) =>
        {
            if (isV6)
            {
                var row = Marshal.PtrToStructure<IpHelperNative.MibUdp6RowOwnerPid>(ptr + 4 + i * rowSize);
                var localPort = IpHelperNative.PortFromNetwork(row.LocalPort);
                return new PortConnection
                {
                    Protocol = "UDP6",
                    LocalAddress = IpHelperNative.FormatIPv6(row.LocalAddr),
                    LocalPort = localPort,
                    RemoteAddress = "*",
                    RemotePort = 0,
                    State = "N/A",
                    ProcessId = (int)row.OwningPid,
                    ProcessName = ResolveName((int)row.OwningPid)
                };
            }
            else
            {
                var row = Marshal.PtrToStructure<IpHelperNative.MibUdpRowOwnerPid>(ptr + 4 + i * rowSize);
                var localPort = IpHelperNative.PortFromNetwork(row.LocalPort);
                return new PortConnection
                {
                    Protocol = "UDP",
                    LocalAddress = IpHelperNative.FormatIPv4(row.LocalAddr),
                    LocalPort = localPort,
                    RemoteAddress = "*",
                    RemotePort = 0,
                    State = "N/A",
                    ProcessId = (int)row.OwningPid,
                    ProcessName = ResolveName((int)row.OwningPid)
                };
            }
        });
    }

    private static List<PortConnection> ReadTable(
        int family,
        int tableClass,
        IpTableGetter getTable,
        int rowSize,
        Func<IntPtr, int, PortConnection> mapRow)
    {
        var result = new List<PortConnection>();
        var size = 0;
        getTable(IntPtr.Zero, ref size, true, family, tableClass, 0);

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (getTable(buffer, ref size, true, family, tableClass, 0) != 0)
                return result;

            var count = Marshal.ReadInt32(buffer);
            for (var i = 0; i < count; i++)
                result.Add(mapRow(buffer, i));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    private string ResolveName(int pid)
    {
        if (pid == 0)
            return "System";

        if (_nameCache.TryGetValue(pid, out var cached))
            return cached;

        string name;
        try
        {
            using var proc = Process.GetProcessById(pid);
            name = proc.ProcessName;
        }
        catch
        {
            name = $"PID {pid}";
        }

        _nameCache[pid] = name;
        return name;
    }
}
