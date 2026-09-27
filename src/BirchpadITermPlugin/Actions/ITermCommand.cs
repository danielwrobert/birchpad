namespace Loupedeck.BirchpadITermPlugin.Actions
{
    using System;

    using Birchpad.Core.Mac;

    using Loupedeck.Tiles;

    // An action list whose options drive iTerm.
    public abstract class ITermCommand : ActionListCommand
    {
        protected ITermCommand(String displayName, String description, String groupName)
            : base(displayName, description, groupName)
        {
        }

        protected ITermController ITerm => (this.Plugin as BirchpadITermPlugin)?.ITerm;

        protected void Add(String parameter, String label, String group, BitmapColor color, Func<ITermController, Boolean> run) =>
            this.Add(parameter, label, group, color, () => this.ITerm is { } iterm && run(iterm));
    }
}
