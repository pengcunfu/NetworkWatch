namespace NetworkWatch.Models;

public sealed class MonitorSnapshot
{
    public required DateTime Timestamp { get; init; }
    public required IReadOnlyList<ProcessNetworkInfo> Processes { get; init; }
    public int TotalConnections { get; init; }
    public double TotalDownloadRate { get; init; }
    public double TotalUploadRate { get; init; }
    public bool IsElevated { get; init; }
    public int TrafficStatsSuccessCount { get; init; }
    public int TrafficStatsEligibleCount { get; init; }
    public string? StatusHint { get; init; }
}
