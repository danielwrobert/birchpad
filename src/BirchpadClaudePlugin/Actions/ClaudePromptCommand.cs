namespace Loupedeck.BirchpadClaudePlugin.Actions
{
    using System;

    using Birchpad.Core.Mac;

    using Loupedeck.Tiles;

    public class ClaudePromptCommand : SavedPromptCommand
    {
        public ClaudePromptCommand()
            : base("Paste a saved prompt into Claude and send it.")
        {
        }

        protected override String BundleId => MacAutomation.ClaudeBundleId;
    }
}
