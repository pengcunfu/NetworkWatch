using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using NetworkWatch.Models;

namespace NetworkWatch.Services;

/// <summary>
/// 采集单个进程的详细信息（CPU、内存、线程、句柄、命令行等），对应端口管理器的进程详情对话框。
/// </summary>
public static class ProcessDetailService
{
    /// <summary>
    /// 采集指定 PID 的进程详情。CPU 采样会阻塞约 100ms，建议在后台线程调用。
    /// </summary>
    public static ProcessDetailInfo Collect(int pid, PortConnection? connection)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);

            var exePath = SafeString(() => proc.MainModule?.FileName);
            var startTime = SafeString(() => proc.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            var responding = SafeString(() => proc.Responding ? "响应" : "无响应");

            var cpuUsage = MeasureCpuUsage(proc);
            var userTime = SafeString(() => FormatTime(proc.UserProcessorTime));
            var privilegedTime = SafeString(() => FormatTime(proc.PrivilegedProcessorTime));
            var affinity = SafeString(() => FormatAffinity(proc.ProcessorAffinity));

            var (pageFaults, peakWs) = ReadMemoryInfo(proc);
            var memPercent = ComputeMemoryPercent(proc.WorkingSet64);

            var threads = SafeString(() => proc.Threads.Count.ToString());
            var handles = SafeString(() => proc.HandleCount.ToString());
            var commandLine = SafeString(() => GetCommandLine(pid)) ?? "N/A";
            var workingDir = "N/A";

            return new ProcessDetailInfo
            {
                Pid = pid.ToString(),
                Name = SafeString(() => proc.ProcessName),
                ExePath = string.IsNullOrEmpty(exePath) ? "N/A" : exePath,
                StartTime = startTime,
                Responding = responding,
                CpuUsage = $"{cpuUsage:0.0} %",
                UserTime = userTime,
                PrivilegedTime = privilegedTime,
                ProcessorAffinity = affinity,
                WorkingSet = TrafficFormatter.FormatBytes((ulong)proc.WorkingSet64),
                VirtualMemory = TrafficFormatter.FormatBytes((ulong)proc.VirtualMemorySize64),
                PeakWorkingSet = peakWs,
                MemoryPercent = memPercent,
                PageFaults = pageFaults,
                Protocol = connection?.Protocol ?? "N/A",
                LocalEndpoint = connection?.LocalEndpoint ?? "N/A",
                RemoteEndpoint = string.IsNullOrEmpty(connection?.RemoteEndpoint) ? "N/A" : connection.RemoteEndpoint,
                CommandLine = string.IsNullOrWhiteSpace(commandLine) ? "N/A" : commandLine,
                WorkingDirectory = workingDir,
                ThreadCount = threads,
                HandleCount = handles
            };
        }
        catch (ArgumentException)
        {
            return new ProcessDetailInfo { Pid = pid.ToString(), ErrorMessage = "进程不存在" };
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new ProcessDetailInfo { Pid = pid.ToString(), ErrorMessage = "权限不足，无法访问进程信息" };
        }
    }

    /// <summary>估算瞬时 CPU 使用率：采样两次 TotalProcessorTime，按 (增量 / (耗时 × 核心数)) × 100。</summary>
    private static double MeasureCpuUsage(Process proc)
    {
        try
        {
            var t1 = proc.TotalProcessorTime;
            var sw = Stopwatch.StartNew();
            Thread.Sleep(100);
            var t2 = proc.TotalProcessorTime;
            sw.Stop();

            var elapsed = sw.Elapsed.TotalSeconds;
            if (elapsed <= 0)
                return 0;

            var cores = Math.Max(1, Environment.ProcessorCount);
            return Math.Min(((t2 - t1).TotalSeconds / (elapsed * cores)) * 100.0, 100.0);
        }
        catch
        {
            return 0;
        }
    }

    private static string ComputeMemoryPercent(long workingSet)
    {
        try
        {
            var total = GetTotalPhysicalMemory();
            if (total <= 0)
                return "N/A";
            return $"{workingSet * 100.0 / total:0.00} %";
        }
        catch
        {
            return "N/A";
        }
    }

    private static (string PageFaults, string PeakWorkingSet) ReadMemoryInfo(Process proc)
    {
        try
        {
            var counters = new ProcessMemoryCounters
            {
                cb = (uint)Marshal.SizeOf<ProcessMemoryCounters>()
            };

            if (GetProcessMemoryInfo(proc.Handle, ref counters, counters.cb))
            {
                var peak = counters.PeakWorkingSetSize == IntPtr.Zero
                    ? TrafficFormatter.FormatBytes((ulong)proc.PeakWorkingSet64)
                    : TrafficFormatter.FormatBytes((ulong)counters.PeakWorkingSetSize);
                return ($"{counters.PageFaultCount:N0}", peak);
            }
        }
        catch
        {
            // 降级到 .NET 自带字段。
        }

        return ("N/A", TrafficFormatter.FormatBytes((ulong)proc.PeakWorkingSet64));
    }

    private static string? GetCommandLine(int pid)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
        foreach (var obj in searcher.Get())
            return obj["CommandLine"]?.ToString();
        return null;
    }

    private static string SafeString(Func<string?> getter)
    {
        try
        {
            return getter() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string FormatTime(TimeSpan ts) => $"{ts.TotalSeconds:0.00} 秒";

    private static string FormatAffinity(IntPtr mask)
    {
        var value = mask.ToInt64();
        var cores = 0;
        for (var i = 0; i < 64; i++)
        {
            if ((value & (1L << i)) != 0)
                cores++;
        }
        return $"{cores} 核 (0x{value:X})";
    }

    private static ulong GetTotalPhysicalMemory()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.ullTotalPhys : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint cb;
        public uint PageFaultCount;
        public IntPtr PeakWorkingSetSize;
        public IntPtr WorkingSetSize;
        public IntPtr QuotaPeakPagedPoolUsage;
        public IntPtr QuotaPagedPoolUsage;
        public IntPtr QuotaPeakNonPagedPoolUsage;
        public IntPtr QuotaNonPagedPoolUsage;
        public IntPtr PagefileUsage;
        public IntPtr PeakPagefileUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr hProcess, ref ProcessMemoryCounters counters, uint cb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
