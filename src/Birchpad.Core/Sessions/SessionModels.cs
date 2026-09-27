namespace Birchpad.Core.Sessions;

/// <summary>Where an agent session runs.</summary>
public enum SessionSource
{
    Unknown,
    ClaudeDesktop,
    ClaudeCli,
    Codex,
}

/// <summary>What an agent session is doing, as far as its hooks tell us.</summary>
public enum SessionState
{
    Idle,
    Working,
    NeedsApproval,
    NeedsInput,
    Done,
    Failed,
    Ended,
}

/// <summary>An immutable view of one session, safe to hand to the UI thread.</summary>
public sealed record SessionSnapshot(
    string SessionId,
    string Agent,
    SessionSource Source,
    SessionState State,
    DateTimeOffset StateSince,
    DateTimeOffset LastEventAt,
    string? Cwd,
    string? Title,
    string? Branch,
    string? Model,
    string? PermissionMode,
    string? LastToolName,
    int? Slot)
{
    /// <summary>The short label for a tile: the session title, else the last folder of its cwd.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(this.Title))
            {
                return this.Title!;
            }

            if (string.IsNullOrWhiteSpace(this.Cwd))
            {
                return "Session";
            }

            var trimmed = this.Cwd!.TrimEnd('/');
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }

    /// <summary>True while the session is waiting on the user.</summary>
    public bool IsWaitingOnUser => this.State is SessionState.NeedsApproval or SessionState.NeedsInput;
}
