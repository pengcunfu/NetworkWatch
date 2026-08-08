using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using NetworkWatch.Models;

namespace NetworkWatch.Services;

/// <summary>
/// 基于公共测速节点的 HTTP 下载/上传测速服务。
/// 下载阶段会自动在候选节点间切换，上传阶段使用 Cloudflare 官方测速端点。
/// </summary>
public sealed class SpeedTestService : IDisposable
{
    private const int StreamCount = 4;
    private const long DownloadTargetBytes = 100L * 1024 * 1024;
    private const long UploadTargetBytes = 100L * 1024 * 1024;
    private const int ChunkSize = 256 * 1024;
    private const double PingSpan = 0.05;
    private const double DownloadSpan = 0.50;
    private const double UploadSpan = 0.45;
    private static readonly TimeSpan PhaseTimeLimit = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(6);

    private static readonly string PingUrl = "https://speed.cloudflare.com/cdn-cgi/trace";
    private static readonly string UploadUrl = "https://speed.cloudflare.com/__up";

    private static readonly (string Name, string Url, bool SupportsByteParam)[] DownloadCandidates =
    [
        ("Cloudflare", "https://speed.cloudflare.com/__down?bytes={0}", true),
        ("Linode (Dallas)", "https://speedtest.dallas.linode.com/100MB-dallas.bin", false),
        ("Tele2 (Sweden)", "http://speedtest.tele2.net/100MB.zip", false)
    ];

    private readonly HttpClient _http;
    private bool _disposed;
    private long _downloadPhaseBytes;

    public SpeedTestService()
    {
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = ConnectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            UseCookies = false
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NetworkWatch/1.0 (speedtest)");
    }

    public async Task<SpeedTestResult> RunAsync(
        CancellationToken token,
        Action<SpeedTestProgress>? onProgress = null)
    {
        var sw = Stopwatch.StartNew();

        Report(onProgress, SpeedTestPhase.Ping, 0, 0, 0, 0, 0, "正在测量网络延迟...");
        var (ping, jitter) = await MeasureLatencyAsync(token).ConfigureAwait(false);
        Report(onProgress, SpeedTestPhase.Ping, 1, PingSpan, 0, 0, 0,
            ping > 0 ? $"延迟 {ping:0} ms" : "延迟测量失败，继续测速");

        var (downloadSpeed, downloadNode) = await RunDownloadAsync(token, onProgress, sw).ConfigureAwait(false);

        double uploadSpeed;
        string? uploadMessage = null;
        try
        {
            uploadSpeed = await RunUploadAsync(token, onProgress, sw).ConfigureAwait(false);
            if (uploadSpeed <= 0)
                uploadMessage = "上传测速未取得有效数据";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            uploadSpeed = 0;
            uploadMessage = $"上传测速失败：{ex.Message}";
        }

        return new SpeedTestResult
        {
            DownloadBytesPerSecond = downloadSpeed,
            UploadBytesPerSecond = uploadSpeed,
            PingMs = ping,
            JitterMs = jitter,
            ServerName = downloadNode,
            TotalSeconds = sw.Elapsed.TotalSeconds,
            UploadMessage = uploadMessage
        };
    }

    private async Task<(double Ping, double Jitter)> MeasureLatencyAsync(CancellationToken token)
    {
        var samples = new List<double>(5);
        for (var i = 0; i < 5; i++)
        {
            token.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            try
            {
                using var response = await _http
                    .GetAsync(PingUrl, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);
                await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            catch (HttpRequestException)
            {
                // 单次采样失败不影响整体延迟测量。
            }

            await Task.Delay(100, token).ConfigureAwait(false);
        }

        if (samples.Count == 0)
            return (0, 0);

        var ping = samples.Min();
        var jitter = samples.Count > 1
            ? samples.Zip(samples.Skip(1), (a, b) => Math.Abs(a - b)).Average()
            : 0;
        return (ping, jitter);
    }

    private async Task<(double Speed, string Node)> RunDownloadAsync(
        CancellationToken token,
        Action<SpeedTestProgress>? onProgress,
        Stopwatch sw)
    {
        _downloadPhaseBytes = 0;
        Exception? lastError = null;
        foreach (var candidate in DownloadCandidates)
        {
            token.ThrowIfCancellationRequested();
            Report(onProgress, SpeedTestPhase.Downloading, 0, PingSpan, 0, 0, 0,
                $"正在连接 {candidate.Name}...");
            try
            {
                var speed = await RunDownloadCandidateAsync(candidate, token, onProgress, sw)
                    .ConfigureAwait(false);
                return (speed, candidate.Name);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Report(onProgress, SpeedTestPhase.Downloading, 0, PingSpan, 0, 0, 0,
                    $"{candidate.Name} 不可用，正在尝试备用节点...");
            }
        }

        throw new HttpRequestException("所有下载测速节点均不可用", lastError);
    }

    private async Task<double> RunDownloadCandidateAsync(
        (string Name, string Url, bool SupportsByteParam) candidate,
        CancellationToken token,
        Action<SpeedTestProgress>? onProgress,
        Stopwatch sw)
    {
        var tracker = new RateTracker(sw.Elapsed.TotalSeconds);
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        phaseCts.CancelAfter(PhaseTimeLimit);

        var reporter = ReportLoopAsync(phaseCts.Token, onProgress, sw, tracker,
            SpeedTestPhase.Downloading, DownloadTargetBytes, PingSpan, DownloadSpan,
            () => Interlocked.Read(ref _downloadPhaseBytes));

        var perStreamBytes = DownloadTargetBytes / StreamCount;
        var urls = Enumerable.Range(0, StreamCount)
            .Select(_ => candidate.SupportsByteParam
                ? string.Format(candidate.Url, perStreamBytes)
                : candidate.Url);

        var streams = urls
            .Select(url => DownloadOneStreamAsync(url, token, tracker, sw, phaseCts.Token))
            .ToArray();

        try
        {
            await Task.WhenAll(streams).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // 达到单阶段时间上限，以已传输数据结算。
        }
        finally
        {
            phaseCts.Cancel();
            try
            {
                await reporter.ConfigureAwait(false);
            }
            catch
            {
                // 报告任务随取消而结束。
            }
        }

        var now = sw.Elapsed.TotalSeconds;
        var elapsed = Math.Max(now - tracker.StartTime, 0.5);
        var rate = tracker.Total / elapsed;
        Report(onProgress, SpeedTestPhase.Downloading, 1, PingSpan + DownloadSpan,
            _downloadPhaseBytes, rate, elapsed,
            $"{candidate.Name} 下载完成 {TrafficFormatter.FormatRate(rate)}");
        return rate;
    }

    private async Task DownloadOneStreamAsync(
        string url,
        CancellationToken token,
        RateTracker tracker,
        Stopwatch sw,
        CancellationToken phaseToken)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, phaseToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(phaseToken).ConfigureAwait(false);
        var buffer = new byte[ChunkSize];
        while (tracker.Total < DownloadTargetBytes &&
               !phaseToken.IsCancellationRequested &&
               !token.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, phaseToken).ConfigureAwait(false);
            if (read == 0)
                break;
            tracker.Add(sw.Elapsed.TotalSeconds, read);
            Interlocked.Add(ref _downloadPhaseBytes, read);
        }
    }

    private async Task<double> RunUploadAsync(
        CancellationToken token,
        Action<SpeedTestProgress>? onProgress,
        Stopwatch sw)
    {
        Report(onProgress, SpeedTestPhase.Uploading, 0, PingSpan + DownloadSpan, 0, 0, 0,
            "正在连接上传端点...");
        var tracker = new RateTracker(sw.Elapsed.TotalSeconds);
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        phaseCts.CancelAfter(PhaseTimeLimit);

        var reporter = ReportLoopAsync(phaseCts.Token, onProgress, sw, tracker,
            SpeedTestPhase.Uploading, UploadTargetBytes, PingSpan + DownloadSpan, UploadSpan);

        var streams = Enumerable.Range(0, StreamCount)
            .Select(_ => UploadOneStreamAsync(token, tracker, sw, phaseCts.Token))
            .ToArray();

        try
        {
            await Task.WhenAll(streams).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // 达到单阶段时间上限，以已上传数据结算。
        }
        catch (HttpRequestException)
        {
            // 部分流失败：以已上传数据结算。
        }
        finally
        {
            phaseCts.Cancel();
            try
            {
                await reporter.ConfigureAwait(false);
            }
            catch
            {
                // 报告任务随取消而结束。
            }
        }

        var now = sw.Elapsed.TotalSeconds;
        var elapsed = Math.Max(now - tracker.StartTime, 0.5);
        var rate = tracker.Total / elapsed;
        Report(onProgress, SpeedTestPhase.Uploading, 1, 1.0,
            tracker.Total, rate, elapsed,
            rate > 0 ? $"上传完成 {TrafficFormatter.FormatRate(rate)}" : "上传测速未取得有效数据");
        return rate;
    }

    private async Task UploadOneStreamAsync(
        CancellationToken token,
        RateTracker tracker,
        Stopwatch sw,
        CancellationToken phaseToken)
    {
        using var content = new StreamContent(new CountingReadStream(
            UploadTargetBytes / StreamCount,
            bytes => tracker.Add(sw.Elapsed.TotalSeconds, bytes),
            () => !token.IsCancellationRequested && !phaseToken.IsCancellationRequested));

        using var response = await _http
            .PostAsync(UploadUrl, content, phaseToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(phaseToken).ConfigureAwait(false);
    }

    private static async Task ReportLoopAsync(
        CancellationToken token,
        Action<SpeedTestProgress>? onProgress,
        Stopwatch sw,
        RateTracker tracker,
        SpeedTestPhase phase,
        long targetBytes,
        double overallBase,
        double overallSpan,
        Func<long>? totalBytes = null)
    {
        var getBytes = totalBytes ?? (() => tracker.Total);
        while (true)
        {
            try
            {
                await Task.Delay(200, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var now = sw.Elapsed.TotalSeconds;
            var total = getBytes();
            var percent = targetBytes > 0 ? Math.Min(1.0, (double)total / targetBytes) : 0;
            onProgress?.Invoke(new SpeedTestProgress
            {
                Phase = phase,
                Percent = percent,
                OverallPercent = overallBase + percent * overallSpan,
                BytesTransferred = total,
                CurrentSpeed = tracker.RateAt(now),
                ElapsedSeconds = Math.Max(0, now - tracker.StartTime)
            });
        }
    }

    private static void Report(
        Action<SpeedTestProgress>? onProgress,
        SpeedTestPhase phase,
        double percent,
        double overallPercent,
        long bytes,
        double speed,
        double elapsed,
        string? message)
    {
        onProgress?.Invoke(new SpeedTestProgress
        {
            Phase = phase,
            Percent = percent,
            OverallPercent = overallPercent,
            BytesTransferred = bytes,
            CurrentSpeed = speed,
            ElapsedSeconds = elapsed,
            Message = message
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _http.Dispose();
    }

    /// <summary>
    /// 聚合多个并发流的字节数，用最近约 2.5 秒的滑动窗口计算实时速率。
    /// </summary>
    private sealed class RateTracker
    {
        private readonly object _gate = new();
        private readonly Queue<(double Time, long Total)> _samples = new();
        private readonly double _start;
        private long _total;

        public RateTracker(double startTime) => _start = startTime;

        public double StartTime => _start;

        public void Add(double now, long bytes)
        {
            lock (_gate)
            {
                _total += bytes;
                _samples.Enqueue((now, _total));
            }
        }

        public long Total
        {
            get
            {
                lock (_gate)
                {
                    return _total;
                }
            }
        }

        public double RateAt(double now)
        {
            lock (_gate)
            {
                Prune(now);
                if (_samples.Count >= 2)
                {
                    var first = _samples.Peek();
                    var last = _samples.Last();
                    var span = last.Time - first.Time;
                    if (span >= 0.5)
                        return (last.Total - first.Total) / span;
                }

                var elapsed = Math.Max(now - _start, 0.001);
                return _total / elapsed;
            }
        }

        private void Prune(double now)
        {
            while (_samples.Count > 1 && now - _samples.Peek().Time > 2.5)
                _samples.Dequeue();
        }
    }

    /// <summary>
    /// 按需生成上传数据并在写出时上报字节数；达到时间上限或取消时提前结束。
    /// </summary>
    private sealed class CountingReadStream : Stream
    {
        private readonly long _total;
        private readonly Action<long> _onRead;
        private readonly Func<bool> _shouldContinue;
        private readonly byte[] _pattern = new byte[8192];
        private long _position;

        public CountingReadStream(long total, Action<long> onRead, Func<bool> shouldContinue)
        {
            _total = total;
            _onRead = onRead;
            _shouldContinue = shouldContinue;
            Random.Shared.NextBytes(_pattern);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _total;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_shouldContinue() || _position >= _total)
                return 0;

            var n = (int)Math.Min(count, Math.Min(_total - _position, _pattern.Length));
            var written = 0;
            while (written < n)
            {
                var copy = Math.Min(_pattern.Length, n - written);
                Buffer.BlockCopy(_pattern, 0, buffer, offset + written, copy);
                written += copy;
            }

            _position += n;
            _onRead(n);
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
