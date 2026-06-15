using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>
/// Modal dialog for the API key, refresh interval, focus-session preferences, and reminders.
/// Laid out with TableLayoutPanel/FlowLayoutPanel so it never overlaps at non-100% DPI.
/// </summary>
public sealed class ApiKeyForm : Form
{
    private readonly TextBox _keyBox;
    private readonly Button _testButton;
    private readonly NumericUpDown _refreshBox;
    private readonly NumericUpDown _focusBox;
    private readonly CheckBox _notifyCheck;
    private readonly CheckBox _soundCheck;
    private readonly TextBox _soundPathBox;
    private readonly CheckBox _remindCheck;
    private readonly NumericUpDown _intervalBox;
    private readonly DateTimePicker _workStartPicker;
    private readonly DateTimePicker _workEndPicker;
    private readonly CheckBox _weekdaysCheck;

    public string ApiKey => _keyBox.Text.Trim();
    public int RefreshMinutes => (int)_refreshBox.Value;
    public int DefaultFocusMinutes => (int)_focusBox.Value;
    public bool ShowFocusNotifications => _notifyCheck.Checked;
    public bool PlayFocusEndSound => _soundCheck.Checked;
    public string FocusEndSoundPath => _soundPathBox.Text.Trim();
    public bool EnableFocusReminders => _remindCheck.Checked;
    public int ReminderIntervalMinutes => (int)_intervalBox.Value;
    public string WorkdayStart => _workStartPicker.Value.ToString("HH:mm");
    public string WorkdayEnd => _workEndPicker.Value.ToString("HH:mm");
    public bool RemindOnlyWeekdays => _weekdaysCheck.Checked;

    public ApiKeyForm(AppConfig config)
    {
        Text = "RescueTime Status — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        Icon = AppIcon.Value;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7f, 15f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        // --- controls ---
        _keyBox = new TextBox { Width = 348, UseSystemPasswordChar = true, Text = config.ApiKey, Margin = new Padding(0) };
        _testButton = new Button { Text = "Test", AutoSize = false, Size = new Size(76, 28), Margin = new Padding(8, 0, 0, 0) };
        _testButton.Click += async (_, _) => await TestApiKeyAsync();

        var link = new LinkLabel
        {
            Text = "Get your API key from rescuetime.com/anapi/manage",
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 8),
        };
        link.LinkClicked += (_, _) => OpenUrl("https://www.rescuetime.com/anapi/manage");

        _refreshBox = Spin(config.RefreshMinutes, 1, 180, 1);
        _focusBox = Spin(config.DefaultFocusMinutes / 5 * 5, 5, 180, 5);

        _notifyCheck = Check("Show notifications when a focus session starts and ends", config.ShowFocusNotifications);
        _soundCheck = Check("Play a sound when a focus session finishes", config.PlayFocusEndSound);

        _soundPathBox = new TextBox { Width = 348, Text = config.FocusEndSoundPath, Margin = new Padding(0) };
        var browseButton = new Button { Text = "Browse…", AutoSize = false, Size = new Size(84, 28), Margin = new Padding(8, 0, 0, 0) };
        browseButton.Click += (_, _) => BrowseForSound();

        _remindCheck = Check("Remind me to start a focus session during work hours", config.EnableFocusReminders);
        _intervalBox = Spin(config.ReminderIntervalMinutes, 5, 480, 5);

        _workStartPicker = TimePicker(config.WorkdayStartTime);
        _workEndPicker = TimePicker(config.WorkdayEndTime);

        _weekdaysCheck = Check("Weekdays only", config.RemindOnlyWeekdays);

        var okButton = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = false, Size = new Size(92, 30), Margin = new Padding(0, 0, 8, 0) };
        okButton.Click += (_, _) =>
        {
            if (ApiKey.Length == 0)
            {
                MessageBox.Show(this, "Please enter your RescueTime API key.", "Missing key",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = false, Size = new Size(92, 30), Margin = new Padding(0) };

        // --- layout ---
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        root.Controls.Add(Label("RescueTime API key:"));
        root.Controls.Add(Row(_keyBox, _testButton));
        root.Controls.Add(link);
        root.Controls.Add(Grid(
            new Control[] { Field("Refresh pulse every"), _refreshBox, Field("minutes") },
            new Control[] { Field("Default focus length"), _focusBox, Field("minutes") }));
        root.Controls.Add(_notifyCheck);
        root.Controls.Add(_soundCheck);
        root.Controls.Add(Label("Custom end sound (.wav, optional):"));
        root.Controls.Add(Row(_soundPathBox, browseButton));
        root.Controls.Add(Divider());
        root.Controls.Add(Heading("Focus reminders"));
        root.Controls.Add(_remindCheck);
        root.Controls.Add(Grid(
            new Control[] { Field("Remind every"), _intervalBox, Field("minutes") },
            new Control[] { Field("Work hours"), _workStartPicker, Field("to"), _workEndPicker }));
        root.Controls.Add(_weekdaysCheck);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);
        root.Controls.Add(buttons);

        Controls.Add(root);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    // ----- layout helpers ---------------------------------------------------

    private static Label Label(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, 4, 0, 2) };

    private static Label Field(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };

    private static Label Heading(string text) =>
        new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 2, 0, 4) };

    private static CheckBox Check(string text, bool value) =>
        new() { Text = text, AutoSize = true, Checked = value, Margin = new Padding(0, 4, 0, 2) };

    private static NumericUpDown Spin(int value, int min, int max, int step) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = step,
        Value = Math.Clamp(value, min, max),
        Width = 60,
        Margin = new Padding(0, 0, 8, 0),
    };

    private static DateTimePicker TimePicker(TimeSpan value) => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "HH:mm",
        ShowUpDown = true,
        Width = 70,
        Value = DateTime.Today + value,
        Margin = new Padding(0, 0, 8, 0),
    };

    private static TableLayoutPanel Row(params Control[] controls)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = controls.Length,
            RowCount = 1,
            Margin = new Padding(0, 3, 0, 3),
        };
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < controls.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            controls[i].Anchor = AnchorStyles.Left; // vertically center each control within the row
            row.Controls.Add(controls[i], i, 0);
        }
        return row;
    }

    // Multiple rows sharing one set of columns, so labels/controls line up vertically
    // across rows (auto-sizes each column to its widest cell — no clipping, no magic widths).
    private static TableLayoutPanel Grid(params Control[][] rows)
    {
        int columns = 0;
        foreach (Control[] r in rows)
        {
            columns = Math.Max(columns, r.Length);
        }

        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columns,
            RowCount = rows.Length,
            Margin = new Padding(0, 3, 0, 3),
        };
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        for (int r = 0; r < rows.Length; r++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int c = 0; c < rows[r].Length; c++)
            {
                rows[r][c].Anchor = AnchorStyles.Left; // vertically center within the row
                grid.Controls.Add(rows[r][c], c, r);
            }
        }
        return grid;
    }

    private static Panel Divider() => new()
    {
        Height = 1,
        Dock = DockStyle.Fill,
        BackColor = SystemColors.ControlDark,
        Margin = new Padding(0, 12, 0, 8),
    };

    // ----- behavior ---------------------------------------------------------

    private async Task TestApiKeyAsync()
    {
        string key = ApiKey;
        if (key.Length == 0)
        {
            MessageBox.Show(this, "Enter your API key first.", "Nothing to test",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string original = _testButton.Text;
        _testButton.Enabled = false;
        _testButton.Text = "Testing…";
        try
        {
            using var client = new RescueTimeClient();
            PulseSnapshot snap = await client.GetTodayAsync(key);
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            MessageBox.Show(this,
                $"Success — RescueTime responded.\n\nToday's productivity pulse: {snap.Pulse}%.",
                "API key works", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (RescueTimeException ex)
        {
            if (!IsDisposed && IsHandleCreated)
            {
                MessageBox.Show(this,
                    "Test failed:\n\n" + ex.Message,
                    "Test failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            if (!IsDisposed && !_testButton.IsDisposed)
            {
                _testButton.Text = original;
                _testButton.Enabled = true;
            }
        }
    }

    private void BrowseForSound()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "WAV audio (*.wav)|*.wav|All files (*.*)|*.*",
            Title = "Choose an end-of-session sound",
        };
        if (_soundPathBox.Text.Trim().Length > 0)
        {
            try { dialog.FileName = _soundPathBox.Text.Trim(); } catch { /* ignore */ }
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _soundPathBox.Text = dialog.FileName;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort.
        }
    }
}
