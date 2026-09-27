namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;

    using Loupedeck.BirchpadAgentHubPlugin.Services;

    // Base for Agent Hub keys: finds the hub and redraws when it changes.
    public abstract class HubCommand : PluginDynamicCommand
    {
        protected HubCommand(String displayName, String description, String groupName)
            : base(displayName, description, groupName)
        {
        }

        protected HubCommand()
            : base()
        {
        }

        protected Hub Hub => (this.Plugin as BirchpadAgentHubPlugin)?.Hub;

        protected override Boolean OnLoad()
        {
            if (this.Hub != null)
            {
                this.Hub.Changed += this.OnHubChanged;
            }

            return true;
        }

        protected override Boolean OnUnload()
        {
            if (this.Hub != null)
            {
                this.Hub.Changed -= this.OnHubChanged;
            }

            return true;
        }

        private void OnHubChanged(Object sender, EventArgs e) => this.ActionImageChanged();
    }
}
