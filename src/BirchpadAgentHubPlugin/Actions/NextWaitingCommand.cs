namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;

    using Birchpad.Core.Agents;

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

        // Hook sessions and herdr agents together, so an agent in a herdr pane is reached through herdr.
        private CodingAgent MostUrgent(String actionParameter) =>
            this.Hub is { } hub ? CodingAgents.MostUrgent(hub.Agents.All(), AgentFor(actionParameter)) : null;

        protected override void RunCommand(String actionParameter)
        {
            var hub = this.Hub;
            var agent = this.MostUrgent(actionParameter);
            if (agent != null)
            {
                // Opening an agent runs open, osascript, or a herdr call; keep it off the key-press thread.
                System.Threading.Tasks.Task.Run(() => hub.Agents.Show(agent));
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var agent = this.MostUrgent(actionParameter);
            if (agent == null)
            {
                return TileRenderer.Ring(String.Empty, "Next", "nothing waiting", TileRenderer.Idle, imageSize);
            }

            var ring = agent.IsWaitingOnUser && !this.Hub.PulseOn ? TileRenderer.Idle : TileRenderer.ColorFor(agent.State);
            return TileRenderer.Ring("NEXT", agent.Name, TileRenderer.LabelFor(agent.State), ring, imageSize);
        }
    }
}
