#!/usr/bin/env bash
# A private test setup on this PC: the game server + the local browser build (Builds/WebGL, from
# Tools/build-web.sh). The cloud server and its lobbies are untouched. Ctrl+C stops both.
# Extra args go to the server, e.g. Tools/dev-session.sh --Game:CountdownSeconds=1 --Game:MaxMinMoves=20
# --lan (first) also lets phones and tablets on the same Wi-Fi in, for testing the phone/tablet layouts on a real device
# (Windows may ask to allow Python and dotnet through the firewall: allow on private networks).
set -e
cd "$(dirname "$0")/.."

HOST=localhost
URLS=()
if [ "$1" = "--lan" ]; then
    shift
    HOST=$(powershell -NoProfile -Command "(Get-NetIPAddress -InterfaceAlias 'Wi-Fi' -AddressFamily IPv4 -ErrorAction SilentlyContinue).IPAddress" | tr -d '')
    [ -n "$HOST" ] || { echo "No Wi-Fi IPv4 address found"; exit 1; }
    python Tools/serve-web.py 8080 0.0.0.0 &
    URLS=(--urls "http://0.0.0.0:5022")
else
    python Tools/serve-web.py 8080 &
fi
WEB=$!
trap 'kill $WEB 2>/dev/null' EXIT

echo
echo "Open: http://$HOST:8080/?server=http://$HOST:5022&lobby=DEV"
echo

dotnet run --project BackendServer --launch-profile http -- "${URLS[@]}" "$@"
