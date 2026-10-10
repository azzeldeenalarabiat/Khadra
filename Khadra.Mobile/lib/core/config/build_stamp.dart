import 'package:flutter_riverpod/flutter_riverpod.dart';

/// The commit this build's Dart was compiled from (pre-launch item 244).
///
/// Stamped at build time, and only there:
///
///     flutter build apk --release --flavor staging \
///       --dart-define=KHADRA_BUILD_COMMIT=$(git rev-parse HEAD)
///
/// It exists because the version cannot say it. On 10 Oct 2026 a Staging APK labelled
/// 1.4.0+7 carried the compiled Dart of an older build: the version, the signer and the API
/// address all come from outside the Dart snapshot, and all three were right. This constant
/// is INSIDE the snapshot, so Profile shows the code that is actually running, and
/// `tools/verify_apk_dart.js --commit` finds it in every ABI's `libapp.so` before an APK is
/// handed to anybody.
///
/// Empty when the build was not stamped (`flutter run`, the tests): nothing is invented.
const String buildCommit = String.fromEnvironment('KHADRA_BUILD_COMMIT');

/// [buildCommit], behind a provider so a test can say which commit it is.
final buildCommitProvider = Provider<String>((ref) => buildCommit);

/// How much of the commit Profile shows: enough to tell builds apart, short enough for a line.
const int buildStampCommitLength = 12;

/// The line at the foot of Profile — `1.4.0+8 · 76ea7bc6c925` — or null when there is
/// nothing to show.
///
/// The version is the platform's (`installedAppVersionProvider`), the commit the stamp's.
/// A commit that is not hexadecimal is not a commit, and is left off rather than shown.
String? buildStampLine({required String? installedVersion, required String commit}) {
  final version = installedVersion?.trim() ?? '';
  final stamped = RegExp(r'^[0-9a-f]{7,40}$').hasMatch(commit)
      ? commit.substring(0, commit.length < buildStampCommitLength ? commit.length : buildStampCommitLength)
      : '';
  if (version.isEmpty && stamped.isEmpty) return null;
  if (stamped.isEmpty) return version;
  if (version.isEmpty) return stamped;
  return '$version · $stamped';
}
