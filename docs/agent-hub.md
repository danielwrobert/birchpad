# Agent Hub developer notes

Agent Hub is the universal plugin that shows agent sessions on the keypad and answers permission requests.
Its logic lives in `src/DevDeck.Core`, which has no Logitech dependency and is covered by `tests/DevDeck.Core.Tests`.
The plugin in `src/AgentHubPlugin` wires that logic to keys.

## Build and test

```sh
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
export PATH=/opt/homebrew/opt/dotnet/bin:$PATH:$HOME/.dotnet/tools
dotnet test tests/DevDeck.Core.Tests
dotnet build src/AgentHubPlugin
```

Building the plugin writes `AgentHubPlugin.link` into `~/Library/Application Support/Logi/LogiPluginService/Plugins/` and asks Logi Plugin Service to reload it.
The plugin log is `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/AgentHub.log`.
A reload clears all session state.

`LogiPluginTool` 6.1.4 is built for .NET 8; run it with `DOTNET_ROLL_FORWARD=Major` so it uses .NET 10.

Every plugin needs a `ClientApplication` subclass, even a universal one.
Without it the service logs only "Cannot load plugin" and adds the plugin to its disabled list.

## HTTP routes

The listener binds `127.0.0.1:8766` and `localhost:8766` only.

- `POST /claude/hook`: any Claude Code hook; updates session state and returns an empty body at once.
- `POST /claude/permission`: a PermissionRequest hook; held up to 5 minutes for a keypad answer, then an empty body.
- `POST /claude/statusline`: the status line JSON; records the model name.
- `GET /sessions`: what the hub knows, as JSON.
- `GET /health`: returns `ok`.

POST routes accept only `Content-Type: application/json` with no `Origin` header, and answer anything else with 403.
A web page can send a simple cross-site POST to localhost, but not with a JSON content type, and a browser always adds `Origin`.

A held permission request is released without an answer when its own tool call finishes or is denied (same session, subagent, tool, and input), or when the turn ends.
Another subagent's tool finishing leaves it held and the tile on "Approve?".

## Hooks

`hooks/claude_hooks.py install` adds native HTTP hooks for 11 events to `~/.claude/settings.json`.
It recognizes its own entries by URL, backs up the file first, and `remove` restores the file exactly.

While the official Claude plugin is installed, PermissionRequest goes to `/claude/hook` (status only).
After the official plugin is removed, `install --blocking-permissions` points it at `/claude/permission` so the keypad can answer.

## Cloud helper

When the `claude` CLI is found, Agent Hub runs it headless every 30 minutes (plugin setting `cloudIntervalMinutes`, minimum 5).
Each run costs about $0.02 of subscription usage and reads cloud routines (`RemoteTrigger`) and peer sessions (`ListAgents`).
Results are taken from the tool output, not the model's reply, and cached in `~/Library/Application Support/DevDeck/cloud-cache.json`.
A failed run keeps the last good lists and marks the Refresh key red.

Local scheduled tasks come from Claude Desktop's `scheduled-tasks.json` and are reread every minute at no cost.

## Profiles

Keypad profiles are JSON files under `~/Library/Application Support/Logi/LogiPluginService/Applications/Loupedeck70/<app>/Profiles/<id>/ProfileInfo.json`.
Each key stores an action ID such as `$AgentHub___Loupedeck.AgentHubPlugin.Actions.SessionSlotCommand___1`, so default profiles can be generated rather than exported by hand.

## Keys

- Session slot 1 to 16: a live tile per session; press to open it in Claude Desktop or its iTerm tab.
- Allow, Always allow, Deny: answer the oldest held permission request.
- Stop: send Escape to the most recently active session.
- Next: open the session waiting longest for you.
- Clear: free the slots of finished sessions.
- Routines folder: Claude Desktop scheduled tasks and cloud routines, soonest first, with the next run time.
- Other sessions folder: Remote Control and cloud sessions from the helper; local sessions already have tiles.
- Artifacts, Chats: open those pages on claude.ai in the default browser.
- Refresh cloud: shows how old the cloud lists are; press to fetch them now.

Tile colors: blue working, amber needs approval, purple question, green done, red failed, gray ready.

CLI tiles find their iTerm tab by matching the session's folder to iTerm's `session.path`, which needs iTerm shell integration.
