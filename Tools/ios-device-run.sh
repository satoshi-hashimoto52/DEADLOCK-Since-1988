#!/bin/zsh
# Build the prototype for a connected iOS device and launch it.
#   Tools/ios-device-run.sh <device-name-or-id> [--dev] [--skip-export]
# Steps: Unity export (batchmode) -> xcodebuild (automatic signing) -> devicectl install -> devicectl launch.
# Close the Unity Editor for this project first (batchmode cannot open a project that is already open).
set -euo pipefail
cd "$(dirname "$0")/.."

DEVICE="${1:?usage: Tools/ios-device-run.sh <device-name-or-id> [--dev] [--skip-export]}"
shift
DEV=0; SKIP_EXPORT=0
for arg in "$@"; do
  case "$arg" in
    --dev) DEV=1 ;;
    --skip-export) SKIP_EXPORT=1 ;;
  esac
done

UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
BUNDLE_ID=com.satoshihashimoto52.deadlock1988
TEAM=HZ43AT7U4F
mkdir -p Logs

if [[ $SKIP_EXPORT -eq 0 ]]; then
  EXTRA=()
  [[ $DEV -eq 1 ]] && EXTRA=(-deadlockDevelopment)
  "$UNITY" -batchmode -quit -projectPath "$PWD" -buildTarget iOS \
    -executeMethod Deadlock.Editor.IOSProjectExporter.ExportFromCommandLine "${EXTRA[@]}" \
    -logFile Logs/ios-export.log
  grep "\[DEADLOCK\] iOS export" Logs/ios-export.log
fi

# The hardware UDID xcodebuild wants, from the CoreDevice list.
DEVICES_JSON=$(mktemp)
xcrun devicectl list devices --json-output "$DEVICES_JSON" >/dev/null
UDID=$(python3 -c '
import json, sys
want = sys.argv[1]
for d in json.load(sys.stdin)["result"]["devices"]:
    names = {d.get("identifier"), d["deviceProperties"].get("name"), d["hardwareProperties"].get("udid")}
    if want in names:
        print(d["hardwareProperties"]["udid"]); break
' "$DEVICE" < "$DEVICES_JSON")
rm -f "$DEVICES_JSON"
[[ -n "$UDID" ]] || { echo "device not found: $DEVICE"; exit 1; }

xcodebuild -project Builds/iOS/Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release \
  -destination "id=$UDID" -derivedDataPath Builds/iOS-DerivedData \
  -allowProvisioningUpdates DEVELOPMENT_TEAM=$TEAM CODE_SIGN_STYLE=Automatic \
  build | tee Logs/xcodebuild.log | grep -E "error:|warning: .*sign|BUILD (SUCCEEDED|FAILED)" || true
grep -q "BUILD SUCCEEDED" Logs/xcodebuild.log || { echo "xcodebuild failed (see Logs/xcodebuild.log)"; exit 1; }

APP=$(find Builds/iOS-DerivedData/Build/Products -maxdepth 2 -name "*.app" -type d | head -1)
xcrun devicectl device install app --device "$UDID" "$APP"
xcrun devicectl device process launch --device "$UDID" --terminate-existing "$BUNDLE_ID"
