namespace Loupedeck.BirchpadITermPlugin
{
    using System;
    using System.IO;

    using Birchpad.Core.Mac;

    // Linked to iTerm, so its keys appear when iTerm is in front.
    public class BirchpadITermPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => false;

        public BirchpadITermPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        // Where saved commands live; edit the file and the keys update.
        public static String CommandsFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Birchpad", "iterm-commands.json");

        public ITermController ITerm { get; private set; }

        public override void Load()
        {
            this.ITerm = new ITermController(new MacAutomation(message => PluginLog.Warning(message)));
        }

        public override void Unload()
        {
            this.ITerm = null;
        }
    }
}
