# Ghostty support for agents run outside herdr

## Context

Agent Hub already tracks a `claude` or `codex` started in a plain Ghostty tab, so its status shows.
Its keys can't act on that tab, because every terminal action goes to iTerm through AppleScript by matching the session's folder:

- Focus: `MacAutomation.FocusItermSessionAtPath`
- Escape (Stop): `MacAutomation.SendEscapeToItermSessionAtPath`
- Typing (slash commands, prompts, answers, menu keys): `MacAutomation.WriteToItermSessionAtPath`

Ghostty 1.3 (installed: 1.3.1) ships an AppleScript dictionary (`Ghostty.app/Contents/Resources/Ghostty.sdef`, on by default through the `macos-applescript` setting).
It covers everything the iTerm path uses:

- `terminals` (app, window, and tab elements), each with `working directory`
- `focus <terminal>`, which brings its window to the front
- `input text "..." to <terminal>`, which inserts text as a paste
- `send key "<name>" [modifiers "shift,control"] to <terminal>`

The change is to add a Ghostty route next to the iTerm route, by folder, so a CLI agent in a Ghostty tab can be focused, stopped, typed into, and answered from the keypad the same way an iTerm tab is.
No new host kind, profile, or action is needed, so existing keypad profiles are unaffected.

## Approach

### 1. `src/Birchpad.Core/Mac/MacAutomation.cs`

- Add `public const string GhosttyBundleId = "com.mitchellh.ghostty";`.
- Add `internal static string GhosttyScript(string path, string action)`, modeled on `ItermScript`:
  - Start with `if application id "<Ghostty>" is not running then return "missing"`, so Ghostty is never launched by a key press.
  - Repeat over `terminals`, read `working directory` inside `try`, and compare it with the path with or without a trailing `/`, reusing `Quote`.
  - Run the action with `t` bound to the matching terminal, then return `"found"`.
- Actions:
  - Focus: `focus t`.
  - Escape: `send key "escape" to t`.
  - Write: `input text <Quote(text)> to t`, then `send key "enter" to t` when submitting.
  - Keys: one `send key` per key name, from a map of herdr's names (`up`, `down`, `left`, `right`, `enter`, `esc`, `tab`, digits) to Ghostty key names.
    Ghostty's `input text` is a paste, so arrows and Enter must be sent as key events, not as escape bytes as iTerm gets them.
- Add the same `is not running` guard to `ItermScript`.
  On a Mac without iTerm running, that skips a failed or app-launching `tell` on every press.
- Add terminal-neutral entry points that try iTerm, then Ghostty:
  - `FocusTerminalAtPath(path)`
  - `SendEscapeToTerminalAtPath(path)`
  - `WriteToTerminalAtPath(path, text, newline)`
  - `PressKeysInTerminalAtPath(path, keys)`: iTerm gets the escape bytes `AgentActions.TerminalBytes` builds today, which move here; Ghostty gets `send key` calls.
- Keep the existing iTerm methods; the iTerm plugin still uses its own `ITermController`.

### 2. Call sites (Agent Hub plugin)

- `src/BirchpadAgentHubPlugin/Services/SessionFocus.cs`: the `ClaudeCli` and `Codex` cases call the `...TerminalAtPath` methods.
  Update the comment that says only an iTerm tab can be raised.
- `src/BirchpadAgentHubPlugin/Services/AgentActions.cs`:
  - `PressKeys` calls `PressKeysInTerminalAtPath` and drops its private `TerminalBytes`.
  - `Send` (the `Terminal` and `Codex` cases) calls `WriteToTerminalAtPath`.
  - `CanPressKeys` and `CanReadScreen` stay as they are.
    A Ghostty tab gets number keys and Esc like an iTerm tab, but no menu read off the screen; that stays herdr only.

### 3. Comments and docs

- `src/Birchpad.Core/Agents/CodingAgents.cs`: `AgentHost.Terminal` becomes "a CLI in an iTerm or Ghostty tab, found by its folder".
- `README.md` Requirements: iTerm shell integration, or Ghostty 1.3 or later with its shell integration (on by default), for keys that find a CLI's tab.
- `docs/agent-hub.md`: the "CLI tiles find their iTerm tab" paragraph names both terminals.
- `SETUP.md`: replace the "outside herdr" limitation with what works and the one limit that remains (next section).

## Known limit

Tabs are matched by folder, as with iTerm.
With two Ghostty tabs in the same folder, say a shell and `claude`, the first one found wins.
iTerm's own plugin tells them apart by tty, but Ghostty's dictionary doesn't expose one.
In practice: start a second agent in another folder, or run it in herdr.

## Tests

- `tests/Birchpad.Core.Tests/AppleScriptCompileTests.cs`: add focus, escape, write, write-and-submit, and key Ghostty scripts, including a path with quotes.
  `osacompile` needs Ghostty's dictionary to compile its terms, so the Ghostty cases return early when `/Applications/Ghostty.app` is missing, as the file already does when `osacompile` is missing.
- `tests/Birchpad.Core.Tests/MacAutomationTests.cs`:
  - The Ghostty script matches the path with and without a trailing slash and starts with the `is not running` guard.
  - Key names map as expected.
- Run: `dotnet test tests/Birchpad.Core.Tests`.

## Verification on the Mac

1. Before coding, confirm Ghostty's key names with a short manual `osascript` against a scratch Ghostty tab.
   The candidates are `"escape"` and `"enter"` (named in the docs) and `"arrow_down"` or `"down"`, `"tab"`, and `"1"`.
   The key map uses whatever works.
2. Build and install with `tools/deploy.sh BirchpadAgentHub`.
   This needs the .NET SDK, so it is the first build step on this Mac.
3. In a plain Ghostty tab (no herdr), run `claude` in a project folder, then from the keypad:
   - Press its tile: the Ghostty window and tab come forward.
   - Send a slash command from its agent page: it appears and submits.
   - Trigger a permission prompt and answer with 1, 2, or Esc.
   - Press Stop: Escape interrupts it.
4. Repeat steps 3's checks with `codex` in a Ghostty tab.
5. Confirm a herdr pane still routes through herdr; it matches as a herdr agent before the terminal route is tried.
6. Check `plugin_logs/` for `osascript exited` lines.
   macOS asks once for Automation permission to control Ghostty; allow it.

## Delivery

This changes plugin code, so using it means building Agent Hub once (`tools/deploy.sh`), or the maintainer merging it and sharing new packages.
Ask before committing; it fits as an upstream pull request with a `feat(agent-hub): reach CLI agents in Ghostty tabs` commit.
