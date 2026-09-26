namespace Loupedeck.CodexPlugin
{
    using System;

    using DevDeck.Core.Mac;

    public class CodexApplication : ClientApplication
    {
        protected override String GetProcessName() => "ChatGPT";

        protected override String GetBundleName() => MacAutomation.CodexBundleId;

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Installed;
    }
}
