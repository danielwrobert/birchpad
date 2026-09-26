using System.Net;
using System.Net.Sockets;
using System.Text;
using DevDeck.Core.Http;
using DevDeck.Core.Permissions;
using DevDeck.Core.Sessions;

namespace DevDeck.Core.Tests;

public sealed class HookListenerTests : IDisposable
{
    private readonly SessionStore store = new();
    private readonly PermissionBroker broker = new();
    private readonly HookListener listener;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public HookListenerTests()
    {
        this.listener = new HookListener(FreePort(), this.store, this.broker);
        this.listener.Start();
    }

    public void Dispose()
    {
        this.listener.Dispose();
        this.http.Dispose();
    }

    private string Url(string path) => $"http://127.0.0.1:{this.listener.Port}{path}";

    private Task<HttpResponseMessage> Post(string path, string json) =>
        this.http.PostAsync(this.Url(path), new StringContent(json, Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Health_answers_ok()
    {
        Assert.Equal("ok", await this.http.GetStringAsync(this.Url("/health")));
    }

    [Fact]
    public async Task Hook_updates_the_store_and_returns_an_empty_body()
    {
        var response = await this.Post("/claude/hook", """{"session_id":"a","hook_event_name":"UserPromptSubmit","cwd":"/tmp/site"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Equal(SessionState.Working, this.store.Get("a")!.State);
    }

    [Fact]
    public async Task Bad_json_is_rejected_without_touching_the_store()
    {
        var response = await this.Post("/claude/hook", "not json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(this.store.All());
    }

    [Fact]
    public async Task Permission_waits_for_the_keypad_answer()
    {
        var pending = this.Post("/claude/permission", """{"session_id":"a","hook_event_name":"PermissionRequest","tool_name":"Bash"}""");
        for (var i = 0; i < 200 && this.broker.PendingCount == 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(SessionState.NeedsApproval, this.store.Get("a")!.State);
        Assert.True(this.broker.AnswerOldest(PermissionAnswer.Deny));

        var body = await (await pending).Content.ReadAsStringAsync();
        Assert.Contains("\"behavior\":\"deny\"", body);
    }

    private async Task<Task<HttpResponseMessage>> Hold(string json)
    {
        var before = this.broker.PendingCount;
        var pending = this.Post("/claude/permission", json);
        for (var i = 0; i < 200 && this.broker.PendingCount == before; i++)
        {
            await Task.Delay(10);
        }

        return pending;
    }

    [Fact]
    public async Task The_matching_tool_finishing_releases_a_held_permission()
    {
        var pending = await this.Hold("""{"session_id":"a","hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"ls"}}""");

        await this.Post("/claude/hook", """{"session_id":"a","hook_event_name":"PostToolUse","tool_name":"Bash","tool_input":{"command":"ls"}}""");

        Assert.Equal(string.Empty, await (await pending).Content.ReadAsStringAsync());
        Assert.Equal(SessionState.Working, this.store.Get("a")!.State);
    }

    [Fact]
    public async Task Another_tool_finishing_keeps_the_request_held_and_the_tile_waiting()
    {
        var bash = await this.Hold("""{"session_id":"a","hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"ls"}}""");
        var edit = await this.Hold("""{"session_id":"a","hook_event_name":"PermissionRequest","tool_name":"Edit","agent_id":"sub-1","tool_input":{"file_path":"/x"}}""");

        await this.Post("/claude/hook", """{"session_id":"a","hook_event_name":"PostToolUse","tool_name":"Read","agent_id":"sub-2","tool_input":{"file_path":"/y"}}""");
        await this.Post("/claude/hook", """{"session_id":"a","hook_event_name":"PostToolUse","tool_name":"Bash","tool_input":{"command":"pwd"}}""");

        // Bash is the only held Bash request, so a Bash result releases it even with a different input.
        Assert.Equal(string.Empty, await (await bash).Content.ReadAsStringAsync());
        Assert.False(edit.IsCompleted);
        Assert.Equal(SessionState.NeedsApproval, this.store.Get("a")!.State);

        Assert.True(this.broker.AnswerOldest(PermissionAnswer.Allow));
        Assert.Contains("allow", await (await edit).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ending_the_turn_releases_everything_held()
    {
        var first = await this.Hold("""{"session_id":"a","hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"ls"}}""");
        var second = await this.Hold("""{"session_id":"a","hook_event_name":"PermissionRequest","tool_name":"Edit","tool_input":{"file_path":"/x"}}""");

        await this.Post("/claude/hook", """{"session_id":"a","hook_event_name":"Stop"}""");

        Assert.Equal(string.Empty, await (await first).Content.ReadAsStringAsync());
        Assert.Equal(string.Empty, await (await second).Content.ReadAsStringAsync());
        Assert.Equal(0, this.broker.PendingCount);
    }

    [Theory]
    [InlineData("text/plain", null)]
    [InlineData("application/json", "https://evil.example")]
    public async Task Cross_site_posts_are_refused(string contentType, string? origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, this.Url("/claude/hook"))
        {
            Content = new StringContent("""{"session_id":"x","hook_event_name":"SessionStart"}""", Encoding.UTF8, contentType),
        };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        var response = await this.http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(this.store.All());
    }

    [Fact]
    public async Task Status_line_records_the_model()
    {
        await this.Post("/claude/hook", """{"session_id":"a","hook_event_name":"SessionStart"}""");

        await this.Post("/claude/statusline", """{"session_id":"a","model":{"id":"claude-opus-5-5","display_name":"Opus 5.5"}}""");

        Assert.Equal("Opus 5.5", this.store.Get("a")!.Model);
    }

    [Fact]
    public async Task Sessions_endpoint_lists_sessions_and_pending_requests()
    {
        await this.Post("/claude/hook", """{"session_id":"a","hook_event_name":"UserPromptSubmit","cwd":"/tmp/site"}""");

        var json = System.Text.Json.Nodes.JsonNode.Parse(await this.http.GetStringAsync(this.Url("/sessions")))!;

        Assert.Equal("a", json["sessions"]![0]!["sessionId"]!.GetValue<string>());
        Assert.Equal("Working", json["sessions"]![0]!["state"]!.GetValue<string>());
        Assert.Empty(json["pendingPermissions"]!.AsArray());
    }

    [Fact]
    public async Task Codex_hooks_create_codex_sessions()
    {
        await this.Post("/codex/hook", """{"session_id":"t1","hook_event_name":"UserPromptSubmit","cwd":"/tmp/site","model":"gpt-5.5"}""");
        await this.Post("/codex/hook", """{"session_id":"t1","hook_event_name":"Interrupt","turn_id":"x"}""");

        var session = this.store.Get("t1")!;
        Assert.Equal("codex", session.Agent);
        Assert.Equal(SessionSource.Codex, session.Source);
        Assert.Equal(SessionState.Done, session.State);
        Assert.Equal("gpt-5.5", session.Model);
    }

    [Fact]
    public void A_taken_port_fails_to_start()
    {
        using var second = new HookListener(this.listener.Port, this.store, this.broker);

        Assert.ThrowsAny<Exception>(() => second.Start());
    }

    private static int FreePort()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}
