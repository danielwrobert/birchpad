namespace Loupedeck.BirchpadITermPlugin
{
    using System;

    using Birchpad.Core.Mac;

    public class BirchpadITermApplication : ClientApplication
    {
        protected override String GetProcessName() => "iTerm2";

        protected override String GetBundleName() => MacAutomation.ItermBundleId;

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Installed;
    }
}
