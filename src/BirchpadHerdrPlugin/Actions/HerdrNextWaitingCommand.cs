namespace Loupedeck.BirchpadHerdrPlugin.Actions
{
    using System;

    using Birchpad.Core.Herdr;

    using Loupedeck.Tiles;

    // Focuses the herdr agent that most needs you: blocked first, then finished and unseen.
    public class HerdrNextWaitingCommand : PluginDynamicCommand
    {
        public HerdrNextWaitingCommand()
            : base("Next waiting", "Focus the herdr agent that needs you.", "Herdr")
        {
        }

        private BirchpadHerdrPlugin Owner => this.Plugin as BirchpadHerdrPlugin;

        protected override Boolean OnLoad()
        {
            if (this.Owner?.Watcher is { } watcher)
            {
                watcher.Changed += this.OnChanged;
            }

            return true;
        }

        protected override Boolean OnUnload()
        {
            if (this.Owner?.Watcher is { } watcher)
            {
                watcher.Changed -= this.OnChanged;
            }

            return true;
        }

        private void OnChanged(Object sender, EventArgs e) => this.ActionImageChanged();

        protected override void RunCommand(String actionParameter)
        {
            var herdr = this.Owner?.Herdr;
            if (herdr == null)
            {
                return;
            }

            // Socket calls and open stay off the key-press thread.
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (herdr.Snapshot().MostUrgent() is { } agent)
                    {
                        herdr.FocusPane(agent.Id);
                        HerdrTerminal.Activate();
                        this.Owner?.Watcher?.Poke();
                    }
                }
                catch (HerdrException ex)
                {
                    PluginLog.Warning($"herdr next waiting: {ex.Code}: {ex.Message}");
                }
            });
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            Tile(this.Owner?.Watcher?.Latest, imageSize);

        public static BitmapImage Tile(HerdrSnapshot snapshot, PluginImageSize imageSize)
        {
            var agent = snapshot?.MostUrgent();
            return agent == null
                ? TileRenderer.Ring(String.Empty, "Next", "nothing waiting", TileRenderer.Idle, imageSize)
                : TileRenderer.Ring("NEXT", agent.Name ?? agent.FolderName, HerdrFolder.LabelFor(agent.Status), HerdrFolder.RingFor(agent.Status), imageSize);
        }
    }
}
