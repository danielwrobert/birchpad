namespace Birchpad.Core.Sessions;

/// <summary>Maps hook events to session states.</summary>
public static class SessionStateMachine
{
    // Tools that stop and wait for the user without a permission prompt.
    private static readonly HashSet<string> QuestionTools = new(StringComparer.Ordinal) { "AskUserQuestion", "ExitPlanMode" };

    /// <summary>Returns the state after an event, or null when the event does not change it.</summary>
    public static SessionState? Next(SessionState current, HookEvent e) => e.EventName switch
    {
        "SessionStart" => SessionState.Idle,
        "UserPromptSubmit" => SessionState.Working,
        "PreToolUse" when e.ToolName is not null && QuestionTools.Contains(e.ToolName) => SessionState.NeedsInput,
        "PreToolUse" or "PostToolUse" or "PostToolUseFailure" or "PostToolBatch" => SessionState.Working,
        "SubagentStart" or "SubagentStop" or "PreCompact" or "PostCompact" => SessionState.Working,
        "PermissionRequest" => SessionState.NeedsApproval,
        "Notification" => FromNotification(current, e.NotificationType),
        "Stop" => SessionState.Done,

        // Codex runs Interrupt when the user stops a turn; the session then waits for input.
        "Interrupt" => SessionState.Done,
        "StopFailure" => SessionState.Failed,
        "SessionEnd" => SessionState.Ended,
        _ => null,
    };

    private static SessionState? FromNotification(SessionState current, string? type) => type switch
    {
        "permission_prompt" => SessionState.NeedsApproval,
        "elicitation_dialog" or "elicitation_url_dialog" or "agent_needs_input" => SessionState.NeedsInput,
        "agent_completed" => SessionState.Done,

        // Idle fires after the prompt has waited a while; it must not hide a pending approval or question.
        "idle_prompt" when current is SessionState.Working => SessionState.Done,
        _ => null,
    };
}
