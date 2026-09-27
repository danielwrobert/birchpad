namespace Loupedeck.BirchpadCodexPlugin.Actions
{
    using System;
    using System.Threading;

    using Birchpad.Core.Mac;

    using Loupedeck.Tiles;

    // ChatGPT app commands, clicked through its menus. Menu paths come from docs/reference/menus-com.openai.codex.txt.
    public class AppCommand : ActionListCommand
    {
        private readonly MacAutomation mac = new MacAutomation();

        public AppCommand()
            : base("ChatGPT", "Chats, panels, and navigation in the ChatGPT app.", "App")
        {
            var color = TileRenderer.Idle;
            this.Menu("new-chat", "New chat", TileRenderer.Done, "File", "New Chat");
            this.Menu("temporary-chat", "Temp chat", color, "File", "New Temporary Chat");
            this.Menu("previous-chat", "Prev chat", color, "View", "Previous Chat");
            this.Menu("next-chat", "Next chat", color, "View", "Next Chat");
            this.Menu("sidebar", "Sidebar", color, "View", "Toggle Sidebar");
            this.Menu("review-panel", "Review", color, "View", "Toggle Review Panel");
            this.Menu("file-tree", "Files", color, "View", "Toggle File Tree");
            this.Menu("terminal", "Terminal", color, "View", "Open Terminal");
            this.Menu("dictate", "Dictate", TileRenderer.Question, "Edit", "Start Dictation…");
            this.Add("stop", "Stop", "App", TileRenderer.Failed, this.Stop);
        }

        private void Menu(String parameter, String label, BitmapColor color, params String[] path) =>
            this.Add(parameter, label, "App", color, () => this.mac.ClickMenu(MacAutomation.CodexBundleId, path));

        // Escape stops the running turn; the app has to be in front to receive it.
        private Boolean Stop()
        {
            if (!this.mac.Activate(MacAutomation.CodexBundleId))
            {
                return false;
            }

            Thread.Sleep(300);
            return this.mac.SendEscape();
        }
    }
}
