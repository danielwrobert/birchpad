using System.Text.Json.Nodes;

namespace Birchpad.Core.Sessions;

/// <summary>The fields of an agent hook payload that the session store uses.</summary>
public sealed record HookEvent(
    string Agent,
    string SessionId,
    string EventName,
    string? Cwd = null,
    string? PermissionMode = null,
    string? ToolName = null,
    string? NotificationType = null,
    string? Message = null,
    string? StartSource = null,
    string? Model = null,
    string? SessionTitle = null,
    string? Error = null,
    string? AgentId = null,
    JsonNode? ToolInput = null,
    string? TranscriptPath = null,
    JsonObject? Raw = null)
{
    /// <summary>Parses a Claude Code hook body. Returns null when it has no session ID or event name.</summary>
    public static HookEvent? ParseClaude(string json) => Parse(json, "claude");

    /// <summary>
    /// Parses a hook body from any agent that uses Claude Code's field names; Codex does.
    /// Returns null when it has no session ID or event name.
    /// </summary>
    public static HookEvent? Parse(string json, string agent)
    {
        JsonObject? obj;
        try
        {
            obj = JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (obj is null)
        {
            return null;
        }

        var sessionId = Str(obj, "session_id");
        var eventName = Str(obj, "hook_event_name");
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(eventName))
        {
            return null;
        }

        return new HookEvent(
            Agent: agent,
            SessionId: sessionId,
            EventName: eventName,
            Cwd: Str(obj, "cwd"),
            PermissionMode: Str(obj, "permission_mode"),
            ToolName: Str(obj, "tool_name"),
            NotificationType: Str(obj, "notification_type"),
            Message: Str(obj, "message"),
            StartSource: Str(obj, "source"),
            Model: Str(obj, "model"),
            SessionTitle: Str(obj, "session_title"),
            Error: Str(obj, "error"),
            AgentId: Str(obj, "agent_id"),
            ToolInput: obj["tool_input"],
            TranscriptPath: Str(obj, "transcript_path"),
            Raw: obj);
    }

    private static string? Str(JsonObject obj, string key) =>
        obj.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
