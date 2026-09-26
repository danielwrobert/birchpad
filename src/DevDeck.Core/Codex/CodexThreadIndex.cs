using DevDeck.Core.Sessions;

namespace DevDeck.Core.Codex;

/// <summary>Names Codex sessions from the app server's thread list, rescanning at most every 30 seconds on a miss.</summary>
public sealed class CodexThreadIndex : ISessionMetadataSource
{
    private readonly Func<IReadOnlyList<CodexThread>> list;
    private readonly Func<DateTimeOffset> clock;
    private readonly object gate = new();
    private Dictionary<string, CodexThread> byId = new(StringComparer.Ordinal);
    private DateTimeOffset lastScan = DateTimeOffset.MinValue;

    public CodexThreadIndex(Func<IReadOnlyList<CodexThread>> list, Func<DateTimeOffset>? clock = null)
    {
        this.list = list;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Returns cached metadata. A miss starts a background refresh (at most once per <see cref="RescanInterval"/>)
    /// and returns null, because listing threads starts a <c>codex app-server</c> process.
    /// </summary>
    public SessionMetadata? Find(string agent, string sessionId)
    {
        if (agent != "codex")
        {
            return null;
        }

        lock (this.gate)
        {
            if (this.byId.TryGetValue(sessionId, out var thread))
            {
                return new SessionMetadata(SessionSource.Codex, thread.Name, thread.Cwd, Branch: null, Model: null);
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

    /// <summary>The background refresh started by the last miss, for tests.</summary>
    internal Task? PendingScan { get; private set; }

    /// <summary>Lists threads now, on the calling thread. A failed listing keeps the old cache.</summary>
    public void Refresh()
    {
        IReadOnlyList<CodexThread> threads;
        try
        {
            threads = this.list();
        }
        catch (Exception)
        {
            return;
        }

        var map = new Dictionary<string, CodexThread>(StringComparer.Ordinal);
        foreach (var thread in threads)
        {
            map[thread.Id] = thread;
        }

        lock (this.gate)
        {
            this.byId = map;
        }
    }
}
