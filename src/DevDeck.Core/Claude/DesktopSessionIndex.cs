using System.Text.Json.Nodes;
using DevDeck.Core.Sessions;

namespace DevDeck.Core.Claude;

/// <summary>
/// Reads Claude Desktop's Code session files to name sessions and tell Desktop sessions from CLI ones.
/// These are internal app files with no stability promise, so every read tolerates missing or changed fields.
/// </summary>
public sealed class DesktopSessionIndex : ISessionMetadataSource
{
    private readonly string root;
    private readonly Func<DateTimeOffset> clock;
    private readonly object gate = new();
    private Dictionary<string, SessionMetadata> byCliSessionId = new(StringComparer.Ordinal);
    private DateTimeOffset lastScan = DateTimeOffset.MinValue;

    public DesktopSessionIndex(string? root = null, Func<DateTimeOffset>? clock = null)
    {
        this.root = root ?? DefaultRoot;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "Claude", "claude-code-sessions");

    /// <summary>The shortest time between rescans triggered by an unknown session.</summary>
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Returns cached metadata. A miss starts a background rescan (at most once per <see cref="RescanInterval"/>)
    /// and returns null, so callers holding locks never wait on disk.
    /// </summary>
    public SessionMetadata? Find(string agent, string sessionId)
    {
        if (agent != "claude")
        {
            return null;
        }

        lock (this.gate)
        {
            if (this.byCliSessionId.TryGetValue(sessionId, out var found))
            {
                return found;
            }

            var now = this.clock();
            if (now - this.lastScan >= this.RescanInterval && (this.PendingScan is null || this.PendingScan.IsCompleted))
            {
                this.lastScan = now;
                this.PendingScan = Task.Run(this.Refresh);
            }

            return null;
        }
    }

    /// <summary>The background rescan started by the last miss, for tests.</summary>
    internal Task? PendingScan { get; private set; }

    /// <summary>Rescans the session files now, on the calling thread.</summary>
    public void Refresh()
    {
        var map = Scan(this.root);
        lock (this.gate)
        {
            this.byCliSessionId = map;
        }
    }

    internal static Dictionary<string, SessionMetadata> Scan(string root)
    {
        var map = new Dictionary<string, SessionMetadata>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
        {
            return map;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "local_*.json", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return map;
        }

        foreach (var file in files)
        {
            var entry = TryRead(file);
            if (entry is { } e)
            {
                map[e.CliSessionId] = e.Metadata;
            }
        }

        return map;
    }

    private static (string CliSessionId, SessionMetadata Metadata)? TryRead(string file)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject obj)
            {
                return null;
            }

            var cliSessionId = Str(obj, "cliSessionId");
            if (string.IsNullOrEmpty(cliSessionId))
            {
                return null;
            }

            return (cliSessionId, new SessionMetadata(
                SessionSource.ClaudeDesktop,
                Title: Str(obj, "title"),
                Cwd: Str(obj, "originCwd") ?? Str(obj, "cwd"),
                Branch: Str(obj, "branch"),
                Model: Str(obj, "model")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonObject obj, string key) =>
        obj.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
