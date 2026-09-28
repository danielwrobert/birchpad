using System.Text.Json;

namespace Birchpad.Core.Claude;

/// <summary>
/// A Cowork task in Claude Desktop. <see cref="CliSessionId"/> is the Claude Code session that runs it,
/// the same ID its hooks send, so a task can be matched to its live status.
/// </summary>
public sealed record CoworkTask(string Id, string? CliSessionId, string Title, DateTimeOffset LastActivity);

/// <summary>
/// Lists Cowork tasks from Claude Desktop's <c>local-agent-mode-sessions</c> files, newest first.
/// Reads only IDs, titles, timestamps, and the archived flag; never a message or prompt.
/// </summary>
public static class CoworkTasks
{
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "Claude", "local-agent-mode-sessions");

    public static IReadOnlyList<CoworkTask> Read(string? root = null, int limit = 40)
    {
        root ??= DefaultRoot;
        if (!Directory.Exists(root))
        {
            return [];
        }

        IEnumerable<string> files;
        try
        {
            // Tasks sit two folders down: <account>/<organization>/local_<id>.json.
            files = Directory.EnumerateFiles(root, "local_*.json", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var tasks = new List<CoworkTask>();
        foreach (var file in files)
        {
            if (ReadOne(file) is { } task)
            {
                tasks.Add(task);
            }
        }

        return tasks.OrderByDescending(t => t.LastActivity).Take(limit).ToList();
    }

    internal static CoworkTask? ReadOne(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || (root.TryGetProperty("isArchived", out var archived) && archived.ValueKind == JsonValueKind.True)
                || !root.TryGetProperty("sessionId", out var id) || id.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } text ? text : "Cowork task";
            var cli = root.TryGetProperty("cliSessionId", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var last = root.TryGetProperty("lastActivityAt", out var l) && l.TryGetInt64(out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : new DateTimeOffset(File.GetLastWriteTimeUtc(file));
            return new CoworkTask(id.GetString()!, string.IsNullOrEmpty(cli) ? null : cli, title, last);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
