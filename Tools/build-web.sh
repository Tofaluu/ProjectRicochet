#!/usr/bin/env bash
# Builds the browser version into Builds/WebGL with Unity in batch mode (close the Editor first).
# The project stays on the WebGL platform: switching platforms re-imports every asset (minutes), and
# the Editor plays the same on it. A few minutes (IL2CPP + WebAssembly). Then Tools/deploy-web.sh.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/c/Program Files/Unity/Hub/Editor/6000.6.5f1/Editor/Unity.exe}"
LOG="$ROOT/Builds/build-web.log"
mkdir -p "$ROOT/Builds"

echo "Building (log: $LOG)..."
if ! "$UNITY" -batchmode -quit -projectPath "$ROOT/UnityClient" -buildTarget WebGL \
     -executeMethod Ricochet.Editor.BuildScripts.WebGL -logFile "$LOG"; then
  grep -E "error|\[Build\]" "$LOG" | tail -20
  exit 1
fi
grep "\[Build\]" "$LOG"
echo "Built $ROOT/Builds/WebGL"
