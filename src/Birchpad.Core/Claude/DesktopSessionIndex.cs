using Birchpad.Core.Sessions;

namespace Birchpad.Core.Claude;

/// <summary>A folder Claude Desktop Code sessions ran in.</summary>
public sealed record DesktopProject(string Path, DateTimeOffset LastUsed)
{
    public string Name => System.IO.Path.GetFileName(this.Path.TrimEnd('/'));
}

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
    private IReadOnlyList<DesktopProject> projects = [];

    // Session files are large (MCP tool lists and more), so each is parsed once and again only when it changes.
    private readonly Dictionary<string, (DateTime Modified, long Length, Entry? Entry)> fileCache = new(StringComparer.Ordinal);
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

    /// <summary>Folders that Claude Desktop Code sessions ran in, most recently used first. Filled by <see cref="Refresh"/>.</summary>
    public IReadOnlyList<DesktopProject> Projects
    {
        get
        {
            lock (this.gate)
            {
                return this.projects;
            }
        }
    }

    /// <summary>Rescans the session files now, on the calling thread.</summary>
    public void Refresh()
    {
        var entries = this.ReadAll(this.root);
        var map = new Dictionary<string, SessionMetadata>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            map[e.CliSessionId] = e.Metadata;
        }

        var projects = entries
            .Where(e => !string.IsNullOrEmpty(e.Metadata.Cwd))
            // macOS folders are case-insensitive, and Desktop records the same folder with different cases.
            .GroupBy(e => e.Metadata.Cwd!.TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            .Select(g => new DesktopProject(g.OrderByDescending(e => e.LastActivity).First().Metadata.Cwd!.TrimEnd('/'), g.Max(e => e.LastActivity)))
            .OrderByDescending(p => p.LastUsed)
            .ToList();

        lock (this.gate)
        {
            this.byCliSessionId = map;
            this.projects = projects;
        }
    }

    private sealed record Entry(string CliSessionId, SessionMetadata Metadata, DateTimeOffset LastActivity);

    private List<Entry> ReadAll(string root)
    {
        var list = new List<Entry>();
        if (!Directory.Exists(root))
        {
            return list;
        }

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "local_*.json", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return list;
        }

        lock (this.fileCache)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                seen.Add(file);
                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!this.fileCache.TryGetValue(file, out var cached) || cached.Modified != info.LastWriteTimeUtc || cached.Length != info.Length)
                {
                    cached = (info.LastWriteTimeUtc, info.Length, TryRead(file));
                    this.fileCache[file] = cached;
                }

                if (cached.Entry is { } entry)
                {
                    list.Add(entry);
                }
            }

            foreach (var gone in this.fileCache.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                this.fileCache.Remove(gone);
            }
        }

        return list;
    }

    // Reads only the top-level fields the index needs and skips everything nested.
    private static Entry? TryRead(string file)
    {
        try
        {
            var bytes = File.ReadAllBytes(file);
            var reader = new System.Text.Json.Utf8JsonReader(bytes, new System.Text.Json.JsonReaderOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
            if (!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            {
                return null;
            }

            var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
            bool? archived = null;
            long? lastActivityMs = null;
            while (reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
            {
                var name = reader.GetString()!;
                reader.Read();
                switch (reader.TokenType)
                {
                    case System.Text.Json.JsonTokenType.String:
                        fields[name] = reader.GetString();
                        break;
                    case System.Text.Json.JsonTokenType.True or System.Text.Json.JsonTokenType.False when name == "isArchived":
                        archived = reader.GetBoolean();
                        break;
                    case System.Text.Json.JsonTokenType.Number when name == "lastActivityAt" && reader.TryGetInt64(out var ms):
                        lastActivityMs = ms;
                        break;
                    case System.Text.Json.JsonTokenType.StartObject or System.Text.Json.JsonTokenType.StartArray:
                        reader.Skip();
                        break;
                }
            }

            var cliSessionId = fields.GetValueOrDefault("cliSessionId");
            if (string.IsNullOrEmpty(cliSessionId) || archived == true)
            {
                return null;
            }

            return new Entry(
                cliSessionId,
                new SessionMetadata(
                    SessionSource.ClaudeDesktop,
                    Title: fields.GetValueOrDefault("title"),
                    Cwd: fields.GetValueOrDefault("originCwd") ?? fields.GetValueOrDefault("cwd"),
                    Branch: fields.GetValueOrDefault("branch"),
                    Model: fields.GetValueOrDefault("model")),
                lastActivityMs is { } t ? DateTimeOffset.FromUnixTimeMilliseconds(t) : DateTimeOffset.MinValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
