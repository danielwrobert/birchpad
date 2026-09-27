namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Birchpad.Core.Cloud;

    using Loupedeck.Tiles;

    // Sessions the hooks cannot see: Remote Control sessions on other machines and cloud sessions.
    public class OtherSessionsFolder : HubFolder
    {
        private const Int32 MaxItems = 8;

        public OtherSessionsFolder()
        {
            this.DisplayName = "Other sessions";
            this.GroupName = "Cloud";
        }

        public override IEnumerable<String> GetButtonPressActionNames(DeviceType deviceType) =>
            new[] { PluginDynamicFolder.NavigateUpActionName }
                .Concat(this.Items().Take(MaxItems).Select(a => this.CreateCommandName(a.Id)));

        // No deep link opens a session on another machine, so pressing does nothing.
        public override void RunCommand(String actionParameter)
        {
        }

        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var agent = this.Items().FirstOrDefault(a => a.Id == actionParameter);
            return agent == null
                ? TileRenderer.Command(String.Empty, TileRenderer.Empty, imageSize)
                : TileRenderer.Info(agent.Kind.ToUpperInvariant(), agent.Name, agent.Status, ColorFor(agent.Status), imageSize);
        }

        public override BitmapImage GetButtonImage(PluginImageSize imageSize)
        {
            var count = this.Items().Count;
            return TileRenderer.Info(String.Empty, "Other sessions", count == 0 ? "none" : $"{count} found", count == 0 ? TileRenderer.Idle : TileRenderer.Working, imageSize);
        }

        private static BitmapColor ColorFor(String status) => status switch
        {
            "busy" => TileRenderer.Working,
            "waiting" => TileRenderer.Approval,
            _ => TileRenderer.Idle,
        };

        // Local peers already have live tiles from hooks, so only show the rest.
        private IReadOnlyList<CloudAgent> Items() =>
            (this.Hub?.Cloud?.Latest?.Agents ?? Array.Empty<CloudAgent>())
                .Where(a => !String.Equals(a.Kind, "interactive", StringComparison.OrdinalIgnoreCase))
                .ToList();
    }
}
