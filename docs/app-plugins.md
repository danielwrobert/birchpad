# Claude and Codex plugin developer notes

"Birchpad Claude" is linked to Claude Desktop (`com.anthropic.claudefordesktop`).
"Birchpad Codex" is linked to the ChatGPT app (`com.openai.codex`), which hosts Codex.
Both use the shared action base in `src/Shared`, so each key runs off the key-press thread and logs failures to its plugin log.

## Claude keys

Each key is a folder with Back in the top-left key.

- New: Chat (`claude://claude.ai/new`), Code session (`claude://code/new`), Cowork (Go > Chat and Cowork).
- Prompts: one key per saved prompt.
- Chats: Recent chats (`claude://claude.ai/chat`) and Projects (`claude://claude.ai/project`), which open in Claude Desktop, and Artifacts, which opens claude.ai in the browser.
  Claude Desktop accepts only a fixed set of `claude://claude.ai/` routes (`chat`, `project`, `new`, `settings`, `tasks`, `cowork`, `customize`, and a few more); `chat` and `project` with no ID fall back to Recents and the projects list, and no route opens Artifacts.
- Projects: folders Claude Desktop Code sessions ran in, most recent first; press one for a new Code session there (`claude://code/new?folder=`).
- Settings: Settings, Connectors, Sidebar, Terminal, Changes, Side chat, then `/mcp`, `/plugins`, `/model`, `/config`, `/hooks` typed into the current session, and Stop.

## ChatGPT and Codex keys

- New: Chat (File > New Chat), then Codex session (`codex://threads/new`), in the same keys as Claude's Chat and Code session.
- Prompts: one key per saved prompt.
- Chats: Recent chats (the sidebar), Prev chat, Next chat.
- Projects: Codex projects from `~/.codex/state_5.sqlite`; the new-thread link takes no folder, so a project opens a new Codex session.
- Scheduled: opens the app, since no local source lists Codex automations yet.
- Settings: Settings, Connections, Plugins, Sidebar, Review, Files, Terminal, then `/mcp`, `/model`, `/approvals`, `/status`, and Stop.

## Slash commands

`~/Library/Application Support/Birchpad/slash-commands.json` holds one list of slash commands for every app, as `{"name": "...", "command": "...", "submit": true}` objects.
With `"submit": false` the command is typed without Return, for commands that take arguments; keep its trailing space.
The file starts with six general commands; replace them with the commands your team uses.

- Claude and ChatGPT: the first key on the Prompts page, Commands, opens the list; a command is pasted into the app's message box.
- iTerm: the Slash commands key on the home page types a command into the current pane.
- Any profile, including the default one: Agent Hub's Slash commands folder pastes a command into whatever app is in front.

## Saved prompts

Both plugins read `~/Library/Application Support/Birchpad/prompts.json`, a JSON array of `{"name": "...", "text": "..."}` objects.
A key pastes its prompt into the app's focused field and presses Return; the clipboard's text is put back afterwards.
The folder reads the file each time it opens.

## Codex status in Agent Hub

`hooks/codex_hooks.py install` writes Birchpad entries to `~/.codex/hooks.json` and copies `birchpad-codex-hook.sh` to `~/.codex/hooks/`.
Codex hooks only run commands, so the script forwards each payload to `http://127.0.0.1:8766/codex/hook` with `curl`.
Codex skips new hooks until they are trusted: open `/hooks` in the Codex CLI and trust the Birchpad entries.

Codex shows its own approval prompt only after PermissionRequest hooks return, so holding the hook for a keypad answer would delay that prompt.
PermissionRequest is status only by default; `install --blocking-permissions` holds it for up to 5 minutes.

Agent Hub names Codex tiles from the Codex app server's thread list and shows 5-hour and weekly usage on the Codex usage key.
The app server makes no model calls.

## Default profiles

`python3 tools/profiles.py write` builds one home page per app plugin from `docs/workflow-design.md` and writes it to `src/<Plugin>Plugin/package/profiles/DefaultProfile70mac.lp5`, which ships in the plugin package, and to `profiles/generated/` for Import in Options+.
`check` confirms every key names a folder or command class that exists, placed as the right kind.

A `.lp5` is a zip of `ProfileInfo.json`, `ApplicationInfo.json`, `ApplicationIcon.png`, and `metadata/` (`LoupedeckPackage.yaml` with `type: Profile5`, `AdvancedInfo.json` naming AgentHub, and `ProfilePreview.json` with one small image per key), copied from the export in `profiles/exported/`.
A folder key is stored as `$<Plugin>___#DynamicFolder___DynamicFolder#Loupedeck.<Plugin>Plugin.Actions.<Class>`.
