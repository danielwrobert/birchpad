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

    public SessionMetadata? Find(string agent, string sessionId)
    {
        if (agent != "codex")
        {
            return null;
        }

        lock (this.gate)
        {
            if (!this.byId.TryGetValue(sessionId, out var thread))
            {
                var now = this.clock();
                if (now - this.lastScan < this.RescanInterval)
                {
                    return null;
                }

                this.lastScan = now;
                this.byId = this.list().ToDictionary(t => t.Id, StringComparer.Ordinal);
                if (!this.byId.TryGetValue(sessionId, out thread))
                {
                    return null;
                }
            }

            return new SessionMetadata(SessionSource.Codex, thread.Name, thread.Cwd, Branch: null, Model: null);
        }
    }
}
