namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Birchpad.Core.Agents;
    using Birchpad.Core.Claude;
    using Birchpad.Core.Codex;
    using Birchpad.Core.Herdr;
    using Birchpad.Core.Mac;
    using Birchpad.Core.Permissions;
    using Birchpad.Core.Sessions;

    using Loupedeck.BirchpadAgentHubPlugin.Services;
    using Loupedeck.Tiles;

    // Folders for the Agents General profile, which works whichever app is in front.
    // Every agent in a list opens the same page: Allow, Deny, then the slash commands, all sent to that agent.
    public abstract class AgentsFolder : HubMenuFolder
    {
        protected AgentsFolder(String displayName)
            : base(displayName)
        {
        }

        // Allow and Deny, then one key per entry in slash-commands.json. The answer keys light up only while the agent waits.
        protected IReadOnlyList<MenuItem> AgentPage(Func<Boolean> waiting, Func<Boolean, Boolean> answer, Func<String, Boolean, Boolean> send)
        {
            var pending = waiting();
            var items = new List<MenuItem>
            {
                new MenuItem("allow", "Allow") { Color = pending ? TileRenderer.Done : TileRenderer.Empty, Run = () => answer(true) },
                new MenuItem("deny", "Deny") { Color = pending ? TileRenderer.Failed : TileRenderer.Empty, Run = () => answer(false) },
            };
            items.AddRange(SlashCommands.Items(send, message => PluginLog.Warning(message)));
            return items;
        }

        // A coding agent's key: where it runs, its name, and its status with time in that status.
        protected BitmapImage AgentTile(CodingAgent agent, PluginImageSize size)
        {
            if (agent == null)
            {
                return TileRenderer.Ring(String.Empty, "Ended", String.Empty, TileRenderer.Idle, size);
            }

            var ring = agent.IsWaitingOnUser && this.Hub?.PulseOn == false ? TileRenderer.Idle : TileRenderer.ColorFor(agent.State);
            var footer = $"{TileRenderer.LabelFor(agent.State)} {TileRenderer.Elapsed(DateTimeOffset.UtcNow - agent.StateSince)}";
            return TileRenderer.Ring(agent.HostLabel, agent.Name, footer, ring, size);
        }

        protected static MenuItem Heading(String id, String label, Func<IReadOnlyList<MenuItem>> open) =>
            new MenuItem(id, label) { Open = open };
    }

    // Every Claude Code and Codex session: Claude Desktop, the CLI, the ChatGPT app, and herdr panes.
    // Pressing one brings it to the front and opens its page.
    public class CodingSessionsFolder : AgentsFolder
    {
        public CodingSessionsFolder()
            : base("Coding sessions")
        {
        }

        protected override BitmapImage FolderImage(PluginImageSize imageSize)
        {
            var agents = this.Hub?.Agents.All() ?? Array.Empty<CodingAgent>();
            var waiting = agents.Count(a => a.IsWaitingOnUser);
            var working = agents.Count(a => a.State == SessionState.Working);
            var ring = waiting > 0 ? TileRenderer.Approval : working > 0 ? TileRenderer.Working : TileRenderer.Idle;
            var footer = waiting > 0 ? $"{waiting} waiting" : working > 0 ? $"{working} working" : $"{agents.Count} open";
            return TileRenderer.Ring(String.Empty, "Coding sessions", footer, ring, imageSize);
        }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var hub = this.Hub;
            if (hub == null)
            {
                return Array.Empty<MenuItem>();
            }

            return hub.Agents.All().Select(agent =>
            {
                var key = agent.Key;
                return new MenuItem(key, agent.Name)
                {
                    Image = size => this.AgentTile(hub.Agents.Find(key), size),
                    Run = () => hub.Agents.Show(hub.Agents.Find(key)),
                    Open = () => this.AgentPage(
                        () => hub.Agents.NeedsAnswer(hub.Agents.Find(key)),
                        allow => hub.Agents.Answer(hub.Agents.Find(key), allow),
                        (text, submit) => hub.Agents.Send(hub.Agents.Find(key), text, submit)),
                };
            }).ToList();
        }
    }

    // Cowork tasks, each with its live status when its session reports one, then keys that open claude.ai chats.
    public class ClaudeTasksFolder : AgentsFolder
    {
        public ClaudeTasksFolder()
            : base("Claude chats and tasks")
        {
        }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var hub = this.Hub;
            if (hub == null)
            {
                return Array.Empty<MenuItem>();
            }

            var items = new List<MenuItem>();
            foreach (var task in CoworkTasks.Read())
            {
                var cli = task.CliSessionId;
                items.Add(new MenuItem("cw:" + task.Id, task.Title)
                {
                    Image = size => this.TaskTile(task, size),
                    Run = () => hub.Mac.OpenUrl(AgentActions.CoworkLink),
                    Open = () => this.AgentPage(
                        () => cli != null && (hub.Permissions.HasPending(cli) || hub.Sessions.Get(cli)?.IsWaitingOnUser == true),
                        allow => cli != null && hub.Permissions.AnswerSession(cli, allow ? PermissionAnswer.Allow : PermissionAnswer.Deny),
                        hub.Agents.SendToCowork),
                });
            }

            items.Add(AppItems.Link("recents", "Recent chats", "claude://claude.ai/chat"));
            items.Add(AppItems.Link("projects", "claude.ai projects", "claude://claude.ai/project"));
            return items;
        }

        private BitmapImage TaskTile(CoworkTask task, PluginImageSize size)
        {
            var session = task.CliSessionId == null ? null : this.Hub?.Sessions.Get(task.CliSessionId);
            if (session == null)
            {
                return TileRenderer.Ring("COWORK", task.Title, TileRenderer.Elapsed(DateTimeOffset.UtcNow - task.LastActivity) + " ago", TileRenderer.Idle, size);
            }

            var ring = session.IsWaitingOnUser && this.Hub?.PulseOn == false ? TileRenderer.Idle : TileRenderer.ColorFor(session.State);
            return TileRenderer.Ring("COWORK", task.Title, TileRenderer.LabelFor(session.State), ring, size);
        }
    }

    // ChatGPT lists chats only in its sidebar, so these keys open and step through them.
    public class ChatGptTasksFolder : AgentsFolder
    {
        public ChatGptTasksFolder()
            : base("ChatGPT chats and tasks")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            new MenuItem("open", "Open ChatGPT") { Color = TileRenderer.Done, Run = () => this.Hub?.Mac.Activate(MacAutomation.CodexBundleId) == true },
            AppItems.Menu("new", "New chat", MacAutomation.CodexBundleId, "File", "New Chat"),
            AppItems.Menu("recents", "Recent chats", MacAutomation.CodexBundleId, "View", "Toggle Sidebar"),
            AppItems.Menu("previous", "Prev chat", MacAutomation.CodexBundleId, "View", "Previous Chat"),
            AppItems.Menu("next", "Next chat", MacAutomation.CodexBundleId, "View", "Next Chat"),
        };
    }

    // Starts a chat, task, or coding session in any app, and an agent in a herdr split for each kind installed.
    public class AgentsNewFolder : AgentsFolder
    {
        public AgentsNewFolder()
            : base("New")
        {
        }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var items = new List<MenuItem>
            {
                AppItems.Link("claude-chat", "Claude chat", "claude://claude.ai/new", TileRenderer.Done),
                AppItems.Link("claude-code", "Code session", "claude://code/new", TileRenderer.Working),
                AppItems.Menu("cowork", "Cowork task", MacAutomation.ClaudeBundleId, "Go", "Chat and Cowork"),
                AppItems.Menu("chatgpt-chat", "ChatGPT chat", MacAutomation.CodexBundleId, "File", "New Chat"),
                AppItems.Link("codex", "Codex thread", "codex://threads/new", TileRenderer.Working),
            };

            var hub = this.Hub;
            var snapshot = hub?.HerdrWatcher.Latest ?? HerdrSnapshot.Empty;
            if (hub != null && !ReferenceEquals(snapshot, HerdrSnapshot.Empty))
            {
                var focused = snapshot.FocusedPane;
                items.AddRange(hub.HerdrAgents.Installed().Select(kind => new MenuItem("herdr:" + kind, kind + " in herdr")
                {
                    Color = TileRenderer.Working,
                    Run = () => StartInHerdr(hub, kind, () => hub.Herdr.Split(focused?.Id, "right", focused?.Cwd, focus: true)),
                }));
            }

            return items;
        }

        // Makes a pane with the given call, then starts the agent in it and brings the terminal forward.
        internal static Boolean StartInHerdr(Hub hub, String kind, Func<String> makePane)
        {
            try
            {
                var pane = makePane();
                if (pane == null)
                {
                    return false;
                }

                hub.Herdr.StartAgent(hub.Herdr.Snapshot().FreeAgentName(kind), kind, pane);
                HerdrTerminal.Activate();
                hub.HerdrWatcher.Poke();
                return true;
            }
            catch (HerdrException ex)
            {
                PluginLog.Warning($"herdr new {kind}: {ex.Code}: {ex.Message}");
                return false;
            }
        }
    }

    // Folders from Claude Desktop, Codex, and ~/Projects; press one to start a coding session there.
    public class AgentsProjectsFolder : AgentsFolder
    {
        private readonly DesktopSessionIndex desktop = new DesktopSessionIndex();
        private readonly CodexState codex = new CodexState();

        public AgentsProjectsFolder()
            : base("Projects")
        {
        }

        public static String ProjectsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects");

        protected override IReadOnlyList<MenuItem> Root()
        {
            this.desktop.Refresh();
            var paths = new List<String>();
            void Add(String path)
            {
                path = path?.TrimEnd('/');
                if (!String.IsNullOrEmpty(path) && Directory.Exists(path) && !paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            foreach (var project in this.desktop.Projects)
            {
                Add(project.Path);
            }

            foreach (var project in this.codex.Projects())
            {
                Add(project.Path);
            }

            try
            {
                foreach (var folder in Directory.EnumerateDirectories(ProjectsRoot).Where(d => !Path.GetFileName(d).StartsWith('.')).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    Add(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PluginLog.Info($"Projects: cannot list {ProjectsRoot}: {ex.Message}");
            }

            return paths.Select(path =>
            {
                var name = Path.GetFileName(path);
                return new MenuItem("p:" + path, name)
                {
                    Image = size => TileRenderer.Ring("PROJECT", name, String.Empty, TileRenderer.Working, size),
                    Open = () => this.ProjectPage(path),
                };
            }).ToList();
        }

        // Where to start: a Claude Desktop Code session, or an agent in a new herdr workspace for each kind installed.
        private IReadOnlyList<MenuItem> ProjectPage(String path)
        {
            var items = new List<MenuItem>
            {
                AppItems.Link("claude-code", "Code session", "claude://code/new?folder=" + Uri.EscapeDataString(path), TileRenderer.Working),
            };

            var hub = this.Hub;
            if (hub != null && !ReferenceEquals(hub.HerdrWatcher.Latest, HerdrSnapshot.Empty))
            {
                items.AddRange(hub.HerdrAgents.Installed().Select(kind => new MenuItem("herdr:" + kind, kind + " in herdr")
                {
                    Color = TileRenderer.Working,
                    Run = () => AgentsNewFolder.StartInHerdr(hub, kind, () => hub.Herdr.NewWorkspace(path)),
                }));
            }

            return items;
        }
    }
}
