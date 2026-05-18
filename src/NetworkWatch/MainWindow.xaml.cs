using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        var selectedPid = _selectedPid;
        if (selectedPid is null && ProcessGrid.SelectedItem is ProcessNetworkInfo selected)
            selectedPid = selected.ProcessId;

        _processes.Clear();
        foreach (var proc in snapshot.Processes)
            _processes.Add(proc);

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

        StatusText.Text = $"活跃进程: {snapshot.Processes.Count}  |  连接总数: {snapshot.TotalConnections}  |  更新于 {snapshot.Timestamp:HH:mm:ss}";
        TotalDownloadText.Text = TrafficFormatter.FormatRate(snapshot.TotalDownloadRate);
        TotalUploadText.Text = TrafficFormatter.FormatRate(snapshot.TotalUploadRate);
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
}
