using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NetworkWatch.Helpers;
using NetworkWatch.Models;
using NetworkWatch.Services;

namespace NetworkWatch;

public partial class PortManagerWindow : Window
{
    private readonly PortManagerService _service = new();
    private readonly ObservableCollection<PortConnection> _connections = new();
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _timer;
    private string _filter = "";

    public PortManagerWindow()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_connections);
        _view.Filter = FilterConnection;
        ConnectionGrid.ItemsSource = _view;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => LoadConnections();

        UpdateElevation();
        LoadConnections();
        Closed += (_, _) => _timer.Stop();
    }

    private void LoadConnections()
    {
        var connections = _service.GetConnections();
        _connections.Clear();
        foreach (var conn in connections)
            _connections.Add(conn);

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var total = _connections.Count;
        var shown = _connections.Count(MatchesFilter);
        StatusText.Text = string.IsNullOrEmpty(_filter)
            ? $"共 {total} 条连接"
            : $"共 {total} 条连接（显示 {shown} 条）";
    }

    private void UpdateElevation() =>
        ElevationText.Text = AdminHelper.IsRunningAsAdministrator() ? "管理员模式" : "普通模式";

    private bool MatchesFilter(PortConnection conn)
    {
        if (string.IsNullOrWhiteSpace(_filter))
            return true;

        return conn.LocalPort.ToString().Contains(_filter, StringComparison.OrdinalIgnoreCase)
               || conn.RemotePort.ToString().Contains(_filter, StringComparison.OrdinalIgnoreCase)
               || conn.LocalAddress.Contains(_filter, StringComparison.OrdinalIgnoreCase)
               || conn.RemoteAddress.Contains(_filter, StringComparison.OrdinalIgnoreCase)
               || conn.ProcessId.ToString().Contains(_filter, StringComparison.Ordinal)
               || conn.ProcessName.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterConnection(object item) => item is PortConnection conn && MatchesFilter(conn);

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = FilterBox.Text.Trim();
        _view.Refresh();
        UpdateStatus();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadConnections();

    private void AutoRefreshCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoRefreshCheckBox.IsChecked == true)
            _timer.Start();
        else
            _timer.Stop();
    }

    // 右键时选中点击所在行，使右键菜单作用于当前行
    private void ConnectionGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dep = (DependencyObject)e.OriginalSource;
        while (dep is not null and not DataGridRow and not DataGrid)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is DataGridRow row && !row.IsSelected)
        {
            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Control) == 0 && (mods & ModifierKeys.Shift) == 0)
                ConnectionGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private void ConnectionGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var hasSelection = ConnectionGrid.SelectedItems.Count > 0;
        if (ConnectionGrid.ContextMenu is { } menu)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
                item.IsEnabled = hasSelection;
        }
    }

    private void ConnectionGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ShowDetail();

    private void ViewDetailMenuItem_Click(object sender, RoutedEventArgs e) => ShowDetail();

    private void ShowDetail()
    {
        if (ConnectionGrid.SelectedItems.Cast<PortConnection>().FirstOrDefault() is not { } conn)
            return;

        var detail = new ProcessDetailWindow(conn.ProcessId, conn) { Owner = this };
        detail.Show();
    }

    private void KillMenuItem_Click(object sender, RoutedEventArgs e) => KillSelectedProcesses();

    private void KillSelectedProcesses()
    {
        var pids = ConnectionGrid.SelectedItems
            .Cast<PortConnection>()
            .Select(c => c.ProcessId)
            .Distinct()
            .ToList();

        if (pids.Count == 0)
            return;

        var killed = 0;
        var failures = new List<string>();
        foreach (var pid in pids)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.Kill();
                proc.WaitForExit(3000);
                killed++;
            }
            catch (ArgumentException)
            {
                failures.Add($"PID {pid}（进程不存在）");
            }
            catch (Win32Exception)
            {
                failures.Add($"PID {pid}（权限不足）");
            }
            catch (Exception ex)
            {
                failures.Add($"PID {pid}（{ex.Message}）");
            }
        }

        LoadConnections();

        if (failures.Count == 0)
        {
            if (killed > 0)
                MessageBox.Show($"已终止 {killed} 个进程", "端口管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = $"已终止 {killed} 个进程。\n以下进程终止失败:\n" + string.Join("\n", failures);
        MessageBox.Show(message, "端口管理", MessageBoxButton.OK,
            killed == 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
}
