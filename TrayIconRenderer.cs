using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace RescueTimeStatus;

/// <summary>
/// Draws the pulse number onto a small, color-coded rounded icon for the tray.
/// </summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <param name="pulse">0-100 pulse, or null when unknown (renders a dash).</param>
    /// <param name="focusFraction">
    /// Fraction of a focus session remaining (0..1) to draw as a countdown ring on top of the
    /// badge, or null when no session is active.
    /// </param>
    public static Icon Render(int? pulse, double? focusFraction = null)
    {
        const int size = 32;

        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            string text = pulse.HasValue ? pulse.Value.ToString() : "–";
            Color bg = pulse.HasValue ? ColorForPulse(pulse.Value) : Color.FromArgb(120, 120, 120);

            using (var brush = new SolidBrush(bg))
            using (var path = RoundedRect(new Rectangle(0, 0, size - 1, size - 1), 7))
            {
                g.FillPath(brush, path);
            }

            // Smaller font for three digits ("100") so it stays inside the badge.
            float fontPx = text.Length >= 3 ? 14f : 19f;
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            using var textBrush = new SolidBrush(Color.White);
            g.DrawString(text, font, textBrush, new RectangleF(0, -1, size, size), format);

            // Focus countdown ring — drawn ON TOP of the badge so it's fully visible.
            if (focusFraction is double frac)
            {
                var ring = new RectangleF(2.5f, 2.5f, size - 5, size - 5);

                using var trackPen = new Pen(Color.FromArgb(95, 0, 0, 0), 3f);
                g.DrawEllipse(trackPen, ring);

                float sweep = 360f * (float)Math.Clamp(frac, 0, 1);
                if (sweep > 0)
                {
                    using var progressPen = new Pen(Color.FromArgb(255, 90, 170, 255), 3f)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                    };
                    g.DrawArc(progressPen, ring, -90f, sweep); // start at top, deplete clockwise
                }
            }
        }

        // GetHicon() allocates a GDI handle we must destroy ourselves. Clone the icon
        // so the managed copy survives after we free the original handle.
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static Color ColorForPulse(int pulse) => pulse switch
    {
        >= 75 => Color.FromArgb(34, 160, 70),   // green  — very productive day
        >= 50 => Color.FromArgb(214, 152, 0),   // amber  — mixed
        _     => Color.FromArgb(201, 58, 48),   // red    — distracted
    };

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
