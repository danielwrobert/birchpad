namespace Loupedeck.BirchpadITermPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Birchpad.Core.Config;
    using Birchpad.Core.Http;
    using Birchpad.Core.Mac;

    using Loupedeck.Tiles;

    // iTerm home page categories, in the same keys as Claude's where there is an equivalent.
    public abstract class ITermFolder : MenuFolder
    {
        protected ITermFolder(String displayName)
            : base(displayName, "iTerm")
        {
        }

        protected ITermController ITerm => (this.Plugin as BirchpadITermPlugin)?.ITerm;

        protected MenuItem Do(String id, String label, BitmapColor color, Func<ITermController, Boolean> run) =>
            new MenuItem(id, label) { Color = color, Run = () => this.ITerm is { } iterm && run(iterm) };
    }

    // Opens a new tab, like Shell > New Tab.
    public class ITermNewTabCommand : PluginDynamicCommand
    {
        public ITermNewTabCommand()
            : base("New tab", "Open a new iTerm tab.", "iTerm")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            var iterm = (this.Plugin as BirchpadITermPlugin)?.ITerm;
            if (iterm != null)
            {
                System.Threading.Tasks.Task.Run(() => iterm.ClickMenu("Shell", "New Tab"));
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            TileRenderer.Command("New tab", TileRenderer.Idle, imageSize);
    }

    // Saved commands from iterm-commands.json; commands with "submit": false are typed for you to finish.
    public class ITermCommandsFolder : ITermFolder
    {
        public ITermCommandsFolder()
            : base("Commands")
        {
        }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var commands = SavedEntries.LoadCommands(BirchpadITermPlugin.CommandsFile, out var error);
            if (error != null)
            {
                this.Plugin?.Log?.Warning($"Commands not loaded from {BirchpadITermPlugin.CommandsFile}: {error}");
            }

            return commands.Select(c =>
            {
                var line = c.Command;
                return c.Submit
                    ? this.Do("c:" + c.Name, c.Name, TileRenderer.Idle, t => t.RunLine(line))
                    : this.Do("c:" + c.Name, c.Name + " …", TileRenderer.Question, t => t.SendRaw(line));
            }).ToList();
        }
    }

    // Every iTerm pane by folder, with the status ring of the agent running there. Press one to focus it.
    public class ITermTabsFolder : ITermFolder
    {
        private readonly HubClient hub = new HubClient();

        public ITermTabsFolder()
            : base("Tabs")
        {
        }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var iterm = this.ITerm;
            if (iterm == null)
            {
                return Array.Empty<MenuItem>();
            }

            var sessions = this.hub.Sessions();
            var agents = TerminalAgents.Detect();
            return iterm.ListSessions().Select(pane =>
            {
                var kind = agents.TryGetValue(pane.Tty, out var k) ? k : AgentKind.None;
                var source = kind == AgentKind.Codex ? "Codex" : "ClaudeCli";
                var status = kind == AgentKind.None ? null : HubClient.AtPath(sessions.Where(s => s.Source == source).ToList(), pane.Path);
                var ring = kind == AgentKind.None ? TileRenderer.Empty : status == null ? TileRenderer.Idle : RingFor(status.State);
                var header = kind switch { AgentKind.Claude => "CLAUDE", AgentKind.Codex => "CODEX", _ => "SHELL" };
                var footer = kind == AgentKind.None ? String.Empty : status?.State ?? "running";
                var id = pane.Id;
                return new MenuItem("t:" + id, pane.FolderName)
                {
                    Image = size => TileRenderer.Ring(header, pane.FolderName, footer, ring, size),
                    Run = () => iterm.Focus(id),
                };
            }).ToList();
        }

        private static BitmapColor RingFor(String state) => state switch
        {
            "Working" => TileRenderer.Working,
            "NeedsApproval" => TileRenderer.Approval,
            "NeedsInput" => TileRenderer.Question,
            "Done" => TileRenderer.Done,
            "Failed" => TileRenderer.Failed,
            _ => TileRenderer.Idle,
        };
    }

    // The shared slash commands, typed into the current pane.
    public class ITermSlashCommandsFolder : SlashCommandsFolder
    {
        public ITermSlashCommandsFolder()
            : base("iTerm")
        {
        }

        protected override Boolean Send(String text, Boolean submit)
        {
            var iterm = (this.Plugin as BirchpadITermPlugin)?.ITerm;
            return iterm != null && (submit ? iterm.RunLine(text) : iterm.SendRaw(text));
        }
    }

    // Keys named by what they do; each sends the right command for the agent running in the current pane.
    public class ITermAgentKeysFolder : ITermFolder
    {
        public ITermAgentKeysFolder()
            : base("Agent keys")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            this.Do("escape", "Esc", TileRenderer.Failed, t => t.SendRaw(ITermController.Escape)),
            this.Do("mode", "Mode", TileRenderer.Question, t => t.SendRaw(ITermController.ShiftTab)),
            this.Do("control-c", "Ctrl-C", TileRenderer.Failed, t => t.SendRaw(ITermController.ControlC)),
            this.Slash("clear", "Clear", "/clear", "/clear"),
            this.Slash("compact", "Compact", "/compact", "/compact"),
            this.Slash("model", "Model", "/model", "/model"),
            this.Slash("context", "Context", "/context", "/status"),
            this.Slash("usage", "Usage", "/usage", "/status"),
            this.Slash("mcp", "/mcp", "/mcp", "/mcp"),
            this.Slash("plugins", "/plugins", "/plugins", "/plugins"),
            this.Slash("config", "/config", "/config", "/config"),
            this.Slash("hooks", "/hooks", "/hooks", "/hooks"),
        };

        private MenuItem Slash(String id, String label, String claude, String codex) =>
            this.Do(id, label, TileRenderer.Working, t => t.RunLine(t.CurrentAgent() == AgentKind.Codex ? codex : claude));
    }

    // iTerm's own settings and window controls.
    public class ITermSettingsFolder : ITermFolder
    {
        public ITermSettingsFolder()
            : base("Settings")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            this.Do("settings", "Settings", TileRenderer.Idle, t => t.ClickMenu("iTerm2", "Settings…")),
            this.Do("split-right", "Split right", TileRenderer.Idle, t => t.ClickMenu("Shell", "Split Vertically with Current Profile")),
            this.Do("split-down", "Split down", TileRenderer.Idle, t => t.ClickMenu("Shell", "Split Horizontally with Current Profile")),
            this.Do("clear", "Clear", TileRenderer.Idle, t => t.ClickMenu("Edit", "Clear Buffer")),
            this.Do("clear-scrollback", "Clear scrollback", TileRenderer.Idle, t => t.ClickMenu("Edit", "Clear Scrollback Buffer")),
            this.Do("full-screen", "Full screen", TileRenderer.Idle, t => t.ClickMenu("View", "Toggle Full Screen")),
            this.Do("broadcast", "Broadcast", TileRenderer.Approval, t => t.ClickMenu("Shell", "Broadcast Input", "Broadcast Input to All Panes in Current Tab")),
            this.Do("broadcast-off", "Broadcast off", TileRenderer.Idle, t => t.ClickMenu("Shell", "Broadcast Input", "Send Input to Current Session Only")),
        };
    }
}
