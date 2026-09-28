using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Birchpad.Core.Herdr;

/// <summary>An error herdr returned, such as <c>agent_not_ready</c>, or <c>unreachable</c> when no server answers.</summary>
public sealed class HerdrException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Talks to a running herdr server over its socket API: one connection per request, one JSON line each way.
/// The socket is the configured path, then <c>HERDR_SOCKET_PATH</c>, then the default session's,
/// then the most recently started named session's, so no session name is fixed in code.
/// </summary>
public sealed class HerdrClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ResolveFor = TimeSpan.FromSeconds(10);

    private readonly string? configuredSocket;
    private readonly string configDirectory;
    private readonly object gate = new();
    private int nextId;
    private string? resolved;
    private DateTimeOffset resolvedAt;

    public HerdrClient(string? socketPath = null, string? configDirectory = null)
    {
        // HERDR_SOCKET_PATH applies only to the default config, so a caller that names a config directory gets that one.
        this.configuredSocket = !string.IsNullOrWhiteSpace(socketPath) ? socketPath
            : configDirectory is null ? Environment.GetEnvironmentVariable("HERDR_SOCKET_PATH")
            : null;
        this.configDirectory = configDirectory ?? DefaultConfigDirectory();
    }

    public static string DefaultConfigDirectory()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrEmpty(xdg) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config") : xdg;
        return Path.Combine(root, "herdr");
    }

    /// <summary>The socket in use, or null when no herdr server is running.</summary>
    public string? SocketPath
    {
        get
        {
            lock (this.gate)
            {
                if (this.resolved is not null && DateTimeOffset.UtcNow - this.resolvedAt < ResolveFor)
                {
                    return this.resolved;
                }
            }

            var found = this.Candidates().FirstOrDefault(Answers);
            lock (this.gate)
            {
                this.resolved = found;
                this.resolvedAt = DateTimeOffset.UtcNow;
            }

            return found;
        }
    }

    internal IEnumerable<string> Candidates()
    {
        if (!string.IsNullOrWhiteSpace(this.configuredSocket))
        {
            yield return this.configuredSocket;
            yield break;
        }

        yield return Path.Combine(this.configDirectory, "herdr.sock");
        var sessions = Path.Combine(this.configDirectory, "sessions");
        if (!Directory.Exists(sessions))
        {
            yield break;
        }

        IEnumerable<FileInfo> sockets;
        try
        {
            sockets = new DirectoryInfo(sessions).GetDirectories()
                .Select(d => new FileInfo(Path.Combine(d.FullName, "herdr.sock")))
                .Where(f => f.Exists)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var socket in sockets)
        {
            yield return socket.FullName;
        }
    }

    private static bool Answers(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(path));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Sends one request and returns its <c>result</c>; throws <see cref="HerdrException"/> on an error reply.</summary>
    public JsonNode? Request(string method, JsonObject? parameters = null)
    {
        var path = this.SocketPath ?? throw new HerdrException("unreachable", "herdr is not running");
        var id = $"birchpad_{Interlocked.Increment(ref this.nextId)}";
        var request = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() };
        string line;
        try
        {
            line = Exchange(path, request.ToJsonString());
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
        {
            lock (this.gate)
            {
                this.resolved = null;
            }

            throw new HerdrException("unreachable", $"herdr at {path}: {ex.Message}");
        }

        var reply = JsonNode.Parse(line);
        if (reply?["error"] is JsonObject error)
        {
            throw new HerdrException(error["code"]?.GetValue<string>() ?? "error", error["message"]?.GetValue<string>() ?? "herdr error");
        }

        return reply?["result"];
    }

    internal static string Exchange(string path, string request)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
        {
            SendTimeout = (int)Timeout.TotalMilliseconds,
            ReceiveTimeout = (int)Timeout.TotalMilliseconds,
        };
        socket.Connect(new UnixDomainSocketEndPoint(path));
        socket.Send(Encoding.UTF8.GetBytes(request + "\n"));

        var received = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var read = socket.Receive(buffer);
            if (read == 0)
            {
                break;
            }

            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
            received.Write(buffer, 0, newline >= 0 ? newline : read);
            if (newline >= 0)
            {
                break;
            }
        }

        return received.Length > 0 ? Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length) : throw new IOException("herdr closed the connection without a reply");
    }

    /// <summary>The live session, or <see cref="HerdrSnapshot.Empty"/> when herdr is not running.</summary>
    public HerdrSnapshot Snapshot()
    {
        try
        {
            return HerdrSnapshot.Parse(this.Request("session.snapshot")?["snapshot"]);
        }
        catch (HerdrException)
        {
            return HerdrSnapshot.Empty;
        }
    }

    public void FocusPane(string paneId) => this.Request("pane.focus", new JsonObject { ["pane_id"] = paneId });

    public void FocusTab(string tabId) => this.Request("tab.focus", new JsonObject { ["tab_id"] = tabId });

    public void FocusWorkspace(string workspaceId) => this.Request("workspace.focus", new JsonObject { ["workspace_id"] = workspaceId });

    /// <summary>Moves focus to the neighboring pane: left, right, up, or down.</summary>
    public void FocusDirection(string direction) => this.Request("pane.focus_direction", new JsonObject { ["direction"] = direction });

    public void ToggleZoom(string? paneId) => this.Request("pane.zoom", new JsonObject { ["pane_id"] = paneId, ["mode"] = "toggle" });

    /// <summary>Presses keys by herdr's names, such as <c>esc</c>, <c>enter</c>, <c>ctrl+c</c>, or <c>shift+tab</c>.</summary>
    public void SendKeys(string paneId, params string[] keys) =>
        this.Request("pane.send_keys", new JsonObject { ["pane_id"] = paneId, ["keys"] = new JsonArray(keys.Select(k => (JsonNode)k).ToArray()) });

    /// <summary>Types text, then presses Return when <paramref name="submit"/> is set, as one ordered write.</summary>
    public void SendText(string paneId, string text, bool submit)
    {
        var parameters = new JsonObject { ["pane_id"] = paneId, ["text"] = text };
        if (submit)
        {
            parameters["keys"] = new JsonArray("enter");
        }

        this.Request("pane.send_input", parameters);
    }

    /// <summary>
    /// Submits a prompt to the agent in a pane, pasted the way the agent expects so a long or multi-line prompt arrives whole.
    /// herdr refuses with <c>agent_blocked</c> while the agent waits on an approval.
    /// </summary>
    public void Prompt(string paneId, string text) => this.Request("agent.prompt", new JsonObject { ["target"] = paneId, ["text"] = text });

    /// <summary>Splits a pane (the focused one when null) to the right or down, and returns the new pane's ID.</summary>
    public string? Split(string? paneId, string direction, string? cwd, bool focus)
    {
        var parameters = new JsonObject { ["direction"] = direction, ["focus"] = focus, ["cwd"] = cwd };
        if (paneId is not null)
        {
            parameters["target_pane_id"] = paneId;
        }

        return this.Request("pane.split", parameters)?["pane"]?["pane_id"]?.GetValue<string>();
    }

    public void NewTab(string? workspaceId, string? cwd) =>
        this.Request("tab.create", new JsonObject { ["workspace_id"] = workspaceId, ["cwd"] = cwd, ["focus"] = true });

    /// <summary>Opens a focused workspace in <paramref name="cwd"/> and returns its first pane's ID.</summary>
    public string? NewWorkspace(string? cwd) =>
        this.Request("workspace.create", new JsonObject { ["cwd"] = cwd, ["focus"] = true })?["root_pane"]?["pane_id"]?.GetValue<string>();

    /// <summary>
    /// Starts an agent of <paramref name="kind"/> (claude, codex, ...) in a shell pane.
    /// An agent that stops at a startup prompt, such as folder trust, counts as started: it now waits for you.
    /// </summary>
    public void StartAgent(string name, string kind, string paneId)
    {
        try
        {
            this.Request("agent.start", new JsonObject { ["name"] = name, ["kind"] = kind, ["pane_id"] = paneId });
        }
        catch (HerdrException ex) when (ex.Code == "agent_not_ready")
        {
        }
    }

    /// <summary>Every agent kind this herdr server can detect and start, from its detection manifests.</summary>
    public IReadOnlyList<string> AgentKinds() =>
        (this.Request("server.agent_manifests")?["manifests"] as JsonArray ?? [])
            .Select(m => m?["agent"]?.GetValue<string>())
            .OfType<string>()
            .ToList();

    public IReadOnlyList<HerdrWorktree> Worktrees(string workspaceId) =>
        HerdrSnapshot.ParseWorktrees(this.Request("worktree.list", new JsonObject { ["workspace_id"] = workspaceId }));

    /// <summary>Opens a worktree as a workspace, or focuses the workspace it is already open in.</summary>
    public void OpenWorktree(string path, string workspaceId) =>
        this.Request("worktree.open", new JsonObject { ["path"] = path, ["workspace_id"] = workspaceId, ["focus"] = true });
}
