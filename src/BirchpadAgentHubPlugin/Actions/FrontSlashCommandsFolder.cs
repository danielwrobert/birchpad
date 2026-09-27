namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;

    using Birchpad.Core.Mac;

    using Loupedeck.Tiles;

    // The shared slash commands for any profile, including the default one: each is pasted into the app in front.
    public class FrontSlashCommandsFolder : SlashCommandsFolder
    {
        private readonly MacAutomation mac = new MacAutomation();

        public FrontSlashCommandsFolder()
            : base("Agent Hub")
        {
        }

        protected override Boolean Send(String text, Boolean submit) => this.mac.PasteIntoFront(text, submit);
    }
}
