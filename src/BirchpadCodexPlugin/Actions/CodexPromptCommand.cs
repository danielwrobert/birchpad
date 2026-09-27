namespace Loupedeck.BirchpadCodexPlugin.Actions
{
    using System;

    using Birchpad.Core.Mac;

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
