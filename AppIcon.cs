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
                    _icon = Icon.ExtractAssociatedIcon(exe);
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
