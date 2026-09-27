using Birchpad.Core.Claude;

namespace Birchpad.Core.Tests;

public sealed class ScheduledTaskReaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "birchpad-tasks-" + Guid.NewGuid().ToString("N"));

    public ScheduledTaskReaderTests()
    {
        Directory.CreateDirectory(Path.Combine(this.root, "account", "org"));
    }

    public void Dispose() => Directory.Delete(this.root, recursive: true);

    [Fact]
    public void Reads_tasks_sorted_by_next_run_and_skips_bad_entries()
    {
        File.WriteAllText(Path.Combine(this.root, "account", "org", "scheduled-tasks.json"), """
            {"scheduledTasks":[
              {"id":"weekly","cronExpression":"0 15 * * 5","enabled":true,"cwd":"/p/w","lastRunAt":"2026-09-18T19:09:25.604Z"},
              {"id":"daily","cronExpression":"0 6 * * 1-5","enabled":true},
              {"id":"off","cronExpression":"0 6 * * *","enabled":false},
              {"id":"no-cron","enabled":true},
              {"cronExpression":"0 6 * * *"}
            ]}
            """);

        var tasks = ScheduledTaskReader.Read(this.root, new DateTime(2026, 9, 25, 12, 0, 0));

        Assert.Equal(["weekly", "daily", "off"], tasks.Select(t => t.Id));
        Assert.Equal(new DateTime(2026, 9, 25, 15, 0, 0), tasks[0].NextRunLocal);
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 19, 9, 25, 604, TimeSpan.Zero), tasks[0].LastRunAt);
        Assert.Null(tasks[2].NextRunLocal);
    }

    [Fact]
    public void Missing_or_broken_files_give_an_empty_list()
    {
        File.WriteAllText(Path.Combine(this.root, "account", "org", "scheduled-tasks.json"), "{ broken");

        Assert.Empty(ScheduledTaskReader.Read(this.root));
        Assert.Empty(ScheduledTaskReader.Read(Path.Combine(this.root, "missing")));
    }
}
