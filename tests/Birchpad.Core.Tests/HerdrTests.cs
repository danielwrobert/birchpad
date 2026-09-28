using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

using Birchpad.Core.Herdr;

namespace Birchpad.Core.Tests;

public sealed class HerdrTests : IDisposable
{
    // Trimmed from a live herdr 0.9.1 session.snapshot.
    private const string Snapshot = """
        {"agents":[
          {"agent":"codex","agent_status":"done","cwd":"/Users/me/site","foreground_cwd":"/Users/me/site","focused":false,"name":"codex-1","pane_id":"w2:p10","tab_id":"w2:t1","terminal_title_stripped":"codex","workspace_id":"w2"},
          {"agent":"claude","agent_status":"working","cwd":"/Users/me/logi","focused":false,"name":"claude-1","pane_id":"w1:p2","tab_id":"w1:t1","terminal_title_stripped":"claude","workspace_id":"w1"},
          {"agent":"claude","agent_status":"blocked","cwd":"/Users/me/api","focused":false,"pane_id":"w2:p3","tab_id":"w2:t1","workspace_id":"w2"}],
         "focused_pane_id":"w1:p1","focused_tab_id":"w1:t1","focused_workspace_id":"w1",
         "panes":[
          {"agent_status":"unknown","cwd":"/Users/me/logi","focused":true,"pane_id":"w1:p1","tab_id":"w1:t1","workspace_id":"w1"},
          {"agent":"claude","agent_status":"working","cwd":"/Users/me/logi","focused":false,"name":"claude-1","pane_id":"w1:p2","tab_id":"w1:t1","workspace_id":"w1"}],
         "protocol":22,
         "tabs":[
          {"agent_status":"working","focused":true,"label":"1","number":1,"pane_count":2,"tab_id":"w1:t1","workspace_id":"w1"},
          {"agent_status":"blocked","focused":false,"label":"api","number":1,"pane_count":2,"tab_id":"w2:t1","workspace_id":"w2"}],
         "version":"0.9.1",
         "workspaces":[
          {"active_tab_id":"w2:t1","agent_status":"blocked","focused":false,"label":"site","number":2,"pane_count":2,"tab_count":1,"workspace_id":"w2"},
          {"active_tab_id":"w1:t1","agent_status":"working","focused":true,"label":"logi","number":1,"pane_count":2,"tab_count":1,"workspace_id":"w1"}]}
        """;

    private readonly string directory = Path.Combine("/tmp", "bp-herdr-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(this.directory))
        {
            Directory.Delete(this.directory, recursive: true);
        }
    }

    [Fact]
    public void ParsesSnapshot()
    {
        var s = HerdrSnapshot.Parse(JsonNode.Parse(Snapshot));

        Assert.Equal(2, s.Workspaces.Count);
        Assert.Equal(3, s.Agents.Count);
        Assert.Equal("w1:p1", s.FocusedPane?.Id);
        Assert.Equal("logi", s.FocusedPane?.FolderName);
        Assert.Equal(HerdrStatus.Blocked, s.Workspaces[0].Status);
        Assert.Null(s.Agents[2].Name);
    }

    [Fact]
    public void OrdersAgentsByWorkspaceThenPaneNumber()
    {
        var s = HerdrSnapshot.Parse(JsonNode.Parse(Snapshot));

        Assert.Equal(["w1:p2", "w2:p3", "w2:p10"], s.OrderedAgents().Select(a => a.Id));
    }

    [Fact]
    public void BlockedIsMostUrgentThenDone()
    {
        var s = HerdrSnapshot.Parse(JsonNode.Parse(Snapshot));

        Assert.Equal("w2:p3", s.MostUrgent()?.Id);
        Assert.Equal(HerdrStatus.Blocked, s.Overall());

        var calm = s with { Agents = s.Agents.Where(a => a.Status != HerdrStatus.Blocked).ToList() };
        Assert.Equal("w2:p10", calm.MostUrgent()?.Id);
        Assert.Equal(HerdrStatus.Done, calm.Overall());

        var busy = s with { Agents = s.Agents.Where(a => a.Status == HerdrStatus.Working).ToList() };
        Assert.Null(busy.MostUrgent());
        Assert.Equal(HerdrStatus.Unknown, HerdrSnapshot.Empty.Overall());
    }

    [Fact]
    public void FreeAgentNameSkipsTakenNames()
    {
        var s = HerdrSnapshot.Parse(JsonNode.Parse(Snapshot));

        Assert.Equal("claude-2", s.FreeAgentName("claude"));
        Assert.Equal("codex-2", s.FreeAgentName("codex"));
        Assert.Equal("gemini-1", s.FreeAgentName("gemini"));
    }

    [Fact]
    public void FingerprintChangesWithStatus()
    {
        var s = HerdrSnapshot.Parse(JsonNode.Parse(Snapshot));
        var changed = HerdrSnapshot.Parse(JsonNode.Parse(Snapshot.Replace("\"agent_status\":\"working\",\"cwd\":\"/Users/me/logi\",\"focused\":false,\"name\":\"claude-1\",\"pane_id\":\"w1:p2\",\"tab_id\":\"w1:t1\",\"workspace_id\"", "\"agent_status\":\"done\",\"cwd\":\"/Users/me/logi\",\"focused\":false,\"name\":\"claude-1\",\"pane_id\":\"w1:p2\",\"tab_id\":\"w1:t1\",\"workspace_id\"")));

        Assert.NotEqual(s.Fingerprint(), changed.Fingerprint());
    }

    [Fact]
    public void ParsesWorktrees()
    {
        var result = JsonNode.Parse("""
            {"type":"worktree_list","worktrees":[
              {"branch":"main","is_linked_worktree":false,"label":"logi","open_workspace_id":"w1","path":"/Users/me/logi"},
              {"branch":"feature/x","is_linked_worktree":true,"label":"logi-x","open_workspace_id":null,"path":"/Users/me/logi-x"}]}
            """);

        var worktrees = HerdrSnapshot.ParseWorktrees(result);

        Assert.Equal(2, worktrees.Count);
        Assert.Equal("feature/x", worktrees[1].Branch);
        Assert.Null(worktrees[1].OpenWorkspaceId);
        Assert.True(worktrees[1].IsLinked);
    }

    [Fact]
    public void FindsTerminalAppAboveHerdrClient()
    {
        const string ps = """
                1     0 /sbin/launchd
             4788     1 /Applications/iTerm.app/Contents/MacOS/iTerm2
             5265  4788 /Users/me/Library/Application Support/iTerm2/iTermServer-3.7.3
            32043  5265 /usr/bin/login
            32044 32043 -zsh
            41947     1 /opt/homebrew/opt/herdr/bin/herdr
            50000 32044 /opt/homebrew/bin/herdr
            """;

        Assert.Equal("/Applications/iTerm.app", HerdrTerminal.FindHostApp(ps));
        Assert.Null(HerdrTerminal.FindHostApp("    1     0 /sbin/launchd\n41947     1 /opt/homebrew/bin/herdr\n"));
    }

    [Fact]
    public void PrefersDefaultSocketThenNewestSession()
    {
        Directory.CreateDirectory(Path.Combine(this.directory, "sessions", "old"));
        Directory.CreateDirectory(Path.Combine(this.directory, "sessions", "new"));
        File.WriteAllText(Path.Combine(this.directory, "sessions", "old", "herdr.sock"), string.Empty);
        File.WriteAllText(Path.Combine(this.directory, "sessions", "new", "herdr.sock"), string.Empty);
        File.SetLastWriteTimeUtc(Path.Combine(this.directory, "sessions", "old", "herdr.sock"), DateTime.UtcNow.AddHours(-1));

        var candidates = new HerdrClient(socketPath: null, configDirectory: this.directory).Candidates().ToList();

        Assert.Equal(Path.Combine(this.directory, "herdr.sock"), candidates[0]);
        Assert.EndsWith(Path.Combine("new", "herdr.sock"), candidates[1]);
        Assert.EndsWith(Path.Combine("old", "herdr.sock"), candidates[2]);
        Assert.Equal(["/x.sock"], new HerdrClient("/x.sock", this.directory).Candidates());
    }

    [Fact]
    public void RoundTripsOverSocketAndReportsErrors()
    {
        Directory.CreateDirectory(this.directory);
        var path = Path.Combine(this.directory, "herdr.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(8);
        var requests = new List<JsonNode>();
        var server = Task.Run(() =>
        {
            // One connection for the socket probe, then one per request.
            for (var i = 0; i < 3; i++)
            {
                using var connection = listener.Accept();
                var buffer = new byte[65536];
                var read = connection.Receive(buffer);
                if (read == 0)
                {
                    continue;
                }

                var request = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, read).Trim())!;
                lock (requests)
                {
                    requests.Add(request);
                }

                var id = request["id"]!.GetValue<string>();
                var reply = request["method"]!.GetValue<string>() == "session.snapshot"
                    ? $$$"""{"id":"{{{id}}}","result":{"type":"session_snapshot","snapshot":{{{Snapshot.ReplaceLineEndings(" ")}}}}}"""
                    : $$$"""{"id":"{{{id}}}","error":{"code":"pane_not_found","message":"no pane w9:p9"}}""";
                connection.Send(Encoding.UTF8.GetBytes(reply + "\n"));
            }
        });

        var client = new HerdrClient(configDirectory: this.directory);
        var snapshot = client.Snapshot();
        var error = Assert.Throws<HerdrException>(() => client.SendKeys("w9:p9", "esc", "ctrl+c"));
        server.Wait(TimeSpan.FromSeconds(5));

        Assert.Equal(3, snapshot.Agents.Count);
        Assert.Equal("pane_not_found", error.Code);
        Assert.Equal("pane.send_keys", requests[1]["method"]!.GetValue<string>());
        Assert.Equal(["esc", "ctrl+c"], requests[1]["params"]!["keys"]!.AsArray().Select(k => k!.GetValue<string>()));
    }

    [Fact]
    public void KeepsAgentKindsFoundOnPath()
    {
        const string output = "/Users/me/.local/bin/claude\nsetopt: noise\n/opt/homebrew/bin/codex\n";

        Assert.Equal(["claude", "codex"], HerdrAgents.FromCommandV(["pi", "claude", "codex", "gemini"], output));
    }

    [Fact]
    public void SnapshotIsEmptyWhenHerdrIsNotRunning()
    {
        var client = new HerdrClient(configDirectory: this.directory);

        Assert.Null(client.SocketPath);
        Assert.Same(HerdrSnapshot.Empty, client.Snapshot());
    }
}
