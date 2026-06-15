using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>
/// Owns the tray icon, the refresh timer, focus-session control, and the right-click menu.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext, IStatusController
{
    private static readonly int[] FocusDurations = { 15, 25, 30, 45, 60, 90 };

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _focusPollTimer;
    private readonly System.Windows.Forms.Timer _reminderTimer;
    private readonly RescueTimeClient _client = new();
    private readonly FocusSessionManager _focus;
    private readonly AppConfig _config;

    private Icon? _currentIcon;
    private SoundPlayer? _endSoundPlayer;
    private bool _isRefreshing;
    private bool _isReconciling;

    private int? _lastPulse;
    private double? _lastTotalSeconds;
    private DateTime? _lastUpdated;
    private int _focusSessionsToday;
    private double _focusSecondsToday;

    private ReminderForm? _reminderForm;
    private DateTime _lastReminderSlot = DateTime.MinValue;
    private DateTime? _snoozeUntil;

    private StatusPopup? _popup;
    private DateTime _popupHiddenAt = DateTime.MinValue;
    private Action? _stateChanged;
    private AchievementForm? _achievementForm;

    public TrayApplicationContext()
    {
        _config = AppConfig.Load();

        _focus = new FocusSessionManager(_client, _config);
        _focus.Changed += OnFocusChanged;
        _focus.Completed += OnFocusCompleted;

        _currentIcon = TrayIconRenderer.Render(null);
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Icon = _currentIcon,
            Text = "RescueTime Status — starting…",
        };
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                TogglePopup();
            }
        };
        _notifyIcon.ContextMenuStrip = BuildMenu();

        _timer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMs };
        _timer.Tick += async (_, _) => await RefreshAllAsync();

        // Faster feed poll that only runs while a focus session is active, so external
        // start/stop changes are picked up within ~1 minute instead of a full refresh cycle.
        _focusPollTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _focusPollTimer.Tick += async (_, _) => await ReconcileFocusAsync();

        // Work-hours reminders to start a focus session. Ticks often; self-gates on config/state.
        _reminderTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _reminderTimer.Tick += (_, _) => CheckReminder();

        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _notifyIcon.Text = "RescueTime Status — set your API key";
            PromptForSettings();
        }
        else
        {
            _timer.Start();
            _reminderTimer.Start();
            _ = RefreshAllAsync();
        }
    }

    private int RefreshIntervalMs => Math.Max(1, _config.RefreshMinutes) * 60_000;

    // ----- Menu -------------------------------------------------------------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => PopulateMenu(menu);
        // Pre-fill so the very first right-click has a non-empty, correctly-sized menu.
        // (An initially-empty ContextMenuStrip can fail to show on the first click.)
        PopulateMenu(menu);
        return menu;
    }

    private void PopulateMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        if (_focus.IsActive)
        {
            menu.Items.Add($"End focus session  ({FormatRemaining(_focus.Remaining)} left)", null,
                async (_, _) => await EndFocusAsync());
        }
        else
        {
            var start = new ToolStripMenuItem("Start focus session");
            foreach (int minutes in FocusDurations)
            {
                int captured = minutes;
                start.DropDownItems.Add($"{captured} minutes", null, async (_, _) => await StartFocusAsync(captured));
            }
            start.DropDownItems.Add(new ToolStripSeparator());
            start.DropDownItems.Add("Until end of day", null, async (_, _) => await StartFocusAsync(-1));
            menu.Items.Add(start);

            menu.Items.Add($"Start default ({_config.DefaultFocusMinutes} min)", null,
                async (_, _) => await StartFocusAsync(_config.DefaultFocusMinutes));
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync());
        menu.Items.Add("Open RescueTime dashboard", null, (_, _) => OpenDashboard());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => PromptForSettings());

        var startup = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
        {
            Checked = SafeStartupEnabled(),
        };
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
    }

    // ----- Focus sessions ---------------------------------------------------

    private async Task StartFocusAsync(int minutes)
    {
        try
        {
            await _focus.StartAsync(minutes);
            if (_config.ShowFocusNotifications)
            {
                string label = minutes < 0 ? "until the end of the day" : $"for {minutes} minutes";
                ShowBalloon("Focus session started", $"RescueTime is focusing {label}.", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            ShowBalloon("Couldn't start focus session", ex.Message, ToolTipIcon.Warning);
        }
    }

    private async Task EndFocusAsync()
    {
        try
        {
            await _focus.EndAsync();
            if (_config.ShowFocusNotifications)
            {
                ShowBalloon("Focus session ended", "You ended the session early.", ToolTipIcon.Info);
            }
            PromptAchievement();
        }
        catch (Exception ex)
        {
            ShowBalloon("Couldn't end focus session", ex.Message, ToolTipIcon.Warning);
        }
    }

    private void OnFocusChanged()
    {
        RenderIcon();
        UpdateTooltip();
        SyncFocusPollTimer();

        // A session is now running — no need to keep nagging.
        if (_focus.IsActive)
        {
            CloseReminder();
        }

        RaiseStateChanged();
    }

    // Run the 60-second feed poll only while a session is active. Toggled on transitions
    // only, so an already-running timer's interval is never reset.
    private void SyncFocusPollTimer()
    {
        if (_focus.IsActive && !_focusPollTimer.Enabled)
        {
            _focusPollTimer.Start();
        }
        else if (!_focus.IsActive && _focusPollTimer.Enabled)
        {
            _focusPollTimer.Stop();
        }
    }

    private void OnFocusCompleted()
    {
        RenderIcon();
        UpdateTooltip();
        RaiseStateChanged();
        NotifyCompleted();
        PromptAchievement();
    }

    // Ask what was achieved; if the user enters text, log it to RescueTime as a highlight.
    private void PromptAchievement()
    {
        if (!_config.PromptForAchievement || string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            return;
        }

        if (_achievementForm is { IsDisposed: false } existing)
        {
            existing.Activate();
            return;
        }

        var form = new AchievementForm("Anything you note is saved to RescueTime as a highlight.");
        form.Saved += text => _ = SaveHighlightAsync(text);
        form.FormClosed += (_, _) => _achievementForm = null;
        _achievementForm = form;
        form.Show();
        form.Activate();
    }

    private async Task SaveHighlightAsync(string text)
    {
        try
        {
            await _client.PostHighlightAsync(_config.ApiKey, text, "Focus session");
            if (_config.ShowFocusNotifications)
            {
                ShowBalloon("Highlight saved", text.Length > 60 ? text[..57] + "…" : text, ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            ShowBalloon("Couldn't save highlight", ex.Message, ToolTipIcon.Warning);
        }
    }

    private async Task RestartFocusAsync()
    {
        int minutes = _focus.RequestedMinutes;
        if (minutes == 0)
        {
            minutes = _config.DefaultFocusMinutes;
        }

        try
        {
            await _focus.EndAsync();
        }
        catch
        {
            // If ending fails, still try to start a fresh one below.
        }

        await StartFocusAsync(minutes);
    }

    private void NotifyCompleted()
    {
        if (_config.ShowFocusNotifications)
        {
            string pulse = _lastPulse.HasValue ? $"  Pulse {_lastPulse}%." : "";
            ShowBalloon("Focus session complete", $"Nice work — time for a break.{pulse}", ToolTipIcon.Info);
        }

        if (_config.PlayFocusEndSound)
        {
            PlayEndSound();
        }
    }

    private void PlayEndSound()
    {
        try
        {
            string path = _config.FocusEndSoundPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                _endSoundPlayer?.Dispose();
                _endSoundPlayer = new SoundPlayer(path);
                _endSoundPlayer.Play();
            }
            else
            {
                SystemSounds.Asterisk.Play();
            }
        }
        catch
        {
            SystemSounds.Asterisk.Play();
        }
    }

    // ----- Work-hours reminders --------------------------------------------

    private void CheckReminder()
    {
        if (!_config.EnableFocusReminders || string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            return;
        }

        DateTime now = DateTime.Now;
        if (!ReminderSchedule.InWorkWindow(now, _config))
        {
            return;
        }

        // A pending snooze takes priority over the regular cadence.
        if (_snoozeUntil is DateTime due)
        {
            if (now >= due)
            {
                _snoozeUntil = null;
                if (!_focus.IsActive)
                {
                    ShowReminder();
                }
            }
            return;
        }

        DateTime slot = ReminderSchedule.CurrentSlot(now, _config);
        if (slot <= _lastReminderSlot)
        {
            return; // already handled this slot
        }

        _lastReminderSlot = slot; // consume the slot whether or not we show anything

        // Only fire if we just crossed the slot (avoids a stale popup after the PC wakes late).
        if (now - slot > TimeSpan.FromMinutes(2))
        {
            return;
        }

        if (_focus.IsActive)
        {
            return; // already focusing — skip silently
        }

        ShowReminder();
    }

    private void ShowReminder()
    {
        if (_reminderForm is { IsDisposed: false } existing)
        {
            existing.BringToFront();
            return;
        }

        var form = new ReminderForm(_config.DefaultFocusMinutes);
        form.StartRequested += minutes => _ = StartFocusAsync(minutes);
        form.Snoozed += minutes => _snoozeUntil = DateTime.Now.AddMinutes(minutes);
        form.FormClosed += (_, _) => _reminderForm = null;
        _reminderForm = form;
        form.Show();
    }

    private void CloseReminder()
    {
        if (_reminderForm is { IsDisposed: false } form)
        {
            form.Close();
        }
    }

    // ----- Pulse refresh ----------------------------------------------------

    private async Task RefreshAsync()
    {
        if (_isRefreshing || string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            PulseSnapshot snap = await _client.GetTodayAsync(_config.ApiKey);
            _lastPulse = snap.Pulse;
            _lastTotalSeconds = snap.TotalSeconds;
            _lastUpdated = snap.RetrievedAt;
            RenderIcon();
            UpdateTooltip();
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            RenderIcon(); // keep the last pulse (or dash), plus the focus ring if active
            _notifyIcon.Text = Clip((ex is RescueTimeException ? "RescueTime: " : "Error: ") + ex.Message);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task RefreshAllAsync()
    {
        await RefreshAsync();
        await ReconcileFocusAsync();
    }

    private async Task ReconcileFocusAsync()
    {
        if (_isReconciling || string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            return;
        }

        _isReconciling = true;
        try
        {
            FocusInfo info = await _client.GetFocusAsync(_config.ApiKey);
            _focusSessionsToday = info.Summary.SessionCount;
            _focusSecondsToday = info.Summary.FocusedSeconds;

            FocusReconcileResult result = _focus.Reconcile(info.State);
            switch (result)
            {
                case FocusReconcileResult.Adopted:
                    if (_config.ShowFocusNotifications)
                    {
                        ShowBalloon("Focus session in progress",
                            $"Picked up a running session — {FormatRemaining(_focus.Remaining)} left.", ToolTipIcon.Info);
                    }
                    break;

                case FocusReconcileResult.Completed:
                    NotifyCompleted();
                    break;

                case FocusReconcileResult.EndedEarly:
                    if (_config.ShowFocusNotifications)
                    {
                        ShowBalloon("Focus session ended", "The session was ended from another device.", ToolTipIcon.Info);
                    }
                    break;
            }

            // Adopted/EndedEarly/Completed already raised StateChanged via the focus Changed event.
            // Only raise here when state was unchanged but today's totals may have refreshed.
            if (result == FocusReconcileResult.None)
            {
                RaiseStateChanged();
            }
        }
        catch
        {
            // Feed errors (offline, non-premium) are non-fatal — leave local state as-is.
        }
        finally
        {
            _isReconciling = false;
        }
    }

    // ----- Rendering --------------------------------------------------------

    private void RenderIcon()
    {
        double? fraction = _focus.IsActive ? _focus.RemainingFraction : null;
        Icon newIcon = TrayIconRenderer.Render(_lastPulse, fraction);
        _notifyIcon.Icon = newIcon;

        Icon? old = _currentIcon;
        _currentIcon = newIcon;
        old?.Dispose();
    }

    private void UpdateTooltip()
    {
        string text;
        if (_focus.IsActive)
        {
            string pulse = _lastPulse.HasValue ? $"  •  Pulse {_lastPulse}%" : "";
            text = $"Focus {FormatRemaining(_focus.Remaining)} left{pulse}";
        }
        else if (_lastPulse.HasValue)
        {
            string time = FormatDuration(_lastTotalSeconds ?? 0);
            string when = _lastUpdated.HasValue ? $"  ({_lastUpdated.Value:HH:mm})" : "";
            text = $"Pulse {_lastPulse}%  •  {time} logged today{when}";
        }
        else
        {
            text = "RescueTime Status";
        }

        _notifyIcon.Text = Clip(text);
    }

    // ----- Settings & misc --------------------------------------------------

    private void PromptForSettings()
    {
        using var form = new ApiKeyForm(_config);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _config.ApiKey = form.ApiKey;
        _config.RefreshMinutes = form.RefreshMinutes;
        _config.DefaultFocusMinutes = form.DefaultFocusMinutes;
        _config.ShowFocusNotifications = form.ShowFocusNotifications;
        _config.PlayFocusEndSound = form.PlayFocusEndSound;
        _config.FocusEndSoundPath = form.FocusEndSoundPath;
        _config.PromptForAchievement = form.PromptForAchievement;
        _config.EnableFocusReminders = form.EnableFocusReminders;
        _config.ReminderIntervalMinutes = form.ReminderIntervalMinutes;
        _config.WorkdayStart = form.WorkdayStart;
        _config.WorkdayEnd = form.WorkdayEnd;
        _config.RemindOnlyWeekdays = form.RemindOnlyWeekdays;
        _config.Save();

        _timer.Stop();
        _timer.Interval = RefreshIntervalMs;
        _timer.Start();

        // Re-arm reminders under the new schedule.
        _snoozeUntil = null;
        _lastReminderSlot = DateTime.MinValue;
        _reminderTimer.Start();

        _ = RefreshAllAsync();
    }

    private static bool SafeStartupEnabled()
    {
        try { return StartupManager.IsEnabled(); }
        catch { return false; }
    }

    private void ToggleStartup()
    {
        try
        {
            StartupManager.SetEnabled(!StartupManager.IsEnabled());
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not change the startup setting:\n" + ex.Message,
                "RescueTime Status", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void OpenDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.rescuetime.com/dashboard") { UseShellExecute = true });
        }
        catch
        {
            // Best-effort.
        }
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(5000);
    }

    private static string FormatRemaining(TimeSpan ts)
    {
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes:00}m"
            : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    private static string FormatDuration(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        int hours = (int)ts.TotalHours;
        return hours > 0 ? $"{hours}h {ts.Minutes}m" : $"{ts.Minutes}m";
    }

    // NotifyIcon tooltips are capped at 63 characters.
    private static string Clip(string s) => s.Length <= 63 ? s : s[..60] + "…";

    // ----- Status flyout (single left-click) --------------------------------

    private void TogglePopup()
    {
        if (_popup is { IsDisposed: false, Visible: true })
        {
            _popup.Hide();
            return;
        }

        // If the flyout was just hidden (e.g. this same click deactivated it), don't reopen it.
        if ((DateTime.Now - _popupHiddenAt).TotalMilliseconds < 300)
        {
            return;
        }

        // Recreate if it was never created or got disposed (a user close, e.g. Alt+F4).
        if (_popup is null || _popup.IsDisposed)
        {
            _popup = CreatePopup();
        }
        _popup.ShowNearTray();
    }

    private StatusPopup CreatePopup()
    {
        var popup = new StatusPopup(this);
        popup.Deactivate += (_, _) =>
        {
            _popupHiddenAt = DateTime.Now;
            popup.Hide();
        };
        return popup;
    }

    private void RaiseStateChanged() => _stateChanged?.Invoke();

    int? IStatusController.Pulse => _lastPulse;
    double? IStatusController.TotalSeconds => _lastTotalSeconds;
    DateTime? IStatusController.LastUpdated => _lastUpdated;
    bool IStatusController.FocusActive => _focus.IsActive;
    TimeSpan IStatusController.FocusRemaining => _focus.Remaining;
    double IStatusController.FocusRemainingFraction => _focus.RemainingFraction;
    int IStatusController.FocusRequestedMinutes => _focus.RequestedMinutes;
    int IStatusController.DefaultFocusMinutes => _config.DefaultFocusMinutes;
    int IStatusController.FocusSessionsToday => _focusSessionsToday;
    double IStatusController.FocusSecondsToday => _focusSecondsToday;

    void IStatusController.StartDefaultFocus() => _ = StartFocusAsync(_config.DefaultFocusMinutes);
    void IStatusController.StopFocus() => _ = EndFocusAsync();
    void IStatusController.RestartFocus() => _ = RestartFocusAsync();
    void IStatusController.RefreshNow() => _ = RefreshAllAsync();
    void IStatusController.OpenDashboard() => OpenDashboard();
    void IStatusController.OpenSettings() => PromptForSettings();

    event Action IStatusController.StateChanged
    {
        add => _stateChanged += value;
        remove => _stateChanged -= value;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _focusPollTimer.Dispose();
            _reminderTimer.Dispose();
            _reminderForm?.Dispose();
            _popup?.Dispose();
            _achievementForm?.Dispose();
            _focus.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _currentIcon?.Dispose();
            _endSoundPlayer?.Dispose();
            _client.Dispose();
        }
        base.Dispose(disposing);
    }
}
