namespace Loupedeck.AgentHubPlugin.Actions
{
    using System;
    using System.Globalization;

    using Loupedeck.Tiles;
    using Loupedeck.AgentHubPlugin.Services;

    // One key per slot. A session keeps its slot until it ends, so a tile stays put while you work.
    public class SessionSlotCommand : HubCommand
    {
        public SessionSlotCommand()
        {
            this.DisplayName = "Session slot";
            this.Description = "Live status of one agent session. Press to open it.";
            this.GroupName = "Sessions";
            for (var slot = 1; slot <= Hub.SlotCount; slot++)
            {
                this.AddParameter(slot.ToString(CultureInfo.InvariantCulture), $"Session slot {slot}", "Sessions");
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            var session = this.Hub?.Sessions.GetBySlot(Slot(actionParameter));
            if (session != null && !this.Hub.Focus.Show(session))
            {
                PluginLog.Warning($"Could not open session {session.SessionId} ({session.Source})");
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            var session = this.Hub?.Sessions.GetBySlot(Slot(actionParameter));
            return session == null ? $"Slot {Slot(actionParameter)}" : session.DisplayName;
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var slot = Slot(actionParameter);
            return TileRenderer.Session(this.Hub?.Sessions.GetBySlot(slot), slot, imageSize, DateTimeOffset.UtcNow);
        }

        private static Int32 Slot(String actionParameter) =>
            Int32.TryParse(actionParameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) ? slot : 0;
    }
}
