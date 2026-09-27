namespace Loupedeck.BirchpadITermPlugin.Actions
{
    using Loupedeck.Tiles;

    // Opens a split in the current folder and starts an agent CLI there.
    public class LaunchAgentCommand : ITermCommand
    {
        public LaunchAgentCommand()
            : base("Launch agent", "Start Claude Code or Codex in a new split in the current folder.", "Agents")
        {
            this.Add("claude", "New claude", "Agents", TileRenderer.Working, t => t.SplitAndRun(vertical: true, "claude", t.CurrentPath()));
            this.Add("codex", "New codex", "Agents", TileRenderer.Done, t => t.SplitAndRun(vertical: true, "codex", t.CurrentPath()));
        }
    }
}
