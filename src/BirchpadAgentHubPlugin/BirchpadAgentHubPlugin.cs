namespace Loupedeck.BirchpadAgentHubPlugin
{
    using System;

    using Loupedeck.BirchpadAgentHubPlugin.Services;

    // Universal plugin: its tiles and answer keys work on every app profile.
    public class BirchpadAgentHubPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public BirchpadAgentHubPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        // Shared services for every action. Null until Load runs.
        public Hub Hub { get; private set; }

        public override void Load()
        {
            var port = this.TryGetPluginSetting("port", out var value) && Int32.TryParse(value, out var parsed) ? parsed : Hub.DefaultPort;
            var cloudMinutes = this.TryGetPluginSetting("cloudIntervalMinutes", out var minutes) && Int32.TryParse(minutes, out var m) && m >= 5 ? m : 30;
            this.Hub = new Hub(port, TimeSpan.FromMinutes(cloudMinutes));
            if (this.Hub.Start(out var error))
            {
                PluginLog.Info($"Agent Hub listening on 127.0.0.1:{port}");
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Normal, null);
            }
            else
            {
                PluginLog.Error($"Agent Hub could not listen on port {port}: {error}");
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Error, $"Port {port} is in use, so agent hooks cannot reach the keypad.");
            }
        }

        public override void Unload()
        {
            this.Hub?.Dispose();
            this.Hub = null;
        }
    }
}
