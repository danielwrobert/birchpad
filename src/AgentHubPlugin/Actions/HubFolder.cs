namespace Loupedeck.AgentHubPlugin.Actions
{
    using System;

    using Loupedeck.AgentHubPlugin.Services;

    // Base for Agent Hub folders: finds the hub and redraws when it changes.
    public abstract class HubFolder : PluginDynamicFolder
    {
        protected Hub Hub => (this.Plugin as AgentHubPlugin)?.Hub;

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

        private void OnHubChanged(Object sender, EventArgs e)
        {
            this.ButtonActionNamesChanged();
            this.CommandImageChanged(null);
        }
    }
}
