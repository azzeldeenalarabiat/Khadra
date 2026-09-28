#!/bin/sh
# Checks that an app `flutter build ios` produced is the flavor the project says it is: its bundle
# id, its name in English and in Arabic, its permission texts, its languages, its version and its
# minimum iOS. Each is compared with the file it came from (ios/Flutter/<flavor>.xcconfig,
# ios/Runner/Info.plist, ios/Runner/Flavors/<flavor>/, pubspec.yaml, project.pbxproj), never with a
# value typed here, so this proves the build did what the files say; test/ios_project_test.dart
# proves the files say the right thing.
#
# macOS only (plutil), from Khadra.Mobile:
#     sh tools/ci/check_ios_app.sh <flavor> build/ios/iphoneos/Runner.app
set -eu

flavor="$1"
app="$2"
failures=0

# A property list's value — Info.plist and .strings alike, text or binary. Strings print as they
# are; anything else as JSON, so two values compare as text.
value() {
  plutil -convert json -o - "$1" | PYTHONIOENCODING=utf-8 python3 -c '
import json, sys
value = json.load(sys.stdin).get(sys.argv[1])
print(value if isinstance(value, str) else json.dumps(value, sort_keys=True, ensure_ascii=False))' "$2"
}

keys() {
  plutil -convert json -o - "$1" | python3 -c 'import json, sys; print(" ".join(sorted(json.load(sys.stdin))))'
}

# An xcconfig setting of this flavor. `//` starts a comment anywhere on a line, as in Xcode.
setting() {
  sed -n "s/^$1 *= *//p" "ios/Flutter/$flavor.xcconfig" | sed 's#[[:space:]]*//.*$##'
}

check() {
  if [ "$2" = "$3" ]; then
    echo "ok      $1: $3"
  else
    echo "::error title=iOS $flavor app::$1 is \"$3\", expected \"$2\""
    failures=$((failures + 1))
  fi
}

plist="$app/Info.plist"
if [ ! -f "$plist" ]; then
  echo "::error title=iOS $flavor app::there is no app at $app"
  exit 1
fi

# The values Info.plist takes from the build.
check "bundle id" "$(setting PRODUCT_BUNDLE_IDENTIFIER)" "$(value "$plist" CFBundleIdentifier)"
check "name" "$(setting KHADRA_DISPLAY_NAME)" "$(value "$plist" CFBundleDisplayName)"
check "short name" "$(setting KHADRA_DISPLAY_NAME)" "$(value "$plist" CFBundleName)"
version="$(sed -n 's/^version: *//p' pubspec.yaml)"
check "version" "${version%%+*}" "$(value "$plist" CFBundleShortVersionString)"
check "build number" "${version#*+}" "$(value "$plist" CFBundleVersion)"
target="$(grep -o 'IPHONEOS_DEPLOYMENT_TARGET = [0-9.]*' ios/Runner.xcodeproj/project.pbxproj | sort -u | sed 's/.* = //')"
check "minimum iOS" "$target" "$(value "$plist" MinimumOSVersion)"

# No exception to App Transport Security: both APIs are HTTPS, and a development allowance that
# reached a built app would reach a customer (pre-launch item 193).
check "App Transport Security exceptions" "null" "$(value "$plist" NSAppTransportSecurity)"

# Every value Info.plist states outright reached the app unchanged: the languages, the permission
# texts, the orientations, the scene manifest.
for key in $(keys ios/Runner/Info.plist); do
  expected="$(value ios/Runner/Info.plist "$key")"
  case "$expected" in *'$('*) continue ;; esac
  check "$key" "$expected" "$(value "$plist" "$key")"
done

# And each language this flavor adds, installed by the "Copy Flavor Localizations" phase.
for source in ios/Runner/Flavors/"$flavor"/*.lproj/InfoPlist.strings; do
  language="$(basename "$(dirname "$source")")"
  built="$app/$language/InfoPlist.strings"
  if [ ! -f "$built" ]; then
    echo "::error title=iOS $flavor app::$language/InfoPlist.strings is missing from the app"
    failures=$((failures + 1))
    continue
  fi
  for key in $(keys "$source"); do
    check "$language $key" "$(value "$source" "$key")" "$(value "$built" "$key")"
  done
done

exit "$failures"
