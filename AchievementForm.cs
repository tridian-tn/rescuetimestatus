using System;
using System.Drawing;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>
/// Shown when a focus session ends: asks what the user got done. If they enter text and save,
/// it's posted to RescueTime as a daily highlight. Stays until the user acts on it.
/// Laid out with TableLayoutPanel/FlowLayoutPanel so nothing clips or overlaps at non-100% DPI.
/// </summary>
public sealed class AchievementForm : Form
{
    private const int ContentWidth = 308;

    private readonly TextBox _input;

    /// <summary>Raised with the (non-empty, trimmed) text when the user saves.</summary>
    public event Action<string>? Saved;

    public AchievementForm(string title, string subtitle)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
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
            Text = "What did you get done?",
            AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };

        var sub = new Label
        {
            Text = subtitle,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0), // wrap, grow vertically instead of truncating
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8),
        };

        _input = new TextBox
        {
            Width = ContentWidth,
            MaxLength = 255,
            PlaceholderText = "e.g. Drafted the Q3 report",
            Margin = new Padding(0, 0, 0, 12),
        };

        var saveButton = CreateButton("Save highlight");
        saveButton.Click += (_, _) =>
        {
            string text = _input.Text.Trim();
            if (text.Length > 0)
            {
                Saved?.Invoke(text);
            }
            Close();
        };

        var skipButton = CreateButton("Skip");
        skipButton.Margin = new Padding(0); // last button: no trailing gap
        skipButton.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        buttons.Controls.AddRange(new Control[] { saveButton, skipButton });

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
        root.Controls.Add(sub);
        root.Controls.Add(_input);
        root.Controls.Add(buttons);

        Controls.Add(root);

        AcceptButton = saveButton;
        CancelButton = skipButton;
    }

    private static Button CreateButton(string text) => new()
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
