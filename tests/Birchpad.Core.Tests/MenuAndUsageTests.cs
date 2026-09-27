using System.Text.Json.Nodes;
using Birchpad.Core.Codex;
using Birchpad.Core.Http;
using Birchpad.Core.Mac;
using Birchpad.Core.Menu;
using Birchpad.Core.Usage;

namespace Birchpad.Core.Tests;

public sealed class MenuAndUsageTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly string dir = Path.Combine(Path.GetTempPath(), "birchpad-menu-" + Guid.NewGuid().ToString("N"));

    public MenuAndUsageTests() => Directory.CreateDirectory(this.dir);

    public void Dispose() => Directory.Delete(this.dir, recursive: true);

    private static int[] Items(KeySlot[] slots) =>
        slots.Select(s => s.Role == KeyRole.Item ? s.ItemIndex : s.Role == KeyRole.Back ? -2 : s.Role == KeyRole.More ? -3 : -1).ToArray();

    [Fact]
    public void Back_is_always_top_left_and_items_follow_it()
    {
        Assert.Equal([-2, 0, 1, 2, -1, -1, -1, -1, -1], Items(MenuLayout.Arrange(3, 0)));
        Assert.Equal([-2, 0, 1, 2, 3, 4, 5, 6, 7], Items(MenuLayout.Arrange(8, 0)));
    }

    [Fact]
    public void Long_lists_page_with_more_and_wrap()
    {
        Assert.Equal(2, MenuLayout.PageCount(9));
        Assert.Equal(3, MenuLayout.PageCount(15));
        Assert.Equal([-2, 0, 1, 2, 3, 4, 5, 6, -3], Items(MenuLayout.Arrange(9, 0)));
        Assert.Equal([-2, 7, 8, -1, -1, -1, -1, -1, -3], Items(MenuLayout.Arrange(9, 1)));
        Assert.Equal(Items(MenuLayout.Arrange(9, 0)), Items(MenuLayout.Arrange(9, 2)));
    }

    [Fact]
    public void Empty_page_still_has_back()
    {
        Assert.Equal([-2, -1, -1, -1, -1, -1, -1, -1, -1], Items(MenuLayout.Arrange(0, 0)));
    }

    [Fact]
    public void Status_line_sets_limits_and_session_numbers()
    {
        var usage = new UsageStore();
        usage.ApplyStatusLine((JsonObject)JsonNode.Parse("""
            {"session_id":"a","context_window":{"used_percentage":42.5},"cost":{"total_cost_usd":1.25,"total_lines_added":120,"total_lines_removed":40},
             "rate_limits":{"five_hour":{"used_percentage":4,"resets_at":1790500000},"seven_day":{"used_percentage":19,"resets_at":1790900000}}}
            """)!, T0);

        Assert.Equal(new LimitWindow(4, DateTimeOffset.FromUnixTimeSeconds(1790500000)), usage.Limits!.FiveHour);
        Assert.Equal(19, usage.Limits.SevenDay!.UsedPercent);
        Assert.Equal(new ClaudeSessionUsage(42.5, 1.25, 120, 40, T0), usage.ForSession("a"));
    }

    [Fact]
    public void Desktop_fallback_never_replaces_a_newer_status_line()
    {
        var usage = new UsageStore();
        usage.ApplyStatusLine((JsonObject)JsonNode.Parse("""{"session_id":"a","rate_limits":{"five_hour":{"used_percentage":10}}}""")!, T0);

        usage.ApplyFallback(new ClaudeLimits(new LimitWindow(4, null), null, T0.AddMinutes(-15), "Claude Desktop"));
        Assert.Equal(10, usage.Limits!.FiveHour!.UsedPercent);

        usage.ApplyFallback(new ClaudeLimits(new LimitWindow(5, null), new LimitWindow(20, null), T0.AddMinutes(15), "Claude Desktop"));
        Assert.Equal("Claude Desktop", usage.Limits.Source);
    }

    [Fact]
    public void Old_session_numbers_are_dropped_and_the_count_is_capped()
    {
        var usage = new UsageStore { KeepFor = TimeSpan.FromHours(1), MaxSessions = 2 };
        JsonObject Line(string id) => (JsonObject)JsonNode.Parse($$$"""{"session_id":"{{{id}}}","context_window":{"used_percentage":1}}""")!;

        usage.ApplyStatusLine(Line("old"), T0);
        usage.ApplyStatusLine(Line("a"), T0.AddHours(2));
        Assert.Null(usage.ForSession("old"));

        usage.ApplyStatusLine(Line("b"), T0.AddHours(2).AddMinutes(1));
        usage.ApplyStatusLine(Line("c"), T0.AddHours(2).AddMinutes(2));
        Assert.Equal(2, usage.SessionCount);
        Assert.Null(usage.ForSession("a"));
    }

    [Fact]
    public void Hub_port_file_round_trips_and_falls_back_to_the_default()
    {
        var file = Path.Combine(this.dir, "port");

        Assert.Equal(HubClient.DefaultPort, HubClient.ReadPort(file));
        HubClient.RecordPort(9001, file);
        Assert.Equal(9001, HubClient.ReadPort(file));
        File.WriteAllText(file, "nonsense");
        Assert.Equal(HubClient.DefaultPort, HubClient.ReadPort(file));
    }

    [Fact]
    public void Plan_usage_history_reads_the_newest_sample()
    {
        var file = Path.Combine(this.dir, "plan.json");
        File.WriteAllText(file, """{"version":1,"samples":[{"t":1790500000000,"org":"o","u":{"fh":2,"sd":19}},{"t":1790500900000,"org":"o","u":{"fh":4,"sd":19}}]}""");

        var limits = PlanUsageHistory.ReadLatest(file)!;

        Assert.Equal(4, limits.FiveHour!.UsedPercent);
        Assert.Equal(19, limits.SevenDay!.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790500900000), limits.UpdatedAt);
        Assert.Null(PlanUsageHistory.ReadLatest(Path.Combine(this.dir, "missing.json")));
    }

    [Fact]
    public void Codex_context_comes_from_the_last_token_count()
    {
        var file = Path.Combine(this.dir, "rollout.jsonl");
        File.WriteAllLines(file,
        [
            """{"type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"total_tokens":1000},"model_context_window":258400}}}""",
            """{"type":"response_item","payload":{"type":"message"}}""",
            """{"type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"total_tokens":68124},"model_context_window":258400}}}""",
        ]);

        var context = CodexState.ReadContext(file)!;

        Assert.Equal(68124, context.UsedTokens);
        Assert.Equal(26, Math.Round(context.Percent));
        Assert.Null(CodexState.ReadContext(Path.Combine(this.dir, "missing.jsonl")));
    }

    [Fact]
    public void Hub_client_parses_sessions_and_matches_by_folder()
    {
        var sessions = HubClient.Parse("""
            {"sessions":[
              {"sessionId":"old","source":"ClaudeCli","state":"Done","name":"logi","cwd":"/p/logi"},
              {"sessionId":"new","source":"Codex","state":"Working","name":"logi","cwd":"/p/logi/"},
              {"sessionId":"x","source":"ClaudeCli","state":"Working","name":"site","cwd":"/p/site"}
            ],"pendingPermissions":[]}
            """);

        Assert.Equal(3, sessions.Count);
        Assert.Equal("new", HubClient.AtPath(sessions, "/p/logi")!.SessionId);
        Assert.Null(HubClient.AtPath(sessions, "/p/other"));
        Assert.Null(HubClient.AtPath(sessions, null));
    }

    [Fact]
    public void Terminal_agents_come_from_the_process_list()
    {
        var agents = TerminalAgents.Parse("""
            ttys001  -zsh
            ttys001  claude
            ttys001  node
            ttys003  /opt/homebrew/bin/codex
            ttys004  -zsh
            ??       /Applications/Claude.app/Contents/MacOS/Claude
            ttys005  codex
            ttys005  claude
            """);

        Assert.Equal(AgentKind.Claude, agents["ttys001"]);
        Assert.Equal(AgentKind.Codex, agents["ttys003"]);
        Assert.False(agents.ContainsKey("ttys004"));
        Assert.Equal(AgentKind.Claude, agents["ttys005"]);
        Assert.Equal("ttys001", TerminalAgents.TtyName("/dev/ttys001"));
    }

    [Fact]
    public void Claude_context_comes_from_the_last_assistant_usage()
    {
        var file = Path.Combine(this.dir, "transcript.jsonl");
        File.WriteAllLines(file,
        [
            """{"type":"assistant","message":{"model":"claude-opus-5-5","usage":{"input_tokens":2,"cache_read_input_tokens":1000,"cache_creation_input_tokens":0}}}""",
            """{"type":"user","message":{"content":"hi"}}""",
            """{"type":"assistant","message":{"model":"claude-opus-5-5","usage":{"input_tokens":2,"cache_read_input_tokens":99000,"cache_creation_input_tokens":998}}}""",
        ]);

        var context = ClaudeTranscript.ReadContext(file)!;

        Assert.Equal(100_000, context.UsedTokens);
        Assert.Equal(ClaudeTranscript.StandardWindow, context.WindowTokens);
        Assert.Equal(50, context.Percent);
        Assert.Equal(ClaudeTranscript.LongWindow, ClaudeTranscript.WindowFor("claude-opus-5-5[1m]", 10));
        Assert.Equal(ClaudeTranscript.LongWindow, ClaudeTranscript.WindowFor("claude-opus-5-5", 732_199));
        Assert.Null(ClaudeTranscript.ReadContext(Path.Combine(this.dir, "missing.jsonl")));
    }

    [Fact]
    public void Iterm_session_list_parses_tab_separated_lines()
    {
        var panes = ITermController.ParseSessions("A1\t/Users/me/Projects/logi\tlogi (-zsh)\t/dev/ttys001\nB2\t\tscratch\n\nbad line\n");

        Assert.Equal([new ITermSession("A1", "/Users/me/Projects/logi", "logi (-zsh)", "ttys001"), new ITermSession("B2", "", "scratch")], panes);
        Assert.Equal("logi", panes[0].FolderName);
        Assert.Equal("scratch", panes[1].FolderName);
    }
}
