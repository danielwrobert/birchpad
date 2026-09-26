using System.Text.Json.Nodes;

namespace DevDeck.Core.Cloud;

/// <summary>A cloud routine (remote trigger). Field names are read defensively; <see cref="Raw"/> keeps the rest.</summary>
public sealed record CloudRoutine(string Id, string Name, string? Schedule, bool? Enabled, DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt, JsonObject Raw);

/// <summary>One row of the agent list: a peer session on this Mac, another machine, or the cloud.</summary>
public sealed record CloudAgent(string Name, string Id, string Kind, string Status, string? Started);

/// <summary>The result of one helper run. On failure the lists are empty and <see cref="Error"/> says why.</summary>
public sealed record CloudSnapshot(DateTimeOffset FetchedAt, IReadOnlyList<CloudRoutine> Routines, IReadOnlyList<CloudAgent> Agents, string? Error)
{
    public static CloudSnapshot Failed(DateTimeOffset at, string error) => new(at, [], [], error);
}
