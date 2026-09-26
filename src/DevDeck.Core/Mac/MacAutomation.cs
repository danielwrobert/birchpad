using System.Diagnostics;

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
            var info = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                this.log?.Invoke($"{Path.GetFileName(file)} timed out");
                return (-1, string.Empty);
            }

            if (process.ExitCode != 0)
            {
                this.log?.Invoke($"{Path.GetFileName(file)} exited {process.ExitCode}: {error.Result.Trim()}");
            }

            return (process.ExitCode, output.Result);
        }
        catch (Exception ex)
        {
            this.log?.Invoke($"{Path.GetFileName(file)} failed: {ex.Message}");
            return (-1, string.Empty);
        }
    }
}
