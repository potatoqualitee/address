using Microsoft.Win32;

namespace AddressBar;

public class AddressBarManager : ApplicationContext
{
    private readonly List<AddressBarForm> _bars = new();
    private readonly NotifyIcon _trayIcon;
    private AppSettings _settings;

    public AddressBarManager()
    {
        _settings = AppSettings.Load();

        _trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Address Bar",
            Visible = true
        };

        BuildContextMenu();
        _trayIcon.DoubleClick += (s, e) => ShowAllBars();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        CreateAppBars();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        RecreateAppBars();
    }

    private void BuildContextMenu()
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, e) => ShowAllBars());
        contextMenu.Items.Add("Hide", null, (s, e) => HideAllBars());
        contextMenu.Items.Add("-");

        var floatItem = new ToolStripMenuItem(_settings.IsFloating ? "Dock to Edge" : "Undock (Compact Mode)");
        floatItem.Click += (s, e) => ToggleFloatingMode();
        contextMenu.Items.Add(floatItem);

        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Settings...", null, (s, e) => ShowSettings());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (s, e) => Exit());

        _trayIcon.ContextMenuStrip = contextMenu;
    }

    public void ToggleFloatingMode()
    {
        _settings.IsFloating = !_settings.IsFloating;
        _settings.Save();
        RecreateAppBars();
        BuildContextMenu();
    }

    public void ShowSettings()
    {
        using var dialog = new SettingsDialog(_settings);
        if (dialog.ShowDialog() == DialogResult.OK && dialog.SettingsChanged)
        {
            RecreateAppBars();
        }
    }

    private void CreateAppBars()
    {
        if (_settings.IsFloating)
        {
            var screen = Screen.PrimaryScreen!;
            var bar = new AddressBarForm(screen, _settings, this);
            bar.Show();
            _bars.Add(bar);
        }
        else if (_settings.MonitorMode == MonitorMode.All)
        {
            foreach (var screen in Screen.AllScreens)
            {
                var bar = new AddressBarForm(screen, _settings, this);
                bar.Show();
                _bars.Add(bar);
            }
        }
        else
        {
            var screens = Screen.AllScreens;
            var targetScreen = _settings.MonitorIndex < screens.Length
                ? screens[_settings.MonitorIndex]
                : Screen.PrimaryScreen!;
            var bar = new AddressBarForm(targetScreen, _settings, this);
            bar.Show();
            _bars.Add(bar);
        }
    }

    private void RecreateAppBars()
    {
        foreach (var bar in _bars)
        {
            bar.Cleanup();
            bar.Close();
        }
        _bars.Clear();
        _settings = AppSettings.Load();
        CreateAppBars();
    }

    private void ShowAllBars()
    {
        foreach (var bar in _bars)
        {
            bar.ShowAppBar();
        }
    }

    private void HideAllBars()
    {
        foreach (var bar in _bars)
        {
            bar.HideAppBar();
        }
    }

    private void Exit()
    {
        foreach (var bar in _bars)
        {
            bar.Cleanup();
            bar.Close();
        }
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Application.Exit();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "addressbar.ico");
            if (File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch { }
        return SystemIcons.Application;
    }
}
