using System.Runtime.InteropServices;

namespace AddressBar.AutoComplete;

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

        Task.Run(() => PreloadIcons(items.Take(MaxVisibleItems + 5)));

        Invalidate();
    }

    public void SetSelectedIndex(int index)
    {
        if (index < 0) index = -1;
        if (index >= _items.Count) index = _items.Count - 1;

        _selectedIndex = index;

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
                return GetShellIcon(path, true);
            }
            else if (File.Exists(path))
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                return icon?.ToBitmap();
            }
            else
            {
                return GetShellIcon(path, false);
            }
        }
        catch
        {
            return null;
        }
    }

    private static Image? GetShellIcon(string path, bool isDirectory)
    {
        var shfi = new NativeMethods.SHFILEINFO();
        uint flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON | NativeMethods.SHGFI_USEFILEATTRIBUTES;
        uint attrs = isDirectory ? NativeMethods.FILE_ATTRIBUTE_DIRECTORY : NativeMethods.FILE_ATTRIBUTE_NORMAL;

        IntPtr result = NativeMethods.SHGetFileInfo(path, attrs, ref shfi, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);

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

        return null;
    }

    public void ShowAt(Point screenLocation, int width)
    {
        Location = screenLocation;
        Width = width;
        BackColor = ThemeHelper.GetDropdownBackColor();

        if (!Visible)
        {
            Show();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool dark = ThemeHelper.IsDarkMode();

        using var bgBrush = new SolidBrush(ThemeHelper.GetDropdownBackColor());
        g.FillRectangle(bgBrush, ClientRectangle);

        using var borderPen = new Pen(ThemeHelper.GetBorderColor());
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        if (_items.Count == 0) return;

        using var textBrush = new SolidBrush(ThemeHelper.GetSystemForeColor());
        using var selectedBrush = new SolidBrush(ThemeHelper.GetSelectionBackColor());
        using var selectedTextBrush = new SolidBrush(Color.White);
        using var hoverBrush = new SolidBrush(ThemeHelper.GetHoverBackColor());
        using var font = new Font("Segoe UI", 9f);

        int visibleCount = Math.Min(_items.Count - _scrollOffset, MaxVisibleItems);

        for (int i = 0; i < visibleCount; i++)
        {
            int itemIndex = _scrollOffset + i;
            if (itemIndex >= _items.Count) break;

            var item = _items[itemIndex];
            int y = 1 + i * ItemHeight;
            var itemRect = new Rectangle(1, y, Width - 2, ItemHeight);

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

            var textRect = new Rectangle(textX, y, Width - textX - 6, ItemHeight);
            var sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisPath,
                FormatFlags = StringFormatFlags.NoWrap
            };

            g.DrawString(item, font, isSelected ? selectedTextBrush : textBrush, textRect, sf);
        }

        if (_scrollOffset > 0)
        {
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
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
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
