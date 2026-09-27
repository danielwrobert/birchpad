#!/usr/bin/env python3
"""Add or remove Birchpad Agent Hub hooks in ~/.codex/hooks.json.

Codex hooks only run commands, so each entry calls birchpad-codex-hook.sh, which
this script copies to ~/.codex/hooks/. Entries are recognized by that script name,
so running this twice changes nothing and remove leaves other hooks alone.

Codex skips new or changed hooks until they are trusted: open /hooks in the Codex
CLI after installing and trust the Birchpad entries.

Usage:
  codex_hooks.py install [--blocking-permissions] [--port 8766] [--codex-home ~/.codex]
  codex_hooks.py remove [--codex-home ~/.codex]
"""

import argparse
import json
import shutil
import sys
import time
from pathlib import Path

SCRIPT_NAME = "birchpad-codex-hook.sh"

# Names used before the rename to Birchpad; entries and files with these are replaced and cleaned up.
LEGACY_SCRIPT_NAMES = ("devdeck-codex-hook.sh",)
LEGACY_DESCRIPTIONS = ("Lifecycle hooks, including DevDeck Agent Hub status.",)

# Written only when this script creates hooks.json, and removed with the last Birchpad entry.
DESCRIPTION = "Lifecycle hooks, including Birchpad Agent Hub status."

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
    command = str(hook.get("command", ""))
    return hook.get("type") == "command" and any(name in command for name in (SCRIPT_NAME, *LEGACY_SCRIPT_NAMES))


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


def add_ours(hooks, script, blocking_permissions, port):
    def entry(route, timeout):
        return {"hooks": [{"type": "command", "command": f"'{script}' {route} {port}", "timeout": timeout}]}

    for event, timeout in STATUS_EVENTS.items():
        hooks.setdefault(event, []).append(entry("hook", timeout))

    if blocking_permissions:
        hooks.setdefault("PermissionRequest", []).append(entry("permission", PERMISSION_TIMEOUT_SECONDS))
    else:
        hooks.setdefault("PermissionRequest", []).append(entry("hook", 3))


def write_in_place(path, text):
    """Replaces a file's contents atomically without breaking a symlink or loosening its permissions."""
    target = path.resolve()
    tmp = target.with_name(target.name + ".birchpad-tmp")
    tmp.write_text(text)
    if target.exists():
        shutil.copymode(target, tmp)
    else:
        tmp.chmod(0o600)
    tmp.replace(target)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("action", choices=["install", "remove"])
    parser.add_argument("--codex-home", type=Path, default=Path.home() / ".codex")
    parser.add_argument("--port", type=int, default=8766)
    parser.add_argument(
        "--blocking-permissions",
        action="store_true",
        help="hold PermissionRequest for a keypad answer; Codex shows its own prompt only after the hook returns",
    )
    args = parser.parse_args()

    hooks_file = (args.codex_home / "hooks.json").resolve()
    config = json.loads(hooks_file.read_text()) if hooks_file.exists() else {}
    hooks = config.setdefault("hooks", {})

    if hooks_file.exists():
        backup = hooks_file.with_name(f"hooks.json.birchpad-{time.strftime('%Y%m%d-%H%M%S')}")
        shutil.copy2(hooks_file, backup)
        print(f"backup at {backup}")

    removed = remove_ours(hooks)
    added = 0
    if args.action == "install":
        script = args.codex_home / "hooks" / SCRIPT_NAME
        script.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(Path(__file__).with_name(SCRIPT_NAME), script)
        script.chmod(0o755)
        add_ours(hooks, script, args.blocking_permissions, args.port)
        added = len(STATUS_EVENTS) + 1

    if not hooks:
        del config["hooks"]
        if config.get("description") in (DESCRIPTION, *LEGACY_DESCRIPTIONS):
            del config["description"]
    else:
        if config.get("description") in LEGACY_DESCRIPTIONS:
            config["description"] = DESCRIPTION
        config.setdefault("description", DESCRIPTION)

    if config:
        write_in_place(hooks_file, json.dumps(config, indent=2) + "\n")
    elif hooks_file.exists():
        hooks_file.unlink()

    for name in LEGACY_SCRIPT_NAMES:
        (args.codex_home / "hooks" / name).unlink(missing_ok=True)
    if args.action == "remove":
        (args.codex_home / "hooks" / SCRIPT_NAME).unlink(missing_ok=True)

    print(f"{args.action}: removed {removed}, added {added} in {hooks_file}")
    if added:
        print("Open /hooks in the Codex CLI and trust the Birchpad entries; Codex skips them until then.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
