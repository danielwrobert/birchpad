#!/bin/sh
# Builds each plugin in Release and packs it into dist/<Name>.lplug4, then verifies the package.
# Install one with: logiplugintool install dist/<Name>.lplug4
set -eu

root=$(cd "$(dirname "$0")/.." && pwd)
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
export PATH="/opt/homebrew/opt/dotnet/bin:$PATH:$HOME/.dotnet/tools"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# LogiPluginTool is built for .NET 8; let it run on the installed .NET 10.
export DOTNET_ROLL_FORWARD=Major

mkdir -p "$root/dist"
dotnet test "$root/tests/DevDeck.Core.Tests" --nologo -v quiet

for plugin in AgentHub ITerm Codex Claude; do
  project="$root/src/${plugin}Plugin"
  output="$project/bin/Release"
  rm -rf "$output"
  dotnet build "$project" -c Release --nologo -v quiet
  logiplugintool pack "$output/" "$root/dist/$plugin.lplug4"
  logiplugintool verify "$root/dist/$plugin.lplug4"
done

ls -l "$root/dist"
