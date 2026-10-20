#!/bin/bash
# Runs the automated online check as two processes on this Mac (host + client over 127.0.0.1 / UDP 7777).
# Needs the macOS development player: Builds/Mac/DEADLOCK.app (see docs/DEVELOPMENT.md).
# Usage: Tools/net-check-local.sh [--build]
set -u
cd "$(dirname "$0")/.."
UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
APP=Builds/Mac/DEADLOCK.app
OUT="$PWD/Logs/netcheck"
mkdir -p "$OUT"

if [ "${1:-}" = "--build" ] || [ ! -d "$APP" ]; then
  echo "== building $APP (Unity Editor must be closed)"
  "$UNITY" -batchmode -quit -projectPath "$PWD" -buildTarget OSXUniversal \
    -executeMethod Deadlock.Editor.MacStandaloneBuilder.BuildFromCommandLine -logFile "$OUT/build.log" || { echo "build failed: $OUT/build.log"; exit 1; }
fi
BIN=$(ls "$APP"/Contents/MacOS/* | head -1)

rm -f "$OUT/host.log" "$OUT/client.log"
"$BIN" -batchmode -nographics -netCheck host -logFile "$OUT/host.log" &
HOST=$!
sleep 3
"$BIN" -batchmode -nographics -netCheck client -netHost 127.0.0.1 -logFile "$OUT/client.log" &
CLIENT=$!
wait $HOST; HOST_EXIT=$?
wait $CLIENT; CLIENT_EXIT=$?

grep -h "NETCHECK \(PASS\|FAIL\|RESULT\)" "$OUT/host.log" "$OUT/client.log"
echo "host exit=$HOST_EXIT client exit=$CLIENT_EXIT (logs: $OUT/host.log, $OUT/client.log)"
[ $HOST_EXIT -eq 0 ] && [ $CLIENT_EXIT -eq 0 ]
