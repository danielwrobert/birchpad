# iTerm plugin developer notes

"DevDeck iTerm" is linked to iTerm2 (`com.googlecode.iterm2`), so its keys show when iTerm is in front.
It drives iTerm through AppleScript and clicks menu items by path where AppleScript has no verb.
Menu paths come from `docs/reference/menus-com.googlecode.iterm2.txt`.

## Build

```sh
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
export PATH=/opt/homebrew/opt/dotnet/bin:$PATH
dotnet build src/ITermPlugin
```

The plugin log is `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/ITerm.log`.

## Keys

- Pane: New tab, Split right, Split down, Clear, Broadcast tab, Broadcast off.
- Agent key: Esc, Mode (Shift+Tab), Ctrl-C, plus `/clear`, `/compact`, `/model`, `/remote-control` for Claude Code and `/new`, `/compact`, `/model`, `/status` for Codex.
- Launch agent: New claude, New codex; each opens a split in the current folder and starts the CLI.
- Saved command: one key per entry in `~/Library/Application Support/DevDeck/iterm-commands.json`.

Agent keys and saved commands type into the current session of the front window.

## Saved commands

The file is a JSON array of `{"name": "...", "command": "..."}` objects.
It is created with four starters on first load, and the keys update when the file is saved.
Entries with a blank name or command, or a repeated name, are skipped.

## Permissions

Menu clicks need Accessibility permission for Logi Plugin Service.
AppleScript control of iTerm needs Automation permission, which macOS asks for on the first key press.
Launch agent reads the current folder from iTerm shell integration.
