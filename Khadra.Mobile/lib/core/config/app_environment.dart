import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart' show appFlavor;

/// Which Khadra this build of the app belongs to.
///
/// Chosen by the build FLAVOR and nothing else — `flutter build apk --flavor staging` — which also
/// chooses the Android application id and the launcher name, so the three cannot disagree. See
/// `android/app/build.gradle.kts` and docs/production.md, "The customer app".
enum KhadraBuild {
  /// The customer app. Also every build that names no flavor (the web, `flutter test`), because
  /// pubspec.yaml makes `production` the default.
  production,

  /// "Khadra TEST": a separate application bound to the staging API, which runs the sandbox
  /// payment provider. It says so on every screen (see `EnvironmentRibbon`).
  staging,
}

/// Where this build of the app finds the Khadra API.
///
/// The app talks to `Khadra.WebAPI` DIRECTLY with a bearer token. It does not go
/// through `Khadra.Bff`: that exists so a BROWSER never holds a token, keeping the
/// session in a `__Host-` cookie with a Redis ticket store and antiforgery. A phone
/// has secure storage and no cookie jar shared with the open web, so routing it
/// through the BFF would add a hop and a CSRF surface to solve a problem it does
/// not have. `docs/auth-and-sessions.md` draws the same line.
///
/// **Production** is exactly what it was before flavors existed. The default is a
/// DEVELOPMENT one and every real build must pass its own:
///
///     flutter build apk --release --dart-define=KHADRA_API_BASE_URL=https://khadra.onrender.com
///
/// There is deliberately no production URL compiled in as a fallback. A release
/// that forgot the flag should fail loudly against an address that does not
/// resolve, rather than quietly talking to somebody's laptop.
///
/// **Staging** is the opposite, deliberately: its address IS compiled in, and it
/// refuses to be pointed anywhere else. The whole value of that build is that a
/// tester holding it knows which server they are testing, so there is no flag to
/// forget and none to get wrong.
abstract final class AppEnvironment {
  static const String _override = String.fromEnvironment('KHADRA_API_BASE_URL');

  /// The staging API. The only address a staging build will talk to.
  static const String stagingApiBaseUrl = 'https://khadra-staging.onrender.com';

  /// This build, from its flavor. Resolved once; an unknown flavor is a build mistake and throws.
  static final KhadraBuild build = buildFor(appFlavor);

  /// Whether this is the "Khadra TEST" build. What the ribbon over every screen keys on.
  ///
  /// NOT what the sandbox payment banner keys on: that is the server's own `payments.mode`, because
  /// whether money moves is a fact about the server, and a build flag is not evidence of it.
  static bool get isStaging => build == KhadraBuild.staging;

  /// The API root, without a trailing slash.
  static final String apiBaseUrl = resolveApiBaseUrl(
    build: build,
    override: _override,
    developmentDefault: _developmentDefault,
  );

  /// Whether this build is talking to a developer machine rather than a real API.
  static bool get isDevelopmentTarget => !isStaging && _override.isEmpty;

  /// Reads every setting above, so a build that is wrong stops at launch with a sentence saying
  /// why, instead of on the first request with a `LateInitializationError`.
  static void verify() {
    if (apiBaseUrl.isEmpty) throw StateError('This build has no API address.');
  }

  /// The flavor name as Flutter hands it over, turned into a [KhadraBuild].
  @visibleForTesting
  static KhadraBuild buildFor(String? flavor) => switch (flavor) {
        null || '' || 'production' => KhadraBuild.production,
        'staging' => KhadraBuild.staging,
        _ => throw StateError(
            'Unknown build flavor "$flavor". This app is built as "production" or "staging".'),
      };

  /// Where a build of the given kind sends its requests. Pure, so both rules below are testable.
  ///
  /// - **Staging** always answers [stagingApiBaseUrl]. A `KHADRA_API_BASE_URL` that names anything
  ///   else is refused rather than obeyed: a "Khadra TEST" app talking to production would put the
  ///   test banner over real customers' data, and a staging build is only worth anything if its
  ///   name is a promise about where it points.
  /// - **Production** is unchanged — the define, or the development loopback — except that it
  ///   refuses the STAGING host. The production application id must never be the one a tester's
  ///   sandbox bookings are made with. That is why the staging hostname appears in the production
  ///   binary as a string: it is a refusal, never a fallback.
  @visibleForTesting
  static String resolveApiBaseUrl({
    required KhadraBuild build,
    required String override,
    required String developmentDefault,
  }) {
    final requested = override.isEmpty ? null : _stripTrailingSlash(override);

    switch (build) {
      case KhadraBuild.staging:
        if (requested != null && requested != stagingApiBaseUrl) {
          throw StateError(
            'The staging build talks only to $stagingApiBaseUrl, and was given $requested. '
            'Drop --dart-define=KHADRA_API_BASE_URL from a --flavor staging build.',
          );
        }
        return stagingApiBaseUrl;

      case KhadraBuild.production:
        final url = requested ?? developmentDefault;
        if (Uri.tryParse(url)?.host == Uri.parse(stagingApiBaseUrl).host) {
          throw StateError(
            'The production build was pointed at the staging API ($url). '
            'Build the staging app instead: flutter build apk --release --flavor staging',
          );
        }
        return url;
    }
  }

  /// Plain HTTP on purpose, and only in development.
  ///
  /// The API also listens on https://localhost:7012 with the ASP.NET developer
  /// certificate, which nothing outside the machine that minted it trusts: a
  /// browser refuses the XHR outright, and an Android emulator refuses the socket.
  /// Working around that means either shipping a certificate-pinning escape hatch
  /// or teaching the app to ignore bad certificates — both of which are exactly
  /// the code that must never reach a release build. Plain HTTP to a loopback
  /// address leaves the machine's own network stack and nothing else.
  ///
  /// `10.0.2.2` is the host as seen from inside the Android emulator; `localhost`
  /// there is the emulated device itself, which is not running an API.
  static String get _developmentDefault {
    if (kIsWeb) {
      // The API is on port 5012 of whichever machine served this page. DERIVED
      // rather than fixed, because the same development build is opened two ways:
      // from `localhost` on the machine itself, and from that machine's address
      // on the Wi-Fi when the app is being tried on a real phone. Hard-coding
      // `localhost` sent the phone's requests to the phone.
      //
      // It also survives the DHCP lease moving, which a compiled-in address does
      // not — and a rebuild is a poor way to find out your IP changed.
      final host = Uri.base.host;
      return 'http://${host.isEmpty ? 'localhost' : host}:5012';
    }
    return defaultTargetPlatform == TargetPlatform.android
        ? 'http://10.0.2.2:5012'
        : 'http://localhost:5012';
  }

  static String _stripTrailingSlash(String value) =>
      value.endsWith('/') ? value.substring(0, value.length - 1) : value;

  /// Absolute URL for a path the API returned relative, such as an image.
  ///
  /// The API answers `/api/v1/vehicle-images/...` — a path, not a URL, because it
  /// does not know what host a client reached it on. Prefixing it here is the one
  /// place that knows.
  static String resolve(String pathOrUrl) {
    if (pathOrUrl.startsWith('http://') || pathOrUrl.startsWith('https://')) {
      return pathOrUrl;
    }
    return pathOrUrl.startsWith('/')
        ? '$apiBaseUrl$pathOrUrl'
        : '$apiBaseUrl/$pathOrUrl';
  }
}
