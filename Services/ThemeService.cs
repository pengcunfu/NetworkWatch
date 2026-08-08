using System.IO;
using System.Text.Json;
using System.Windows;
using NetworkWatch.Models;

namespace NetworkWatch.Services;

/// <summary>
/// 管理浅色/深色主题的切换与持久化。默认浅色主题。
/// </summary>
public static class ThemeService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetworkWatch");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>
    /// 读取已保存的主题；无记录或读取失败时返回默认浅色主题。
    /// </summary>
    public static AppTheme LoadSaved()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("theme", out var value) &&
                    Enum.TryParse<AppTheme>(value.GetString(), ignoreCase: true, out var theme))
                {
                    return theme;
                }
            }
        }
        catch
        {
            // 忽略读取异常，回退到默认主题。
        }

        return AppTheme.Light;
    }

    /// <summary>
    /// 应用指定主题：替换应用级主题资源字典，使所有 DynamicResource 引用实时刷新。
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        Current = theme;

        var newDict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{theme}.xaml")
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString ?? string.Empty;
            if (source.EndsWith("/Themes/Light.xaml", StringComparison.Ordinal) ||
                source.EndsWith("/Themes/Dark.xaml", StringComparison.Ordinal))
            {
                merged[i] = newDict;
                return;
            }
        }

        merged.Add(newDict);
    }

    /// <summary>
    /// 切换到另一主题并持久化。
    /// </summary>
    public static AppTheme Toggle()
    {
        var next = Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        Apply(next);
        Save(next);
        return next;
    }

    private static void Save(AppTheme theme)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { theme = theme.ToString() }));
        }
        catch
        {
            // 忽略写入异常，不影响应用运行。
        }
    }
}
