#!/usr/bin/env python3
"""Generate the Birchpad default keypad profiles.

Each app plugin gets one home page from docs/workflow-design.md. Every key is a
category that opens a folder listing its items at run time; no key is bound to a
specific session or item.

The .lp5 archive copies the layout of a profile exported from Options+
(profiles/exported/): ProfileInfo.json, ApplicationInfo.json, ApplicationIcon.png,
and metadata/LoupedeckPackage.yaml, AdvancedInfo.json, and ProfilePreview.json.

  profiles.py check   verify every placed key names an action or folder that exists
  profiles.py write   check, then write the .lp5 files:
                      src/<Plugin>Plugin/package/profiles/DefaultProfile70mac.lp5
                      profiles/generated/<Display name>.lp5 (for Import in Options+)
"""

import base64
import datetime
import io
import json
import re
import sys
import uuid
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "profiles" / "generated"
KEYS_PER_PAGE = 9
FONT = "/System/Library/Fonts/Helvetica.ttc"

# Written into every profile, in the form the Options+ export uses.
NOW = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f0Z")

# Package versions use four parts, as in the Options+ export.
PACKAGE_VERSION = "1.0.0.1"

# Stable IDs so regenerating a profile does not look like a new one.
NAMESPACE = uuid.UUID("5b0f6a52-8f5e-4f8e-9a57-0d3c6f1e2a10")


def command(plugin, cls, param=None):
    ident = f"${plugin}___Loupedeck.{plugin}Plugin.Actions.{cls}"
    return f"{ident}___{param}" if param is not None else ident


def folder(plugin, cls):
    # The form Options+ stores for a dynamic folder key, taken from an exported profile.
    return f"${plugin}___#DynamicFolder___DynamicFolder#Loupedeck.{plugin}Plugin.Actions.{cls}"


SCHEDULED = folder("BirchpadAgentHub", "ScheduledFolder")

# Keys run top row first; key 7 (bottom left) is Next waiting on every home page.
PROFILES = {
    "claude": {
        "applicationName": "@_birchpadclaude",
        "displayName": "Birchpad Claude",
        "plugin": "BirchpadClaude",
        "bundle": "com.anthropic.claudefordesktop",
        "keys": [
            (folder("BirchpadAgentHub", "ClaudeSessionsFolder"), "Sessions"), (folder("BirchpadClaude", "ClaudeNewFolder"), "New"), (folder("BirchpadClaude", "ClaudePromptsFolder"), "Prompts"),
            (folder("BirchpadClaude", "ClaudeChatsFolder"), "Chats"), (folder("BirchpadClaude", "ClaudeProjectsFolder"), "Projects"), (SCHEDULED, "Scheduled"),
            (command("BirchpadAgentHub", "NextWaitingCommand", "claude"), "Next waiting"), (folder("BirchpadAgentHub", "ClaudeUsageFolder"), "Usage"), (folder("BirchpadClaude", "ClaudeSettingsFolder"), "Settings"),
        ],
    },
    "codex": {
        "applicationName": "@_birchpadcodex",
        "displayName": "Birchpad Codex",
        "plugin": "BirchpadCodex",
        "bundle": "com.openai.codex",
        "keys": [
            (folder("BirchpadAgentHub", "CodexSessionsFolder"), "Sessions"), (folder("BirchpadCodex", "CodexNewFolder"), "New"), (folder("BirchpadCodex", "CodexPromptsFolder"), "Prompts"),
            (folder("BirchpadCodex", "CodexChatsFolder"), "Chats"), (folder("BirchpadCodex", "CodexProjectsFolder"), "Projects"), (folder("BirchpadCodex", "CodexScheduledFolder"), "Scheduled"),
            (command("BirchpadAgentHub", "NextWaitingCommand", "codex"), "Next waiting"), (folder("BirchpadAgentHub", "CodexUsageFolder"), "Usage"), (folder("BirchpadCodex", "CodexSettingsFolder"), "Settings"),
        ],
    },
    "iterm": {
        "applicationName": "@_birchpaditerm",
        "displayName": "Birchpad iTerm",
        "plugin": "BirchpadITerm",
        "bundle": "com.googlecode.iterm2",
        "extraPlugins": ["BirchpadHerdr"],
        "keys": [
            (folder("BirchpadITerm", "ITermTabsFolder"), "Tabs"), (command("BirchpadITerm", "ITermNewTabCommand"), "New tab"), (folder("BirchpadITerm", "ITermCommandsFolder"), "Commands"),
            (folder("BirchpadITerm", "ITermSlashCommandsFolder"), "Slash commands"), (folder("BirchpadHerdr", "HerdrHomeFolder"), "Herdr"), (folder("BirchpadITerm", "ITermAgentKeysFolder"), "Agent keys"),
            (command("BirchpadAgentHub", "NextWaitingCommand", "all"), "Next waiting"), (folder("BirchpadAgentHub", "AllUsageFolder"), "Usage"), (folder("BirchpadITerm", "ITermSettingsFolder"), "Settings"),
        ],
    },
}

# General profiles belong to Options+'s General app (@_defaultmac), not to one app's plugin.
# With Adapt to App off, the keypad stays on the one you pick; they are imported by hand, not shipped in a package.
GENERAL = {
    "agents": {
        "applicationName": "@_defaultmac",
        "displayName": "Birchpad Agents",
        "plugin": "BirchpadAgentHub",
        "general": True,
        "extraPlugins": ["BirchpadHerdr"],
        "keys": [
            (folder("BirchpadAgentHub", "CodingSessionsFolder"), "Coding sessions"), (folder("BirchpadAgentHub", "ClaudeTasksFolder"), "Claude chats and tasks"), (folder("BirchpadAgentHub", "ChatGptTasksFolder"), "ChatGPT chats and tasks"),
            (folder("BirchpadAgentHub", "AgentsNewFolder"), "New"), (folder("BirchpadHerdr", "HerdrWorkspacesFolder"), "Workspaces"), (SCHEDULED, "Scheduled"),
            (command("BirchpadAgentHub", "NextWaitingCommand", "all"), "Next waiting"), (folder("BirchpadAgentHub", "AllUsageFolder"), "Usage"), (folder("BirchpadAgentHub", "AgentsProjectsFolder"), "Projects"),
        ],
    },
}

# The General app's own entry, as Options+ stores it; a General profile's package must not change it.
GENERAL_APP = {"displayName": "System plugin", "nativePluginName": "DefaultMac", "bundle": None, "mode": ("System", "System")}

CLASS_DECL = re.compile(r"public (?:abstract )?class (\w+)\s*:\s*(\w+)")
ABSTRACT_DECL = re.compile(r"public abstract class (\w+)")


PARAM_DECL = re.compile(r'AddParameter\(\s*"([^"]+)"')


def known_parameters():
    """Maps each class to the parameters its source file declares with AddParameter."""
    params = {}
    for source in (ROOT / "src").rglob("*.cs"):
        if "/bin/" in str(source) or "/obj/" in str(source):
            continue
        text = source.read_text()
        declared = set(PARAM_DECL.findall(text))
        for cls, _ in CLASS_DECL.findall(text):
            params[cls] = declared
    return params


def known_actions():
    """Maps (plugin, class) to 'command' or 'folder', read from the C# sources and src/Shared."""
    bases, abstract, owner = {}, set(), {}
    for source in (ROOT / "src").rglob("*.cs"):
        if "/bin/" in str(source) or "/obj/" in str(source):
            continue
        text = source.read_text()
        plugin = next((p.name.removesuffix("Plugin") for p in source.parents if p.name.endswith("Plugin") and p.parent.name == "src"), None)
        for cls, base in CLASS_DECL.findall(text):
            bases[cls] = base
            owner[cls] = plugin
        abstract.update(ABSTRACT_DECL.findall(text))

    def root(cls):
        seen = set()
        while cls in bases and cls not in seen:
            seen.add(cls)
            if bases[cls] in ("MenuFolder", "PluginDynamicFolder"):
                return "folder"
            if bases[cls] in ("PluginDynamicCommand", "HubCommand"):
                return "command"
            cls = bases[cls]
        return None

    return {(owner[c], c): root(c) for c in bases if owner.get(c) and c not in abstract and root(c)}


ID_PATTERN = re.compile(r"\$([A-Za-z0-9]+)___(#DynamicFolder___DynamicFolder#)?Loupedeck\.[A-Za-z0-9]+\.Actions\.([A-Za-z0-9]+)(?:___(.+))?")


def check():
    known = known_actions()
    parameters = known_parameters()
    errors = []
    for name, profile in {**PROFILES, **GENERAL}.items():
        if len(profile["keys"]) != KEYS_PER_PAGE:
            errors.append(f"{name}: {len(profile['keys'])} keys, expected {KEYS_PER_PAGE}")
        for action, _ in profile["keys"]:
            if action is None:
                continue
            m = ID_PATTERN.fullmatch(action)
            if not m:
                errors.append(f"{name}: malformed {action}")
                continue
            plugin, is_folder, cls, param = m.groups()
            kind = known.get((plugin, cls))
            expected = "folder" if is_folder else "command"
            if kind is None:
                errors.append(f"{name}: no action class {plugin}.{cls}")
            elif kind != expected:
                errors.append(f"{name}: {plugin}.{cls} is a {kind}, placed as a {expected}")
            elif param is not None and param not in parameters.get(cls, set()):
                errors.append(f"{name}: {plugin}.{cls} has no parameter '{param}'")
            elif param is None and parameters.get(cls) and kind == "command":
                errors.append(f"{name}: {plugin}.{cls} needs a parameter")
    return errors


def ident(*parts):
    return uuid.uuid5(NAMESPACE, "/".join(parts)).hex.upper()


def profile_info(name, profile):
    workspace = ident(name, "workspace")
    return {
        "$type": "Loupedeck.Service.ApplicationProfile, LoupedeckService",
        "name": ident(name, "profile"),
        "profileFlags": "None",
        "displayName": profile["displayName"],
        "description": None,
        "deviceType": "Loupedeck70",
        "applicationName": profile["applicationName"],
        "nativePluginName": GENERAL_APP["nativePluginName"] if profile.get("general") else profile["plugin"],
        "hasNativePlugin": True,
        "additionalNativePluginNames": plugins_for(profile) if profile.get("general") else ["DefaultMac"],
        "lastModifiedTimeUtc": NOW,
        "profileSettings": {"$type": "Loupedeck.DictionaryNoCase`1[[System.String, System.Private.CoreLib]], PluginApi"},
        "actionImages90": None,
        "actionImages60": None,
        "wheelImages": None,
        "actionColors": None,
        "macroCommands": [],
        "macroAdjustments": [],
        "profileCommands": [],
        "profileAdjustments": [],
        # Same service version as the Options+ export; without it the service may treat the profile as an old format to convert.
        "conversionHistory": f"{NOW} bj 6.4.1.3246\r\n",
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
                "modeName": GENERAL_APP["mode"][0] if profile.get("general") else "main",
                "parentModeName": None,
                "actions": None,
                "dynamicButtonPages": None,
                "dynamicEncoderPages": None,
                "workspaces": [{
                    "$type": "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutWorkspace7, LoupedeckService",
                    "name": workspace,
                    "displayName": "Workspace 1",
                    "description": None,
                    "pressPages": [{
                        "$type": "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutPage7, LoupedeckService",
                        "name": ident(name, "page", "home"),
                        "displayName": "Home",
                        "description": None,
                        "controls": [
                            {
                                "$type": "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutControl7, LoupedeckService",
                                "controlId": i,
                                "pressAction": action,
                                "rotateAction": None,
                            }
                            for i, (action, _) in enumerate(profile["keys"])
                        ],
                    }],
                    "rotatePages": [],
                }],
                "homeWorkspaceName": workspace,
            }],
            "folderPages": [],
        },
    }


def application_info(profile, profile_id):
    general = profile.get("general")
    mode, mode_label = GENERAL_APP["mode"] if general else ("main", "Main")
    return {
        "$type": "Loupedeck.Service.SupportedApplicationInfo, LoupedeckService",
        "name": profile["applicationName"],
        "displayName": GENERAL_APP["displayName"] if general else profile["displayName"],
        "description": None,
        "deviceType": "Loupedeck70",
        "nativePluginName": GENERAL_APP["nativePluginName"] if general else profile["plugin"],
        "hasNativePlugin": True,
        "processOrBundleName": GENERAL_APP["bundle"] if general else profile["bundle"],
        "modes": [{"$type": "Loupedeck.Service.ApplicationMode, LoupedeckService", "name": mode, "parentModeName": None, "displayName": mode_label}],
        "defaultProfileName": profile_id,
        "isEnabled": True,
    }


def preview_tile(label):
    img = Image.new("RGB", (80, 80), (38, 38, 38))
    draw = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, 13)
    words, lines, line = (label or "").split(), [], ""
    for word in words:
        trial = f"{line} {word}".strip()
        if draw.textlength(trial, font=font) > 72 and line:
            lines.append(line)
            line = word
        else:
            line = trial
    if line:
        lines.append(line)
    y = 40 - (len(lines) * 16) // 2
    for text in lines:
        draw.text((40 - draw.textlength(text, font=font) / 2, y), text, font=font, fill=(255, 255, 255))
        y += 16
    buf = io.BytesIO()
    img.save(buf, "PNG")
    return base64.b64encode(buf.getvalue()).decode()


def preview(profile):
    return {
        "buttonPages": [
            {"controlId": i, "actionName": action, "displayName": label, "description": label, "image": preview_tile(label)}
            for i, (action, label) in enumerate(profile["keys"])
            if action is not None
        ],
        "encoderPages": [],
    }


def plugins_for(profile):
    return list(dict.fromkeys([profile["plugin"], "BirchpadAgentHub", *profile.get("extraPlugins", [])]))


def host_app(name, info, app):
    """Rebinds a profile to the host app itself, as rshankras/keypad-profiles does.

    Options+ refuses to import a profile for a plugin's own app (@_...), but imports one for a
    plain app whose plugin actions come in through additionalNativePluginNames.
    """
    info["applicationName"] = app["name"] = app["processOrBundleName"].lower()
    info["name"] = app["defaultProfileName"] = ident(name, "host-profile")
    info["displayName"] = f"{info['displayName']} (host app)"
    info["nativePluginName"] = app["nativePluginName"] = None
    info["hasNativePlugin"] = app["hasNativePlugin"] = False
    info["additionalNativePluginNames"] = ["DefaultMac", *info.pop("_plugins")]
    info["layout"]["layoutModes"][0]["workspaces"][0]["name"] = ident(name, "host-workspace")
    info["layout"]["layoutModes"][0]["homeWorkspaceName"] = ident(name, "host-workspace")
    app["displayName"] = info["displayName"]


def lp5(name, profile, host=False):
    info = profile_info(name, profile)
    app = application_info(profile, info["name"])
    if host:
        info["_plugins"] = plugins_for(profile)
        host_app(name, info, app)
    icon = ROOT / "src" / f"{profile['plugin']}Plugin" / "package" / "metadata" / "Icon256x256.png"
    package_yaml = f"type: Profile5\nname: {info['name']}\ndisplayName: {info['displayName']}\nversion: {PACKAGE_VERSION}\n"
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("ApplicationIcon.png", icon.read_bytes())
        z.writestr("ApplicationInfo.json", json.dumps(app, indent=4))
        z.writestr("ProfileInfo.json", json.dumps(info, indent=4))
        z.writestr("metadata/LoupedeckPackage.yaml", package_yaml)
        z.writestr("metadata/ProfilePreview.json", json.dumps(preview(profile), indent=4))
        z.writestr("metadata/AdvancedInfo.json", json.dumps({"additionalPluginNames": [p for p in plugins_for(profile) if host or profile.get("general") or p != profile["plugin"]]}, indent=4))
    return buf.getvalue()


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "check"
    errors = check()
    for error in errors:
        print(f"error: {error}")
    if errors:
        return 1
    print(f"ok: {len(PROFILES)} home pages, {len(GENERAL)} general")
    if mode == "write":
        OUT.mkdir(parents=True, exist_ok=True)
        for name, profile in PROFILES.items():
            data = lp5(name, profile)
            shipped = ROOT / "src" / f"{profile['plugin']}Plugin" / "package" / "profiles" / "DefaultProfile70mac.lp5"
            shipped.parent.mkdir(parents=True, exist_ok=True)
            shipped.write_bytes(data)
            (OUT / f"{profile['displayName']}.lp5").write_bytes(data)
            # A second copy bound to the host app, for Import in Options+.
            (OUT / f"{profile['displayName']} (host app).lp5").write_bytes(lp5(name, profile, host=True))
        for name, profile in GENERAL.items():
            path = OUT / f"{profile['displayName']}.lp5"
            path.write_bytes(lp5(name, profile))
            print(path.relative_to(ROOT))
            print(shipped.relative_to(ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
