using System.Text.Json.Nodes;

using Birchpad.Core.Agents;
using Birchpad.Core.Claude;
using Birchpad.Core.Herdr;
using Birchpad.Core.Sessions;

namespace Birchpad.Core.Tests;

public sealed class AgentsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "bp-agents-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(this.directory))
        {
            Directory.Delete(this.directory, recursive: true);
        }
    }

    private static SessionSnapshot Session(string id, string agent, SessionSource source, SessionState state, string? cwd) =>
        new(id, agent, source, state, Now.AddMinutes(-3), Now, cwd, null, null, null, null, null, null);

    private static HerdrSnapshot Herdr(string agents) => HerdrSnapshot.Parse(JsonNode.Parse($$"""
        {"workspaces":[{"workspace_id":"w1","number":1,"label":"logi","agent_status":"working"}],"tabs":[],"panes":[],
         "agents":{{agents}}}
        """));

    [Fact]
    public void MatchesHerdrPaneBySessionIdAndRoutesThroughHerdr()
    {
        var sessions = new[] { Session("abc", "claude", SessionSource.ClaudeCli, SessionState.Working, "/Users/me/logi") };
        var herdr = Herdr("""[{"pane_id":"w1:p2","workspace_id":"w1","agent":"claude","agent_status":"working","cwd":"/Users/me/other","agent_session":{"agent":"claude","kind":"id","source":"herdr:claude","value":"abc"}}]""");

        var agent = Assert.Single(CodingAgents.Merge(sessions, herdr, Now));

        Assert.Equal(AgentHost.Herdr, agent.Host);
        Assert.Equal("w1:p2", agent.PaneId);
        Assert.Equal("abc", agent.SessionId);
        Assert.Equal("HERDR", agent.HostLabel);
    }

    [Fact]
    public void MatchesByFolderWhenHerdrHasNoSessionId()
    {
        var sessions = new[] { Session("abc", "codex", SessionSource.Codex, SessionState.Idle, "/Users/me/logi/") };
        var herdr = Herdr("""[{"pane_id":"w1:p3","workspace_id":"w1","agent":"codex","agent_status":"blocked","cwd":"/Users/me/logi"}]""");

        var agent = Assert.Single(CodingAgents.Merge(sessions, herdr, Now));

        Assert.Equal("w1:p3", agent.PaneId);
        Assert.Equal(SessionState.NeedsApproval, agent.State);
        Assert.True(agent.IsWaitingOnUser);
    }

    [Fact]
    public void HerdrWorkingClearsAStaleApprovalFromTheHook()
    {
        var sessions = new[] { Session("abc", "claude", SessionSource.ClaudeCli, SessionState.NeedsApproval, "/Users/me/logi") };
        var herdr = Herdr("""[{"pane_id":"w1:p2","workspace_id":"w1","agent":"claude","agent_status":"working","agent_session":{"agent":"claude","kind":"id","source":"herdr:claude","value":"abc"}}]""");

        var agent = Assert.Single(CodingAgents.Merge(sessions, herdr, Now));

        Assert.Equal(SessionState.Working, agent.State);
        Assert.False(agent.IsWaitingOnUser);
    }

    [Fact]
    public void KeepsDesktopSessionsOutOfHerdrAndListsUnmatchedPanes()
    {
        var sessions = new[]
        {
            Session("d1", "claude", SessionSource.ClaudeDesktop, SessionState.Done, "/Users/me/logi"),
            Session("c1", "codex", SessionSource.Codex, SessionState.Working, "/Users/me/site"),
        };
        var herdr = Herdr("""[{"pane_id":"w1:p1","workspace_id":"w1","agent":"claude","agent_status":"done","cwd":"/Users/me/logi","name":"claude-1"}]""");

        var agents = CodingAgents.Merge(sessions, herdr, Now);

        Assert.Equal(3, agents.Count);
        Assert.Equal(AgentHost.ClaudeDesktop, agents[0].Host);
        Assert.Null(agents[0].PaneId);
        Assert.Equal(AgentHost.Codex, agents[1].Host);
        Assert.Equal("h:w1:p1", agents[2].Key);
        Assert.Equal("claude-1", agents[2].Name);
        Assert.Equal(SessionState.Done, agents[2].State);
    }

    [Fact]
    public void MostUrgentPrefersApprovalsThenFinishedAndFiltersByAgent()
    {
        var sessions = new[]
        {
            Session("done", "claude", SessionSource.ClaudeDesktop, SessionState.Done, "/a"),
            Session("codex", "codex", SessionSource.Codex, SessionState.NeedsInput, "/b"),
        };
        var herdr = Herdr("""[{"pane_id":"w1:p1","workspace_id":"w1","agent":"claude","agent_status":"blocked","cwd":"/c"}]""");
        var agents = CodingAgents.Merge(sessions, herdr, Now);

        Assert.Equal("h:w1:p1", CodingAgents.MostUrgent(agents)?.Key);
        Assert.Equal("h:w1:p1", CodingAgents.MostUrgent(agents, "claude")?.Key);
        Assert.Equal("s:codex", CodingAgents.MostUrgent(agents, "codex")?.Key);
        Assert.Equal("s:done", CodingAgents.MostUrgent(agents.Where(a => a.Host != AgentHost.Herdr).ToList(), "claude")?.Key);
        Assert.Null(CodingAgents.MostUrgent([]));
    }

    [Fact]
    public void ReadsCoworkTasksNewestFirstAndSkipsArchived()
    {
        var folder = Path.Combine(this.directory, "account", "org");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "local_a.json"), """{"sessionId":"local_a","cliSessionId":"cli-a","title":"Daily report","lastActivityAt":1790000000000,"isArchived":false,"initialMessage":"secret"}""");
        File.WriteAllText(Path.Combine(folder, "local_b.json"), """{"sessionId":"local_b","title":"Newer","lastActivityAt":1790000500000}""");
        File.WriteAllText(Path.Combine(folder, "local_c.json"), """{"sessionId":"local_c","title":"Old","isArchived":true}""");
        File.WriteAllText(Path.Combine(folder, "local_d.json"), "not json");
        File.WriteAllText(Path.Combine(folder, "cowork-clientdata-cache.json"), "{}");

        var tasks = CoworkTasks.Read(this.directory);

        Assert.Equal(["local_b", "local_a"], tasks.Select(t => t.Id));
        Assert.Equal("cli-a", tasks[1].CliSessionId);
        Assert.Null(tasks[0].CliSessionId);
        Assert.Empty(CoworkTasks.Read(Path.Combine(this.directory, "missing")));
    }
}
