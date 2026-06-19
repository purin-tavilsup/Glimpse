#!/usr/bin/env bash
# Verify every `icon:` URL in a D2 file resolves (HTTP 200).
#
# Why this exists: D2 icons fail SILENTLY — a wrong/dead URL renders nothing, with no
# error and no missing-image placeholder. The diagram just quietly loses a logo, and the
# render still "succeeds". This deterministic check (200 or not) catches that before you
# trust the diagram. Run it after writing/editing a .d2 that uses icons, before rendering.
#
# Usage:  ./scripts/check-d2-icons.sh <file.d2>
# Exit:   0 all icons resolve (or none present) · 1 one or more broken · 2 bad usage
set -uo pipefail

file="${1:-}"
[ -n "$file" ] || { echo "usage: check-d2-icons.sh <file.d2>" >&2; exit 2; }
[ -f "$file" ] || { echo "not found: $file" >&2; exit 2; }

urls="$(grep -oE 'icon:[[:space:]]*https?://[^[:space:]]+' "$file" | sed -E 's/^icon:[[:space:]]*//')"
if [ -z "$urls" ]; then
    echo "No icon: URLs in $file (nothing to check)."
    exit 0
fi

fail=0
while IFS= read -r url; do
    [ -n "$url" ] || continue
    code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$url" || echo 000)"
    if [ "$code" = "200" ]; then
        echo "ok   $code  $url"
    else
        echo "BAD  $code  $url"
        fail=1
    fi
done <<< "$urls"

if [ "$fail" -eq 0 ]; then
    echo "All icon URLs resolve."
else
    echo "Broken icon URL(s) — fix them or those icons will silently vanish from the render." >&2
    exit 1
fi
