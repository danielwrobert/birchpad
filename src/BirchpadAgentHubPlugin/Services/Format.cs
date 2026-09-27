namespace Loupedeck.BirchpadAgentHubPlugin.Services
{
    using System;
    using System.Globalization;

    using Birchpad.Core.Usage;

    using Loupedeck.Tiles;

    // Short text for 80-pixel keys.
    public static class Format
    {
        public static String Percent(Double? value) =>
            value == null ? "?" : ((Int32)Math.Round(value.Value)).ToString(CultureInfo.InvariantCulture) + "%";

        // "resets 45m", "resets 3h", then a weekday for anything further out.
        public static String Resets(DateTimeOffset? at, DateTimeOffset now)
        {
            if (at == null)
            {
                return String.Empty;
            }

            var span = at.Value - now;
            return span.TotalMinutes < 60 ? $"resets {Math.Max(1, (Int32)span.TotalMinutes)}m"
                : span.TotalHours < 24 ? $"resets {(Int32)span.TotalHours}h"
                : "resets " + at.Value.ToLocalTime().ToString("ddd", CultureInfo.CurrentCulture);
        }

        public static String Tokens(Int64? tokens) =>
            tokens == null ? "?"
            : tokens >= 1_000_000 ? (tokens.Value / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M"
            : tokens >= 1_000 ? (tokens.Value / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K"
            : tokens.Value.ToString(CultureInfo.InvariantCulture);

        // Green below 70%, amber from 70%, red from 90%.
        public static BitmapColor UsageColor(Double? percent) =>
            percent == null ? TileRenderer.Idle
            : percent >= 90 ? TileRenderer.Failed
            : percent >= 70 ? TileRenderer.Approval
            : TileRenderer.Done;

        public static Double? Worst(params LimitWindow[] windows)
        {
            Double? worst = null;
            foreach (var w in windows)
            {
                if (w != null && (worst == null || w.UsedPercent > worst))
                {
                    worst = w.UsedPercent;
                }
            }

            return worst;
        }
    }
}
