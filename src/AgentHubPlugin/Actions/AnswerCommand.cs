namespace Loupedeck.AgentHubPlugin.Actions
{
    using System;
    using System.Globalization;

    using DevDeck.Core.Permissions;

    using Loupedeck.Tiles;

    // Answers the oldest permission request held by the hub.
    public class AnswerCommand : HubCommand
    {
        public AnswerCommand()
        {
            this.DisplayName = "Answer permission";
            this.Description = "Allow, always allow, or deny the oldest pending permission request.";
            this.GroupName = "Permissions";
            this.AddParameter("allow", "Allow", "Permissions");
            this.AddParameter("always", "Always allow", "Permissions");
            this.AddParameter("deny", "Deny", "Permissions");
        }

        protected override void RunCommand(String actionParameter)
        {
            if (this.Hub != null && TryParse(actionParameter, out var answer) && !this.Hub.Permissions.AnswerOldest(answer))
            {
                PluginLog.Info("No permission request is waiting");
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => Label(actionParameter);

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var pending = this.Hub?.Permissions.PendingCount ?? 0;
            var color = pending == 0 ? TileRenderer.Empty
                : actionParameter == "deny" ? TileRenderer.Failed
                : TileRenderer.Done;
            return TileRenderer.Command(Label(actionParameter), color, imageSize, pending > 1 ? pending.ToString(CultureInfo.InvariantCulture) : null);
        }

        private static String Label(String actionParameter) => actionParameter switch
        {
            "allow" => "Allow",
            "always" => "Always",
            "deny" => "Deny",
            _ => "Answer",
        };

        private static Boolean TryParse(String actionParameter, out PermissionAnswer answer)
        {
            switch (actionParameter)
            {
                case "allow":
                    answer = PermissionAnswer.Allow;
                    return true;
                case "always":
                    answer = PermissionAnswer.AllowAlways;
                    return true;
                case "deny":
                    answer = PermissionAnswer.Deny;
                    return true;
                default:
                    answer = default;
                    return false;
            }
        }
    }
}
