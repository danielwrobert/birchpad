using Birchpad.Core.Codex;
using Birchpad.Core.Mac;

namespace Birchpad.Core.Tests;

// Codex keeps a thread's first message in "title" and its short name in "name"; keys show the name.
public sealed class CodexThreadNameTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "bp-codex-" + Guid.NewGuid().ToString("N")[..8]);

    public CodexThreadNameTests() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, recursive: true);

    private string Database(string schema, string rows)
    {
        var path = Path.Combine(this.directory, Guid.NewGuid().ToString("N") + ".sqlite");
        var result = ProcessRunner.Run("/usr/bin/sqlite3", [path, schema + rows], TimeSpan.FromSeconds(5));
        Assert.Equal(0, result.ExitCode);
        return path;
    }

    [Fact]
    public void PrefersTheThreadNameOverItsFirstMessage()
    {
        var db = this.Database(
            "create table threads (id text, title text, name text, cwd text, rollout_path text, updated_at integer, archived integer, source text);",
            "insert into threads values ('a', 'Please look at why the plugin upload fails and', 'Diagnose plugin upload', '/p', null, 2, 0, 'vscode');" +
            "insert into threads values ('b', 'Fix the tests', '', '/p', null, 1, 0, 'cli');");

        var threads = new CodexState(db).RecentThreads();

        Assert.Equal(["Diagnose plugin upload", "Fix the tests"], threads.Select(t => t.Title));
        Assert.Equal(["a"], new CodexState(db).AppThreads().Select(t => t.Id));
    }

    [Fact]
    public void FallsBackToTheTitleWithoutANameColumn()
    {
        var db = this.Database(
            "create table threads (id text, title text, cwd text, rollout_path text, updated_at integer, archived integer, source text);",
            "insert into threads values ('a', 'Old title', '/p', null, 2, 0, 'vscode');");

        Assert.Equal("Old title", Assert.Single(new CodexState(db).RecentThreads()).Title);
    }
}
