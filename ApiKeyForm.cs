using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>
/// Modal dialog for the API key, refresh interval, focus-session preferences, and reminders.
/// </summary>
public sealed class ApiKeyForm : Form
{
    private readonly TextBox _keyBox;
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
        ClientSize = new Size(460, 492);
        Font = new Font("Segoe UI", 9f);

        var keyLabel = new Label { Text = "RescueTime API key:", Location = new Point(16, 14), AutoSize = true };
        _keyBox = new TextBox
        {
            Text = config.ApiKey,
            Location = new Point(16, 35),
            Width = 428,
            UseSystemPasswordChar = true,
        };

        var link = new LinkLabel
        {
            Text = "Get your API key from rescuetime.com/anapi/manage",
            Location = new Point(16, 62),
            AutoSize = true,
        };
        link.LinkClicked += (_, _) => OpenUrl("https://www.rescuetime.com/anapi/manage");

        var refreshLabel = new Label { Text = "Refresh pulse every", Location = new Point(16, 98), AutoSize = true };
        _refreshBox = new NumericUpDown
        {
            Location = new Point(150, 95),
            Width = 60,
            Minimum = 1,
            Maximum = 180,
            Value = Math.Clamp(config.RefreshMinutes, 1, 180),
        };
        var refreshUnit = new Label { Text = "minutes", Location = new Point(216, 98), AutoSize = true };

        var focusLabel = new Label { Text = "Default focus length", Location = new Point(16, 130), AutoSize = true };
        _focusBox = new NumericUpDown
        {
            Location = new Point(150, 127),
            Width = 60,
            Minimum = 5,
            Maximum = 180,
            Increment = 5,
            Value = Math.Clamp(config.DefaultFocusMinutes / 5 * 5, 5, 180),
        };
        var focusUnit = new Label { Text = "minutes", Location = new Point(216, 130), AutoSize = true };

        _notifyCheck = new CheckBox
        {
            Text = "Show notifications when a focus session starts and ends",
            Location = new Point(16, 162),
            AutoSize = true,
            Checked = config.ShowFocusNotifications,
        };

        _soundCheck = new CheckBox
        {
            Text = "Play a sound when a focus session finishes",
            Location = new Point(16, 188),
            AutoSize = true,
            Checked = config.PlayFocusEndSound,
        };

        var soundLabel = new Label { Text = "Custom end sound (.wav, optional):", Location = new Point(16, 220), AutoSize = true };
        _soundPathBox = new TextBox
        {
            Text = config.FocusEndSoundPath,
            Location = new Point(16, 241),
            Width = 340,
        };
        var browseButton = new Button { Text = "Browse…", Location = new Point(364, 240), Width = 80 };
        browseButton.Click += (_, _) => BrowseForSound();

        var divider = new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Location = new Point(16, 280),
            Size = new Size(428, 2),
        };
        var reminderHeading = new Label
        {
            Text = "Focus reminders",
            Location = new Point(16, 290),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };

        _remindCheck = new CheckBox
        {
            Text = "Remind me to start a focus session during work hours",
            Location = new Point(16, 316),
            AutoSize = true,
            Checked = config.EnableFocusReminders,
        };

        var intervalLabel = new Label { Text = "Remind every", Location = new Point(16, 350), AutoSize = true };
        _intervalBox = new NumericUpDown
        {
            Location = new Point(150, 347),
            Width = 60,
            Minimum = 5,
            Maximum = 480,
            Increment = 5,
            Value = Math.Clamp(config.ReminderIntervalMinutes, 5, 480),
        };
        var intervalUnit = new Label { Text = "minutes", Location = new Point(216, 350), AutoSize = true };

        var workLabel = new Label { Text = "Work hours", Location = new Point(16, 382), AutoSize = true };
        _workStartPicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Width = 70,
            Location = new Point(150, 379),
            Value = DateTime.Today + config.WorkdayStartTime,
        };
        var toLabel = new Label { Text = "to", Location = new Point(228, 382), AutoSize = true };
        _workEndPicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Width = 70,
            Location = new Point(250, 379),
            Value = DateTime.Today + config.WorkdayEndTime,
        };

        _weekdaysCheck = new CheckBox
        {
            Text = "Weekdays only",
            Location = new Point(16, 412),
            AutoSize = true,
            Checked = config.RemindOnlyWeekdays,
        };

        var okButton = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(278, 452), Width = 80 };
        okButton.Click += (_, _) =>
        {
            if (ApiKey.Length == 0)
            {
                MessageBox.Show(this, "Please enter your RescueTime API key.", "Missing key",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(364, 452), Width = 80 };

        Controls.AddRange(new Control[]
        {
            keyLabel, _keyBox, link,
            refreshLabel, _refreshBox, refreshUnit,
            focusLabel, _focusBox, focusUnit,
            _notifyCheck, _soundCheck,
            soundLabel, _soundPathBox, browseButton,
            divider, reminderHeading, _remindCheck,
            intervalLabel, _intervalBox, intervalUnit,
            workLabel, _workStartPicker, toLabel, _workEndPicker,
            _weekdaysCheck,
            okButton, cancelButton,
        });

        AcceptButton = okButton;
        CancelButton = cancelButton;
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
