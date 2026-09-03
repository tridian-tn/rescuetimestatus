using System;
using System.Runtime.InteropServices;

namespace RescueTimeStatus;

/// <summary>
/// Hands back memory the app isn't using. A tray app is idle between polls, so nearly everything
/// resident is cold — Windows can have it back and will page in the handful of pages the next tick
/// actually touches.
/// </summary>
public static class MemoryTrim
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    // The pseudo-handle for the current process; it needs no closing.
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// Collects, lets finalizers run, then releases the working set. Measured on an idle instance:
    /// ~65 MB resident down to ~6 MB, private bytes unchanged around 14 MB. The collection on its
    /// own doesn't move the resident figure — releasing the working set is what does.
    /// </summary>
    public static void Run()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        EmptyWorkingSet(GetCurrentProcess());
    }
}
