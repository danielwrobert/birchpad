namespace Loupedeck.BirchpadClaudePlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Birchpad.Core.Claude;
    using Birchpad.Core.Mac;

    using Loupedeck.Tiles;

    // Claude Desktop home page categories. Menu paths come from docs/reference/menus-com.anthropic.claudefordesktop.txt.
    internal static class Claude
    {
        public const String Bundle = MacAutomation.ClaudeBundleId;
    }

    public class ClaudeNewFolder : MenuFolder
    {
        public ClaudeNewFolder()
            : base("New", "Claude")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            AppItems.Link("chat", "Chat", "claude://claude.ai/new", TileRenderer.Done),
            AppItems.Link("code", "Code session", "claude://code/new", TileRenderer.Working),
            AppItems.Menu("cowork", "Cowork", Claude.Bundle, "Go", "Chat and Cowork"),
            AppItems.ClaudeSidebar("design", "Design", "Design"),
        };
    }

    public class ClaudePromptsFolder : PromptsFolder
    {
        protected override String BundleId => Claude.Bundle;
    }

    // claude.ai views that no local file lists. Claude Desktop accepts only fixed claude:// routes:
    // "chat" and "project" with no ID fall back to Recents and the projects list. No route opens Artifacts,
    // so that key presses the Artifacts button in the Chat sidebar.
    public class ClaudeChatsFolder : MenuFolder
    {
        public ClaudeChatsFolder()
            : base("Chats", "Claude")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            AppItems.Link("recents", "Recent chats", "claude://claude.ai/chat"),
            AppItems.Link("projects", "Projects", "claude://claude.ai/project"),
            AppItems.ClaudeSidebar("artifacts", "Artifacts", "Artifacts"),
        };
    }

    // Folders Claude Desktop Code sessions ran in, most recent first; press one to start a Code session there.
    public class ClaudeProjectsFolder : MenuFolder
    {
        private readonly DesktopSessionIndex index = new DesktopSessionIndex();

        public ClaudeProjectsFolder()
            : base("Projects", "Claude")
        {
        }

        // Root runs on a worker thread, so the rescan never blocks the key press.
        protected override IReadOnlyList<MenuItem> Root()
        {
            this.index.Refresh();
            return this.index.Projects
                .Select(p => AppItems.Link("f:" + p.Path, p.Name, "claude://code/new?folder=" + Uri.EscapeDataString(p.Path), TileRenderer.Working))
                .ToList();
        }
    }

    public class ClaudeSettingsFolder : MenuFolder
    {
        public ClaudeSettingsFolder()
            : base("Settings", "Claude")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            AppItems.Menu("settings", "Settings", Claude.Bundle, "Claude", "Settings…"),
            AppItems.Link("connectors", "Connectors", "claude://claude.ai/settings/connectors"),
            AppItems.Shortcut("sidebar", "Sidebar", Claude.Bundle, 'b'),
            AppItems.Shortcut("terminal", "Terminal", Claude.Bundle, 'j'),
            AppItems.Shortcut("changes", "Changes", Claude.Bundle, 'd', shift: true),
            AppItems.Shortcut("side-chat", "Side chat", Claude.Bundle, ';'),
            AppItems.Command("mcp", "/mcp", Claude.Bundle),
            AppItems.Command("plugins", "/plugins", Claude.Bundle),
            AppItems.Command("model", "/model", Claude.Bundle),
            AppItems.Command("config", "/config", Claude.Bundle),
            AppItems.Command("hooks", "/hooks", Claude.Bundle),
            AppItems.Stop(Claude.Bundle),
        };
    }
}
