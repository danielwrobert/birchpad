#!/usr/bin/env python3
"""Generate default keypad layouts for the DevDeck plugins.

Each layout is written as a ProfileInfo.json in the schema Logi Plugin Service
stores under Applications/Loupedeck70/<app>/Profiles/<id>/. Packing one into a
.lp5 needs the archive layout from a profile exported by Options+ (not yet done).

  profiles.py check   verify every placed key names an action that exists
  profiles.py write   check, then write profiles/generated/<app>/ProfileInfo.json
"""

import json
import re
import sys
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "profiles" / "generated"
KEYS_PER_PAGE = 9

# Stable IDs so regenerating a profile does not look like a new one.
NAMESPACE = uuid.UUID("5b0f6a52-8f5e-4f8e-9a57-0d3c6f1e2a10")


def action(plugin, cls, param=None):
    ident = f"${plugin}___Loupedeck.{plugin}Plugin.Actions.{cls}"
    return f"{ident}___{param}" if param is not None else ident


def hub(cls, param=None):
    return action("AgentHub", cls, param)


# Page 1 of every app profile: status is visible whichever app is in front.
AGENTS_PAGE = ("Agents", [
    hub("SessionSlotCommand", "1"), hub("SessionSlotCommand", "2"), hub("SessionSlotCommand", "3"),
    hub("SessionSlotCommand", "4"), hub("SessionSlotCommand", "5"), hub("NextWaitingCommand"),
    hub("AnswerCommand", "allow"), hub("AnswerCommand", "deny"), hub("InterruptCommand"),
])


def claude(param):
    return action("Claude", "AppCommand", param)


def codex(param):
    return action("Codex", "AppCommand", param)


def iterm(cls, param):
    return action("ITerm", cls, param)


PROFILES = {
    "claude": {
        "applicationName": "@_claude",
        "displayName": "DevDeck Claude",
        "plugin": "Claude",
        "pages": [
            AGENTS_PAGE,
            ("Claude", [
                claude("new-chat"), claude("new-code"), claude("needs-input"),
                claude("chat-cowork"), claude("code"), claude("palette"),
                claude("previous-session"), claude("next-session"), claude("stop"),
            ]),
            ("More", [
                action("Claude", "ClaudePromptCommand", "Explain"), action("Claude", "ClaudePromptCommand", "Review"), action("Claude", "ClaudePromptCommand", "Tests"),
                claude("sidebar"), claude("terminal"), claude("changes"),
                claude("side-chat"), claude("dictate"), claude("search"),
            ]),
        ],
    },
    "codex": {
        "applicationName": "@_codex",
        "displayName": "DevDeck Codex",
        "plugin": "Codex",
        "pages": [
            AGENTS_PAGE,
            ("Codex", [
                codex("new-chat"), codex("temporary-chat"), codex("review-panel"),
                codex("previous-chat"), codex("next-chat"), codex("sidebar"),
                codex("file-tree"), codex("terminal"), codex("stop"),
            ]),
            ("More", [
                action("Codex", "CodexPromptCommand", "Explain"), action("Codex", "CodexPromptCommand", "Review"), action("Codex", "CodexPromptCommand", "Tests"),
                hub("CodexUsageCommand"), codex("dictate"), None,
                None, None, None,
            ]),
        ],
    },
    "iterm": {
        "applicationName": "@_iterm",
        "displayName": "DevDeck iTerm",
        "plugin": "ITerm",
        "pages": [
            AGENTS_PAGE,
            ("iTerm", [
                iterm("PaneCommand", "new-tab"), iterm("PaneCommand", "split-vertical"), iterm("PaneCommand", "split-horizontal"),
                iterm("LaunchAgentCommand", "claude"), iterm("LaunchAgentCommand", "codex"), iterm("PaneCommand", "clear-buffer"),
                iterm("AgentKeyCommand", "escape"), iterm("AgentKeyCommand", "shift-tab"), iterm("AgentKeyCommand", "control-c"),
            ]),
            ("Commands", [
                iterm("SavedCommandCommand", "ddev start"), iterm("SavedCommandCommand", "drush cr"), iterm("SavedCommandCommand", "theme build"),
                iterm("SavedCommandCommand", "git status"), iterm("AgentKeyCommand", "claude-clear"), iterm("AgentKeyCommand", "claude-compact"),
                iterm("AgentKeyCommand", "claude-model"), iterm("PaneCommand", "broadcast-tab"), iterm("PaneCommand", "broadcast-off"),
            ]),
        ],
    },
}

# Parameters that come from settings files; the starters are what a fresh install has.
DYNAMIC_PARAMETERS = {
    "ClaudePromptCommand": {"Explain", "Review", "Tests"},
    "CodexPromptCommand": {"Explain", "Review", "Tests"},
    "SavedCommandCommand": {"ddev start", "drush cr", "theme build", "git status"},
    "SessionSlotCommand": {str(i) for i in range(1, 17)},
}

PARAM_CALL = re.compile(r'this\.(?:Add|Menu|Link|Key|AddParameter)\(\s*"([^"]+)"')


def known_actions():
    """Maps each plugin's action class to its parameters, read from the C# sources."""
    known = {}
    for plugin_dir in (ROOT / "src").glob("*Plugin"):
        plugin = plugin_dir.name.removesuffix("Plugin")
        for source in (plugin_dir / "Actions").glob("*.cs"):
            text = source.read_text()
            if re.search(r"\babstract class\b", text):
                continue
            cls = source.stem
            params = set(PARAM_CALL.findall(text)) | DYNAMIC_PARAMETERS.get(cls, set())
            known[(plugin, cls)] = params
    return known


def check():
    known = known_actions()
    errors = []
    for name, profile in PROFILES.items():
        for page_name, keys in profile["pages"]:
            if len(keys) != KEYS_PER_PAGE:
                errors.append(f"{name}/{page_name}: {len(keys)} keys, expected {KEYS_PER_PAGE}")
            for key in filter(None, keys):
                m = re.fullmatch(r"\$([A-Za-z0-9]+)___Loupedeck\.[A-Za-z0-9]+\.Actions\.([A-Za-z0-9]+)(?:___(.+))?", key)
                if not m:
                    errors.append(f"{name}/{page_name}: malformed {key}")
                    continue
                plugin, cls, param = m.groups()
                params = known.get((plugin, cls))
                if params is None:
                    errors.append(f"{name}/{page_name}: no action class {plugin}.{cls}")
                elif param is not None and param not in params:
                    errors.append(f"{name}/{page_name}: {plugin}.{cls} has no parameter '{param}'")
                elif param is None and params:
                    errors.append(f"{name}/{page_name}: {plugin}.{cls} needs a parameter")
    return errors


def ident(*parts):
    return uuid.uuid5(NAMESPACE, "/".join(parts)).hex.upper()


def build(name, profile):
    def page(page_name, keys):
        return {
            "$type": "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutPage7, LoupedeckService",
            "name": ident(name, "page", page_name),
            "displayName": page_name,
            "description": None,
            "controls": [
                {
                    "$type": "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutControl7, LoupedeckService",
                    "controlId": i,
                    "pressAction": key,
                    "rotateAction": None,
                }
                for i, key in enumerate(keys)
            ],
        }

    workspace = ident(name, "workspace")
    return {
        "$type": "Loupedeck.Service.ApplicationProfile, LoupedeckService",
        "name": ident(name, "profile"),
        "profileFlags": "None",
        "displayName": profile["displayName"],
        "description": None,
        "deviceType": "Loupedeck70",
        "applicationName": profile["applicationName"],
        "nativePluginName": profile["plugin"],
        "hasNativePlugin": True,
        "additionalNativePluginNames": ["AgentHub"],
        "profileSettings": {"$type": "Loupedeck.DictionaryNoCase`1[[System.String, System.Private.CoreLib]], PluginApi"},
        "actionImages90": None,
        "actionImages60": None,
        "wheelImages": None,
        "actionColors": None,
        "macroCommands": [],
        "macroAdjustments": [],
        "profileCommands": [],
        "profileAdjustments": [],
        "packageName": None,
        "packageVersion": None,
        "profileActions": [],
        "layout": {
            "$type": "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayout7, LoupedeckService",
            "deviceType": "Loupedeck70",
            "profileFlags": "None",
            "layoutModes": [{
                "$type": "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutMode7, LoupedeckService",
                "deviceType": "Loupedeck70",
                "modeName": "main",
                "parentModeName": None,
                "actions": None,
                "dynamicButtonPages": None,
                "dynamicEncoderPages": None,
                "workspaces": [{
                    "$type": "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutWorkspace7, LoupedeckService",
                    "name": workspace,
                    "displayName": "Workspace 1",
                    "description": None,
                    "pressPages": [page(n, k) for n, k in profile["pages"]],
                    "rotatePages": [],
                }],
                "homeWorkspaceName": workspace,
            }],
            "folderPages": [],
        },
    }


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "check"
    errors = check()
    for error in errors:
        print(f"error: {error}")
    if errors:
        return 1
    print(f"ok: {sum(len(p['pages']) for p in PROFILES.values())} pages across {len(PROFILES)} profiles")
    if mode == "write":
        for name, profile in PROFILES.items():
            path = OUT / name / "ProfileInfo.json"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(build(name, profile), indent=2) + "\n")
            print(path.relative_to(ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
