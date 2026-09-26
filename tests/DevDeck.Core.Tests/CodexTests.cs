using System.Text.Json.Nodes;
using DevDeck.Core.Codex;
using DevDeck.Core.Sessions;

namespace DevDeck.Core.Tests;

public class CodexTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_rate_limit_windows()
    {
        var limits = JsonNode.Parse("""{"primary":{"usedPercent":12.5,"windowDurationMins":300,"resetsAt":1790461668},"secondary":{"usedPercent":3,"windowDurationMins":10080,"resetsAt":null}}""")!;

        var usage = CodexAppServer.ParseUsage(limits, T0);

        Assert.Equal(new CodexLimit(12.5, 300, DateTimeOffset.FromUnixTimeSeconds(1790461668)), usage.Primary);
        Assert.Equal(new CodexLimit(3, 10080, null), usage.Secondary);
    }

    [Fact]
    public void Parses_threads_and_skips_entries_without_an_id()
    {
        var data = JsonNode.Parse("""[{"id":"t1","name":"Fix CSV","cwd":"/p","updatedAt":1789756442},{"name":"no id"}]""")!.AsArray();

        var thread = Assert.Single(CodexAppServer.ParseThreads(data));

        Assert.Equal(new CodexThread("t1", "Fix CSV", "/p", DateTimeOffset.FromUnixTimeSeconds(1789756442)), thread);
    }

    [Fact]
    public void Thread_index_names_codex_sessions_and_ignores_claude()
    {
        var calls = 0;
        var now = T0;
        var index = new CodexThreadIndex(() => { calls++; return [new CodexThread("t1", "Fix CSV", "/p", null)]; }, () => now);

        Assert.Null(index.Find("claude", "t1"));
        Assert.Equal(new SessionMetadata(SessionSource.Codex, "Fix CSV", "/p", null, null), index.Find("codex", "t1"));
        Assert.Null(index.Find("codex", "t2"));
        Assert.Equal(1, calls);

        now = now.AddMinutes(1);
        Assert.Null(index.Find("codex", "t2"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Live_app_server_reads_usage_when_codex_is_installed()
    {
        var codex = CodexAppServer.FindCodex();
        if (codex is null)
        {
            return;
        }

        var usage = new CodexAppServer(codex).ReadUsage(T0);

        Assert.NotNull(usage);
        Assert.NotNull(usage!.Primary);
    }
}
