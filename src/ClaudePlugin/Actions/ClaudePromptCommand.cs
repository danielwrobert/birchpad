namespace Loupedeck.ClaudePlugin.Actions
{
    using System;

    using DevDeck.Core.Mac;

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
