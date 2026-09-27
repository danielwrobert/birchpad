namespace Loupedeck.Tiles
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Birchpad.Core.Config;

    // Slash commands from slash-commands.json, one key each, shared by every app so a team keeps one list.
    // Commands with "submit": false are typed for you to finish, for commands that take arguments.
    public static class SlashCommands
    {
        public static String File => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Birchpad", "slash-commands.json");

        // Builds one key per command; send types the text and presses Return when its flag says so.
        public static IReadOnlyList<MenuItem> Items(Func<String, Boolean, Boolean> send, Action<String> warn)
        {
            var commands = SavedEntries.LoadSlashCommands(File, out var error);
            if (error != null)
            {
                warn?.Invoke($"Slash commands not loaded from {File}: {error}");
            }

            return commands.Select(c =>
            {
                var text = c.Command;
                var submit = c.Submit;
                return new MenuItem("sc:" + c.Name, submit ? c.Name : c.Name + " …")
                {
                    Color = submit ? TileRenderer.Working : TileRenderer.Question,
                    Run = () => send(text, submit),
                };
            }).ToList();
        }
    }

    // A folder of the shared slash commands; each app says how its commands are typed.
    public abstract class SlashCommandsFolder : MenuFolder
    {
        protected SlashCommandsFolder(String groupName)
            : base("Slash commands", groupName)
        {
        }

        protected abstract Boolean Send(String text, Boolean submit);

        protected override IReadOnlyList<MenuItem> Root() => SlashCommands.Items(this.Send, m => this.Plugin?.Log?.Warning(m));
    }
}
