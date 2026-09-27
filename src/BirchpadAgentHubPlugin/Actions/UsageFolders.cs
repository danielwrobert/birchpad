namespace Loupedeck.BirchpadAgentHubPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using Birchpad.Core.Usage;

    using Loupedeck.BirchpadAgentHubPlugin.Services;
    using Loupedeck.Tiles;

    // Shared tiles for the three Usage folders.
    public abstract class UsageFolder : HubMenuFolder
    {
        protected UsageFolder(String displayName)
            : base(displayName)
        {
        }

        protected MenuItem Limit(String id, String header, String name, LimitWindow window) =>
            new MenuItem(id, name)
            {
                Image = size => TileRenderer.Ring(header, $"{name} {Format.Percent(window?.UsedPercent)}", Format.Resets(window?.ResetsAt, DateTimeOffset.UtcNow), Format.UsageColor(window?.UsedPercent), size),
            };

        protected MenuItem Info(String id, String header, String name, String footer, BitmapColor ring) =>
            new MenuItem(id, name) { Image = size => TileRenderer.Ring(header, name, footer, ring, size) };

        protected MenuItem RefreshItem() =>
            new MenuItem("refresh", "Refresh")
            {
                Image = size => TileRenderer.Command("Refresh", TileRenderer.Idle, size),
                Run = () =>
                {
                    var hub = this.Hub;
                    Task.Run(() => hub?.RefreshUsage());
                    return hub != null;
                },
            };

        // Context of the most recently active Claude session: its status line when it reports one, else its transcript.
        protected MenuItem ClaudeContext()
        {
            var hub = this.Hub;
            var session = hub?.Sessions.All()
                .Where(s => s.Agent == "claude")
                .OrderByDescending(s => s.LastEventAt)
                .FirstOrDefault();
            var reported = session == null ? null : hub.Usage.ForSession(session.SessionId)?.ContextPercent;
            var percent = reported ?? Birchpad.Core.Usage.ClaudeTranscript.ReadContext(session?.TranscriptPath, session?.Model)?.Percent;
            var footer = session == null ? "no session" : session.DisplayName;
            return this.Info("claude-context", "CLAUDE", $"Context {Format.Percent(percent)}", footer, Format.UsageColor(percent));
        }

        protected MenuItem CodexContext()
        {
            var context = this.Hub?.CodexContext;
            return this.Info("codex-context", "CODEX", $"Context {Format.Percent(context?.Percent)}", context == null ? "no thread" : $"{Format.Tokens(context.UsedTokens)}/{Format.Tokens(context.WindowTokens)}", Format.UsageColor(context?.Percent));
        }
    }

    // Claude plan limits and the current session's numbers.
    public class ClaudeUsageFolder : UsageFolder
    {
        public ClaudeUsageFolder()
            : base("Claude usage")
        {
        }

        protected override BitmapImage FolderImage(PluginImageSize imageSize)
        {
            var limits = this.Hub?.Usage.Limits;
            var worst = Format.Worst(limits?.FiveHour, limits?.SevenDay);
            return TileRenderer.Ring("USAGE", $"5h {Format.Percent(limits?.FiveHour?.UsedPercent)}", $"week {Format.Percent(limits?.SevenDay?.UsedPercent)}", Format.UsageColor(worst), imageSize);
        }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var limits = this.Hub?.Usage.Limits;
            var session = this.Hub?.Usage.Latest();
            var cost = session?.CostUsd == null ? "?" : "$" + session.CostUsd.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            var lines = session?.LinesAdded == null ? "?" : $"+{session.LinesAdded}/-{session.LinesRemoved ?? 0}";
            return new[]
            {
                this.Limit("five-hour", "CLAUDE", "5h", limits?.FiveHour),
                this.Limit("week", "CLAUDE", "Week", limits?.SevenDay),
                this.ClaudeContext(),
                this.Info("cost", "SESSION", cost, "cost", TileRenderer.Idle),
                this.Info("lines", "SESSION", lines, "lines", TileRenderer.Idle),
                this.Info("source", "FROM", limits?.Source ?? "no data", limits == null ? String.Empty : TileRenderer.Elapsed(DateTimeOffset.UtcNow - limits.UpdatedAt) + " ago", TileRenderer.Idle),
                this.RefreshItem(),
            };
        }
    }

    // Codex limits, plan, and account history.
    public class CodexUsageFolder : UsageFolder
    {
        public CodexUsageFolder()
            : base("Codex usage")
        {
        }

        protected override BitmapImage FolderImage(PluginImageSize imageSize)
        {
            var usage = this.Hub?.CodexUsage;
            var worst = Math.Max(usage?.Primary?.UsedPercent ?? 0, usage?.Secondary?.UsedPercent ?? 0);
            return TileRenderer.Ring("USAGE", $"5h {Format.Percent(usage?.Primary?.UsedPercent)}", $"week {Format.Percent(usage?.Secondary?.UsedPercent)}", usage == null ? TileRenderer.Idle : Format.UsageColor(worst), imageSize);
        }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var usage = this.Hub?.CodexUsage;
            var credits = usage == null ? "?" : usage.HasCredits ? (usage.CreditBalance?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "yes") : "none";
            return new[]
            {
                this.Limit("five-hour", "CODEX", "5h", usage?.Primary == null ? null : new LimitWindow(usage.Primary.UsedPercent, usage.Primary.ResetsAt)),
                this.Limit("week", "CODEX", "Week", usage?.Secondary == null ? null : new LimitWindow(usage.Secondary.UsedPercent, usage.Secondary.ResetsAt)),
                this.CodexContext(),
                this.Info("plan", "PLAN", usage?.Plan ?? "?", $"credits {credits}", TileRenderer.Idle),
                this.Info("lifetime", "LIFETIME", Format.Tokens(usage?.LifetimeTokens), "tokens", TileRenderer.Idle),
                this.Info("peak", "PEAK DAY", Format.Tokens(usage?.PeakDailyTokens), "tokens", TileRenderer.Idle),
                this.Info("streak", "STREAK", $"{usage?.CurrentStreakDays?.ToString() ?? "?"} days", $"best {usage?.LongestStreakDays?.ToString() ?? "?"}", TileRenderer.Idle),
                this.RefreshItem(),
            };
        }
    }

    // Claude and Codex together, for iTerm where both CLIs run.
    public class AllUsageFolder : UsageFolder
    {
        public AllUsageFolder()
            : base("Usage")
        {
        }

        protected override BitmapImage FolderImage(PluginImageSize imageSize)
        {
            var claude = this.Hub?.Usage.Limits;
            var codex = this.Hub?.CodexUsage;
            var worst = Math.Max(Format.Worst(claude?.FiveHour, claude?.SevenDay) ?? 0, Math.Max(codex?.Primary?.UsedPercent ?? 0, codex?.Secondary?.UsedPercent ?? 0));
            return TileRenderer.Ring("USAGE", $"C {Format.Percent(claude?.FiveHour?.UsedPercent)}", $"X {Format.Percent(codex?.Primary?.UsedPercent)}", Format.UsageColor(worst), imageSize);
        }

        protected override IReadOnlyList<MenuItem> Root()
        {
            var claude = this.Hub?.Usage.Limits;
            var codex = this.Hub?.CodexUsage;
            return new[]
            {
                this.Limit("claude-five-hour", "CLAUDE", "5h", claude?.FiveHour),
                this.Limit("claude-week", "CLAUDE", "Week", claude?.SevenDay),
                this.ClaudeContext(),
                this.Limit("codex-five-hour", "CODEX", "5h", codex?.Primary == null ? null : new LimitWindow(codex.Primary.UsedPercent, codex.Primary.ResetsAt)),
                this.Limit("codex-week", "CODEX", "Week", codex?.Secondary == null ? null : new LimitWindow(codex.Secondary.UsedPercent, codex.Secondary.ResetsAt)),
                this.CodexContext(),
                this.Info("plan", "CODEX", codex?.Plan ?? "?", "plan", TileRenderer.Idle),
                this.RefreshItem(),
            };
        }
    }
}
