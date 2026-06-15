using System;

namespace RescueTimeStatus;

/// <summary>
/// Pure scheduling helpers for work-hours reminders (kept side-effect-free for testability).
/// </summary>
public static class ReminderSchedule
{
    /// <summary>True if <paramref name="now"/> falls inside the configured work window.</summary>
    public static bool InWorkWindow(DateTime now, AppConfig config)
    {
        if (config.RemindOnlyWeekdays &&
            (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday))
        {
            return false;
        }

        return now.TimeOfDay >= config.WorkdayStartTime && now.TimeOfDay < config.WorkdayEndTime;
    }

    /// <summary>
    /// The most recent reminder slot at or before <paramref name="now"/>, aligned to the
    /// work-day start (e.g. start 09:00 + 60 min → 09:00, 10:00, 11:00…).
    /// </summary>
    public static DateTime CurrentSlot(DateTime now, AppConfig config)
    {
        DateTime workStart = now.Date + config.WorkdayStartTime;
        int interval = Math.Max(5, config.ReminderIntervalMinutes);
        int elapsedMinutes = (int)(now - workStart).TotalMinutes;
        int steps = elapsedMinutes / interval;
        return workStart.AddMinutes(steps * interval);
    }
}
