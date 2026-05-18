namespace NetworkWatch.Models;

public sealed class ConnectionDetail
{
    public required string Protocol { get; init; }
    public required string LocalAddress { get; init; }
    public required int LocalPort { get; init; }
    public required string RemoteAddress { get; init; }
    public required int RemotePort { get; init; }
    public required string State { get; init; }
    public ulong BytesIn { get; init; }
    public ulong BytesOut { get; init; }
    public double DownloadRate { get; init; }
    public double UploadRate { get; init; }

    public string LocalEndpoint => $"{LocalAddress}:{LocalPort}";
    public string RemoteEndpoint => RemotePort > 0 ? $"{RemoteAddress}:{RemotePort}" : RemoteAddress;
    public string DownloadRateText => TrafficFormatter.FormatRate(DownloadRate);
    public string UploadRateText => TrafficFormatter.FormatRate(UploadRate);
    public string BytesInText => TrafficFormatter.FormatBytes(BytesIn);
    public string BytesOutText => TrafficFormatter.FormatBytes(BytesOut);
}
