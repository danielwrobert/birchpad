namespace Loupedeck.ITermPlugin.Actions
{
    using Loupedeck.Tiles;

    // Tabs, splits, buffer, and broadcast input.
    public class PaneCommand : ITermCommand
    {
        public PaneCommand()
            : base("Pane", "Open tabs and splits, clear the buffer, or broadcast input.", "Panes")
        {
            var color = TileRenderer.Idle;
            this.Add("new-tab", "New tab", "Panes", color, t => t.NewTab());
            this.Add("split-vertical", "Split right", "Panes", color, t => t.ClickMenu("Shell", "Split Vertically with Current Profile"));
            this.Add("split-horizontal", "Split down", "Panes", color, t => t.ClickMenu("Shell", "Split Horizontally with Current Profile"));
            this.Add("clear-buffer", "Clear", "Panes", color, t => t.ClickMenu("Edit", "Clear Buffer"));
            this.Add("broadcast-tab", "Broadcast tab", "Panes", TileRenderer.Approval, t => t.ClickMenu("Shell", "Broadcast Input", "Broadcast Input to All Panes in Current Tab"));
            this.Add("broadcast-off", "Broadcast off", "Panes", color, t => t.ClickMenu("Shell", "Broadcast Input", "Send Input to Current Session Only"));
        }
    }
}
