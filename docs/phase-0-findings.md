# Phase 0 findings

Date: 2026-09-26.
All items are answered; item 1 has no key-state photos.

## 1. Reference capture of the official plugin

Saved in `docs/reference/`:

- `official-plugin-hooks.json`: the 10 hook events it registered on port 8765, and its status line setting.
- `official-statusline.sh`, `official-config.json`, `official-LoupedeckPackage.yaml`, `official-actions.txt`.
- `settings.json.snapshot-2026-09-26`: a copy of `~/.claude/settings.json` before any changes.

Its `config.json` shows features to match: slot order modes (sticky, recency, settled), prompt auto-submit, clipboard prompts, and chat prompt keys.

No status line was set before the official plugin; the 2026-08-24 settings backup has none.
Removing the official plugin needs no status line restore.

Still to do: photos or screenshots of each key state on the device.

## 2. Toolchain

The installed Logi Plugin Service ships `PluginApi.dll` 6.4.1, built for .NET 10.
A plugin targeting `net8.0` fails to compile against it (error CS1705).
The official plugin targets .NET 8 and still loads, so the host runs older plugins on its .NET 10 runtime.

Decision: target `net10.0` with the .NET 10 SDK (10.0.401, Homebrew `dotnet`).
`LogiPluginTool` 6.1.4 is installed as a global dotnet tool.
Builds need `DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec`.

The spike plugin at `spikes/BirchpadSpikePlugin` builds, links, and loads: "Plugin 'BirchpadSpike' version '1.0' loaded ... in 196 ms" in `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/BirchpadSpike.log`.
It is linked to iTerm by bundle ID and its key redraws in green, amber, or red with a counter on each press.

## 3. App linking

The spike returns `com.googlecode.iterm2` from `GetBundleName()` and declares `HasApplication`.
Passed on the device: the keypad switches to the spike's profile when iTerm is in front, and the key redraws its color and count on each press.
The profile tab is named after the plugin (BirchpadSpike) with a puzzle-piece icon, so each real plugin needs a clear `displayName` and its own `Icon256x256.png`.

## 4. Universal actions on app profiles

The official plugin is `HasNoApplication`.
Passed: its session slot and Allow keys can be placed on the spike's app-linked profile.
They did nothing when pressed because no session was in that slot and nothing was waiting for approval.

## 5. Headless helper

- `claude -p` works with the Pro/Max login; `--bare` fails because it needs an API key.
- The Artifact tool is not available in headless mode, so artifacts cannot be listed this way.
- `RemoteTrigger` (action `list`) works and returns cloud routines as JSON; this account has none today.
- `ListAgents` works but returns only local peer sessions; remote rows appear only when Remote Control is connected.
- No tool lists claude.ai chats.
- Trimmed flags (`--setting-sources "" --strict-mcp-config --mcp-config empty.json --disable-slash-commands --tools RemoteTrigger,ToolSearch`, model haiku) cut a run to about $0.02 and 5.5 seconds.
  The full default setup cost about $0.06 per run.
- `--setting-sources ""` also skips user hooks, so helper runs do not show up as sessions.

At the measured $0.0216 per run, every 10 minutes is about $3.10 of usage per day; every 30 minutes is about $1.05.

Consequences: routines come from the helper; artifacts and chats become keys that open their pages.

## 6. App shortcuts and URL schemes

Full menu dumps are in `docs/reference/menus-com.anthropic.claudefordesktop.txt` and `docs/reference/menus-com.openai.codex.txt`.

System Events can read and click menu items by path, which also covers items with no shortcut.
Useful Claude Desktop items: File > New Session (⌘N), View > Command Palette (⌘K), View > Show Side Chat (⌘;), View > Show Terminal (⌘J), Go > Chat and Cowork, Go > Code, Go > Previous and Next Session (⇧⌘[ and ⇧⌘]), Go > Search (⇧⌘K).
Useful ChatGPT items: File > New Chat (⌘N), File > New Temporary Chat (⇧⌘N), View > Toggle Review Panel (⌥⌘B), View > Previous and Next Chat (⇧⌘[ and ⇧⌘]).

URL schemes: Claude Desktop registers `claude://`; the official plugin uses `claude://code/new` and `claude://resume`.
ChatGPT registers `codex://`; its paths are not yet known.

Decision: actions click menu items by path first and fall back to keystrokes.

## 7. Codex

Codex CLI 0.157.0 has the `hooks` feature marked stable and enabled.
Hook events: preToolUse, permissionRequest, postToolUse, preCompact, postCompact, sessionStart, sessionEnd, userPromptSubmit, subagentStart, subagentStop, stop, interrupt.
Hooks have a trust status (trusted, untrusted, modified), so new hooks must be trusted before they run.

The `notify` wrapper is not needed.

`codex app-server` speaks JSON-RPC over stdio and needs no model calls.
Tested: `thread/list` returns thread ID, name, cwd, status, updated time, and source for Codex desktop app threads.
`account/rateLimits/read` returns 5-hour and weekly usage percentages.
It also has `turn/interrupt`, `thread/read`, and `thread/loaded/list`.
The shared daemon was not running (`codex app-server daemon version` found no socket), so tests used a private app-server process.

Open question for phase 5: whether a second app-server client can see live status and approvals for turns started in the desktop app.

## 8. Existing plugins

Reviews and Logitech's launch press release say community Claude Code and Codex plugins are on the Logi Marketplace, but name none.
One open-source example: `niklasberglund/herdr-logi-plugin` shows agent status from herdr-managed terminal panes on the keypad and prefers iTerm.
It depends on herdr, which is not in this workflow, so it is a design reference only.

## 9. Accessibility and automation

System Events reads both apps' menus from this session, so Accessibility is granted to the app running Claude Code.
Logi Plugin Service needs its own Accessibility grant; check when the first menu-click action runs.

## Plan changes

- Target `net10.0` and the .NET 10 SDK.
- Codex status comes from Codex hooks and the Codex app-server; drop the `notify` wrapper and session-file tailing.
- Artifacts and chats become open-the-page keys; the helper covers cloud routines and remote sessions only.
- Default helper interval: 30 minutes, plus a refresh key.
- Actions click menu items by path, with keystrokes as the fallback.
