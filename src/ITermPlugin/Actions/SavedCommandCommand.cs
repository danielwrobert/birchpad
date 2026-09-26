namespace Loupedeck.ITermPlugin.Actions
{
    using System;

    using DevDeck.Core.Config;
    using DevDeck.Core.Mac;

    using Loupedeck.Tiles;

    // One key per entry in iterm-commands.json; editing the file updates the keys.
    public class SavedCommandCommand : FileBackedCommand
    {
        public SavedCommandCommand()
            : base("Saved command", "Run a saved shell command in the current session.", "Commands")
        {
        }

        protected override String FilePath => ITermPlugin.CommandsFile;

        protected override String Rebuild()
        {
            var commands = SavedEntries.LoadCommands(this.FilePath, out var error);
            if (error != null)
            {
                return error;
            }

            this.ClearEntries();
            foreach (var command in commands)
            {
                var line = command.Command;
                this.Add(command.Name, command.Name, "Commands", TileRenderer.Idle, () => (this.Plugin as ITermPlugin)?.ITerm is { } iterm && iterm.RunLine(line));
            }

            return null;
        }
    }
}
