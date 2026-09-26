namespace Loupedeck.Tiles
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    // An action list rebuilt from a JSON file whenever the file is saved.
    // Editors raise several change events per save, so reloads are debounced and run one at a time.
    public abstract class FileBackedCommand : ActionListCommand
    {
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

        private readonly Object reloadGate = new Object();
        private FileSystemWatcher watcher;
        private Timer debounce;

        protected FileBackedCommand(String displayName, String description, String groupName)
            : base(displayName, description, groupName)
        {
        }

        protected abstract String FilePath { get; }

        // Reads the file into options. Returns null and sets error when the file cannot be used.
        protected abstract IEnumerable<(String Parameter, String Label, String Group, BitmapColor Color, Func<Boolean> Run)> Load(out String error);

        protected override Boolean OnLoad()
        {
            this.Reload();
            this.debounce = new Timer(_ => this.Reload(), null, Timeout.Infinite, Timeout.Infinite);
            this.watcher = new FileSystemWatcher(Path.GetDirectoryName(this.FilePath), Path.GetFileName(this.FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            this.watcher.Changed += (_, _) => this.ScheduleReload();
            this.watcher.Created += (_, _) => this.ScheduleReload();
            this.watcher.Renamed += (_, _) => this.ScheduleReload();
            return true;
        }

        protected override Boolean OnUnload()
        {
            this.watcher?.Dispose();
            this.watcher = null;
            this.debounce?.Dispose();
            this.debounce = null;
            return true;
        }

        private void ScheduleReload() => this.debounce?.Change(Debounce, Timeout.InfiniteTimeSpan);

        // Runs on the plugin thread at load, then on timer threads; never lets an exception escape.
        private void Reload()
        {
            lock (this.reloadGate)
            {
                try
                {
                    var options = this.Load(out var error);
                    if (options == null)
                    {
                        this.Plugin?.Log?.Warning($"{this.DisplayName}: not loaded from {this.FilePath}: {error}");
                        return;
                    }

                    this.ReplaceEntries(options);
                    this.ParametersChanged();
                }
                catch (Exception ex)
                {
                    this.Plugin?.Log?.Error(ex, $"{this.DisplayName}: reload from {this.FilePath} failed");
                }
            }
        }
    }
}
