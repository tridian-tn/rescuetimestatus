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

        // A tray utility shouldn't die behind a modal dialog nobody's watching. Record what went
        // wrong (with the resource counters, which is what a drawing failure turns on) and carry on;
        // an exception on a timer tick is rarely something the next tick can't recover from.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ResourceLog.Failure("unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ResourceLog.Failure("unhandled background exception", e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext();
        Application.Run(context);

        GC.KeepAlive(_singleInstance);
    }
}
