using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
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
public enum MonitorMode { Single, All }

public class AppSettings
{
    public MonitorMode MonitorMode { get; set; } = MonitorMode.Single;
    public int MonitorIndex { get; set; } = 0;
    public int BarHeight { get; set; } = 40;
    public DockPosition DockPosition { get; set; } = DockPosition.Top;
    public bool RunAtStartup { get; set; } = false;

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

#endregion

#region Settings Dialog

public class SettingsDialog : Form
{
    private readonly AppSettings _settings;
    private readonly ComboBox _monitorModeCombo;
    private readonly ComboBox _monitorSelectCombo;
    private readonly ComboBox _dockPositionCombo;
    private readonly NumericUpDown _barHeightNumeric;
    private readonly CheckBox _startupCheckbox;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;

    public bool SettingsChanged { get; private set; }

    public SettingsDialog(AppSettings settings)
    {
        _settings = settings;

        Text = "Address Bar Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(350, 255);
        BackColor = IsDarkMode() ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
        ForeColor = IsDarkMode() ? Color.White : SystemColors.ControlText;

        var labelFont = new Font("Segoe UI", 9f);
        int y = 15;
        int labelX = 15;
        int controlX = 140;
        int controlWidth = 180;
        int rowHeight = 32;

        // Monitor Mode
        var modeLabel = new Label { Text = "Monitor:", Location = new Point(labelX, y + 3), AutoSize = true, Font = labelFont };
        _monitorModeCombo = new ComboBox
        {
            Location = new Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = labelFont
        };
        _monitorModeCombo.Items.AddRange(new object[] { "Single Monitor", "All Monitors" });
        _monitorModeCombo.SelectedIndex = _settings.MonitorMode == MonitorMode.All ? 1 : 0;
        _monitorModeCombo.SelectedIndexChanged += (s, e) => UpdateMonitorSelectEnabled();
        Controls.Add(modeLabel);
        Controls.Add(_monitorModeCombo);
        y += rowHeight;

        // Monitor Selection
        var selectLabel = new Label { Text = "Display:", Location = new Point(labelX, y + 3), AutoSize = true, Font = labelFont };
        _monitorSelectCombo = new ComboBox
        {
            Location = new Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = labelFont
        };
        for (int i = 0; i < Screen.AllScreens.Length; i++)
        {
            var screen = Screen.AllScreens[i];
            _monitorSelectCombo.Items.Add($"Monitor {i + 1}{(screen.Primary ? " (Primary)" : "")}");
        }
        _monitorSelectCombo.SelectedIndex = Math.Min(_settings.MonitorIndex, Screen.AllScreens.Length - 1);
        Controls.Add(selectLabel);
        Controls.Add(_monitorSelectCombo);
        y += rowHeight;

        // Dock Position
        var dockLabel = new Label { Text = "Dock Position:", Location = new Point(labelX, y + 3), AutoSize = true, Font = labelFont };
        _dockPositionCombo = new ComboBox
        {
            Location = new Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = labelFont
        };
        _dockPositionCombo.Items.AddRange(new object[] { "Top", "Bottom" });
        _dockPositionCombo.SelectedIndex = _settings.DockPosition == DockPosition.Bottom ? 1 : 0;
        Controls.Add(dockLabel);
        Controls.Add(_dockPositionCombo);
        y += rowHeight;

        // Bar Height
        var heightLabel = new Label { Text = "Bar Height:", Location = new Point(labelX, y + 3), AutoSize = true, Font = labelFont };
        _barHeightNumeric = new NumericUpDown
        {
            Location = new Point(controlX, y),
            Width = 80,
            Minimum = 20,
            Maximum = 60,
            Value = _settings.BarHeight,
            Font = labelFont
        };
        Controls.Add(heightLabel);
        Controls.Add(_barHeightNumeric);
        y += rowHeight;

        // Run at Startup
        _startupCheckbox = new CheckBox
        {
            Text = "Run at Windows startup",
            Location = new Point(labelX, y + 3),
            AutoSize = true,
            Font = labelFont,
            Checked = AppSettings.IsRunningAtStartup()
        };
        Controls.Add(_startupCheckbox);
        y += rowHeight + 10;

        // Buttons
        _saveButton = new Button
        {
            Text = "Save",
            Location = new Point(controlX, y),
            Width = 80,
            Font = labelFont,
            DialogResult = DialogResult.OK
        };
        _saveButton.Click += SaveButton_Click;

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(controlX + 90, y),
            Width = 80,
            Font = labelFont,
            DialogResult = DialogResult.Cancel
        };

        Controls.Add(_saveButton);
        Controls.Add(_cancelButton);

        AcceptButton = _saveButton;
        CancelButton = _cancelButton;

        UpdateMonitorSelectEnabled();
    }

    private void UpdateMonitorSelectEnabled()
    {
        _monitorSelectCombo.Enabled = _monitorModeCombo.SelectedIndex == 0;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        var newMode = _monitorModeCombo.SelectedIndex == 1 ? MonitorMode.All : MonitorMode.Single;
        var newMonitorIndex = _monitorSelectCombo.SelectedIndex;
        var newDockPosition = _dockPositionCombo.SelectedIndex == 1 ? DockPosition.Bottom : DockPosition.Top;
        var newBarHeight = (int)_barHeightNumeric.Value;
        var newStartup = _startupCheckbox.Checked;

        if (newMode != _settings.MonitorMode ||
            newMonitorIndex != _settings.MonitorIndex ||
            newDockPosition != _settings.DockPosition ||
            newBarHeight != _settings.BarHeight ||
            newStartup != _settings.RunAtStartup)
        {
            _settings.MonitorMode = newMode;
            _settings.MonitorIndex = newMonitorIndex;
            _settings.DockPosition = newDockPosition;
            _settings.BarHeight = newBarHeight;
            _settings.RunAtStartup = newStartup;
            _settings.Save();
            SettingsChanged = true;
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
        catch { return false; }
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
        contextMenu.Items.Add("Settings...", null, (s, e) => ShowSettings());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (s, e) => Exit());

        _trayIcon.ContextMenuStrip = contextMenu;
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
        if (_settings.MonitorMode == MonitorMode.All)
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
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var iconPath = Path.Combine(Path.GetDirectoryName(exePath)!, "addressbar.ico");
            if (File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch { }
        return SystemIcons.Application;
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

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const int EM_SETMARGINS = 0xD3;
    private const int EC_LEFTMARGIN = 0x1;

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    #endregion

    private readonly Screen _screen;
    private readonly AppSettings _settings;
    private readonly AddressBarManager _manager;
    private int _appBarHeight;
    private uint _callbackMessageId;
    private bool _appBarRegistered;
    private bool _isFullScreen;

    private readonly TextBox _addressBox;
    private readonly Panel _addressBoxContainer;
    private readonly Button _goButton;
    private readonly Button _settingsButton;
    private readonly Label _addressLabel;
    private readonly PictureBox _iconBox;
    private CancellationTokenSource? _iconCts;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public AddressBarForm(Screen screen, AppSettings settings, AddressBarManager manager)
    {
        _screen = screen;
        _settings = settings;
        _manager = manager;

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
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9f),
            BackColor = GetTextBoxBackColor(),
            ForeColor = GetSystemForeColor()
        };
        _addressBox.KeyDown += AddressBox_KeyDown;
        _addressBox.TextChanged += AddressBox_TextChanged;

        _addressBoxContainer = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = GetTextBoxBackColor()
        };
        _addressBoxContainer.Controls.Add(_addressBox);

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

        _settingsButton = new Button
        {
            Text = "⚙",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            BackColor = GetButtonBackColor(),
            ForeColor = GetSystemForeColor(),
            Cursor = Cursors.Hand,
            Width = LogicalToDeviceUnits(30)
        };
        _settingsButton.FlatAppearance.BorderSize = 1;
        _settingsButton.FlatAppearance.BorderColor = GetBorderColor();
        _settingsButton.Click += SettingsButton_Click;

        _iconBox = new PictureBox
        {
            Size = new Size(16, 16),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };

        Controls.Add(_iconBox);
        Controls.Add(_addressLabel);
        Controls.Add(_addressBoxContainer);
        Controls.Add(_goButton);
        Controls.Add(_settingsButton);

        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        Resize += (s, e) => LayoutControls();
        Load += AddressBarForm_Load;
    }

    private void AddressBarForm_Load(object? sender, EventArgs e)
    {
        RegisterAppBar();
        SetTextBoxMargins();
        LayoutControls();
    }

    private void SetTextBoxMargins()
    {
        // Add left padding inside the textbox (4 pixels)
        int leftMargin = LogicalToDeviceUnits(4);
        SendMessage(_addressBox.Handle, EM_SETMARGINS, (IntPtr)EC_LEFTMARGIN, (IntPtr)leftMargin);
    }

    public void Cleanup()
    {
        UnregisterAppBar();
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _iconCts?.Cancel();
        _iconCts?.Dispose();
        _iconBox.Image?.Dispose();
    }

    private void LayoutControls()
    {
        int padding = LogicalToDeviceUnits(6);
        int iconSize = LogicalToDeviceUnits(16);
        int iconPadding = LogicalToDeviceUnits(4);
        int labelWidth = LogicalToDeviceUnits(50);
        int buttonWidth = _goButton.Width;
        int settingsWidth = _settingsButton.Width;

        // Icon box at the left
        _iconBox.Size = new Size(iconSize, iconSize);
        _iconBox.Location = new Point(padding, (Height - iconSize) / 2);

        // Label after icon
        _addressLabel.Location = new Point(padding + iconSize + iconPadding, (Height - _addressLabel.Height) / 2);

        int textBoxLeft = padding + iconSize + iconPadding + labelWidth;
        int textBoxWidth = Width - textBoxLeft - buttonWidth - settingsWidth - padding * 3;
        int containerHeight = _addressBox.PreferredHeight + 6;
        int controlsY = (Height - containerHeight) / 2;

        _addressBoxContainer.Location = new Point(textBoxLeft, controlsY);
        _addressBoxContainer.Size = new Size(textBoxWidth, containerHeight);

        // Center textbox vertically inside container, with left padding
        int textBoxY = (containerHeight - _addressBox.PreferredHeight - 2) / 2;
        _addressBox.Location = new Point(4, textBoxY);
        _addressBox.Width = textBoxWidth - 10;

        _goButton.Location = new Point(Width - buttonWidth - settingsWidth - padding * 2, controlsY);
        _goButton.Height = containerHeight;

        _settingsButton.Location = new Point(Width - settingsWidth - padding, controlsY);
        _settingsButton.Height = containerHeight;
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

    private void SettingsButton_Click(object? sender, EventArgs e)
    {
        _manager.ShowSettings();
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
                if (Uri.TryCreate(url, UriKind.Absolute, out uri))
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
        _addressBoxContainer.BackColor = GetTextBoxBackColor();
        _goButton.BackColor = GetButtonBackColor();
        _goButton.ForeColor = GetSystemForeColor();
        _goButton.FlatAppearance.BorderColor = GetBorderColor();
        _settingsButton.BackColor = GetButtonBackColor();
        _settingsButton.ForeColor = GetSystemForeColor();
        _settingsButton.FlatAppearance.BorderColor = GetBorderColor();
        ApplyDarkMode();
        Invalidate();
    }

    #endregion

    #region Icon Extraction (IE-style favicon + shell icons)

    private async void AddressBox_TextChanged(object? sender, EventArgs e)
    {
        // Cancel any previous icon fetch
        _iconCts?.Cancel();
        _iconCts = new CancellationTokenSource();
        var token = _iconCts.Token;

        string input = _addressBox.Text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            _iconBox.Image?.Dispose();
            _iconBox.Image = null;
            return;
        }

        // Debounce - wait 400ms after typing stops before fetching
        try
        {
            await Task.Delay(400, token);
            if (token.IsCancellationRequested) return;

            var icon = await GetIconForInputAsync(input, token);

            if (!token.IsCancellationRequested && !IsDisposed)
            {
                var oldImage = _iconBox.Image;
                _iconBox.Image = icon;
                oldImage?.Dispose();
            }
            else
            {
                icon?.Dispose();
            }
        }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task<Image?> GetIconForInputAsync(string input, CancellationToken token)
    {
        string expanded = Environment.ExpandEnvironmentVariables(input);

        // 1. Check if it's a local file/folder - use shell icons
        if (Directory.Exists(expanded))
        {
            return GetFileIcon(expanded, isDirectory: true);
        }
        if (File.Exists(expanded))
        {
            return GetFileIcon(expanded, isDirectory: false);
        }

        // 2. Check if it's a full URL
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await GetFaviconAsync(uri, token);
        }

        // 3. Looks like a domain without scheme (e.g., "google.com")
        if (input.Contains('.') && !input.Contains('\\') && !input.Contains('/') && !input.Contains(' '))
        {
            if (Uri.TryCreate("https://" + input, UriKind.Absolute, out uri))
            {
                return await GetFaviconAsync(uri, token);
            }
        }

        // 4. Try to get icon by file extension if it looks like a path
        if (input.Contains('\\') || input.Contains('/'))
        {
            return GetFileIcon(expanded, isDirectory: false);
        }

        return null;
    }

    /// <summary>
    /// Gets the shell icon for a local file or folder using SHGetFileInfo
    /// </summary>
    private static Image? GetFileIcon(string path, bool isDirectory)
    {
        var shfi = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON;
        uint attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        // If file doesn't exist, use SHGFI_USEFILEATTRIBUTES to get icon by extension
        bool exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
        if (!exists)
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        try
        {
            IntPtr result = SHGetFileInfo(path, attributes, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

            if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
            {
                // Clone the icon to a bitmap before destroying the handle
                using var icon = Icon.FromHandle(shfi.hIcon);
                var bitmap = icon.ToBitmap();
                DestroyIcon(shfi.hIcon);
                return bitmap;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Gets favicon for a website URL the way IE did:
    /// 1. Parse HTML for link rel="icon" or link rel="shortcut icon"
    /// 2. Fall back to /favicon.ico at root
    /// </summary>
    private static async Task<Image?> GetFaviconAsync(Uri uri, CancellationToken token)
    {
        string baseUrl = $"{uri.Scheme}://{uri.Host}";

        try
        {
            // Step 1: Try to fetch and parse HTML for favicon link (like IE did)
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType?.Contains("text/html") == true)
                {
                    var html = await response.Content.ReadAsStringAsync(token);
                    var faviconUrl = ParseFaviconFromHtml(html, baseUrl);

                    if (!string.IsNullOrEmpty(faviconUrl))
                    {
                        var icon = await DownloadIconAsync(faviconUrl, token);
                        if (icon != null) return icon;
                    }
                }
            }
            catch (TaskCanceledException) { throw; }
            catch { /* Continue to fallback */ }

            // Step 2: Fall back to /favicon.ico at root (what IE does if no LINK tag)
            string rootFavicon = $"{baseUrl}/favicon.ico";
            return await DownloadIconAsync(rootFavicon, token);
        }
        catch (TaskCanceledException) { throw; }
        catch { }

        return null;
    }

    /// <summary>
    /// Parses HTML to find favicon URL from link tags (like IE did)
    /// </summary>
    private static string? ParseFaviconFromHtml(string html, string baseUrl)
    {
        // Look for: <link rel="icon" href="..."> or <link rel="shortcut icon" href="...">
        // Also handle: <link href="..." rel="icon">
        var patterns = new[]
        {
            @"<link[^>]*rel\s*=\s*[""'](?:shortcut\s+)?icon[""'][^>]*href\s*=\s*[""']([^""']+)[""']",
            @"<link[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*rel\s*=\s*[""'](?:shortcut\s+)?icon[""']",
            @"<link[^>]*rel\s*=\s*[""']apple-touch-icon[""'][^>]*href\s*=\s*[""']([^""']+)[""']"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string href = match.Groups[1].Value;
                return ResolveUrl(href, baseUrl);
            }
        }

        return null;
    }

    private static string ResolveUrl(string href, string baseUrl)
    {
        if (href.StartsWith("//"))
            return "https:" + href;
        if (href.StartsWith("/"))
            return baseUrl + href;
        if (href.StartsWith("http://") || href.StartsWith("https://"))
            return href;
        // Relative URL
        return baseUrl + "/" + href;
    }

    /// <summary>
    /// Downloads and parses an icon file (handles .ico, .png, .gif formats)
    /// </summary>
    private static async Task<Image?> DownloadIconAsync(string url, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            using var response = await _httpClient.SendAsync(request, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength == 0)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            if (bytes.Length == 0) return null;

            using var ms = new MemoryStream(bytes);

            // Try loading as .ico first (handles multi-size icon files)
            try
            {
                ms.Position = 0;
                using var icon = new Icon(ms);
                return icon.ToBitmap();
            }
            catch
            {
                // Fall back to Image.FromStream (handles png, gif, jpg, etc.)
                try
                {
                    ms.Position = 0;
                    return Image.FromStream(ms);
                }
                catch { }
            }
        }
        catch (TaskCanceledException) { throw; }
        catch { }

        return null;
    }

    #endregion
}
