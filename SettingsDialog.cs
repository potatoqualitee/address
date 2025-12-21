namespace AddressBar;

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
        BackColor = ThemeHelper.IsDarkMode() ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
        ForeColor = ThemeHelper.IsDarkMode() ? Color.White : SystemColors.ControlText;

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
}
