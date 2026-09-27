namespace Loupedeck.Tiles
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Birchpad.Core.Config;
    using Birchpad.Core.Mac;

    // A Commands key that opens the shared slash commands, then one key per saved prompt in prompts.json;
    // pressing a prompt pastes it into the app and sends it.
    // Claude and ChatGPT share the file, so a prompt written once works in both. The file is read each time the folder opens.
    public abstract class PromptsFolder : MenuFolder
    {
        private readonly MacAutomation mac = new MacAutomation();

        protected PromptsFolder()
            : base("Prompts", "Prompts")
        {
        }

        public static String PromptsFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Birchpad", "prompts.json");

        protected abstract String BundleId { get; }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var prompts = SavedEntries.LoadPrompts(PromptsFile, out var error);
            if (error != null)
            {
                this.Plugin?.Log?.Warning($"Prompts not loaded from {PromptsFile}: {error}");
            }

            // The first key opens the shared slash commands, so the home page needs no key of its own for them.
            var commands = new MenuItem("slash-commands", "Commands ›")
            {
                Color = TileRenderer.Working,
                Open = () => SlashCommands.Items((text, submit) => this.mac.PasteInto(this.BundleId, text, submit), m => this.Plugin?.Log?.Warning(m)),
            };

            return new[] { commands }.Concat(prompts.Select(p =>
            {
                var text = p.Text;
                return new MenuItem("p:" + p.Name, p.Name)
                {
                    Color = TileRenderer.Question,
                    Run = () => this.mac.PasteInto(this.BundleId, text, submit: true),
                };
            })).ToList();
        }
    }
}
