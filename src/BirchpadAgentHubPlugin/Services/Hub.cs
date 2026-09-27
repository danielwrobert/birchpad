namespace Loupedeck.BirchpadAgentHubPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Birchpad.Core.Claude;
    using Birchpad.Core.Cloud;
    using Birchpad.Core.Codex;
    using Birchpad.Core.Http;
    using Birchpad.Core.Mac;
    using Birchpad.Core.Permissions;
    using Birchpad.Core.Sessions;
    using Birchpad.Core.Usage;

    // Owns the listener, session store, permission broker, and usage readers, and tells folders when to redraw.
    public sealed class Hub : IDisposable
    {
        public const Int32 DefaultPort = 8766;

        // Sessions are listed on pages, not slots; this only caps how many are tracked at once.
        private const Int32 MaxSessions = 64;

        private readonly Timer timer;
        private readonly DesktopSessionIndex desktopIndex;
        private readonly CodexThreadIndex codexIndex;
        private Int32 ticks;

        public Hub(Int32 port, TimeSpan cloudInterval)
        {
            var codex = CodexAppServer.FindCodex();
            this.Codex = codex == null ? null : new CodexAppServer(codex);
            this.CodexState = new CodexState();
            this.desktopIndex = new DesktopSessionIndex();
            this.codexIndex = new CodexThreadIndex(() => this.CodexState.RecentThreads()
                .Select(t => new CodexThread(t.Id, String.IsNullOrEmpty(t.Title) ? null : t.Title, t.Cwd, t.UpdatedAt))
                .ToList());
            this.Sessions = new SessionStore(MaxSessions, new CompositeMetadataSource(this.desktopIndex, this.codexIndex));
            this.Permissions = new PermissionBroker();
            this.Usage = new UsageStore();
            this.Mac = new MacAutomation(message => PluginLog.Warning(message));
            this.Listener = new HookListener(port, this.Sessions, this.Permissions, log: message => PluginLog.Warning(message), usage: this.Usage);
            this.Focus = new SessionFocus(this.Mac);

            var claude = CloudHelper.FindClaude();
            if (claude != null)
            {
                var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Birchpad");
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
            this.Usage.Changed += (_, _) => this.RaiseChanged();
            this.timer = new Timer(_ => this.OnTick(), null, Timeout.Infinite, Timeout.Infinite);
        }

        // Raised when tiles should redraw.
        public event EventHandler Changed;

        public SessionStore Sessions { get; }

        public PermissionBroker Permissions { get; }

        public UsageStore Usage { get; }

        public MacAutomation Mac { get; }

        public HookListener Listener { get; }

        public SessionFocus Focus { get; }

        public CodexState CodexState { get; }

        // Null when the claude CLI is missing.
        public CloudCache Cloud { get; }

        // Null when the codex CLI is missing.
        public CodexAppServer Codex { get; }

        // Latest Codex limits and history, refreshed every 5 minutes. Null until the first read.
        public CodexUsage CodexUsage { get; private set; }

        // Context use of the most recently active Codex thread. Null until read.
        public CodexContext CodexContext { get; private set; }

        // Claude Desktop scheduled tasks, reread every minute.
        public IReadOnlyList<ScheduledTask> LocalRoutines { get; private set; }

        // True on alternate seconds, so waiting tiles can pulse.
        public Boolean PulseOn => this.ticks % 2 == 0;

        public Boolean Start(out String error)
        {
            try
            {
                this.Listener.Start();
                this.timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                this.Cloud?.Start();

                // Fill caches up front so the first hooks and the first folder open already have data.
                Task.Run(() =>
                {
                    this.desktopIndex.Refresh();
                    this.codexIndex.Refresh();
                    this.RefreshUsage();
                });
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

        // Rereads Claude's recorded limits, Codex limits, and Codex context. Runs on a worker or timer thread.
        public void RefreshUsage()
        {
            if (PlanUsageHistory.ReadLatest() is { } claude)
            {
                this.Usage.ApplyFallback(claude);
            }

            if (this.Codex?.ReadUsage(DateTimeOffset.UtcNow) is { } codex)
            {
                this.CodexUsage = codex;
            }

            var latest = this.CodexState.RecentThreads(1).FirstOrDefault();
            if (latest != null && CodexState.ReadContext(latest.RolloutPath) is { } context)
            {
                this.CodexContext = context;
            }

            this.RaiseChanged();
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

                if (this.ticks % 300 == 5)
                {
                    this.RefreshUsage();
                }

                var pruned = this.Sessions.Prune(DateTimeOffset.UtcNow);

                // Waiting sessions pulse every second; otherwise elapsed times only need a redraw every 10 seconds.
                if (!pruned && (this.ticks % 10 == 0 || this.Sessions.All().Any(s => s.IsWaitingOnUser)))
                {
                    this.RaiseChanged();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Agent Hub tick failed");
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
