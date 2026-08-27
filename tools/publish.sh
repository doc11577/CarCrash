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
