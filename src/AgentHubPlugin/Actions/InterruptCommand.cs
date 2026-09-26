namespace Loupedeck.AgentHubPlugin.Actions
{
    using System;
    using System.Threading.Tasks;

    using Loupedeck.Tiles;

    // Sends Escape to the most recently active working session.
    public class InterruptCommand : HubCommand
    {
        public InterruptCommand()
            : base("Interrupt", "Stop the most recently active agent session.", "Sessions")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            var session = this.Hub?.Sessions.MostRecentWorking();
            if (session == null)
            {
                return;
            }

            // Interrupting waits for the app to switch sessions; keep that off the key-press thread.
            var focus = this.Hub.Focus;
            Task.Run(() =>
            {
                if (!focus.Interrupt(session))
                {
                    PluginLog.Warning($"Could not interrupt session {session.SessionId} ({session.Source})");
                }
            });
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var active = this.Hub?.Sessions.MostRecentWorking() != null;
            return TileRenderer.Command("Stop", active ? TileRenderer.Failed : TileRenderer.Empty, imageSize);
        }
    }
}
