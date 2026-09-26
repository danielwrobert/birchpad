namespace Loupedeck.Tiles
{
    using System;
    using System.IO;

    // An action list rebuilt from a JSON file whenever the file is saved.
    public abstract class FileBackedCommand : ActionListCommand
    {
        private FileSystemWatcher watcher;

        protected FileBackedCommand(String displayName, String description, String groupName)
            : base(displayName, description, groupName)
        {
        }

        protected abstract String FilePath { get; }

        // Clears and re-adds the options from the file. Returns an error message, or null.
        protected abstract String Rebuild();

        protected override Boolean OnLoad()
        {
            this.Reload();
            this.watcher = new FileSystemWatcher(Path.GetDirectoryName(this.FilePath), Path.GetFileName(this.FilePath))
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
            var error = this.Rebuild();
            if (error != null)
            {
                this.Plugin?.Log?.Warning($"{this.DisplayName}: not loaded from {this.FilePath}: {error}");
                return;
            }

            this.ParametersChanged();
        }
    }
}
