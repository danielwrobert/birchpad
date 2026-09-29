using Birchpad.Core.Herdr;
using Birchpad.Core.Sessions;

namespace Birchpad.Core.Agents;

/// <summary>Where a coding agent runs, which decides how the keypad reaches it.</summary>
public enum AgentHost
{
    /// <summary>A herdr pane: focused and typed into through herdr's socket, with no window focus needed.</summary>
    Herdr,

    /// <summary>A Claude Code CLI in an iTerm tab, found by its folder.</summary>
    Terminal,

    /// <summary>A Claude Desktop Code session.</summary>
    ClaudeDesktop,

    /// <summary>A Codex session: a CLI in an iTerm tab when one is in its folder, otherwise a ChatGPT app thread.</summary>
    Codex,
}

/// <summary>One coding agent on the keypad, from Agent Hub's hooks, herdr, or both.</summary>
public sealed record CodingAgent(
    string Key,
    string Agent,
    AgentHost Host,
    SessionState State,
    DateTimeOffset StateSince,
    string Name,
    string? Cwd,
    string? SessionId,
    string? PaneId)
{
    public bool IsWaitingOnUser => this.State is SessionState.NeedsApproval or SessionState.NeedsInput;

    /// <summary>The small label above the name: where the agent runs.</summary>
    public string HostLabel => this.Host switch
    {
        AgentHost.Herdr => "HERDR",
        AgentHost.ClaudeDesktop => "DESKTOP",
        AgentHost.Codex => "CODEX",
        _ => "CLI",
    };
}

/// <summary>Merges Agent Hub sessions with herdr's agents into one list, one key per agent.</summary>
public static class CodingAgents
{
    /// <summary>
    /// A session herdr also hosts is one agent that herdr reaches; herdr matches by the session ID its integration reports,
    /// then by agent kind and folder. herdr agents with no hook session are listed too, with herdr's status.
    /// </summary>
    public static IReadOnlyList<CodingAgent> Merge(IReadOnlyList<SessionSnapshot> sessions, HerdrSnapshot herdr, DateTimeOffset now)
    {
        var unmatched = herdr.OrderedAgents().ToList();
        var result = new List<CodingAgent>();
        foreach (var session in sessions)
        {
            var pane = unmatched.FirstOrDefault(p => p.SessionId != null && p.SessionId == session.SessionId)
                ?? (session.Source == SessionSource.ClaudeDesktop ? null : unmatched.FirstOrDefault(p =>
                    p.SessionId == null && p.Agent == session.Agent && SameFolder(p.Cwd, session.Cwd)));
            if (pane != null)
            {
                unmatched.Remove(pane);
            }

            var host = pane != null ? AgentHost.Herdr : session.Source switch
            {
                SessionSource.ClaudeDesktop => AgentHost.ClaudeDesktop,
                SessionSource.Codex => AgentHost.Codex,
                _ => AgentHost.Terminal,
            };

            // herdr reads the screen, so it sees an approval prompt even when no hook reported one.
            var state = pane?.Status == HerdrStatus.Blocked && !session.IsWaitingOnUser ? SessionState.NeedsApproval : session.State;
            result.Add(new CodingAgent("s:" + session.SessionId, session.Agent, host, state, session.StateSince, session.DisplayName, session.Cwd, session.SessionId, pane?.Id));
        }

        foreach (var pane in unmatched)
        {
            var state = pane.Status switch
            {
                HerdrStatus.Working => SessionState.Working,
                HerdrStatus.Blocked => SessionState.NeedsApproval,
                HerdrStatus.Done => SessionState.Done,
                _ => SessionState.Idle,
            };
            result.Add(new CodingAgent("h:" + pane.Id, pane.Agent ?? "agent", AgentHost.Herdr, state, now, pane.Name ?? pane.FolderName, pane.Cwd, pane.SessionId, pane.Id));
        }

        return result;
    }

    /// <summary>
    /// The agent that most needs you, from <paramref name="agent"/> ("claude", "codex") or every agent when null:
    /// approvals first, then questions, oldest wait first, then the most recently finished.
    /// </summary>
    public static CodingAgent? MostUrgent(IReadOnlyList<CodingAgent> agents, string? agent = null)
    {
        var candidates = agents.Where(a => agent is null || a.Agent == agent).ToList();
        return candidates
                .Where(a => a.IsWaitingOnUser)
                .OrderBy(a => a.State == SessionState.NeedsApproval ? 0 : 1)
                .ThenBy(a => a.StateSince)
                .FirstOrDefault()
            ?? candidates
                .Where(a => a.State == SessionState.Done)
                .OrderByDescending(a => a.StateSince)
                .FirstOrDefault();
    }

    private static bool SameFolder(string? a, string? b) =>
        a != null && b != null && string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.Ordinal);
}
