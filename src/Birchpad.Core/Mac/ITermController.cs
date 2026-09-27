namespace Birchpad.Core.Mac;

/// <summary>Drives iTerm2 through its AppleScript dictionary and, where AppleScript has no verb, its menus.</summary>
public sealed class ITermController
{
    /// <summary>Escape, which interrupts Claude Code and Codex.</summary>
    public const string Escape = "\u001b";

    /// <summary>Shift+Tab as a terminal sends it; cycles Claude Code's permission mode.</summary>
    public const string ShiftTab = "\u001b[Z";

    /// <summary>Control-C.</summary>
    public const string ControlC = "\u0003";

    private readonly MacAutomation mac;

    public ITermController(MacAutomation mac)
    {
        this.mac = mac;
    }

    /// <summary>Types a line into the current session and presses Return.</summary>
    public bool RunLine(string text) => this.mac.AppleScript(WriteScript(text, newline: true)) is not null;

    /// <summary>Sends raw characters, such as a control sequence, without pressing Return.</summary>
    public bool SendRaw(string text) => this.mac.AppleScript(WriteScript(text, newline: false)) is not null;

    /// <summary>The working directory of the current session, from shell integration.</summary>
    public string? CurrentPath()
    {
        var path = this.mac.AppleScript($$"""
            tell application id "{{MacAutomation.ItermBundleId}}"
              return (variable named "session.path") of current session of current window
            end tell
            """);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>Opens a new tab with the named profile, or the default profile when null.</summary>
    public bool NewTab(string? profile = null) => this.mac.AppleScript($$"""
        tell application id "{{MacAutomation.ItermBundleId}}"
          if (count of windows) is 0 then
            {{(profile is null ? "create window with default profile" : $"create window with profile {MacAutomation.Quote(profile)}")}}
          else
            tell current window to {{(profile is null ? "create tab with default profile" : $"create tab with profile {MacAutomation.Quote(profile)}")}}
          end if
          activate
        end tell
        """) is not null;

    /// <summary>Splits the current pane and runs <paramref name="command"/> in the new pane, in <paramref name="cwd"/> when given.</summary>
    public bool SplitAndRun(bool vertical, string command, string? cwd) => this.mac.AppleScript(SplitScript(vertical, command, cwd)) is not null;

    public bool ClickMenu(params string[] path) => this.mac.ClickMenu(MacAutomation.ItermBundleId, path);

    internal static string WriteScript(string text, bool newline)
    {
        // AppleScript string literals cannot hold control characters, so build them with "character id".
        var parts = new List<string>();
        var run = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (ch < ' ')
            {
                if (run.Length > 0)
                {
                    parts.Add(MacAutomation.Quote(run.ToString()));
                    run.Clear();
                }

                parts.Add($"(character id {(int)ch})");
            }
            else
            {
                run.Append(ch);
            }
        }

        if (run.Length > 0 || parts.Count == 0)
        {
            parts.Add(MacAutomation.Quote(run.ToString()));
        }

        return $$"""
            tell application id "{{MacAutomation.ItermBundleId}}"
              tell current session of current window to write text ({{string.Join(" & ", parts)}}) newline {{(newline ? "YES" : "NO")}}
            end tell
            """;
    }

    internal static string SplitScript(bool vertical, string command, string? cwd)
    {
        var line = cwd is null ? command : $"cd {ShellQuote(cwd)} && {command}";
        return $$"""
            tell application id "{{MacAutomation.ItermBundleId}}"
              tell current session of current window
                set newSession to (split {{(vertical ? "vertically" : "horizontally")}} with default profile)
              end tell
              tell newSession to write text {{MacAutomation.Quote(line)}}
              activate
            end tell
            """;
    }

    /// <summary>Single-quotes a string for a POSIX shell.</summary>
    internal static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
