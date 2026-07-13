using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace RescueTimeStatus;

/// <summary>
/// Appends a line of process resource counters to <c>%APPDATA%\RescueTimeStatus\diag.log</c> so a
/// slow leak can be diagnosed after a crash: whichever counter climbs steadily over hours points at
/// the culprit — GDI → graphics/icons, USER/handles → windows/menus, managed heap → a rooted-object
/// leak, private bytes with everything else flat → something native/unmanaged.
/// </summary>
public static class ResourceLog
{
    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

    private const int GR_GDIOBJECTS = 0;
    private const int GR_USEROBJECTS = 1;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RescueTimeStatus",
        "diag.log");

    /// <summary>Records one sample. Never throws — diagnostics must not take the app down.</summary>
    /// <param name="note">Short tag for the event that triggered the sample (e.g. "startup").</param>
    public static void Sample(string note = "")
    {
        try
        {
            using Process p = Process.GetCurrentProcess();

            long managed = GC.GetTotalMemory(false);
            long priv = p.PrivateMemorySize64;
            long ws = p.WorkingSet64;
            int handles = p.HandleCount;
            int gdi = GetGuiResources(p.Handle, GR_GDIOBJECTS);
            int user = GetGuiResources(p.Handle, GR_USEROBJECTS);

            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss}  managed={1,12:N0}  priv={2,14:N0}  ws={3,14:N0}  handles={4,6}  gdi={5,6}  user={6,6}  {7}",
                DateTime.Now, managed, priv, ws, handles, gdi, user, note);

            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // Best-effort: a diagnostic that fails must stay silent.
        }
    }
}
