#!/usr/bin/env python3
"""Add or remove Birchpad Agent Hub hooks in a Claude Code settings file.

Entries are native HTTP hooks, so a stopped plugin costs Claude Code a refused
connection and nothing else. Our entries are recognized by their URL, so running
this twice changes nothing and --remove leaves every other hook alone.

Usage:
  claude_hooks.py install [--blocking-permissions] [--statusline] [--port 8766] [--settings PATH]
  claude_hooks.py remove [--port 8766] [--settings PATH]
  claude_hooks.py remove-legacy [--settings PATH]

--statusline copies birchpad-statusline.sh next to the settings file and sets it as the
status line, unless a different status line is already set. remove clears it again.

remove-legacy takes out what Logitech's "Claude Desktop (Beta)" plugin added:
command hooks that post to localhost:8765, and its status line script.
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


STATUSLINE_NAME = "birchpad-statusline.sh"

LEGACY_PORT_MARK = "localhost:8765"
LEGACY_STATUSLINE_MARK = "claude-desktop/statusline.sh"


def is_legacy(hook):
    return hook.get("type") == "command" and LEGACY_PORT_MARK in str(hook.get("command", ""))


def is_ours(hook, port):
    return hook.get("type") == "http" and str(hook.get("url", "")).startswith(base_url(port))


def remove_ours(hooks, port):
    """Drops our hook handlers, then any matcher group left empty. Returns the count removed."""
    return remove_matching(hooks, lambda h: is_ours(h, port))


def remove_matching(hooks, doomed):
    removed = 0
    for event in list(hooks):
        groups = []
        for group in hooks[event]:
            kept = [h for h in group.get("hooks", []) if not doomed(h)]
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


def set_statusline(settings, claude_dir, port):
    current = str(settings.get("statusLine", {}).get("command", ""))
    if current and STATUSLINE_NAME not in current:
        print(f"kept the existing status line: {current}")
        return
    script = claude_dir / STATUSLINE_NAME
    shutil.copy2(Path(__file__).with_name(STATUSLINE_NAME), script)
    script.chmod(0o755)
    settings["statusLine"] = {"type": "command", "command": f"'{script}' {port}"}
    print(f"status line set to {script}")


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
    parser.add_argument("action", choices=["install", "remove", "remove-legacy"])
    parser.add_argument("--port", type=int, default=8766)
    parser.add_argument("--settings", type=Path, default=Path.home() / ".claude" / "settings.json")
    parser.add_argument(
        "--blocking-permissions",
        action="store_true",
        help="hold PermissionRequest hooks for a keypad answer (only once no other plugin does)",
    )
    parser.add_argument("--statusline", action="store_true", help="install the Birchpad status line")
    args = parser.parse_args()

    settings = json.loads(args.settings.read_text()) if args.settings.exists() else {}
    hooks = settings.setdefault("hooks", {})

    real = args.settings.resolve()
    backup = real.with_name(f"{real.name}.birchpad-{time.strftime('%Y%m%d-%H%M%S')}")
    if real.exists():
        shutil.copy2(real, backup)

    if args.action == "remove-legacy":
        removed = remove_matching(hooks, is_legacy)
        if LEGACY_STATUSLINE_MARK in str(settings.get("statusLine", {}).get("command", "")):
            del settings["statusLine"]
            print("removed the legacy status line")
    else:
        removed = remove_ours(hooks, args.port)
    if args.action == "install":
        add_ours(hooks, args.port, args.blocking_permissions)
        if args.statusline:
            set_statusline(settings, args.settings.parent, args.port)
    elif args.action == "remove" and STATUSLINE_NAME in str(settings.get("statusLine", {}).get("command", "")):
        del settings["statusLine"]
        print("removed the Birchpad status line")
    if not hooks:
        del settings["hooks"]

    write_in_place(args.settings, json.dumps(settings, indent=2) + "\n")

    added = len(STATUS_EVENTS) + 1 if args.action == "install" else 0
    print(f"{args.action}: removed {removed}, added {added}; backup at {backup}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
