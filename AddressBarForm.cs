using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AddressBar.AutoComplete;
using Microsoft.Win32;

namespace AddressBar;

public class AddressBarForm : Form
{
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
    private readonly Button _dockButton;
    private readonly Label _addressLabel;
    private readonly PictureBox _iconBox;
    private readonly Button _dropdownButton;
    private readonly HistoryDropdown _historyDropdown;
    private AutoCompleteController _autoComplete;
    private readonly List<string> _history = new();
    private readonly Dictionary<string, Image?> _historyIcons = new();
    private CancellationTokenSource? _iconCts;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private const string HistoryRegPath = @"Software\AddressBar\TypedPaths";
    private const int MaxHistoryItems = 25;

    // Floating mode state
    private bool _isDragging;
    private Point _dragStartPoint;
    private bool _isResizing;
    private const int ResizeGripSize = 8;

    public AddressBarForm(Screen screen, AppSettings settings, AddressBarManager manager)
    {
        _screen = screen;
        _settings = settings;
        _manager = manager;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = ThemeHelper.GetSystemBackColor();

        _appBarHeight = LogicalToDeviceUnits(_settings.BarHeight);

        if (_settings.IsFloating)
        {
            Location = new Point(_settings.FloatingX, _settings.FloatingY);
            Size = new Size(_settings.FloatingWidth, _appBarHeight);
            MinimumSize = new Size(200, _appBarHeight);
            MaximumSize = new Size(800, _appBarHeight);

            MouseDown += FloatingForm_MouseDown;
            MouseMove += FloatingForm_MouseMove;
            MouseUp += FloatingForm_MouseUp;
            LocationChanged += FloatingForm_LocationChanged;
            SizeChanged += FloatingForm_SizeChanged;
        }

        ApplyDarkMode();

        _addressLabel = new Label
        {
            Text = "Address",
            AutoSize = true,
            ForeColor = ThemeHelper.GetSystemForeColor(),
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _addressBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9f),
            BackColor = ThemeHelper.GetTextBoxBackColor(),
            ForeColor = ThemeHelper.GetSystemForeColor()
        };
        _addressBox.KeyDown += AddressBox_KeyDown;
        _addressBox.TextChanged += AddressBox_TextChanged;

        _addressBoxContainer = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeHelper.GetTextBoxBackColor()
        };
        _addressBoxContainer.Controls.Add(_addressBox);

        _goButton = new Button
        {
            Text = "Go",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            BackColor = ThemeHelper.GetButtonBackColor(),
            ForeColor = ThemeHelper.GetSystemForeColor(),
            Cursor = Cursors.Hand,
            Width = LogicalToDeviceUnits(40)
        };
        _goButton.FlatAppearance.BorderSize = 1;
        _goButton.FlatAppearance.BorderColor = ThemeHelper.GetBorderColor();
        _goButton.Click += GoButton_Click;

        _settingsButton = new Button
        {
            Text = "\u2699",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            BackColor = ThemeHelper.GetButtonBackColor(),
            ForeColor = ThemeHelper.GetSystemForeColor(),
            Cursor = Cursors.Hand,
            Width = LogicalToDeviceUnits(30)
        };
        _settingsButton.FlatAppearance.BorderSize = 1;
        _settingsButton.FlatAppearance.BorderColor = ThemeHelper.GetBorderColor();
        _settingsButton.Click += SettingsButton_Click;

        _dockButton = new Button
        {
            Text = _settings.IsFloating ? "\ud83d\udccc" : "\ud83d\udccd",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            BackColor = ThemeHelper.GetButtonBackColor(),
            ForeColor = ThemeHelper.GetSystemForeColor(),
            Cursor = Cursors.Hand,
            Width = LogicalToDeviceUnits(30)
        };
        _dockButton.FlatAppearance.BorderSize = 1;
        _dockButton.FlatAppearance.BorderColor = ThemeHelper.GetBorderColor();
        _dockButton.Click += DockButton_Click;
        var toolTip = new ToolTip();
        toolTip.SetToolTip(_dockButton, _settings.IsFloating ? "Dock to screen edge" : "Undock to floating mode");

        _iconBox = new PictureBox
        {
            Size = new Size(16, 16),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = (_settings.IconPosition == IconPosition.Inside || _settings.IsFloating)
                ? ThemeHelper.GetTextBoxBackColor()
                : ThemeHelper.GetSystemBackColor()
        };

        _dropdownButton = new Button
        {
            Text = "\u25bc",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7f),
            BackColor = ThemeHelper.GetTextBoxBackColor(),
            ForeColor = ThemeHelper.GetSystemForeColor(),
            Cursor = Cursors.Hand,
            Width = LogicalToDeviceUnits(20),
            TabStop = false
        };
        _dropdownButton.FlatAppearance.BorderSize = 0;
        _dropdownButton.FlatAppearance.BorderColor = ThemeHelper.GetTextBoxBackColor();
        _dropdownButton.FlatAppearance.MouseOverBackColor = ThemeHelper.GetDropdownHoverColor();
        _dropdownButton.FlatAppearance.MouseDownBackColor = ThemeHelper.GetDropdownHoverColor();
        _dropdownButton.Click += DropdownButton_Click;
        _dropdownButton.GotFocus += (s, e) => _addressBox.Focus();

        _historyDropdown = new HistoryDropdown();
        _historyDropdown.ItemSelected += HistoryDropdown_ItemSelected;

        _autoComplete = null!;

        if (_settings.IconPosition == IconPosition.Inside || _settings.IsFloating)
        {
            _addressBoxContainer.Controls.Add(_iconBox);
        }
        else
        {
            Controls.Add(_iconBox);
        }

        _addressBoxContainer.Controls.Add(_dropdownButton);

        Controls.Add(_addressLabel);
        Controls.Add(_addressBoxContainer);
        Controls.Add(_goButton);
        Controls.Add(_dockButton);
        Controls.Add(_settingsButton);

        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        Resize += (s, e) => LayoutControls();
        Load += AddressBarForm_Load;
    }

    private void AddressBarForm_Load(object? sender, EventArgs e)
    {
        // Exclude from Aero Peek so the bar stays visible when hovering taskbar previews
        int excludeFromPeek = 1;
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_EXCLUDED_FROM_PEEK,
            ref excludeFromPeek, sizeof(int));

        if (!_settings.IsFloating)
        {
            RegisterAppBar();
        }

        SetTextBoxMargins();
        LayoutControls();
        _iconBox.Image = GetDefaultAppIcon();
        LoadHistory();

        _autoComplete = new AutoCompleteController(_addressBox, _addressBoxContainer);
        _autoComplete.DockAtBottom = _settings.DockPosition == DockPosition.Bottom && !_settings.IsFloating;
        _autoComplete.PreloadCommonPaths();
    }

    private void SetTextBoxMargins()
    {
        int leftMargin = LogicalToDeviceUnits(4);
        NativeMethods.SendMessage(_addressBox.Handle, NativeMethods.EM_SETMARGINS,
            (IntPtr)NativeMethods.EC_LEFTMARGIN, (IntPtr)leftMargin);
    }

    public void Cleanup()
    {
        UnregisterAppBar();
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _iconCts?.Cancel();
        _iconCts?.Dispose();
        _iconBox.Image?.Dispose();
        _autoComplete.Dispose();
    }

    private void LayoutControls()
    {
        int padding = LogicalToDeviceUnits(6);
        int iconSize = LogicalToDeviceUnits(16);
        int iconPadding = LogicalToDeviceUnits(4);
        int labelWidth = LogicalToDeviceUnits(50);
        int buttonWidth = _goButton.Width;
        int dockWidth = _dockButton.Width;
        int settingsWidth = _settingsButton.Width;
        int dropdownWidth = _dropdownButton.Width;

        int containerHeight = _addressBox.PreferredHeight + 6;
        int controlsY = (Height - containerHeight) / 2;

        _iconBox.Size = new Size(iconSize, iconSize);

        bool isCompact = _settings.IsFloating;
        _addressLabel.Visible = !isCompact;

        int rightButtonsWidth = buttonWidth + dockWidth + settingsWidth + padding * 4;

        if (isCompact)
        {
            int textBoxLeft = padding;
            int textBoxWidth = Width - textBoxLeft - rightButtonsWidth;

            _addressBoxContainer.Location = new Point(textBoxLeft, controlsY);
            _addressBoxContainer.Size = new Size(textBoxWidth, containerHeight);

            _iconBox.Location = new Point(4, (containerHeight - iconSize - 2) / 2);

            _dropdownButton.Height = containerHeight - 2;
            _dropdownButton.Location = new Point(textBoxWidth - dropdownWidth - 1, 0);

            int textBoxY = (containerHeight - _addressBox.PreferredHeight - 2) / 2;
            int textBoxInternalLeft = iconSize + iconPadding + 4;
            _addressBox.Location = new Point(textBoxInternalLeft, textBoxY);
            _addressBox.Width = textBoxWidth - textBoxInternalLeft - dropdownWidth - 6;
        }
        else if (_settings.IconPosition == IconPosition.Left)
        {
            _iconBox.Location = new Point(padding, (Height - iconSize) / 2);
            _addressLabel.Location = new Point(padding + iconSize + iconPadding, (Height - _addressLabel.Height) / 2);

            int textBoxLeft = padding + iconSize + iconPadding + labelWidth;
            int textBoxWidth = Width - textBoxLeft - rightButtonsWidth;

            _addressBoxContainer.Location = new Point(textBoxLeft, controlsY);
            _addressBoxContainer.Size = new Size(textBoxWidth, containerHeight);

            _dropdownButton.Height = containerHeight - 2;
            _dropdownButton.Location = new Point(textBoxWidth - dropdownWidth - 1, 0);

            int textBoxY = (containerHeight - _addressBox.PreferredHeight - 2) / 2;
            _addressBox.Location = new Point(4, textBoxY);
            _addressBox.Width = textBoxWidth - dropdownWidth - 10;
        }
        else
        {
            _addressLabel.Location = new Point(padding, (Height - _addressLabel.Height) / 2);

            int textBoxLeft = padding + labelWidth;
            int textBoxWidth = Width - textBoxLeft - rightButtonsWidth;

            _addressBoxContainer.Location = new Point(textBoxLeft, controlsY);
            _addressBoxContainer.Size = new Size(textBoxWidth, containerHeight);

            _iconBox.Location = new Point(4, (containerHeight - iconSize - 2) / 2);

            _dropdownButton.Height = containerHeight - 2;
            _dropdownButton.Location = new Point(textBoxWidth - dropdownWidth - 1, 0);

            int textBoxY = (containerHeight - _addressBox.PreferredHeight - 2) / 2;
            int textBoxInternalLeft = iconSize + iconPadding + 4;
            _addressBox.Location = new Point(textBoxInternalLeft, textBoxY);
            _addressBox.Width = textBoxWidth - textBoxInternalLeft - dropdownWidth - 6;
        }

        _goButton.Location = new Point(Width - buttonWidth - dockWidth - settingsWidth - padding * 3, controlsY);
        _goButton.Height = containerHeight;

        _dockButton.Location = new Point(Width - dockWidth - settingsWidth - padding * 2, controlsY);
        _dockButton.Height = containerHeight;

        _settingsButton.Location = new Point(Width - settingsWidth - padding, controlsY);
        _settingsButton.Height = containerHeight;
    }

    #region AppBar Registration

    private void RegisterAppBar()
    {
        if (_appBarRegistered) return;

        _callbackMessageId = (uint)NativeMethods.RegisterWindowMessage($"AddressBarAppBarMessage_{_screen.DeviceName}");

        var abd = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = Handle,
            uCallbackMessage = _callbackMessageId
        };

        uint result = NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd);
        if (result != 0)
        {
            _appBarRegistered = true;
            SetAppBarPos();
        }
    }

    private void UnregisterAppBar()
    {
        if (!_appBarRegistered) return;

        var abd = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = Handle
        };

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
        _appBarRegistered = false;
    }

    private void SetAppBarPos()
    {
        if (!_appBarRegistered) return;

        var bounds = _screen.Bounds;
        uint edge = _settings.DockPosition == DockPosition.Top ? NativeMethods.ABE_TOP : NativeMethods.ABE_BOTTOM;

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

        var abd = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = Handle,
            uEdge = edge,
            rc = new NativeMethods.RECT
            {
                left = bounds.Left,
                top = top,
                right = bounds.Right,
                bottom = bottom
            }
        };

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);

        if (_settings.DockPosition == DockPosition.Top)
            abd.rc.bottom = abd.rc.top + _appBarHeight;
        else
            abd.rc.top = abd.rc.bottom - _appBarHeight;

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

        SetBounds(abd.rc.left, abd.rc.top, abd.rc.right - abd.rc.left, abd.rc.bottom - abd.rc.top);
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
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
                case NativeMethods.ABN_POSCHANGED:
                    SetAppBarPos();
                    break;

                case NativeMethods.ABN_FULLSCREENAPP:
                    _isFullScreen = m.LParam.ToInt32() != 0;
                    if (_isFullScreen)
                    {
                        Hide();
                        TopMost = false;
                    }
                    else
                    {
                        Show();
                        TopMost = true;
                        SetAppBarPos();
                        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    }
                    break;

                case NativeMethods.ABN_STATECHANGE:
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
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    #endregion

    #region Address Navigation

    private void AddressBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;

        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            if (_addressBox.SelectionLength > 0)
            {
                _addressBox.SelectionStart = _addressBox.Text.Length;
                _addressBox.SelectionLength = 0;
            }
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

    private void DockButton_Click(object? sender, EventArgs e)
    {
        _manager.ToggleFloatingMode();
    }

    #region Floating Mode

    private void FloatingForm_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (e.X >= Width - ResizeGripSize)
        {
            _isResizing = true;
        }
        else
        {
            _isDragging = true;
            _dragStartPoint = e.Location;
        }
    }

    private void FloatingForm_MouseMove(object? sender, MouseEventArgs e)
    {
        if (e.X >= Width - ResizeGripSize)
        {
            Cursor = Cursors.SizeWE;
        }
        else if (!_isResizing)
        {
            Cursor = Cursors.SizeAll;
        }

        if (_isDragging)
        {
            var screenPoint = PointToScreen(e.Location);
            Location = new Point(screenPoint.X - _dragStartPoint.X, screenPoint.Y - _dragStartPoint.Y);
        }
        else if (_isResizing)
        {
            int newWidth = Math.Max(MinimumSize.Width, Math.Min(MaximumSize.Width, e.X + ResizeGripSize));
            Width = newWidth;
        }
    }

    private void FloatingForm_MouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
        _isResizing = false;
    }

    private void FloatingForm_LocationChanged(object? sender, EventArgs e)
    {
        if (_settings.IsFloating && !_isDragging)
        {
            return;
        }

        if (_settings.IsFloating)
        {
            _settings.FloatingX = Location.X;
            _settings.FloatingY = Location.Y;
        }
    }

    private void FloatingForm_SizeChanged(object? sender, EventArgs e)
    {
        if (_settings.IsFloating)
        {
            _settings.FloatingWidth = Width;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_settings.IsFloating && (_isDragging || _isResizing))
        {
            _settings.Save();
        }

        _isDragging = false;
        _isResizing = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_isDragging && !_isResizing)
        {
            Cursor = Cursors.Default;
        }
    }

    #endregion

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
                SaveToHistory(input);
                return;
            }

            if (input.Contains('.') && !input.Contains(' ') && !input.Contains('\\'))
            {
                string url = "https://" + input;
                if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    SaveToHistory(input);
                    return;
                }
            }

            string expandedPath = Environment.ExpandEnvironmentVariables(input);
            if (Directory.Exists(expandedPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{expandedPath}\"") { UseShellExecute = true });
                SaveToHistory(input);
                return;
            }

            if (File.Exists(expandedPath))
            {
                Process.Start(new ProcessStartInfo(expandedPath) { UseShellExecute = true });
                SaveToHistory(input);
                return;
            }

            string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                string command = parts[0];
                string arguments = parts[1];
                Process.Start(new ProcessStartInfo(command, arguments) { UseShellExecute = true });
                SaveToHistory(input);
                return;
            }

            Process.Start(new ProcessStartInfo(input) { UseShellExecute = true });
            SaveToHistory(input);
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
        if (ThemeHelper.IsDarkMode())
        {
            int value = 1;
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            UpdateTheme();
        }
    }

    private void UpdateTheme()
    {
        BackColor = ThemeHelper.GetSystemBackColor();
        _addressLabel.ForeColor = ThemeHelper.GetSystemForeColor();
        _addressBox.BackColor = ThemeHelper.GetTextBoxBackColor();
        _addressBox.ForeColor = ThemeHelper.GetSystemForeColor();
        _addressBoxContainer.BackColor = ThemeHelper.GetTextBoxBackColor();
        _iconBox.BackColor = _settings.IconPosition == IconPosition.Inside || _settings.IsFloating
            ? ThemeHelper.GetTextBoxBackColor()
            : ThemeHelper.GetSystemBackColor();
        _dropdownButton.BackColor = ThemeHelper.GetTextBoxBackColor();
        _dropdownButton.ForeColor = ThemeHelper.GetSystemForeColor();
        _dropdownButton.FlatAppearance.BorderColor = ThemeHelper.GetTextBoxBackColor();
        _dropdownButton.FlatAppearance.MouseOverBackColor = ThemeHelper.GetDropdownHoverColor();
        _dropdownButton.FlatAppearance.MouseDownBackColor = ThemeHelper.GetDropdownHoverColor();
        _goButton.BackColor = ThemeHelper.GetButtonBackColor();
        _goButton.ForeColor = ThemeHelper.GetSystemForeColor();
        _goButton.FlatAppearance.BorderColor = ThemeHelper.GetBorderColor();
        _dockButton.BackColor = ThemeHelper.GetButtonBackColor();
        _dockButton.ForeColor = ThemeHelper.GetSystemForeColor();
        _dockButton.FlatAppearance.BorderColor = ThemeHelper.GetBorderColor();
        _settingsButton.BackColor = ThemeHelper.GetButtonBackColor();
        _settingsButton.ForeColor = ThemeHelper.GetSystemForeColor();
        _settingsButton.FlatAppearance.BorderColor = ThemeHelper.GetBorderColor();
        ApplyDarkMode();
        Invalidate();
    }

    #endregion

    #region Icon Extraction

    private async void AddressBox_TextChanged(object? sender, EventArgs e)
    {
        _iconCts?.Cancel();
        _iconCts = new CancellationTokenSource();
        var token = _iconCts.Token;

        string input = _addressBox.Text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            var oldImage = _iconBox.Image;
            _iconBox.Image = GetDefaultAppIcon();
            oldImage?.Dispose();
            return;
        }

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

        if (Directory.Exists(expanded))
        {
            return GetFileIcon(expanded, isDirectory: true);
        }
        if (File.Exists(expanded))
        {
            return GetFileIcon(expanded, isDirectory: false);
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await GetFaviconAsync(uri, token) ?? GetDefaultAppIcon();
        }

        if (input.Contains('.') && !input.Contains('\\') && !input.Contains(' '))
        {
            if (Uri.TryCreate("https://" + input, UriKind.Absolute, out uri))
            {
                return await GetFaviconAsync(uri, token) ?? GetDefaultAppIcon();
            }
        }

        // Check for command with arguments (e.g., "notepad C:\github\")
        if (input.Contains(' '))
        {
            string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1)
            {
                string command = parts[0];
                var exePath = FindExecutableInPath(command);
                if (exePath != null)
                {
                    return GetFileIcon(exePath, isDirectory: false);
                }
            }
        }

        if (input.Contains('\\'))
        {
            return GetFileIcon(expanded, isDirectory: false);
        }

        var exePathSimple = FindExecutableInPath(input);
        if (exePathSimple != null)
        {
            return GetFileIcon(exePathSimple, isDirectory: false);
        }

        return null;
    }

    private static Image? GetDefaultAppIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "addressbar.ico");
            if (File.Exists(iconPath))
            {
                using var icon = new Icon(iconPath, 16, 16);
                return icon.ToBitmap();
            }
        }
        catch { }
        return null;
    }

    private static string? FindExecutableInPath(string name)
    {
        string fileName = name;
        if (!Path.HasExtension(fileName))
        {
            fileName += ".exe";
        }

        if (File.Exists(fileName))
        {
            return Path.GetFullPath(fileName);
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var dir in paths)
        {
            try
            {
                var fullPath = Path.Combine(dir, fileName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch { }
        }

        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var winPath = Path.Combine(winDir, fileName);
        if (File.Exists(winPath))
            return winPath;

        var sys32Path = Path.Combine(winDir, "System32", fileName);
        if (File.Exists(sys32Path))
            return sys32Path;

        return null;
    }

    private static Image? GetFileIcon(string path, bool isDirectory)
    {
        try
        {
            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(path);
            }

            bool exists = isDirectory ? Directory.Exists(path) : File.Exists(path);

            if (exists && !isDirectory)
            {
                try
                {
                    using var icon = Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        return icon.ToBitmap();
                    }
                }
                catch { }
            }

            var shfi = new NativeMethods.SHFILEINFO();
            uint flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON;
            uint attributes = isDirectory ? NativeMethods.FILE_ATTRIBUTE_DIRECTORY : NativeMethods.FILE_ATTRIBUTE_NORMAL;

            if (!exists)
            {
                flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
            }

            IntPtr result = NativeMethods.SHGetFileInfo(path, attributes, ref shfi,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);

            if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
            {
                try
                {
                    using var icon = Icon.FromHandle(shfi.hIcon);
                    return icon.ToBitmap();
                }
                finally
                {
                    NativeMethods.DestroyIcon(shfi.hIcon);
                }
            }
        }
        catch { }

        return null;
    }

    private static async Task<Image?> GetFaviconAsync(Uri uri, CancellationToken token)
    {
        string baseUrl = $"{uri.Scheme}://{uri.Host}";

        try
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

                var finalUri = response.RequestMessage?.RequestUri ?? uri;
                baseUrl = $"{finalUri.Scheme}://{finalUri.Host}";

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
            catch { }

            string rootFavicon = $"{baseUrl}/favicon.ico";
            return await DownloadIconAsync(rootFavicon, token);
        }
        catch (TaskCanceledException) { throw; }
        catch { }

        return null;
    }

    private static string? ParseFaviconFromHtml(string html, string baseUrl)
    {
        var patterns = new[]
        {
            @"<link[^>]*rel\s*=\s*[""']?(?:shortcut\s+)?icon[""']?[^>]*href\s*=\s*[""']?([^""'\s>]+)[""']?",
            @"<link[^>]*href\s*=\s*[""']?([^""'\s>]+)[""']?[^>]*rel\s*=\s*[""']?(?:shortcut\s+)?icon[""']?",
            @"<link[^>]*rel\s*=\s*[""']?apple-touch-icon[""']?[^>]*href\s*=\s*[""']?([^""'\s>]+)[""']?"
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
        return baseUrl + "/" + href;
    }

    private static async Task<Image?> DownloadIconAsync(string url, CancellationToken token)
    {
        try
        {
            if (url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            using var response = await _httpClient.SendAsync(request, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength == 0)
                return null;

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType?.Contains("svg") == true)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            if (bytes.Length == 0) return null;

            using var ms = new MemoryStream(bytes);

            try
            {
                ms.Position = 0;
                using var icon = new Icon(ms);
                return icon.ToBitmap();
            }
            catch
            {
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

    #region History Management

    private void LoadHistory()
    {
        _history.Clear();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(HistoryRegPath);
            if (key != null)
            {
                var names = key.GetValueNames().OrderBy(n => n).ToList();
                foreach (var name in names)
                {
                    var value = key.GetValue(name) as string;
                    if (!string.IsNullOrEmpty(value) && !_history.Contains(value))
                    {
                        _history.Add(value);
                    }
                }
            }
        }
        catch { }

        _ = LoadHistoryIconsAsync();
    }

    private async Task LoadHistoryIconsAsync()
    {
        foreach (var path in _history.ToList())
        {
            if (_historyIcons.ContainsKey(path)) continue;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var icon = await GetIconForInputAsync(path, cts.Token);
                _historyIcons[path] = icon;
            }
            catch
            {
                _historyIcons[path] = null;
            }
        }
    }

    private void SaveToHistory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        _history.Remove(path);
        _history.Insert(0, path);

        while (_history.Count > MaxHistoryItems)
        {
            _history.RemoveAt(_history.Count - 1);
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(HistoryRegPath);
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                {
                    key.DeleteValue(name, false);
                }

                for (int i = 0; i < _history.Count; i++)
                {
                    key.SetValue($"url{i + 1}", _history[i]);
                }
            }
        }
        catch { }

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var icon = await GetIconForInputAsync(path, cts.Token);
                _historyIcons[path] = icon;
            }
            catch
            {
                _historyIcons[path] = null;
            }
        });
    }

    private void DropdownButton_Click(object? sender, EventArgs e)
    {
        var items = _history.Select(path =>
        {
            _historyIcons.TryGetValue(path, out var icon);
            return (path, icon);
        }).ToList();

        _historyDropdown.SetItems(items);
        _historyDropdown.SetWidth(_addressBoxContainer.Width);

        var screenPoint = _addressBoxContainer.PointToScreen(new Point(0, _addressBoxContainer.Height));

        if (_settings.DockPosition == DockPosition.Bottom)
        {
            screenPoint = _addressBoxContainer.PointToScreen(new Point(0, -_historyDropdown.Height));
        }

        _historyDropdown.ShowAt(screenPoint);
    }

    private void HistoryDropdown_ItemSelected(object? sender, string path)
    {
        _addressBox.Text = path;
        Navigate();
    }

    #endregion
}
