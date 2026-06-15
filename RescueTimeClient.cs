using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RescueTimeStatus;

/// <summary>A snapshot of today's RescueTime stats.</summary>
public sealed record PulseSnapshot(int Pulse, double TotalSeconds, DateTime RetrievedAt);

/// <summary>The latest start/end derived from the focus-session feeds.</summary>
public sealed record FocusFeedState(DateTime? StartedAt, int DurationMinutes, DateTime? EndedAt);

public sealed class RescueTimeException : Exception
{
    public RescueTimeException(string message) : base(message) { }
}

/// <summary>
/// Talks to the RescueTime Analytic Data API and derives the Productivity Pulse
/// and total logged time for the current day.
///
/// We compute the pulse ourselves (rather than reading the daily-summary feed) so the
/// number is live for *today* — the summary feed is only finalized after a day ends.
/// </summary>
public sealed class RescueTimeClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<PulseSnapshot> GetTodayAsync(string apiKey, CancellationToken ct = default)
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // perspective=interval + restrict_kind=productivity returns one row per
        // productivity level (-2..2) with seconds spent, aggregated for the day.
        string url =
            "https://www.rescuetime.com/anapi/data" +
            "?key=" + Uri.EscapeDataString(apiKey) +
            "&perspective=interval" +
            "&restrict_kind=productivity" +
            "&interval=day" +
            "&restrict_begin=" + today +
            "&restrict_end=" + today +
            "&format=json";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new RescueTimeException("Network error: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            throw new RescueTimeException("Request to RescueTime timed out.");
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new RescueTimeException("Invalid API key.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new RescueTimeException($"RescueTime returned HTTP {(int)response.StatusCode}.");
            }

            return Parse(body);
        }
    }

    private static PulseSnapshot Parse(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // RescueTime returns a plain-text error (e.g. "key not found") on bad keys.
            throw new RescueTimeException(body.Length > 0 ? body.Trim() : "Empty response from RescueTime.");
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("rows", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
            {
                throw new RescueTimeException("Unexpected response shape from RescueTime.");
            }

            double totalSeconds = 0;
            double weightedSeconds = 0;

            foreach (JsonElement row in rows.EnumerateArray())
            {
                // Row layout for restrict_kind=productivity:
                //   [ Rank, Time Spent (seconds), Number of People, Productivity (-2..2) ]
                double seconds = row[1].GetDouble();
                int level = row[3].GetInt32();

                // Pulse weight: -2->0, -1->25, 0->50, 1->75, 2->100.
                double weightPercent = (level + 2) * 25.0;

                totalSeconds += seconds;
                weightedSeconds += seconds * weightPercent;
            }

            int pulse = totalSeconds > 0
                ? (int)Math.Round(weightedSeconds / totalSeconds, MidpointRounding.AwayFromZero)
                : 0;

            return new PulseSnapshot(pulse, totalSeconds, DateTime.Now);
        }
    }

    /// <summary>Starts a FocusTime session. duration must be a multiple of 5, or -1 for "until end of day".</summary>
    public Task StartFocusAsync(string apiKey, int durationMinutes, CancellationToken ct = default)
    {
        string url =
            "https://www.rescuetime.com/anapi/start_focustime" +
            "?key=" + Uri.EscapeDataString(apiKey) +
            "&duration=" + durationMinutes.ToString(CultureInfo.InvariantCulture);
        return PostAsync(url, "Start focus session", premiumHintOn400: true, ct);
    }

    /// <summary>Ends the currently active FocusTime session.</summary>
    public Task EndFocusAsync(string apiKey, CancellationToken ct = default)
    {
        string url =
            "https://www.rescuetime.com/anapi/end_focustime" +
            "?key=" + Uri.EscapeDataString(apiKey);
        return PostAsync(url, "End focus session", premiumHintOn400: false, ct);
    }

    private async Task PostAsync(string url, string action, bool premiumHintOn400, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(url, content: null, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new RescueTimeException("Network error: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            throw new RescueTimeException($"{action}: request timed out.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string body = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new RescueTimeException("Invalid API key.");
            }

            // 400 is what RescueTime returns for a non-premium account or an invalid duration.
            string detail = body.Length is > 0 and < 160 ? $" ({body})" : "";
            string hint = premiumHintOn400 && response.StatusCode == HttpStatusCode.BadRequest
                ? " This usually means RescueTime Premium is required, or the duration was invalid."
                : "";
            throw new RescueTimeException($"{action} failed (HTTP {(int)response.StatusCode}).{detail}{hint}");
        }
    }

    /// <summary>Reads the started/ended focus feeds and reduces them to the latest start + latest end.</summary>
    public async Task<FocusFeedState> GetFocusFeedStateAsync(string apiKey, CancellationToken ct = default)
    {
        List<FocusFeedEntry> started = await GetFeedAsync(apiKey, "focustime_started_feed", ct).ConfigureAwait(false);
        List<FocusFeedEntry> ended = await GetFeedAsync(apiKey, "focustime_ended_feed", ct).ConfigureAwait(false);

        FocusFeedEntry? latestStart = Latest(started);
        FocusFeedEntry? latestEnd = Latest(ended);

        return new FocusFeedState(latestStart?.Time, latestStart?.DurationMinutes ?? 0, latestEnd?.Time);
    }

    private static FocusFeedEntry? Latest(List<FocusFeedEntry> entries)
    {
        FocusFeedEntry? latest = null;
        foreach (FocusFeedEntry e in entries)
        {
            if (latest is null || e.Time > latest.Time)
            {
                latest = e;
            }
        }
        return latest;
    }

    private async Task<List<FocusFeedEntry>> GetFeedAsync(string apiKey, string endpoint, CancellationToken ct)
    {
        string url = $"https://www.rescuetime.com/anapi/{endpoint}?key={Uri.EscapeDataString(apiKey)}&format=json";

        using HttpResponseMessage resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new RescueTimeException("Invalid API key.");
        }

        var list = new List<FocusFeedEntry>();
        if (!resp.IsSuccessStatusCode)
        {
            // Non-premium accounts return no usable feed; treat as empty rather than failing.
            return list;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (JsonElement el in doc.RootElement.EnumerateArray())
            {
                DateTime? time = ParseFeedTime(el);
                if (time is null)
                {
                    continue;
                }

                int duration = el.TryGetProperty("duration", out JsonElement d) && d.ValueKind == JsonValueKind.Number
                    ? d.GetInt32()
                    : 0;

                list.Add(new FocusFeedEntry(time.Value, duration));
            }
        }
        catch (JsonException)
        {
            // Empty or non-JSON body (e.g. Lite plan) — return what we have.
        }

        return list;
    }

    private static DateTime? ParseFeedTime(JsonElement el)
    {
        if (el.TryGetProperty("created_at", out JsonElement c) && c.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(c.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime dt))
        {
            return dt;
        }

        // Fall back to the id, which is a UNIX timestamp for the event.
        if (el.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.Number)
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)idEl.GetDouble()).LocalDateTime;
        }

        return null;
    }

    private sealed record FocusFeedEntry(DateTime Time, int DurationMinutes);

    public void Dispose() => _http.Dispose();
}
