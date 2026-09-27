namespace Loupedeck.BirchpadCodexPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Birchpad.Core.Codex;
    using Birchpad.Core.Mac;

    using Loupedeck.Tiles;

    // ChatGPT home page categories, in the same keys as Claude's. Menu paths come from docs/reference/menus-com.openai.codex.txt.
    internal static class Codex
    {
        public const String Bundle = MacAutomation.CodexBundleId;
    }

    public class CodexNewFolder : MenuFolder
    {
        public CodexNewFolder()
            : base("New", "ChatGPT")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            AppItems.Menu("chat", "Chat", Codex.Bundle, "File", "New Chat"),
            AppItems.Link("codex", "Codex session", "codex://threads/new", TileRenderer.Working),
        };
    }

    public class CodexPromptsFolder : PromptsFolder
    {
        protected override String BundleId => Codex.Bundle;
    }

    // ChatGPT lists chats in its sidebar; no local file lists them.
    public class CodexChatsFolder : MenuFolder
    {
        public CodexChatsFolder()
            : base("Chats", "ChatGPT")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            AppItems.Menu("recents", "Recent chats", Codex.Bundle, "View", "Toggle Sidebar"),
            AppItems.Menu("previous", "Prev chat", Codex.Bundle, "View", "Previous Chat"),
            AppItems.Menu("next", "Next chat", Codex.Bundle, "View", "Next Chat"),
        };
    }

    // Codex projects from its local state. The new-thread link takes no folder, so a project opens a new Codex session.
    public class CodexProjectsFolder : MenuFolder
    {
        private readonly CodexState state = new CodexState();
        private readonly MacAutomation mac = new MacAutomation();

        public CodexProjectsFolder()
            : base("Projects", "ChatGPT")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() =>
            this.state.Projects()
                .Select(p => new MenuItem("p:" + p.Id, p.Name)
                {
                    Image = size => TileRenderer.Ring("PROJECT", p.Name, "new session", TileRenderer.Working, size),
                    Run = () => this.mac.OpenUrl("codex://threads/new"),
                })
                .ToList();
    }

    // Codex automations exist in the app, but nothing local lists them yet, so this opens the app.
    public class CodexScheduledFolder : MenuFolder
    {
        private readonly MacAutomation mac = new MacAutomation();

        public CodexScheduledFolder()
            : base("Scheduled", "ChatGPT")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            new MenuItem("open", "Automations") { Color = TileRenderer.Working, Run = () => this.mac.Activate(Codex.Bundle) },
        };
    }

    public class CodexSettingsFolder : MenuFolder
    {
        public CodexSettingsFolder()
            : base("Settings", "ChatGPT")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            AppItems.Menu("settings", "Settings", Codex.Bundle, "ChatGPT", "Settings…"),
            AppItems.Link("connections", "Connections", "codex://settings/connections"),
            AppItems.Link("plugins", "Plugins", "codex://plugins/"),
            AppItems.Menu("sidebar", "Sidebar", Codex.Bundle, "View", "Toggle Sidebar"),
            AppItems.Menu("review", "Review", Codex.Bundle, "View", "Toggle Review Panel"),
            AppItems.Menu("files", "Files", Codex.Bundle, "View", "Toggle File Tree"),
            AppItems.Menu("terminal", "Terminal", Codex.Bundle, "View", "Open Terminal"),
            AppItems.Command("mcp", "/mcp", Codex.Bundle),
            AppItems.Command("model", "/model", Codex.Bundle),
            AppItems.Command("approvals", "/approvals", Codex.Bundle),
            AppItems.Command("status", "/status", Codex.Bundle),
            AppItems.Stop(Codex.Bundle),
        };
    }
}
