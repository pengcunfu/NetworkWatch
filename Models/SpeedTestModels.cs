namespace NetworkWatch.Models;

public enum SpeedTestPhase
{
    Idle,
    Ping,
    Downloading,
    Uploading,
    Completed,
    Cancelled,
    Failed
}

public sealed class SpeedTestProgress
{
    public required SpeedTestPhase Phase { get; init; }
    public double Percent { get; init; }
    public double OverallPercent { get; init; }
    public long BytesTransferred { get; init; }
    public double CurrentSpeed { get; init; }
    public double ElapsedSeconds { get; init; }
    public string? Message { get; init; }
}

public sealed class SpeedTestResult
{
    public required double DownloadBytesPerSecond { get; init; }
    public required double UploadBytesPerSecond { get; init; }
    public double PingMs { get; init; }
    public double JitterMs { get; init; }
    public string ServerName { get; init; } = "Cloudflare";
    public double TotalSeconds { get; init; }
    public string? UploadMessage { get; init; }

    public string DownloadText => TrafficFormatter.FormatRate(DownloadBytesPerSecond);
    public string UploadText => TrafficFormatter.FormatRate(UploadBytesPerSecond);
    public string PingText => $"{PingMs:0} ms";
    public string JitterText => $"{JitterMs:0} ms";
    public string DurationText => $"{TotalSeconds:0.0} 秒";
}
