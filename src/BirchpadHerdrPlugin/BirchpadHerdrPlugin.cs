namespace Loupedeck.BirchpadHerdrPlugin
{
    using System;

    using Birchpad.Core.Herdr;

    // Universal plugin: herdr runs inside a terminal app, so its keys can sit on any app profile.
    public class BirchpadHerdrPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public BirchpadHerdrPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        // Null until Load runs.
        public HerdrClient Herdr { get; private set; }

        public HerdrWatcher Watcher { get; private set; }

        public HerdrAgents Agents { get; private set; }

        // Raised when herdr's snapshot changes. It lives on the plugin, which exists before any action loads,
        // so an action that subscribes before Load has created the watcher still hears every change.
        public event EventHandler HerdrChanged;

        public override void Load()
        {
            // The "socket" plugin setting points at one herdr server; without it the client finds the running one.
            this.Herdr = new HerdrClient(this.TryGetPluginSetting("socket", out var socket) ? socket : null);
            this.Agents = new HerdrAgents(this.Herdr);
            this.Watcher = new HerdrWatcher(this.Herdr);
            this.Watcher.Changed += (sender, e) => this.HerdrChanged?.Invoke(this, EventArgs.Empty);
        }

        public override void Unload()
        {
            this.Watcher?.Dispose();
            this.Watcher = null;
            this.Herdr = null;
        }
    }
}
