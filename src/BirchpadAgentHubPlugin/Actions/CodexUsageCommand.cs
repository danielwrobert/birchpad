namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;
    using System.Globalization;
    using System.Threading.Tasks;

    using Birchpad.Core.Codex;

    using Loupedeck.Tiles;

    // Codex 5-hour and weekly usage from the Codex app server; press to refresh.
    public class CodexUsageCommand : HubCommand
    {
        public CodexUsageCommand()
            : base("Codex usage", "Codex 5-hour and weekly usage. Press to refresh.", "Codex")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            var hub = this.Hub;
            if (hub != null)
            {
                Task.Run(hub.RefreshCodexUsage);
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (this.Hub?.Codex == null)
            {
                return TileRenderer.Info("CODEX", "No codex CLI", String.Empty, TileRenderer.Failed, imageSize);
            }

            var usage = this.Hub.CodexUsage;
            if (usage == null)
            {
                return TileRenderer.Info("CODEX", "Usage", "loading", TileRenderer.Idle, imageSize);
            }

            var worst = Math.Max(usage.Primary?.UsedPercent ?? 0, usage.Secondary?.UsedPercent ?? 0);
            var color = worst >= 90 ? TileRenderer.Failed : worst >= 70 ? TileRenderer.Approval : TileRenderer.Done;
            return TileRenderer.Info("CODEX", $"5h {Percent(usage.Primary)}", $"week {Percent(usage.Secondary)}", color, imageSize);
        }

        private static String Percent(CodexLimit limit) =>
            limit == null ? "?" : ((Int32)Math.Round(limit.UsedPercent)).ToString(CultureInfo.InvariantCulture) + "%";
    }
}
