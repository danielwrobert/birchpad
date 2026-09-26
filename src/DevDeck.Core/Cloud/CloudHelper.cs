using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DevDeck.Core.Mac;

namespace DevDeck.Core.Cloud;

/// <summary>
/// Reads cloud routines and remote sessions by running headless Claude Code on the user's subscription.
/// The model only drives two read-only tool calls; results are taken from the tool output, not from its reply.
/// </summary>
public sealed partial class CloudHelper
{
    // Keep the prompt tiny: every word is billed on every run.
    internal const string Prompt = "Load RemoteTrigger and ListAgents with ToolSearch. Call RemoteTrigger with action list, then call ListAgents. Then reply: done.";

    private readonly string claudePath;
    private readonly string workingDirectory;

    public CloudHelper(string claudePath, string workingDirectory)
    {
        this.claudePath = claudePath;
        this.workingDirectory = workingDirectory;
    }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Finds the <c>claude</c> CLI; the plugin service's PATH does not include the user's shell setup.</summary>
    public static string? FindClaude()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            Path.Combine(home, ".local", "bin", "claude"),
            "/opt/homebrew/bin/claude",
            "/usr/local/bin/claude",
            Path.Combine(home, ".claude", "local", "claude"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>The arguments for one run: no user settings (so no hooks fire), no MCP servers, only the needed tools.</summary>
    internal IReadOnlyList<string> Arguments(string emptyMcpConfig) =>
    [
        "-p", Prompt,
        "--model", "haiku",
        "--output-format", "stream-json",
        "--verbose",
        "--setting-sources", string.Empty,
        "--strict-mcp-config",
        "--mcp-config", emptyMcpConfig,
        "--disable-slash-commands",
        "--tools", "ToolSearch,RemoteTrigger,ListAgents",
        "--allowedTools", "ToolSearch,RemoteTrigger,ListAgents",
        "--max-turns", "6",
    ];

    public CloudSnapshot Run(DateTimeOffset now)
    {
        Directory.CreateDirectory(this.workingDirectory);
        var emptyMcp = Path.Combine(this.workingDirectory, "empty-mcp.json");
        if (!File.Exists(emptyMcp))
        {
            File.WriteAllText(emptyMcp, """{"mcpServers":{}}""");
        }

        try
        {
            var (exitCode, output, error) = ProcessRunner.Run(this.claudePath, this.Arguments(emptyMcp), this.Timeout, this.workingDirectory);
            if (exitCode != 0)
            {
                return CloudSnapshot.Failed(now, $"claude exited {exitCode}: {Last(error)}");
            }

            return Parse(output, now);
        }
        catch (Exception ex)
        {
            return CloudSnapshot.Failed(now, ex.Message);
        }
    }

    /// <summary>Pulls the RemoteTrigger and ListAgents results out of stream-json output.</summary>
    internal static CloudSnapshot Parse(string streamJson, DateTimeOffset now)
    {
        var toolNames = new Dictionary<string, string>(StringComparer.Ordinal);
        string? triggers = null;
        string? agents = null;
        string? resultError = null;

        foreach (var line in streamJson.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            JsonObject? message;
            try
            {
                message = JsonNode.Parse(line) as JsonObject;
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }

            var type = message?["type"]?.GetValue<string>();
            if (type == "result" && message!["is_error"] is JsonValue isError && isError.GetValue<bool>())
            {
                resultError = message["result"]?.ToString() ?? "claude reported an error";
            }

            if ((type != "assistant" && type != "user") || message!["message"]?["content"] is not JsonArray content)
            {
                continue;
            }

            foreach (var block in content.OfType<JsonObject>())
            {
                var kind = block["type"]?.GetValue<string>();
                if (kind == "tool_use" && block["id"] is JsonValue id && block["name"] is JsonValue name)
                {
                    toolNames[id.GetValue<string>()] = name.GetValue<string>();
                }
                else if (kind == "tool_result" && block["tool_use_id"] is JsonValue useId && toolNames.TryGetValue(useId.GetValue<string>(), out var tool))
                {
                    var text = ResultText(block["content"]);
                    if (tool == "RemoteTrigger")
                    {
                        triggers = text;
                    }
                    else if (tool == "ListAgents")
                    {
                        agents = text;
                    }
                }
            }
        }

        if (triggers is null && agents is null)
        {
            return CloudSnapshot.Failed(now, resultError ?? "the helper returned no tool results");
        }

        return new CloudSnapshot(now, ParseRoutines(triggers), ParseAgents(agents), null);
    }

    // RemoteTrigger answers "HTTP 200" on the first line, then the API body.
    internal static IReadOnlyList<CloudRoutine> ParseRoutines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var start = text.IndexOf('{');
        if (start < 0)
        {
            return [];
        }

        try
        {
            if (JsonNode.Parse(text[start..])?["data"] is not JsonArray data)
            {
                return [];
            }

            return data.OfType<JsonObject>().Select(r =>
            {
                var id = First(r, "id", "trigger_id") ?? string.Empty;
                return new CloudRoutine(
                    id,
                    First(r, "name", "title", "description") ?? id,
                    First(r, "cron_expression", "cron", "schedule"),
                    r["enabled"] is JsonValue e && e.TryGetValue<bool>(out var enabled) ? enabled : null,
                    Date(First(r, "next_run_at", "next_run")),
                    Date(First(r, "last_run_at", "last_run")),
                    r);
            }).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<CloudAgent> ParseAgents(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return text.Split('\n')
            .Select(line => AgentLine().Match(line))
            .Where(m => m.Success)
            .Select(m => new CloudAgent(
                m.Groups["name"].Value.Trim(),
                m.Groups["id"].Value,
                m.Groups["kind"].Value.Trim(),
                m.Groups["status"].Value.Trim(),
                m.Groups["started"].Success ? m.Groups["started"].Value.Trim() : null))
            .ToList();
    }

    // "  logi-94 [9cc107]  ·  interactive  ·  busy  ·  started 50m ago"
    [GeneratedRegex(@"^\s+(?<name>\S.*?)\s+\[(?<id>[0-9a-zA-Z]+)\]\s+·\s+(?<kind>[^·]+?)\s+·\s+(?<status>[^·]+?)(\s+·\s+(?<started>.+))?\s*$")]
    private static partial Regex AgentLine();

    private static string ResultText(JsonNode? content) => content switch
    {
        JsonValue v => v.ToString(),
        JsonArray a => string.Join("\n", a.OfType<JsonObject>().Select(b => b["text"]?.ToString()).Where(t => t is not null)),
        _ => string.Empty,
    };

    private static string? First(JsonObject obj, params string[] keys) =>
        keys.Select(k => obj[k]).OfType<JsonValue>().Select(v => v.ToString()).FirstOrDefault(s => !string.IsNullOrEmpty(s));

    private static DateTimeOffset? Date(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static string Last(string error)
    {
        var lines = error.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 ? "no error output" : lines[^1].Trim();
    }
}
