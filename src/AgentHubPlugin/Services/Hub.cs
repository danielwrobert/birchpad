namespace Loupedeck.AgentHubPlugin.Services
{
    using System;
    using System.Threading;

    using DevDeck.Core.Claude;
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

        public Hub(Int32 port)
        {
            this.Sessions = new SessionStore(SlotCount, new DesktopSessionIndex());
            this.Permissions = new PermissionBroker();
            this.Mac = new MacAutomation(message => PluginLog.Warning(message));
            this.Listener = new HookListener(port, this.Sessions, this.Permissions, log: message => PluginLog.Warning(message));
            this.Focus = new SessionFocus(this.Mac);

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

        public Boolean Start(out String error)
        {
            try
            {
                this.Listener.Start();
                this.timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
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
            this.Listener.Dispose();
        }

        private void OnTick()
        {
            try
            {
                if (!this.Sessions.Prune(DateTimeOffset.UtcNow) && ++this.ticks % 10 == 0)
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
