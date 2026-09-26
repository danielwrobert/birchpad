using System.Text.Json.Nodes;
using DevDeck.Core.Permissions;

namespace DevDeck.Core.Tests;

public class PermissionBrokerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static JsonObject Request(bool withSuggestion = false)
    {
        var obj = new JsonObject { ["session_id"] = "a", ["hook_event_name"] = "PermissionRequest", ["tool_name"] = "Bash" };
        if (withSuggestion)
        {
            obj["permission_suggestions"] = JsonNode.Parse("""[{"type":"addRules","rules":[{"toolName":"Bash","ruleContent":"ls"}],"behavior":"allow","destination":"localSettings"}]""");
        }

        return obj;
    }

    private static async Task WaitForPending(PermissionBroker broker, int count)
    {
        for (var i = 0; i < 100 && broker.PendingCount < count; i++)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Allow_answer_returns_allow_decision_and_clears_pending()
    {
        var broker = new PermissionBroker();
        var wait = broker.WaitAsync("a", Request(), TimeSpan.FromSeconds(5), T0);
        await WaitForPending(broker, 1);

        Assert.True(broker.AnswerOldest(PermissionAnswer.Allow));
        var json = JsonNode.Parse((await wait)!)!;

        Assert.Equal("PermissionRequest", json["hookSpecificOutput"]!["hookEventName"]!.GetValue<string>());
        Assert.Equal("allow", json["hookSpecificOutput"]!["decision"]!["behavior"]!.GetValue<string>());
        Assert.Equal(0, broker.PendingCount);
    }

    [Fact]
    public void Always_echoes_the_permission_suggestions()
    {
        var json = JsonNode.Parse(PermissionBroker.BuildResponse(PermissionAnswer.AllowAlways, Request(withSuggestion: true)))!;
        var decision = json["hookSpecificOutput"]!["decision"]!;

        Assert.Equal("allow", decision["behavior"]!.GetValue<string>());
        Assert.Equal("addRules", decision["updatedPermissions"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Deny_includes_a_message()
    {
        var decision = JsonNode.Parse(PermissionBroker.BuildResponse(PermissionAnswer.Deny, Request()))!["hookSpecificOutput"]!["decision"]!;

        Assert.Equal("deny", decision["behavior"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(decision["message"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Timeout_returns_null()
    {
        var broker = new PermissionBroker();

        Assert.Null(await broker.WaitAsync("a", Request(), TimeSpan.FromMilliseconds(50), T0));
        Assert.Equal(0, broker.PendingCount);
    }

    [Fact]
    public async Task Release_session_returns_null_without_answering()
    {
        var broker = new PermissionBroker();
        var wait = broker.WaitAsync("a", Request(), TimeSpan.FromSeconds(5), T0);
        await WaitForPending(broker, 1);

        broker.ReleaseSession("a");

        Assert.Null(await wait);
        Assert.False(broker.AnswerOldest(PermissionAnswer.Allow));
    }

    [Fact]
    public async Task Answer_session_targets_only_that_session()
    {
        var broker = new PermissionBroker();
        var first = broker.WaitAsync("a", Request(), TimeSpan.FromSeconds(5), T0);
        var second = broker.WaitAsync("b", Request(), TimeSpan.FromSeconds(5), T0);
        await WaitForPending(broker, 2);

        Assert.True(broker.AnswerSession("b", PermissionAnswer.Deny));

        Assert.Contains("deny", (await second)!);
        Assert.False(first.IsCompleted);
        broker.AnswerOldest(PermissionAnswer.Allow);
        Assert.Contains("allow", (await first)!);
    }
}
