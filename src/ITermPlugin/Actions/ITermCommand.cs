namespace Loupedeck.ITermPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using DevDeck.Core.Mac;

    using Loupedeck.Tiles;

    // A key that runs one iTerm operation per parameter. AppleScript can take a moment, so it runs off the key-press thread.
    public abstract class ITermCommand : PluginDynamicCommand
    {
        private readonly Dictionary<String, Entry> entries = new Dictionary<String, Entry>(StringComparer.Ordinal);

        protected ITermCommand(String displayName, String description, String groupName)
        {
            this.DisplayName = displayName;
            this.Description = description;
            this.GroupName = groupName;
        }

        protected ITermController ITerm => (this.Plugin as ITermPlugin)?.ITerm;

        protected void Add(String parameter, String label, String group, BitmapColor color, Func<ITermController, Boolean> run)
        {
            this.entries[parameter] = new Entry(label, color, run);
            this.AddParameter(parameter, label, group);
        }

        protected void ClearEntries()
        {
            this.entries.Clear();
            this.RemoveAllParameters();
        }

        protected override void RunCommand(String actionParameter)
        {
            var iterm = this.ITerm;
            if (iterm == null || !this.entries.TryGetValue(actionParameter, out var entry))
            {
                return;
            }

            Task.Run(() =>
            {
                if (!entry.Run(iterm))
                {
                    PluginLog.Warning($"iTerm action '{actionParameter}' failed");
                }
            });
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            this.entries.TryGetValue(actionParameter, out var entry) ? entry.Label : actionParameter;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            this.entries.TryGetValue(actionParameter, out var entry)
                ? TileRenderer.Command(entry.Label, entry.Color, imageSize)
                : TileRenderer.Command(String.Empty, TileRenderer.Empty, imageSize);

        private sealed class Entry
        {
            public Entry(String label, BitmapColor color, Func<ITermController, Boolean> run)
            {
                this.Label = label;
                this.Color = color;
                this.Run = run;
            }

            public String Label { get; }

            public BitmapColor Color { get; }

            public Func<ITermController, Boolean> Run { get; }
        }
    }
}
