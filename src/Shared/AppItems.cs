namespace Loupedeck.Tiles
{
    using System;

    using Birchpad.Core.Mac;

    // Builds the kinds of keys the app plugins use: deep links, menu clicks, shortcuts, and typed commands.
    public static class AppItems
    {
        private static readonly MacAutomation Mac = new MacAutomation();

        public static MenuItem Link(String id, String label, String url, BitmapColor color = default) =>
            new MenuItem(id, label) { Color = Pick(color), Run = () => Mac.OpenUrl(url) };

        // A page in Claude Desktop's Chat sidebar that no link opens, such as Artifacts or Design.
        public static MenuItem ClaudeSidebar(String id, String label, String page, BitmapColor color = default) =>
            new MenuItem(id, label) { Color = Pick(color), Run = () => Mac.OpenClaudeSidebarPage(page) };

        public static MenuItem Menu(String id, String label, String bundleId, params String[] path) =>
            new MenuItem(id, label) { Run = () => Mac.ClickMenu(bundleId, path) };

        public static MenuItem Shortcut(String id, String label, String bundleId, Char key, Boolean shift = false) =>
            new MenuItem(id, label) { Run = () => Mac.CommandKey(bundleId, key, shift) };

        // Types a slash command into the app's focused field and sends it.
        public static MenuItem Command(String id, String command, String bundleId) =>
            new MenuItem(id, command) { Color = TileRenderer.Working, Run = () => Mac.PasteInto(bundleId, command, submit: true) };

        public static MenuItem Stop(String bundleId) =>
            new MenuItem("stop", "Stop")
            {
                Color = TileRenderer.Failed,
                Run = () =>
                {
                    if (!Mac.Activate(bundleId))
                    {
                        return false;
                    }

                    System.Threading.Thread.Sleep(300);
                    return Mac.SendEscape();
                },
            };

        private static BitmapColor Pick(BitmapColor color) => color.Equals(default(BitmapColor)) ? TileRenderer.Idle : color;
    }
}
