namespace Loupedeck.BirchpadAgentHubPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    using Birchpad.Core.Agents;
    using Birchpad.Core.Herdr;
    using Birchpad.Core.Mac;
    using Birchpad.Core.Permissions;

    // Reaches one coding agent wherever it runs: brings it forward, answers its approval, and types into it.
    // herdr panes take text through herdr's socket, iTerm tabs through AppleScript, and the desktop apps by pasting.
    public sealed class AgentActions
    {
        // How long an app gets to switch to a session before text is pasted into it.
        private const Int32 SwitchDelay = 800;

        private readonly Hub hub;

        public AgentActions(Hub hub)
        {
            this.hub = hub;
        }

        private MacAutomation Mac => this.hub.Mac;

        private HerdrClient Herdr => this.hub.Herdr;

        // Every coding agent from hooks and herdr, one entry each.
        public IReadOnlyList<CodingAgent> All() =>
            CodingAgents.Merge(this.hub.Sessions.All(), this.hub.HerdrWatcher.Latest, DateTimeOffset.UtcNow);

        public CodingAgent Find(String key)
        {
            foreach (var agent in this.All())
            {
                if (agent.Key == key)
                {
                    return agent;
                }
            }

            return null;
        }

        // True while the agent waits on an approval, from a held hook request or from what herdr sees.
        public Boolean NeedsAnswer(CodingAgent agent) =>
            agent != null && (agent.IsWaitingOnUser || (agent.SessionId != null && this.hub.Permissions.HasPending(agent.SessionId)));

        public Boolean Show(CodingAgent agent)
        {
            if (agent == null)
            {
                return false;
            }

            if (agent.Host == AgentHost.Herdr && agent.PaneId != null)
            {
                return this.OnHerdr(h =>
                {
                    h.FocusPane(agent.PaneId);
                    HerdrTerminal.Activate();
                });
            }

            return agent.SessionId != null && this.hub.Focus.Show(this.hub.Sessions.Get(agent.SessionId));
        }

        // Allow picks the approval prompt's first option; Deny presses Esc. A request held by the permission hook is answered directly.
        public Boolean Answer(CodingAgent agent, Boolean allow)
        {
            if (agent == null)
            {
                return false;
            }

            if (agent.SessionId != null && this.hub.Permissions.HasPending(agent.SessionId))
            {
                return this.hub.Permissions.AnswerSession(agent.SessionId, allow ? PermissionAnswer.Allow : PermissionAnswer.Deny);
            }

            switch (agent.Host)
            {
                case AgentHost.Herdr when agent.PaneId != null:
                    return this.OnHerdr(h =>
                    {
                        if (allow)
                        {
                            h.SendText(agent.PaneId, "1", submit: false);
                        }
                        else
                        {
                            h.SendKeys(agent.PaneId, "esc");
                        }
                    });
                case AgentHost.Terminal or AgentHost.Codex when agent.Cwd != null:
                    return this.Mac.WriteToItermSessionAtPath(agent.Cwd, allow ? "1" : ITermController.Escape, newline: false);
                default:
                    // Claude Desktop and the ChatGPT app answer only through the permission hook.
                    PluginLog.Warning($"no way to answer {agent.HostLabel} session {agent.Name} without the permission hook");
                    return false;
            }
        }

        // Types a command into the agent, pressing Return when submit is set.
        public Boolean Send(CodingAgent agent, String text, Boolean submit)
        {
            if (agent == null)
            {
                return false;
            }

            switch (agent.Host)
            {
                case AgentHost.Herdr when agent.PaneId != null:
                    return this.OnHerdr(h => h.SendText(agent.PaneId, text, submit));
                case AgentHost.Terminal when agent.Cwd != null:
                    return this.Mac.WriteToItermSessionAtPath(agent.Cwd, text, submit);
                case AgentHost.ClaudeDesktop when agent.SessionId != null:
                    return this.Mac.OpenClaudeDesktopSession(agent.SessionId) && this.PasteAfterSwitch(MacAutomation.ClaudeBundleId, text, submit);
                case AgentHost.Codex:
                    // A Codex CLI in an iTerm tab takes the text there; otherwise its thread opens in the ChatGPT app.
                    if (agent.Cwd != null && this.Mac.WriteToItermSessionAtPath(agent.Cwd, text, submit))
                    {
                        return true;
                    }

                    return agent.SessionId != null
                        && this.Mac.OpenUrl($"codex://threads/{Uri.EscapeDataString(agent.SessionId)}")
                        && this.PasteAfterSwitch(MacAutomation.CodexBundleId, text, submit);
                default:
                    return false;
            }
        }

        // Opens Claude Desktop's Cowork view and pastes there, for a Cowork task, which no link opens by ID.
        public Boolean SendToCowork(String text, Boolean submit) =>
            this.Mac.OpenUrl(CoworkLink) && this.PasteAfterSwitch(MacAutomation.ClaudeBundleId, text, submit);

        public const String CoworkLink = "claude://claude.ai/cowork";

        private Boolean PasteAfterSwitch(String bundleId, String text, Boolean submit)
        {
            Thread.Sleep(SwitchDelay);
            return this.Mac.PasteInto(bundleId, text, submit);
        }

        private Boolean OnHerdr(Action<HerdrClient> run)
        {
            try
            {
                run(this.Herdr);
                this.hub.HerdrWatcher.Poke();
                return true;
            }
            catch (HerdrException ex)
            {
                PluginLog.Warning($"herdr: {ex.Code}: {ex.Message}");
                return false;
            }
        }
    }
}
