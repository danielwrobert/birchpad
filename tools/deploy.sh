#!/bin/sh
# Builds the given plugins (default: all) in Release and copies them over their installed copies, then reloads them.
# The plugins must already be installed once from their .lplug4 (tools/package.sh, then open the file).
# Usage: tools/deploy.sh [BirchpadAgentHub BirchpadITerm BirchpadCodex BirchpadClaude]
set -eu

root=$(cd "$(dirname "$0")/.." && pwd)
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
export PATH="/opt/homebrew/opt/dotnet/bin:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
installed="$HOME/Library/Application Support/Logi/LogiPluginService/Plugins"

python3 "$root/tools/profiles.py" write >/dev/null
for plugin in "${@:-BirchpadAgentHub BirchpadITerm BirchpadCodex BirchpadClaude}"; do
  for name in $plugin; do
    target="$installed/$name"
    if [ ! -d "$target" ]; then
      echo "skip $name: not installed; open dist/$name.lplug4 once first"
      continue
    fi
    dotnet build "$root/src/${name}Plugin" -c Release --nologo -v quiet
    cp "$root/src/${name}Plugin/bin/Release/bin/"*.dll "$target/bin/"
    if [ -d "$root/src/${name}Plugin/bin/Release/profiles" ]; then
      mkdir -p "$target/profiles"
      cp "$root/src/${name}Plugin/bin/Release/profiles/"* "$target/profiles/"
    fi
    open "loupedeck:plugin/$name/reload"
    echo "deployed $name"
    sleep 3
  done
done
