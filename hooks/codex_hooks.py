#!/usr/bin/env python3
"""Add or remove DevDeck Agent Hub hooks in ~/.codex/hooks.json.

Codex hooks only run commands, so each entry calls devdeck-codex-hook.sh, which
this script copies to ~/.codex/hooks/. Entries are recognized by that script name,
so running this twice changes nothing and remove leaves other hooks alone.

Codex skips new or changed hooks until they are trusted: open /hooks in the Codex
CLI after installing and trust the DevDeck entries.

Usage:
  codex_hooks.py install [--blocking-permissions] [--codex-home ~/.codex]
  codex_hooks.py remove [--codex-home ~/.codex]
"""

import argparse
import json
import shutil
import sys
import time
from pathlib import Path

SCRIPT_NAME = "devdeck-codex-hook.sh"

# Written only when this script creates hooks.json, and removed with the last DevDeck entry.
DESCRIPTION = "Lifecycle hooks, including DevDeck Agent Hub status."

# Event name -> timeout in seconds. SessionEnd and Interrupt allow at most 3.
STATUS_EVENTS = {
    "SessionStart": 3,
    "SessionEnd": 3,
    "UserPromptSubmit": 3,
    "PreToolUse": 3,
    "PostToolUse": 3,
    "SubagentStop": 3,
    "Stop": 3,
    "Interrupt": 3,
}

# Longer than Agent Hub's 5-minute hold, so Agent Hub ends the wait.
PERMISSION_TIMEOUT_SECONDS = 330


def is_ours(hook):
    return hook.get("type") == "command" and SCRIPT_NAME in str(hook.get("command", ""))


def remove_ours(hooks):
    removed = 0
    for event in list(hooks):
        groups = []
        for group in hooks[event]:
            kept = [h for h in group.get("hooks", []) if not is_ours(h)]
            removed += len(group.get("hooks", [])) - len(kept)
            if kept:
                groups.append({**group, "hooks": kept})
        if groups:
            hooks[event] = groups
        else:
            del hooks[event]
    return removed


def add_ours(hooks, script, blocking_permissions):
    def entry(route, timeout):
        return {"hooks": [{"type": "command", "command": f"'{script}' {route}", "timeout": timeout}]}

    for event, timeout in STATUS_EVENTS.items():
        hooks.setdefault(event, []).append(entry("hook", timeout))

    if blocking_permissions:
        hooks.setdefault("PermissionRequest", []).append(entry("permission", PERMISSION_TIMEOUT_SECONDS))
    else:
        hooks.setdefault("PermissionRequest", []).append(entry("hook", 3))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("action", choices=["install", "remove"])
    parser.add_argument("--codex-home", type=Path, default=Path.home() / ".codex")
    parser.add_argument(
        "--blocking-permissions",
        action="store_true",
        help="hold PermissionRequest for a keypad answer; Codex shows its own prompt only after the hook returns",
    )
    args = parser.parse_args()

    hooks_file = args.codex_home / "hooks.json"
    config = json.loads(hooks_file.read_text()) if hooks_file.exists() else {}
    hooks = config.setdefault("hooks", {})

    if hooks_file.exists():
        backup = hooks_file.with_name(f"hooks.json.devdeck-{time.strftime('%Y%m%d-%H%M%S')}")
        shutil.copy2(hooks_file, backup)
        print(f"backup at {backup}")

    removed = remove_ours(hooks)
    added = 0
    if args.action == "install":
        script = args.codex_home / "hooks" / SCRIPT_NAME
        script.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(Path(__file__).with_name(SCRIPT_NAME), script)
        script.chmod(0o755)
        add_ours(hooks, script, args.blocking_permissions)
        added = len(STATUS_EVENTS) + 1

    if not hooks:
        del config["hooks"]
        if config.get("description") == DESCRIPTION:
            del config["description"]
    else:
        config.setdefault("description", DESCRIPTION)

    if config:
        tmp = hooks_file.with_suffix(".json.devdeck-tmp")
        tmp.write_text(json.dumps(config, indent=2) + "\n")
        tmp.replace(hooks_file)
    elif hooks_file.exists():
        hooks_file.unlink()

    if args.action == "remove":
        (args.codex_home / "hooks" / SCRIPT_NAME).unlink(missing_ok=True)

    print(f"{args.action}: removed {removed}, added {added} in {hooks_file}")
    if added:
        print("Open /hooks in the Codex CLI and trust the DevDeck entries; Codex skips them until then.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
