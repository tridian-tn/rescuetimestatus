using System;
using System.Drawing;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>
/// A small top-most reminder popup that stays on screen until the user acts on it
/// (Windows toast balloons can't guarantee "stays until dismissed").
/// Laid out with TableLayoutPanel/FlowLayoutPanel so nothing clips or overlaps at non-100% DPI.
/// </summary>
public sealed class ReminderForm : Form
{
    private const int SnoozeMinutes = 10;

    /// <summary>Raised with the focus length (minutes) when the user clicks Start.</summary>
    public event Action<int>? StartRequested;

    /// <summary>Raised with the snooze length (minutes) when the user snoozes.</summary>
    public event Action<int>? Snoozed;

    public ReminderForm(int defaultFocusMinutes)
    {
        Text = "Focus reminder";
        FormBorderStyle = FormBorderStyle.FixedToolWindow; // small frame + a real close button
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Icon = AppIcon.Value;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7f, 15f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var heading = new Label
        {
            Text = "Time to focus?",
            AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };

        var message = new Label
        {
            Text = "No focus session is running. Start one to stay on track.",
            AutoSize = true,
            MaximumSize = new Size(290, 0), // wrap at 290px, grow vertically instead of truncating
            Margin = new Padding(0, 0, 0, 14),
        };

        var startButton = Button($"Start {defaultFocusMinutes} min");
        startButton.Click += (_, _) =>
        {
            StartRequested?.Invoke(defaultFocusMinutes);
            Close();
        };

        var snoozeButton = Button($"Snooze {SnoozeMinutes}m");
        snoozeButton.Click += (_, _) =>
        {
            Snoozed?.Invoke(SnoozeMinutes);
            Close();
        };

        var dismissButton = Button("Dismiss");
        dismissButton.Margin = new Padding(0); // last button: no trailing gap
        dismissButton.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        buttons.Controls.AddRange(new Control[] { startButton, snoozeButton, dismissButton });

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 14, 16, 14),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(heading);
        root.Controls.Add(message);
        root.Controls.Add(buttons);

        Controls.Add(root);

        AcceptButton = startButton;
        CancelButton = dismissButton;
    }

    // Auto-sizing button with a comfortable minimum height and horizontal padding so the
    // label (e.g. "Snooze 10m") is never clipped, whatever the DPI.
    private static Button Button(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(80, 30),
        Padding = new Padding(8, 0, 8, 0),
        Margin = new Padding(0, 0, 8, 0),
    };

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PositionBottomRight(); // after auto-size + DPI scaling, so Width/Height are final
    }

    private void PositionBottomRight()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
    }

    // Surface the window without stealing keyboard focus from whatever the user is doing.
    protected override bool ShowWithoutActivation => true;
}
