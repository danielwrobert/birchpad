#!/usr/bin/env python3
"""Add or remove DevDeck Agent Hub hooks in a Claude Code settings file.

Entries are native HTTP hooks, so a stopped plugin costs Claude Code a refused
connection and nothing else. Our entries are recognized by their URL, so running
this twice changes nothing and --remove leaves every other hook alone.

Usage:
  claude_hooks.py install [--blocking-permissions] [--port 8766] [--settings PATH]
  claude_hooks.py remove [--port 8766] [--settings PATH]
"""

import argparse
import json
import shutil
import sys
import time
from pathlib import Path

# Events that drive tile state. PermissionRequest is added separately.
STATUS_EVENTS = [
    "SessionStart",
    "SessionEnd",
    "UserPromptSubmit",
    "PreToolUse",
    "PostToolUse",
    "PostToolUseFailure",
    "Notification",
    "Stop",
    "StopFailure",
    "SubagentStop",
]

# Events whose hooks take a tool-name matcher.
TOOL_EVENTS = {"PreToolUse", "PostToolUse", "PostToolUseFailure", "PermissionRequest"}

STATUS_TIMEOUT_SECONDS = 5

# Must exceed the plugin's hold time (5 minutes) so the plugin, not Claude, ends the wait.
PERMISSION_TIMEOUT_SECONDS = 330


def base_url(port):
    return f"http://127.0.0.1:{port}/claude/"


def is_ours(hook, port):
    return hook.get("type") == "http" and str(hook.get("url", "")).startswith(base_url(port))


def remove_ours(hooks, port):
    """Drops our hook handlers, then any matcher group left empty. Returns the count removed."""
    removed = 0
    for event in list(hooks):
        groups = []
        for group in hooks[event]:
            kept = [h for h in group.get("hooks", []) if not is_ours(h, port)]
            removed += len(group.get("hooks", [])) - len(kept)
            if kept:
                groups.append({**group, "hooks": kept})
        if groups:
            hooks[event] = groups
        else:
            del hooks[event]
    return removed


def add_ours(hooks, port, blocking_permissions):
    def group(event, route, timeout):
        entry = {"hooks": [{"type": "http", "url": base_url(port) + route, "timeout": timeout}]}
        if event in TOOL_EVENTS:
            entry["matcher"] = "*"
        return entry

    for event in STATUS_EVENTS:
        hooks.setdefault(event, []).append(group(event, "hook", STATUS_TIMEOUT_SECONDS))

    if blocking_permissions:
        permission = group("PermissionRequest", "permission", PERMISSION_TIMEOUT_SECONDS)
    else:
        permission = group("PermissionRequest", "hook", STATUS_TIMEOUT_SECONDS)
    hooks.setdefault("PermissionRequest", []).append(permission)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("action", choices=["install", "remove"])
    parser.add_argument("--port", type=int, default=8766)
    parser.add_argument("--settings", type=Path, default=Path.home() / ".claude" / "settings.json")
    parser.add_argument(
        "--blocking-permissions",
        action="store_true",
        help="hold PermissionRequest hooks for a keypad answer (only once no other plugin does)",
    )
    args = parser.parse_args()

    settings = json.loads(args.settings.read_text()) if args.settings.exists() else {}
    hooks = settings.setdefault("hooks", {})

    backup = args.settings.with_name(f"{args.settings.name}.devdeck-{time.strftime('%Y%m%d-%H%M%S')}")
    if args.settings.exists():
        shutil.copy2(args.settings, backup)

    removed = remove_ours(hooks, args.port)
    if args.action == "install":
        add_ours(hooks, args.port, args.blocking_permissions)
    if not hooks:
        del settings["hooks"]

    tmp = args.settings.with_suffix(".json.devdeck-tmp")
    tmp.write_text(json.dumps(settings, indent=2) + "\n")
    tmp.replace(args.settings)

    added = 0 if args.action == "remove" else len(STATUS_EVENTS) + 1
    print(f"{args.action}: removed {removed}, added {added}; backup at {backup}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
