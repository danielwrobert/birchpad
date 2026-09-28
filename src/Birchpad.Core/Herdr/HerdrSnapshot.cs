using System.Text.Json.Nodes;

namespace Birchpad.Core.Herdr;

/// <summary>An agent's lifecycle state as herdr reports it; <see cref="Done"/> means finished and not yet seen.</summary>
public enum HerdrStatus
{
    Unknown,
    Idle,
    Working,
    Blocked,
    Done,
}

public sealed record HerdrWorkspace(string Id, int Number, string Label, bool Focused, int TabCount, string ActiveTabId, HerdrStatus Status);

public sealed record HerdrTab(string Id, string WorkspaceId, int Number, string Label, bool Focused, HerdrStatus Status);

/// <summary>A pane; <see cref="Agent"/> is the detected agent kind (claude, codex, ...) or null for a plain shell.</summary>
public sealed record HerdrPane(
    string Id,
    string WorkspaceId,
    string TabId,
    bool Focused,
    HerdrStatus Status,
    string? Agent,
    string? Name,
    string? Title,
    string? Cwd)
{
    /// <summary>The folder the pane is in, for a key label.</summary>
    public string FolderName => string.IsNullOrEmpty(this.Cwd) ? this.Id : Path.GetFileName(this.Cwd.TrimEnd('/')) is { Length: > 0 } name ? name : this.Cwd;
}

public sealed record HerdrWorktree(string Path, string? Branch, string Label, bool IsLinked, string? OpenWorkspaceId);

/// <summary>One read of herdr's live session: every workspace, tab, pane, and agent.</summary>
public sealed record HerdrSnapshot(
    IReadOnlyList<HerdrWorkspace> Workspaces,
    IReadOnlyList<HerdrTab> Tabs,
    IReadOnlyList<HerdrPane> Panes,
    IReadOnlyList<HerdrPane> Agents,
    string? FocusedWorkspaceId,
    string? FocusedPaneId)
{
    public static readonly HerdrSnapshot Empty = new([], [], [], [], null, null);

    public HerdrPane? FocusedPane => this.Panes.FirstOrDefault(p => p.Id == this.FocusedPaneId);

    public IReadOnlyList<HerdrTab> TabsOf(string workspaceId) =>
        this.Tabs.Where(t => t.WorkspaceId == workspaceId).OrderBy(t => t.Number).ToList();

    /// <summary>Agents in sidebar order: by workspace number, then by pane number within it.</summary>
    public IReadOnlyList<HerdrPane> OrderedAgents()
    {
        var numbers = this.Workspaces.ToDictionary(w => w.Id, w => w.Number);
        return this.Agents
            .OrderBy(a => numbers.TryGetValue(a.WorkspaceId, out var n) ? n : int.MaxValue)
            .ThenBy(a => PaneNumber(a.Id))
            .ToList();
    }

    /// <summary>The agent that most needs you: blocked first, then finished and unseen; null when none is waiting.</summary>
    public HerdrPane? MostUrgent() =>
        this.OrderedAgents()
            .Where(a => a.Status is HerdrStatus.Blocked or HerdrStatus.Done)
            .OrderBy(a => a.Status == HerdrStatus.Blocked ? 0 : 1)
            .FirstOrDefault();

    /// <summary>The most urgent state across all agents, for a summary ring.</summary>
    public HerdrStatus Overall() =>
        this.Agents.Count == 0 ? HerdrStatus.Unknown
        : this.Agents.Any(a => a.Status == HerdrStatus.Blocked) ? HerdrStatus.Blocked
        : this.Agents.Any(a => a.Status == HerdrStatus.Done) ? HerdrStatus.Done
        : this.Agents.Any(a => a.Status == HerdrStatus.Working) ? HerdrStatus.Working
        : HerdrStatus.Idle;

    /// <summary>A name for a new agent of <paramref name="kind"/> that no live agent uses, such as <c>claude-2</c>.</summary>
    public string FreeAgentName(string kind)
    {
        var taken = this.Agents.Select(a => a.Name).Where(n => n is not null).ToHashSet(StringComparer.Ordinal);
        for (var i = 1; ; i++)
        {
            var name = $"{kind}-{i}";
            if (!taken.Contains(name))
            {
                return name;
            }
        }
    }

    /// <summary>A short string that changes whenever anything a key shows changes.</summary>
    public string Fingerprint() => string.Join(
        '|',
        this.Workspaces.Select(w => $"{w.Id}:{w.Label}:{w.Status}:{w.Focused}:{w.TabCount}")
            .Concat(this.Tabs.Select(t => $"{t.Id}:{t.Label}:{t.Status}:{t.Focused}"))
            .Concat(this.Panes.Select(p => $"{p.Id}:{p.Agent}:{p.Name}:{p.Status}:{p.Focused}:{p.Cwd}")));

    /// <summary>Reads the <c>snapshot</c> object of a <c>session.snapshot</c> result.</summary>
    public static HerdrSnapshot Parse(JsonNode? snapshot)
    {
        if (snapshot is not JsonObject s)
        {
            return Empty;
        }

        var workspaces = Items(s["workspaces"]).Select(w => new HerdrWorkspace(
            Text(w, "workspace_id") ?? string.Empty,
            Number(w, "number"),
            Text(w, "label") ?? string.Empty,
            Flag(w, "focused"),
            Number(w, "tab_count"),
            Text(w, "active_tab_id") ?? string.Empty,
            StatusOf(w))).ToList();
        var tabs = Items(s["tabs"]).Select(t => new HerdrTab(
            Text(t, "tab_id") ?? string.Empty,
            Text(t, "workspace_id") ?? string.Empty,
            Number(t, "number"),
            Text(t, "label") ?? string.Empty,
            Flag(t, "focused"),
            StatusOf(t))).ToList();
        var panes = Items(s["panes"]).Select(PaneOf).ToList();
        var agents = Items(s["agents"]).Select(PaneOf).ToList();
        return new HerdrSnapshot(workspaces, tabs, panes, agents, Text(s, "focused_workspace_id"), Text(s, "focused_pane_id"));
    }

    /// <summary>Reads a <c>worktree.list</c> result.</summary>
    public static IReadOnlyList<HerdrWorktree> ParseWorktrees(JsonNode? result) =>
        Items(result?["worktrees"]).Select(w => new HerdrWorktree(
            Text(w, "path") ?? string.Empty,
            Text(w, "branch"),
            Text(w, "label") ?? string.Empty,
            Flag(w, "is_linked_worktree"),
            Text(w, "open_workspace_id"))).Where(w => w.Path.Length > 0).ToList();

    public static HerdrStatus ParseStatus(string? status) => status switch
    {
        "idle" => HerdrStatus.Idle,
        "working" => HerdrStatus.Working,
        "blocked" => HerdrStatus.Blocked,
        "done" => HerdrStatus.Done,
        _ => HerdrStatus.Unknown,
    };

    private static HerdrPane PaneOf(JsonObject p) => new(
        Text(p, "pane_id") ?? string.Empty,
        Text(p, "workspace_id") ?? string.Empty,
        Text(p, "tab_id") ?? string.Empty,
        Flag(p, "focused"),
        StatusOf(p),
        Text(p, "agent"),
        Text(p, "name"),
        Text(p, "terminal_title_stripped") ?? Text(p, "title"),
        Text(p, "foreground_cwd") ?? Text(p, "cwd"));

    // "w1:p12" sorts as 12, so p10 comes after p2.
    private static int PaneNumber(string paneId)
    {
        var at = paneId.LastIndexOf(":p", StringComparison.Ordinal);
        return at >= 0 && int.TryParse(paneId.AsSpan(at + 2), out var n) ? n : int.MaxValue;
    }

    private static IEnumerable<JsonObject> Items(JsonNode? array) =>
        array is JsonArray a ? a.OfType<JsonObject>() : [];

    private static HerdrStatus StatusOf(JsonObject o) => ParseStatus(Text(o, "agent_status"));

    private static string? Text(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static int Number(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

    private static bool Flag(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}
