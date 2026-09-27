namespace Loupedeck.BirchpadClaudePlugin
{
    using System;

    using Birchpad.Core.Mac;

    public class BirchpadClaudeApplication : ClientApplication
    {
        protected override String GetProcessName() => "Claude";

        protected override String GetBundleName() => MacAutomation.ClaudeBundleId;

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Installed;
    }
}
