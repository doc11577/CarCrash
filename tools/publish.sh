#!/usr/bin/env bash
# Copies the Unity Web build payload into prod/ for jsDelivr hosting.
# Run from the repo root after every Web build.
#   Build output must be at: WebBuild/carcrash/
set -euo pipefail

SRC="WebBuild/carcrash/Build"
DST="prod"

if [ ! -d "$SRC" ]; then
  echo "ERROR: no build at $SRC" >&2
  echo "Build to C:\Users\ethan\Documents\GitHub\CarCrash\WebBuild\carcrash first." >&2
  exit 1
fi

# Refuse to publish a build that is older than the project it is supposed to contain.
# publish.sh only copies, so it will happily ship last week's build and say "Copied to prod"
# -- which is exactly how the 2026-08-30 release put a three-day-old smoke test on the site
# and reported success. A stale build loads perfectly; it is just the wrong game.
NEWEST_SRC=$(find Assets ProjectSettings -type f \
               \( -name '*.cs' -o -name '*.unity' -o -name '*.prefab' -o -name '*.asset' \
                  -o -name '*.mat' -o -name '*.fbx' -o -name '*.png' -o -name '*.jpg' \) \
               -newer "$SRC/carcrash.loader.js" -print -quit 2>/dev/null || true)

if [ -n "$NEWEST_SRC" ]; then
  echo "ERROR: the build at $SRC is OLDER than your project files." >&2
  echo "       e.g. $NEWEST_SRC changed after the build was written." >&2
  echo "       Build again in Unity (File > Build Profiles > Build) before publishing," >&2
  echo "       or pass --force if you really mean to ship this one." >&2
  [ "${1:-}" = "--force" ] || exit 1
  echo "       --force given, continuing anyway." >&2
fi

mkdir -p "$DST"
cp "$SRC"/carcrash.data.unityweb \
   "$SRC"/carcrash.framework.js.unityweb \
   "$SRC"/carcrash.loader.js \
   "$SRC"/carcrash.wasm.unityweb \
   "$DST"/

echo "Copied to $DST:"
ls -la "$DST"
echo
echo "Now: git add prod && git commit && git push"
echo "Then update the commit hash in the Google Sites embed (tools/embed.html)."
