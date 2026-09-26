namespace Loupedeck.AgentHubPlugin.Actions
{
    using System;

    using Loupedeck.AgentHubPlugin.Rendering;

    // Frees the slots of sessions that are done, failed, or ended.
    public class ClearFinishedCommand : HubCommand
    {
        public ClearFinishedCommand()
            : base("Clear finished", "Remove finished sessions from their slots.", "Sessions")
        {
        }

        protected override void RunCommand(String actionParameter) => this.Hub?.Sessions.ClearFinished();

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            TileRenderer.Command("Clear", TileRenderer.Idle, imageSize);
    }
}
