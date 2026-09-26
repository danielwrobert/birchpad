# Claude and Codex plugin developer notes

"DevDeck Claude" is linked to Claude Desktop (`com.anthropic.claudefordesktop`).
"DevDeck Codex" is linked to the ChatGPT app (`com.openai.codex`), which hosts Codex.
Both use the shared action base in `src/Shared`, so each key runs off the key-press thread and logs failures to its plugin log.

## Claude keys

- Deep links: New chat (`claude://claude.ai/new`), New code (`claude://code/new`), Needs me (`claude://code/needs-input`).
- Menu clicks: Chat (Go > Chat and Cowork), Code, Prev and Next session, Search, Palette, Dictate.
- Shortcuts: Sidebar (⌘B), Terminal (⌘J), Changes (⇧⌘D), Side chat (⌘;); their menu names change with state, so they use keys.
- Stop: brings Claude forward and sends Escape.

## Codex keys

- Menu clicks: New chat, Temp chat, Prev and Next chat, Sidebar, Review, Files, Terminal, Dictate.
- Stop: brings ChatGPT forward and sends Escape.

The app has no menu item or known shortcut for switching between Codex and Work, or for approving a request, so those keys are not built.

## Saved prompts

Both plugins read `~/Library/Application Support/DevDeck/prompts.json`, a JSON array of `{"name": "...", "text": "..."}` objects.
A key pastes its prompt into the app's focused field and presses Return; the clipboard's text is put back afterwards.
The file starts with three prompts and the keys update when it is saved.

## Codex status in Agent Hub

`hooks/codex_hooks.py install` writes DevDeck entries to `~/.codex/hooks.json` and copies `devdeck-codex-hook.sh` to `~/.codex/hooks/`.
Codex hooks only run commands, so the script forwards each payload to `http://127.0.0.1:8766/codex/hook` with `curl`.
Codex skips new hooks until they are trusted: open `/hooks` in the Codex CLI and trust the DevDeck entries.

Codex shows its own approval prompt only after PermissionRequest hooks return, so holding the hook for a keypad answer would delay that prompt.
PermissionRequest is status only by default; `install --blocking-permissions` holds it for up to 5 minutes.

Agent Hub names Codex tiles from the Codex app server's thread list and shows 5-hour and weekly usage on the Codex usage key.
The app server makes no model calls.

## Default profiles

A `.lp5` profile is a zip of the profile folder, `ApplicationInfo.json`, the app icon, and package metadata.
The exact file names are obfuscated in the plugin service, so the default profiles will be built from one profile exported from Options+.
