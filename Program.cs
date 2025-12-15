using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AddressBar;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new AddressBarForm());
    }
}

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
    private const uint ABM_ACTIVATE = 0x06;
    private const uint ABM_WINDOWPOSCHANGED = 0x09;

    private const int ABN_STATECHANGE = 0x00;
    private const int ABN_POSCHANGED = 0x01;
    private const int ABN_FULLSCREENAPP = 0x02;
    private const int ABN_WINDOWARRANGE = 0x03;

    private const uint ABE_TOP = 1;

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

    private readonly int _appBarHeight = 30;
    private uint _callbackMessageId;
    private bool _appBarRegistered;
    private bool _isFullScreen;

    private readonly TextBox _addressBox;
    private readonly Button _goButton;
    private readonly Label _addressLabel;
    private readonly NotifyIcon _trayIcon;

    public AddressBarForm()
    {
        // Form setup
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = GetSystemBackColor();

        // Scale height for DPI
        _appBarHeight = LogicalToDeviceUnits(30);

        // Apply dark mode to title bar (affects some elements)
        ApplyDarkMode();

        // Create controls
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

        // System tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Address Bar",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, e) => ShowAppBar());
        contextMenu.Items.Add("Hide", null, (s, e) => HideAppBar());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (s, e) => Application.Exit());
        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (s, e) => ShowAppBar();

        // Listen for theme changes
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        Resize += (s, e) => LayoutControls();
        Load += AddressBarForm_Load;
        FormClosing += AddressBarForm_FormClosing;
    }

    private void AddressBarForm_Load(object? sender, EventArgs e)
    {
        RegisterAppBar();
        LayoutControls();
    }

    private void AddressBarForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        UnregisterAppBar();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
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

        _callbackMessageId = (uint)RegisterWindowMessage("AddressBarAppBarMessage");

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

        var screen = Screen.PrimaryScreen!;
        var workArea = screen.Bounds;

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = Handle,
            uEdge = ABE_TOP,
            rc = new RECT
            {
                left = workArea.Left,
                top = workArea.Top,
                right = workArea.Right,
                bottom = workArea.Top + _appBarHeight
            }
        };

        // Query for position
        SHAppBarMessage(ABM_QUERYPOS, ref abd);

        // Adjust based on query result
        abd.rc.bottom = abd.rc.top + _appBarHeight;

        // Set the position
        SHAppBarMessage(ABM_SETPOS, ref abd);

        // Actually move the window
        SetBounds(abd.rc.left, abd.rc.top, abd.rc.right - abd.rc.left, abd.rc.bottom - abd.rc.top);

        // Ensure topmost
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void ShowAppBar()
    {
        Show();
        if (!_appBarRegistered)
        {
            RegisterAppBar();
        }
        SetAppBarPos();
    }

    private void HideAppBar()
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
                        // A fullscreen app is active, lower our z-order
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
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW - don't show in taskbar/alt-tab
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
            // Check if it's a URL
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                Process.Start(new ProcessStartInfo(input) { UseShellExecute = true });
                return;
            }

            // Check if it looks like a URL without scheme
            if (input.Contains('.') && !input.Contains(' ') && !input.Contains('\\') && !input.Contains('/'))
            {
                string url = "https://" + input;
                if (Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return;
                }
            }

            // Check if it's a file or folder path
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

            // Try as shell command
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

    private static Color GetSystemBackColor()
    {
        return IsDarkMode() ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
    }

    private static Color GetSystemForeColor()
    {
        return IsDarkMode() ? Color.FromArgb(255, 255, 255) : Color.FromArgb(0, 0, 0);
    }

    private static Color GetTextBoxBackColor()
    {
        return IsDarkMode() ? Color.FromArgb(45, 45, 45) : Color.White;
    }

    private static Color GetButtonBackColor()
    {
        return IsDarkMode() ? Color.FromArgb(55, 55, 55) : Color.FromArgb(225, 225, 225);
    }

    private static Color GetBorderColor()
    {
        return IsDarkMode() ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            // Theme might have changed
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
