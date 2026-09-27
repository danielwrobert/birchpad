namespace Birchpad.Core.Usage;

/// <summary>Use of one rate-limit window, as a percentage with its reset time when known.</summary>
public sealed record LimitWindow(double UsedPercent, DateTimeOffset? ResetsAt);

/// <summary>Claude plan limits, from the status line or Claude Desktop's usage history.</summary>
public sealed record ClaudeLimits(LimitWindow? FiveHour, LimitWindow? SevenDay, DateTimeOffset UpdatedAt, string Source);

/// <summary>One Claude Code session's numbers from its status line.</summary>
public sealed record ClaudeSessionUsage(double? ContextPercent, double? CostUsd, int? LinesAdded, int? LinesRemoved, DateTimeOffset UpdatedAt);
