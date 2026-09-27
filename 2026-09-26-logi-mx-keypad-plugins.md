# Logi MX Keypad plugins for the dev workflow

Status: Not started.
Phase 0 research is written up in `docs/phase-0-findings.md`.
Code for phases 1, 3, 4, 5, and 6 exists and awaits review on the device; see `docs/agent-hub.md`, `docs/iterm.md`, and `docs/app-plugins.md`.
Phase 7 packaging exists: `tools/package.sh` writes verified `.lplug4` files to `dist/`.
Phase 2 and the default profiles wait for that review.

## Goal

Build Logi Actions SDK plugins so the MX Keypad controls and reports on the tools used every day:

- Claude Desktop (Claude Code, Cowork, Design, chats, artifacts, routines, Remote Control)
- ChatGPT desktop app (Codex and Work), bundle ID `com.openai.codex`
- iTerm2, including the `claude` and `codex` CLIs running inside it

## Decisions

- SDK: C# targeting `net10.0`, because the installed Plugin Service's `PluginApi.dll` 6.4.1 is built for .NET 10.
  The Node.js SDK (v0.1.1, checked in its source) returns no key image and has no app linking, so it cannot do live status or per-app switching.
- Structure: one app-linked plugin per app so "Adapt to App" switches the keypad, plus one universal plugin for status and lists.
- Live status: fed by Claude Code and Codex hooks posting to a local HTTP listener.
- Hosts: agents run in Claude Desktop, the ChatGPT/Codex app, and the CLI in iTerm, so status covers all three.
- Replace Logitech's official "Claude Desktop (Beta)" plugin with our own, using it as a reference for behavior and design.
- Cloud routines and remote sessions on other machines come from a headless Claude Code helper run every 30 minutes and on demand.
- Artifacts and chats are keys that open their pages; no headless tool lists them.

## Reference: the official Claude plugin

Installed version 1.0.0.0011, by Logitech, closed source.
Used as a guide for behavior and tile design only; no code is copied from it.

What it does, from its package contents:

- 16 session tiles as dynamic folders, a slot-order command, and a "clear finished" command.
- Global permission answers: allow, always, deny.
- Global interrupt (sends Escape).
- Chat keys: new chat, dictate, screenshot, clipboard.
- Deep links `claude://code/new` and `claude://resume`.
- A listener on `localhost:8765` with `/hook`, `/permission` (blocks up to 330 seconds for a keypad answer), and `/statusline`.
- It declares `HasNoApplication` so its tiles stay visible in every app.

What it changed on this Mac:

- Added hook entries to `~/.claude/settings.json` that `curl` port 8765 for Notification, PermissionRequest, PostToolUse, and other events.
- Replaced the Claude Code status line with `~/.claude/claude-desktop/statusline.sh`, which prints nothing and posts to `/statusline`.
  The script says it is rewritten on every plugin load.

What it lacks: Remote Control sessions, scheduled routines, chat lists, artifacts, Cowork, Design, and Codex.

## Data sources

Local, read directly and cheap:

- Live state: Claude Code hooks and the status line JSON, for Desktop, CLI, and Remote Control sessions started on this Mac.
- Desktop Code sessions: `~/Library/Application Support/Claude/claude-code-sessions/*/*/local_*.json` (title, cwd, branch, model, permission mode, last activity, archived).
- Local scheduled tasks: `scheduled-tasks.json` in the same folder (cron, enabled, last run, cwd).
- Cowork sessions: `~/Library/Application Support/Claude/local-agent-mode-sessions/`.
- CLI sessions: `~/.claude/projects/`.
- Codex: Codex hooks for live state, and `codex app-server` (JSON-RPC over stdio, no model calls) for thread lists and rate limits.

Cloud, through the headless helper:

- The helper runs `claude -p` with haiku, `--output-format json`, `--setting-sources ""`, an empty MCP config, and only `RemoteTrigger`, `ListAgents`, and `ToolSearch` allowed.
  Measured cost: about $0.02 and 5.5 seconds per run.
- Cloud routines: `RemoteTrigger` list.
- Remote sessions on other machines and cloud sessions: `ListAgents`, when Remote Control is connected.
- Artifacts and chats: not available headless; keys open their pages.
- Results are cached to a JSON file; the plugin reads the cache and never waits on the helper.

The Desktop session files and `scheduled-tasks.json` are internal app files with no stability promise.
Each reader lives in one class so a format change is one fix.

## Hardware constraints

The MX Keypad has 9 full-color LCD keys and 2 page buttons, with up to 15 pages.
It has no dial, so adjustment actions are out of scope.
Key images are 80x80 PNG, drawn at runtime with `BitmapBuilder` and refreshed with `ActionImageChanged()`.
Default layouts ship as `profiles/DefaultProfile70mac.lp5` (70 is the MX Creative Keypad ID).

## Architecture

```text
logi/
  src/
    Birchpad.Core/          shared library
    BirchpadAgentHubPlugin/        HasNoApplication: status tiles, permissions, lists
    BirchpadClaudePlugin/          app-linked: com.anthropic.claudefordesktop
    BirchpadCodexPlugin/           app-linked: com.openai.codex
    BirchpadITermPlugin/           app-linked: com.googlecode.iterm2
  helper/                  headless Claude Code prompt, allowed-tools list, cache schema
  hooks/                   scripts that Claude Code and Codex hooks call
  docs/
```

Birchpad.Core holds the code every plugin needs:

- `HookListener`: `HttpListener` on `localhost:8766`, parsing hook and status line JSON into session events.
- `SessionStore`: sessions by ID with source (Desktop, CLI, Remote Control, Codex), state (working, waiting for input, waiting for approval, done, failed), project, and last update.
- `LocalReaders`: one class per local file source listed above.
- `CloudCache`: runs the helper on a timer and reads its cache file.
- `MacAutomation`: activate an app by bundle ID, click a menu item by path through System Events (keystrokes as fallback), run AppleScript, open URLs and `claude://` links.
  Requires Accessibility permission for Logi Plugin Service.
- `TileRenderer`: status tiles (project, state color, elapsed time) and command keys (symbol plus label).

Only Agent Hub runs the listener and the helper, so there is one owner for hooks.

## Plugins

### Agent Hub plugin

Universal (`HasNoApplication`), so its keys work on every app profile.

- Live session tiles for all sources, as a dynamic folder; pressing a tile focuses that session in Claude Desktop, the Codex app, or the iTerm tab that owns its TTY.
- Global allow, always, deny, and interrupt for the session waiting longest.
- Jump to the oldest session waiting for input.
- Routines folder: local scheduled tasks and cloud routines, with next and last run; press to open, with run-now where the tool allows it.
- Artifacts key: opens the artifacts page.
- Remote sessions folder: sessions on other machines and in the cloud.
- Chats key: opens the chat list.
- Codex usage key: 5-hour and weekly limits from the Codex app-server.
- Refresh key that runs the helper now.

### Claude plugin

Linked to `com.anthropic.claudefordesktop`.
Commands for the app itself; the default profile puts Agent Hub tiles on page 1.

- Switch between Chat, Cowork, Code, and Design.
- New chat, new Code session (`claude://code/new`), resume (`claude://resume`), new Cowork task, new design.
- Dictate, screenshot, paste clipboard (parity with the official plugin).
- Saved prompts: a command with a parameter that pastes a stored prompt and sends it.
- Cycle permission mode, change model, toggle sidebar, search.

### Codex plugin

Linked to `com.openai.codex`.

- New Codex task, new chat, switch between Codex and Work.
- Approve, deny, or interrupt the current turn.
- Change model or reasoning level if the app has a shortcut.
- Saved prompts.

### iTerm plugin

Linked to `com.googlecode.iterm2`.
Uses iTerm's AppleScript dictionary first, with the iTerm Python API as the fallback.

- New tab or window with a chosen iTerm profile, split pane vertical or horizontal.
- Run a saved command in the current session (parameterized: `ddev start`, `ddev drush cr`, `git status`, `ddev theme-build`).
- Toggle broadcast input, clear buffer.
- Agent controls sent to the current session: Esc, Shift+Tab, `/clear`, `/compact`, `/model`, `/remote-control`, and the Codex equivalents.
- Launch `claude` or `codex` in a new pane in the current directory.

## Default profiles

Each plugin ships a ready-made keypad layout so it works on first install.

- App-linked plugins (Claude, Codex, iTerm) ship `package/profiles/DefaultProfile70mac.lp5`, which Options+ applies when the plugin is installed or a new profile is created.
- Agent Hub is universal and has no app profile of its own, so its layout ships as an importable `profiles/agent-hub.lp5` in this repo.
- Each app plugin's default profile puts Agent Hub's session tiles on page 1, so status is visible in every app.

Workflow: arrange the keys in Options+, export the profile, save it in the repo, and inspect the `.lp5` format to see whether later changes can be made in the file directly.
Updating a shipped default profile does not change profiles users already have; it applies only to new ones.

## Hook wiring

Claude Code: one hook entry per event that `curl`s `localhost:8766` with a short timeout and `exit 0`, so a stopped plugin never blocks Claude Code.
PermissionRequest uses a blocking request with a timeout, the same approach as the official plugin, so the keypad can answer.
The status line points to a script that posts to the listener and also prints a normal status line, so the terminal keeps one.
`~/.config/iterm2/cc-status` stays as it is.

Codex: hooks are stable in 0.157.0 and cover the same events as Claude Code, including permissionRequest.
The same `curl` pattern posts to the listener; each new hook must be marked trusted in Codex.
The existing `notify` entry for Computer Use stays as it is.

## Phases

### Phase 0: spikes and reference capture

Findings: `docs/phase-0-findings.md`.
Key-state screenshots were skipped by decision.

1. Record the official plugin's behavior before removing it: screenshots of each key state, its action list, and a copy of its hook entries and status line script in `docs/reference/`.
2. Install the .NET SDK and `LogiPluginTool`; build and link the generated sample plugin to the keypad.
3. Confirm app linking by macOS bundle ID and that "Adapt to App" switches profiles.
4. Confirm a `HasNoApplication` plugin's actions can sit on an app-linked profile's keys.
5. Run the headless helper by hand: confirm which tools list artifacts, routines, remote sessions, and chats, the time per run, and the usage per run.
6. List keyboard shortcuts and URL schemes for Claude Desktop and the ChatGPT/Codex app.
7. Check Codex 0.157.0 for hooks support and the `notify` payload.
8. Check the Logi Marketplace for community Codex and iTerm plugins and note what they cover.
9. Confirm Accessibility permission and that System Events keystrokes reach each app.

Exit: `docs/phase-0-findings.md`, and this plan updated to match.

### Phase 1: Birchpad.Core and Agent Hub with Claude status

Listener, session store, local readers, tiles, permissions, interrupt.
Runs next to the official plugin on its own port so both can be compared on the device.

### Phase 2: replace the official plugin

Uninstall "Claude Desktop (Beta)" from Logi Options+.
Remove its port 8765 hook entries from `~/.claude/settings.json` and replace its status line script.
Confirm nothing posts to port 8765 and Claude Code shows no hook errors.

### Phase 3: cloud lists

Helper, cache, the routines and remote sessions folders, and the artifacts and chats keys.

### Phase 4: iTerm plugin

Saved commands, pane and tab actions, CLI agent controls, default profile.

### Phase 5: Codex status and Codex plugin

Codex hooks and app-server reader into Agent Hub, then the app-linked Codex plugin.
Confirm whether a second app-server client sees live turns and approvals from the desktop app.

### Phase 6: Claude plugin

App commands, saved prompts, default profile with Agent Hub tiles on page 1.

### Phase 7: polish and packaging

Icons and action symbols, settings for saved prompts, commands, and helper interval, default profiles for every plugin, `.lplug4` packages for local install.
Marketplace submission is optional and follows the approval guidelines if chosen later.

## Risks

- Desktop session files, `scheduled-tasks.json`, and the Cowork folder are internal formats and can change with any Claude Desktop update.
- The headless helper uses plan usage on every run and depends on tools that exist today; the interval is a setting and the lists degrade to open-the-page keys if a tool disappears.
- Keystroke automation breaks when an app changes its shortcuts; each shortcut lives in one mapping table per plugin.
- Codex's hook surface may be thinner than Claude Code's, which limits status detail and keypad approvals.

## Acceptance criteria

- Each plugin installs from its `.lplug4`, loads in Logi Options+ without errors, and appears on the MX Keypad.
- With the official plugin removed, every feature listed under "Reference: the official Claude plugin" works from our plugins.
- Switching between Claude Desktop, ChatGPT, and iTerm switches the keypad profile.
- A session waiting for approval (Claude or Codex) shows on a tile within 2 seconds and can be answered or focused from the keypad.
- The routines folder shows data no older than the helper interval.
- Stopping any plugin leaves Claude Code and Codex working with no added delay.

## References

- Logi Actions SDK docs: https://logitech.github.io/actions-sdk-docs/
- App linking: https://logitech.github.io/actions-sdk-docs/csharp/tutorial/link-the-plugin-to-an-application/
- Changing key images: https://logitech.github.io/actions-sdk-docs/csharp/tutorial/change-a-button-image/
- Dynamic folders: https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/implementing-dynamic-folders/
- Default profiles: https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/default-application-profiles/
