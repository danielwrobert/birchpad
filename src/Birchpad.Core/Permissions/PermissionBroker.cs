using System.Text.Json.Nodes;

namespace Birchpad.Core.Permissions;

/// <summary>An answer given from the keypad.</summary>
public enum PermissionAnswer
{
    Allow,
    AllowAlways,
    Deny,
}

/// <summary>A permission request held open while it waits for a keypad answer.</summary>
public sealed record PendingPermission(string RequestId, string SessionId, string? ToolName, DateTimeOffset ReceivedAt);

/// <summary>
/// Holds PermissionRequest hooks open until the keypad answers, the session moves on,
/// or the wait times out. A timeout or cancel returns null, which leaves Claude's own prompt in charge.
/// </summary>
public sealed class PermissionBroker
{
    private readonly object gate = new();
    private readonly List<Entry> pending = new();

    /// <summary>Raised when a request is added or removed.</summary>
    public event EventHandler? Changed;

    public int PendingCount
    {
        get
        {
            lock (this.gate)
            {
                return this.pending.Count;
            }
        }
    }

    public IReadOnlyList<PendingPermission> Pending()
    {
        lock (this.gate)
        {
            return this.pending.Select(e => e.Info).ToList();
        }
    }

    /// <summary>
    /// Waits for an answer to one PermissionRequest hook body.
    /// Returns the hook response JSON, or null when nobody answered.
    /// </summary>
    public async Task<string?> WaitAsync(string sessionId, JsonObject request, TimeSpan timeout, DateTimeOffset now, CancellationToken cancel = default, string agent = "claude")
    {
        var entry = new Entry(
            new PendingPermission(Guid.NewGuid().ToString("N"), sessionId, Str(request, "tool_name"), now),
            request);
        lock (this.gate)
        {
            this.pending.Add(entry);
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var answer = await entry.Answer.Task.WaitAsync(timeout, cancel).ConfigureAwait(false);
            return answer is null ? null : BuildResponse(answer.Value, request, agent);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return null;
        }
        finally
        {
            this.Remove(entry);
        }
    }

    /// <summary>Answers the oldest pending request. Returns false when none is pending.</summary>
    public bool AnswerOldest(PermissionAnswer answer)
    {
        Entry? entry;
        lock (this.gate)
        {
            entry = this.pending.FirstOrDefault();
        }

        return entry is not null && this.Resolve(entry, answer);
    }

    /// <summary>Answers the oldest pending request for one session. Returns false when it has none.</summary>
    public bool AnswerSession(string sessionId, PermissionAnswer answer)
    {
        Entry? entry;
        lock (this.gate)
        {
            entry = this.pending.FirstOrDefault(e => e.Info.SessionId == sessionId);
        }

        return entry is not null && this.Resolve(entry, answer);
    }

    /// <summary>
    /// Releases every request for a session without an answer, for when the user
    /// answered in the app and the session moved on.
    /// </summary>
    public void ReleaseSession(string sessionId)
    {
        List<Entry> released;
        lock (this.gate)
        {
            released = this.pending.Where(e => e.Info.SessionId == sessionId).ToList();
        }

        released.ForEach(e => this.Resolve(e, null));
    }

    /// <summary>True while the session has a request waiting for a keypad answer.</summary>
    public bool HasPending(string sessionId)
    {
        lock (this.gate)
        {
            return this.pending.Any(e => e.Info.SessionId == sessionId);
        }
    }

    /// <summary>
    /// Releases the held request that a finished or denied tool call answered: same session, same subagent,
    /// same tool, and the same input. When no input matches but exactly one request for that tool is held,
    /// that one is released, since the app may have changed the input. Other held requests stay put.
    /// </summary>
    public void ReleaseMatching(string sessionId, string? agentId, string? toolName, JsonNode? toolInput)
    {
        Entry? match;
        lock (this.gate)
        {
            var candidates = this.pending
                .Where(e => e.Info.SessionId == sessionId
                         && Str(e.Request, "agent_id") == agentId
                         && e.Info.ToolName == toolName)
                .ToList();
            match = candidates.FirstOrDefault(e => JsonNode.DeepEquals(e.Request["tool_input"], toolInput))
                ?? (candidates.Count == 1 ? candidates[0] : null);
        }

        if (match is not null)
        {
            this.Resolve(match, null);
        }
    }

    /// <summary>Builds the Claude Code hook response for an answer.</summary>
    public static string BuildResponse(PermissionAnswer answer, JsonObject request, string agent = "claude")
    {
        var decision = new JsonObject();
        switch (answer)
        {
            case PermissionAnswer.Allow:
                decision["behavior"] = "allow";
                break;
            case PermissionAnswer.AllowAlways:
                decision["behavior"] = "allow";

                // Echo Claude's own suggestion, so "always" means what the dialog's "always" would.
                // Codex fails closed on updatedPermissions, so there "always" is a plain allow.
                if (agent == "claude" && request["permission_suggestions"] is JsonArray suggestions && suggestions.Count > 0)
                {
                    decision["updatedPermissions"] = suggestions.DeepClone();
                }

                break;
            case PermissionAnswer.Deny:
                decision["behavior"] = "deny";
                decision["message"] = "Denied from the keypad.";
                break;
        }

        var response = new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PermissionRequest",
                ["decision"] = decision,
            },
        };
        return response.ToJsonString();
    }

    // Takes the entry off the list before waking its waiter, so HasPending and PendingCount
    // are already right when the caller looks next.
    private bool Resolve(Entry entry, PermissionAnswer? answer)
    {
        this.Remove(entry);
        return entry.Answer.TrySetResult(answer);
    }

    private void Remove(Entry entry)
    {
        bool removed;
        lock (this.gate)
        {
            removed = this.pending.Remove(entry);
        }

        if (removed)
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string? Str(JsonObject obj, string key) =>
        obj.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private sealed class Entry(PendingPermission info, JsonObject request)
    {
        public PendingPermission Info { get; } = info;

        public JsonObject Request { get; } = request;

        public TaskCompletionSource<PermissionAnswer?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
