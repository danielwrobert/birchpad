namespace Loupedeck.Tiles
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    // A key with one option per parameter. Each option runs off the key-press thread,
    // because AppleScript and app switching can take a moment.
    // Options can be replaced from another thread (a settings file reload), so reads and writes take a lock.
    public abstract class ActionListCommand : PluginDynamicCommand
    {
        private readonly Object gate = new Object();
        private readonly Dictionary<String, Entry> entries = new Dictionary<String, Entry>(StringComparer.Ordinal);

        protected ActionListCommand(String displayName, String description, String groupName)
        {
            this.DisplayName = displayName;
            this.Description = description;
            this.GroupName = groupName;
        }

        protected void Add(String parameter, String label, String group, BitmapColor color, Func<Boolean> run)
        {
            lock (this.gate)
            {
                this.entries[parameter] = new Entry(label, color, run);
                this.AddParameter(parameter, label, group);
            }
        }

        // Swaps every option at once, so a key press never sees a half-built list.
        protected void ReplaceEntries(IEnumerable<(String Parameter, String Label, String Group, BitmapColor Color, Func<Boolean> Run)> options)
        {
            lock (this.gate)
            {
                this.entries.Clear();
                this.RemoveAllParameters();
                foreach (var option in options)
                {
                    this.entries[option.Parameter] = new Entry(option.Label, option.Color, option.Run);
                    this.AddParameter(option.Parameter, option.Label, option.Group);
                }
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!this.TryGet(actionParameter, out var entry))
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
            this.TryGet(actionParameter, out var entry) ? entry.Label : actionParameter;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            this.TryGet(actionParameter, out var entry)
                ? TileRenderer.Command(entry.Label, entry.Color, imageSize)
                : TileRenderer.Command(String.Empty, TileRenderer.Empty, imageSize);

        private Boolean TryGet(String actionParameter, out Entry entry)
        {
            lock (this.gate)
            {
                return this.entries.TryGetValue(actionParameter, out entry);
            }
        }

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
