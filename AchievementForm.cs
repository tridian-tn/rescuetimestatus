using System;
using System.Drawing;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>
/// Shown when a focus session ends: asks what the user got done. If they enter text and save,
/// it's posted to RescueTime as a daily highlight. Stays until the user acts on it.
/// </summary>
public sealed class AchievementForm : Form
{
    private readonly TextBox _input;

    /// <summary>Raised with the (non-empty, trimmed) text when the user saves.</summary>
    public event Action<string>? Saved;

    public AchievementForm(string subtitle)
    {
        Text = "Focus session complete";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Icon = AppIcon.Value;
        ClientSize = new Size(340, 150);
        Font = new Font("Segoe UI", 9f);

        var heading = new Label
        {
            Text = "What did you get done?",
            Location = new Point(16, 14),
            AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        };

        var sub = new Label
        {
            Text = subtitle,
            Location = new Point(16, 40),
            Size = new Size(308, 20),
            ForeColor = SystemColors.GrayText,
        };

        _input = new TextBox
        {
            Location = new Point(16, 64),
            Width = 308,
            MaxLength = 255,
            PlaceholderText = "e.g. Drafted the Q3 report",
        };

        var saveButton = new Button { Text = "Save highlight", Location = new Point(16, 104), Width = 130 };
        saveButton.Click += (_, _) =>
        {
            string text = _input.Text.Trim();
            if (text.Length > 0)
            {
                Saved?.Invoke(text);
            }
            Close();
        };

        var skipButton = new Button { Text = "Skip", Location = new Point(250, 104), Width = 74 };
        skipButton.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { heading, sub, _input, saveButton, skipButton });
        AcceptButton = saveButton;
        CancelButton = skipButton;

        PositionBottomRight();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _input.Focus(); // ready to type immediately
    }

    private void PositionBottomRight()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
    }
}
