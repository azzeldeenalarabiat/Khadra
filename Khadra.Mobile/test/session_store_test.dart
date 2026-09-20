import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/session/session_controller.dart';
import 'package:khadra_mobile/core/session/session_store.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fake_api.dart';

/// A signed-out session must stay signed out, whatever the secure store does.
///
/// Every delete in [SessionStore] is deliberately bounded and deliberately
/// swallows its own failures: an Android Keystore that has not been unlocked, an
/// iOS Keychain during a background launch, or a store that simply never answers
/// must not leave a customer looking at a splash screen for ever. The cost of that
/// choice is that `clear()` can report success having deleted nothing — and the
/// next cold start would then rotate a live refresh token and sign somebody back
/// into the account they had just left.
///
/// The ownership marker is what closes that, and these are the cases it closes.
/// Each one is a real device story, not a contrived failure.
void main() {
  const tokenKey = 'khadra.refresh_token';
  const expiresKey = 'khadra.refresh_expires_at';
  const ownedKey = 'khadra.session_owned';

  late _Keychain keychain;
  late SharedPreferences preferences;

  Future<SessionStore> storeWith({Map<String, Object> initialPreferences = const {}}) async {
    SharedPreferences.setMockInitialValues(Map.of(initialPreferences));
    preferences = await SharedPreferences.getInstance();
    return SessionStore(secureStorage: keychain, preferences: preferences);
  }

  setUp(() => keychain = _Keychain());

  final expiry = DateTime.utc(2026, 10, 1);

  test('a sign-out whose delete fails still ends the session', () async {
    final store = await storeWith();
    await store.saveRefreshToken('a-live-token', expiry);
    expect(await store.readRefreshToken(), 'a-live-token');

    // The keystore stops cooperating at the worst possible moment. Nothing about
    // this is visible to the caller, by design.
    keychain.deleteFails = true;
    await store.clear();

    // The token is STILL THERE, and it is still valid as far as the server is
    // concerned: a sign-out that could not reach the network leaves the family
    // alive for fourteen days.
    expect(keychain.values[tokenKey], 'a-live-token');

    // And it is unreachable. This is the assertion the requirement is written for.
    expect(await store.readRefreshToken(), isNull);
    expect(await store.readRefreshExpiry(), isNull);
    expect(store.sessionIsOwned, isFalse);
    expect(preferences.getBool(ownedKey), isNull);
  });

  test('a token left behind by a previous install is never handed over', () async {
    // iOS keeps Keychain items when an app is DELETED, so a reinstall can wake up
    // holding the previous user's token — theirs, on a device they may have sold.
    // Preferences ARE cleared on delete, so there is no marker.
    keychain.values[tokenKey] = 'somebody-elses-token';
    keychain.values[expiresKey] = expiry.toIso8601String();

    final store = await storeWith();

    expect(store.sessionIsOwned, isFalse);
    expect(await store.readRefreshToken(), isNull);

    // Deleted, not merely ignored: it is a credential belonging to somebody else.
    await store.discardDisownedTokens();
    expect(keychain.values, isEmpty);
  });

  test('the claim is made after the token is written', () async {
    final store = await storeWith();

    bool? claimedWhenWriting;
    keychain.onWrite = () => claimedWhenWriting = preferences.getBool(ownedKey);

    await store.saveRefreshToken('a-live-token', expiry);

    // Not yet claimed while the write was in progress. A crash in that window
    // leaves a valid token nobody will read, which costs one sign-in; the other
    // order would leave a claim on a token that was never written, which costs a
    // session that can never be restored at all.
    expect(claimedWhenWriting, isNull);
    expect(preferences.getBool(ownedKey), isTrue);
  });

  test('the claim is dropped before the store is touched', () async {
    final store = await storeWith();
    await store.saveRefreshToken('a-live-token', expiry);

    bool? claimedWhenDeleting;
    keychain.onDelete = () => claimedWhenDeleting = preferences.getBool(ownedKey);

    await store.clear();

    // Already given up by the time the first delete ran, which is what makes every
    // way that delete can fail harmless.
    expect(claimedWhenDeleting, isNull);
  });

  test('a device that cannot remember anything trusts what it holds', () async {
    // Not the same answer as "the marker is absent". A store with no preferences
    // at all cannot tell, and answering "not ours" there would mean no session
    // ever survived a cold start.
    final store = SessionStore(secureStorage: keychain);
    keychain.values[tokenKey] = 'a-live-token';

    expect(store.sessionIsOwned, isTrue);
    expect(await store.readRefreshToken(), 'a-live-token');
  });

  group('restore', () {
    /// The controller, on the real store, so the marker is what decides.
    (SessionController, _CountingApi) controllerOn(SessionStore store) {
      final api = _CountingApi();
      return (SessionController(api: api, store: store), api);
    }

    test('a disowned token is not rotated, and the session is signed out',
        () async {
      keychain.values[tokenKey] = 'a-token-from-before';
      keychain.values[expiresKey] = expiry.toIso8601String();

      final store = await storeWith();
      final (controller, api) = controllerOn(store);

      await controller.restore();

      // Never presented. Rotating a token the app does not own is how a sign-out
      // becomes a sign-in, and on a shared phone how one customer becomes another.
      expect(api.refreshCalls, 0);
      expect(controller.state.status, SessionStatus.signedOut);

      // Ended by the device's own state, not by a verdict from the server, so the
      // sign-in screen must not accuse the session of expiring.
      expect(controller.state.endedReason, isNull);
    });

    test('a store that did not ANSWER does not cost the session', () async {
      // The difference between "there is no token" and "nobody said". A keystore
      // that has not been unlocked, or one slow answer past the five-second bound,
      // reads as null through every other path here -- and giving up the claim on
      // that would be permanent, because the next launch DELETES what this install
      // no longer owns. It unlocks a moment later; the session must survive.
      keychain.values[tokenKey] = 'a-live-token';
      keychain.values[expiresKey] = expiry.toIso8601String();

      final store = await storeWith(initialPreferences: {ownedKey: true});
      keychain.readFails = true;

      final (controller, _) = controllerOn(store);
      await controller.restore();

      expect(controller.state.status, SessionStatus.signedOut);

      // The claim stands, and so does the token.
      expect(store.sessionIsOwned, isTrue);
      expect(preferences.getBool(ownedKey), isTrue);

      // Proved by the store answering again: the session is still there to find.
      keychain.readFails = false;
      expect(await store.readRefreshToken(), 'a-live-token');
    });

    test('a claim that outlived its token is given up', () async {
      // Android's Auto Backup restores preferences to a NEW device and the
      // Keystore key does not travel, so `resetOnError` discards the entry it
      // cannot decrypt: the marker promises a session that is not there.
      final store = await storeWith(initialPreferences: {ownedKey: true});
      final (controller, api) = controllerOn(store);

      await controller.restore();

      expect(api.refreshCalls, 0);
      expect(controller.state.status, SessionStatus.signedOut);
      expect(store.sessionIsOwned, isFalse);
      expect(preferences.getBool(ownedKey), isNull);
    });
  });
}

/// A secure store in memory, which can be told to stop deleting.
class _Keychain extends FlutterSecureStorage {
  _Keychain();

  final Map<String, String> values = <String, String>{};

  bool deleteFails = false;
  bool readFails = false;

  /// Run at the moment a write or a delete reaches the store, so a test can see
  /// what the ownership marker said AT THAT INSTANT. Ordering is the whole point
  /// of two of the tests above and nothing else can observe it.
  void Function()? onWrite;
  void Function()? onDelete;

  @override
  Future<void> write({
    required String key,
    required String? value,
    AppleOptions? iOptions,
    AndroidOptions? aOptions,
    LinuxOptions? lOptions,
    WebOptions? webOptions,
    AppleOptions? mOptions,
    WindowsOptions? wOptions,
  }) async {
    onWrite?.call();
    if (value == null) {
      values.remove(key);
    } else {
      values[key] = value;
    }
  }

  @override
  Future<String?> read({
    required String key,
    AppleOptions? iOptions,
    AndroidOptions? aOptions,
    LinuxOptions? lOptions,
    WebOptions? webOptions,
    AppleOptions? mOptions,
    WindowsOptions? wOptions,
  }) async {
    // A locked keystore, or one too slow to answer inside the bound. Both reach
    // the app the same way and neither says the token is gone.
    if (readFails) throw Exception('the keystore is locked');
    return values[key];
  }

  @override
  Future<void> delete({
    required String key,
    AppleOptions? iOptions,
    AndroidOptions? aOptions,
    LinuxOptions? lOptions,
    WebOptions? webOptions,
    AppleOptions? mOptions,
    WindowsOptions? wOptions,
  }) async {
    onDelete?.call();
    // The way a real one fails: loudly to itself and silently to the app, because
    // `_bounded` swallows it.
    if (deleteFails) throw Exception('the keystore is not available');
    values.remove(key);
  }
}

/// Counts the rotations nobody should be asking for.
class _CountingApi extends FakeApi {
  int refreshCalls = 0;

  @override
  Future<AuthTokens> refresh(String refreshToken) async {
    refreshCalls++;
    return FakeApi.fakeTokens();
  }
}
