namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;

    using Birchpad.Core.Sessions;

    using Loupedeck.Tiles;

    // Opens the session that most needs you: approvals first, then questions, then the latest finished turn.
    // The parameter scopes it to the profile's app: "claude", "codex", or "all" for iTerm.
    public class NextWaitingCommand : HubCommand
    {
        public NextWaitingCommand()
        {
            this.DisplayName = "Next waiting";
            this.Description = "Open the session that has waited longest for you.";
            this.GroupName = "Agent Hub";
            this.AddParameter("claude", "Next waiting (Claude)", "Agent Hub");
            this.AddParameter("codex", "Next waiting (Codex)", "Agent Hub");
            this.AddParameter("all", "Next waiting (all agents)", "Agent Hub");
        }

        private static String AgentFor(String actionParameter) => actionParameter is "claude" or "codex" ? actionParameter : null;

        protected override void RunCommand(String actionParameter)
        {
            var hub = this.Hub;
            var session = hub?.Sessions.MostUrgent(AgentFor(actionParameter));
            if (session != null)
            {
                // Opening a session runs open or osascript; keep it off the key-press thread.
                System.Threading.Tasks.Task.Run(() => hub.Focus.Show(session));
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var session = this.Hub?.Sessions.MostUrgent(AgentFor(actionParameter));
            if (session == null)
            {
                return TileRenderer.Ring(String.Empty, "Next", "nothing waiting", TileRenderer.Idle, imageSize);
            }

            var ring = session.IsWaitingOnUser && !this.Hub.PulseOn ? TileRenderer.Idle : TileRenderer.ColorFor(session.State);
            return TileRenderer.Ring("NEXT", session.DisplayName, TileRenderer.LabelFor(session.State), ring, imageSize);
        }
    }
}
