using System.Text.Json.Nodes;

namespace Birchpad.Core.Usage;

/// <summary>
/// Holds the latest Claude plan limits and per-session numbers from status line updates.
/// Thread-safe; <see cref="Changed"/> fires outside the lock.
/// </summary>
public sealed class UsageStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, ClaudeSessionUsage> sessions = new(StringComparer.Ordinal);
    private ClaudeLimits? limits;

    public event EventHandler? Changed;

    public ClaudeLimits? Limits
    {
        get
        {
            lock (this.gate)
            {
                return this.limits;
            }
        }
    }

    public ClaudeSessionUsage? ForSession(string sessionId)
    {
        lock (this.gate)
        {
            return this.sessions.GetValueOrDefault(sessionId);
        }
    }

    /// <summary>The most recently updated session's numbers, for keys that show "this session".</summary>
    public ClaudeSessionUsage? Latest()
    {
        lock (this.gate)
        {
            return this.sessions.Values.OrderByDescending(u => u.UpdatedAt).FirstOrDefault();
        }
    }

    /// <summary>Reads limits and session numbers from one Claude Code status line payload.</summary>
    public void ApplyStatusLine(JsonObject statusLine, DateTimeOffset now)
    {
        var sessionId = Str(statusLine["session_id"]);
        var fiveHour = Window(statusLine["rate_limits"]?["five_hour"]);
        var sevenDay = Window(statusLine["rate_limits"]?["seven_day"]);
        var session = new ClaudeSessionUsage(
            Num(statusLine["context_window"]?["used_percentage"]),
            Num(statusLine["cost"]?["total_cost_usd"]),
            (int?)Num(statusLine["cost"]?["total_lines_added"]),
            (int?)Num(statusLine["cost"]?["total_lines_removed"]),
            now);

        lock (this.gate)
        {
            if (fiveHour is not null || sevenDay is not null)
            {
                this.limits = new ClaudeLimits(fiveHour ?? this.limits?.FiveHour, sevenDay ?? this.limits?.SevenDay, now, "status line");
            }

            if (!string.IsNullOrEmpty(sessionId))
            {
                this.sessions[sessionId] = session;
                this.Trim(now);
            }
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Uses Claude Desktop's recorded limits when no status line has reported newer ones.</summary>
    public void ApplyFallback(ClaudeLimits fallback)
    {
        lock (this.gate)
        {
            if (this.limits is not null && this.limits.UpdatedAt >= fallback.UpdatedAt)
            {
                return;
            }

            this.limits = fallback;
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>How long a session's numbers are kept after its last status line.</summary>
    public TimeSpan KeepFor { get; init; } = TimeSpan.FromHours(8);

    /// <summary>The most sessions kept; the oldest go first.</summary>
    public int MaxSessions { get; init; } = 64;

    internal int SessionCount
    {
        get
        {
            lock (this.gate)
            {
                return this.sessions.Count;
            }
        }
    }

    // Caller holds gate.
    private void Trim(DateTimeOffset now)
    {
        var doomed = this.sessions
            .Where(kv => now - kv.Value.UpdatedAt >= this.KeepFor)
            .Select(kv => kv.Key)
            .Concat(this.sessions.OrderByDescending(kv => kv.Value.UpdatedAt).Skip(this.MaxSessions).Select(kv => kv.Key))
            .Distinct()
            .ToList();
        doomed.ForEach(id => this.sessions.Remove(id));
    }

    private static LimitWindow? Window(JsonNode? node)
    {
        var used = Num(node?["used_percentage"]);
        if (used is null)
        {
            return null;
        }

        var resets = Num(node?["resets_at"]);
        return new LimitWindow(used.Value, resets is null ? null : DateTimeOffset.FromUnixTimeSeconds((long)resets.Value));
    }

    private static double? Num(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
