namespace Loupedeck.BirchpadAgentHubPlugin
{
    using System;

    // The plugin service expects one application class even from a universal plugin; this one links to no app.
    public class BirchpadAgentHubApplication : ClientApplication
    {
        protected override String GetProcessName() => String.Empty;

        protected override String GetBundleName() => String.Empty;

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
