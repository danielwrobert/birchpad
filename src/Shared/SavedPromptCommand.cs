namespace Loupedeck.Tiles
{
    using System;
    using System.IO;

    using DevDeck.Core.Config;
    using DevDeck.Core.Mac;

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
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevDeck", "prompts.json");

        protected abstract String BundleId { get; }

        protected override String FilePath => PromptsFile;

        protected override String Rebuild()
        {
            var prompts = SavedEntries.LoadPrompts(this.FilePath, out var error);
            if (error != null)
            {
                return error;
            }

            this.ClearEntries();
            foreach (var prompt in prompts)
            {
                var text = prompt.Text;
                this.Add(prompt.Name, prompt.Name, "Prompts", TileRenderer.Question, () => this.mac.PasteInto(this.BundleId, text, submit: true));
            }

            return null;
        }
    }
}
