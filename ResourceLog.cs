using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

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

            Append(line);
        }
        catch
        {
            // Best-effort: a diagnostic that fails must stay silent.
        }
    }

    /// <summary>
    /// Records a failure with the usual counters, followed by the processes holding the most GDI
    /// objects. A drawing failure while our own GDI count is low means the shortage is session-wide,
    /// and this line names whoever caused it.
    /// </summary>
    /// <param name="note">Short tag for what failed</param>
    /// <param name="ex">The exception that triggered the sample, if there was one</param>
    public static void Failure(string note, Exception? ex = null)
    {
        Sample(ex is null ? note : $"{note} — {ex.GetType().Name}: {ex.Message}");

        try
        {
            Append("                       " + TopGdiConsumers(5));
        }
        catch
        {
            // As above — diagnostics never take the app down.
        }
    }

    /// <summary>Summarises session-wide GDI use: the total we can see, and the heaviest processes.</summary>
    /// <param name="count">How many processes to name</param>
    /// <returns>A single-line summary</returns>
    private static string TopGdiConsumers(int count)
    {
        var rows = new List<(string Name, int Pid, int Gdi, int User)>();

        foreach (Process p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    int gdi = GetGuiResources(p.Handle, GR_GDIOBJECTS);
                    if (gdi > 0) rows.Add((p.ProcessName, p.Id, gdi, GetGuiResources(p.Handle, GR_USEROBJECTS)));
                }
                catch
                {
                    // Protected or already-exited processes can't be queried — skip them.
                }
            }
        }

        rows.Sort((a, b) => b.Gdi.CompareTo(a.Gdi));

        int total = 0;
        foreach ((_, _, int gdi, _) in rows) total += gdi;

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"gdi across {rows.Count} visible processes = {total:N0}; top:");
        for (int i = 0; i < Math.Min(count, rows.Count); i++)
        {
            (string name, int pid, int gdi, int user) = rows[i];
            sb.Append(CultureInfo.InvariantCulture, $" {name}({pid}) gdi={gdi:N0} user={user:N0};");
        }
        return sb.ToString();
    }

    private static void Append(string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.AppendAllText(LogPath, line + Environment.NewLine);
    }
}
