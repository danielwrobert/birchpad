namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;
    using System.Threading.Tasks;

    using Loupedeck.Tiles;

    // Shows how fresh the cloud lists are; press to fetch them now.
    public class CloudRefreshCommand : HubCommand
    {
        public CloudRefreshCommand()
            : base("Refresh cloud", "Fetch cloud routines and remote sessions now.", "Cloud")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            var cloud = this.Hub?.Cloud;
            if (cloud != null)
            {
                // A run takes several seconds; keep it off the key-press thread.
                Task.Run(() => cloud.RefreshNow());
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var cloud = this.Hub?.Cloud;
            if (cloud == null)
            {
                return TileRenderer.Info("CLOUD", "No claude CLI", String.Empty, TileRenderer.Failed, imageSize);
            }

            if (cloud.IsRunning)
            {
                return TileRenderer.Info("CLOUD", "Refreshing", String.Empty, TileRenderer.Working, imageSize);
            }

            var latest = cloud.Latest;
            if (latest == null)
            {
                return TileRenderer.Info("CLOUD", "Refresh", "never", TileRenderer.Idle, imageSize);
            }

            var age = TileRenderer.Elapsed(DateTimeOffset.UtcNow - latest.FetchedAt) + " ago";
            return latest.Error == null
                ? TileRenderer.Info("CLOUD", "Refresh", age, TileRenderer.Done, imageSize)
                : TileRenderer.Info("CLOUD", "Failed", age, TileRenderer.Failed, imageSize);
        }
    }
}
