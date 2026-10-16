#!/usr/bin/env bash
# Install the Glimpse plugins (CLIs + skills) for use in any repo on this machine.
# Symlinks ~/.claude/skills/<plugin> -> <repo>/<plugin folder> (repo stays canonical, edits live).
# Usage:  ./scripts/install.sh            install/refresh
#         ./scripts/install.sh --uninstall
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# name | plugin folder | project its wrapper runs | framework to build (empty = the project's only one).
# The desktop driver multi-targets; its net10.0 build is the one the bash wrapper runs.
plugins=(
    "glimpse|plugin|tools/Glimpse.Capture/Glimpse.Capture.csproj|"
    "glimpse-drive|drive-plugin|tools/Glimpse.Drive/Glimpse.Drive.csproj|net10.0"
)

if [ "${1:-}" = "--uninstall" ]; then
    for entry in "${plugins[@]}"; do
        IFS='|' read -r name _ _ _ <<< "$entry"
        link="$HOME/.claude/skills/$name"
        if [ -L "$link" ]; then rm "$link"; echo "Removed $link"; else echo "No symlink at $link"; fi
    done
    exit 0
fi

command -v dotnet >/dev/null 2>&1 || { echo "dotnet not found — install the .NET 10 SDK first." >&2; exit 1; }

mkdir -p "$HOME/.claude/skills"
for entry in "${plugins[@]}"; do
    IFS='|' read -r name folder project framework <<< "$entry"
    link="$HOME/.claude/skills/$name"

    echo "Building $(basename "$project" .csproj)..."
    dotnet build "$repo/$project" ${framework:+-f "$framework"} -v quiet
    chmod +x "$repo/$folder/bin/$name"

    if [ -e "$link" ] && [ ! -L "$link" ]; then
        echo "REFUSE: a real (non-symlink) entry exists at $link — remove it manually, then re-run." >&2
        exit 1
    fi
    rm -f "$link"
    ln -s "$repo/$folder" "$link"
    echo "Linked $link -> $repo/$folder"
done

echo
echo "Done. Restart Claude Code (or run /reload-plugins) to load the 'glimpse' and 'glimpse-drive' plugins."
echo "Verify (in a NEW session):  claude plugin list   and   which glimpse glimpse-drive"
