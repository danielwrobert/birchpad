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

        var found = new DesktopSessionIndex(this.root).Find("cli-1");

        Assert.Equal(new SessionMetadata(SessionSource.ClaudeDesktop, "Release prep", "/p/amca", "claude/x", "opus"), found);
    }

    [Fact]
    public void Skips_broken_files_and_files_without_an_id()
    {
        this.Write("local_bad.json", "{ not json");
        this.Write("local_noid.json", """{"title":"x"}""");
        this.Write("local_ok.json", """{"cliSessionId":"ok"}""");
        this.Write("scheduled-tasks.json", """{"cliSessionId":"ignored"}""");

        var index = new DesktopSessionIndex(this.root);

        Assert.NotNull(index.Find("ok"));
        Assert.Null(index.Find("ignored"));
    }

    [Fact]
    public void Rescans_on_a_miss_only_after_the_interval()
    {
        var now = DateTimeOffset.UnixEpoch;
        var index = new DesktopSessionIndex(this.root, () => now) { RescanInterval = TimeSpan.FromSeconds(5) };
        Assert.Null(index.Find("late"));

        this.Write("local_late.json", """{"cliSessionId":"late"}""");
        now = now.AddSeconds(1);
        Assert.Null(index.Find("late"));

        now = now.AddSeconds(5);
        Assert.NotNull(index.Find("late"));
    }

    [Fact]
    public void Missing_root_finds_nothing()
    {
        Assert.Null(new DesktopSessionIndex(Path.Combine(this.root, "missing")).Find("x"));
    }
}
