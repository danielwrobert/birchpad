using DevDeck.Core.Claude;
using DevDeck.Core.Sessions;

namespace DevDeck.Core.Tests;

public sealed class DesktopSessionIndexTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "devdeck-tests-" + Guid.NewGuid().ToString("N"));

    public DesktopSessionIndexTests()
    {
        Directory.CreateDirectory(Path.Combine(this.root, "account", "org"));
    }

    public void Dispose() => Directory.Delete(this.root, recursive: true);

    private void Write(string name, string json) => File.WriteAllText(Path.Combine(this.root, "account", "org", name), json);

    [Fact]
    public void Finds_sessions_by_cli_session_id_and_prefers_origin_cwd()
    {
        this.Write("local_1.json", """{"cliSessionId":"cli-1","title":"Release prep","cwd":"/p/amca/.claude","originCwd":"/p/amca","branch":"claude/x","model":"opus"}""");
        var index = new DesktopSessionIndex(this.root);

        index.Refresh();

        Assert.Equal(new SessionMetadata(SessionSource.ClaudeDesktop, "Release prep", "/p/amca", "claude/x", "opus"), index.Find("claude", "cli-1"));
    }

    [Fact]
    public void Skips_broken_files_and_files_without_an_id()
    {
        this.Write("local_bad.json", "{ not json");
        this.Write("local_noid.json", """{"title":"x"}""");
        this.Write("local_ok.json", """{"cliSessionId":"ok"}""");
        this.Write("scheduled-tasks.json", """{"cliSessionId":"ignored"}""");
        var index = new DesktopSessionIndex(this.root);

        index.Refresh();

        Assert.NotNull(index.Find("claude", "ok"));
        Assert.Null(index.Find("claude", "ignored"));
    }

    [Fact]
    public void A_miss_rescans_in_the_background_at_most_once_per_interval()
    {
        var now = DateTimeOffset.UnixEpoch;
        var index = new DesktopSessionIndex(this.root, () => now) { RescanInterval = TimeSpan.FromSeconds(5) };
        this.Write("local_late.json", """{"cliSessionId":"late"}""");

        // The miss returns at once and starts a scan; the answer is there on the next lookup.
        Assert.Null(index.Find("claude", "late"));
        index.PendingScan!.Wait();
        Assert.NotNull(index.Find("claude", "late"));

        this.Write("local_later.json", """{"cliSessionId":"later"}""");
        var scan = index.PendingScan;
        now = now.AddSeconds(1);
        Assert.Null(index.Find("claude", "later"));
        Assert.Same(scan, index.PendingScan);

        now = now.AddSeconds(5);
        Assert.Null(index.Find("claude", "later"));
        index.PendingScan!.Wait();
        Assert.NotNull(index.Find("claude", "later"));
    }

    [Fact]
    public void Other_agents_are_never_desktop_sessions()
    {
        this.Write("local_1.json", """{"cliSessionId":"same-id"}""");

        var index = new DesktopSessionIndex(this.root);
        index.Refresh();

        Assert.Null(index.Find("codex", "same-id"));
    }

    [Fact]
    public void Missing_root_finds_nothing()
    {
        var index = new DesktopSessionIndex(Path.Combine(this.root, "missing"));
        index.Refresh();

        Assert.Null(index.Find("claude", "x"));
    }
}
