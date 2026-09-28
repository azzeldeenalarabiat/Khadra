#!/bin/sh
# Installs an app on an iPhone simulator and opens it in English, then in Arabic, keeping what the
# screen showed and what the app printed each time. It proves the iOS app starts and draws its
# first screen, in both directions, before any Apple account exists. It signs in to nothing and taps
# nothing; a simulator build needs no signing at all.
#
# macOS only, from Khadra.Mobile, after `flutter build ios --simulator --debug --flavor <flavor>`:
#     sh tools/ci/launch_on_simulator.sh build/ios/iphonesimulator/Runner.app <output folder>
set -eu

app="$1"
out="$2"
mkdir -p "$out"
id="$(plutil -extract CFBundleIdentifier raw "$app/Info.plist")"

# The first iPhone of the newest iOS the runner has. Which one does not matter; an iPhone does.
device="$(xcrun simctl list devices available --json | python3 -c '
import json, sys
best = None
for runtime, devices in json.load(sys.stdin)["devices"].items():
    if ".iOS-" not in runtime:
        continue
    version = tuple(int(part) for part in runtime.rsplit(".iOS-", 1)[1].split("-"))
    for device in devices:
        if device["name"].startswith("iPhone") and (best is None or version > best[0]):
            best = (version, device["udid"], device["name"])
if best is None:
    sys.exit("no iPhone simulator is available")
print(best[1])
print("simulator: %s, iOS %s" % (best[2], ".".join(map(str, best[0]))), file=sys.stderr)')"

xcrun simctl boot "$device" 2>/dev/null || true
xcrun simctl bootstatus "$device" -b
xcrun simctl install "$device" "$app"

status=0
for language in en:en_US ar:ar_JO; do
  code="${language%%:*}"
  # -AppleLanguages is what the device's language setting would say; the app follows it on a
  # first run, exactly as it does on a phone set to that language. simctl returns once the app is
  # started and answers "<bundle id>: <pid>"; what the app prints goes to the two files.
  launched="$(xcrun simctl launch --terminate-running-process \
    --stdout="$out/stdout-$code.log" --stderr="$out/stderr-$code.log" \
    "$device" "$id" -AppleLanguages "($code)" -AppleLocale "${language#*:}")"
  pid="$(printf '%s\n' "$launched" | sed -n 's/^.*: *\([0-9][0-9]*\)[[:space:]]*$/\1/p' | head -1)"
  if [ -z "$pid" ]; then
    # A fault in this script, not in the app: say so, rather than report a crash that never was.
    echo "::error title=iOS simulator::could not tell which process the app is; simctl said: $launched"
    status=1
    continue
  fi

  sleep 30
  xcrun simctl io "$device" screenshot "$out/first-screen-$code.png"
  # Simulator apps are ordinary processes of the Mac, so the pid can be asked directly.
  if kill -0 "$pid" 2>/dev/null; then
    echo "ok      running 30 seconds after opening in $code (pid $pid)"
  else
    echo "::error title=iOS simulator::the app stopped within 30 seconds of opening in $code"
    status=1
  fi
  xcrun simctl terminate "$device" "$id" 2>/dev/null || true
done

# Any crash report the simulator wrote for the app.
find "$HOME/Library/Logs/DiagnosticReports" -name 'Runner*' -newer "$app/Info.plist" -exec cp {} "$out/" \; 2>/dev/null || true
exit "$status"
