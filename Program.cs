using System;
using System.Threading;
using System.Windows.Forms;

namespace RescueTimeStatus;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main()
    {
        // Only allow one tray icon at a time.
        _singleInstance = new Mutex(initiallyOwned: true, "RescueTimeStatus.SingleInstance", out bool isNew);
        if (!isNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext();
        Application.Run(context);

        GC.KeepAlive(_singleInstance);
    }
}
