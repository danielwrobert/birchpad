# Agents profile

Status: Not started.
Code for the home page, the agent page, and the generated profile exists and awaits review on the keypad.
The profile is `profiles/generated/Birchpad Agents.lp5`, built by `python3 tools/profiles.py write`.

## Goal

Give the keypad one General profile, Agents, that works the same whichever app is in front.
Every list of agents leads to one agent's page, and everything you send to an agent (slash commands, prompts, git and PR commands) lives on that page and goes to that agent.

## Decisions

- One General profile, Agents; Adapt to App is off, so the keypad stays on it.
- Chats and tasks are agents too: Claude chats, Cowork tasks, and ChatGPT chats sit beside coding sessions.
- Commands are sent from an agent's page, never from the home page, because a home page key has no agent to send to.
- The first version of the agent page has no Open, Always, Stop, Prompts, or Commands keys.
- The per-app home pages stay; this profile reuses their folders where they fit.
- The design rules still hold: no key is bound to one item, Back is top left in folders, and key 7 on the home page is Next waiting.

## Home page

| | | |
| --- | --- | --- |
| Coding sessions | Claude chats and tasks | ChatGPT chats and tasks |
| New | Workspaces | Scheduled |
| Next waiting | Usage | Projects |

- Coding sessions: every Claude Code and Codex session in Claude Desktop, the CLI, the ChatGPT app, and herdr panes, one key each with its status ring.
- Claude chats and tasks: Cowork tasks, listed from `local-agent-mode-sessions`, then keys that open recent chats and projects.
- ChatGPT chats and tasks: keys that open ChatGPT's chats; see the checks for what can be listed.
- New: new chat, Code session, Cowork task, Codex thread, and a herdr agent in a split.
- Workspaces: herdr workspaces and their tabs.
- Scheduled: Claude scheduled tasks and routines, then Codex automations once they can be listed.
- Projects: folders from Claude Desktop, Codex, and `~/Projects`; press one to start a coding session there.

## Agent page

Pressing any agent in a list does two things at once: it brings that agent to the front in its app, and it opens the agent's page on the keypad.
The page starts simple: approvals, then your commands.

| | | |
| --- | --- | --- |
| Back | Allow | Deny |
| /commit | /pr-create | /pr-to-tw |
| /pr-review | /codex:review | /pr-release |

- Allow and Deny light up only while the agent waits on an approval.
- The six command keys are the entries in `slash-commands.json`, read each time the page opens, so editing the file changes the keys.
- A seventh command would take the bottom-right key's place behind More.
- Each command goes to this agent, wherever it runs.

## Agent lists

Every key in Coding sessions, and in any other list of agents, shows the agent's status as a colored ring, its name or folder, and a status word:

| Ring | Word |
| --- | --- |
| Blue | Working |
| Amber, pulsing | Approve? |
| Purple, pulsing | Question |
| Green | Done |
| Gray | Ready |

The words are Agent Hub's, so a session reads the same here as on the app profiles.

## What exists

- `Birchpad.Core.Agents.CodingAgents` merges Agent Hub sessions with herdr agents; herdr reports each agent's session ID, the same one its hooks send, so a session in a herdr pane is one key.
- `Birchpad.Core.Claude.CoworkTasks` lists Cowork tasks, reading only IDs, titles, timestamps, and the archived flag.
- Agent Hub's `AgentActions` focuses, answers, and types into an agent by where it runs.
- Agent Hub folders: `CodingSessionsFolder`, `ClaudeTasksFolder`, `ChatGptTasksFolder`, `AgentsNewFolder`, and `AgentsProjectsFolder`; Workspaces, Scheduled, Next waiting, and Usage reuse existing actions.
- A menu key can now open a page and run an action on the same press, which is how pressing an agent both focuses it and opens its page.
- Cowork tasks have no link by ID, so pressing one opens the Cowork view and its commands are pasted there.
- Allow and Deny answer a request held by the permission hook; otherwise they type 1 or Esc into a herdr pane or iTerm tab. Claude Desktop and the ChatGPT app can be answered only through the hook.

## Sending to one agent

Each kind of agent needs its own way to receive typed text:

| Agent runs in | How text reaches it |
| --- | --- |
| herdr pane | herdr's `pane.send_input` and `agent.prompt`, already built in Birchpad Herdr |
| iTerm tab | iTerm's `write text` to the tab found by the session's folder |
| Claude Desktop Code session | Open the session, then paste into its prompt field |
| ChatGPT app thread | Open `codex://threads/<id>`, then paste |
| Claude chat or Cowork task | Open it, then paste |

Agent Hub owns the session list, so it gains one "send to session" call that picks the route from where the session runs.
A session seen by both Agent Hub hooks and herdr is one key, and herdr's route wins because it needs no window focus.

## Checks before building

1. Answered: the Options+ switch action is stored as `$@Generic___@ChangeProfile`, with `applicationName` and `profileName` (the target profile's ID) as parameters, so a generator can place it.
2. Answered: Options+ imports a generated `.lp5` bound to `@_defaultmac`, the General app, and its keys work (2026-09-28).
3. With Adapt to App off, confirm the Agents profile stays when iTerm, Claude, or ChatGPT comes to the front.
4. Find a local source that lists ChatGPT chats or tasks; until one exists, that key only opens ChatGPT.
5. Confirm pasting into a Claude Desktop Code session submits the same way as the CLI.

## Work

1. Run the checks and record the answers in `docs/workflow-design.md`.
2. Add the "send to session" call to Agent Hub, with one route per host in the table above.
3. Build the agent page once in shared code and use it from Coding sessions, Workspaces, and Next waiting.
4. Merge herdr agents into Coding sessions by matching pane folder and agent kind.
5. Add the Agents profile to `tools/profiles.py`, generate its `.lp5`, and import it.
6. Update `README.md`, `docs/workflow-design.md`, and `AGENTS.md` once it is reviewed on the keypad.

## Open questions

- Whether Claude chats and ChatGPT chats need their own agent pages, or whether opening them is enough.
- Whether New belongs on the home page or on each list's first page.
