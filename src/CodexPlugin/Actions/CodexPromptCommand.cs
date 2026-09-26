namespace Loupedeck.CodexPlugin.Actions
{
    using System;

    using DevDeck.Core.Mac;

    using Loupedeck.Tiles;

    public class CodexPromptCommand : SavedPromptCommand
    {
        public CodexPromptCommand()
            : base("Paste a saved prompt into ChatGPT and send it.")
        {
        }

        protected override String BundleId => MacAutomation.CodexBundleId;
    }
}
