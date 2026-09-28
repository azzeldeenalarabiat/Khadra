#!/bin/sh
# Runs `flutter build ios` with the arguments given, keeping its whole verbose log — Xcode's full
# output is only in there — then lists every warning and error Xcode reported. On a failure it
# prints the end of the log, where Xcode says why.
#
# macOS only, from Khadra.Mobile:
#     sh tools/ci/build_ios.sh <label> <evidence folder> <flutter build ios arguments...>
set -eu

label="$1"
evidence="$2"
shift 2
mkdir -p "$evidence"
log="$evidence/build-$label.log"

echo "flutter build ios $* --verbose   (full log: $(basename "$log") in the evidence artifact)"
status=0
flutter build ios "$@" --verbose > "$log" 2>&1 || status=$?

# The lines Flutter would have shown without --verbose.
sed 's/^\[[^]]*\] *//' "$log" \
  | grep -E 'Built build/|Xcode build done|Failed to build|Encountered error|BUILD (SUCCEEDED|FAILED)' || true

sh "$(dirname "$0")/xcode_issues.sh" "$log" "$label"

if [ "$status" -ne 0 ]; then
  echo "::error title=iOS build ($label)::flutter build ios exited with $status; the end of its log follows"
  tail -n 150 "$log"
fi
exit "$status"
