using Birchpad.Core.Mac;

namespace Birchpad.Core.Herdr;

/// <summary>
/// Finds the terminal app showing herdr, so focusing a pane also brings that window forward.
/// herdr can run in any terminal, so the app is found from the process tree, not named in code.
/// </summary>
public static class HerdrTerminal
{
    private static readonly object Gate = new();
    private static string? cached;
    private static DateTimeOffset cachedAt;

    /// <summary>The <c>.app</c> path of the terminal running the newest herdr client, or null when none is found.</summary>
    public static string? HostApp()
    {
        lock (Gate)
        {
            if (DateTimeOffset.UtcNow - cachedAt < TimeSpan.FromSeconds(30))
            {
                return cached;
            }
        }

        var result = ProcessRunner.Run("/bin/ps", ["-axo", "pid=,ppid=,comm="], TimeSpan.FromSeconds(3));
        var app = result.ExitCode == 0 ? FindHostApp(result.Output) : null;
        lock (Gate)
        {
            cached = app;
            cachedAt = DateTimeOffset.UtcNow;
        }

        return app;
    }

    /// <summary>Brings the terminal showing herdr to the front; does nothing when none is found.</summary>
    public static bool Activate() =>
        HostApp() is { } app && ProcessRunner.Run("/usr/bin/open", ["-a", app], TimeSpan.FromSeconds(5)).ExitCode == 0;

    /// <summary>
    /// Walks up from each herdr process, newest first, to the first ancestor inside an app bundle.
    /// The server has no app ancestor, so only a client in a terminal window matches.
    /// </summary>
    internal static string? FindHostApp(string psOutput)
    {
        var parents = new Dictionary<int, (int Parent, string Command)>();
        foreach (var line in psOutput.Split('\n'))
        {
            var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && int.TryParse(parts[0], out var pid) && int.TryParse(parts[1], out var ppid))
            {
                parents[pid] = (ppid, parts[2].Trim());
            }
        }

        foreach (var pid in parents.Where(p => Path.GetFileName(p.Value.Command) == "herdr").Select(p => p.Key).OrderByDescending(p => p))
        {
            var current = parents[pid].Parent;
            for (var depth = 0; depth < 32 && parents.TryGetValue(current, out var entry); depth++)
            {
                var at = entry.Command.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
                if (at > 0)
                {
                    return entry.Command[..(at + 4)];
                }

                current = entry.Parent;
            }
        }

        return null;
    }
}
