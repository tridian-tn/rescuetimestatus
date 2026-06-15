# RescueTime Status

A lightweight Windows system-tray app that shows your **RescueTime Productivity Pulse**
as a number on the tray icon, color-coded by how your day is going. Hover the icon to see
the pulse and **time logged today**, **single-click** for a status flyout with focus controls,
and right-click to **start/stop a Focus Session**.

| Icon color | Pulse  | Meaning            |
|------------|--------|--------------------|
| 🟢 Green   | 75–100 | Very productive    |
| 🟠 Amber   | 50–74  | Mixed              |
| 🔴 Red     | 0–49   | Distracted         |

The tooltip reads, e.g.: `Pulse 82%  •  5h 23m logged today  (14:05)`.

## Status flyout (single-click)

A single left-click on the tray icon opens a small flyout near the tray showing:

- Today's **productivity pulse** (color-coded) and **time logged**, with the last-updated time.
- **Focus controls** that match the current state:
  - *Idle* → a **Start focus · N min** button (uses the default length).
  - *Running* → time remaining, a progress bar, and **Stop** / **Restart** buttons.
- Footer links: **Refresh**, **Dashboard**, **Settings**.

It updates live (counts down while a session runs) and closes when it loses focus or on another
single-click.

## Focus Sessions

Right-click → **Start focus session** (pick a length, or *Until end of day*), or
**Start default** for the configured default length. While a session runs:

- A **blue countdown ring** is drawn on top of the badge — it depletes as the session counts
  down, so the icon doubles as a timer. The pulse color and number stay fully visible.
- The tooltip shows time remaining, e.g. `Focus 12:34 left  •  Pulse 82%`.
- Right-click → **End focus session** stops it early.

When a session finishes, you get a **balloon/toast notification** and a **sound** (both
configurable). Focus Sessions are a **RescueTime Premium** feature — on the free plan the
start call fails and the app shows a notification saying so.

### Feed reconciliation

The app polls the `focustime_started_feed` / `focustime_ended_feed` endpoints (alongside each
pulse refresh and at startup) and aligns the icon with reality:

- **Restores the ring after a restart** — if a session is already running (started here earlier,
  on your phone, or from the desktop app), the icon picks it up with the correct time remaining.
- **Clears the ring if the session ends elsewhere** — stop it from another device and the ring
  disappears on the next refresh (early end = quiet notification; reaching the scheduled end =
  the normal completion toast + sound).

To avoid fighting the ~1-minute desktop sync lag, a freshly started session is only cleared once
the server actually records an end event at/after its start time. While a session is active, the
feed is polled every **60 seconds** so an external stop is caught quickly; when idle, the feed is
checked on the normal pulse refresh (which is when a session started elsewhere gets adopted).

> Note: starting/ending still takes effect on the desktop app's next sync (≤1 minute), so actual
> website-blocking may lag the icon slightly.

## Work-hours reminders

The app can nudge you to start a focus session on a regular cadence during your work day:

- Every `ReminderIntervalMinutes` (default 60), aligned to your work-day start — e.g. 9:00, 10:00,
  11:00…
- **Only if no session is already running** (manual or one picked up via feed reconciliation).
- **Only inside your work hours** (`WorkdayStart`–`WorkdayEnd`, weekdays-only by default).

The reminder is a small top-most popup that **stays until you act on it** — Start (one click,
uses the default length), Snooze 10 min, or Dismiss. (Windows toast balloons auto-dismiss into the
Action Center, so a custom window is used to guarantee it persists.)

> RescueTime's API does not expose your configured work hours, so they're set in this app's
> Settings. A slot is "consumed" once per interval whether or not it's shown, so you won't get a
> late pop-up if a session was running at the top of the hour or if the PC was asleep.

## How it works

It calls the RescueTime [Analytic Data API](https://www.rescuetime.com/anapi/data)
once per refresh for today's date with `restrict_kind=productivity`, then computes the
pulse the same way RescueTime does — a time-weighted average across the five productivity
levels (very distracting → very productive mapped to 0 → 100). Total logged time is the sum
of all seconds returned. Computing it live means the number reflects **today so far**, not
just finalized past days.

## Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (only needed to
  run the framework-dependent build; see "Self-contained" below to avoid it)

## Setup

1. Get your API key from <https://www.rescuetime.com/anapi/manage>.
2. Build and run:
   ```bash
   dotnet build -c Release
   ./bin/Release/net8.0-windows/RescueTimeStatus.exe
   ```
3. On first launch a **Settings** dialog appears — paste your API key and choose a refresh
   interval (default 5 minutes). The key is saved to
   `%APPDATA%\RescueTimeStatus\config.json`.

## Tray menu (right-click)

- **Start focus session ▸** — 15 / 25 / 30 / 45 / 60 / 90 min, or *Until end of day*
- **Start default (N min)** — one-click using the configured default length
- **End focus session (mm:ss left)** — shown instead while a session is active
- **Refresh now** — fetch immediately
- **Open RescueTime dashboard** — opens the web dashboard (also from the flyout footer)
- **Settings…** — API key, refresh interval, default focus length, notifications & sound, reminders
- **Start with Windows** — toggle launch at login (per-user `Run` registry key)
- **Exit**

## Settings

Stored in `%APPDATA%\RescueTimeStatus\config.json`:

| Key | Default | Meaning |
|---|---|---|
| `ApiKey` | — | RescueTime API key |
| `RefreshMinutes` | 5 | How often the pulse is polled |
| `DefaultFocusMinutes` | 25 | Length for the "Start default" item (multiple of 5) |
| `ShowFocusNotifications` | true | Toast on focus start/end |
| `PlayFocusEndSound` | true | Sound when a session finishes |
| `FocusEndSoundPath` | "" | Custom `.wav` (empty = Windows system sound) |
| `EnableFocusReminders` | true | Nudge to start a session during work hours |
| `ReminderIntervalMinutes` | 60 | Reminder cadence, aligned to `WorkdayStart` |
| `WorkdayStart` / `WorkdayEnd` | "09:00" / "17:00" | Work-hours window for reminders |
| `RemindOnlyWeekdays` | true | Don't remind on Saturday/Sunday |

## Build a single-file, self-contained .exe (no runtime install needed)

```bash
dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The result lands in `bin/Release/net8.0-windows/win-x64/publish/RescueTimeStatus.exe`.

## Files

- `Program.cs` — entry point + single-instance guard
- `TrayApplicationContext.cs` — tray icon, refresh timer, focus control, right-click menu
- `RescueTimeClient.cs` — API calls (pulse/time + start/end FocusTime)
- `FocusSessionManager.cs` — focus-session state + 1-second countdown
- `TrayIconRenderer.cs` — draws the number + countdown ring onto the icon
- `ReminderSchedule.cs` — pure work-hours / reminder-slot logic
- `ReminderForm.cs` — the persistent "time to focus?" popup
- `StatusPopup.cs` — the single-click status flyout (`IStatusController` + the form)
- `ApiKeyForm.cs` — first-run / settings dialog
- `AppConfig.cs` — JSON settings in `%APPDATA%`
- `StartupManager.cs` — launch-at-login toggle
