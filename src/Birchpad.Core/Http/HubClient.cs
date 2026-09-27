using System.Text.Json.Nodes;

namespace Birchpad.Core.Http;

/// <summary>A session as Agent Hub's <c>/sessions</c> route describes it.</summary>
public sealed record HubSession(string SessionId, string Source, string State, string Name, string? Cwd);

/// <summary>
/// Reads Agent Hub's session list over loopback, for plugins that run apart from it (such as iTerm).
/// Returns an empty list when Agent Hub is not running.
/// </summary>
public sealed class HubClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public const int DefaultPort = 8766;

    private readonly int? port;

    /// <summary>With no port, reads the one Agent Hub recorded in <see cref="PortFile"/> on each call.</summary>
    public HubClient(int? port = null)
    {
        this.port = port;
    }

    public static string PortFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Birchpad", "agent-hub-port");

    /// <summary>Records the port Agent Hub listens on, for plugins that talk to it.</summary>
    public static void RecordPort(int port, string? file = null)
    {
        try
        {
            file ??= PortFile;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Other plugins fall back to the default port.
        }
    }

    public static int ReadPort(string? file = null)
    {
        try
        {
            return int.TryParse(File.ReadAllText(file ?? PortFile).Trim(), out var port) && port is > 0 and < 65536 ? port : DefaultPort;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DefaultPort;
        }
    }

    public IReadOnlyList<HubSession> Sessions()
    {
        try
        {
            var body = Http.GetStringAsync($"http://127.0.0.1:{this.port ?? ReadPort()}/sessions").GetAwaiter().GetResult();
            return Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<HubSession> Parse(string body) =>
        (JsonNode.Parse(body)?["sessions"] as JsonArray)?.OfType<JsonObject>()
            .Select(s => new HubSession(
                s["sessionId"]?.ToString() ?? string.Empty,
                s["source"]?.ToString() ?? string.Empty,
                s["state"]?.ToString() ?? string.Empty,
                s["name"]?.ToString() ?? string.Empty,
                s["cwd"]?.ToString()))
            .Where(s => s.SessionId.Length > 0)
            .ToList() ?? [];

    /// <summary>The session running in <paramref name="path"/>, preferring one that is not finished.</summary>
    public static HubSession? AtPath(IReadOnlyList<HubSession> sessions, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var target = path.TrimEnd('/');
        return sessions
            .Where(s => s.Cwd?.TrimEnd('/') == target)
            .OrderBy(s => s.State is "Ended" or "Done" ? 1 : 0)
            .FirstOrDefault();
    }
}
