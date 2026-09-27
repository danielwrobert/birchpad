namespace Birchpad.Core.Sessions;

/// <summary>Extra facts about a session that hooks do not carry, such as a Claude Desktop title.</summary>
public sealed record SessionMetadata(SessionSource Source, string? Title, string? Cwd, string? Branch, string? Model);

/// <summary>Looks up metadata by agent and session ID. Returns null when the session is unknown.</summary>
public interface ISessionMetadataSource
{
    SessionMetadata? Find(string agent, string sessionId);
}

/// <summary>Asks each source in turn and returns the first answer.</summary>
public sealed class CompositeMetadataSource(params ISessionMetadataSource[] sources) : ISessionMetadataSource
{
    public SessionMetadata? Find(string agent, string sessionId) =>
        sources.Select(s => s.Find(agent, sessionId)).FirstOrDefault(m => m is not null);
}

/// <summary>
/// Tracks agent sessions from hook events and gives each a sticky slot.
/// All members are thread-safe; <see cref="Changed"/> fires outside the lock.
/// </summary>
public sealed class SessionStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, Record> sessions = new(StringComparer.Ordinal);
    private readonly ISessionMetadataSource? metadata;

    public SessionStore(int slotCount = 16, ISessionMetadataSource? metadata = null)
    {
        if (slotCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCount));
        }

        this.SlotCount = slotCount;
        this.metadata = metadata;
    }

    /// <summary>Raised after any session is added, changed, or removed.</summary>
    public event EventHandler? Changed;

    public int SlotCount { get; }

    /// <summary>How long an ended session stays on its tile before its slot is freed.</summary>
    public TimeSpan EndedLinger { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How long a session may go without events before it is dropped.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromHours(8);

    /// <summary>Applies one hook event. Returns the session after the event, or null if it was ignored.</summary>
    public SessionSnapshot? Apply(HookEvent e, DateTimeOffset now)
    {
        SessionSnapshot result;
        lock (this.gate)
        {
            if (!this.sessions.TryGetValue(e.SessionId, out var record))
            {
                // An end event for a session we never saw has nothing to show.
                if (e.EventName == "SessionEnd")
                {
                    return null;
                }

                record = new Record(e.SessionId, e.Agent, now) { Slot = this.FreeSlot() };
                this.sessions.Add(e.SessionId, record);
            }

            record.LastEventAt = now;
            // Hooks report the shell's current folder, which moves as the agent runs cd.
            // The first one is where the session started, and what iTerm reports for its tab.
            record.Cwd ??= e.Cwd;
            record.PermissionMode = e.PermissionMode ?? record.PermissionMode;
            record.Model = e.Model ?? record.Model;
            record.Title = e.SessionTitle ?? record.Title;
            if (e.ToolName is not null)
            {
                record.LastToolName = e.ToolName;
            }

            var next = SessionStateMachine.Next(record.State, e);
            if (next is { } state && state != record.State)
            {
                record.State = state;
                record.StateSince = now;
            }

            this.Enrich(record);
            result = record.ToSnapshot();
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Records a model name from a status line update without changing state.</summary>
    public void ApplyModel(string sessionId, string? model, DateTimeOffset now)
    {
        lock (this.gate)
        {
            if (model is null || !this.sessions.TryGetValue(sessionId, out var record) || record.Model == model)
            {
                return;
            }

            record.Model = model;
            record.LastEventAt = now;
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops ended and stale sessions. Returns true if anything was removed.</summary>
    public bool Prune(DateTimeOffset now)
    {
        bool removed;
        lock (this.gate)
        {
            var doomed = this.sessions.Values
                .Where(r => (r.State == SessionState.Ended && now - r.StateSince >= this.EndedLinger)
                         || now - r.LastEventAt >= this.StaleAfter)
                .Select(r => r.SessionId)
                .ToList();
            doomed.ForEach(id => this.sessions.Remove(id));
            removed = doomed.Count > 0;
            if (removed)
            {
                this.FillFreedSlots();
            }
        }

        if (removed)
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>Removes finished sessions (done, failed, ended) at once. Returns how many were removed.</summary>
    public int ClearFinished()
    {
        int count;
        lock (this.gate)
        {
            var doomed = this.sessions.Values
                .Where(r => r.State is SessionState.Done or SessionState.Failed or SessionState.Ended)
                .Select(r => r.SessionId)
                .ToList();
            doomed.ForEach(id => this.sessions.Remove(id));
            count = doomed.Count;
            if (count > 0)
            {
                this.FillFreedSlots();
            }
        }

        if (count > 0)
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }

        return count;
    }

    public SessionSnapshot? Get(string sessionId)
    {
        lock (this.gate)
        {
            return this.sessions.TryGetValue(sessionId, out var r) ? r.ToSnapshot() : null;
        }
    }

    public SessionSnapshot? GetBySlot(int slot)
    {
        lock (this.gate)
        {
            return this.sessions.Values.FirstOrDefault(r => r.Slot == slot)?.ToSnapshot();
        }
    }

    /// <summary>All sessions, oldest first.</summary>
    public IReadOnlyList<SessionSnapshot> All()
    {
        lock (this.gate)
        {
            return this.sessions.Values.OrderBy(r => r.CreatedAt).Select(r => r.ToSnapshot()).ToList();
        }
    }

    /// <summary>
    /// The session that most needs the user: the longest-waiting approval, then question,
    /// then the most recently finished turn.
    /// </summary>
    public SessionSnapshot? MostUrgent()
    {
        lock (this.gate)
        {
            var waiting = this.sessions.Values
                .Where(r => r.State is SessionState.NeedsApproval or SessionState.NeedsInput)
                .OrderBy(r => r.State == SessionState.NeedsApproval ? 0 : 1)
                .ThenBy(r => r.StateSince)
                .FirstOrDefault();
            return (waiting ?? this.sessions.Values
                .Where(r => r.State == SessionState.Done)
                .OrderByDescending(r => r.StateSince)
                .FirstOrDefault())?.ToSnapshot();
        }
    }

    /// <summary>The most recently active working session, the target of a global interrupt.</summary>
    public SessionSnapshot? MostRecentWorking()
    {
        lock (this.gate)
        {
            return this.sessions.Values
                .Where(r => r.State is SessionState.Working or SessionState.NeedsApproval or SessionState.NeedsInput)
                .OrderByDescending(r => r.LastEventAt)
                .FirstOrDefault()?.ToSnapshot();
        }
    }

    /// <summary>Marks a session as waiting for approval, for when a held request outlives a later event.</summary>
    public void MarkNeedsApproval(string sessionId, DateTimeOffset now)
    {
        lock (this.gate)
        {
            if (!this.sessions.TryGetValue(sessionId, out var record) || record.State == SessionState.NeedsApproval)
            {
                return;
            }

            record.State = SessionState.NeedsApproval;
            record.StateSince = now;
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    // Sessions that arrived while every slot was taken get freed slots, oldest first.
    private void FillFreedSlots()
    {
        foreach (var record in this.sessions.Values.Where(r => r.Slot is null).OrderBy(r => r.CreatedAt))
        {
            record.Slot = this.FreeSlot();
            if (record.Slot is null)
            {
                return;
            }
        }
    }

    private int? FreeSlot()
    {
        var used = this.sessions.Values.Where(r => r.Slot is not null).Select(r => r.Slot!.Value).ToHashSet();
        for (var slot = 1; slot <= this.SlotCount; slot++)
        {
            if (!used.Contains(slot))
            {
                return slot;
            }
        }

        // More sessions than slots: the session is tracked but has no tile.
        return null;
    }

    private void Enrich(Record record)
    {
        // Desktop sessions and named Codex threads are settled; others are looked up again on each event.
        if (record.Source == SessionSource.ClaudeDesktop || (record.Source == SessionSource.Codex && record.Title is not null))
        {
            return;
        }

        var found = this.metadata?.Find(record.Agent, record.SessionId);
        if (found is null)
        {
            record.Source = record.Agent == "codex" ? SessionSource.Codex : SessionSource.ClaudeCli;
            return;
        }

        record.Source = found.Source;
        record.Title ??= found.Title;
        record.Branch ??= found.Branch;
        record.Model ??= found.Model;

        // Desktop sessions run in a worktree; the origin folder is the name the user knows.
        record.Cwd = found.Cwd ?? record.Cwd;
    }

    private sealed class Record(string sessionId, string agent, DateTimeOffset createdAt)
    {
        public string SessionId { get; } = sessionId;

        public string Agent { get; } = agent;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public SessionSource Source { get; set; } = SessionSource.Unknown;

        public SessionState State { get; set; } = SessionState.Idle;

        public DateTimeOffset StateSince { get; set; } = createdAt;

        public DateTimeOffset LastEventAt { get; set; } = createdAt;

        public string? Cwd { get; set; }

        public string? Title { get; set; }

        public string? Branch { get; set; }

        public string? Model { get; set; }

        public string? PermissionMode { get; set; }

        public string? LastToolName { get; set; }

        public int? Slot { get; set; }

        public SessionSnapshot ToSnapshot() => new(
            this.SessionId, this.Agent, this.Source, this.State, this.StateSince, this.LastEventAt,
            this.Cwd, this.Title, this.Branch, this.Model, this.PermissionMode, this.LastToolName, this.Slot);
    }
}
