# Herdr plugin developer notes

"Birchpad Herdr" is a universal plugin for [herdr](https://herdr.dev), the terminal multiplexer for coding agents.
herdr runs inside a terminal app, so it has no app profile of its own; its Herdr key sits in key 5 of the iTerm home page and can be placed on any profile.
The design took ideas from [herdr-logi-plugin](https://github.com/niklasberglund/herdr-logi-plugin), which binds 27 fixed slots; this plugin binds none.

## How it talks to herdr

`Birchpad.Core.Herdr.HerdrClient` speaks herdr's socket API: one connection per request, one JSON line each way.
It never runs the `herdr` CLI.
The socket is the `socket` plugin setting, then `HERDR_SOCKET_PATH`, then `~/.config/herdr/herdr.sock`, then the most recently started named session under `~/.config/herdr/sessions/`.
`HerdrWatcher` polls `session.snapshot` every second while herdr answers and every five seconds while it does not, and redraws keys only when the snapshot's fingerprint changes.
Focusing a pane also brings forward the terminal app running the newest herdr client, found by walking up the process tree from it (`HerdrTerminal`).

## Keys

The Herdr key opens a home page; each category below it is also its own folder, so one can be placed on a profile directly.

| Key | Category | What it shows |
| --- | --- | --- |
| 1 | Back | |
| 2 | Agents | Every agent pane, ringed by status; press one for Open, Allow, Deny, Always, Stop, Slash commands, Prompts, and Mode, in the keys an Agent Hub session uses |
| 3 | New | New tab, new workspace, split right and down, then one key per installed agent that opens it in a split |
| 4 | Slash commands | The shared `slash-commands.json`, typed into herdr's focused pane |
| 5 | Workspaces | Every workspace, ringed by its agents; one with several tabs opens a page of them |
| 6 | Worktrees | Git worktrees of the focused workspace; press to open one or switch to it |
| 7 | Next waiting | The blocked agent first, then the finished one you have not seen |
| 8 | Agent keys | Esc, Mode, Ctrl-C, Clear, Compact, Model, Context, and Usage, sent to the focused pane |
| 9 | Panes | Arrows around Zoom to move between panes, plus split right and down |

Allow and Always type 1 and 2 into the approval prompt; Deny and Stop press Esc.
An agent's slash commands and prompts go to that agent's pane, whichever pane has focus.
Agent keys send `/context` and `/usage` to Claude Code and `/status` to Codex, from the agent herdr detected in the focused pane.
The New page lists the agent kinds from herdr's detection manifests whose executable a login shell finds, so installing another agent adds its key.
Keys reach herdr's panes directly, so they work while another app is in front.

## Status colors

| herdr status | Ring | Footer |
| --- | --- | --- |
| working | Blue | Working |
| blocked | Amber | Needs you |
| done | Green | Done |
| idle | Gray | Ready |
| unknown | Gray | None |

herdr's `blocked` covers both approvals and questions, so it takes the approval color.
`done` means finished and not yet seen; focusing the pane turns it into `idle`.

## Build and check

```sh
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
export PATH=/opt/homebrew/opt/dotnet/bin:$PATH
dotnet build src/BirchpadHerdrPlugin
```

The plugin log is `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/BirchpadHerdr.log`.
For experiments, start an isolated server with `herdr --session <name> server`; the client finds it when the default session is stopped.
