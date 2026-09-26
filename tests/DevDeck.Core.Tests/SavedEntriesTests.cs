using DevDeck.Core.Config;

namespace DevDeck.Core.Tests;

public sealed class SavedEntriesTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "devdeck-cmds-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.dir))
        {
            Directory.Delete(this.dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_is_created_with_starters()
    {
        var file = Path.Combine(this.dir, "commands.json");

        var commands = SavedEntries.LoadCommands(file, out var error);

        Assert.Null(error);
        Assert.Equal(SavedEntries.StarterCommands, commands);
        Assert.Equal(SavedEntries.StarterCommands, SavedEntries.LoadCommands(file, out _));
    }

    [Fact]
    public void Blank_and_repeated_names_are_dropped()
    {
        Directory.CreateDirectory(this.dir);
        var file = Path.Combine(this.dir, "commands.json");
        File.WriteAllText(file, """[{"name":"up","command":"ddev start"},null,{"name":"UP","command":"other"},{"name":"","command":"x"},{"name":"blank"}]""");

        var commands = SavedEntries.LoadCommands(file, out _);

        Assert.Equal([new SavedCommand("up", "ddev start")], commands);
    }

    [Fact]
    public void Prompts_use_the_same_rules()
    {
        Directory.CreateDirectory(this.dir);
        var file = Path.Combine(this.dir, "prompts.json");
        File.WriteAllText(file, """[{"name":"Explain","text":"Explain this."},{"name":"explain","text":"again"},{"name":"Empty","text":" "}]""");

        Assert.Equal([new SavedPrompt("Explain", "Explain this.")], SavedEntries.LoadPrompts(file, out _));
    }

    [Fact]
    public void Broken_file_reports_an_error()
    {
        Directory.CreateDirectory(this.dir);
        var file = Path.Combine(this.dir, "commands.json");
        File.WriteAllText(file, "[ broken");

        Assert.Empty(SavedEntries.LoadCommands(file, out var error));
        Assert.NotNull(error);
    }
}
