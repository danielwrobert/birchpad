using System.Globalization;
using System.Text.Json.Nodes;
using Birchpad.Core.Scheduling;

namespace Birchpad.Core.Claude;

/// <summary>One Claude Desktop scheduled task, with its next run worked out from its cron expression.</summary>
public sealed record ScheduledTask(
    string Id,
    string CronExpression,
    bool Enabled,
    string? Cwd,
    DateTimeOffset? LastRunAt,
    DateTime? NextRunLocal);

/// <summary>
/// Reads Claude Desktop's <c>scheduled-tasks.json</c>, an internal file with no stability promise.
/// Unknown or broken entries are skipped.
/// </summary>
public static class ScheduledTaskReader
{
    public static IReadOnlyList<ScheduledTask> Read(string? root = null, DateTime? nowLocal = null)
    {
        root ??= DesktopSessionIndex.DefaultRoot;
        var now = nowLocal ?? DateTime.Now;
        var tasks = new List<ScheduledTask>();
        if (!Directory.Exists(root))
        {
            return tasks;
        }

        foreach (var file in SafeEnumerate(root))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(file))?["scheduledTasks"] is not JsonArray entries)
                {
                    continue;
                }

                foreach (var entry in entries.OfType<JsonObject>())
                {
                    var id = Str(entry, "id");
                    var cron = Str(entry, "cronExpression");
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(cron))
                    {
                        continue;
                    }

                    var enabled = entry["enabled"] is JsonValue v && v.TryGetValue<bool>(out var e) && e;
                    DateTime? next = enabled && CronSchedule.TryParse(cron, out var schedule) ? schedule.Next(now) : null;
                    DateTimeOffset? lastRun = DateTimeOffset.TryParse(Str(entry, "lastRunAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
                    tasks.Add(new ScheduledTask(id, cron, enabled, Str(entry, "cwd"), lastRun, next));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                // Skip a file that is mid-write or unreadable; the next refresh reads it again.
            }
        }

        return tasks
            .OrderBy(t => t.NextRunLocal ?? DateTime.MaxValue)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "scheduled-tasks.json", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? Str(JsonObject obj, string key) =>
        obj.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
