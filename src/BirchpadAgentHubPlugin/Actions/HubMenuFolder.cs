namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;

    using Loupedeck.BirchpadAgentHubPlugin.Services;
    using Loupedeck.Tiles;

    // A menu folder that reads the hub and redraws whenever it changes.
    public abstract class HubMenuFolder : MenuFolder
    {
        protected HubMenuFolder(String displayName)
            : base(displayName, "Agent Hub")
        {
        }

        protected Hub Hub => (this.Plugin as BirchpadAgentHubPlugin)?.Hub;

        public override Boolean Load()
        {
            if (this.Hub != null)
            {
                this.Hub.Changed += this.OnHubChanged;
            }

            return true;
        }

        public override Boolean Unload()
        {
            if (this.Hub != null)
            {
                this.Hub.Changed -= this.OnHubChanged;
            }

            return true;
        }

        private void OnHubChanged(Object sender, EventArgs e) => this.Refresh();
    }
}
