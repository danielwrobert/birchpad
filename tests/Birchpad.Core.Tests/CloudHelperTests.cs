using Birchpad.Core.Cloud;

namespace Birchpad.Core.Tests;

public sealed class CloudHelperTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly string dir = Path.Combine(Path.GetTempPath(), "birchpad-cloud-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.dir))
        {
            Directory.Delete(this.dir, recursive: true);
        }
    }

    private const string Stream = """
        {"type":"system","subtype":"init"}
        {"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"RemoteTrigger","input":{"action":"list"}},{"type":"tool_use","id":"t2","name":"ListAgents","input":{}}]}}
        {"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"HTTP 200\n{\"data\":[{\"id\":\"trig_1\",\"name\":\"Nightly review\",\"cron_expression\":\"0 3 * * *\",\"enabled\":true,\"next_run_at\":\"2026-09-27T07:00:00Z\"}],\"has_more\":false}"}]}}
        {"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t2","content":[{"type":"text","text":"This session is scratchpad-73 [a7c224].\n\nPeer sessions (2):\n  logi-94 [9cc107]  ·  interactive  ·  busy  ·  started 50m ago\n  laptop-review [d0ffc9]  ·  remote-control  ·  waiting"}]}]}}
        {"type":"result","is_error":false,"result":"done"}
        """;

    [Fact]
    public void Parse_reads_routines_and_agents_from_tool_results()
    {
        var snapshot = CloudHelper.Parse(Stream, T0);

        Assert.Null(snapshot.Error);
        var routine = Assert.Single(snapshot.Routines);
        Assert.Equal(("trig_1", "Nightly review", "0 3 * * *", true), (routine.Id, routine.Name, routine.Schedule, routine.Enabled));
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 7, 0, 0, TimeSpan.Zero), routine.NextRunAt);

        Assert.Equal(2, snapshot.Agents.Count);
        Assert.Equal(new CloudAgent("logi-94", "9cc107", "interactive", "busy", "started 50m ago"), snapshot.Agents[0]);
        Assert.Equal(new CloudAgent("laptop-review", "d0ffc9", "remote-control", "waiting", null), snapshot.Agents[1]);
    }

    [Fact]
    public void Parse_without_tool_results_is_a_failure()
    {
        var snapshot = CloudHelper.Parse("""{"type":"result","is_error":true,"result":"Not logged in"}""", T0);

        Assert.Equal("Not logged in", snapshot.Error);
        Assert.Empty(snapshot.Routines);
    }

    [Fact]
    public void Arguments_skip_user_settings_and_limit_tools()
    {
        var args = new CloudHelper("/bin/claude", this.dir).Arguments("/tmp/empty.json");

        Assert.Equal(string.Empty, args[args.ToList().IndexOf("--setting-sources") + 1]);
        Assert.Equal("ToolSearch,RemoteTrigger,ListAgents", args[args.ToList().IndexOf("--tools") + 1]);
        Assert.Contains("--strict-mcp-config", args);
    }

    [Fact]
    public void Cache_keeps_the_last_good_lists_when_a_run_fails()
    {
        var file = Path.Combine(this.dir, "cache.json");
        var results = new Queue<CloudSnapshot>([CloudHelper.Parse(Stream, T0), CloudSnapshot.Failed(T0.AddMinutes(30), "offline")]);
        using var cache = new CloudCache(_ => results.Dequeue(), file, TimeSpan.FromMinutes(30), () => T0);

        cache.RefreshNow();
        cache.RefreshNow();

        Assert.Single(cache.Latest!.Routines);
        Assert.Equal("offline", cache.Latest.Error);
    }

    [Fact]
    public void Cache_reloads_the_last_good_result_from_disk()
    {
        var file = Path.Combine(this.dir, "cache.json");
        using (var first = new CloudCache(_ => CloudHelper.Parse(Stream, T0), file, TimeSpan.FromMinutes(30), () => T0))
        {
            first.RefreshNow();
        }

        using var second = new CloudCache(_ => throw new InvalidOperationException("should not run"), file, TimeSpan.FromMinutes(30), () => T0);

        Assert.Equal("Nightly review", Assert.Single(second.Latest!.Routines).Name);
        Assert.Equal(2, second.Latest.Agents.Count);
    }

    [Fact]
    public void Refresh_is_skipped_while_a_run_is_going()
    {
        using var gate = new ManualResetEventSlim();
        using var cache = new CloudCache(_ => { gate.Wait(); return CloudHelper.Parse(Stream, T0); }, Path.Combine(this.dir, "c.json"), TimeSpan.FromMinutes(30), () => T0);

        var first = Task.Run(cache.RefreshNow);
        SpinWait.SpinUntil(() => cache.IsRunning, TimeSpan.FromSeconds(5));

        Assert.False(cache.RefreshNow());
        gate.Set();
        Assert.True(first.Result);
    }
}
