using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace AddressBar;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new AddressBarManager());
    }
}

#region Settings

public enum DockPosition { Top, Bottom }

public class AppSettings
{
    public bool MultiMonitor { get; set; } = false;
    public int MonitorIndex { get; set; } = 0;
    public int BarHeight { get; set; } = 30;
    public DockPosition DockPosition { get; set; } = DockPosition.Top;

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AddressBar");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

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
        }
        catch { }
    }

    public static void OpenSettingsFolder()
    {
        Directory.CreateDirectory(SettingsDir);
        // Create default settings if none exist
        if (!File.Exists(SettingsPath))
        {
            new AppSettings().Save();
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsDir}\"") { UseShellExecute = true });
    }
}

#endregion

#region Manager (handles multiple bars)

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
            Icon = SystemIcons.Application,
            Text = "Address Bar",
            Visible = true
        };

        BuildContextMenu();
        _trayIcon.DoubleClick += (s, e) => ShowAllBars();

        SystemEvents.DisplaySettingsChanged += (s, e) => RecreateAppBars();

        CreateAppBars();
    }

    private void BuildContextMenu()
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, e) => ShowAllBars());
        contextMenu.Items.Add("Hide", null, (s, e) => HideAllBars());
        contextMenu.Items.Add("-");

        // Monitor submenu
        var monitorMenu = new ToolStripMenuItem("Monitor");
        var multiItem = new ToolStripMenuItem("All Monitors")
        {
            Checked = _settings.MultiMonitor
        };
        multiItem.Click += (s, e) =>
        {
            _settings.MultiMonitor = true;
            _settings.Save();
            RecreateAppBars();
            BuildContextMenu();
        };
        monitorMenu.DropDownItems.Add(multiItem);
        monitorMenu.DropDownItems.Add("-");

        for (int i = 0; i < Screen.AllScreens.Length; i++)
        {
            var screen = Screen.AllScreens[i];
            var idx = i;
            var item = new ToolStripMenuItem($"Monitor {i + 1}{(screen.Primary ? " (Primary)" : "")}")
            {
                Checked = !_settings.MultiMonitor && _settings.MonitorIndex == i
            };
            item.Click += (s, e) =>
            {
                _settings.MultiMonitor = false;
                _settings.MonitorIndex = idx;
                _settings.Save();
                RecreateAppBars();
                BuildContextMenu();
            };
            monitorMenu.DropDownItems.Add(item);
        }
        contextMenu.Items.Add(monitorMenu);

        // Dock position submenu
        var dockMenu = new ToolStripMenuItem("Dock Position");
        var topItem = new ToolStripMenuItem("Top") { Checked = _settings.DockPosition == DockPosition.Top };
        topItem.Click += (s, e) =>
        {
            _settings.DockPosition = DockPosition.Top;
            _settings.Save();
            RecreateAppBars();
            BuildContextMenu();
        };
        var bottomItem = new ToolStripMenuItem("Bottom") { Checked = _settings.DockPosition == DockPosition.Bottom };
        bottomItem.Click += (s, e) =>
        {
            _settings.DockPosition = DockPosition.Bottom;
            _settings.Save();
            RecreateAppBars();
            BuildContextMenu();
        };
        dockMenu.DropDownItems.Add(topItem);
        dockMenu.DropDownItems.Add(bottomItem);
        contextMenu.Items.Add(dockMenu);

        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Settings Folder", null, (s, e) => AppSettings.OpenSettingsFolder());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (s, e) => Exit());

        _trayIcon.ContextMenuStrip = contextMenu;
    }

    private void CreateAppBars()
    {
        if (_settings.MultiMonitor)
        {
            foreach (var screen in Screen.AllScreens)
            {
                var bar = new AddressBarForm(screen, _settings);
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
            var bar = new AddressBarForm(targetScreen, _settings);
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
        SystemEvents.DisplaySettingsChanged -= (s, e) => RecreateAppBars();
        Application.Exit();
    }
}

#endregion

public class AddressBarForm : Form
{
    #region Win32 API

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int RegisterWindowMessage(string msg);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const uint ABM_NEW = 0x00;
    private const uint ABM_REMOVE = 0x01;
    private const uint ABM_QUERYPOS = 0x02;
    private const uint ABM_SETPOS = 0x03;

    private const int ABN_STATECHANGE = 0x00;
    private const int ABN_POSCHANGED = 0x01;
    private const int ABN_FULLSCREENAPP = 0x02;

    private const uint ABE_TOP = 1;
    private const uint ABE_BOTTOM = 3;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    #endregion

    private readonly Screen _screen;
    private readonly AppSettings _settings;
    private int _appBarHeight;
    private uint _callbackMessageId;
    private bool _appBarRegistered;
    private bool _isFullScreen;

    private readonly TextBox _addressBox;
    private readonly Button _goButton;
    private readonly Label _addressLabel;

    public AddressBarForm(Screen screen, AppSettings settings)
    {
        _screen = screen;
        _settings = settings;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = GetSystemBackColor();

        _appBarHeight = LogicalToDeviceUnits(_settings.BarHeight);

        ApplyDarkMode();

        _addressLabel = new Label
        {
            Text = "Address",
            AutoSize = true,
            ForeColor = GetSystemForeColor(),
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _addressBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f),
            BackColor = GetTextBoxBackColor(),
            ForeColor = GetSystemForeColor()
        };
        _addressBox.KeyDown += AddressBox_KeyDown;

        _goButton = new Button
        {
            Text = "→",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            BackColor = GetButtonBackColor(),
            ForeColor = GetSystemForeColor(),
            Cursor = Cursors.Hand,
            Width = LogicalToDeviceUnits(30)
        };
        _goButton.FlatAppearance.BorderSize = 1;
        _goButton.FlatAppearance.BorderColor = GetBorderColor();
        _goButton.Click += GoButton_Click;

        Controls.Add(_addressLabel);
        Controls.Add(_addressBox);
        Controls.Add(_goButton);

        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        Resize += (s, e) => LayoutControls();
        Load += AddressBarForm_Load;
    }

    private void AddressBarForm_Load(object? sender, EventArgs e)
    {
        RegisterAppBar();
        LayoutControls();
    }

    public void Cleanup()
    {
        UnregisterAppBar();
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
    }

    private void LayoutControls()
    {
        int padding = LogicalToDeviceUnits(6);
        int labelWidth = LogicalToDeviceUnits(50);
        int buttonWidth = _goButton.Width;

        _addressLabel.Location = new Point(padding, (Height - _addressLabel.Height) / 2);

        int textBoxLeft = padding + labelWidth;
        int textBoxWidth = Width - textBoxLeft - buttonWidth - padding * 2;
        _addressBox.Location = new Point(textBoxLeft, (Height - _addressBox.Height) / 2);
        _addressBox.Width = textBoxWidth;

        _goButton.Location = new Point(Width - buttonWidth - padding, (Height - _goButton.Height) / 2);
        _goButton.Height = _addressBox.Height;
    }

    #region AppBar Registration

    private void RegisterAppBar()
    {
        if (_appBarRegistered) return;

        _callbackMessageId = (uint)RegisterWindowMessage($"AddressBarAppBarMessage_{_screen.DeviceName}");

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = Handle,
            uCallbackMessage = _callbackMessageId
        };

        uint result = SHAppBarMessage(ABM_NEW, ref abd);
        if (result != 0)
        {
            _appBarRegistered = true;
            SetAppBarPos();
        }
    }

    private void UnregisterAppBar()
    {
        if (!_appBarRegistered) return;

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = Handle
        };

        SHAppBarMessage(ABM_REMOVE, ref abd);
        _appBarRegistered = false;
    }

    private void SetAppBarPos()
    {
        if (!_appBarRegistered) return;

        var bounds = _screen.Bounds;
        uint edge = _settings.DockPosition == DockPosition.Top ? ABE_TOP : ABE_BOTTOM;

        int top, bottom;
        if (_settings.DockPosition == DockPosition.Top)
        {
            top = bounds.Top;
            bottom = bounds.Top + _appBarHeight;
        }
        else
        {
            top = bounds.Bottom - _appBarHeight;
            bottom = bounds.Bottom;
        }

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = Handle,
            uEdge = edge,
            rc = new RECT
            {
                left = bounds.Left,
                top = top,
                right = bounds.Right,
                bottom = bottom
            }
        };

        SHAppBarMessage(ABM_QUERYPOS, ref abd);

        if (_settings.DockPosition == DockPosition.Top)
            abd.rc.bottom = abd.rc.top + _appBarHeight;
        else
            abd.rc.top = abd.rc.bottom - _appBarHeight;

        SHAppBarMessage(ABM_SETPOS, ref abd);

        SetBounds(abd.rc.left, abd.rc.top, abd.rc.right - abd.rc.left, abd.rc.bottom - abd.rc.top);
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void ShowAppBar()
    {
        Show();
        if (!_appBarRegistered)
        {
            RegisterAppBar();
        }
        SetAppBarPos();
    }

    public void HideAppBar()
    {
        UnregisterAppBar();
        Hide();
    }

    protected override void WndProc(ref Message m)
    {
        if (_appBarRegistered && m.Msg == _callbackMessageId)
        {
            switch (m.WParam.ToInt32())
            {
                case ABN_POSCHANGED:
                    SetAppBarPos();
                    break;

                case ABN_FULLSCREENAPP:
                    _isFullScreen = m.LParam.ToInt32() != 0;
                    if (_isFullScreen)
                    {
                        TopMost = false;
                    }
                    else
                    {
                        TopMost = true;
                        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                    break;

                case ABN_STATECHANGE:
                    SetAppBarPos();
                    break;
            }
            return;
        }

        base.WndProc(ref m);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    #endregion

    #region Address Navigation

    private void AddressBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            Navigate();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            _addressBox.Clear();
        }
    }

    private void GoButton_Click(object? sender, EventArgs e)
    {
        Navigate();
    }

    private void Navigate()
    {
        string input = _addressBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        try
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                Process.Start(new ProcessStartInfo(input) { UseShellExecute = true });
                return;
            }

            if (input.Contains('.') && !input.Contains(' ') && !input.Contains('\\') && !input.Contains('/'))
            {
                string url = "https://" + input;
                if (Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return;
                }
            }

            string expandedPath = Environment.ExpandEnvironmentVariables(input);
            if (Directory.Exists(expandedPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{expandedPath}\"") { UseShellExecute = true });
                return;
            }
            if (File.Exists(expandedPath))
            {
                Process.Start(new ProcessStartInfo(expandedPath) { UseShellExecute = true });
                return;
            }

            Process.Start(new ProcessStartInfo(input) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not navigate to: {input}\n\n{ex.Message}", "Address Bar",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    #endregion

    #region Theming

    private void ApplyDarkMode()
    {
        if (IsDarkMode())
        {
            int value = 1;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
    }

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color GetSystemBackColor() =>
        IsDarkMode() ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);

    private static Color GetSystemForeColor() =>
        IsDarkMode() ? Color.FromArgb(255, 255, 255) : Color.FromArgb(0, 0, 0);

    private static Color GetTextBoxBackColor() =>
        IsDarkMode() ? Color.FromArgb(45, 45, 45) : Color.White;

    private static Color GetButtonBackColor() =>
        IsDarkMode() ? Color.FromArgb(55, 55, 55) : Color.FromArgb(225, 225, 225);

    private static Color GetBorderColor() =>
        IsDarkMode() ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            UpdateTheme();
        }
    }

    private void UpdateTheme()
    {
        BackColor = GetSystemBackColor();
        _addressLabel.ForeColor = GetSystemForeColor();
        _addressBox.BackColor = GetTextBoxBackColor();
        _addressBox.ForeColor = GetSystemForeColor();
        _goButton.BackColor = GetButtonBackColor();
        _goButton.ForeColor = GetSystemForeColor();
        _goButton.FlatAppearance.BorderColor = GetBorderColor();
        ApplyDarkMode();
        Invalidate();
    }

    #endregion
}
