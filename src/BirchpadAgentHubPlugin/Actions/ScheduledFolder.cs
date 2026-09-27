namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using Birchpad.Core.Mac;

    using Loupedeck.BirchpadAgentHubPlugin.Services;
    using Loupedeck.Tiles;

    // Claude Desktop scheduled tasks and cloud routines, soonest first.
    public class ScheduledFolder : HubMenuFolder
    {
        public ScheduledFolder()
            : base("Scheduled")
        {
        }

        protected override BitmapImage FolderImage(PluginImageSize imageSize)
        {
            var next = this.Hub == null ? null : RoutineItem.From(this.Hub).FirstOrDefault(i => i.NextRun != null);
            var footer = next == null ? "none due" : "next " + RoutineItem.When(next.NextRun.Value, DateTime.Now);
            return TileRenderer.Ring(String.Empty, "Scheduled", footer, TileRenderer.Working, imageSize);
        }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var hub = this.Hub;
            if (hub == null)
            {
                return Array.Empty<MenuItem>();
            }

            var items = RoutineItem.From(hub).Select(r => new MenuItem(r.Key, r.Name)
            {
                Image = size => TileRenderer.Ring(r.IsLocal ? "LOCAL" : "CLOUD", r.Name, r.Footer(DateTime.Now), r.Enabled ? TileRenderer.Working : TileRenderer.Idle, size),
                Run = () => r.IsLocal ? hub.Mac.OpenClaudeSidebarPage("Scheduled") : hub.Mac.OpenUrl(Links.Routines),
            }).ToList();

            if (hub.Cloud != null)
            {
                items.Add(new MenuItem("refresh", "Refresh")
                {
                    Image = size => this.RefreshTile(size),
                    Run = () =>
                    {
                        Task.Run(() => hub.Cloud.RefreshNow());
                        return true;
                    },
                });
            }

            return items;
        }

        private BitmapImage RefreshTile(PluginImageSize size)
        {
            var cloud = this.Hub?.Cloud;
            if (cloud == null || cloud.IsRunning)
            {
                return TileRenderer.Ring("CLOUD", "Refreshing", String.Empty, TileRenderer.Working, size);
            }

            var latest = cloud.Latest;
            var age = latest == null ? "never" : TileRenderer.Elapsed(DateTimeOffset.UtcNow - latest.FetchedAt) + " ago";
            return TileRenderer.Ring("CLOUD", latest?.Error == null ? "Refresh" : "Failed", age, latest?.Error == null ? TileRenderer.Done : TileRenderer.Failed, size);
        }
    }
}
