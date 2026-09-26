namespace Loupedeck.ITermPlugin.Actions
{
    using System;
    using System.IO;

    using DevDeck.Core.Config;

    using Loupedeck.Tiles;

    // One key per entry in iterm-commands.json; editing the file updates the keys.
    public class SavedCommandCommand : ITermCommand
    {
        private FileSystemWatcher watcher;

        public SavedCommandCommand()
            : base("Saved command", "Run a saved shell command in the current session.", "Commands")
        {
        }

        protected override Boolean OnLoad()
        {
            this.Reload();
            var file = ITermPlugin.CommandsFile;
            this.watcher = new FileSystemWatcher(Path.GetDirectoryName(file), Path.GetFileName(file))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            this.watcher.Changed += (_, _) => this.Reload();
            this.watcher.Created += (_, _) => this.Reload();
            this.watcher.Renamed += (_, _) => this.Reload();
            return true;
        }

        protected override Boolean OnUnload()
        {
            this.watcher?.Dispose();
            this.watcher = null;
            return true;
        }

        private void Reload()
        {
            var commands = SavedCommands.LoadOrCreate(ITermPlugin.CommandsFile, out var error);
            if (error != null)
            {
                PluginLog.Warning($"Saved commands not loaded from {ITermPlugin.CommandsFile}: {error}");
                return;
            }

            this.ClearEntries();
            foreach (var command in commands)
            {
                var line = command.Command;
                this.Add(command.Name, command.Name, "Commands", TileRenderer.Idle, t => t.RunLine(line));
            }

            this.ParametersChanged();
        }
    }
}
