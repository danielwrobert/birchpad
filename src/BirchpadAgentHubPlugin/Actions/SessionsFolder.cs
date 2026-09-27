namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Birchpad.Core.Cloud;
    using Birchpad.Core.Permissions;
    using Birchpad.Core.Sessions;

    using Loupedeck.Tiles;

    // Agent sessions with a status ring, scoped to the app whose profile shows them.
    // Press one for Open, Allow, Always, Deny, and Stop.
    public abstract class SessionsFolder : HubMenuFolder
    {
        protected SessionsFolder(String displayName)
            : base(displayName)
        {
        }

        // "claude", "codex", or null for every agent.
        protected abstract String Agent { get; }

        private IReadOnlyList<SessionSnapshot> Mine() =>
            (this.Hub?.Sessions.All() ?? Array.Empty<SessionSnapshot>())
                .Where(s => this.Agent == null || s.Agent == this.Agent)
                .ToList();

        protected override BitmapImage FolderImage(PluginImageSize imageSize)
        {
            var sessions = this.Mine();
            var waiting = sessions.Count(s => s.IsWaitingOnUser);
            var working = sessions.Count(s => s.State == SessionState.Working);
            var ring = waiting > 0 ? TileRenderer.Approval : working > 0 ? TileRenderer.Working : TileRenderer.Idle;
            var footer = waiting > 0 ? $"{waiting} waiting" : working > 0 ? $"{working} working" : $"{sessions.Count} open";
            return TileRenderer.Ring(String.Empty, "Sessions", footer, ring, imageSize);
        }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var hub = this.Hub;
            if (hub == null)
            {
                return Array.Empty<MenuItem>();
            }

            var items = new List<MenuItem>();
            foreach (var session in this.Mine())
            {
                var id = session.SessionId;
                items.Add(new MenuItem("s:" + id, session.DisplayName)
                {
                    Image = size => this.Tile(id, size),
                    Open = () => this.Actions(id),
                });
            }

            // Sessions on other machines come from the cloud helper, which only sees Claude Code.
            foreach (var agent in this.Agent == "codex" ? Array.Empty<CloudAgent>() : hub.Cloud?.Latest?.Agents ?? Array.Empty<CloudAgent>())
            {
                if (String.Equals(agent.Kind, "interactive", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var ring = agent.Status == "waiting" ? TileRenderer.Approval : agent.Status == "busy" ? TileRenderer.Working : TileRenderer.Idle;
                items.Add(new MenuItem("r:" + agent.Id, agent.Name)
                {
                    Image = size => TileRenderer.Ring(agent.Kind.ToUpperInvariant(), agent.Name, agent.Status, ring, size),
                });
            }

            if (this.Mine().Any(s => s.State is SessionState.Done or SessionState.Failed or SessionState.Ended))
            {
                items.Add(new MenuItem("clear-finished", "Clear finished")
                {
                    Run = () => hub.Sessions.ClearFinished() >= 0,
                });
            }

            return items;
        }

        private IReadOnlyList<MenuItem> Actions(String sessionId)
        {
            var hub = this.Hub;
            var session = hub?.Sessions.Get(sessionId);
            if (session == null)
            {
                return Array.Empty<MenuItem>();
            }

            var pending = hub.Permissions.HasPending(sessionId);
            var answer = pending ? TileRenderer.Done : TileRenderer.Empty;
            return new[]
            {
                new MenuItem("open", "Open") { Image = size => this.Tile(sessionId, size), Run = () => hub.Focus.Show(hub.Sessions.Get(sessionId)) },
                new MenuItem("allow", "Allow") { Color = answer, Run = () => hub.Permissions.AnswerSession(sessionId, PermissionAnswer.Allow) },
                new MenuItem("deny", "Deny") { Color = pending ? TileRenderer.Failed : TileRenderer.Empty, Run = () => hub.Permissions.AnswerSession(sessionId, PermissionAnswer.Deny) },
                new MenuItem("always", "Always") { Color = answer, Run = () => hub.Permissions.AnswerSession(sessionId, PermissionAnswer.AllowAlways) },
                new MenuItem("stop", "Stop") { Color = TileRenderer.Failed, Run = () => hub.Focus.Interrupt(hub.Sessions.Get(sessionId)) },
            };
        }

        private BitmapImage Tile(String sessionId, PluginImageSize size)
        {
            var hub = this.Hub;
            var session = hub?.Sessions.Get(sessionId);
            if (session == null)
            {
                return TileRenderer.Ring(String.Empty, "Ended", String.Empty, TileRenderer.Idle, size);
            }

            var ring = TileRenderer.ColorFor(session.State);

            // Waiting tiles pulse between their color and gray so they stand out.
            if (session.IsWaitingOnUser && !hub.PulseOn)
            {
                ring = TileRenderer.Idle;
            }

            var footer = $"{TileRenderer.LabelFor(session.State)} {TileRenderer.Elapsed(DateTimeOffset.UtcNow - session.StateSince)}";
            return TileRenderer.Ring(TileRenderer.SourceLabel(session.Source), session.DisplayName, footer, ring, size);
        }
    }

    // Claude Code sessions from Claude Desktop and the CLI, for the Claude profile.
    public class ClaudeSessionsFolder : SessionsFolder
    {
        public ClaudeSessionsFolder()
            : base("Claude sessions")
        {
        }

        protected override String Agent => "claude";
    }

    // Codex sessions from the ChatGPT app and the CLI, for the ChatGPT profile.
    public class CodexSessionsFolder : SessionsFolder
    {
        public CodexSessionsFolder()
            : base("Codex sessions")
        {
        }

        protected override String Agent => "codex";
    }

    // Every agent, for iTerm, where both CLIs run.
    public class AllSessionsFolder : SessionsFolder
    {
        public AllSessionsFolder()
            : base("All sessions")
        {
        }

        protected override String Agent => null;
    }
}
