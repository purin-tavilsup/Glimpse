#!/usr/bin/env bash
# Install the Glimpse plugin (CLI + skills) for use in any repo on this machine.
# Symlinks ~/.claude/skills/glimpse -> <repo>/plugin (repo stays canonical, edits live).
# Usage:  ./scripts/install.sh            install/refresh
#         ./scripts/install.sh --uninstall
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
plugin="$repo/plugin"
link="$HOME/.claude/skills/glimpse"

if [ "${1:-}" = "--uninstall" ]; then
    if [ -L "$link" ]; then rm "$link"; echo "Removed $link"; else echo "No symlink at $link"; fi
    exit 0
fi

command -v dotnet >/dev/null 2>&1 || { echo "dotnet not found — install the .NET 10 SDK first." >&2; exit 1; }

echo "Building Glimpse.Capture..."
dotnet build "$repo/tools/Glimpse.Capture/Glimpse.Capture.csproj" -v quiet
chmod +x "$plugin/bin/glimpse"

mkdir -p "$HOME/.claude/skills"
if [ -e "$link" ] && [ ! -L "$link" ]; then
    echo "REFUSE: a real (non-symlink) entry exists at $link — remove it manually, then re-run." >&2
    exit 1
fi
rm -f "$link"
ln -s "$plugin" "$link"
echo "Linked $link -> $plugin"
echo
echo "Done. Restart Claude Code (or run /reload-plugins) to load the 'glimpse' plugin."
echo "Verify (in a NEW session):  claude plugin list | grep glimpse   and   which glimpse"
