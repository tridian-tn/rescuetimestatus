using System;
using System.Drawing;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>
/// The application icon (from the .exe's embedded <c>ApplicationIcon</c>), loaded once and
/// reused for window title bars and the taskbar.
/// </summary>
public static class AppIcon
{
    private static bool _loaded;
    private static Icon? _icon;

    public static Icon? Value
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                try
                {
                    string exe = Environment.ProcessPath ?? Application.ExecutablePath;
                    // ExtractAssociatedIcon owns a GDI handle; clone a managed copy and free the
                    // original rather than holding the extracted handle for the app's lifetime.
                    using Icon? extracted = Icon.ExtractAssociatedIcon(exe);
                    _icon = extracted is null ? null : (Icon)extracted.Clone();
                }
                catch
                {
                    _icon = null;
                }
            }
            return _icon;
        }
    }
}
