namespace NetworkWatch.Models;

public sealed class ProcessNetworkInfo
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string? ExecutablePath { get; init; }
    public int ConnectionCount { get; init; }
    public ulong TotalBytesIn { get; init; }
    public ulong TotalBytesOut { get; init; }
    public double DownloadRate { get; init; }
    public double UploadRate { get; init; }
    public IReadOnlyList<ConnectionDetail> Connections { get; init; } = [];

    public string DownloadRateText => TrafficFormatter.FormatRate(DownloadRate);
    public string UploadRateText => TrafficFormatter.FormatRate(UploadRate);
    public string TotalBytesInText => TrafficFormatter.FormatBytes(TotalBytesIn);
    public string TotalBytesOutText => TrafficFormatter.FormatBytes(TotalBytesOut);
    public string DisplayName => string.IsNullOrWhiteSpace(ProcessName) ? $"PID {ProcessId}" : ProcessName;
}
