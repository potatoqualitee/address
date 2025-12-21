namespace AddressBar;

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
        BackColor = ThemeHelper.GetDropdownBackColor();
    }

    public void SetItems(List<(string Path, Image? Icon)> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _hoveredIndex = -1;

        int visibleCount = Math.Min(items.Count, MaxVisibleItems);
        if (visibleCount == 0) visibleCount = 1;
        Height = visibleCount * ItemHeight + 2;
        BackColor = ThemeHelper.GetDropdownBackColor();
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
        bool dark = ThemeHelper.IsDarkMode();

        using var bgBrush = new SolidBrush(ThemeHelper.GetDropdownBackColor());
        g.FillRectangle(bgBrush, ClientRectangle);

        using var borderPen = new Pen(ThemeHelper.GetBorderColor());
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        using var textBrush = new SolidBrush(ThemeHelper.GetSystemForeColor());
        using var hoverBrush = new SolidBrush(ThemeHelper.GetHoverBackColor());
        using var font = new Font("Segoe UI", 9f);

        if (_items.Count == 0)
        {
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

            if (i == _hoveredIndex)
            {
                g.FillRectangle(hoverBrush, itemRect);
            }

            if (item.Icon != null)
            {
                int iconY = y + (ItemHeight - IconSize) / 2;
                g.DrawImage(item.Icon, new Rectangle(6, iconY, IconSize, IconSize));
            }

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

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            cp.ClassStyle |= NativeMethods.CS_DROPSHADOW;
            return cp;
        }
    }
}
