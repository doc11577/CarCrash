#!/usr/bin/env bash
# Point tools/embed.html at a commit, and prove jsDelivr can actually serve it.
#
#   bash tools/pin.sh            # pin to HEAD
#   bash tools/pin.sh <hash>     # pin to a specific commit
#
# This exists because updating BUILD_BASE is the step that gets missed, and it fails
# SILENTLY: the old build loads perfectly, so the page looks fine and is simply the wrong
# game. It cost a whole release on 2026-08-30. Doing it by hand means copying a 40-character
# hash into an HTML file, which is exactly the kind of task to hand to a script.
#
# Pinning to a BRANCH instead would avoid all this and is wrong: jsDelivr caches branch URLs
# hard and will serve a stale build for hours with no way to bust it.
set -euo pipefail

REPO="doc11577/CarCrash"
EMBED="tools/embed.html"
HASH="${1:-$(git rev-parse HEAD)}"

[ -f "$EMBED" ] || { echo "ERROR: no $EMBED -- run from the repo root." >&2; exit 1; }

# Expand a short hash, and fail on one that does not exist at all.
HASH=$(git rev-parse "$HASH^{commit}") || { echo "ERROR: no such commit." >&2; exit 1; }

# jsDelivr can only serve what GitHub has. A hash taken before `git push` points at a commit
# the CDN cannot fetch, and the symptom is a game stuck at 0% rather than an error.
URL="https://cdn.jsdelivr.net/gh/$REPO@$HASH/prod/carcrash.loader.js"
CODE=$(curl -s -o /dev/null -w "%{http_code}" -r 0-0 "$URL" || echo 000)

if [ "$CODE" != "200" ] && [ "$CODE" != "206" ]; then
  echo "ERROR: jsDelivr cannot serve $HASH yet (HTTP $CODE)." >&2
  echo "       $URL" >&2
  echo "       Push first, then run this again. Nothing was changed." >&2
  exit 1
fi

OLD=$(grep -oE 'CarCrash@[0-9a-f]{40}' "$EMBED" | head -1 | cut -d@ -f2 || true)
sed -i -E "s|CarCrash@[0-9a-f]{40}|CarCrash@$HASH|g" "$EMBED"

echo "jsDelivr has it (HTTP $CODE)."
echo "  was: ${OLD:-<none>}"
echo "  now: $HASH"
echo
echo "Now re-paste $EMBED into Google Sites > Insert > Embed > Embed code."
echo "Nothing on the live site changes until you do."
