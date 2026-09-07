import 'package:flutter/foundation.dart';

/// Where this build of the app finds the Khadra API.
///
/// The app talks to `Khadra.WebAPI` DIRECTLY with a bearer token. It does not go
/// through `Khadra.Bff`: that exists so a BROWSER never holds a token, keeping the
/// session in a `__Host-` cookie with a Redis ticket store and antiforgery. A phone
/// has secure storage and no cookie jar shared with the open web, so routing it
/// through the BFF would add a hop and a CSRF surface to solve a problem it does
/// not have. `docs/auth-and-sessions.md` draws the same line.
///
/// The default is a DEVELOPMENT one and every real build must pass its own:
///
///     flutter build apk --dart-define=KHADRA_API_BASE_URL=https://api.khadra.jo
///
/// There is deliberately no production URL compiled in as a fallback. A release
/// that forgot the flag should fail loudly against an address that does not
/// resolve, rather than quietly talking to somebody's laptop.
abstract final class AppEnvironment {
  static const String _override = String.fromEnvironment('KHADRA_API_BASE_URL');

  /// The API root, without a trailing slash.
  static String get apiBaseUrl {
    if (_override.isNotEmpty) return _stripTrailingSlash(_override);
    return _developmentDefault;
  }

  /// Whether this build is talking to a developer machine rather than a real API.
  static bool get isDevelopmentTarget => _override.isEmpty;

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
    if (kIsWeb) return 'http://localhost:5012';
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
