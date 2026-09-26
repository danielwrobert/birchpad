namespace DevDeck.Core.Mac;

/// <summary>
/// macOS automation through <c>open</c> and <c>osascript</c>.
/// Keystrokes and menu clicks need Accessibility permission for the calling process.
/// </summary>
public sealed class MacAutomation
{
    private readonly Action<string>? log;

    public MacAutomation(Action<string>? log = null)
    {
        this.log = log;
    }

    public const string ClaudeBundleId = "com.anthropic.claudefordesktop";

    public const string ItermBundleId = "com.googlecode.iterm2";

    /// <summary>Opens a URL or custom-scheme link such as <c>claude://</c>.</summary>
    public bool OpenUrl(string url) => this.Run("/usr/bin/open", [url], TimeSpan.FromSeconds(5)).ExitCode == 0;

    /// <summary>Brings an app to the front by bundle ID.</summary>
    public bool Activate(string bundleId) => this.Run("/usr/bin/open", ["-b", bundleId], TimeSpan.FromSeconds(5)).ExitCode == 0;

    /// <summary>Runs AppleScript and returns its trimmed output, or null on failure.</summary>
    public string? AppleScript(string script, TimeSpan? timeout = null)
    {
        var result = this.Run("/usr/bin/osascript", ["-e", script], timeout ?? TimeSpan.FromSeconds(10));
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    /// <summary>
    /// Clicks a menu item by path, for example <c>"Edit", "Clear Buffer"</c> or
    /// <c>"Shell", "Broadcast Input", "Broadcast Input to All Panes in Current Tab"</c>.
    /// Works for items with no shortcut; the app does not need to be in front.
    /// </summary>
    public bool ClickMenu(string bundleId, params string[] path) =>
        path.Length >= 2 && this.AppleScript(MenuScript(bundleId, path)) is not null;

    internal static string MenuScript(string bundleId, string[] path)
    {
        // Build "menu item X of menu 1 of menu item Y of menu Z of menu bar item Z of menu bar 1" from the leaf up.
        var reference = $"menu bar item {Quote(path[0])} of menu bar 1";
        reference = $"menu {Quote(path[0])} of {reference}";
        for (var i = 1; i < path.Length - 1; i++)
        {
            reference = $"menu 1 of menu item {Quote(path[i])} of {reference}";
        }

        reference = $"menu item {Quote(path[^1])} of {reference}";
        return $$"""
            tell application "System Events"
              tell (first process whose bundle identifier is {{Quote(bundleId)}})
                click {{reference}}
              end tell
            end tell
            """;
    }

    /// <summary>Opens a Claude Desktop Code session by its Claude Code session ID.</summary>
    public bool OpenClaudeDesktopSession(string sessionId) =>
        this.OpenUrl($"claude://resume?session={Uri.EscapeDataString(sessionId)}");

    /// <summary>Sends Escape to the front app. The caller brings the right app forward first.</summary>
    public bool SendEscape() =>
        this.AppleScript("tell application \"System Events\" to key code 53") is not null;

    /// <summary>
    /// Selects the iTerm session whose working directory is <paramref name="path"/> and brings it forward.
    /// Needs iTerm shell integration for <c>session.path</c>. Returns false when no session matches.
    /// </summary>
    public bool FocusItermSessionAtPath(string path) =>
        this.AppleScript(ItermScript(path, sendEscape: false)) == "found";

    /// <summary>Sends Escape to the iTerm session whose working directory is <paramref name="path"/>.</summary>
    public bool SendEscapeToItermSessionAtPath(string path) =>
        this.AppleScript(ItermScript(path, sendEscape: true)) == "found";

    internal static string ItermScript(string path, bool sendEscape)
    {
        var target = Quote(path.TrimEnd('/'));
        var action = sendEscape
            ? "tell s to write text (character id 27) newline NO"
            : "select w\n            tell t to select\n            tell s to select\n            activate";
        return $$"""
            tell application id "{{ItermBundleId}}"
              repeat with w in windows
                repeat with t in tabs of w
                  repeat with s in sessions of t
                    set p to ""
                    try
                      set p to (variable named "session.path") of s
                    end try
                    if p is {{target}} or p is ({{target}} & "/") then
                      {{action}}
                      return "found"
                    end if
                  end repeat
                end repeat
              end repeat
            end tell
            return "missing"
            """;
    }

    /// <summary>Quotes a string as an AppleScript string literal.</summary>
    internal static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private (int ExitCode, string Output) Run(string file, string[] args, TimeSpan timeout)
    {
        try
        {
            var (exitCode, output, error) = ProcessRunner.Run(file, args, timeout);
            if (exitCode != 0)
            {
                this.log?.Invoke($"{Path.GetFileName(file)} exited {exitCode}: {error.Trim()}");
            }

            return (exitCode, output);
        }
        catch (Exception ex)
        {
            this.log?.Invoke($"{Path.GetFileName(file)} failed: {ex.Message}");
            return (-1, string.Empty);
        }
    }
}
