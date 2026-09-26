namespace Loupedeck.ClaudePlugin
{
    using System;

    using DevDeck.Core.Mac;

    public class ClaudeApplication : ClientApplication
    {
        protected override String GetProcessName() => "Claude";

        protected override String GetBundleName() => MacAutomation.ClaudeBundleId;

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Installed;
    }
}
