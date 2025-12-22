namespace AddressBar.AutoComplete;

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
    private bool _suppressTextChanged;
    private bool _suppressLostFocus;
    private int _selectedIndex = -1;
    private List<string> _currentItems = new();
    private const int DebounceMs = 150;
    private const int ItemHeight = 22;
    private bool _dockAtBottom;

    public event EventHandler<string>? ItemSelected;

    public bool DockAtBottom
    {
        get => _dockAtBottom;
        set => _dockAtBottom = value;
    }

    public void PreloadCommonPaths()
    {
        Task.Run(() =>
        {
            try
            {
                PreloadDirectory("C:\\");

                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    PreloadDirectory(userProfile + "\\");
                }

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
            if (_cache.ContainsKey(key)) return;
        }

        var items = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(dirPath))
            {
                items.Add(dir);
            }
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
        _dropdown.SetOwner(parent.FindForm());
        _dropdown.SetTargetTextBox(textBox);

        _debounceTimer = new System.Windows.Forms.Timer { Interval = DebounceMs };
        _debounceTimer.Tick += OnDebounceTimerTick;

        _textBox.TextChanged += OnTextChanged;
        _textBox.KeyDown += OnKeyDown;
        _textBox.KeyPress += OnKeyPress;
        _textBox.LostFocus += OnLostFocus;
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
        // No special key handling needed
    }

    private void OnLostFocus(object? sender, EventArgs e)
    {
        if (_suppressLostFocus) return;

        if (_dropdown.Visible && _dropdown.Bounds.Contains(Control.MousePosition))
        {
            return;
        }

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
        HideDropdown();
        ItemSelected?.Invoke(this, item);

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

        _enumCts?.Cancel();
        _enumCts = new CancellationTokenSource();
        var token = _enumCts.Token;

        // Extract path portion if there's a command prefix (e.g., "notepad C:\github\")
        string pathPortion = text;
        string commandPrefix = "";

        if (text.Contains(' '))
        {
            int spaceIndex = text.IndexOf(' ');
            string possibleCommand = text.Substring(0, spaceIndex);
            string afterSpace = text.Substring(spaceIndex + 1);

            // Check if the part after the space looks like a path
            if (afterSpace.Length > 0 &&
                (afterSpace.Contains('\\') || afterSpace.Contains('/') ||
                 (afterSpace.Length >= 2 && afterSpace[1] == ':')))
            {
                commandPrefix = text.Substring(0, spaceIndex + 1);
                pathPortion = afterSpace;
            }
        }

        bool isPath = pathPortion.Contains('\\') || pathPortion.Contains('/') ||
                      (pathPortion.Length >= 2 && pathPortion[1] == ':');

        if (!isPath)
        {
            HideDropdown();
            return;
        }

        string prefix = GetDirectoryPrefix(pathPortion);

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
            FilterAndShowResults(text, cachedItems, commandPrefix, pathPortion);
        }
        else
        {
            var capturedPrefix = prefix;
            var capturedText = text;
            var capturedCommandPrefix = commandPrefix;
            var capturedPathPortion = pathPortion;

            _enumThread = new Thread(() => EnumerateDirectory(capturedPrefix, capturedText, capturedCommandPrefix, capturedPathPortion, token))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _enumThread.Start();
        }
    }

    private static string GetDirectoryPrefix(string text)
    {
        int lastSep = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));

        if (lastSep >= 0)
        {
            return text.Substring(0, lastSep + 1);
        }

        if (text.Length >= 2 && text[1] == ':')
        {
            return text.Substring(0, 2) + "\\";
        }

        return text;
    }

    private void EnumerateDirectory(string prefix, string originalText, string commandPrefix, string pathPortion, CancellationToken token)
    {
        var items = new List<string>();
        int batchCount = 0;
        const int BatchSize = 20;

        try
        {
            string dirPath = prefix;
            if (!Directory.Exists(dirPath))
            {
                dirPath = Path.GetDirectoryName(prefix) ?? prefix;
            }

            if (Directory.Exists(dirPath))
            {
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(dirPath))
                    {
                        if (token.IsCancellationRequested) return;
                        items.Add(dir);
                        batchCount++;

                        if (batchCount == BatchSize)
                        {
                            SendPartialResults(originalText, items, commandPrefix, pathPortion, token);
                        }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }

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

        lock (_cacheLock)
        {
            _cache[prefix.ToLowerInvariant()] = items;
        }

        if (_textBox.IsHandleCreated && !_textBox.IsDisposed)
        {
            _textBox.BeginInvoke(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    FilterAndShowResults(originalText, items, commandPrefix, pathPortion);
                }
            });
        }
    }

    private void SendPartialResults(string text, List<string> items, string commandPrefix, string pathPortion, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        if (!_textBox.IsHandleCreated || _textBox.IsDisposed) return;

        var itemsCopy = items.ToList();

        _textBox.BeginInvoke(() =>
        {
            if (!token.IsCancellationRequested)
            {
                FilterAndShowResults(text, itemsCopy, commandPrefix, pathPortion);
            }
        });
    }

    private void FilterAndShowResults(string text, List<string> items, string commandPrefix, string pathPortion)
    {
        // Filter based on the path portion only
        var filtered = items
            .Where(item => item.StartsWith(pathPortion, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();

        // If there's a command prefix, prepend it to the filtered items
        if (!string.IsNullOrEmpty(commandPrefix))
        {
            _currentItems = filtered.Select(item => commandPrefix + item).ToList();
        }
        else
        {
            _currentItems = filtered;
        }

        _selectedIndex = -1;

        if (_currentItems.Count == 0)
        {
            HideDropdown();
            return;
        }

        ShowDropdown();
        _dropdown.SetItems(_currentItems);
    }

    private void ShowDropdown()
    {
        if (_currentItems.Count == 0) return;

        var form = _parent.FindForm();
        if (form == null) return;

        int dropdownHeight = Math.Min(_currentItems.Count, 12) * ItemHeight + 2;
        int x = _parent.PointToScreen(new Point(0, 0)).X;
        Point screenPoint;

        if (_dockAtBottom)
        {
            screenPoint = new Point(x, form.Bounds.Top - dropdownHeight);
        }
        else
        {
            screenPoint = new Point(x, form.Bounds.Bottom);
        }

        _suppressLostFocus = true;
        _dropdown.ShowAt(screenPoint, _parent.Width);

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
