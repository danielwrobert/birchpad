namespace Birchpad.Core.Mac;

/// <summary>One iTerm pane.</summary>
public sealed record ITermSession(string Id, string Path, string Name, string Tty = "")
{
    public string FolderName => System.IO.Path.GetFileName(this.Path.TrimEnd('/')) is { Length: > 0 } name ? name : this.Name;
}

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

    /// <summary>The terminal name (such as <c>ttys001</c>) of the current session.</summary>
    public string CurrentTty() => TerminalAgents.TtyName(this.mac.AppleScript($$"""
        tell application id "{{MacAutomation.ItermBundleId}}"
          return tty of current session of current window
        end tell
        """));

    /// <summary>The agent CLI running in the current session.</summary>
    public AgentKind CurrentAgent() =>
        TerminalAgents.Detect().TryGetValue(this.CurrentTty(), out var kind) ? kind : AgentKind.None;

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

    /// <summary>Every iTerm session, one per pane, with its working folder from shell integration.</summary>
    public IReadOnlyList<ITermSession> ListSessions()
    {
        var output = this.mac.AppleScript(ListScript);
        return output is null ? [] : ParseSessions(output);
    }

    /// <summary>Selects the session with this iTerm session ID and brings its window forward.</summary>
    public bool Focus(string sessionId) => this.mac.AppleScript(FocusScript(sessionId)) == "found";

    /// <summary>Opens a new tab and runs <paramref name="command"/> in <paramref name="cwd"/>, or just opens a shell there.</summary>
    public bool NewTabAt(string? cwd, string? command) => this.mac.AppleScript(NewTabScript(cwd, command)) is not null;

    internal const string ListScript = """
        set out to ""
        set sep to character id 9
        tell application id "com.googlecode.iterm2"
          repeat with w in windows
            repeat with t in tabs of w
              repeat with s in sessions of t
                set p to ""
                try
                  set p to (variable named "session.path") of s
                end try
                set out to out & (id of s) & sep & p & sep & (name of s) & sep & (tty of s) & linefeed
              end repeat
            end repeat
          end repeat
        end tell
        return out
        """;

    internal static IReadOnlyList<ITermSession> ParseSessions(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length >= 3 && parts[0].Length > 0)
            .Select(parts => new ITermSession(parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts.Length > 3 ? TerminalAgents.TtyName(parts[3].Trim()) : string.Empty))
            .ToList();

    internal static string FocusScript(string sessionId) => $$"""
        tell application id "{{MacAutomation.ItermBundleId}}"
          repeat with w in windows
            repeat with t in tabs of w
              repeat with s in sessions of t
                if (id of s) is {{MacAutomation.Quote(sessionId)}} then
                  select w
                  tell t to select
                  tell s to select
                  activate
                  return "found"
                end if
              end repeat
            end repeat
          end repeat
        end tell
        return "missing"
        """;

    internal static string NewTabScript(string? cwd, string? command)
    {
        var line = (cwd, command) switch
        {
            (null, null) => null,
            (null, _) => command,
            (_, null) => $"cd {ShellQuote(cwd)}",
            _ => $"cd {ShellQuote(cwd)} && {command}",
        };
        var write = line is null ? string.Empty : $"tell current session of current window to write text {MacAutomation.Quote(line)}";
        return $$"""
            tell application id "{{MacAutomation.ItermBundleId}}"
              if (count of windows) is 0 then
                create window with default profile
              else
                tell current window to create tab with default profile
              end if
              {{write}}
              activate
            end tell
            """;
    }

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
