namespace Loupedeck.BirchpadAgentHubPlugin.Services
{
    using System;
    using System.Threading;

    using Birchpad.Core.Mac;
    using Birchpad.Core.Sessions;

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
                    // Only an iTerm or Ghostty tab in the session's folder; a CLI in another terminal has no window to raise,
                    // and raising a terminal anyway shows an unrelated window.
                    return session.Cwd != null && this.mac.FocusTerminalAtPath(session.Cwd);
                case SessionSource.Codex:
                    // A Codex CLI session lives in a terminal tab; otherwise open its thread in the ChatGPT app.
                    return (session.Cwd != null && this.mac.FocusTerminalAtPath(session.Cwd))
                        || this.mac.OpenUrl($"codex://threads/{Uri.EscapeDataString(session.SessionId)}");
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
                    return session.Cwd != null && this.mac.SendEscapeToTerminalAtPath(session.Cwd);
                case SessionSource.Codex:
                    if (session.Cwd != null && this.mac.SendEscapeToTerminalAtPath(session.Cwd))
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
