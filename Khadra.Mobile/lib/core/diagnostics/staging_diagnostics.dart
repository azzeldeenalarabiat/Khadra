import 'package:flutter/services.dart' show appFlavor;

import '../config/app_environment.dart';

/// TEMPORARY — the W4-9 device investigation (2026-10-08). Remove with [PushTrace] once a
/// tap is verified on a phone (pre-launch checklist item 27).
///
/// A fixed word compiled into this source tree and into nothing else. It is shown at the
/// top of Profile on a Staging build, and it can be found INSIDE an APK before it is
/// installed (`unzip -p app-staging-release.apk lib/arm64-v8a/libapp.so | strings | grep`),
/// so whether a phone runs this source is a fact to read rather than a thing to assume.
/// Change it whenever the diagnostics change.
const diagnosticsMarker = 'w4-9-diag-3';

/// What this running build says it is, from TWO independent sources, so the diagnostics
/// stay reachable even if one of them were wrong.
///
/// - The Dart side: `appFlavor`, which the Flutter tool compiles in from `--flavor`, and
///   [AppEnvironment.build] resolved from it.
/// - The Android side: the application id the system reports at run time, which the
///   `staging` product flavor suffixes with `.staging` (android/app/build.gradle.kts).
///
/// Diagnostics are on when EITHER says Staging; [mismatch] says so out loud when the two
/// disagree. A production build reports neither, and shows nothing.
abstract final class StagingDiagnostics {
  static String? packageName;
  static String? version;
  static String? buildNumber;

  /// Set once by `main()` from the platform's package info.
  static void attach({required String? packageName, required String? version, required String? buildNumber}) {
    StagingDiagnostics.packageName = packageName;
    StagingDiagnostics.version = version;
    StagingDiagnostics.buildNumber = buildNumber;
  }

  static bool get flavorSaysStaging => AppEnvironment.isStaging;

  static bool get packageSaysStaging => packageName?.endsWith('.staging') ?? false;

  static bool get enabled => flavorSaysStaging || packageSaysStaging;

  /// The Dart flavor and the Android identity disagree: a build that cannot be trusted.
  static bool get mismatch => packageName != null && flavorSaysStaging != packageSaysStaging;

  /// One line naming this build: marker, version and build number.
  static String get headline =>
      'staging diagnostics · $diagnosticsMarker · ${version ?? '?'}+${buildNumber ?? '?'}'
      '${mismatch ? ' · FLAVOR MISMATCH' : ''}';

  /// Everything known about this build. None of it is personal or secret.
  static List<String> facts() {
    String host;
    try {
      host = Uri.parse(AppEnvironment.apiBaseUrl).host;
    } on Object catch (error) {
      host = 'unreadable ${error.runtimeType}';
    }
    return [
      'marker=$diagnosticsMarker',
      'package=${packageName ?? '(unknown)'}',
      'version=${version ?? '?'} build=${buildNumber ?? '?'}',
      'appFlavor=${appFlavor ?? '(none)'} resolved=${AppEnvironment.build.name}',
      'flavorSaysStaging=$flavorSaysStaging packageSaysStaging=$packageSaysStaging mismatch=$mismatch',
      'api=$host',
    ];
  }
}
