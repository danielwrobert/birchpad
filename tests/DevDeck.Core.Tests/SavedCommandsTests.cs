using DevDeck.Core.Config;

namespace DevDeck.Core.Tests;

public sealed class SavedCommandsTests : IDisposable
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

        var commands = SavedCommands.LoadOrCreate(file, out var error);

        Assert.Null(error);
        Assert.Equal(SavedCommands.Starters, commands);
        Assert.Equal(SavedCommands.Starters, SavedCommands.LoadOrCreate(file, out _));
    }

    [Fact]
    public void Blank_and_repeated_names_are_dropped()
    {
        Directory.CreateDirectory(this.dir);
        var file = Path.Combine(this.dir, "commands.json");
        File.WriteAllText(file, """[{"name":"up","command":"ddev start"},{"name":"UP","command":"other"},{"name":"","command":"x"},{"name":"blank"}]""");

        var commands = SavedCommands.LoadOrCreate(file, out _);

        Assert.Equal([new SavedCommand("up", "ddev start")], commands);
    }

    [Fact]
    public void Broken_file_reports_an_error()
    {
        Directory.CreateDirectory(this.dir);
        var file = Path.Combine(this.dir, "commands.json");
        File.WriteAllText(file, "[ broken");

        Assert.Empty(SavedCommands.LoadOrCreate(file, out var error));
        Assert.NotNull(error);
    }
}
