# iTerm plugin developer notes

"Birchpad iTerm" is linked to iTerm2 (`com.googlecode.iterm2`), so its keys show when iTerm is in front.
It drives iTerm through AppleScript and clicks menu items by path where AppleScript has no verb.
Menu paths come from `docs/reference/menus-com.googlecode.iterm2.txt`.

## Build

```sh
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
export PATH=/opt/homebrew/opt/dotnet/bin:$PATH
dotnet build src/BirchpadITermPlugin
```

The plugin log is `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/ITerm.log`.

## Keys

Each key is a folder with Back in the top-left key.

- New: Shell tab, claude, codex, claude in a split, codex in a split, all in the current folder.
- Commands: one key per saved command.
- Tabs: every iTerm pane by folder, with the status ring of the agent running there; press to focus it.
  Agent status comes from Agent Hub's `/sessions` route.
- Agent keys: Esc, Mode (Shift+Tab), Ctrl-C, Clear, Compact, Model, Context, and Usage.
  Context and Usage send `/context` and `/usage` to Claude Code and `/status` to Codex, depending on which agent Agent Hub has in the current tab's folder.
- Settings: `/mcp`, `/plugins`, `/config`, `/hooks`, split right and down, broadcast on and off, and clear.

## Saved commands

The file is a JSON array of `{"name": "...", "command": "...", "submit": true}` objects.
With `"submit": false` the command is typed without pressing Return, so it can be finished by hand; keep its trailing space.
Entries with a blank name or command, or a repeated name, are skipped.
The folder reads the file each time it opens.

## Permissions

Menu clicks need Accessibility permission for Logi Plugin Service.
AppleScript control of iTerm needs Automation permission, which macOS asks for on the first key press.
Launch agent reads the current folder from iTerm shell integration.
