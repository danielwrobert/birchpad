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

## Hooks

`hooks/claude_hooks.py install` adds native HTTP hooks for 11 events to `~/.claude/settings.json`.
It recognizes its own entries by URL, backs up the file first, and `remove` restores the file exactly.

While the official Claude plugin is installed, PermissionRequest goes to `/claude/hook` (status only).
After the official plugin is removed, `install --blocking-permissions` points it at `/claude/permission` so the keypad can answer.

## Keys

- Session slot 1 to 16: a live tile per session; press to open it in Claude Desktop or its iTerm tab.
- Allow, Always allow, Deny: answer the oldest held permission request.
- Stop: send Escape to the most recently active session.
- Next: open the session waiting longest for you.
- Clear: free the slots of finished sessions.

Tile colors: blue working, amber needs approval, purple question, green done, red failed, gray ready.

CLI tiles find their iTerm tab by matching the session's folder to iTerm's `session.path`, which needs iTerm shell integration.
