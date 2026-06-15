using System;
using System.Drawing;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>
/// What the status flyout needs from the host. Keeps the popup decoupled from the tray context.
/// </summary>
public interface IStatusController
{
    int? Pulse { get; }
    double? TotalSeconds { get; }
    DateTime? LastUpdated { get; }

    bool FocusActive { get; }
    TimeSpan FocusRemaining { get; }
    double FocusRemainingFraction { get; }
    int FocusRequestedMinutes { get; }
    int DefaultFocusMinutes { get; }

    void StartDefaultFocus();
    void StopFocus();
    void RestartFocus();
    void RefreshNow();
    void OpenDashboard();
    void OpenSettings();

    event Action StateChanged;
}

/// <summary>
/// A small top-most flyout shown on a single left-click of the tray icon. Shows today's pulse
/// and time logged, plus focus-session controls (Start, or remaining time + Stop/Restart).
/// </summary>
public sealed class StatusPopup : Form
{
    private const int ContentWidth = 224;

    private readonly IStatusController _controller;

    private readonly Label _pulseLabel = new();
    private readonly Label _captionLabel = new();
    private readonly Label _timeLabel = new();
    private readonly Label _updatedLabel = new();
    private readonly Label _focusStatus = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _startButton = new();
    private readonly TableLayoutPanel _stopRestartRow = new();

    public StatusPopup(IStatusController controller)
    {
        _controller = controller;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7f, 15f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(160, 160, 160); // 1px border color
        Padding = new Padding(1);

        BuildUi();
        _controller.StateChanged += OnStateChanged;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            Padding = new Padding(14),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _pulseLabel.AutoSize = true;
        _pulseLabel.Font = new Font("Segoe UI", 26f, FontStyle.Bold);
        _pulseLabel.Margin = new Padding(0);

        _captionLabel.AutoSize = true;
        _captionLabel.Text = "productivity pulse";
        _captionLabel.ForeColor = SystemColors.GrayText;
        _captionLabel.Margin = new Padding(0, 0, 0, 6);

        _timeLabel.AutoSize = true;
        _timeLabel.Font = new Font("Segoe UI", 10.5f);
        _timeLabel.Margin = new Padding(0, 0, 0, 1);

        _updatedLabel.AutoSize = true;
        _updatedLabel.ForeColor = SystemColors.GrayText;
        _updatedLabel.Margin = new Padding(0, 0, 0, 4);

        _focusStatus.AutoSize = true;
        _focusStatus.Font = new Font("Segoe UI", 9.5f);
        _focusStatus.Margin = new Padding(0, 2, 0, 4);

        _progress.Width = ContentWidth;
        _progress.Height = 8;
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        _progress.Margin = new Padding(0, 0, 0, 8);

        _startButton.Text = "Start focus";
        _startButton.AutoSize = false;
        _startButton.Size = new Size(ContentWidth, 30);
        _startButton.Margin = new Padding(0, 0, 0, 4);
        _startButton.Click += (_, _) => _controller.StartDefaultFocus();

        BuildStopRestartRow();

        root.Controls.Add(_pulseLabel);
        root.Controls.Add(_captionLabel);
        root.Controls.Add(_timeLabel);
        root.Controls.Add(_updatedLabel);
        root.Controls.Add(Divider());
        root.Controls.Add(_focusStatus);
        root.Controls.Add(_progress);
        root.Controls.Add(_startButton);
        root.Controls.Add(_stopRestartRow);
        root.Controls.Add(Divider());
        root.Controls.Add(BuildFooter());

        Controls.Add(root);
    }

    private void BuildStopRestartRow()
    {
        _stopRestartRow.ColumnCount = 2;
        _stopRestartRow.RowCount = 1;
        _stopRestartRow.AutoSize = true;
        _stopRestartRow.Margin = new Padding(0, 0, 0, 4);
        _stopRestartRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth / 2f));
        _stopRestartRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth / 2f));

        var stop = new Button { Text = "Stop", Dock = DockStyle.Fill, Height = 30, Margin = new Padding(0, 0, 4, 0) };
        stop.Click += (_, _) => _controller.StopFocus();

        var restart = new Button { Text = "Restart", Dock = DockStyle.Fill, Height = 30, Margin = new Padding(4, 0, 0, 0) };
        restart.Click += (_, _) => _controller.RestartFocus();

        _stopRestartRow.Controls.Add(stop, 0, 0);
        _stopRestartRow.Controls.Add(restart, 1, 0);
    }

    private FlowLayoutPanel BuildFooter()
    {
        var footer = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        footer.Controls.Add(FooterLink("Refresh", _controller.RefreshNow));
        footer.Controls.Add(Dot());
        footer.Controls.Add(FooterLink("Dashboard", _controller.OpenDashboard));
        footer.Controls.Add(Dot());
        footer.Controls.Add(FooterLink("Settings", () => { _controller.OpenSettings(); Hide(); }));
        return footer;
    }

    private static LinkLabel FooterLink(string text, Action onClick)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        link.LinkClicked += (_, _) => onClick();
        return link;
    }

    private static Label Dot() => new()
    {
        Text = "·",
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(6, 2, 6, 0),
    };

    private static Panel Divider() => new()
    {
        Width = ContentWidth,
        Height = 1,
        BackColor = SystemColors.ControlLight,
        Margin = new Padding(0, 6, 0, 8),
    };

    public void ShowNearTray()
    {
        UpdateView();
        PositionNearTray();
        Show();
        Activate();
    }

    private void PositionNearTray()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
    }

    private void OnStateChanged()
    {
        if (!IsDisposed)
        {
            UpdateView();
        }
    }

    private void UpdateView()
    {
        int? pulse = _controller.Pulse;
        _pulseLabel.Text = pulse.HasValue ? $"{pulse.Value}%" : "—";
        _pulseLabel.ForeColor = pulse.HasValue ? PulseColor(pulse.Value) : SystemColors.GrayText;

        _timeLabel.Text = _controller.TotalSeconds.HasValue
            ? $"{FormatDuration(_controller.TotalSeconds.Value)} logged today"
            : "No time logged yet";
        _updatedLabel.Text = _controller.LastUpdated.HasValue
            ? $"updated {_controller.LastUpdated.Value:HH:mm}"
            : "updating…";

        bool active = _controller.FocusActive;
        _startButton.Visible = !active;
        _stopRestartRow.Visible = active;
        _progress.Visible = active;

        if (active)
        {
            string ofPart = _controller.FocusRequestedMinutes > 0 ? $" of {_controller.FocusRequestedMinutes} min" : "";
            _focusStatus.Text = $"Focusing — {FormatClock(_controller.FocusRemaining)} left{ofPart}";
            int elapsed = (int)Math.Round((1 - _controller.FocusRemainingFraction) * 100);
            _progress.Value = Math.Clamp(elapsed, 0, 100);
        }
        else
        {
            _focusStatus.Text = "No focus session running";
            _startButton.Text = $"Start focus · {_controller.DefaultFocusMinutes} min";
        }

        if (Visible)
        {
            PositionNearTray(); // keep anchored bottom-right as the height changes
        }
    }

    private static Color PulseColor(int pulse) => pulse switch
    {
        >= 75 => Color.FromArgb(34, 160, 70),
        >= 50 => Color.FromArgb(196, 140, 0),
        _ => Color.FromArgb(201, 58, 48),
    };

    private static string FormatDuration(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        int hours = (int)ts.TotalHours;
        return hours > 0 ? $"{hours}h {ts.Minutes}m" : $"{ts.Minutes}m";
    }

    private static string FormatClock(TimeSpan ts) =>
        ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes}:{ts.Seconds:00}";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.StateChanged -= OnStateChanged;
        }
        base.Dispose(disposing);
    }
}
