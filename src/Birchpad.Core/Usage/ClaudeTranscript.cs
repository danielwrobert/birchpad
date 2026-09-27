using System.Text.Json.Nodes;

namespace Birchpad.Core.Usage;

/// <summary>How full a Claude session's context is, from its transcript.</summary>
public sealed record ClaudeContext(long UsedTokens, long WindowTokens)
{
    public double Percent => this.WindowTokens <= 0 ? 0 : 100.0 * this.UsedTokens / this.WindowTokens;
}

/// <summary>
/// Reads context use from the last assistant message in a Claude Code transcript, for sessions whose
/// status line does not report it (Claude Desktop sessions, or ones started before the status line was set).
/// Only the usage numbers are read, never message text.
/// </summary>
public static class ClaudeTranscript
{
    public const long StandardWindow = 200_000;
    public const long LongWindow = 1_000_000;

    public static ClaudeContext? ReadContext(string? transcriptPath, string? model = null)
    {
        if (string.IsNullOrEmpty(transcriptPath) || !File.Exists(transcriptPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            const int tail = 1024 * 1024;
            stream.Seek(Math.Max(0, stream.Length - tail), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n');
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (!lines[i].Contains("\"assistant\"", StringComparison.Ordinal) || !lines[i].Contains("\"usage\"", StringComparison.Ordinal))
                {
                    continue;
                }

                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(lines[i]);
                }
                catch (System.Text.Json.JsonException)
                {
                    // The first line of the tail can be cut in half.
                    continue;
                }

                if (node?["type"]?.ToString() != "assistant" || node["message"]?["usage"] is not JsonObject usage)
                {
                    continue;
                }

                var used = Long(usage["input_tokens"]) + Long(usage["cache_read_input_tokens"]) + Long(usage["cache_creation_input_tokens"]);
                return new ClaudeContext(used, WindowFor(model ?? node["message"]?["model"]?.ToString(), used));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A transcript mid-write or moved away has no usable count this time.
        }

        return null;
    }

    // The transcript does not record the window size: a [1m] model or more than a standard window in use means 1M.
    internal static long WindowFor(string? model, long used) =>
        (model?.Contains("[1m]", StringComparison.OrdinalIgnoreCase) ?? false) || used > StandardWindow ? LongWindow : StandardWindow;

    private static long Long(JsonNode? node) => node is JsonValue v && v.TryGetValue<long>(out var l) ? l : 0;
}
