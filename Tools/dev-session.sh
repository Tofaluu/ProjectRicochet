#!/usr/bin/env bash
# A private test setup on this PC: the game server + the local browser build (Builds/WebGL, from
# Tools/build-web.sh). The cloud server and its lobbies are untouched. Ctrl+C stops both.
# Extra args go to the server, e.g. Tools/dev-session.sh --Game:CountdownSeconds=1 --Game:MaxMinMoves=20
set -e
cd "$(dirname "$0")/.."

python Tools/serve-web.py 8080 &
WEB=$!
trap 'kill $WEB 2>/dev/null' EXIT

echo
echo "Open: http://localhost:8080/?server=http://localhost:5022&lobby=DEV"
echo

dotnet run --project BackendServer --launch-profile http -- "$@"
