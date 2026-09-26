using DevDeck.Core.Sessions;

namespace DevDeck.Core.Tests;

public class SessionStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static HookEvent Ev(string id, string name, string? tool = null, string? notification = null, string? cwd = "/Users/me/Projects/site") =>
        new("claude", id, name, Cwd: cwd, ToolName: tool, NotificationType: notification);

    [Theory]
    [InlineData("SessionStart", null, null, SessionState.Idle)]
    [InlineData("UserPromptSubmit", null, null, SessionState.Working)]
    [InlineData("PreToolUse", "Bash", null, SessionState.Working)]
    [InlineData("PreToolUse", "AskUserQuestion", null, SessionState.NeedsInput)]
    [InlineData("PreToolUse", "ExitPlanMode", null, SessionState.NeedsInput)]
    [InlineData("PermissionRequest", "Bash", null, SessionState.NeedsApproval)]
    [InlineData("Notification", null, "permission_prompt", SessionState.NeedsApproval)]
    [InlineData("Notification", null, "elicitation_dialog", SessionState.NeedsInput)]
    [InlineData("Stop", null, null, SessionState.Done)]
    [InlineData("StopFailure", null, null, SessionState.Failed)]
    public void Events_set_the_expected_state(string name, string? tool, string? notification, SessionState expected)
    {
        var store = new SessionStore();
        store.Apply(Ev("a", "UserPromptSubmit"), T0);

        var after = store.Apply(Ev("a", name, tool, notification), T0.AddSeconds(1));

        Assert.Equal(expected, after!.State);
    }

    [Fact]
    public void Idle_notification_does_not_hide_a_pending_approval()
    {
        var store = new SessionStore();
        store.Apply(Ev("a", "PermissionRequest", "Bash"), T0);

        var after = store.Apply(Ev("a", "Notification", notification: "idle_prompt"), T0.AddMinutes(1));

        Assert.Equal(SessionState.NeedsApproval, after!.State);
    }

    [Fact]
    public void Unknown_events_keep_state_and_state_since()
    {
        var store = new SessionStore();
        store.Apply(Ev("a", "UserPromptSubmit"), T0);

        var after = store.Apply(Ev("a", "SomethingNew"), T0.AddSeconds(30));

        Assert.Equal(SessionState.Working, after!.State);
        Assert.Equal(T0, after.StateSince);
        Assert.Equal(T0.AddSeconds(30), after.LastEventAt);
    }

    [Fact]
    public void Slots_are_sticky_and_reused_after_prune()
    {
        var store = new SessionStore(slotCount: 2) { EndedLinger = TimeSpan.FromMinutes(1) };
        var a = store.Apply(Ev("a", "SessionStart"), T0);
        var b = store.Apply(Ev("b", "SessionStart"), T0);
        var c = store.Apply(Ev("c", "SessionStart"), T0);

        Assert.Equal(1, a!.Slot);
        Assert.Equal(2, b!.Slot);
        Assert.Null(c!.Slot);

        store.Apply(Ev("a", "SessionEnd"), T0.AddSeconds(1));
        Assert.False(store.Prune(T0.AddSeconds(30)));
        Assert.True(store.Prune(T0.AddMinutes(2)));

        var d = store.Apply(Ev("d", "SessionStart"), T0.AddMinutes(3));
        Assert.Equal(1, d!.Slot);
        Assert.Equal(2, store.GetBySlot(2)!.Slot);
    }

    [Fact]
    public void End_event_for_an_unknown_session_is_ignored()
    {
        var store = new SessionStore();

        Assert.Null(store.Apply(Ev("x", "SessionEnd"), T0));
        Assert.Empty(store.All());
    }

    [Fact]
    public void Stale_sessions_are_pruned()
    {
        var store = new SessionStore { StaleAfter = TimeSpan.FromHours(1) };
        store.Apply(Ev("a", "Stop"), T0);

        Assert.True(store.Prune(T0.AddHours(2)));
        Assert.Empty(store.All());
    }

    [Fact]
    public void Most_urgent_prefers_oldest_approval_then_question_then_latest_done()
    {
        var store = new SessionStore();
        store.Apply(Ev("done-old", "Stop"), T0);
        store.Apply(Ev("done-new", "Stop"), T0.AddMinutes(1));
        Assert.Equal("done-new", store.MostUrgent()!.SessionId);

        store.Apply(Ev("question", "PreToolUse", "AskUserQuestion"), T0.AddMinutes(2));
        Assert.Equal("question", store.MostUrgent()!.SessionId);

        store.Apply(Ev("approve-new", "PermissionRequest", "Bash"), T0.AddMinutes(4));
        store.Apply(Ev("approve-old", "PermissionRequest", "Bash"), T0.AddMinutes(3));
        Assert.Equal("approve-old", store.MostUrgent()!.SessionId);
    }

    [Fact]
    public void Clear_finished_keeps_active_sessions()
    {
        var store = new SessionStore();
        store.Apply(Ev("working", "UserPromptSubmit"), T0);
        store.Apply(Ev("done", "Stop"), T0);
        store.Apply(Ev("failed", "StopFailure"), T0);

        Assert.Equal(2, store.ClearFinished());
        Assert.Equal("working", Assert.Single(store.All()).SessionId);
    }

    [Fact]
    public void Metadata_marks_desktop_sessions_and_uses_the_origin_folder()
    {
        var metadata = new FakeMetadata(new Dictionary<string, SessionMetadata>
        {
            ["desk"] = new(SessionSource.ClaudeDesktop, "Release prep", "/Users/me/projects/amca", "claude/x", "opus"),
        });
        var store = new SessionStore(metadata: metadata);

        var desk = store.Apply(Ev("desk", "SessionStart", cwd: "/Users/me/projects/amca/.claude/worktrees/x"), T0);
        var cli = store.Apply(Ev("cli", "SessionStart"), T0);

        Assert.Equal(SessionSource.ClaudeDesktop, desk!.Source);
        Assert.Equal("Release prep", desk.DisplayName);
        Assert.Equal("/Users/me/projects/amca", desk.Cwd);
        Assert.Equal(SessionSource.ClaudeCli, cli!.Source);
        Assert.Equal("site", cli.DisplayName);
    }

    [Fact]
    public void Changed_fires_for_each_applied_event()
    {
        var store = new SessionStore();
        var count = 0;
        store.Changed += (_, _) => count++;

        store.Apply(Ev("a", "SessionStart"), T0);
        store.Apply(Ev("a", "UserPromptSubmit"), T0);

        Assert.Equal(2, count);
    }

    private sealed class FakeMetadata(Dictionary<string, SessionMetadata> map) : ISessionMetadataSource
    {
        public SessionMetadata? Find(string sessionId) => map.GetValueOrDefault(sessionId);
    }
}
