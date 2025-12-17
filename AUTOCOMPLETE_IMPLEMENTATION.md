# Explorer-Style Autocomplete Implementation Guide

This document describes the complete implementation of a Windows Explorer-style autocomplete system for a WinForms address bar application. The goal is to replicate the smooth, non-intrusive autocomplete behavior of Windows Explorer.

## Overview

The system consists of two main classes:
1. **AutoCompleteController** - Manages the autocomplete logic, caching, background enumeration, and keyboard handling
2. **AutoCompleteDropdown** - A custom borderless Form that renders the dropdown list with icons

## Key Design Principles

### 1. Never Steal Focus
The dropdown must NEVER steal focus from the textbox. This is critical for a smooth typing experience. Achieved through:
- `WS_EX_NOACTIVATE` extended window style (0x08000000)
- `WS_EX_TOOLWINDOW` extended window style (0x00000080)
- Override `ShowWithoutActivation => true`
- Handle `WM_MOUSEACTIVATE` message and return `MA_NOACTIVATE`
- Call `Show()` without an owner form (using `Show()` instead of `Show(owner)`)

### 2. Background Threading
Directory enumeration happens on a background thread to avoid UI freezes:
- Use a dedicated `Thread` with `IsBackground = true` and `ThreadPriority.BelowNormal`
- Use `CancellationToken` to abort enumeration when user types more
- Marshal results back to UI thread via `BeginInvoke()`
- Send partial results after first 20 items for perceived responsiveness

### 3. Aggressive Caching
Cache directory contents by path to avoid repeated disk access:
- Dictionary keyed by lowercase directory path
- Preload common directories on startup (C:\, user profile, Desktop, Documents)
- Cache persists for the session

### 4. Debouncing
Don't enumerate on every keystroke:
- Use a 150ms `System.Windows.Forms.Timer`
- Reset timer on each keystroke
- Only start enumeration when timer fires

## Implementation Details

### AutoCompleteController Class

```csharp
public class AutoCompleteController : IDisposable
{
    // Core components
    private readonly TextBox _textBox;
    private readonly Control _parent;  // Container for positioning
    private readonly AutoCompleteDropdown _dropdown;

    // Caching
    private readonly Dictionary<string, List<string>> _cache = new();
    private readonly object _cacheLock = new();

    // Debouncing
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private const int DebounceMs = 150;

    // Background enumeration
    private CancellationTokenSource? _enumCts;
    private Thread? _enumThread;

    // State
    private string _pendingText = "";
    private bool _suppressTextChanged;  // Prevent recursive updates
    private bool _suppressLostFocus;    // Prevent hiding during Show()
    private int _selectedIndex = -1;
    private List<string> _currentItems = new();
    private bool _dockAtBottom;  // For positioning dropdown above/below
}
```

### Key Methods

#### Preloading (called on form Load)
```csharp
public void PreloadCommonPaths()
{
    Task.Run(() =>
    {
        PreloadDirectory("C:\\");
        PreloadDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\");
        PreloadDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\");
        PreloadDirectory(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\");
    });
}

private void PreloadDirectory(string dirPath)
{
    // Check cache first, enumerate directories then files, store in cache
}
```

#### Text Changed Handling
```csharp
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
```

#### Enumeration Logic
```csharp
private void StartEnumeration(string text)
{
    if (string.IsNullOrEmpty(text)) { HideDropdown(); return; }

    // Cancel any ongoing enumeration
    _enumCts?.Cancel();
    _enumCts = new CancellationTokenSource();

    // Check if path-like input
    bool isPath = text.Contains('\\') || text.Contains('/') ||
                  (text.Length >= 2 && text[1] == ':');
    if (!isPath) { HideDropdown(); return; }

    // Get directory prefix (e.g., "C:\Users\" from "C:\Users\foo")
    string prefix = GetDirectoryPrefix(text);

    // Check cache first
    if (cache has prefix)
    {
        FilterAndShowResults(text, cachedItems);
    }
    else
    {
        // Start background thread
        _enumThread = new Thread(() => EnumerateDirectory(prefix, text, token))
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        _enumThread.Start();
    }
}
```

#### Directory Prefix Extraction
```csharp
private static string GetDirectoryPrefix(string text)
{
    int lastSep = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
    if (lastSep >= 0)
        return text.Substring(0, lastSep + 1);

    // Drive letter only (e.g., "C:")
    if (text.Length >= 2 && text[1] == ':')
        return text.Substring(0, 2) + "\\";

    return text;
}
```

#### Background Enumeration with Partial Results
```csharp
private void EnumerateDirectory(string prefix, string originalText, CancellationToken token)
{
    var items = new List<string>();
    int batchCount = 0;
    const int BatchSize = 20;

    // Enumerate directories first (faster, more useful for navigation)
    foreach (var dir in Directory.EnumerateDirectories(dirPath))
    {
        if (token.IsCancellationRequested) return;
        items.Add(dir);
        batchCount++;

        // Send partial results for responsiveness
        if (batchCount == BatchSize)
            SendPartialResults(originalText, items, token);
    }

    // Then enumerate files
    foreach (var file in Directory.EnumerateFiles(dirPath))
    {
        if (token.IsCancellationRequested) return;
        items.Add(file);
    }

    // Cache and show final results
    lock (_cacheLock) { _cache[prefix.ToLowerInvariant()] = items; }

    _textBox.BeginInvoke(() => FilterAndShowResults(originalText, items));
}
```

#### Filtering and Display
```csharp
private void FilterAndShowResults(string text, List<string> items)
{
    var filtered = items
        .Where(item => item.StartsWith(text, StringComparison.OrdinalIgnoreCase))
        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
        .Take(15)
        .ToList();

    _currentItems = filtered;
    _selectedIndex = -1;

    if (filtered.Count == 0) { HideDropdown(); return; }

    ShowDropdown();
    _dropdown.SetItems(filtered);
}
```

#### Keyboard Navigation
```csharp
private void OnKeyDown(object? sender, KeyEventArgs e)
{
    if (!_dropdown.Visible)
    {
        // Alt+Down opens dropdown if items available
        if (e.KeyCode == Keys.Down && e.Alt && _currentItems.Count > 0)
        {
            ShowDropdown();
            e.Handled = true;
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
```

#### Focus Handling (Critical!)
```csharp
private void OnLostFocus(object? sender, EventArgs e)
{
    // Ignore if we're in the middle of showing the dropdown
    if (_suppressLostFocus) return;

    // Check if mouse is over the dropdown - if so, don't hide
    if (_dropdown.Visible && _dropdown.Bounds.Contains(Control.MousePosition))
        return;

    // Small delay to allow dropdown clicks to be processed
    Task.Delay(150).ContinueWith(_ =>
    {
        if (_textBox.IsHandleCreated && !_textBox.IsDisposed)
        {
            _textBox.BeginInvoke(() =>
            {
                if (!_textBox.Focused && !_suppressLostFocus)
                {
                    if (!_dropdown.Visible || !_dropdown.Bounds.Contains(Control.MousePosition))
                    {
                        HideDropdown();
                    }
                }
            });
        }
    });
}
```

#### Showing Dropdown (with focus protection)
```csharp
private void ShowDropdown()
{
    if (_currentItems.Count == 0) return;

    int dropdownHeight = Math.Min(_currentItems.Count, 12) * 22 + 2;
    Point screenPoint;

    if (_dockAtBottom)
        screenPoint = _parent.PointToScreen(new Point(0, -dropdownHeight));
    else
        screenPoint = _parent.PointToScreen(new Point(0, _parent.Height));

    // CRITICAL: Suppress lost focus events while showing
    _suppressLostFocus = true;
    _dropdown.ShowAt(screenPoint, _parent.Width);

    // Restore focus and clear suppress flag after brief delay
    Task.Delay(50).ContinueWith(_ =>
    {
        if (_textBox.IsHandleCreated && !_textBox.IsDisposed)
        {
            _textBox.BeginInvoke(() =>
            {
                if (!_textBox.Focused)
                    _textBox.Focus();
                _suppressLostFocus = false;
            });
        }
    });
}
```

#### Accepting Completion
```csharp
private void AcceptCompletion(string item)
{
    _suppressTextChanged = true;
    _textBox.Text = item;
    _textBox.SelectionStart = item.Length;
    _suppressTextChanged = false;
    HideDropdown();

    // If it's a directory, auto-append backslash and show contents
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
```

### AutoCompleteDropdown Class

```csharp
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
    private readonly System.Windows.Forms.Timer _scrollTimer;
    private int _scrollDirection;

    public event EventHandler<string>? ItemClicked;
}
```

#### Constructor Setup
```csharp
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
```

#### Critical: Window Styles to Prevent Focus Stealing
```csharp
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

private const int WM_MOUSEACTIVATE = 0x0021;
private const int MA_NOACTIVATE = 3;

protected override void WndProc(ref Message m)
{
    if (m.Msg == WM_MOUSEACTIVATE)
    {
        m.Result = (IntPtr)MA_NOACTIVATE;
        return;
    }
    base.WndProc(ref m);
}
```

#### Show Without Owner (prevents focus issues)
```csharp
public void ShowAt(Point screenLocation, int width)
{
    Location = screenLocation;
    Width = width;
    BackColor = IsDarkMode() ? Color.FromArgb(45, 45, 45) : Color.White;

    if (!Visible)
    {
        // CRITICAL: Show without owner to avoid focus stealing issues
        Show();  // NOT Show(owner)
    }
}
```

#### Icon Loading with Shell API
```csharp
[DllImport("shell32.dll", CharSet = CharSet.Auto)]
private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
    ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

[DllImport("user32.dll")]
private static extern bool DestroyIcon(IntPtr hIcon);

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
```

#### Custom Painting with Dark Mode Support
```csharp
protected override void OnPaint(PaintEventArgs e)
{
    var g = e.Graphics;
    g.SmoothingMode = SmoothingMode.HighSpeed;
    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

    bool dark = IsDarkMode();

    // Background
    using var bgBrush = new SolidBrush(dark ? Color.FromArgb(45, 45, 45) : Color.White);
    g.FillRectangle(bgBrush, ClientRectangle);

    // Border
    using var borderPen = new Pen(dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200));
    g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

    // Items with icons, selection highlight, hover effect
    // ... (see full implementation)
}

private static bool IsDarkMode()
{
    using var key = Registry.CurrentUser.OpenSubKey(
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
    var value = key?.GetValue("AppsUseLightTheme");
    return value is int i && i == 0;
}
```

## Integration with AddressBarForm

```csharp
private AutoCompleteController _autoComplete;

// In constructor:
_autoComplete = null!;  // Will be initialized in Load event

// In Load event (IMPORTANT: must be after form handle is created):
private void AddressBarForm_Load(object? sender, EventArgs e)
{
    // ... other initialization ...

    // Initialize autocomplete after form handle is created (timer needs message loop)
    _autoComplete = new AutoCompleteController(_addressBox, _addressBoxContainer);
    _autoComplete.DockAtBottom = _settings.DockPosition == DockPosition.Bottom;

    // Preload common directories in background for instant autocomplete
    _autoComplete.PreloadCommonPaths();
}

// In Cleanup:
public void Cleanup()
{
    // ... other cleanup ...
    _autoComplete.Dispose();
}
```

## Common Pitfalls and Solutions

### 1. Dropdown Flashes and Disappears
**Cause**: LostFocus event fires when showing dropdown, even with WS_EX_NOACTIVATE
**Solution**: Use `_suppressLostFocus` flag, set before showing, clear after short delay

### 2. Dropdown Steals Focus When Clicked
**Cause**: Default Form behavior activates on click
**Solution**: Handle WM_MOUSEACTIVATE, return MA_NOACTIVATE, use Show() without owner

### 3. Initial Freeze on C:\
**Cause**: Synchronous directory enumeration on first access
**Solution**: Preload common directories on startup, use background threading, send partial results

### 4. Text Gets Corrupted
**Cause**: Event handlers modifying text recursively
**Solution**: Use `_suppressTextChanged` flag when programmatically modifying text

### 5. Dropdown Won't Close
**Cause**: Complex focus/activation state machine
**Solution**: Check `Control.MousePosition` against dropdown bounds, use delays for click processing

## What NOT to Implement (Removed Features)

Originally implemented but removed at user request:
- **Auto-append/inline completion**: The feature that auto-fills the first match with selected text
- This was removed because it felt aggressive - user preferred to manually choose from dropdown

The code for auto-append involved:
- Setting `_isAutoAppending` flag
- Manipulating `_textBox.SelectionStart` and `_textBox.SelectionLength`
- Special handling in `OnKeyPress` to consume typed characters matching the appended text

This was all removed to let the dropdown "just chill" without auto-selecting anything.
