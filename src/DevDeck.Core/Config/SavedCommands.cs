using System.Text.Json;

namespace DevDeck.Core.Config;

/// <summary>A named shell command for a keypad key.</summary>
public sealed record SavedCommand(string Name, string Command);

/// <summary>
/// Reads saved commands from a JSON array of <c>{"name", "command"}</c> objects.
/// A missing file is created with starter commands so there is something to edit.
/// </summary>
public static class SavedCommands
{
    public static readonly IReadOnlyList<SavedCommand> Starters =
    [
        new("ddev start", "ddev start"),
        new("drush cr", "ddev drush cr"),
        new("theme build", "ddev theme-build"),
        new("git status", "git status"),
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Loads the file, creating it with <see cref="Starters"/> when missing. Returns an error message for a bad file.</summary>
    public static IReadOnlyList<SavedCommand> LoadOrCreate(string file, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, JsonSerializer.Serialize(Starters, Options));
                return Starters;
            }

            var loaded = JsonSerializer.Deserialize<List<SavedCommand>>(File.ReadAllText(file), Options) ?? [];

            // Names become key labels and IDs, so blanks and repeats are dropped.
            return loaded
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Command))
                .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First() with { Name = g.Key })
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return [];
        }
    }
}
