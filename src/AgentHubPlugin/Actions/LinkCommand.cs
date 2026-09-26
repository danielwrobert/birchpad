namespace Loupedeck.AgentHubPlugin.Actions
{
    using System;

    using Loupedeck.Tiles;
    using Loupedeck.AgentHubPlugin.Services;

    // Opens claude.ai pages that no local tool can list.
    public class LinkCommand : HubCommand
    {
        public LinkCommand()
        {
            this.DisplayName = "Open Claude page";
            this.Description = "Open artifacts or chats on claude.ai.";
            this.GroupName = "Cloud";
            this.AddParameter("artifacts", "Artifacts", "Cloud");
            this.AddParameter("chats", "Chats", "Cloud");
        }

        protected override void RunCommand(String actionParameter)
        {
            var url = actionParameter == "artifacts" ? Links.Artifacts : actionParameter == "chats" ? Links.Chats : null;
            if (url != null)
            {
                this.Hub?.Mac.OpenUrl(url);
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => Label(actionParameter);

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            TileRenderer.Command(Label(actionParameter), TileRenderer.Idle, imageSize);

        private static String Label(String actionParameter) => actionParameter == "artifacts" ? "Artifacts" : "Chats";
    }
}
