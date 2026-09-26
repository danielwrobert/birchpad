namespace Loupedeck.AgentHubPlugin
{
    using System;

    using Loupedeck.AgentHubPlugin.Services;

    // Universal plugin: its tiles and answer keys work on every app profile.
    public class AgentHubPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public AgentHubPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        // Shared services for every action. Null until Load runs.
        public Hub Hub { get; private set; }

        public override void Load()
        {
            var port = this.TryGetPluginSetting("port", out var value) && Int32.TryParse(value, out var parsed) ? parsed : Hub.DefaultPort;
            this.Hub = new Hub(port);
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
