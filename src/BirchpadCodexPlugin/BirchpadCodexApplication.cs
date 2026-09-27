namespace Loupedeck.BirchpadCodexPlugin
{
    using System;

    using Birchpad.Core.Mac;

    public class BirchpadCodexApplication : ClientApplication
    {
        protected override String GetProcessName() => "ChatGPT";

        protected override String GetBundleName() => MacAutomation.CodexBundleId;

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Installed;
    }
}
