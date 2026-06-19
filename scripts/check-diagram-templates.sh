#!/usr/bin/env bash
# Smoke test: render every diagram-design template and fail if any doesn't render
# cleanly. The templates are the skill's backbone — a mermaid/D2 upgrade could
# silently break one, and this catches it. Skips renderers whose tool is absent.
#
# Usage:  ./scripts/check-diagram-templates.sh
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TPL="$ROOT/.claude/skills/diagram-design/templates"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

have() { command -v "$1" >/dev/null 2>&1; }
fail=0

for f in "$TPL"/*.mmd "$TPL"/*.d2; do
    name="$(basename "$f")"
    case "$f" in
        *.d2)  have d2   || { echo "skip  $name (d2 not installed)";   continue; } ;;
        *.mmd) have mmdc || { echo "skip  $name (mmdc not installed)"; continue; } ;;
    esac

    # For D2 templates, also verify icon URLs resolve (D2 icons fail silently).
    if [ "${f##*.}" = "d2" ] && ! "$ROOT/scripts/glimpse" --check-icons "$f" >/dev/null 2>&1; then
        echo "FAIL  $name (broken icon URL — run scripts/glimpse --check-icons $f)"
        fail=1
        continue
    fi

    out="$("$ROOT/scripts/glimpse" "$f" --name "check-${name//./-}" --out "$TMP" --no-manifest 2>&1 || true)"
    status="$(printf '%s\n' "$out" | sed -n 's/^Status:[[:space:]]*\([a-z]*\).*/\1/p')"
    warn="$(printf '%s\n' "$out" | sed -n 's/^Warnings:[[:space:]]*//p')"

    if [ "$status" = "ok" ] && [ -z "$warn" ]; then
        echo "ok    $name"
    else
        echo "FAIL  $name (status=${status:-none})"
        [ -n "$warn" ] && echo "      warnings: $warn"
        fail=1
    fi
done

if [ "$fail" -eq 0 ]; then
    echo "All diagram-design templates render cleanly."
else
    echo "Some templates failed to render cleanly." >&2
    exit 1
fi
