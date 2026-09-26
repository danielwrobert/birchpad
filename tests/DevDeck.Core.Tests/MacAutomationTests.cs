using DevDeck.Core.Mac;

namespace DevDeck.Core.Tests;

public class MacAutomationTests
{
    [Fact]
    public void Quote_escapes_backslashes_and_quotes()
    {
        Assert.Equal("\"a\\\\b\\\"c\"", MacAutomation.Quote("a\\b\"c"));
    }

    [Fact]
    public void Iterm_script_matches_the_path_with_or_without_a_trailing_slash()
    {
        var script = MacAutomation.ItermScript("/Users/me/site/", sendEscape: false);

        Assert.Contains("p is \"/Users/me/site\" or p is (\"/Users/me/site\" & \"/\")", script);
        Assert.Contains("tell s to select", script);
    }
}
