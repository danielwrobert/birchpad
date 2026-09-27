namespace Birchpad.Core.Mac;

/// <summary>Which agent CLI runs in a terminal, if any.</summary>
public enum AgentKind
{
    None,
    Claude,
    Codex,
}

/// <summary>
/// Finds the agent CLI running on each terminal from the process list, so a pane is identified by what runs in it,
/// not by its folder: two panes in one folder, one running claude and one a plain shell, stay apart.
/// </summary>
public static class TerminalAgents
{
    /// <summary>Maps a terminal name such as <c>ttys001</c> to the agent running on it.</summary>
    public static IReadOnlyDictionary<string, AgentKind> Detect()
    {
        try
        {
            var (exitCode, output, _) = ProcessRunner.Run("/bin/ps", ["-A", "-o", "tty=,comm="], TimeSpan.FromSeconds(5));
            return exitCode == 0 ? Parse(output) : new Dictionary<string, AgentKind>();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return new Dictionary<string, AgentKind>();
        }
    }

    internal static IReadOnlyDictionary<string, AgentKind> Parse(string psOutput)
    {
        var map = new Dictionary<string, AgentKind>(StringComparer.Ordinal);
        foreach (var line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var split = trimmed.IndexOf(' ');
            if (split <= 0)
            {
                continue;
            }

            var tty = trimmed[..split];
            var command = Path.GetFileName(trimmed[(split + 1)..].Trim());
            var kind = command switch
            {
                "claude" => AgentKind.Claude,
                "codex" => AgentKind.Codex,
                _ => AgentKind.None,
            };

            // Claude wins a tie: a claude session can run codex as a tool, not the other way around.
            if (kind != AgentKind.None && (!map.TryGetValue(tty, out var existing) || existing == AgentKind.Codex))
            {
                map[tty] = kind;
            }
        }

        return map;
    }

    /// <summary>The terminal name from an iTerm tty path such as <c>/dev/ttys001</c>.</summary>
    public static string TtyName(string? ttyPath) =>
        string.IsNullOrEmpty(ttyPath) ? string.Empty : Path.GetFileName(ttyPath);
}
