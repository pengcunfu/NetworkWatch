using System.Windows;
using NetworkWatch.Models;
using NetworkWatch.Services;

namespace NetworkWatch;

public partial class SpeedTestWindow : Window
{
    private readonly SpeedTestService _service = new();
    private CancellationTokenSource? _cts;
    private bool _running;
    private SpeedTestResult? _lastResult;

    public SpeedTestWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            _cts?.Cancel();
            _service.Dispose();
        };
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
            return;

        _running = true;
        SetRunningUi(true);
        _cts = new CancellationTokenSource();

        try
        {
            var result = await _service.RunAsync(_cts.Token, p => Dispatcher.Invoke(() => ShowProgress(p)));
            ShowResult(result);
        }
        catch (OperationCanceledException)
        {
            PhaseText.Text = "已取消";
            StatusText.Text = "测速已取消";
        }
        catch (Exception ex)
        {
            PhaseText.Text = "测速失败";
            StatusText.Text = $"测速失败：{ex.Message}";
            if (_lastResult is not null)
            {
                ShowResult(_lastResult);
                StatusText.Text += "（已保留上次结果）";
            }
            else
            {
                DownloadDetailText.Text = "下载测速失败";
                UploadDetailText.Text = "上传测速失败";
            }
        }
        finally
        {
            _running = false;
            _cts.Dispose();
            _cts = null;
            SetRunningUi(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void ShowProgress(SpeedTestProgress progress)
    {
        MainProgressBar.Value = progress.OverallPercent * 100;
        PercentText.Text = $"{progress.OverallPercent * 100:0}%";
        PhaseText.Text = progress.Phase switch
        {
            SpeedTestPhase.Ping => "延迟测试",
            SpeedTestPhase.Downloading => "下载测速",
            SpeedTestPhase.Uploading => "上传测速",
            _ => PhaseText.Text
        };
        SpeedText.Text = progress.CurrentSpeed > 0
            ? TrafficFormatter.FormatRate(progress.CurrentSpeed)
            : "--";
        ElapsedText.Text = $"{progress.ElapsedSeconds:0.0}s";

        if (progress.Phase == SpeedTestPhase.Downloading)
        {
            DownloadValue.Text = progress.CurrentSpeed > 0
                ? TrafficFormatter.FormatRate(progress.CurrentSpeed)
                : "--";
            DownloadDetailText.Text =
                $"已下载 {TrafficFormatter.FormatBytes((ulong)Math.Max(0, progress.BytesTransferred))}";
        }
        else if (progress.Phase == SpeedTestPhase.Uploading)
        {
            UploadValue.Text = progress.CurrentSpeed > 0
                ? TrafficFormatter.FormatRate(progress.CurrentSpeed)
                : "--";
            UploadDetailText.Text =
                $"已上传 {TrafficFormatter.FormatBytes((ulong)Math.Max(0, progress.BytesTransferred))}";
        }

        if (!string.IsNullOrEmpty(progress.Message))
        {
            StatusText.Text = progress.Message;
        }
        else if (progress.Phase is SpeedTestPhase.Downloading or SpeedTestPhase.Uploading)
        {
            StatusText.Text = $"{PhaseText.Text}中… 已传输 {TrafficFormatter.FormatBytes((ulong)Math.Max(0, progress.BytesTransferred))}";
        }
    }

    private void ShowResult(SpeedTestResult result)
    {
        _lastResult = result;
        PhaseText.Text = "测速完成";
        MainProgressBar.Value = 100;
        PercentText.Text = "100%";
        SpeedText.Text = "--";

        DownloadValue.Text = result.DownloadText;
        UploadValue.Text = result.UploadText;
        DownloadDetailText.Text = result.DownloadBytesPerSecond > 0 ? "下载测试完成" : "下载测速失败";
        UploadDetailText.Text = result.UploadBytesPerSecond > 0 ? "上传测试完成" : "上传测速失败";
        PingValue.Text = result.PingText;
        JitterValue.Text = result.JitterText;
        ServerValue.Text = result.ServerName;
        DurationValue.Text = result.DurationText;
        StatusText.Text = result.UploadMessage ?? "测速完成";
    }

    private void ResetResult()
    {
        DownloadValue.Text = "--";
        UploadValue.Text = "--";
        PingValue.Text = "--";
        JitterValue.Text = "--";
        ServerValue.Text = "--";
        DurationValue.Text = "--";
    }

    private void SetRunningUi(bool running)
    {
        StartButton.IsEnabled = !running;
        StartButton.Content = running ? "测速中…" : "重新测速";
        CancelButton.IsEnabled = running;

        if (running)
        {
            MainProgressBar.Value = 0;
            PercentText.Text = "0%";
            SpeedText.Text = "--";
            ElapsedText.Text = "0.0s";
            ResetResult();
        }
    }
}
