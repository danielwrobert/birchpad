namespace Loupedeck.Tiles
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    // A key with one option per parameter. Each option runs off the key-press thread,
    // because AppleScript and app switching can take a moment.
    public abstract class ActionListCommand : PluginDynamicCommand
    {
        private readonly Dictionary<String, Entry> entries = new Dictionary<String, Entry>(StringComparer.Ordinal);

        protected ActionListCommand(String displayName, String description, String groupName)
        {
            this.DisplayName = displayName;
            this.Description = description;
            this.GroupName = groupName;
        }

        protected void Add(String parameter, String label, String group, BitmapColor color, Func<Boolean> run)
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
            if (!this.entries.TryGetValue(actionParameter, out var entry))
            {
                return;
            }

            var log = this.Plugin?.Log;
            Task.Run(() =>
            {
                try
                {
                    if (!entry.Run())
                    {
                        log?.Warning($"{this.DisplayName}: '{actionParameter}' failed");
                    }
                }
                catch (Exception ex)
                {
                    log?.Error(ex, $"{this.DisplayName}: '{actionParameter}' threw");
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
            public Entry(String label, BitmapColor color, Func<Boolean> run)
            {
                this.Label = label;
                this.Color = color;
                this.Run = run;
            }

            public String Label { get; }

            public BitmapColor Color { get; }

            public Func<Boolean> Run { get; }
        }
    }
}
