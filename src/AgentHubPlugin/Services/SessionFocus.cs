namespace Loupedeck.AgentHubPlugin.Services
{
    using System;
    using System.Threading;

    using DevDeck.Core.Mac;
    using DevDeck.Core.Sessions;

    // Brings a session's window forward, and interrupts it, wherever it runs.
    public sealed class SessionFocus
    {
        private readonly MacAutomation mac;

        public SessionFocus(MacAutomation mac)
        {
            this.mac = mac;
        }

        public Boolean Show(SessionSnapshot session)
        {
            if (session is null)
            {
                return false;
            }

            switch (session.Source)
            {
                case SessionSource.ClaudeDesktop:
                    return this.mac.OpenClaudeDesktopSession(session.SessionId);
                case SessionSource.ClaudeCli:
                    // Fall back to just raising iTerm when no tab is in the session's folder.
                    return (session.Cwd != null && this.mac.FocusItermSessionAtPath(session.Cwd)) || this.mac.Activate(MacAutomation.ItermBundleId);
                case SessionSource.Codex:
                    // A Codex CLI session lives in an iTerm tab; otherwise it is in the ChatGPT app, which has no thread deep link.
                    return (session.Cwd != null && this.mac.FocusItermSessionAtPath(session.Cwd)) || this.mac.Activate(MacAutomation.CodexBundleId);
                default:
                    return false;
            }
        }

        public Boolean Interrupt(SessionSnapshot session)
        {
            if (session is null)
            {
                return false;
            }

            switch (session.Source)
            {
                case SessionSource.ClaudeDesktop:
                    if (!this.mac.OpenClaudeDesktopSession(session.SessionId))
                    {
                        return false;
                    }

                    // Give Claude Desktop time to switch to the session before Escape arrives.
                    Thread.Sleep(600);
                    return this.mac.SendEscape();
                case SessionSource.ClaudeCli:
                    return session.Cwd != null && this.mac.SendEscapeToItermSessionAtPath(session.Cwd);
                case SessionSource.Codex:
                    if (session.Cwd != null && this.mac.SendEscapeToItermSessionAtPath(session.Cwd))
                    {
                        return true;
                    }

                    if (!this.mac.Activate(MacAutomation.CodexBundleId))
                    {
                        return false;
                    }

                    Thread.Sleep(400);
                    return this.mac.SendEscape();
                default:
                    return false;
            }
        }
    }
}
