using System.Text.RegularExpressions;

namespace Birchpad.Core.Agents;

/// <summary>One choice in a menu an agent is showing, in screen order.</summary>
public sealed record PromptOption(int Index, string Label);

/// <summary>
/// A menu an agent is waiting on, read from its screen: a permission prompt, a question, or a startup check.
/// <see cref="Cursor"/> is the option the ❯ marker is on; <see cref="Tabs"/> names the questions of a tabbed question set.
/// The screen is read only to put these labels on keys; nothing is stored or logged.
/// </summary>
public sealed partial record AgentPrompt(IReadOnlyList<PromptOption> Options, int Cursor, IReadOnlyList<string> Tabs)
{
    private const char Marker = '❯';

    /// <summary>
    /// Keys that pick <paramref name="option"/>: arrows from the cursor, then Enter.
    /// Arrows work for numbered and unnumbered menus alike, so the key never depends on a menu's shortcuts.
    /// </summary>
    public IReadOnlyList<string> KeysFor(PromptOption option)
    {
        var steps = option.Index - this.Cursor;
        return Enumerable.Repeat(steps > 0 ? "down" : "up", Math.Abs(steps)).Append("enter").ToList();
    }

    /// <summary>Finds the menu on an agent's screen, or null when there is none. Call it only while the agent waits.</summary>
    public static AgentPrompt? Parse(string? screen)
    {
        if (string.IsNullOrWhiteSpace(screen))
        {
            return null;
        }

        var lines = screen.Replace("\r", string.Empty).Split('\n');
        var options = Numbered(lines) ?? Unnumbered(lines);
        return options is { Options.Count: >= 2 } found ? new AgentPrompt(found.Options, found.Cursor, ReadTabs(lines)) : null;
    }

    // "❯ 1. Yes" and "  2. No": the last run numbered from 1, with description lines and separators allowed between options.
    private static (IReadOnlyList<PromptOption> Options, int Cursor)? Numbered(string[] lines)
    {
        var start = -1;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var m = NumberedLine().Match(lines[i]);
            if (m.Success && m.Groups["n"].Value == "1")
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var options = new List<PromptOption>();
        var cursor = 0;
        var sawCursor = false;
        for (var i = start; i < lines.Length; i++)
        {
            var m = NumberedLine().Match(lines[i]);
            if (!m.Success || m.Groups["n"].Value != (options.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                continue;
            }

            if (m.Groups["cursor"].Success)
            {
                cursor = options.Count;
                sawCursor = true;
            }

            options.Add(new PromptOption(options.Count, m.Groups["label"].Value.Trim()));
        }

        // A menu always shows the ❯ cursor on one option; a numbered list in the agent's reply never does.
        return sawCursor ? (options, cursor) : null;
    }

    // "❯ No, exit" with "  Yes, I trust this folder" below: the lines whose text starts in the same column as the marked one.
    private static (IReadOnlyList<PromptOption> Options, int Cursor)? Unnumbered(string[] lines)
    {
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var at = lines[i].IndexOf(Marker);
            if (at < 0 || lines[i][..at].Trim().Length > 0)
            {
                continue;
            }

            var column = TextColumn(lines[i], at + 1);
            if (column < 0)
            {
                continue;
            }

            var first = i;
            while (first > 0 && IsSibling(lines[first - 1], column))
            {
                first--;
            }

            var last = i;
            while (last < lines.Length - 1 && IsSibling(lines[last + 1], column))
            {
                last++;
            }

            if (last == first)
            {
                continue;
            }

            var options = new List<PromptOption>();
            for (var j = first; j <= last; j++)
            {
                options.Add(new PromptOption(options.Count, lines[j][column..].Trim()));
            }

            return (options, i - first);
        }

        return null;
    }

    // An option line: spaces up to the option column, then text, with no marker of its own.
    private static bool IsSibling(string line, int column) =>
        line.Length > column
        && line[..column].Trim().Length == 0
        && line[column] != ' '
        && line.IndexOf(Marker) < 0
        && !line.TrimStart().StartsWith('─');

    private static int TextColumn(string line, int from)
    {
        for (var i = from; i < line.Length; i++)
        {
            if (line[i] != ' ')
            {
                return i;
            }
        }

        return -1;
    }

    // "←  ☐ Language  ☐ Editor  ✔ Submit  →": the question names, without their check boxes.
    private static IReadOnlyList<string> ReadTabs(string[] lines)
    {
        var line = lines.LastOrDefault(l => l.TrimStart().StartsWith('←') && l.TrimEnd().EndsWith('→'));
        if (line == null)
        {
            return [];
        }

        return TabSplit().Split(line.Trim().Trim('←', '→'))
            .Select(t => t.Trim().TrimStart('☐', '☒', '✔', '✓', '■', '□').Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    [GeneratedRegex(@"^\s*(?<cursor>❯\s*)?(?<n>\d{1,2})\.\s+(?<label>\S.*)$")]
    private static partial Regex NumberedLine();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex TabSplit();
}
