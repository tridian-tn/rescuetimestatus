using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>
/// A small "About" dialog showing the app name, the git-derived <see cref="AppVersion"/>, and a
/// link to the project. Modal, centred on screen. Laid out with TableLayoutPanel/FlowLayoutPanel
/// so nothing clips or overlaps at non-100% DPI (matches <see cref="AchievementForm"/>).
/// </summary>
public sealed class AboutForm : Form
{
    private const string RepoUrl = "https://github.com/tridian-tn/RescueTimeStatus";
    private const int ContentWidth = 300;

    // ToBitmap() allocates a new bitmap each time; a PictureBox doesn't dispose its Image, so we
    // own it and free it in Dispose to avoid leaking a GDI handle each time the dialog is opened.
    private readonly Bitmap _iconBitmap;

    public AboutForm()
    {
        Text = "About RescueTime Status";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Icon = AppIcon.Value;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7f, 15f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _iconBitmap = (AppIcon.Value ?? SystemIcons.Application).ToBitmap();
        var iconBox = new PictureBox
        {
            Image = _iconBitmap,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(48, 48),
            Margin = new Padding(0, 0, 0, 10),
        };

        var nameLabel = new Label
        {
            Text = "RescueTime Status",
            AutoSize = true,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2),
        };

        var versionLabel = new Label
        {
            Text = $"Version {AppVersion.Display}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 10),
        };

        var description = new Label
        {
            Text = "A Windows tray app for your RescueTime Productivity Pulse and focus sessions.",
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0), // wrap, grow vertically instead of truncating
            Margin = new Padding(0, 0, 0, 10),
        };

        var link = new LinkLabel
        {
            Text = "View project on GitHub",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14),
        };
        link.LinkClicked += (_, _) => OpenRepo();

        var okButton = new Button
        {
            Text = "OK",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(80, 30),
            Padding = new Padding(8, 0, 8, 0),
            Margin = new Padding(0),
            Anchor = AnchorStyles.None,
            DialogResult = DialogResult.OK,
        };

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20, 18, 20, 16),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Centre each item in the single column.
        foreach (Control c in new Control[] { iconBox, nameLabel, versionLabel, description, link, okButton })
        {
            c.Anchor = AnchorStyles.None;
            root.Controls.Add(c);
        }

        Controls.Add(root);

        AcceptButton = okButton;
        CancelButton = okButton;
    }

    private static void OpenRepo()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort, same as the dashboard link on the tray menu.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconBitmap.Dispose();
        }
        base.Dispose(disposing);
    }
}
