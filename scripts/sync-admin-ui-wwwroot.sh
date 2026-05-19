#!/usr/bin/env bash
# Build web/admin-ui and copy output into EngineIQ.Admin/wwwroot/admin (same layout Docker uses).
# Run from repo root or anywhere — keeps dotnet run / IDE launches aligned with Dockerfile.admin.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UI="$ROOT/web/admin-ui"
DEST="$ROOT/src/EngineIQ.Admin/wwwroot/admin"

if [[ ! -f "$UI/package.json" ]]; then
  echo "Expected $UI/package.json — are you in the EngineIQ repo?"
  exit 1
fi

cd "$UI"
npm ci
npm run build

rm -rf "$DEST"
mkdir -p "$DEST"
cp -r "$UI/dist/." "$DEST/"

echo "OK — admin SPA synced to $DEST (matches docker/Dockerfile.admin wwwroot layout)."
