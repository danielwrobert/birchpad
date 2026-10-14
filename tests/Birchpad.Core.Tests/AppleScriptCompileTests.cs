using Birchpad.Core.Mac;

namespace Birchpad.Core.Tests;

// Compiles generated AppleScript without running it, so syntax errors show up here and not on a key press.
public sealed class AppleScriptCompileTests : IDisposable
{
    private const string Compiler = "/usr/bin/osacompile";

    private readonly string output = Path.Combine(Path.GetTempPath(), "birchpad-" + Guid.NewGuid().ToString("N") + ".scpt");

    public void Dispose()
    {
        if (File.Exists(this.output))
        {
            File.Delete(this.output);
        }
    }

    public static TheoryData<string, string> Scripts() => new()
    {
        { "iterm focus", MacAutomation.ItermScript("/Users/me/it's \"here\"", sendEscape: false) },
        { "iterm escape", MacAutomation.ItermScript("/Users/me/site", sendEscape: true) },
        { "iterm write at path", MacAutomation.ItermScript("/Users/me/site", sendEscape: false, write: ("/pr-review \"x\"", false)) },
        { "iterm answer at path", MacAutomation.ItermScript("/Users/me/site", sendEscape: false, write: ("1", false)) },
        { "iterm arrows at path", MacAutomation.ItermScript("/Users/me/site", sendEscape: false, write: ("\u001b[B\u001b[B\r", false)) },
        { "menu two levels", MacAutomation.MenuScript(MacAutomation.ItermBundleId, ["Edit", "Clear Buffer"]) },
        { "menu three levels", MacAutomation.MenuScript(MacAutomation.ItermBundleId, ["Shell", "Broadcast Input", "Broadcast Input to All Panes in Current Tab"]) },
        { "write line", ITermController.WriteScript("/compact", newline: true) },
        { "write control", ITermController.WriteScript(ITermController.ShiftTab, newline: false) },
        { "write mixed", ITermController.WriteScript("a\u0003b\"c", newline: false) },
        { "split", ITermController.SplitScript(vertical: true, "claude", "/Users/me/it's here") },
        { "paste submit", MacAutomation.PasteScript(MacAutomation.CodexBundleId, "Review \"this\"", submit: true) },
        { "command key", MacAutomation.CommandKeyScript(MacAutomation.ClaudeBundleId, 'b', shift: false) },
        { "command shift key", MacAutomation.CommandKeyScript(MacAutomation.ClaudeBundleId, 'd', shift: true) },
        { "iterm list", ITermController.ListScript },
        { "iterm focus by id", ITermController.FocusScript("31FFE014-86E0-460B-9424-62AF6C931680") },
        { "iterm new tab", ITermController.NewTabScript("/Users/me/it's here", "claude") },
        { "iterm new shell tab", ITermController.NewTabScript(null, null) },
        { "paste front submit", MacAutomation.PasteFrontScript("/review", submit: true) },
        { "paste front only", MacAutomation.PasteFrontScript("/cms-cultivator:pr-create ", submit: false) },
        { "paste only", MacAutomation.PasteScript(MacAutomation.ClaudeBundleId, "Explain", submit: false) },
    };

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Script_compiles(string name, string script)
    {
        if (!File.Exists(Compiler))
        {
            return; // osacompile is only on macOS.
        }

        // iTerm's terms compile only where its dictionary is installed; iTerm is optional.
        if (script.Contains(MacAutomation.ItermBundleId, StringComparison.Ordinal) && !Directory.Exists("/Applications/iTerm.app"))
        {
            return;
        }

        var (exitCode, _, error) = ProcessRunner.Run(Compiler, ["-e", script, "-o", this.output], TimeSpan.FromSeconds(20));

        Assert.True(exitCode == 0, $"{name}: {error}");
    }

    public static TheoryData<string, string> GhosttyScripts() => new()
    {
        { "ghostty focus", MacAutomation.GhosttyScript("/Users/me/it's \"here\"", "focus t") },
        { "ghostty escape", MacAutomation.GhosttyScript("/Users/me/site", MacAutomation.GhosttyKeys(["esc"])) },
        { "ghostty write", MacAutomation.GhosttyScript("/Users/me/site", MacAutomation.GhosttyWrite("/pr-review \"x\"", newline: false)) },
        { "ghostty write submit", MacAutomation.GhosttyScript("/Users/me/site", MacAutomation.GhosttyWrite("Line one\nline \\two", newline: true)) },
        { "ghostty enter only", MacAutomation.GhosttyScript("/Users/me/site", MacAutomation.GhosttyWrite("", newline: true)) },
        { "ghostty keys", MacAutomation.GhosttyScript("/Users/me/site", MacAutomation.GhosttyKeys(["down", "down", "enter", "1", "tab"])) },
    };

    // Ghostty's terms compile only where its dictionary is installed.
    [Theory]
    [MemberData(nameof(GhosttyScripts))]
    public void Ghostty_script_compiles(string name, string script)
    {
        if (!File.Exists(Compiler) || !Directory.Exists("/Applications/Ghostty.app"))
        {
            return;
        }

        var (exitCode, _, error) = ProcessRunner.Run(Compiler, ["-e", script, "-o", this.output], TimeSpan.FromSeconds(20));

        Assert.True(exitCode == 0, $"{name}: {error}");
    }

    [Fact]
    public void Press_button_script_compiles_as_javascript()
    {
        if (!File.Exists(Compiler))
        {
            return; // osacompile is only on macOS.
        }

        var (exitCode, _, error) = ProcessRunner.Run(Compiler, ["-l", "JavaScript", "-e", MacAutomation.PressButtonScript, "-o", this.output], TimeSpan.FromSeconds(20));

        Assert.True(exitCode == 0, error);
    }

    [Fact]
    public void Control_characters_become_character_ids()
    {
        var script = ITermController.WriteScript("a\u001b[Z", newline: false);

        Assert.Contains("\"a\" & (character id 27) & \"[Z\"", script);
        Assert.Contains("newline NO", script);
    }

    [Fact]
    public void Shell_quote_escapes_single_quotes()
    {
        Assert.Equal("'it'\\''s'", ITermController.ShellQuote("it's"));
    }
}
