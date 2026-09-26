using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DevDeck.Core.Permissions;
using DevDeck.Core.Sessions;

namespace DevDeck.Core.Http;

/// <summary>
/// Receives agent hooks over HTTP on the loopback interface only.
/// Every failure is answered with an empty 200 or a 4xx, so a hook never blocks the agent.
/// </summary>
public sealed class HookListener : IDisposable
{
    // Events after which a held permission request can no longer be answered from the keypad.
    private static readonly HashSet<string> MovedOnEvents = new(StringComparer.Ordinal)
    {
        "PostToolUse", "PostToolUseFailure", "PermissionDenied", "UserPromptSubmit", "Stop", "StopFailure", "SessionEnd",
    };

    private readonly HttpListener listener = new();
    private readonly SessionStore store;
    private readonly PermissionBroker broker;
    private readonly Func<DateTimeOffset> clock;
    private readonly Action<string>? log;
    private CancellationTokenSource? stopping;
    private Task? loop;

    public HookListener(int port, SessionStore store, PermissionBroker broker, Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        this.Port = port;
        this.store = store;
        this.broker = broker;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.log = log;

        // Both host spellings, never a wildcard: the approval route must not be reachable from the network.
        this.listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        this.listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public int Port { get; }

    /// <summary>How long a permission request is held for a keypad answer.</summary>
    public TimeSpan PermissionTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public bool IsRunning => this.listener.IsListening;

    /// <summary>Starts listening. Throws <see cref="HttpListenerException"/> when the port is taken.</summary>
    public void Start()
    {
        this.listener.Start();
        this.stopping = new CancellationTokenSource();
        this.loop = Task.Run(() => this.AcceptLoop(this.stopping.Token));
    }

    public void Stop()
    {
        this.stopping?.Cancel();
        if (this.listener.IsListening)
        {
            this.listener.Stop();
        }
    }

    public void Dispose()
    {
        this.Stop();
        this.listener.Close();
        this.stopping?.Dispose();
    }

    private async Task AcceptLoop(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await this.listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (cancel.IsCancellationRequested || !this.listener.IsListening)
            {
                return;
            }
            catch (Exception ex)
            {
                this.log?.Invoke($"Hook listener accept failed: {ex.Message}");
                continue;
            }

            _ = Task.Run(() => this.Handle(context, cancel), cancel);
        }
    }

    private async Task Handle(HttpListenerContext context, CancellationToken cancel)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (context.Request.HttpMethod == "GET" && path == "/health")
            {
                await Respond(context, 200, "ok", "text/plain").ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/sessions")
            {
                await Respond(context, 200, this.DescribeState()).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod != "POST")
            {
                await Respond(context, 405, null).ConfigureAwait(false);
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync(cancel).ConfigureAwait(false);
            }

            var response = path switch
            {
                "/claude/hook" => this.OnClaudeHook(body),
                "/claude/permission" => await this.OnClaudePermission(body, cancel).ConfigureAwait(false),
                "/claude/statusline" => this.OnClaudeStatusLine(body),
                _ => (Status: 404, Body: (string?)null),
            };
            await Respond(context, response.Status, response.Body).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.log?.Invoke($"Hook request failed: {ex.Message}");
            try
            {
                await Respond(context, 200, null).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The client is gone; nothing left to answer.
            }
        }
    }

    private (int Status, string? Body) OnClaudeHook(string body)
    {
        var e = HookEvent.ParseClaude(body);
        if (e is null)
        {
            return (400, null);
        }

        this.store.Apply(e, this.clock());
        if (MovedOnEvents.Contains(e.EventName))
        {
            this.broker.ReleaseSession(e.SessionId);
        }

        return (200, null);
    }

    private async Task<(int Status, string? Body)> OnClaudePermission(string body, CancellationToken cancel)
    {
        var e = HookEvent.ParseClaude(body);
        if (e?.Raw is null)
        {
            return (400, null);
        }

        this.store.Apply(e, this.clock());
        var answer = await this.broker.WaitAsync(e.SessionId, e.Raw, this.PermissionTimeout, this.clock(), cancel).ConfigureAwait(false);
        return (200, answer);
    }

    private (int Status, string? Body) OnClaudeStatusLine(string body)
    {
        JsonObject? obj;
        try
        {
            obj = JsonNode.Parse(body) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return (400, null);
        }

        var sessionId = obj?["session_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(sessionId))
        {
            return (400, null);
        }

        var model = obj?["model"]?["display_name"]?.GetValue<string>();
        this.store.ApplyModel(sessionId, model, this.clock());
        return (200, null);
    }

    // A read-only view of what the hub knows, for debugging without the keypad.
    private string DescribeState()
    {
        var state = new JsonObject
        {
            ["sessions"] = new JsonArray(this.store.All().Select(s => (JsonNode)new JsonObject
            {
                ["sessionId"] = s.SessionId,
                ["slot"] = s.Slot,
                ["source"] = s.Source.ToString(),
                ["state"] = s.State.ToString(),
                ["stateSince"] = s.StateSince.ToString("O"),
                ["name"] = s.DisplayName,
                ["cwd"] = s.Cwd,
                ["model"] = s.Model,
                ["lastTool"] = s.LastToolName,
            }).ToArray()),
            ["pendingPermissions"] = new JsonArray(this.broker.Pending().Select(p => (JsonNode)new JsonObject
            {
                ["sessionId"] = p.SessionId,
                ["tool"] = p.ToolName,
                ["receivedAt"] = p.ReceivedAt.ToString("O"),
            }).ToArray()),
        };
        return state.ToJsonString();
    }

    private static async Task Respond(HttpListenerContext context, int status, string? body, string contentType = "application/json")
    {
        context.Response.StatusCode = status;
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }

        context.Response.Close();
    }
}
