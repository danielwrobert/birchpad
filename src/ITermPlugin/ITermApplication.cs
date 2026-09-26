namespace Loupedeck.ITermPlugin
{
    using System;

    using DevDeck.Core.Mac;

    public class ITermApplication : ClientApplication
    {
        protected override String GetProcessName() => "iTerm2";

        protected override String GetBundleName() => MacAutomation.ItermBundleId;

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Installed;
    }
}
