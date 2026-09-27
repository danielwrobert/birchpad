#!/bin/sh
# Birchpad: forwards one Codex hook payload (stdin) to Agent Hub.
# Usage: birchpad-codex-hook.sh hook|permission [port]
# "hook" never prints, so Codex always carries on.
# "permission" prints Agent Hub's decision, which is empty when nobody answered on the keypad.
route="${1:-hook}"
port="${2:-8766}"
url="http://127.0.0.1:$port/codex/$route"
if [ "$route" = "permission" ]; then
  curl -s --connect-timeout 0.5 -m 330 -X POST -H 'Content-Type: application/json' --data-binary @- "$url" 2>/dev/null
else
  curl -s --connect-timeout 0.5 -m 3 -X POST -H 'Content-Type: application/json' --data-binary @- "$url" >/dev/null 2>&1
fi
exit 0
