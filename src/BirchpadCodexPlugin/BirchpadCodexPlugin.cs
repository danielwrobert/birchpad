namespace Loupedeck.BirchpadCodexPlugin
{
    using System;

    // Linked to the ChatGPT app, which hosts Codex, so its keys appear when ChatGPT is in front.
    public class BirchpadCodexPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => false;

        public BirchpadCodexPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
        }

        public override void Unload()
        {
        }
    }
}
