namespace Loupedeck.ITermPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using DevDeck.Core.Config;

    using Loupedeck.Tiles;

    // One key per entry in iterm-commands.json; editing the file updates the keys.
    public class SavedCommandCommand : FileBackedCommand
    {
        public SavedCommandCommand()
            : base("Saved command", "Run a saved shell command in the current session.", "Commands")
        {
        }

        protected override String FilePath => ITermPlugin.CommandsFile;

        protected override IEnumerable<(String Parameter, String Label, String Group, BitmapColor Color, Func<Boolean> Run)> Load(out String error)
        {
            var commands = SavedEntries.LoadCommands(this.FilePath, out error);
            if (error != null)
            {
                return null;
            }

            return commands.Select(c =>
            {
                var line = c.Command;
                return (c.Name, c.Name, "Commands", TileRenderer.Idle, (Func<Boolean>)(() => (this.Plugin as ITermPlugin)?.ITerm is { } iterm && iterm.RunLine(line)));
            }).ToList();
        }
    }
}
