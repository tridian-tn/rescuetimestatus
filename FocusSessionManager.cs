using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RescueTimeStatus;

/// <summary>Outcome of reconciling local state against the focus feeds.</summary>
public enum FocusReconcileResult
{
    None,
    Adopted,
    EndedEarly,
    Completed,
}

/// <summary>
/// Tracks the local state of a RescueTime focus session and drives a 1-second countdown
/// while one is active. The actual session lives server-side; this is the UI's view of it.
/// </summary>
public sealed class FocusSessionManager : IDisposable
{
    private readonly RescueTimeClient _client;
    private readonly AppConfig _config;
    private readonly System.Windows.Forms.Timer _tick;

    public bool IsActive { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime EndsAt { get; private set; }

    /// <summary>Requested duration in minutes (-1 = until end of day).</summary>
    public int RequestedMinutes { get; private set; }

    /// <summary>Fired on start, on every tick, and on end — the UI should redraw.</summary>
    public event Action? Changed;

    /// <summary>Fired once when the countdown reaches zero on its own.</summary>
    public event Action? Completed;

    public FocusSessionManager(RescueTimeClient client, AppConfig config)
    {
        _client = client;
        _config = config;
        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) => OnTick();
    }

    public TimeSpan Remaining
    {
        get
        {
            if (!IsActive)
            {
                return TimeSpan.Zero;
            }
            TimeSpan r = EndsAt - DateTime.Now;
            return r > TimeSpan.Zero ? r : TimeSpan.Zero;
        }
    }

    public double RemainingFraction
    {
        get
        {
            if (!IsActive)
            {
                return 0;
            }
            double total = (EndsAt - StartedAt).TotalSeconds;
            return total <= 0 ? 0 : Math.Clamp(Remaining.TotalSeconds / total, 0, 1);
        }
    }

    public async Task StartAsync(int durationMinutes)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            throw new RescueTimeException("Set your API key first.");
        }

        await _client.StartFocusAsync(_config.ApiKey, durationMinutes).ConfigureAwait(true);

        StartedAt = DateTime.Now;
        RequestedMinutes = durationMinutes;
        EndsAt = durationMinutes < 0
            ? DateTime.Today.AddDays(1)               // local midnight
            : StartedAt.AddMinutes(durationMinutes);
        IsActive = true;

        _tick.Start();
        Changed?.Invoke();
    }

    public async Task EndAsync()
    {
        if (!IsActive)
        {
            return;
        }

        // Call the API first; only clear local state once the server confirms.
        await _client.EndFocusAsync(_config.ApiKey).ConfigureAwait(true);

        _tick.Stop();
        IsActive = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// Aligns local state with the server feeds: adopts a session running elsewhere, or clears
    /// ours when the server has recorded an end for it. Returns what happened so the caller can
    /// notify appropriately.
    /// </summary>
    public FocusReconcileResult Reconcile(FocusFeedState feed)
    {
        // The feed shows an active session when the latest start has no later end.
        bool feedHasActive = feed.StartedAt is DateTime start &&
                             (feed.EndedAt is not DateTime end || end < start);

        if (!IsActive)
        {
            if (feedHasActive)
            {
                DateTime startedAt = feed.StartedAt!.Value;
                DateTime endsAt = ComputeEndsAt(startedAt, feed.DurationMinutes);
                if (endsAt > DateTime.Now)
                {
                    StartedAt = startedAt;
                    RequestedMinutes = feed.DurationMinutes;
                    EndsAt = endsAt;
                    IsActive = true;
                    _tick.Start();
                    Changed?.Invoke();
                    return FocusReconcileResult.Adopted;
                }
            }
            return FocusReconcileResult.None;
        }

        // We believe a session is active — only clear it if the server recorded an end
        // for *our* session (an end at/after our start), not a stale end from before it.
        if (feed.EndedAt is DateTime endedAt && endedAt >= StartedAt.AddSeconds(-30))
        {
            bool reachedScheduledEnd = DateTime.Now >= EndsAt.AddSeconds(-30);
            _tick.Stop();
            IsActive = false;
            Changed?.Invoke();
            return reachedScheduledEnd ? FocusReconcileResult.Completed : FocusReconcileResult.EndedEarly;
        }

        return FocusReconcileResult.None;
    }

    private static DateTime ComputeEndsAt(DateTime start, int durationMinutes)
    {
        return durationMinutes > 0 ? start.AddMinutes(durationMinutes) : start.Date.AddDays(1);
    }

    private void OnTick()
    {
        if (!IsActive)
        {
            return;
        }

        if (DateTime.Now >= EndsAt)
        {
            _tick.Stop();
            IsActive = false;
            Changed?.Invoke();
            Completed?.Invoke();
        }
        else
        {
            Changed?.Invoke();
        }
    }

    public void Dispose() => _tick.Dispose();
}
