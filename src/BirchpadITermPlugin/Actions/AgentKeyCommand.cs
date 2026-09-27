namespace Loupedeck.BirchpadITermPlugin.Actions
{
    using Birchpad.Core.Mac;

    using Loupedeck.Tiles;

    // Keys and slash commands for the Claude Code or Codex CLI in the current session.
    public class AgentKeyCommand : ITermCommand
    {
        public AgentKeyCommand()
            : base("Agent key", "Send a key or slash command to the agent in the current session.", "Agents")
        {
            this.Add("escape", "Esc", "Agents", TileRenderer.Failed, t => t.SendRaw(ITermController.Escape));
            this.Add("shift-tab", "Mode", "Agents", TileRenderer.Question, t => t.SendRaw(ITermController.ShiftTab));
            this.Add("control-c", "Ctrl-C", "Agents", TileRenderer.Failed, t => t.SendRaw(ITermController.ControlC));

            var claude = TileRenderer.Working;
            this.Add("claude-clear", "/clear", "Claude Code", claude, t => t.RunLine("/clear"));
            this.Add("claude-compact", "/compact", "Claude Code", claude, t => t.RunLine("/compact"));
            this.Add("claude-model", "/model", "Claude Code", claude, t => t.RunLine("/model"));
            this.Add("claude-remote", "/remote-control", "Claude Code", claude, t => t.RunLine("/remote-control"));

            var codex = TileRenderer.Done;
            this.Add("codex-new", "/new", "Codex", codex, t => t.RunLine("/new"));
            this.Add("codex-compact", "/compact", "Codex", codex, t => t.RunLine("/compact"));
            this.Add("codex-model", "/model", "Codex", codex, t => t.RunLine("/model"));
            this.Add("codex-status", "/status", "Codex", codex, t => t.RunLine("/status"));
        }
    }
}
