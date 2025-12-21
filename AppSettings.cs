using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace AddressBar;

public enum DockPosition { Top, Bottom }
public enum MonitorMode { Single, All }
public enum IconPosition { Inside, Left }

public class AppSettings
{
    public MonitorMode MonitorMode { get; set; } = MonitorMode.Single;
    public int MonitorIndex { get; set; } = 0;
    public int BarHeight { get; set; } = 40;
    public DockPosition DockPosition { get; set; } = DockPosition.Top;
    public IconPosition IconPosition { get; set; } = IconPosition.Inside;
    public bool RunAtStartup { get; set; } = false;

    // Compact/floating mode settings
    public bool IsFloating { get; set; } = false;
    public int FloatingX { get; set; } = 100;
    public int FloatingY { get; set; } = 100;
    public int FloatingWidth { get; set; } = 400;

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AddressBar");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");
    private const string StartupKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "AddressBar";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
            ApplyStartupSetting();
        }
        catch { }
    }

    public void ApplyStartupSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, true);
            if (key == null) return;

            if (RunAtStartup)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(StartupValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(StartupValueName, false);
            }
        }
        catch { }
    }

    public static bool IsRunningAtStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath);
            return key?.GetValue(StartupValueName) != null;
        }
        catch { return false; }
    }
}
