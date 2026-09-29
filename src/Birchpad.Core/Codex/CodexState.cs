using System.Text.Json.Nodes;
using Birchpad.Core.Mac;

namespace Birchpad.Core.Codex;

/// <summary>A Codex thread row from the local state database.</summary>
public sealed record CodexThreadRow(string Id, string Title, string Cwd, string? RolloutPath, DateTimeOffset UpdatedAt);

/// <summary>A Codex project and its first folder.</summary>
public sealed record CodexProject(string Id, string Name, string? Path);

/// <summary>How full one thread's context is, from its last token count.</summary>
public sealed record CodexContext(long UsedTokens, long WindowTokens)
{
    public double Percent => this.WindowTokens <= 0 ? 0 : 100.0 * this.UsedTokens / this.WindowTokens;
}

/// <summary>
/// Reads Codex's local state (<c>~/.codex/state_5.sqlite</c>) with the system <c>sqlite3</c> in read-only mode,
/// and thread rollout logs for context use. Both are internal formats; every read tolerates changes.
/// Only IDs, titles, folders, and token counts are read, never message content.
/// </summary>
public sealed class CodexState
{
    private const string Sqlite = "/usr/bin/sqlite3";

    private readonly string database;

    public CodexState(string? database = null)
    {
        this.database = database ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "state_5.sqlite");
    }

    public IReadOnlyList<CodexThreadRow> RecentThreads(int limit = 50) =>
        this.Query($"select id, title, cwd, rollout_path, updated_at from threads where archived = 0 order by updated_at desc limit {Math.Clamp(limit, 1, 500)}")
            .Select(r => new CodexThreadRow(
                Str(r, "id") ?? string.Empty,
                FirstLine(Str(r, "title")),
                Str(r, "cwd") ?? string.Empty,
                Str(r, "rollout_path"),
                DateTimeOffset.FromUnixTimeSeconds(Long(r, "updated_at"))))
            .Where(t => t.Id.Length > 0)
            .ToList();

    /// <summary>Threads started in the ChatGPT app (chats handed to Codex, Work tasks, and app sessions), newest first; CLI threads are left out.</summary>
    public IReadOnlyList<CodexThreadRow> AppThreads(int limit = 40) =>
        this.Query($"select id, title, cwd, rollout_path, updated_at from threads where archived = 0 and source <> 'cli' order by updated_at desc limit {Math.Clamp(limit, 1, 500)}")
            .Select(r => new CodexThreadRow(
                Str(r, "id") ?? string.Empty,
                FirstLine(Str(r, "title")),
                Str(r, "cwd") ?? string.Empty,
                Str(r, "rollout_path"),
                DateTimeOffset.FromUnixTimeSeconds(Long(r, "updated_at"))))
            .Where(t => t.Id.Length > 0)
            .ToList();

    public IReadOnlyList<CodexProject> Projects() =>
        this.Query("select p.id, p.name, (select path from project_roots r where r.project_id = p.id order by position limit 1) as path from projects p order by p.position")
            .Select(r => new CodexProject(Str(r, "id") ?? string.Empty, Str(r, "name") ?? "Project", Str(r, "path")))
            .Where(p => p.Id.Length > 0)
            .ToList();

    /// <summary>Context use from the last <c>token_count</c> event in a rollout log, reading only its tail.</summary>
    public static CodexContext? ReadContext(string? rolloutPath)
    {
        if (string.IsNullOrEmpty(rolloutPath) || !File.Exists(rolloutPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(rolloutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            const int tail = 512 * 1024;
            stream.Seek(Math.Max(0, stream.Length - tail), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n');
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (!lines[i].Contains("\"token_count\"", StringComparison.Ordinal))
                {
                    continue;
                }

                var info = FindInfo(JsonNode.Parse(lines[i]));
                var used = info?["last_token_usage"]?["total_tokens"];
                var window = info?["model_context_window"];
                if (used is JsonValue u && window is JsonValue w && u.TryGetValue<long>(out var usedTokens) && w.TryGetValue<long>(out var windowTokens))
                {
                    return new CodexContext(usedTokens, windowTokens);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // A log mid-write or rotated away has no usable count this time.
        }

        return null;
    }

    // The token_count event nests its info object at different depths across Codex versions.
    private static JsonNode? FindInfo(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        if (obj["model_context_window"] is not null && obj["last_token_usage"] is not null)
        {
            return obj;
        }

        foreach (var (_, child) in obj)
        {
            if (FindInfo(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private IReadOnlyList<JsonObject> Query(string sql)
    {
        if (!File.Exists(this.database) || !File.Exists(Sqlite))
        {
            return [];
        }

        try
        {
            var (exitCode, output, _) = ProcessRunner.Run(Sqlite, ["-readonly", "-json", this.database, sql], TimeSpan.FromSeconds(5));
            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return [];
            }

            return (JsonNode.Parse(output) as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or System.ComponentModel.Win32Exception or IOException)
        {
            return [];
        }
    }

    private static string FirstLine(string? text)
    {
        var line = (text ?? string.Empty).Split('\n', 2)[0].Trim();
        return line.Length > 80 ? line[..80] : line;
    }

    private static string? Str(JsonObject row, string key) =>
        row[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static long Long(JsonObject row, string key) =>
        row[key] is JsonValue v && v.TryGetValue<long>(out var l) ? l : 0;
}
