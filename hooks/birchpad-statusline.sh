#!/bin/sh
# Birchpad status line: shows "model · folder · context" and sends the same JSON to Agent Hub.
# Claude Code prints stdout as the status bar, so the Agent Hub post never writes to it.
# Usage: birchpad-statusline.sh [port]
port="${1:-8766}"
input=$(cat)
printf '%s' "$input" | curl -s --connect-timeout 0.3 -m 1 -H 'Content-Type: application/json' \
  --data-binary @- "http://127.0.0.1:$port/claude/statusline" >/dev/null 2>&1 &
printf '%s' "$input" | /usr/bin/jq -r '
  [ .model.display_name,
    (.workspace.current_dir // .cwd // "" | split("/") | last),
    (if .context_window.used_percentage then "\(.context_window.used_percentage | floor)% context" else empty end)
  ] | map(select(. != null and . != "")) | join(" · ")' 2>/dev/null
exit 0
