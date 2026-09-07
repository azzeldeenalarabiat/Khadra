import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Where the tokens live, and the ONE place that reads or writes them.
///
/// The split is deliberate and follows `docs/auth-and-sessions.md`:
///
/// - The **access token stays in memory**. It lives fifteen minutes, every request
///   re-checks the account's security stamp, and writing it to disk would only
///   widen where a short-lived credential can be found.
/// - The **refresh token goes to secure storage** — Keychain on iOS, Keystore on
///   Android — because it is the only thing that survives a cold start, and it is
///   worth fourteen days.
///
/// There is exactly one reader and one writer, and callers must read at SEND time
/// rather than capture a token in a closure. A stale token captured a minute ago
/// and presented after a rotation is precisely what replay detection is watching
/// for: the server would revoke the whole family and sign the person out of every
/// device for a bug in an interceptor.
class SessionStore {
  SessionStore({FlutterSecureStorage? secureStorage})
      : _secure = secureStorage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
              iOptions: IOSOptions(
                // The app refreshes on cold start, which can happen from a
                // background launch before the first unlock of the day. Anything
                // stricter than this and that refresh reads null and signs a
                // perfectly valid session out.
                accessibility: KeychainAccessibility.first_unlock_this_device,
              ),
            );

  final FlutterSecureStorage _secure;

  /// How long any single secure-storage call may take before it is treated as
  /// having no answer.
  ///
  /// Not a nicety. Every one of these sits between the app starting and the first
  /// screen appearing, and the platform stores are not guaranteed to answer at
  /// all: an Android keystore that has not been unlocked, an iOS Keychain during a
  /// background launch, a browser with site data blocked, or a web implementation
  /// that simply never resolves its future. Without a bound, any of those leaves a
  /// customer looking at a splash screen for ever with nothing to tap.
  ///
  /// Timing out means "no session", which is recoverable: they sign in again. A
  /// spinner is not.
  static const _storageTimeout = Duration(seconds: 5);

  static const _refreshTokenKey = 'khadra.refresh_token';
  static const _refreshExpiresKey = 'khadra.refresh_expires_at';
  static const _installMarkerKey = 'khadra.install_marker';

  String? _accessToken;
  DateTime? _accessExpiresAt;

  String? get accessToken => _accessToken;

  /// Whether the access token is close enough to expiry to refresh before using.
  ///
  /// The margin covers the API's own 30-second `ClockSkew` plus the round trip. A
  /// proactive refresh here is what keeps the 401 storm — every in-flight request
  /// failing at once — an exception rather than the normal path.
  bool get accessTokenIsStale {
    if (_accessToken == null || _accessExpiresAt == null) return true;
    return DateTime.now().toUtc().isAfter(
          _accessExpiresAt!.toUtc().subtract(const Duration(seconds: 60)),
        );
  }

  void setAccessToken(String token, DateTime expiresAt) {
    _accessToken = token;
    _accessExpiresAt = expiresAt;
  }

  void clearAccessToken() {
    _accessToken = null;
    _accessExpiresAt = null;
  }

  Future<String?> readRefreshToken() =>
      _bounded(() => _secure.read(key: _refreshTokenKey), null);

  /// The refresh token's own deadline, so a cold start can tell a dead session
  /// from a live one without spending a request to find out.
  Future<DateTime?> readRefreshExpiry() async {
    final raw = await _bounded(() => _secure.read(key: _refreshExpiresKey), null);
    if (raw == null) return null;
    return DateTime.tryParse(raw)?.toUtc();
  }

  /// Writes the rotated token, and the caller must AWAIT this before installing
  /// the new access token or releasing anyone waiting on the refresh.
  ///
  /// The order is the whole point. If the process dies between the server's commit
  /// and this write, the token on disk is the one the server has just consumed;
  /// only the server's 60-second reuse grace saves the session, and only if the
  /// next attempt comes promptly. Installing the access token first would widen
  /// that window for no benefit.
  Future<void> saveRefreshToken(String token, DateTime expiresAt) async {
    await _bounded(
      () => _secure.write(key: _refreshTokenKey, value: token),
      null,
    );
    await _bounded(
      () => _secure.write(
        key: _refreshExpiresKey,
        value: expiresAt.toUtc().toIso8601String(),
      ),
      null,
    );
  }

  Future<void> clear() async {
    clearAccessToken();
    await _bounded(() => _secure.delete(key: _refreshTokenKey), null);
    await _bounded(() => _secure.delete(key: _refreshExpiresKey), null);
  }

  /// Runs one secure-storage call with a deadline, and treats a failure or a
  /// silence as [fallback].
  ///
  /// Both outcomes mean the same thing to this app: the stored token could not be
  /// read or written, so there is no session to restore. Nothing here retries,
  /// because a store that did not answer in five seconds will not answer in ten,
  /// and the customer is waiting.
  Future<T?> _bounded<T>(Future<T?> Function() call, T? fallback) async {
    try {
      return await call().timeout(_storageTimeout);
    } on Object {
      return fallback;
    }
  }

  /// Throws away a Keychain entry that outlived the app that wrote it.
  ///
  /// iOS keeps Keychain items when an app is DELETED, so a reinstall can wake up
  /// holding the previous user's refresh token — theirs, on a device they may have
  /// sold. The marker lives in ordinary preferences, which iOS does clear on
  /// delete, so its absence is exactly the signal "this is a fresh install".
  ///
  /// Called once at startup, before anything reads a token.
  Future<void> clearIfReinstalled() async {
    final preferences = await SharedPreferences.getInstance();
    if (preferences.getBool(_installMarkerKey) ?? false) return;

    await clear();
    await preferences.setBool(_installMarkerKey, true);
  }
}
