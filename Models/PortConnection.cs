namespace NetworkWatch.Models;

/// <summary>
/// 扁平连接记录，用于端口管理视图（每行一条连接，含归属 PID）。
/// </summary>
public sealed class PortConnection
{
    public required string Protocol { get; init; }
    public required string LocalAddress { get; init; }
    public required int LocalPort { get; init; }
    public required string RemoteAddress { get; init; }
    public required int RemotePort { get; init; }
    public required string State { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";

    public string LocalEndpoint => $"{LocalAddress}:{LocalPort}";
    public string RemoteEndpoint => RemotePort > 0 ? $"{RemoteAddress}:{RemotePort}" : RemoteAddress;
    public string DisplayName => string.IsNullOrWhiteSpace(ProcessName) ? $"PID {ProcessId}" : $"{ProcessName} ({ProcessId})";
}
