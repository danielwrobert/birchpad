namespace Loupedeck.BirchpadHerdrPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Birchpad.Core.Herdr;

    using Loupedeck.Tiles;

    // A folder of herdr pages. Every page is read from herdr's live snapshot and redraws when it changes.
    public abstract class HerdrFolder : MenuFolder
    {
        protected HerdrFolder(String displayName)
            : base(displayName, "Herdr")
        {
        }

        private BirchpadHerdrPlugin Owner => this.Plugin as BirchpadHerdrPlugin;

        protected HerdrClient Herdr => this.Owner?.Herdr;

        public override Boolean Load()
        {
            if (this.Owner?.Watcher is { } watcher)
            {
                watcher.Changed += this.OnChanged;
            }

            return true;
        }

        public override Boolean Unload()
        {
            if (this.Owner?.Watcher is { } watcher)
            {
                watcher.Changed -= this.OnChanged;
            }

            return true;
        }

        private void OnChanged(Object sender, EventArgs e) => this.Refresh();

        // A key that runs a herdr call; errors are logged and the key reports failure.
        protected MenuItem Do(String id, String label, BitmapColor color, Action<HerdrClient> run) =>
            new MenuItem(id, label) { Color = color, Run = () => this.Call(id, run) };

        private Boolean Call(String id, Action<HerdrClient> run)
        {
            var herdr = this.Herdr;
            if (herdr == null)
            {
                return false;
            }

            try
            {
                run(herdr);
                this.Owner?.Watcher?.Poke();
                return true;
            }
            catch (HerdrException ex)
            {
                PluginLog.Warning($"herdr {id}: {ex.Code}: {ex.Message}");
                return false;
            }
        }

        // One key saying herdr is not running, so an empty page is never a mystery.
        private static IReadOnlyList<MenuItem> Offline() => new[]
        {
            new MenuItem("offline", "herdr offline")
            {
                Image = size => TileRenderer.Ring("HERDR", "Not running", "run herdr", TileRenderer.Failed, size),
            },
        };

        protected IReadOnlyList<MenuItem> WhenRunning(Func<HerdrClient, HerdrSnapshot, IReadOnlyList<MenuItem>> build)
        {
            var herdr = this.Herdr;
            var snapshot = herdr?.Snapshot() ?? HerdrSnapshot.Empty;
            return ReferenceEquals(snapshot, HerdrSnapshot.Empty) ? Offline() : build(herdr, snapshot);
        }

        public static BitmapColor RingFor(HerdrStatus status) => status switch
        {
            HerdrStatus.Working => TileRenderer.Working,
            HerdrStatus.Blocked => TileRenderer.Approval,
            HerdrStatus.Done => TileRenderer.Done,
            _ => TileRenderer.Idle,
        };

        public static String LabelFor(HerdrStatus status) => status switch
        {
            HerdrStatus.Working => "Working",
            HerdrStatus.Blocked => "Needs you",
            HerdrStatus.Done => "Done",
            HerdrStatus.Idle => "Ready",
            _ => String.Empty,
        };

        // Focuses the pane in herdr, then brings the terminal showing herdr forward.
        protected static void Show(HerdrClient herdr, String paneId)
        {
            herdr.FocusPane(paneId);
            HerdrTerminal.Activate();
        }

        // Every agent, colored by status; each opens its page of actions.
        protected IReadOnlyList<MenuItem> AgentsPage() => this.WhenRunning((herdr, snapshot) =>
            snapshot.OrderedAgents().Select(agent =>
            {
                var id = agent.Id;
                return new MenuItem("a:" + id, agent.FolderName)
                {
                    Image = size => TileRenderer.Ring((agent.Agent ?? "agent").ToUpperInvariant(), agent.Name ?? agent.FolderName, LabelFor(agent.Status), RingFor(agent.Status), size),
                    Open = () => this.AgentPage(id),
                };
            }).ToList());

        // What you can do to one agent without switching to it, plus Focus to switch.
        private IReadOnlyList<MenuItem> AgentPage(String paneId) => new[]
        {
            this.Do("focus", "Focus", TileRenderer.Done, h => Show(h, paneId)),
            this.Do("enter", "Enter", TileRenderer.Working, h => h.SendKeys(paneId, "enter")),
            this.Do("escape", "Esc", TileRenderer.Failed, h => h.SendKeys(paneId, "esc")),
            this.Do("mode", "Mode", TileRenderer.Question, h => h.SendKeys(paneId, "shift+tab")),
            this.Do("control-c", "Ctrl-C", TileRenderer.Failed, h => h.SendKeys(paneId, "ctrl+c")),
            this.Do("zoom", "Zoom", TileRenderer.Idle, h =>
            {
                Show(h, paneId);
                h.ToggleZoom(paneId);
            }),
        };

        // Every workspace, colored by its agents; one with several tabs opens a page of them.
        protected IReadOnlyList<MenuItem> WorkspacesPage() => this.WhenRunning((herdr, snapshot) =>
            snapshot.Workspaces.OrderBy(w => w.Number).Select(workspace =>
            {
                var id = workspace.Id;
                var header = workspace.Focused ? $"{workspace.Number} ●" : $"{workspace.Number}";
                var footer = workspace.TabCount > 1 ? $"{workspace.TabCount} tabs" : LabelFor(workspace.Status);
                var item = new MenuItem("w:" + id, workspace.Label)
                {
                    Image = size => TileRenderer.Ring(header, workspace.Label, footer, RingFor(workspace.Status), size),
                };
                if (workspace.TabCount > 1)
                {
                    item.Open = () => this.TabsPage(id);
                }
                else
                {
                    item.Run = () => this.Call("workspace", h =>
                    {
                        h.FocusWorkspace(id);
                        HerdrTerminal.Activate();
                    });
                }

                return item;
            }).ToList());

        private IReadOnlyList<MenuItem> TabsPage(String workspaceId) => this.WhenRunning((herdr, snapshot) =>
            snapshot.TabsOf(workspaceId).Select(tab =>
            {
                var id = tab.Id;
                return new MenuItem("t:" + id, tab.Label)
                {
                    Image = size => TileRenderer.Ring(tab.Focused ? "TAB ●" : "TAB", tab.Label, LabelFor(tab.Status), RingFor(tab.Status), size),
                    Run = () => this.Call("tab", h =>
                    {
                        h.FocusTab(id);
                        HerdrTerminal.Activate();
                    }),
                };
            }).ToList());

        // New tab, workspace, and splits in the focused pane's folder, then one key per installed agent.
        protected IReadOnlyList<MenuItem> NewPage() => this.WhenRunning((herdr, snapshot) =>
        {
            var focused = snapshot.FocusedPane;
            var cwd = focused?.Cwd;
            var items = new List<MenuItem>
            {
                this.Do("new-tab", "New tab", TileRenderer.Idle, h => h.NewTab(snapshot.FocusedWorkspaceId, cwd)),
                this.Do("new-workspace", "New workspace", TileRenderer.Idle, h => h.NewWorkspace(cwd)),
                this.Do("split-right", "Split right", TileRenderer.Idle, h => h.Split(focused?.Id, "right", cwd, focus: true)),
                this.Do("split-down", "Split down", TileRenderer.Idle, h => h.Split(focused?.Id, "down", cwd, focus: true)),
            };
            var agents = this.Owner?.Agents?.Installed() ?? Array.Empty<String>();
            items.AddRange(agents.Select(kind => this.Do("agent:" + kind, kind, TileRenderer.Working, h =>
            {
                // A fresh split keeps focus on the new pane, so the agent opens where you are looking.
                var pane = h.Split(focused?.Id, "right", cwd, focus: true);
                if (pane != null)
                {
                    h.StartAgent(h.Snapshot().FreeAgentName(kind), kind, pane);
                }
            })));
            return items;
        });

        // Keys named by what they do, sent to herdr's focused pane in the form its agent expects.
        protected IReadOnlyList<MenuItem> AgentKeysPage() => new[]
        {
            this.Focused("escape", "Esc", TileRenderer.Failed, (h, p) => h.SendKeys(p.Id, "esc")),
            this.Focused("mode", "Mode", TileRenderer.Question, (h, p) => h.SendKeys(p.Id, "shift+tab")),
            this.Focused("control-c", "Ctrl-C", TileRenderer.Failed, (h, p) => h.SendKeys(p.Id, "ctrl+c")),
            this.Slash("clear", "Clear", "/clear", "/clear"),
            this.Slash("compact", "Compact", "/compact", "/compact"),
            this.Slash("model", "Model", "/model", "/model"),
            this.Slash("context", "Context", "/context", "/status"),
            this.Slash("usage", "Usage", "/usage", "/status"),
        };

        private MenuItem Focused(String id, String label, BitmapColor color, Action<HerdrClient, HerdrPane> run) =>
            this.Do(id, label, color, h =>
            {
                var pane = h.Snapshot().FocusedPane ?? throw new HerdrException("no_focus", "herdr has no focused pane");
                run(h, pane);
            });

        private MenuItem Slash(String id, String label, String claude, String codex) =>
            this.Focused(id, label, TileRenderer.Working, (h, p) => h.SendText(p.Id, p.Agent == "codex" ? codex : claude, submit: true));

        // The shared slash commands, typed into herdr's focused pane.
        protected IReadOnlyList<MenuItem> SlashCommandsPage() =>
            SlashCommands.Items(
                (text, submit) => this.Call("slash", h =>
                {
                    var pane = h.Snapshot().FocusedPane ?? throw new HerdrException("no_focus", "herdr has no focused pane");
                    h.SendText(pane.Id, text, submit);
                }),
                message => PluginLog.Warning(message));

        // Moving between panes, laid out as arrows around Zoom in the middle key.
        protected IReadOnlyList<MenuItem> PanesPage()
        {
            MenuItem Split(String direction, String label) => this.Do("split-" + direction, label, TileRenderer.Idle, h =>
                h.Split(null, direction, h.Snapshot().FocusedPane?.Cwd, focus: true));

            return new[]
            {
                this.Do("up", "↑", TileRenderer.Idle, h => h.FocusDirection("up")),
                Split("right", "Split right"),
                this.Do("left", "←", TileRenderer.Idle, h => h.FocusDirection("left")),
                this.Do("zoom", "Zoom", TileRenderer.Question, h => h.ToggleZoom(null)),
                this.Do("right", "→", TileRenderer.Idle, h => h.FocusDirection("right")),
                Split("down", "Split down"),
                this.Do("down", "↓", TileRenderer.Idle, h => h.FocusDirection("down")),
            };
        }

        // The focused workspace's Git worktrees; press one to open it, or switch to the workspace showing it.
        protected IReadOnlyList<MenuItem> WorktreesPage() => this.WhenRunning((herdr, snapshot) =>
        {
            var workspaceId = snapshot.FocusedWorkspaceId;
            if (workspaceId == null)
            {
                return Array.Empty<MenuItem>();
            }

            IReadOnlyList<HerdrWorktree> worktrees;
            try
            {
                worktrees = herdr.Worktrees(workspaceId);
            }
            catch (HerdrException ex)
            {
                // Not a Git checkout, or a repository herdr has not been told to trust.
                PluginLog.Info($"herdr worktrees: {ex.Code}: {ex.Message}");
                return Array.Empty<MenuItem>();
            }

            return worktrees.Select(tree =>
            {
                var path = tree.Path;
                var open = tree.OpenWorkspaceId != null;
                return new MenuItem("wt:" + path, tree.Label)
                {
                    Image = size => TileRenderer.Ring(open ? "OPEN" : "WORKTREE", tree.Branch ?? tree.Label, tree.Label, open ? TileRenderer.Done : TileRenderer.Idle, size),
                    Run = () => this.Call("worktree", h =>
                    {
                        h.OpenWorktree(path, workspaceId);
                        HerdrTerminal.Activate();
                    }),
                };
            }).ToList();
        });

        // Focuses the agent that most needs you: blocked first, then finished and unseen.
        protected MenuItem NextWaitingItem() => new MenuItem("next", "Next waiting")
        {
            Image = size => HerdrNextWaitingCommand.Tile(this.Owner?.Watcher?.Latest, size),
            Run = () => this.Call("next", h =>
            {
                if (h.Snapshot().MostUrgent() is { } agent)
                {
                    Show(h, agent.Id);
                }
            }),
        };
    }

    // The herdr home page: one key per category, in the same keys as Claude's where there is an equivalent.
    public class HerdrHomeFolder : HerdrFolder
    {
        public HerdrHomeFolder()
            : base("Herdr")
        {
        }

        protected override BitmapImage FolderImage(PluginImageSize imageSize)
        {
            var snapshot = (this.Plugin as BirchpadHerdrPlugin)?.Watcher?.Latest ?? HerdrSnapshot.Empty;
            var waiting = snapshot.Agents.Count(a => a.Status is HerdrStatus.Blocked or HerdrStatus.Done);
            return TileRenderer.Ring("HERDR", "Herdr", waiting > 0 ? $"{waiting} waiting" : $"{snapshot.Agents.Count} agents", RingFor(snapshot.Overall()), imageSize);
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            Category("agents", "Agents", this.AgentsPage),
            Category("new", "New", this.NewPage),
            Category("slash", "Slash commands", this.SlashCommandsPage),
            Category("workspaces", "Workspaces", this.WorkspacesPage),
            Category("worktrees", "Worktrees", this.WorktreesPage),
            this.NextWaitingItem(),
            Category("keys", "Agent keys", this.AgentKeysPage),
            Category("panes", "Panes", this.PanesPage),
        };

        private static MenuItem Category(String id, String label, Func<IReadOnlyList<MenuItem>> page) =>
            new MenuItem(id, label) { Open = page };
    }

    // Each category as its own folder, for placing one directly on any profile.
    public class HerdrAgentsFolder : HerdrFolder
    {
        public HerdrAgentsFolder()
            : base("Agents")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => this.AgentsPage();
    }

    public class HerdrNewFolder : HerdrFolder
    {
        public HerdrNewFolder()
            : base("New")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => this.NewPage();
    }

    public class HerdrSlashCommandsFolder : HerdrFolder
    {
        public HerdrSlashCommandsFolder()
            : base("Slash commands")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => this.SlashCommandsPage();
    }

    public class HerdrWorkspacesFolder : HerdrFolder
    {
        public HerdrWorkspacesFolder()
            : base("Workspaces")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => this.WorkspacesPage();
    }

    public class HerdrWorktreesFolder : HerdrFolder
    {
        public HerdrWorktreesFolder()
            : base("Worktrees")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => this.WorktreesPage();
    }

    public class HerdrAgentKeysFolder : HerdrFolder
    {
        public HerdrAgentKeysFolder()
            : base("Agent keys")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => this.AgentKeysPage();
    }

    public class HerdrPanesFolder : HerdrFolder
    {
        public HerdrPanesFolder()
            : base("Panes")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => this.PanesPage();
    }
}
