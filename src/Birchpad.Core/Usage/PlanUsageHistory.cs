using System.Text.Json.Nodes;

namespace Birchpad.Core.Usage;

/// <summary>
/// Reads Claude Desktop's <c>plan-usage-history.json</c>, which records the 5-hour (<c>fh</c>) and
/// 7-day (<c>sd</c>) percentages about every 15 minutes. It is an internal file with no stability promise.
/// </summary>
public static class PlanUsageHistory
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "Claude", "plan-usage-history.json");

    /// <summary>The newest sample, or null when the file is missing or unreadable.</summary>
    public static ClaudeLimits? ReadLatest(string? path = null)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path ?? DefaultPath))?["samples"] is not JsonArray samples)
            {
                return null;
            }

            var latest = samples.OfType<JsonObject>()
                .Where(s => s["t"] is JsonValue && s["u"] is JsonObject)
                .MaxBy(s => s["t"]!.GetValue<double>());
            if (latest is null)
            {
                return null;
            }

            var t = latest["t"]!.GetValue<double>();
            var at = t > 1e11 ? DateTimeOffset.FromUnixTimeMilliseconds((long)t) : DateTimeOffset.FromUnixTimeSeconds((long)t);
            var u = (JsonObject)latest["u"]!;
            return new ClaudeLimits(Window(u["fh"]), Window(u["sd"]), at, "Claude Desktop");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static LimitWindow? Window(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) ? new LimitWindow(d, null) : null;
}
