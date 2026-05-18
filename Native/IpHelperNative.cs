using System.Net;
using System.Runtime.InteropServices;

namespace NetworkWatch.Native;

internal static class IpHelperNative
{
    public const int AfInet = 2;
    public const int AfInet6 = 23;

    public const int TcpTableOwnerPidAll = 5;
    public const int UdpTableOwnerPid = 1;

    public enum TcpEstatsType
    {
        TcpConnectionEstatsData = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcpTableOwnerPid
    {
        public uint NumEntries;
        public MibTcpRowOwnerPid Table;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibUdpRowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibUdpTableOwnerPid
    {
        public uint NumEntries;
        public MibUdpRowOwnerPid Table;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;

        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;

        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcp6TableOwnerPid
    {
        public uint NumEntries;
        public MibTcp6RowOwnerPid Table;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;

        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;

        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibUdp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;

        public uint LocalScopeId;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibUdp6TableOwnerPid
    {
        public uint NumEntries;
        public MibUdp6RowOwnerPid Table;
    }

    /// <summary>Must match TCP_ESTATS_DATA_RW_v0 (BOOLEAN EnableCollection).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TcpEstatsDataRwV0
    {
        [MarshalAs(UnmanagedType.U1)]
        public byte EnableCollection;
    }

    /// <summary>Must match TCP_ESTATS_DATA_ROD_v0 (88 bytes, pack 1).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TcpEstatsDataRodV0
    {
        public ulong DataBytesOut;
        public ulong DataSegsOut;
        public ulong DataBytesIn;
        public ulong DataSegsIn;
        public ulong SegsOut;
        public ulong SegsIn;
        public uint SoftErrors;
        public uint SoftErrorReason;
        public uint SndUna;
        public uint SndNxt;
        public uint SndMax;
        public ulong ThruBytesAcked;
        public uint RcvNxt;
        public ulong ThruBytesReceived;
    }

    public static readonly uint TcpEstatsDataRwV0Size = (uint)Marshal.SizeOf<TcpEstatsDataRwV0>();
    public static readonly uint TcpEstatsDataRodV0Size = (uint)Marshal.SizeOf<TcpEstatsDataRodV0>();

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int dwSize,
        [MarshalAs(UnmanagedType.Bool)] bool sort,
        int ipVersion,
        int tblClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int dwSize,
        [MarshalAs(UnmanagedType.Bool)] bool sort,
        int ipVersion,
        int tblClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint SetPerTcpConnectionEStats(
        ref MibTcpRow row,
        TcpEstatsType estatsType,
        ref TcpEstatsDataRwV0 rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        IntPtr rod,
        uint rodVersion,
        uint rodSize);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetPerTcpConnectionEStats(
        ref MibTcpRow row,
        TcpEstatsType estatsType,
        ref TcpEstatsDataRwV0 rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        ref TcpEstatsDataRodV0 rod,
        uint rodVersion,
        uint rodSize);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint SetPerTcp6ConnectionEStats(
        ref MibTcp6Row row,
        TcpEstatsType estatsType,
        ref TcpEstatsDataRwV0 rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        IntPtr rod,
        uint rodVersion,
        uint rodSize);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetPerTcp6ConnectionEStats(
        ref MibTcp6Row row,
        TcpEstatsType estatsType,
        ref TcpEstatsDataRwV0 rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        ref TcpEstatsDataRodV0 rod,
        uint rodVersion,
        uint rodSize);

    public static ushort PortFromNetwork(uint port) =>
        (ushort)(((port & 0xFF00) >> 8) | ((port & 0x00FF) << 8));

    public static string FormatIPv4(uint addr) => new IPAddress(addr).ToString();

    public static string FormatIPv6(byte[] addr)
    {
        if (addr is null || addr.Length != 16)
            return "?";
        return new IPAddress(addr).ToString();
    }

    public static string TcpStateToString(uint state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTEN",
        3 => "SYN_SENT",
        4 => "SYN_RECEIVED",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT1",
        7 => "FIN_WAIT2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "LAST_ACK",
        11 => "TIME_WAIT",
        12 => "DELETE_TCB",
        _ => state.ToString()
    };
}
