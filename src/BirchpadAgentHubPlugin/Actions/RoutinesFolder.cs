namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Birchpad.Core.Mac;

    using Loupedeck.Tiles;
    using Loupedeck.BirchpadAgentHubPlugin.Services;

    // Claude Desktop scheduled tasks and cloud routines, soonest first.
    public class RoutinesFolder : HubFolder
    {
        // The keypad has 9 keys and the folder needs one for the way back.
        private const Int32 MaxItems = 8;

        public RoutinesFolder()
        {
            this.DisplayName = "Routines";
            this.GroupName = "Cloud";
        }

        public override IEnumerable<String> GetButtonPressActionNames(DeviceType deviceType) =>
            new[] { PluginDynamicFolder.NavigateUpActionName }
                .Concat(this.Items().Take(MaxItems).Select(item => this.CreateCommandName(item.Key)));

        public override void RunCommand(String actionParameter)
        {
            var item = this.Items().FirstOrDefault(i => i.Key == actionParameter);
            if (item == null)
            {
                return;
            }

            if (item.IsLocal)
            {
                this.Hub.Mac.Activate(MacAutomation.ClaudeBundleId);
            }
            else
            {
                this.Hub.Mac.OpenUrl(Links.Routines);
            }
        }

        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var item = this.Items().FirstOrDefault(i => i.Key == actionParameter);
            return item == null
                ? TileRenderer.Command(String.Empty, TileRenderer.Empty, imageSize)
                : TileRenderer.Info(item.IsLocal ? "LOCAL" : "CLOUD", item.Name, item.Footer(DateTime.Now), item.Enabled ? TileRenderer.Working : TileRenderer.Idle, imageSize);
        }

        public override BitmapImage GetButtonImage(PluginImageSize imageSize)
        {
            var next = this.Items().FirstOrDefault(i => i.NextRun != null);
            return TileRenderer.Info(String.Empty, "Routines", next == null ? "none due" : $"next {RoutineItem.When(next.NextRun.Value, DateTime.Now)}", TileRenderer.Working, imageSize);
        }

        private IReadOnlyList<RoutineItem> Items() => this.Hub == null ? Array.Empty<RoutineItem>() : RoutineItem.From(this.Hub);
    }
}
