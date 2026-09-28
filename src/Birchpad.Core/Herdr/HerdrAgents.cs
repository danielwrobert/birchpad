using Birchpad.Core.Mac;

namespace Birchpad.Core.Herdr;

/// <summary>
/// The agent kinds a New page offers: those herdr can detect whose executable is on your shell's PATH.
/// herdr names each kind after its executable, so nothing here lists agents by name.
/// </summary>
public sealed class HerdrAgents(HerdrClient client)
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(5);

    private readonly object gate = new();
    private IReadOnlyList<string> cached = [];
    private DateTimeOffset cachedAt;

    public IReadOnlyList<string> Installed()
    {
        lock (this.gate)
        {
            if (this.cached.Count > 0 && DateTimeOffset.UtcNow - this.cachedAt < KeepFor)
            {
                return this.cached;
            }
        }

        IReadOnlyList<string> kinds;
        try
        {
            kinds = client.AgentKinds();
        }
        catch (HerdrException)
        {
            return [];
        }

        var found = OnPath(kinds);
        lock (this.gate)
        {
            this.cached = found;
            this.cachedAt = DateTimeOffset.UtcNow;
        }

        return found;
    }

    // The plugin service has a bare PATH, so ask a login shell, which reads the same profile a herdr pane does.
    private static IReadOnlyList<string> OnPath(IReadOnlyList<string> kinds)
    {
        var safe = kinds.Where(k => k.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')).ToList();
        if (safe.Count == 0)
        {
            return [];
        }

        var shell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } s ? s : "/bin/zsh";
        var result = ProcessRunner.Run(shell, ["-lc", "command -v " + string.Join(' ', safe) + " 2>/dev/null; true"], TimeSpan.FromSeconds(8));
        return FromCommandV(safe, result.Output);
    }

    /// <summary>Keeps the kinds whose executable <c>command -v</c> printed as a path, in herdr's order.</summary>
    internal static IReadOnlyList<string> FromCommandV(IReadOnlyList<string> kinds, string output)
    {
        var names = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('/'))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);
        return kinds.Where(names.Contains).ToList();
    }
}
