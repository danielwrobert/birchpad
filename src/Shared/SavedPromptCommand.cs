namespace Loupedeck.Tiles
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Birchpad.Core.Config;
    using Birchpad.Core.Mac;

    // One key per saved prompt in prompts.json, pasted into an agent app and sent.
    // Claude and Codex share the file, so a prompt written once works in both.
    public abstract class SavedPromptCommand : FileBackedCommand
    {
        private readonly MacAutomation mac = new MacAutomation();

        protected SavedPromptCommand(String description)
            : base("Saved prompt", description, "Prompts")
        {
        }

        public static String PromptsFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Birchpad", "prompts.json");

        protected abstract String BundleId { get; }

        protected override String FilePath => PromptsFile;

        protected override IEnumerable<(String Parameter, String Label, String Group, BitmapColor Color, Func<Boolean> Run)> Load(out String error)
        {
            var prompts = SavedEntries.LoadPrompts(this.FilePath, out error);
            if (error != null)
            {
                return null;
            }

            return prompts.Select(p =>
            {
                var text = p.Text;
                return (p.Name, p.Name, "Prompts", TileRenderer.Question, (Func<Boolean>)(() => this.mac.PasteInto(this.BundleId, text, submit: true)));
            }).ToList();
        }
    }
}
