using System;
using System.IO;
using System.Text.Json;

namespace RescueTimeStatus;

/// <summary>
/// User settings, persisted to %APPDATA%\RescueTimeStatus\config.json.
/// </summary>
public sealed class AppConfig
{
    public string ApiKey { get; set; } = "";

    public int RefreshMinutes { get; set; } = 5;

    /// <summary>Default focus-session length (minutes) for the one-click menu item.</summary>
    public int DefaultFocusMinutes { get; set; } = 25;

    /// <summary>Show balloon/toast notifications when a focus session starts and ends.</summary>
    public bool ShowFocusNotifications { get; set; } = true;

    /// <summary>Play a sound when a focus session finishes on its own.</summary>
    public bool PlayFocusEndSound { get; set; } = true;

    /// <summary>Optional path to a custom .wav for the end-of-session sound (empty = system sound).</summary>
    public string FocusEndSoundPath { get; set; } = "";

    /// <summary>After a focus session ends, ask what was achieved and log it as a RescueTime highlight.</summary>
    public bool PromptForAchievement { get; set; } = true;

    /// <summary>Periodically remind (during work hours) to start a focus session if none is running.</summary>
    public bool EnableFocusReminders { get; set; } = true;

    /// <summary>How often to remind, in minutes (aligned to the work-day start, e.g. 9:00, 10:00…).</summary>
    public int ReminderIntervalMinutes { get; set; } = 60;

    /// <summary>Start of the work day for reminders, "HH:mm".</summary>
    public string WorkdayStart { get; set; } = "09:00";

    /// <summary>End of the work day for reminders, "HH:mm".</summary>
    public string WorkdayEnd { get; set; } = "17:00";

    /// <summary>Only remind Monday–Friday.</summary>
    public bool RemindOnlyWeekdays { get; set; } = true;

    public TimeSpan WorkdayStartTime => ParseTime(WorkdayStart, new TimeSpan(9, 0, 0));

    public TimeSpan WorkdayEndTime => ParseTime(WorkdayEnd, new TimeSpan(17, 0, 0));

    private static TimeSpan ParseTime(string value, TimeSpan fallback) =>
        TimeSpan.TryParse(value, out TimeSpan t) ? t : fallback;

    private static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RescueTimeStatus");

    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                if (cfg != null)
                {
                    if (cfg.RefreshMinutes < 1)
                    {
                        cfg.RefreshMinutes = 5;
                    }
                    // Focus duration must be a multiple of 5, at least 5.
                    cfg.DefaultFocusMinutes = Math.Max(5, cfg.DefaultFocusMinutes / 5 * 5);
                    if (cfg.ReminderIntervalMinutes < 5)
                    {
                        cfg.ReminderIntervalMinutes = 60;
                    }
                    return cfg;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable config — fall back to defaults.
        }

        return new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
