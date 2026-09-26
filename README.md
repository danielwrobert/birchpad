# DevDeck: MX Keypad plugins for agent work

Four Logi Actions SDK plugins that put Claude Code, Codex, and iTerm on a Logitech MX Keypad.

- DevDeck Agent Hub (universal): live session tiles for Claude Code and Codex, permission answers, interrupt, routines, cloud sessions, and Codex usage.
- DevDeck Claude (Claude Desktop): chats, Code sessions, panels, saved prompts, and stop.
- DevDeck Codex (ChatGPT app): chats, panels, saved prompts, and stop.
- DevDeck iTerm (iTerm2): tabs, splits, broadcast, saved commands, and agent keys.

The plan is in `2026-09-26-logi-mx-keypad-plugins.md`.
Developer notes are in `docs/agent-hub.md`, `docs/app-plugins.md`, and `docs/iterm.md`.

## Requirements

- macOS with Logi Options+ and Logi Plugin Service.
- .NET 10 SDK (`brew install dotnet`) and LogiPluginTool (`dotnet tool install --global LogiPluginTool`).
- The `claude` and `codex` CLIs for cloud lists, thread names, and usage.
- iTerm shell integration, so keys can find a session's tab by folder.

## Build and run from source

```sh
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
export PATH=/opt/homebrew/opt/dotnet/bin:$PATH:$HOME/.dotnet/tools
dotnet test tests/DevDeck.Core.Tests
dotnet build DevDeck.slnx
```

A Debug build links each plugin into Logi Plugin Service and reloads it.
If a reload collides with the running copy, run `open loupedeck:plugin/<Name>/reload` (Name is AgentHub, ITerm, Codex, or Claude).

## Hooks

```sh
python3 hooks/claude_hooks.py install
python3 hooks/codex_hooks.py install
```

Then run `codex`, type `/hooks`, and trust the DevDeck entries; Codex skips them until then.
Both installers take `--port` (default 8766) and write it into the hook commands; use the same value as the Agent Hub `port` plugin setting.
Both scripts back up the file they change, and `remove` takes the DevDeck entries out again.
While Logitech's "Claude Desktop (Beta)" plugin is installed, leave off `--blocking-permissions` so the two plugins do not both hold permission requests.

## Package

```sh
tools/package.sh
logiplugintool install dist/AgentHub.lplug4
```

`tools/package.sh` runs the tests, builds each plugin in Release, and writes a verified `.lplug4` per plugin to `dist/`.

## Settings files

These live in `~/Library/Application Support/DevDeck/` and are created on first use:

- `prompts.json`: saved prompts for the Claude and Codex plugins.
- `iterm-commands.json`: saved commands for the iTerm plugin.
- `cloud-cache.json`: the last cloud helper result.

Keys update when `prompts.json` or `iterm-commands.json` is saved.

## Permissions

Logi Plugin Service needs Accessibility permission for menu clicks and keystrokes.
macOS asks for Automation permission the first time a key controls iTerm, Claude, or ChatGPT.
