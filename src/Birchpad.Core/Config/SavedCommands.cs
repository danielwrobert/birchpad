using System.Text.Json;

namespace Birchpad.Core.Config;

/// <summary>
/// A named shell command for a keypad key. With <paramref name="Submit"/> false the command is typed
/// without pressing Return, so it can be finished by hand.
/// </summary>
public sealed record SavedCommand(string Name, string Command, bool Submit = true);

/// <summary>A named prompt that a key pastes into an agent app.</summary>
public sealed record SavedPrompt(string Name, string Text);

/// <summary>
/// Reads named key entries from a JSON array. A missing file is created with starters
/// so there is something to edit. Blank entries and repeated names are dropped.
/// </summary>
public static class SavedEntries
{
    public static readonly IReadOnlyList<SavedCommand> StarterCommands =
    [
        new("git status", "git status"),
        new("git pull", "git pull"),
        new("git checkout main", "git checkout main"),
        new("ddev init", "ddev init"),
        new("ddev refresh", "ddev refresh"),
        new("ddev drush", "ddev drush ", Submit: false),
        new("ddev terminus", "ddev terminus ", Submit: false),
        new("ddev wp", "ddev wp ", Submit: false),
    ];

    /// <summary>
    /// Starter slash commands shared by every app. They are typed without Return where a command usually takes
    /// arguments; edit the file to add your team's commands.
    /// </summary>
    public static readonly IReadOnlyList<SavedCommand> StarterSlashCommands =
    [
        new("/clear", "/clear"),
        new("/compact", "/compact"),
        new("/model", "/model"),
        new("/review", "/review"),
        new("/init", "/init"),
        new("/mcp", "/mcp"),
    ];

    public static readonly IReadOnlyList<SavedPrompt> StarterPrompts =
    [
        new("Explain", "Explain what this code does, briefly."),
        new("Review", "Review my changes and point out anything wrong."),
        new("Tests", "Write tests for the code I just changed."),
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static IReadOnlyList<SavedCommand> LoadCommands(string file, out string? error) =>
        LoadOrCreate(file, StarterCommands, c => c.Name, c => c.Command, (c, name) => c with { Name = name }, out error);

    public static IReadOnlyList<SavedCommand> LoadSlashCommands(string file, out string? error) =>
        LoadOrCreate(file, StarterSlashCommands, c => c.Name, c => c.Command, (c, name) => c with { Name = name }, out error);

    public static IReadOnlyList<SavedPrompt> LoadPrompts(string file, out string? error) =>
        LoadOrCreate(file, StarterPrompts, p => p.Name, p => p.Text, (p, name) => p with { Name = name }, out error);

    private static IReadOnlyList<T> LoadOrCreate<T>(
        string file,
        IReadOnlyList<T> starters,
        Func<T, string?> name,
        Func<T, string?> value,
        Func<T, string, T> rename,
        out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, JsonSerializer.Serialize(starters, Options));
                return starters;
            }

            var loaded = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(file), Options) ?? [];

            // Names become key labels and IDs, so blanks and repeats are dropped.
            return loaded
                .Where(e => e is not null && !string.IsNullOrWhiteSpace(name(e)) && !string.IsNullOrWhiteSpace(value(e)))
                .GroupBy(e => name(e)!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => rename(g.First(), g.Key))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return [];
        }
    }
}
