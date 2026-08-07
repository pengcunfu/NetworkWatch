using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NetworkWatch.Helpers;
using NetworkWatch.Models;
using NetworkWatch.Services;

namespace NetworkWatch;

public partial class MainWindow : Window
{
    private readonly NetworkMonitorService _monitor = new();
    private readonly ObservableCollection<ProcessNetworkInfo> _processes = new();
    private readonly ICollectionView _processView;
    private MonitorSnapshot? _latestSnapshot;
    private bool _paused;
    private int? _selectedPid;

    public MainWindow()
    {
        InitializeComponent();
        _processView = CollectionViewSource.GetDefaultView(_processes);
        _processView.Filter = FilterProcess;
        ProcessGrid.ItemsSource = _processView;

        _monitor.SortByTraffic = AutoSortCheckBox.IsChecked == true;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        _monitor.Start();

        Closed += (_, _) => _monitor.Dispose();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        _processView.Refresh();

    private bool FilterProcess(object item)
    {
        if (item is not ProcessNetworkInfo proc)
            return false;

        var query = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return proc.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || proc.ProcessId.ToString().Contains(query, StringComparison.Ordinal)
               || (proc.ExecutablePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void OnSnapshotUpdated(MonitorSnapshot snapshot)
    {
        if (_paused)
            return;

        Dispatcher.Invoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(MonitorSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        AdminButton.Content = snapshot.IsElevated ? "普通模式" : "管理员模式";
        AdminButton.ToolTip = snapshot.IsElevated
            ? "当前以管理员权限运行；点击可恢复普通权限"
            : "点击以管理员身份重启，临时启用完整 ETW 流量统计";

        var selectedPid = _selectedPid;
        if (selectedPid is null && ProcessGrid.SelectedItem is ProcessNetworkInfo selected)
            selectedPid = selected.ProcessId;

        UpdateProcessList(snapshot.Processes);

        _processView.Refresh();

        if (selectedPid is int pid)
        {
            var match = _processes.FirstOrDefault(p => p.ProcessId == pid);
            if (match is not null)
            {
                ProcessGrid.SelectedItem = match;
                ShowConnections(match);
            }
        }

        var status = $"活跃进程: {snapshot.Processes.Count}  |  连接总数: {snapshot.TotalConnections}  |  流量采样: {snapshot.TrafficStatsSuccessCount}/{snapshot.TrafficStatsEligibleCount}  |  更新于 {snapshot.Timestamp:HH:mm:ss}";
        if (!string.IsNullOrEmpty(snapshot.StatusHint))
            status += $"  |  {snapshot.StatusHint}";
        StatusText.Text = status;
        TotalDownloadText.Text = TrafficFormatter.FormatRate(snapshot.TotalDownloadRate);
        TotalUploadText.Text = TrafficFormatter.FormatRate(snapshot.TotalUploadRate);
    }

    private void UpdateProcessList(IReadOnlyList<ProcessNetworkInfo> processes)
    {
        if (_monitor.SortByTraffic)
        {
            _processes.Clear();
            foreach (var proc in processes)
                _processes.Add(proc);
            return;
        }

        var byPid = processes.ToDictionary(p => p.ProcessId);
        for (var i = _processes.Count - 1; i >= 0; i--)
        {
            if (!byPid.ContainsKey(_processes[i].ProcessId))
                _processes.RemoveAt(i);
        }

        for (var i = 0; i < _processes.Count; i++)
        {
            var pid = _processes[i].ProcessId;
            if (byPid.TryGetValue(pid, out var updated))
                _processes[i] = updated;
        }

        var existing = new HashSet<int>(_processes.Select(p => p.ProcessId));
        foreach (var proc in processes)
        {
            if (!existing.Contains(proc.ProcessId))
                _processes.Add(proc);
        }
    }

    private void AutoSortCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _monitor.SortByTraffic = AutoSortCheckBox.IsChecked == true;
        if (_latestSnapshot is not null)
            ApplySnapshot(_latestSnapshot);
    }

    private void ProcessGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessGrid.SelectedItem is ProcessNetworkInfo proc)
        {
            _selectedPid = proc.ProcessId;
            ShowConnections(proc);
        }
    }

    private void ShowConnections(ProcessNetworkInfo proc)
    {
        SelectedProcessText.Text = string.IsNullOrWhiteSpace(proc.ExecutablePath)
            ? $"{proc.DisplayName}  (PID {proc.ProcessId})"
            : $"{proc.DisplayName}  (PID {proc.ProcessId})\n{proc.ExecutablePath}";

        ConnectionGrid.ItemsSource = proc.Connections;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? "继续" : "暂停";
        if (!_paused && _latestSnapshot is not null)
            ApplySnapshot(_latestSnapshot);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshot = _monitor.CollectSnapshot();
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"刷新失败: {ex.Message}", "NetworkWatch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdminHelper.IsRunningAsAdministrator())
            RestartAsNormalUser();
        else
            RestartAsAdministrator();
    }

    private static void RestartAsAdministrator()
    {
        var (fileName, arguments) = GetRestartCommand();
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.CurrentDirectory
            });
            Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // 用户取消了 UAC 提示，继续以普通权限运行。
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法以管理员身份启动: {ex.Message}", "NetworkWatch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void RestartAsNormalUser()
    {
        var (fileName, arguments) = GetRestartCommand();
        try
        {
            // 经由非提权的 explorer.exe 启动，使新实例降回普通权限。
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{fileName}\" {arguments}".TrimEnd(),
                UseShellExecute = true
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法以普通权限重启: {ex.Message}", "NetworkWatch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static (string FileName, string Arguments) GetRestartCommand()
    {
        var processPath = Environment.ProcessPath;
        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (processPath is not null &&
            Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(assemblyPath))
            return (processPath, $"\"{assemblyPath}\"");

        return (processPath ?? assemblyPath ?? string.Empty, string.Empty);
    }
}
