# Birchpad: MX Keypad plugins for agent work

Five Logi Actions SDK plugins that put Claude Code, Codex, iTerm, and herdr on a Logitech MX Keypad.

- Birchpad Agent Hub (universal): Sessions with status rings and per-session approvals, Scheduled, Usage, and Next waiting.
- Birchpad Claude (Claude Desktop): New, Prompts, Chats, Projects, and Settings.
- Birchpad Codex (ChatGPT app): New, Prompts, Chats, Projects, Scheduled, and Settings.
- Birchpad iTerm (iTerm2): New, Commands, Tabs, Agent keys, and Settings.
- Birchpad Herdr (universal): herdr agents, workspaces, worktrees, splits, and agent keys, from the Herdr key on the iTerm home page.

`profiles/generated/Birchpad Agents.lp5` is a General profile that works in any app: coding sessions, chats and tasks, and each agent's approvals and commands; see `2026-09-28-general-workflow-profiles.md`.
Each app opens on a home page of categories; every category opens pages listed at run time, with Back in the top-left key.
The design is in `docs/workflow-design.md`, and each app plugin ships its home page as a default profile.

To install and use the plugins with Ghostty and herdr, without iTerm2 or a development setup, follow `SETUP.md`.

The plan is in `2026-09-26-logi-mx-keypad-plugins.md`.
Developer notes are in `docs/agent-hub.md`, `docs/app-plugins.md`, `docs/iterm.md`, and `docs/herdr.md`.

## Requirements

- macOS with Logi Options+ and Logi Plugin Service.
- .NET 10 SDK (`brew install dotnet`) and LogiPluginTool (`dotnet tool install --global LogiPluginTool`).
- The `claude` and `codex` CLIs for cloud lists, thread names, and usage.
- iTerm shell integration, or Ghostty 1.3 or later (its shell integration is on by default), so keys can find a CLI session's tab by folder.
- herdr 0.9 or later for the Herdr plugin.

## Build and run from source

```sh
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
export PATH=/opt/homebrew/opt/dotnet/bin:$PATH:$HOME/.dotnet/tools
dotnet test tests/Birchpad.Core.Tests
dotnet build Birchpad.slnx
```

A Debug build links each plugin into Logi Plugin Service and reloads it.
If a reload collides with the running copy, run `open loupedeck:plugin/<Name>/reload` (Name is AgentHub, Herdr, ITerm, Codex, or Claude).

## Hooks

```sh
python3 hooks/claude_hooks.py install
python3 hooks/codex_hooks.py install
```

Then run `codex`, type `/hooks`, and trust the Birchpad entries; Codex skips them until then.
Both installers take `--port` (default 8766) and write it into the hook commands; use the same value as the Agent Hub `port` plugin setting.
Both scripts back up the file they change, and `remove` takes the Birchpad entries out again.
While Logitech's "Claude Desktop (Beta)" plugin is installed, leave off `--blocking-permissions` so the two plugins do not both hold permission requests.

## Package

```sh
tools/package.sh
open dist/BirchpadAgentHub.lplug4
```

`tools/package.sh` runs the tests, builds each plugin in Release, and writes a verified `.lplug4` per plugin to `dist/`.
Opening a package installs it after you accept the Options+ prompt; `logiplugintool install` fails against Logi Plugin Service.
`SETUP.md` covers the full install: which plugins, hooks, permissions, and the Agents profile.

## Settings files

These live in `~/Library/Application Support/Birchpad/` and are created on first use:

- `prompts.json`: saved prompts for the Claude and Codex plugins.
- `iterm-commands.json`: saved commands for the iTerm plugin.
- `cloud-cache.json`: the last cloud helper result.

The Prompts and Commands folders reread their file each time they open.

## Permissions

Logi Plugin Service needs Accessibility permission for menu clicks and keystrokes.
macOS asks for Automation permission the first time a key controls iTerm, Ghostty, Claude, or ChatGPT.
