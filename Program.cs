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
public enum IconPosition { Inside, Left }

public class AppSettings
{
    public MonitorMode MonitorMode { get; set; } = MonitorMode.Single;
    public int MonitorIndex { get; set; } = 0;
    public int BarHeight { get; set; } = 40;
    public DockPosition DockPosition { get; set; } = DockPosition.Top;
    public IconPosition IconPosition { get; set; } = IconPosition.Inside;
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
    private readonly ComboBox _iconPositionCombo;
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
        Size = new Size(350, 290);
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

        // Icon Position
        var iconPosLabel = new Label { Text = "Icon Position:", Location = new Point(labelX, y + 3), AutoSize = true, Font = labelFont };
        _iconPositionCombo = new ComboBox
        {
            Location = new Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = labelFont
        };
        _iconPositionCombo.Items.AddRange(new object[] { "Inside Address Box", "Left of Address Box" });
        _iconPositionCombo.SelectedIndex = _settings.IconPosition == IconPosition.Left ? 1 : 0;
        Controls.Add(iconPosLabel);
        Controls.Add(_iconPositionCombo);
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
        var newIconPosition = _iconPositionCombo.SelectedIndex == 1 ? IconPosition.Left : IconPosition.Inside;
        var newStartup = _startupCheckbox.Checked;

        if (newMode != _settings.MonitorMode ||
            newMonitorIndex != _settings.MonitorIndex ||
            newDockPosition != _settings.DockPosition ||
            newBarHeight != _settings.BarHeight ||
            newIconPosition != _settings.IconPosition ||
            newStartup != _settings.RunAtStartup)
        {
            _settings.MonitorMode = newMode;
            _settings.MonitorIndex = newMonitorIndex;
            _settings.DockPosition = newDockPosition;
            _settings.BarHeight = newBarHeight;
            _settings.IconPosition = newIconPosition;
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
            var iconPath = Path.Combine(AppContext.BaseDirectory, "addressbar.ico");
            if (File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch { }
        return SystemIcons.Application;
    }
}

#endregion

#region AutoComplete System

/// <summary>
/// Explorer-style autocomplete system with background threading, hierarchical expansion,
/// caching, inline auto-append, and smooth dropdown rendering.
/// </summary>
public class AutoCompleteController : IDisposable
{
    private readonly TextBox _textBox;
    private readonly Control _parent;
    private readonly AutoCompleteDropdown _dropdown;
    private readonly Dictionary<string, List<string>> _cache = new();
    private readonly object _cacheLock = new();
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private CancellationTokenSource? _enumCts;
    private Thread? _enumThread;
    private string _pendingText = "";
    private bool _isAutoAppending;
    private bool _suppressTextChanged;
    private bool _suppressLostFocus;
    private int _selectedIndex = -1;
    private List<string> _currentItems = new();
    private const int DebounceMs = 150;
    private bool _dockAtBottom;

    public event EventHandler<string>? ItemSelected;

    /// <summary>
    /// Set to true when the address bar is docked at the bottom of the screen
    /// </summary>
    public bool DockAtBottom
    {
        get => _dockAtBottom;
        set => _dockAtBottom = value;
    }

    /// <summary>
    /// Preload common directories into cache for instant response
    /// </summary>
    public void PreloadCommonPaths()
    {
        // Fire and forget - preload in background
        Task.Run(() =>
        {
            try
            {
                // Preload C:\
                PreloadDirectory("C:\\");

                // Preload user directories
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    PreloadDirectory(userProfile + "\\");
                }

                // Preload common user folders
                PreloadDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\");
                PreloadDirectory(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\");
            }
            catch { }
        });
    }

    private void PreloadDirectory(string dirPath)
    {
        if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath)) return;

        var key = dirPath.ToLowerInvariant();
        lock (_cacheLock)
        {
            if (_cache.ContainsKey(key)) return; // Already cached
        }

        var items = new List<string>();
        try
        {
            // Enumerate directories first
            foreach (var dir in Directory.EnumerateDirectories(dirPath))
            {
                items.Add(dir);
            }
            // Then files
            foreach (var file in Directory.EnumerateFiles(dirPath))
            {
                items.Add(file);
            }
        }
        catch { }

        if (items.Count > 0)
        {
            lock (_cacheLock)
            {
                _cache[key] = items;
            }
        }
    }

    public AutoCompleteController(TextBox textBox, Control parent)
    {
        _textBox = textBox;
        _parent = parent;
        _dropdown = new AutoCompleteDropdown();
        _dropdown.ItemClicked += OnDropdownItemClicked;
        _dropdown.DismissRequested += OnDropdownDismissed;
        _dropdown.SetOwner(parent.FindForm());
        _dropdown.SetTargetTextBox(textBox);

        _debounceTimer = new System.Windows.Forms.Timer { Interval = DebounceMs };
        _debounceTimer.Tick += OnDebounceTimerTick;

        _textBox.TextChanged += OnTextChanged;
        _textBox.KeyDown += OnKeyDown;
        _textBox.KeyPress += OnKeyPress;
        _textBox.LostFocus += OnLostFocus;
    }

    private void OnDropdownDismissed(object? sender, EventArgs e)
    {
        _selectedIndex = -1;
        _isAutoAppending = false;
    }

    public void Dispose()
    {
        _debounceTimer.Stop();
        _debounceTimer.Dispose();
        _enumCts?.Cancel();
        _enumCts?.Dispose();
        _dropdown.Dispose();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_suppressTextChanged) return;

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _pendingText = _textBox.Text;
        StartEnumeration(_pendingText);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_dropdown.Visible)
        {
            // Open dropdown on Alt+Down or when we have items
            if (e.KeyCode == Keys.Down && e.Alt)
            {
                if (_currentItems.Count > 0)
                {
                    ShowDropdown();
                    e.Handled = true;
                }
            }
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Down:
                _selectedIndex = Math.Min(_selectedIndex + 1, _currentItems.Count - 1);
                _dropdown.SetSelectedIndex(_selectedIndex);
                e.Handled = true;
                break;

            case Keys.Up:
                _selectedIndex = Math.Max(_selectedIndex - 1, -1);
                _dropdown.SetSelectedIndex(_selectedIndex);
                e.Handled = true;
                break;

            case Keys.Enter:
            case Keys.Tab:
                if (_selectedIndex >= 0 && _selectedIndex < _currentItems.Count)
                {
                    AcceptCompletion(_currentItems[_selectedIndex]);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (_isAutoAppending && _textBox.SelectionLength > 0)
                {
                    // Accept the auto-appended text
                    _suppressTextChanged = true;
                    _textBox.SelectionStart = _textBox.Text.Length;
                    _textBox.SelectionLength = 0;
                    _suppressTextChanged = false;
                    HideDropdown();
                    e.Handled = true;
                    if (e.KeyCode == Keys.Tab) e.SuppressKeyPress = true;
                }
                break;

            case Keys.Escape:
                HideDropdown();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            case Keys.PageDown:
                _selectedIndex = Math.Min(_selectedIndex + 10, _currentItems.Count - 1);
                _dropdown.SetSelectedIndex(_selectedIndex);
                e.Handled = true;
                break;

            case Keys.PageUp:
                _selectedIndex = Math.Max(_selectedIndex - 10, 0);
                _dropdown.SetSelectedIndex(_selectedIndex);
                e.Handled = true;
                break;
        }
    }

    private void OnKeyPress(object? sender, KeyPressEventArgs e)
    {
        // When user types a character that matches the auto-appended text, consume it
        if (_isAutoAppending && _textBox.SelectionLength > 0)
        {
            int selStart = _textBox.SelectionStart;
            if (selStart < _textBox.Text.Length)
            {
                char expected = _textBox.Text[selStart];
                if (char.ToLowerInvariant(e.KeyChar) == char.ToLowerInvariant(expected))
                {
                    _suppressTextChanged = true;
                    _textBox.SelectionStart = selStart + 1;
                    _textBox.SelectionLength = _textBox.Text.Length - selStart - 1;
                    _suppressTextChanged = false;
                    e.Handled = true;

                    // Re-trigger enumeration if this was a path separator
                    if (e.KeyChar == '\\' || e.KeyChar == '/')
                    {
                        _pendingText = _textBox.Text.Substring(0, _textBox.SelectionStart);
                        StartEnumeration(_pendingText);
                    }
                }
            }
        }
    }

    private void OnLostFocus(object? sender, EventArgs e)
    {
        // Ignore if we're in the middle of showing the dropdown
        if (_suppressLostFocus) return;

        // Small delay to allow dropdown clicks to be processed first
        // The dropdown won't steal focus due to WS_EX_NOACTIVATE
        Task.Delay(100).ContinueWith(_ =>
        {
            if (_textBox.IsHandleCreated && !_textBox.IsDisposed)
            {
                _textBox.BeginInvoke(() =>
                {
                    // Only hide if textbox still doesn't have focus
                    if (!_textBox.Focused && !_suppressLostFocus)
                    {
                        HideDropdown();
                    }
                });
            }
        });
    }

    private void OnDropdownItemClicked(object? sender, string item)
    {
        AcceptCompletion(item);
    }

    private void AcceptCompletion(string item)
    {
        _suppressTextChanged = true;
        _textBox.Text = item;
        _textBox.SelectionStart = item.Length;
        _suppressTextChanged = false;
        _isAutoAppending = false;
        HideDropdown();
        ItemSelected?.Invoke(this, item);

        // If it's a directory, trigger new enumeration for contents
        if (Directory.Exists(item) && !item.EndsWith("\\"))
        {
            _suppressTextChanged = true;
            _textBox.Text = item + "\\";
            _textBox.SelectionStart = _textBox.Text.Length;
            _suppressTextChanged = false;
            _pendingText = _textBox.Text;
            StartEnumeration(_pendingText);
        }
    }

    private void StartEnumeration(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            HideDropdown();
            return;
        }

        // Cancel any ongoing enumeration
        _enumCts?.Cancel();
        _enumCts = new CancellationTokenSource();
        var token = _enumCts.Token;

        // Check if this is a path-based input
        bool isPath = text.Contains('\\') || text.Contains('/') ||
                      (text.Length >= 2 && text[1] == ':');

        if (!isPath)
        {
            // For non-paths, just hide the dropdown
            HideDropdown();
            return;
        }

        // Determine the directory prefix to enumerate
        string prefix = GetDirectoryPrefix(text);

        // Check cache first
        List<string>? cachedItems = null;
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(prefix.ToLowerInvariant(), out var cached))
            {
                cachedItems = cached;
            }
        }

        if (cachedItems != null)
        {
            // Filter cached results on UI thread
            FilterAndShowResults(text, cachedItems);
        }
        else
        {
            // Enumerate in background thread
            var capturedPrefix = prefix;
            var capturedText = text;

            _enumThread = new Thread(() => EnumerateDirectory(capturedPrefix, capturedText, token))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _enumThread.Start();
        }
    }

    private static string GetDirectoryPrefix(string text)
    {
        // Find the directory part of the path
        int lastSep = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));

        if (lastSep >= 0)
        {
            return text.Substring(0, lastSep + 1);
        }

        // Drive letter only (e.g., "C:")
        if (text.Length >= 2 && text[1] == ':')
        {
            return text.Substring(0, 2) + "\\";
        }

        return text;
    }

    private void EnumerateDirectory(string prefix, string originalText, CancellationToken token)
    {
        var items = new List<string>();
        int batchCount = 0;
        const int BatchSize = 20; // Show results incrementally

        try
        {
            string dirPath = prefix;
            if (!Directory.Exists(dirPath))
            {
                dirPath = Path.GetDirectoryName(prefix) ?? prefix;
            }

            if (Directory.Exists(dirPath))
            {
                // Enumerate directories first (faster, more useful for navigation)
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(dirPath))
                    {
                        if (token.IsCancellationRequested) return;
                        items.Add(dir);
                        batchCount++;

                        // Show first batch quickly for responsiveness
                        if (batchCount == BatchSize)
                        {
                            SendPartialResults(originalText, items, token);
                        }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }

                // Then files
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dirPath))
                    {
                        if (token.IsCancellationRequested) return;
                        items.Add(file);
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

            // Enumerate drives if at root
            if (string.IsNullOrEmpty(prefix) || prefix == "\\" || prefix.Length <= 3)
            {
                try
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (token.IsCancellationRequested) return;
                        if (drive.IsReady)
                        {
                            items.Add(drive.Name);
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception)
        {
            // Ignore enumeration errors
        }

        if (token.IsCancellationRequested) return;

        // Cache results
        lock (_cacheLock)
        {
            _cache[prefix.ToLowerInvariant()] = items;
        }

        // Update UI on main thread with final results
        if (_textBox.IsHandleCreated && !_textBox.IsDisposed)
        {
            _textBox.BeginInvoke(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    FilterAndShowResults(originalText, items);
                }
            });
        }
    }

    private void SendPartialResults(string text, List<string> items, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        if (!_textBox.IsHandleCreated || _textBox.IsDisposed) return;

        // Make a copy for thread safety
        var itemsCopy = items.ToList();

        _textBox.BeginInvoke(() =>
        {
            if (!token.IsCancellationRequested)
            {
                FilterAndShowResults(text, itemsCopy);
            }
        });
    }

    private void FilterAndShowResults(string text, List<string> items)
    {
        // Filter items that match the current text
        var filtered = items
            .Where(item => item.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();

        _currentItems = filtered;
        _selectedIndex = -1;

        if (filtered.Count == 0)
        {
            HideDropdown();
            return;
        }

        // Show dropdown (no auto-append - let user choose)
        ShowDropdown();
        _dropdown.SetItems(filtered);
    }

    private void DoAutoAppend(string typed, string match)
    {
        if (match.Length <= typed.Length) return;
        if (!_textBox.Focused) return;

        // Only auto-append if the match starts with what was typed
        if (!match.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return;

        _suppressTextChanged = true;
        _isAutoAppending = true;

        int caretPos = typed.Length;
        _textBox.Text = match;
        _textBox.SelectionStart = caretPos;
        _textBox.SelectionLength = match.Length - caretPos;

        _suppressTextChanged = false;
    }

    private void ShowDropdown()
    {
        if (_currentItems.Count == 0) return;

        int dropdownHeight = Math.Min(_currentItems.Count, 12) * 22 + 2;
        Point screenPoint;

        if (_dockAtBottom)
        {
            // Show dropdown above the textbox when docked at bottom
            screenPoint = _parent.PointToScreen(new Point(0, -dropdownHeight));
        }
        else
        {
            // Show dropdown below the textbox when docked at top
            screenPoint = _parent.PointToScreen(new Point(0, _parent.Height));
        }

        // Suppress lost focus events while showing the dropdown
        _suppressLostFocus = true;
        _dropdown.ShowAt(screenPoint, _parent.Width);

        // Restore focus to textbox and clear suppress flag after a brief delay
        Task.Delay(50).ContinueWith(_ =>
        {
            if (_textBox.IsHandleCreated && !_textBox.IsDisposed)
            {
                _textBox.BeginInvoke(() =>
                {
                    if (!_textBox.Focused)
                    {
                        _textBox.Focus();
                    }
                    _suppressLostFocus = false;
                });
            }
        });
    }

    private void HideDropdown()
    {
        _dropdown.Hide();
        _selectedIndex = -1;
        _isAutoAppending = false;
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    public void RefreshCache(string prefix)
    {
        lock (_cacheLock)
        {
            _cache.Remove(prefix.ToLowerInvariant());
        }
    }
}

/// <summary>
/// Smooth autocomplete dropdown with virtual rendering for performance
/// </summary>
public class AutoCompleteDropdown : Form
{
    private List<string> _items = new();
    private int _selectedIndex = -1;
    private int _hoveredIndex = -1;
    private int _scrollOffset;
    private const int ItemHeight = 22;
    private const int MaxVisibleItems = 12;
    private const int IconSize = 16;
    private readonly Dictionary<string, Image?> _iconCache = new();
    private Form? _owner;
    private readonly System.Windows.Forms.Timer _scrollTimer;
    private int _scrollDirection;
    private TextBox? _targetTextBox;

    public event EventHandler<string>? ItemClicked;
    public event EventHandler? DismissRequested;

    public AutoCompleteDropdown()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _scrollTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _scrollTimer.Tick += OnScrollTimerTick;
    }

    public void SetOwner(Form? owner)
    {
        _owner = owner;
    }

    public void SetTargetTextBox(TextBox? textBox)
    {
        _targetTextBox = textBox;
    }

    public void SetItems(List<string> items)
    {
        _items = items;
        _scrollOffset = 0;
        _hoveredIndex = -1;

        int visibleCount = Math.Min(items.Count, MaxVisibleItems);
        Height = visibleCount * ItemHeight + 2;

        // Pre-fetch icons for visible items in background
        Task.Run(() => PreloadIcons(items.Take(MaxVisibleItems + 5)));

        Invalidate();
    }

    public void SetSelectedIndex(int index)
    {
        if (index < 0) index = -1;
        if (index >= _items.Count) index = _items.Count - 1;

        _selectedIndex = index;

        // Ensure selected item is visible
        if (index >= 0)
        {
            if (index < _scrollOffset)
            {
                _scrollOffset = index;
            }
            else if (index >= _scrollOffset + MaxVisibleItems)
            {
                _scrollOffset = index - MaxVisibleItems + 1;
            }
        }

        Invalidate();
    }

    private void PreloadIcons(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (_iconCache.ContainsKey(path)) continue;

            try
            {
                var icon = GetPathIcon(path);
                lock (_iconCache)
                {
                    _iconCache[path] = icon;
                }
            }
            catch
            {
                lock (_iconCache)
                {
                    _iconCache[path] = null;
                }
            }
        }

        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(Invalidate);
        }
    }

    private static Image? GetPathIcon(string path)
    {
        try
        {
            bool isDir = Directory.Exists(path);
            if (isDir)
            {
                // Use shell icon for folders
                return GetShellIcon(path, true);
            }
            else if (File.Exists(path))
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                return icon?.ToBitmap();
            }
            else
            {
                // Get icon by extension
                return GetShellIcon(path, false);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

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

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private static Image? GetShellIcon(string path, bool isDirectory)
    {
        var shfi = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
        uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        IntPtr result = SHGetFileInfo(path, attrs, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

        if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
        {
            try
            {
                using var icon = Icon.FromHandle(shfi.hIcon);
                return icon.ToBitmap();
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }

        return null;
    }

    public void ShowAt(Point screenLocation, int width)
    {
        Location = screenLocation;
        Width = width;
        BackColor = IsDarkMode() ? Color.FromArgb(45, 45, 45) : Color.White;

        if (!Visible)
        {
            // Show without owner to avoid focus stealing issues
            // The WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW styles handle the rest
            Show();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool dark = IsDarkMode();

        // Background
        using var bgBrush = new SolidBrush(dark ? Color.FromArgb(45, 45, 45) : Color.White);
        g.FillRectangle(bgBrush, ClientRectangle);

        // Border
        using var borderPen = new Pen(dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200));
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        if (_items.Count == 0) return;

        using var textBrush = new SolidBrush(dark ? Color.White : Color.Black);
        using var selectedBrush = new SolidBrush(dark ? Color.FromArgb(0, 120, 215) : Color.FromArgb(0, 120, 215));
        using var selectedTextBrush = new SolidBrush(Color.White);
        using var hoverBrush = new SolidBrush(dark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(230, 230, 230));
        using var font = new Font("Segoe UI", 9f);

        int visibleCount = Math.Min(_items.Count - _scrollOffset, MaxVisibleItems);

        for (int i = 0; i < visibleCount; i++)
        {
            int itemIndex = _scrollOffset + i;
            if (itemIndex >= _items.Count) break;

            var item = _items[itemIndex];
            int y = 1 + i * ItemHeight;
            var itemRect = new Rectangle(1, y, Width - 2, ItemHeight);

            // Selection/hover highlight
            bool isSelected = itemIndex == _selectedIndex;
            bool isHovered = itemIndex == _hoveredIndex;

            if (isSelected)
            {
                g.FillRectangle(selectedBrush, itemRect);
            }
            else if (isHovered)
            {
                g.FillRectangle(hoverBrush, itemRect);
            }

            // Icon
            Image? icon = null;
            lock (_iconCache)
            {
                _iconCache.TryGetValue(item, out icon);
            }

            int textX = 6;
            if (icon != null)
            {
                int iconY = y + (ItemHeight - IconSize) / 2;
                g.DrawImage(icon, new Rectangle(6, iconY, IconSize, IconSize));
                textX = 6 + IconSize + 4;
            }

            // Text - show just the filename part bolded if it's a path
            var textRect = new Rectangle(textX, y, Width - textX - 6, ItemHeight);
            var sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisPath,
                FormatFlags = StringFormatFlags.NoWrap
            };

            g.DrawString(item, font, isSelected ? selectedTextBrush : textBrush, textRect, sf);
        }

        // Scroll indicators if needed
        if (_scrollOffset > 0)
        {
            // Up arrow indicator
            using var arrowBrush = new SolidBrush(dark ? Color.Gray : Color.DarkGray);
            var upArrow = new Point[] {
                new Point(Width / 2 - 5, 8),
                new Point(Width / 2 + 5, 8),
                new Point(Width / 2, 3)
            };
            g.FillPolygon(arrowBrush, upArrow);
        }

        if (_scrollOffset + MaxVisibleItems < _items.Count)
        {
            // Down arrow indicator
            using var arrowBrush = new SolidBrush(dark ? Color.Gray : Color.DarkGray);
            var downArrow = new Point[] {
                new Point(Width / 2 - 5, Height - 8),
                new Point(Width / 2 + 5, Height - 8),
                new Point(Width / 2, Height - 3)
            };
            g.FillPolygon(arrowBrush, downArrow);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int index = _scrollOffset + (e.Y - 1) / ItemHeight;
        if (index >= 0 && index < _items.Count && index != _hoveredIndex)
        {
            _hoveredIndex = index;
            Invalidate();
        }

        // Auto-scroll when near edges
        if (e.Y < 20 && _scrollOffset > 0)
        {
            _scrollDirection = -1;
            if (!_scrollTimer.Enabled) _scrollTimer.Start();
        }
        else if (e.Y > Height - 20 && _scrollOffset + MaxVisibleItems < _items.Count)
        {
            _scrollDirection = 1;
            if (!_scrollTimer.Enabled) _scrollTimer.Start();
        }
        else
        {
            _scrollTimer.Stop();
        }
    }

    private void OnScrollTimerTick(object? sender, EventArgs e)
    {
        _scrollOffset = Math.Max(0, Math.Min(_items.Count - MaxVisibleItems, _scrollOffset + _scrollDirection));
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _scrollTimer.Stop();
        if (_hoveredIndex != -1)
        {
            _hoveredIndex = -1;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        int index = _scrollOffset + (e.Y - 1) / ItemHeight;
        if (index >= 0 && index < _items.Count)
        {
            ItemClicked?.Invoke(this, _items[index]);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        int delta = e.Delta > 0 ? -3 : 3;
        _scrollOffset = Math.Max(0, Math.Min(_items.Count - MaxVisibleItems, _scrollOffset + delta));
        Invalidate();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW - don't show in taskbar/alt-tab
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE - never steal focus
            cp.ClassStyle |= 0x0002;  // CS_DROPSHADOW
            return cp;
        }
    }

    // Prevent activation when clicked
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;
    private const int WM_NCACTIVATE = 0x0086;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scrollTimer.Dispose();
            foreach (var icon in _iconCache.Values)
            {
                icon?.Dispose();
            }
            _iconCache.Clear();
        }
        base.Dispose(disposing);
    }
}

#endregion

/// <summary>
/// Custom dropdown popup for history - uses a borderless Form for reliable display
/// </summary>
public class HistoryDropdown : Form
{
    private readonly List<(string Path, Image? Icon)> _items = new();
    private int _hoveredIndex = -1;
    private const int ItemHeight = 24;
    private const int IconSize = 16;
    private const int MaxVisibleItems = 10;
    private bool _justShown;

    public event EventHandler<string>? ItemSelected;

    public HistoryDropdown()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = IsDarkMode() ? Color.FromArgb(45, 45, 45) : Color.White;
    }

    public void SetItems(List<(string Path, Image? Icon)> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _hoveredIndex = -1;

        int visibleCount = Math.Min(items.Count, MaxVisibleItems);
        if (visibleCount == 0) visibleCount = 1; // Show at least empty state
        Height = visibleCount * ItemHeight + 2; // +2 for border
        BackColor = IsDarkMode() ? Color.FromArgb(45, 45, 45) : Color.White;
        Invalidate();
    }

    public void SetWidth(int width)
    {
        Width = width;
    }

    public void ShowAt(Point screenLocation)
    {
        if (Visible)
        {
            Hide();
            return;
        }

        Location = screenLocation;
        _justShown = true;
        Show();

        // Use a timer to reset the flag after the form is shown
        var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (s, e) =>
        {
            _justShown = false;
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (!_justShown)
        {
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        bool dark = IsDarkMode();

        // Background
        using var bgBrush = new SolidBrush(dark ? Color.FromArgb(45, 45, 45) : Color.White);
        g.FillRectangle(bgBrush, ClientRectangle);

        // Border
        using var borderPen = new Pen(dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200));
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        // Items
        using var textBrush = new SolidBrush(dark ? Color.White : Color.Black);
        using var hoverBrush = new SolidBrush(dark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(230, 230, 230));
        using var font = new Font("Segoe UI", 9f);

        if (_items.Count == 0)
        {
            // Empty state
            var emptyRect = new Rectangle(1, 1, Width - 2, ItemHeight);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var dimBrush = new SolidBrush(dark ? Color.Gray : Color.DarkGray);
            g.DrawString("No history", font, dimBrush, emptyRect, sf);
            return;
        }

        for (int i = 0; i < _items.Count && i < MaxVisibleItems; i++)
        {
            var item = _items[i];
            int y = 1 + i * ItemHeight;
            var itemRect = new Rectangle(1, y, Width - 2, ItemHeight);

            // Highlight on hover
            if (i == _hoveredIndex)
            {
                g.FillRectangle(hoverBrush, itemRect);
            }

            // Icon
            if (item.Icon != null)
            {
                int iconY = y + (ItemHeight - IconSize) / 2;
                g.DrawImage(item.Icon, new Rectangle(6, iconY, IconSize, IconSize));
            }

            // Text
            var textRect = new Rectangle(6 + IconSize + 6, y, Width - IconSize - 18, ItemHeight);
            var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisPath };
            g.DrawString(item.Path, font, textBrush, textRect, sf);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = (e.Y - 1) / ItemHeight;
        if (index >= 0 && index < _items.Count && index < MaxVisibleItems && index != _hoveredIndex)
        {
            _hoveredIndex = index;
            Invalidate();
        }
        else if ((index < 0 || index >= _items.Count) && _hoveredIndex != -1)
        {
            _hoveredIndex = -1;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredIndex != -1)
        {
            _hoveredIndex = -1;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int index = (e.Y - 1) / ItemHeight;
        if (index >= 0 && index < _items.Count && index < MaxVisibleItems)
        {
            ItemSelected?.Invoke(this, _items[index].Path);
            Hide();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    protected override bool ShowWithoutActivation => true;

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

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW - don't show in taskbar/alt-tab
            cp.ClassStyle |= 0x0002; // CS_DROPSHADOW
            return cp;
        }
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
    private readonly Button _dropdownButton;
    private readonly HistoryDropdown _historyDropdown;
    private AutoCompleteController _autoComplete;
    private readonly List<string> _history = new();
    private readonly Dictionary<string, Image?> _historyIcons = new();
    private CancellationTokenSource? _iconCts;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private const string HistoryRegPath = @"Software\AddressBar\TypedPaths";
    private const int MaxHistoryItems = 25;

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
            Text = "Go",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            BackColor = GetButtonBackColor(),
            ForeColor = GetSystemForeColor(),
            Cursor = Cursors.Hand,
            Width = LogicalToDeviceUnits(40)
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
            BackColor = _settings.IconPosition == IconPosition.Inside ? GetTextBoxBackColor() : GetSystemBackColor()
        };

        // Dropdown button - inside the container, no border, seamless with textbox
        _dropdownButton = new Button
        {
            Text = "▼",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7f),
            BackColor = GetTextBoxBackColor(),
            ForeColor = GetSystemForeColor(),
            Cursor = Cursors.Hand,
            Width = LogicalToDeviceUnits(20),
            TabStop = false
        };
        _dropdownButton.FlatAppearance.BorderSize = 0;
        _dropdownButton.FlatAppearance.BorderColor = GetTextBoxBackColor(); // Match background to hide any border
        _dropdownButton.FlatAppearance.MouseOverBackColor = GetDropdownHoverColor();
        _dropdownButton.FlatAppearance.MouseDownBackColor = GetDropdownHoverColor();
        _dropdownButton.Click += DropdownButton_Click;
        _dropdownButton.GotFocus += (s, e) => _addressBox.Focus(); // Redirect focus to textbox

        // History dropdown popup
        _historyDropdown = new HistoryDropdown();
        _historyDropdown.ItemSelected += HistoryDropdown_ItemSelected;

        // Explorer-style autocomplete (initialized in Load event after handle is created)
        _autoComplete = null!;

        // Add icon to the appropriate parent based on settings
        if (_settings.IconPosition == IconPosition.Inside)
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
        _iconBox.Image = GetDefaultAppIcon();
        LoadHistory();

        // Initialize autocomplete after form handle is created (timer needs message loop)
        _autoComplete = new AutoCompleteController(_addressBox, _addressBoxContainer);
        _autoComplete.DockAtBottom = _settings.DockPosition == DockPosition.Bottom;

        // Preload common directories in background for instant autocomplete
        _autoComplete.PreloadCommonPaths();
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
        _autoComplete.Dispose();
    }

    private void LayoutControls()
    {
        int padding = LogicalToDeviceUnits(6);
        int iconSize = LogicalToDeviceUnits(16);
        int iconPadding = LogicalToDeviceUnits(4);
        int labelWidth = LogicalToDeviceUnits(50);
        int buttonWidth = _goButton.Width;
        int settingsWidth = _settingsButton.Width;
        int dropdownWidth = _dropdownButton.Width;

        int containerHeight = _addressBox.PreferredHeight + 6;
        int controlsY = (Height - containerHeight) / 2;

        _iconBox.Size = new Size(iconSize, iconSize);

        if (_settings.IconPosition == IconPosition.Left)
        {
            // Icon on the left, then label, then address box
            _iconBox.Location = new Point(padding, (Height - iconSize) / 2);
            _addressLabel.Location = new Point(padding + iconSize + iconPadding, (Height - _addressLabel.Height) / 2);

            int textBoxLeft = padding + iconSize + iconPadding + labelWidth;
            int textBoxWidth = Width - textBoxLeft - buttonWidth - settingsWidth - padding * 3;

            _addressBoxContainer.Location = new Point(textBoxLeft, controlsY);
            _addressBoxContainer.Size = new Size(textBoxWidth, containerHeight);

            // Dropdown button on the right inside container
            _dropdownButton.Height = containerHeight - 2;
            _dropdownButton.Location = new Point(textBoxWidth - dropdownWidth - 1, 0);

            // Textbox fills the container (no icon inside), leaving room for dropdown
            int textBoxY = (containerHeight - _addressBox.PreferredHeight - 2) / 2;
            _addressBox.Location = new Point(4, textBoxY);
            _addressBox.Width = textBoxWidth - dropdownWidth - 10;
        }
        else
        {
            // Label on the left, then address box with icon inside
            _addressLabel.Location = new Point(padding, (Height - _addressLabel.Height) / 2);

            int textBoxLeft = padding + labelWidth;
            int textBoxWidth = Width - textBoxLeft - buttonWidth - settingsWidth - padding * 3;

            _addressBoxContainer.Location = new Point(textBoxLeft, controlsY);
            _addressBoxContainer.Size = new Size(textBoxWidth, containerHeight);

            // Icon inside container, on the left
            _iconBox.Location = new Point(4, (containerHeight - iconSize - 2) / 2);

            // Dropdown button on the right inside container
            _dropdownButton.Height = containerHeight - 2;
            _dropdownButton.Location = new Point(textBoxWidth - dropdownWidth - 1, 0);

            // Textbox after the icon inside container, leaving room for dropdown
            int textBoxY = (containerHeight - _addressBox.PreferredHeight - 2) / 2;
            int textBoxInternalLeft = iconSize + iconPadding + 4;
            _addressBox.Location = new Point(textBoxInternalLeft, textBoxY);
            _addressBox.Width = textBoxWidth - textBoxInternalLeft - dropdownWidth - 6;
        }

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
        // Let autocomplete handle keys first - if it handled the event, skip our handler
        if (e.Handled) return;

        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            // Accept any selected text from auto-append
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

    private void Navigate()
    {
        string input = _addressBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        try
        {
            // 1. Already a full URL with scheme
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                Process.Start(new ProcessStartInfo(input) { UseShellExecute = true });
                SaveToHistory(input);
                return;
            }

            // 2. Looks like a URL (has dot, no spaces, no backslash) - add https://
            // Allow forward slashes for paths like github.com/user/repo
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

            // 3. Local directory
            string expandedPath = Environment.ExpandEnvironmentVariables(input);
            if (Directory.Exists(expandedPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{expandedPath}\"") { UseShellExecute = true });
                SaveToHistory(input);
                return;
            }

            // 4. Local file
            if (File.Exists(expandedPath))
            {
                Process.Start(new ProcessStartInfo(expandedPath) { UseShellExecute = true });
                SaveToHistory(input);
                return;
            }

            // 5. Fall back to shell execute (commands, etc.)
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

    private static Color GetDropdownHoverColor() =>
        IsDarkMode() ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 220, 220);

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
        _iconBox.BackColor = _settings.IconPosition == IconPosition.Inside ? GetTextBoxBackColor() : GetSystemBackColor();
        _dropdownButton.BackColor = GetTextBoxBackColor();
        _dropdownButton.ForeColor = GetSystemForeColor();
        _dropdownButton.FlatAppearance.BorderColor = GetTextBoxBackColor();
        _dropdownButton.FlatAppearance.MouseOverBackColor = GetDropdownHoverColor();
        _dropdownButton.FlatAppearance.MouseDownBackColor = GetDropdownHoverColor();
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
            var oldImage = _iconBox.Image;
            _iconBox.Image = GetDefaultAppIcon();
            oldImage?.Dispose();
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
            return await GetFaviconAsync(uri, token) ?? GetDefaultAppIcon();
        }

        // 3. Looks like a URL without scheme (e.g., "google.com" or "github.com/user/repo")
        // Allow forward slashes for paths like github.com/user/repo
        if (input.Contains('.') && !input.Contains('\\') && !input.Contains(' '))
        {
            if (Uri.TryCreate("https://" + input, UriKind.Absolute, out uri))
            {
                return await GetFaviconAsync(uri, token) ?? GetDefaultAppIcon();
            }
        }

        // 4. Try to get icon by file extension if it looks like a local path (backslash indicates Windows path)
        if (input.Contains('\\'))
        {
            return GetFileIcon(expanded, isDirectory: false);
        }

        // 5. Try to find executable in PATH (for commands like "notepad", "explorer")
        var exePath = FindExecutableInPath(input);
        if (exePath != null)
        {
            return GetFileIcon(exePath, isDirectory: false);
        }

        return null;
    }

    /// <summary>
    /// Gets the default app icon (addressbar.ico) as a fallback
    /// </summary>
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

    /// <summary>
    /// Searches for an executable in the system PATH
    /// </summary>
    private static string? FindExecutableInPath(string name)
    {
        // Add .exe if no extension
        string fileName = name;
        if (!Path.HasExtension(fileName))
        {
            fileName += ".exe";
        }

        // Check if it's already a valid path
        if (File.Exists(fileName))
        {
            return Path.GetFullPath(fileName);
        }

        // Search in PATH environment variable
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

        // Also check Windows directory
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var winPath = Path.Combine(winDir, fileName);
        if (File.Exists(winPath))
            return winPath;

        var sys32Path = Path.Combine(winDir, "System32", fileName);
        if (File.Exists(sys32Path))
            return sys32Path;

        return null;
    }

    /// <summary>
    /// Gets the shell icon for a local file or folder using SHGetFileInfo
    /// </summary>
    private static Image? GetFileIcon(string path, bool isDirectory)
    {
        try
        {
            // Ensure we have an absolute path
            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(path);
            }

            bool exists = isDirectory ? Directory.Exists(path) : File.Exists(path);

            // For existing files, use Icon.ExtractAssociatedIcon (more reliable)
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
                catch { /* Fall through to SHGetFileInfo */ }
            }

            // For directories or if ExtractAssociatedIcon failed, use SHGetFileInfo
            var shfi = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON;
            uint attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

            if (!exists)
            {
                flags |= SHGFI_USEFILEATTRIBUTES;
            }

            IntPtr result = SHGetFileInfo(path, attributes, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

            if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
            {
                try
                {
                    using var icon = Icon.FromHandle(shfi.hIcon);
                    return icon.ToBitmap();
                }
                finally
                {
                    DestroyIcon(shfi.hIcon);
                }
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

                // Use the final URL after redirects to get the correct host
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
        // Supports both quoted and unquoted attribute values
        var patterns = new[]
        {
            // rel="icon" or rel=icon followed by href
            @"<link[^>]*rel\s*=\s*[""']?(?:shortcut\s+)?icon[""']?[^>]*href\s*=\s*[""']?([^""'\s>]+)[""']?",
            // href followed by rel="icon" or rel=icon
            @"<link[^>]*href\s*=\s*[""']?([^""'\s>]+)[""']?[^>]*rel\s*=\s*[""']?(?:shortcut\s+)?icon[""']?",
            // apple-touch-icon
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
            // Skip SVG files - they require a separate rendering library
            if (url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            using var response = await _httpClient.SendAsync(request, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength == 0)
                return null;

            // Skip SVG content type
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType?.Contains("svg") == true)
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

    #region History Management

    private void LoadHistory()
    {
        _history.Clear();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(HistoryRegPath);
            if (key != null)
            {
                // Get all value names and sort them to maintain order
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

        // Load icons asynchronously for history items
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

        // Remove if exists (to move to top)
        _history.Remove(path);
        _history.Insert(0, path);

        // Trim to max items
        while (_history.Count > MaxHistoryItems)
        {
            _history.RemoveAt(_history.Count - 1);
        }

        // Save to registry
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(HistoryRegPath);
            if (key != null)
            {
                // Clear existing values
                foreach (var name in key.GetValueNames())
                {
                    key.DeleteValue(name, false);
                }

                // Write new values
                for (int i = 0; i < _history.Count; i++)
                {
                    key.SetValue($"url{i + 1}", _history[i]);
                }
            }
        }
        catch { }

        // Fetch icon for new entry
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
        // Build items with icons
        var items = _history.Select(path =>
        {
            _historyIcons.TryGetValue(path, out var icon);
            return (path, icon);
        }).ToList();

        _historyDropdown.SetItems(items);
        _historyDropdown.SetWidth(_addressBoxContainer.Width);

        // Show dropdown below the address box container
        var screenPoint = _addressBoxContainer.PointToScreen(new Point(0, _addressBoxContainer.Height));

        // For AppBar at top of screen, show below; for bottom, show above
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
