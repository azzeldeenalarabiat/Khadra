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
  SessionStore({FlutterSecureStorage? secureStorage, SharedPreferences? preferences})
      // Private field, public parameter -- `this._preferences` would put an
      // underscore in the constructor signature of the class every request on this
      // app passes through. Same choice as AuthInterceptor and SessionController.
      // ignore: prefer_initializing_formals
      : _preferences = preferences,
        _secure = secureStorage ??
            const FlutterSecureStorage(
              // The token MOVES HOUSE on the first read after this version ships.
              //
              // Until now it lived in EncryptedSharedPreferences, which is
              // Google's Jetpack Crypto library, which Google deprecated. v10 of
              // this package replaces it with its own ciphers and carries the
              // existing data across on first access; v11 removes the old
              // backend entirely and tells you to pass through v10 first or
              // strand whatever is already stored. So the app sits on v10 until
              // that move is proved on a handset that actually holds a token
              // written by the old one -- pre-launch item 94.
              //
              // All three flags are STATED rather than left to their defaults.
              // They decide what happens to an authentication credential, and a
              // default is a decision nobody can find later.
              aOptions: AndroidOptions(
                // Carry the data to the new ciphers instead of losing it.
                migrateOnAlgorithmChange: true,
                // NOT `migrateWithBackup`. It reads like the safe choice -- keep
                // a copy while the one-time move runs -- and it is the reason the
                // move did not happen at all.
                //
                // `FlutterSecureStorage.java:170` guards the whole
                // EncryptedSharedPreferences migration with
                // `if (!isAlreadyMigrated && !config.shouldMigrateWithBackup())`,
                // deferring it to "step 6 of the backup-protected migration
                // path" -- which is the ALGORITHM-CHANGE path, and that never
                // runs on a v9 store because there are no v10 algorithm markers
                // to have changed. Turning the backup on therefore skips the only
                // branch that can read Jetpack Crypto data.
                //
                // Measured, not reasoned: with it on, an in-place 9.2.4 to 10.3.2
                // upgrade left the Tink entries untouched and the customer signed
                // out. With it off the migration runs. See pre-launch item 94.
                // A token that cannot be decrypted is DISCARDED. This is a
                // change -- v9 defaulted it to false -- so what each setting
                // actually does to a customer is written out here rather than
                // left to a changelog.
                //
                // The plugin reaches this in two places
                // (`FlutterSecureStorage.java`): when the one-time migration
                // off the old backend fails, and when any single read or write
                // throws afterwards.
                //
                // TRUE -- the store deletes the unreadable entries, marks itself
                // migrated so it does not try again, and answers the next read
                // normally. Here that is the refresh token and its expiry, and
                // nothing else: `_refreshTokenKey` and `_refreshExpiresKey` are
                // all this app keeps. `_restore` reads null, the session goes to
                // signedOut, and the customer sees the app signed out with no
                // error. They sign in again and it works. Nothing is lost --
                // bookings, documents and the account itself live on the server.
                //
                // FALSE -- the plugin does NOT set its migrated marker, so every
                // later call fails the same way. `_bounded` below swallows those
                // failures by design, which means reads return null AND WRITES
                // SILENTLY DO NOTHING: signing in appears to work, the token is
                // never stored, and the customer is thrown out when the access
                // token goes stale -- forever, on every launch, with no message.
                // A store nobody can end is worse than a session nobody kept.
                resetOnError: true,
              ),
              iOptions: IOSOptions(
                // The app refreshes on cold start, which can happen from a
                // background launch before the first unlock of the day. Anything
                // stricter than this and that refresh reads null and signs a
                // perfectly valid session out.
                accessibility: KeychainAccessibility.first_unlock_this_device,
              ),
            );

  final FlutterSecureStorage _secure;

  /// Ordinary preferences, holding ONLY the ownership marker below.
  ///
  /// Deliberately not the place any token goes. It is here because the one thing
  /// this class cannot get from the secure store is a reliable answer about the
  /// secure store.
  final SharedPreferences? _preferences;

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

  /// Set while this install holds a session of its own. See [sessionIsOwned].
  ///
  /// It replaces the old `khadra.install_marker`, which answered only one of the
  /// two questions this one answers. Nothing reads the old key any more; a device
  /// carrying it is simply a device with no marker, which is the safe answer.
  static const _ownedKey = 'khadra.session_owned';

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

  /// Whether the tokens in the secure store belong to a session this install
  /// actually has.
  ///
  /// The secure store can be asked what it holds and cannot be relied on to
  /// FORGET. Every delete here goes through [_bounded], which swallows a failure
  /// or a five-second silence by design — so a sign-out can leave a perfectly
  /// usable refresh token on disk and report success. Without this marker the next
  /// cold start would rotate that token and sign the customer back into an account
  /// they deliberately left.
  ///
  /// So ownership is recorded where a failure is visible and a write is cheap, and
  /// the two operations are ordered so that every crash window fails safe:
  ///
  /// - [saveRefreshToken] sets it AFTER the token is written. A crash in between
  ///   leaves a valid token nobody will read, which costs one sign-in.
  /// - [clear] removes it BEFORE deleting anything. A crash, a failure or a
  ///   timeout in between leaves an orphan nobody will read, which costs nothing.
  ///
  /// It also covers what `clearIfReinstalled` used to cover on its own: iOS keeps
  /// Keychain items when an app is DELETED, so a reinstall can wake up holding the
  /// previous user's refresh token — theirs, on a device they may have sold.
  /// Preferences ARE cleared on delete, so the marker's absence is exactly the
  /// signal "this session is not ours".
  ///
  /// A device with no preferences AT ALL is a different answer from one whose
  /// marker is absent. The first cannot tell, and answering "not ours" there would
  /// mean no session ever survived a cold start; it trusts the secure store, as
  /// this class did before the marker existed.
  bool get sessionIsOwned {
    final preferences = _preferences;
    if (preferences == null) return true;
    return preferences.getBool(_ownedKey) ?? false;
  }

  Future<String?> readRefreshToken() async =>
      (await readRefreshTokenOutcome()).token;

  /// The stored refresh token, AND whether the store answered at all.
  ///
  /// Two different facts, and only one of them is safe to act on. Everything here
  /// turns a failure or a five-second silence into null, which is right for
  /// "should this request carry a token" — and wrong for "does this install still
  /// have a session". A keystore that has not been unlocked, an iOS Keychain read
  /// during a background launch, or one slow answer would otherwise look exactly
  /// like a token that is not there, and [disownSession] would throw away a
  /// perfectly good session over a transient fault.
  Future<({String? token, bool answered})> readRefreshTokenOutcome() async {
    // Not merely ignored — not even read. A guest's every request asks for this
    // through `AuthInterceptor`, and a bounded platform-channel round trip per
    // call is worth avoiding for somebody who has no token at all. Disowned IS a
    // definite answer: this install has no session, and it knows why.
    if (!sessionIsOwned) return (token: null, answered: true);

    try {
      final token =
          await _secure.read(key: _refreshTokenKey).timeout(_storageTimeout);
      return (token: token, answered: true);
    } on Object {
      return (token: null, answered: false);
    }
  }

  /// The refresh token's own deadline, so a cold start can tell a dead session
  /// from a live one without spending a request to find out.
  Future<DateTime?> readRefreshExpiry() async {
    if (!sessionIsOwned) return null;
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
    // LAST. See [sessionIsOwned] for why this order is the safe one.
    await _setOwned(true);
  }

  Future<void> clear() async {
    clearAccessToken();
    // FIRST, and this is the line that makes a sign-out stick. Everything below it
    // is allowed to fail.
    await _setOwned(false);
    await _bounded(() => _secure.delete(key: _refreshTokenKey), null);
    await _bounded(() => _secure.delete(key: _refreshExpiresKey), null);
  }

  /// Gives up the claim without asking the secure store for anything.
  ///
  /// For the case where the marker outlived the token rather than the other way
  /// round: Android's Auto Backup restores preferences to a NEW device, and the
  /// Keystore key does not travel with them, so `resetOnError` discards the entry
  /// it cannot decrypt. The marker would then promise a session that is not there.
  Future<void> disownSession() async {
    if (!sessionIsOwned) return;
    await _setOwned(false);
  }

  Future<void> _setOwned(bool owned) async {
    final preferences = _preferences;
    if (preferences == null) return;

    if (owned) {
      await preferences.setBool(_ownedKey, true);
    } else {
      await preferences.remove(_ownedKey);
    }
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

  /// Throws away any token this install does not own.
  ///
  /// Three different histories arrive here looking identical, and all three mean
  /// the same thing: a fresh install, an iOS reinstall still holding the previous
  /// user's Keychain entry, and a sign-out whose delete did not take. The entry is
  /// DELETED rather than merely ignored, because on the reinstall it is somebody
  /// else's credential on a device that may have changed hands.
  ///
  /// If the delete fails as well, nothing is lost: [sessionIsOwned] is already
  /// false, so [readRefreshToken] will not hand the orphan to anyone.
  ///
  /// Called once at startup, before anything reads a token.
  Future<void> discardDisownedTokens() async {
    if (sessionIsOwned) return;
    await clear();
  }
}
