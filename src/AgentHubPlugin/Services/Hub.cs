namespace Loupedeck.AgentHubPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;

    using DevDeck.Core.Claude;
    using DevDeck.Core.Cloud;
    using DevDeck.Core.Codex;
    using DevDeck.Core.Http;
    using DevDeck.Core.Mac;
    using DevDeck.Core.Permissions;
    using DevDeck.Core.Sessions;

    // Owns the listener, session store, and permission broker, and tells actions when to redraw.
    public sealed class Hub : IDisposable
    {
        public const Int32 DefaultPort = 8766;

        public const Int32 SlotCount = 16;

        private readonly Timer timer;
        private Int32 ticks;

        public Hub(Int32 port, TimeSpan cloudInterval)
        {
            var codex = CodexAppServer.FindCodex();
            this.Codex = codex == null ? null : new CodexAppServer(codex);
            ISessionMetadataSource metadata = this.Codex == null
                ? new DesktopSessionIndex()
                : new CompositeMetadataSource(new DesktopSessionIndex(), new CodexThreadIndex(() => this.Codex.ListThreads()));
            this.Sessions = new SessionStore(SlotCount, metadata);
            this.Permissions = new PermissionBroker();
            this.Mac = new MacAutomation(message => PluginLog.Warning(message));
            this.Listener = new HookListener(port, this.Sessions, this.Permissions, log: message => PluginLog.Warning(message));
            this.Focus = new SessionFocus(this.Mac);

            var claude = CloudHelper.FindClaude();
            if (claude != null)
            {
                var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevDeck");
                var helper = new CloudHelper(claude, Path.Combine(dataDir, "helper"));
                this.Cloud = new CloudCache(helper.Run, Path.Combine(dataDir, "cloud-cache.json"), cloudInterval);
                this.Cloud.Changed += (_, _) => this.RaiseChanged();
            }
            else
            {
                PluginLog.Warning("claude CLI not found; cloud routines and remote sessions are off");
            }

            this.LocalRoutines = ScheduledTaskReader.Read();
            this.Sessions.Changed += (_, _) => this.RaiseChanged();
            this.Permissions.Changed += (_, _) => this.RaiseChanged();
            this.timer = new Timer(_ => this.OnTick(), null, Timeout.Infinite, Timeout.Infinite);
        }

        // Raised when tiles should redraw: a session or pending request changed, or elapsed times moved on.
        public event EventHandler Changed;

        public SessionStore Sessions { get; }

        public PermissionBroker Permissions { get; }

        public MacAutomation Mac { get; }

        public HookListener Listener { get; }

        public SessionFocus Focus { get; }

        // Null when the claude CLI is missing.
        public CloudCache Cloud { get; }

        // Null when the codex CLI is missing.
        public CodexAppServer Codex { get; }

        // Latest Codex rate limits, refreshed every 5 minutes. Null until the first read.
        public CodexUsage CodexUsage { get; private set; }

        // Claude Desktop scheduled tasks, reread every minute.
        public IReadOnlyList<ScheduledTask> LocalRoutines { get; private set; }

        public Boolean Start(out String error)
        {
            try
            {
                this.Listener.Start();
                this.timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                this.Cloud?.Start();
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            this.timer.Dispose();
            this.Cloud?.Dispose();
            this.Listener.Dispose();
        }

        private void OnTick()
        {
            try
            {
                ++this.ticks;
                if (this.ticks % 60 == 0)
                {
                    this.LocalRoutines = ScheduledTaskReader.Read();
                }

                if (this.Codex != null && this.ticks % 300 == 5)
                {
                    this.RefreshCodexUsage();
                }

                if (!this.Sessions.Prune(DateTimeOffset.UtcNow) && this.ticks % 10 == 0)
                {
                    // Elapsed times on tiles are shown in whole minutes, so a redraw every 10 seconds is enough.
                    this.RaiseChanged();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Agent Hub tick failed");
            }
        }

        // Reads Codex usage now; the key calls this on press. Runs on the timer or a worker thread.
        public void RefreshCodexUsage()
        {
            var usage = this.Codex?.ReadUsage(DateTimeOffset.UtcNow);
            if (usage != null)
            {
                this.CodexUsage = usage;
                this.RaiseChanged();
            }
        }

        private void RaiseChanged()
        {
            try
            {
                this.Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Agent Hub redraw failed");
            }
        }
    }
}
