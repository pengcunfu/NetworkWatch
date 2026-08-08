using System.Windows;
using NetworkWatch.Services;

namespace NetworkWatch;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 在主窗口创建前应用已保存的主题，避免主题闪烁。默认浅色主题。
        ThemeService.Apply(ThemeService.LoadSaved());
        base.OnStartup(e);
    }
}
