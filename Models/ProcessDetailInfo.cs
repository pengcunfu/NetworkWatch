namespace NetworkWatch.Models;

/// <summary>
/// 进程详情（对应 PortManager 进程详情对话框的五组信息）。
/// 所有字段已格式化为可读字符串，供 UI 直接绑定。
/// </summary>
public sealed class ProcessDetailInfo
{
    // 基本信息
    public string Pid { get; init; } = "";
    public string Name { get; init; } = "";
    public string ExePath { get; init; } = "";
    public string StartTime { get; init; } = "";
    public string Responding { get; init; } = "";

    // CPU 信息
    public string CpuUsage { get; init; } = "";
    public string UserTime { get; init; } = "";
    public string PrivilegedTime { get; init; } = "";
    public string ProcessorAffinity { get; init; } = "";

    // 内存信息
    public string WorkingSet { get; init; } = "";
    public string VirtualMemory { get; init; } = "";
    public string PeakWorkingSet { get; init; } = "";
    public string MemoryPercent { get; init; } = "";
    public string PageFaults { get; init; } = "";

    // 连接信息
    public string Protocol { get; init; } = "";
    public string LocalEndpoint { get; init; } = "";
    public string RemoteEndpoint { get; init; } = "";

    // 其他信息
    public string CommandLine { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string ThreadCount { get; init; } = "";
    public string HandleCount { get; init; } = "";

    public string ErrorMessage { get; init; } = "";
}
