namespace Loupedeck.ClaudePlugin
{
    using System;

    // Linked to Claude Desktop, so its keys appear when Claude is in front.
    public class ClaudePlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => false;

        public ClaudePlugin()
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
