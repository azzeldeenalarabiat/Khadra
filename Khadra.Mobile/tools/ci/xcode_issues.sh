#!/bin/sh
# Lists every warning and error Xcode reported in a `flutter build ios --verbose` log. Flutter shows
# none of Xcode's warnings on its own (it discards them on purpose, flutter/flutter#95354), so the
# verbose log is the only place they can be read. This reports; it never fails a build.
#
#     sh tools/ci/xcode_issues.sh <log> <label>
set -eu

log="$1"
label="$2"
report="${log%.log}-xcode-issues.txt"

# Flutter prefixes every line of a verbose log with its own timing, "[  +12 ms] ". Each distinct
# line is listed once, with how often it appeared.
sed 's/^\[[^]]*\] *//' "$log" \
  | grep -E '(^|[[:space:]:])(warning|error): ' \
  | sort | uniq -c | sort -rn > "$report" || true

warnings="$(grep -c 'warning: ' "$report" || true)"
errors="$(grep -c 'error: ' "$report" || true)"
echo "Xcode ($label): $warnings distinct warning line(s), $errors distinct error line(s)"
cat "$report"

{
  echo "### Xcode ($label): $warnings warning line(s), $errors error line(s)"
  if [ -s "$report" ]; then
    echo '```'
    cat "$report"
    echo '```'
  fi
} >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
