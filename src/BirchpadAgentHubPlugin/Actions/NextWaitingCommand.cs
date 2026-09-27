namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;

    using Birchpad.Core.Sessions;

    using Loupedeck.Tiles;

    // Opens the session that most needs you: approvals first, then questions, then the latest finished turn.
    public class NextWaitingCommand : HubCommand
    {
        public NextWaitingCommand()
            : base("Next waiting", "Open the session that has waited longest for you.", "Sessions")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            var session = this.Hub?.Sessions.MostUrgent();
            if (session != null)
            {
                this.Hub.Focus.Show(session);
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var session = this.Hub?.Sessions.MostUrgent();
            var color = session == null ? TileRenderer.Empty : TileRenderer.ColorFor(session.State);
            return TileRenderer.Command("Next", color, imageSize);
        }
    }
}
