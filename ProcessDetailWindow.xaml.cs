using System.Windows;
using NetworkWatch.Models;
using NetworkWatch.Services;

namespace NetworkWatch;

public partial class ProcessDetailWindow : Window
{
    private readonly int _pid;
    private readonly PortConnection? _connection;

    public ProcessDetailWindow(int pid, PortConnection? connection)
    {
        _pid = pid;
        _connection = connection;
        InitializeComponent();
        BasicText.Text = "加载中…";
        Loaded += (_, _) => _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var info = await Task.Run(() => ProcessDetailService.Collect(_pid, _connection));
        Dispatcher.Invoke(() => Apply(info));
    }

    private void Apply(ProcessDetailInfo info)
    {
        Title = string.IsNullOrEmpty(info.Name)
            ? $"进程详细信息 - PID {_pid}"
            : $"进程详细信息 - {info.Name} ({_pid})";

        if (!string.IsNullOrEmpty(info.ErrorMessage))
        {
            BasicText.Text = info.ErrorMessage;
            CpuText.Text = MemoryText.Text = ConnectionText.Text = OtherText.Text = "";
            return;
        }

        BasicText.Text =
            $"进程ID (PID): {info.Pid}\n" +
            $"进程名称: {info.Name}\n" +
            $"是否响应: {info.Responding}\n" +
            $"可执行文件: {info.ExePath}\n" +
            $"创建时间: {info.StartTime}";

        CpuText.Text =
            $"CPU 使用率: {info.CpuUsage}\n" +
            $"用户时间: {info.UserTime}\n" +
            $"系统时间: {info.PrivilegedTime}\n" +
            $"处理器亲和性: {info.ProcessorAffinity}";

        MemoryText.Text =
            $"工作集 (RSS): {info.WorkingSet}\n" +
            $"虚拟内存 (VMS): {info.VirtualMemory}\n" +
            $"峰值工作集: {info.PeakWorkingSet}\n" +
            $"内存百分比: {info.MemoryPercent}\n" +
            $"页错误数: {info.PageFaults}";

        ConnectionText.Text =
            $"协议: {info.Protocol}\n" +
            $"本地地址: {info.LocalEndpoint}\n" +
            $"远程地址: {info.RemoteEndpoint}";

        OtherText.Text =
            $"命令行: {info.CommandLine}\n" +
            $"工作目录: {info.WorkingDirectory}\n" +
            $"线程数: {info.ThreadCount}\n" +
            $"句柄数: {info.HandleCount}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
