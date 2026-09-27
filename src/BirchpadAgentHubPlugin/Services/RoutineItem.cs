namespace Loupedeck.BirchpadAgentHubPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    // One routine tile, local or cloud, in a shape the folder can draw.
    public sealed class RoutineItem
    {
        public String Key { get; private set; }

        public String Name { get; private set; }

        public Boolean IsLocal { get; private set; }

        public Boolean Enabled { get; private set; }

        public DateTime? NextRun { get; private set; }

        public DateTimeOffset? LastRun { get; private set; }

        public String Footer(DateTime now) =>
            !this.Enabled ? "paused"
            : this.NextRun != null ? When(this.NextRun.Value, now)
            : this.LastRun != null ? $"ran {TileRendererAgo(now, this.LastRun.Value)}"
            : "no schedule";

        // "in 45m", "in 3h", then a weekday and time for anything further out.
        public static String When(DateTime next, DateTime now)
        {
            var span = next - now;
            return span.TotalMinutes < 60 ? $"in {Math.Max(1, (Int32)span.TotalMinutes)}m"
                : span.TotalHours < 12 ? $"in {(Int32)span.TotalHours}h"
                : next.ToString("ddd HH:mm", CultureInfo.CurrentCulture);
        }

        public static IReadOnlyList<RoutineItem> From(Hub hub)
        {
            var local = hub.LocalRoutines.Select(t => new RoutineItem
            {
                Key = "local:" + t.Id,
                Name = Pretty(t.Id),
                IsLocal = true,
                Enabled = t.Enabled,
                NextRun = t.NextRunLocal,
                LastRun = t.LastRunAt,
            });
            var cloud = (hub.Cloud?.Latest?.Routines ?? Array.Empty<Birchpad.Core.Cloud.CloudRoutine>()).Select(r => new RoutineItem
            {
                Key = "cloud:" + r.Id,
                Name = r.Name,
                IsLocal = false,
                Enabled = r.Enabled ?? true,
                NextRun = r.NextRunAt?.LocalDateTime,
                LastRun = r.LastRunAt,
            });
            return local.Concat(cloud)
                .OrderBy(i => i.NextRun ?? DateTime.MaxValue)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // "americares-code-review" reads better on a key as "americares code review".
        private static String Pretty(String id) => id.Replace('-', ' ').Replace('_', ' ');

        private static String TileRendererAgo(DateTime now, DateTimeOffset then) =>
            Loupedeck.Tiles.TileRenderer.Elapsed(now - then.LocalDateTime) + " ago";
    }
}
