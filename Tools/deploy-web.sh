#!/usr/bin/env bash
# Uploads the browser build (Builds/WebGL, see Tools/build-web.sh) to the cloud server, where Caddy
# serves it at https://ricochet.thomasliu.dev. Run from Git Bash (Windows) or a Mac/Linux shell.
#   Tools/deploy-web.sh [path to the server's SSH key, default ~/.ssh/ricochet.pem]
set -euo pipefail

KEY="${1:-$HOME/.ssh/ricochet.pem}"
HOST="ubuntu@ricochet.thomasliu.dev"
BUILD="$(cd "$(dirname "$0")/.." && pwd)/Builds/WebGL"

[ -f "$BUILD/index.html" ] || { echo "No browser build in $BUILD (run Tools/build-web.sh first)"; exit 1; }
[ -f "$KEY" ] || { echo "SSH key not found: $KEY"; exit 1; }

echo "Uploading $(du -sh "$BUILD" | cut -f1) to $HOST..."
# Unpack next to the live folder, then swap the contents in (Caddy mounts ~/ricochet/web itself).
tar -C "$BUILD" -czf - . | ssh -i "$KEY" "$HOST" '
  set -e
  rm -rf ~/ricochet/web.new && mkdir -p ~/ricochet/web.new ~/ricochet/web
  tar -xzf - -C ~/ricochet/web.new
  find ~/ricochet/web -mindepth 1 -delete
  cp -a ~/ricochet/web.new/. ~/ricochet/web/
  rm -rf ~/ricochet/web.new'
echo "Done: https://ricochet.thomasliu.dev"
