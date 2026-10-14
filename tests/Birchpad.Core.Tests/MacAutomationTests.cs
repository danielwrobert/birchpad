using Birchpad.Core.Mac;

namespace Birchpad.Core.Tests;

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

    [Fact]
    public void Ghostty_script_matches_the_path_with_or_without_a_trailing_slash()
    {
        var script = MacAutomation.GhosttyScript("/Users/me/site/", "focus t");

        Assert.Contains("set p to working directory of t", script);
        Assert.Contains("p is \"/Users/me/site\" or p is (\"/Users/me/site\" & \"/\")", script);
        Assert.Contains("focus t", script);
    }

    [Fact]
    public void Ghostty_keys_send_named_keys_and_type_characters()
    {
        var script = MacAutomation.GhosttyKeys(["down", "esc", "enter", "2", "\\"]);

        Assert.Contains("send key \"arrowDown\" to t", script);
        Assert.Contains("send key \"escape\" to t", script);
        Assert.Contains("send key \"enter\" to t", script);
        Assert.Contains("perform action \"text:2\" on t", script);
        Assert.Contains("perform action \"text:\\\\\\\\\" on t", script);
    }

    [Fact]
    public void Ghostty_write_pastes_then_presses_enter_when_submitting()
    {
        Assert.Equal("input text \"/clear\" to t", MacAutomation.GhosttyWrite("/clear", newline: false));
        Assert.EndsWith("send key \"enter\" to t", MacAutomation.GhosttyWrite("/clear", newline: true));
        Assert.DoesNotContain("input text", MacAutomation.GhosttyWrite("", newline: true));
    }

    [Fact]
    public void Terminal_bytes_match_what_a_terminal_sends()
    {
        Assert.Equal("\u001b[B", MacAutomation.TerminalBytes("down"));
        Assert.Equal("\r", MacAutomation.TerminalBytes("enter"));
        Assert.Equal("1", MacAutomation.TerminalBytes("1"));
    }
}
