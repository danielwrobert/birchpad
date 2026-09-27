namespace Loupedeck.BirchpadClaudePlugin.Actions
{
    using System;
    using System.Threading;

    using Birchpad.Core.Mac;

    using Loupedeck.Tiles;

    // Claude Desktop commands. Deep links where the app has one, otherwise menu clicks by path
    // (docs/reference/menus-com.anthropic.claudefordesktop.txt), and shortcuts for items whose name changes.
    public class AppCommand : ActionListCommand
    {
        private const String Bundle = MacAutomation.ClaudeBundleId;

        private readonly MacAutomation mac = new MacAutomation();

        public AppCommand()
            : base("Claude", "Chats, Code sessions, panels, and navigation in Claude Desktop.", "App")
        {
            var idle = TileRenderer.Idle;
            this.Link("new-chat", "New chat", TileRenderer.Done, "claude://claude.ai/new");
            this.Link("new-code", "New code", TileRenderer.Working, "claude://code/new");
            this.Link("needs-input", "Needs me", TileRenderer.Approval, "claude://code/needs-input?source=desktop_action");
            this.Menu("chat-cowork", "Chat", idle, "Go", "Chat and Cowork");
            this.Menu("code", "Code", idle, "Go", "Code");
            this.Menu("previous-session", "Prev", idle, "Go", "Previous Session");
            this.Menu("next-session", "Next", idle, "Go", "Next Session");
            this.Menu("search", "Search", idle, "Go", "Search…");
            this.Menu("palette", "Palette", idle, "View", "Command Palette…");
            this.Key("sidebar", "Sidebar", idle, 'b');
            this.Key("terminal", "Terminal", idle, 'j');
            this.Key("changes", "Changes", idle, 'd', shift: true);
            this.Key("side-chat", "Side chat", idle, ';');
            this.Menu("dictate", "Dictate", TileRenderer.Question, "Edit", "Start Dictation…");
            this.Add("stop", "Stop", "App", TileRenderer.Failed, this.Stop);
        }

        private void Link(String parameter, String label, BitmapColor color, String url) =>
            this.Add(parameter, label, "App", color, () => this.mac.OpenUrl(url));

        private void Menu(String parameter, String label, BitmapColor color, params String[] path) =>
            this.Add(parameter, label, "App", color, () => this.mac.ClickMenu(Bundle, path));

        private void Key(String parameter, String label, BitmapColor color, Char key, Boolean shift = false) =>
            this.Add(parameter, label, "App", color, () => this.mac.CommandKey(Bundle, key, shift));

        // Escape stops the running turn in the front session.
        private Boolean Stop()
        {
            if (!this.mac.Activate(Bundle))
            {
                return false;
            }

            Thread.Sleep(300);
            return this.mac.SendEscape();
        }
    }
}
