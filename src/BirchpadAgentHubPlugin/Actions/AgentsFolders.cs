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

        // While the agent waits: the options of the menu on its screen, then Prev and Next tab for a set of questions,
        // then Esc. A menu that cannot be read falls back to Allow, Always, and Deny for a request the permission hook holds,
        // or plain number keys. The rest of the time: one key per entry in slash-commands.json, sent to the agent.
        protected IReadOnlyList<MenuItem> AgentPage(Func<CodingAgent> find, Func<String, Boolean, Boolean> send = null)
        {
            var hub = this.Hub;
            var agent = find();
            if (hub == null || agent == null)
            {
                return Array.Empty<MenuItem>();
            }

            send ??= (text, submit) => hub.Agents.Send(find(), text, submit);
            if (!hub.Agents.NeedsAnswer(agent))
            {
                return SlashCommands.Items(send, message => PluginLog.Warning(message));
            }

            Boolean Press(params String[] keys) => hub.Agents.PressKeys(find(), keys);
            var items = new List<MenuItem>();
            if (hub.Agents.ReadPrompt(agent) is { } prompt)
            {
                foreach (var option in prompt.Options)
                {
                    var keys = prompt.KeysFor(option).ToArray();
                    items.Add(new MenuItem($"option:{option.Index}:{option.Label}", $"{option.Index + 1} {option.Label}")
                    {
                        Color = option.Index == prompt.Cursor ? TileRenderer.Approval : TileRenderer.Working,
                        Run = () => Press(keys),
                    });
                }

                if (prompt.Tabs.Count > 0)
                {
                    items.Add(new MenuItem("tab-previous", "‹ Tab") { Color = TileRenderer.Idle, Run = () => Press("left") });
                    items.Add(new MenuItem("tab-next", "Tab ›") { Color = TileRenderer.Idle, Run = () => Press("right") });
                }
            }
            else if (hub.Agents.HookPending(agent))
            {
                items.Add(new MenuItem("allow", "Allow") { Color = TileRenderer.Done, Run = () => hub.Agents.AnswerHook(find(), PermissionAnswer.Allow) });
                items.Add(new MenuItem("always", "Always") { Color = TileRenderer.Done, Run = () => hub.Agents.AnswerHook(find(), PermissionAnswer.AllowAlways) });
                items.Add(new MenuItem("deny", "Deny") { Color = TileRenderer.Failed, Run = () => hub.Agents.AnswerHook(find(), PermissionAnswer.Deny) });
                return items;
            }
            else if (hub.Agents.CanPressKeys(agent))
            {
                foreach (var n in new[] { "1", "2", "3" })
                {
                    items.Add(new MenuItem("number:" + n, n) { Color = TileRenderer.Working, Run = () => Press(n) });
                }
            }
            else
            {
                return SlashCommands.Items(send, message => PluginLog.Warning(message));
            }

            items.Add(new MenuItem("escape", "Esc") { Color = TileRenderer.Failed, Run = () => Press("esc") });
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

        private readonly CodexState codexState = new CodexState();

        // Cowork tasks, newest first, each opening the agent page; commands are pasted into the Cowork view.
        protected IReadOnlyList<MenuItem> CoworkPage()
        {
            var hub = this.Hub;
            if (hub == null)
            {
                return Array.Empty<MenuItem>();
            }

            return CoworkTasks.Read().Select(task =>
            {
                var cli = task.CliSessionId;
                return new MenuItem("cw:" + task.Id, task.Title)
                {
                    Image = size => this.TaskTile(task, size),
                    Run = () => hub.Mac.OpenUrl(AgentActions.CoworkLink),
                    Open = () => this.AgentPage(() => this.TaskAgent(task), hub.Agents.SendToCowork),
                };
            }).ToList();
        }

        // ChatGPT app threads, newest first; pressing one opens it in the app and opens its agent page.
        protected IReadOnlyList<MenuItem> ChatGptPage()
        {
            var hub = this.Hub;
            if (hub == null)
            {
                return Array.Empty<MenuItem>();
            }

            return this.codexState.AppThreads().Select(thread =>
            {
                var id = thread.Id;
                var title = String.IsNullOrEmpty(thread.Title) ? Path.GetFileName(thread.Cwd.TrimEnd('/')) : thread.Title;
                CodingAgent Agent() => this.ThreadAgent(id, title);
                return new MenuItem("t:" + id, title)
                {
                    Image = size => this.ThreadTile(id, title, thread.UpdatedAt, size),
                    Run = () => hub.Mac.OpenUrl($"codex://threads/{Uri.EscapeDataString(id)}"),
                    Open = () => this.AgentPage(Agent),
                };
            }).ToList();
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

        // A Cowork task as an agent of Claude Desktop, answered through its session's permission hook.
        private CodingAgent TaskAgent(CoworkTask task)
        {
            var session = task.CliSessionId == null ? null : this.Hub?.Sessions.Get(task.CliSessionId);
            return new CodingAgent("cw:" + task.Id, "claude", AgentHost.ClaudeDesktop, session?.State ?? SessionState.Idle, session?.StateSince ?? task.LastActivity, task.Title, null, task.CliSessionId, null);
        }

        // A thread as an agent in the ChatGPT app: no folder, so text goes to the app, never to an iTerm tab.
        private CodingAgent ThreadAgent(String id, String title)
        {
            var session = this.Hub?.Sessions.Get(id);
            return new CodingAgent("t:" + id, "codex", AgentHost.Codex, session?.State ?? SessionState.Idle, session?.StateSince ?? DateTimeOffset.UtcNow, title, null, id, null);
        }

        private BitmapImage ThreadTile(String id, String title, DateTimeOffset updated, PluginImageSize size)
        {
            var session = this.Hub?.Sessions.Get(id);
            if (session == null)
            {
                return TileRenderer.Ring("CHATGPT", title, TileRenderer.Elapsed(DateTimeOffset.UtcNow - updated) + " ago", TileRenderer.Idle, size);
            }

            var ring = session.IsWaitingOnUser && this.Hub?.PulseOn == false ? TileRenderer.Idle : TileRenderer.ColorFor(session.State);
            return TileRenderer.Ring("CHATGPT", title, TileRenderer.LabelFor(session.State), ring, size);
        }
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
                    Open = () => this.AgentPage(() => hub.Agents.Find(key)),
                };
            }).ToList();
        }
    }

    // Cowork tasks, each with its live status when its session reports one. No link opens a task by ID,
    // so pressing one opens the Cowork view, and its commands are pasted there.
    public class ClaudeTasksFolder : AgentsFolder
    {
        public ClaudeTasksFolder()
            : base("Cowork tasks")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => this.CoworkPage();
    }

    // Threads started in the ChatGPT app (chats handed to Codex, Work tasks, and app sessions), newest first.
    public class ChatGptTasksFolder : AgentsFolder
    {
        public ChatGptTasksFolder()
            : base("ChatGPT tasks")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => this.ChatGptPage();
    }

    // Both task lists, then keys that open each app's own chats; plain chats are listed only inside the apps.
    public class ChatsFolder : AgentsFolder
    {
        public ChatsFolder()
            : base("Chats")
        {
        }

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            Heading("cowork", "Cowork tasks ›", this.CoworkPage),
            Heading("chatgpt", "ChatGPT tasks ›", this.ChatGptPage),
            AppItems.Link("claude-recents", "Claude chats", "claude://claude.ai/chat"),
            AppItems.Link("claude-projects", "claude.ai projects", "claude://claude.ai/project"),
            AppItems.ClaudeSidebar("artifacts", "Artifacts", "Artifacts"),
            AppItems.Menu("chatgpt-recents", "ChatGPT chats", MacAutomation.CodexBundleId, "View", "Toggle Sidebar"),
            AppItems.Menu("chatgpt-previous", "Prev ChatGPT chat", MacAutomation.CodexBundleId, "View", "Previous Chat"),
            AppItems.Menu("chatgpt-next", "Next ChatGPT chat", MacAutomation.CodexBundleId, "View", "Next Chat"),
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

    // Projects by where they come from: Claude (folders Claude Desktop Code sessions ran in), ChatGPT (Codex projects),
    // and Folders (~/Projects). Each opens its list; press a project to start a coding session there.
    public class AgentsProjectsFolder : AgentsFolder
    {
        private readonly DesktopSessionIndex desktop = new DesktopSessionIndex();
        private readonly CodexState codex = new CodexState();

        public AgentsProjectsFolder()
            : base("Projects")
        {
        }

        public static String ProjectsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects");

        protected override IReadOnlyList<MenuItem> Root() => new[]
        {
            Heading("claude", "Claude ›", () => this.List("CLAUDE", this.ClaudeFolders())),
            Heading("chatgpt", "ChatGPT ›", () => this.List("CHATGPT", this.codex.Projects().Select(p => p.Path))),
            Heading("folders", "Folders ›", () => this.List("FOLDER", LocalFolders())),
        };

        private IEnumerable<String> ClaudeFolders()
        {
            this.desktop.Refresh();
            return this.desktop.Projects.Select(p => p.Path);
        }

        private static IEnumerable<String> LocalFolders()
        {
            try
            {
                return Directory.EnumerateDirectories(ProjectsRoot)
                    .Where(d => !Path.GetFileName(d).StartsWith('.'))
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PluginLog.Info($"Projects: cannot list {ProjectsRoot}: {ex.Message}");
                return Array.Empty<String>();
            }
        }

        private IReadOnlyList<MenuItem> List(String header, IEnumerable<String> folders) =>
            folders
                .Select(f => f?.TrimEnd('/'))
                .Where(f => !String.IsNullOrEmpty(f) && Directory.Exists(f))
                .Distinct(StringComparer.Ordinal)
                .Select(path =>
                {
                    var name = Path.GetFileName(path);
                    return new MenuItem("p:" + path, name)
                    {
                        Image = size => TileRenderer.Ring(header, name, "start here", TileRenderer.Working, size),
                        Open = () => this.ProjectPage(path),
                    };
                })
                .ToList();

        // Where to start: a Claude Desktop Code session, or an agent in a new herdr workspace for each kind installed.
        private IReadOnlyList<MenuItem> ProjectPage(String path)
        {
            var items = new List<MenuItem>
            {
                AppItems.Link("claude-code", "Claude Code", "claude://code/new?folder=" + Uri.EscapeDataString(path), TileRenderer.Working),
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
