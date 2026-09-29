using Birchpad.Core.Agents;

namespace Birchpad.Core.Tests;

// Screens captured from Claude Code 2.1 in a herdr pane.
public sealed class AgentPromptTests
{
    private const string Trust = """
         Accessing workspace:
         /private/tmp/bp-prompts
         Claude Code'll be able to read, edit, and execute files
         here.
         Security guide
         ❯ No, exit
           Yes, I trust this folder
         Enter to confirm · Esc to cancel
        """;

    private const string Questions = """
        ❯ Use the AskUserQuestion tool to ask me two short multiple
          choice questions, each with three options, in one call.
        ────────────────────────────────────────────────────────────
        ←  ☐ Language  ☐ Editor  ✔ Submit  →
        Which language do you prefer for scripts?
          1. Python
             Readable, large library set.
        ❯ 2. JavaScript
             Runs with Node, good for web tooling.
          3. Bash
             Best for short shell tasks.
          4. Type something.
        ────────────────────────────────────────────────────────────
          5. Chat about this
        Enter to select · Tab/Arrow keys to navigate · Esc to cancel
        """;

    private const string Permission = """
        ⏺ Earlier I listed 1. first and 2. second.
         Bash command
           curl -sI https://example.com
         Do you want to proceed?
         ❯ 1. Yes
           2. Yes, and don't ask again for curl commands in /Users/me/site
           3. No, and tell Claude what to do differently (esc)
        """;

    [Fact]
    public void ReadsAnUnnumberedMenu()
    {
        var prompt = AgentPrompt.Parse(Trust)!;

        Assert.Equal(["No, exit", "Yes, I trust this folder"], prompt.Options.Select(o => o.Label));
        Assert.Equal(0, prompt.Cursor);
        Assert.Empty(prompt.Tabs);
        Assert.Equal(["down", "enter"], prompt.KeysFor(prompt.Options[1]));
    }

    [Fact]
    public void ReadsATabbedQuestionWithTheCursorOnTheSecondOption()
    {
        var prompt = AgentPrompt.Parse(Questions)!;

        Assert.Equal(["Python", "JavaScript", "Bash", "Type something.", "Chat about this"], prompt.Options.Select(o => o.Label));
        Assert.Equal(1, prompt.Cursor);
        Assert.Equal(["Language", "Editor", "Submit"], prompt.Tabs);
        Assert.Equal(["up", "enter"], prompt.KeysFor(prompt.Options[0]));
        Assert.Equal(["enter"], prompt.KeysFor(prompt.Options[1]));
        Assert.Equal(["down", "down", "down", "enter"], prompt.KeysFor(prompt.Options[4]));
    }

    [Fact]
    public void ReadsAPermissionPromptAndIgnoresEarlierNumbers()
    {
        var prompt = AgentPrompt.Parse(Permission)!;

        Assert.Equal(3, prompt.Options.Count);
        Assert.Equal("Yes", prompt.Options[0].Label);
        Assert.StartsWith("No, and tell Claude", prompt.Options[2].Label);
    }

    [Fact]
    public void FindsNoMenuOnAnIdleScreen()
    {
        const string idle = """
            ────────────────────────────────────────────────────────────
            ❯ Try "create a util logging.py that..."
            ────────────────────────────────────────────────────────────
              ⏸ manual mode on
            """;

        Assert.Null(AgentPrompt.Parse(idle));
        Assert.Null(AgentPrompt.Parse(null));
    }
}
