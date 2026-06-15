using System;
using System.Drawing;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>
/// A small top-most reminder popup that stays on screen until the user acts on it
/// (Windows toast balloons can't guarantee "stays until dismissed").
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
        ClientSize = new Size(320, 146);
        Font = new Font("Segoe UI", 9f);

        var heading = new Label
        {
            Text = "Time to focus?",
            Location = new Point(16, 14),
            AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        };

        var message = new Label
        {
            Text = "No focus session is running. Start one to stay on track.",
            Location = new Point(16, 42),
            Size = new Size(290, 36),
        };

        var startButton = new Button
        {
            Text = $"Start {defaultFocusMinutes} min",
            Location = new Point(16, 100),
            Width = 110,
        };
        startButton.Click += (_, _) =>
        {
            StartRequested?.Invoke(defaultFocusMinutes);
            Close();
        };

        var snoozeButton = new Button
        {
            Text = $"Snooze {SnoozeMinutes}m",
            Location = new Point(134, 100),
            Width = 90,
        };
        snoozeButton.Click += (_, _) =>
        {
            Snoozed?.Invoke(SnoozeMinutes);
            Close();
        };

        var dismissButton = new Button
        {
            Text = "Dismiss",
            Location = new Point(232, 100),
            Width = 74,
        };
        dismissButton.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { heading, message, startButton, snoozeButton, dismissButton });

        AcceptButton = startButton;
        CancelButton = dismissButton;

        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
    }

    // Surface the window without stealing keyboard focus from whatever the user is doing.
    protected override bool ShowWithoutActivation => true;
}
