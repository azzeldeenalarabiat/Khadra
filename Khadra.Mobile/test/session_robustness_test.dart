import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/api/khadra_api.dart';
import 'package:khadra_mobile/core/api/api_client.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/api/auth_interceptor.dart';
import 'package:khadra_mobile/core/session/session_controller.dart';
import 'package:khadra_mobile/core/session/session_store.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fake_api.dart';

/// The ways a valid session used to be lost on the phone, each held down by a test that
/// failed on `59abeff` (Wave 7; pre-launch items 95, 126, 127 and 128, and the latent
/// "a store that did not answer" bug the advisor found beside them).
///
/// Refresh tokens ROTATE. Presenting one consumes it; presenting a consumed one again is
/// replay, which the server forgives for sixty seconds — handing the late caller a NEW pair
/// and retiring the replacement it gave the first — and after that revokes the token's whole
/// family, which on a phone is the customer's session. So every test here comes down to
/// one question: is the token presented the newest one this phone holds, presented once?
///
/// Over a REAL loopback socket (see `token_rotation_test.dart` for why), against a server
/// that keeps that rotation strictly, including the grace and the revocation.
void main() {
  late _StrictApi api;
  late _Keychain keychain;
  late SessionStore store;
  late Dio dio;
  late ApiClient client;
  late SessionController session;


  setUp(() async {
    api = await _StrictApi.start();
    keychain = _Keychain();


    SharedPreferences.setMockInitialValues(<String, Object>{});
    store = SessionStore(secureStorage: keychain, preferences: await SharedPreferences.getInstance());

    // The real composition, from `apiClientProvider`. Only the address differs, and the
    // receive timeout, so a rotation that is never answered times out in a test's time.
    dio = ApiClient.createDio()
      ..options.baseUrl = api.baseUrl
      ..options.receiveTimeout = const Duration(milliseconds: 400);
    client = ApiClient(dio);
    session = SessionController(api: KhadraApi(client), store: store);
    dio.interceptors.add(AuthInterceptor(
      store: store,
      refresh: session.refresh,
      onSessionEnded: () async {

        await session.endSession();
      },
      resend: (options) => dio.fetch<dynamic>(options),
    ));
  });

  tearDown(() => api.stop());

  /// Signed in on `refresh-1`, the way a sign-in leaves the phone.
  Future<void> signedIn({bool accessStale = false}) async {
    await session.adoptTokens(_StrictApi.tokensOf(1));
    if (accessStale) {
      store.setAccessToken('access-1', DateTime.now().toUtc().add(const Duration(seconds: 30)));
    }
  }

  /// Waits, in real time, until the server has seen [count] presentations.
  Future<void> presentedAtLeast(int count) async {
    for (var i = 0; i < 200 && api.presented.length < count; i++) {
      await Future<void>.delayed(const Duration(milliseconds: 10));
    }
    expect(api.presented.length, greaterThanOrEqualTo(count), reason: 'the server never saw the rotation');
  }

  // ── 95: one gate, whoever asks for the rotation ────────────────────────────────────────────

  group('95: every presenter of the refresh token shares one rotation', () {
    test('95-A: a request made during the cold-start rotation joins it', () async {
      await store.saveRefreshToken('refresh-1', DateTime.now().toUtc().add(const Duration(days: 14)));
      api.holdRefreshes = Completer<void>();

      final restoring = session.restore();
      await presentedAtLeast(1);
      // A screen asks for something while the launch rotation is still on its way.
      final bookings = client.get<dynamic>('/api/v1/bookings');
      await Future<void>.delayed(const Duration(milliseconds: 100));
      api.holdRefreshes!.complete();
      await restoring;
      await bookings;

      expect(api.presented, ['refresh-1'], reason: 'one token, presented once');
      expect(api.bearerOf('GET /api/v1/bookings'), 'access-2');
      expect(session.state.isSignedIn, isTrue);
      expect(api.revoked, isFalse);
    }, timeout: const Timeout(Duration(seconds: 20)));

    test('95-B: refresh() is single flight', () async {
      await signedIn();
      api.holdRefreshes = Completer<void>();

      final first = session.refresh();
      final second = session.refresh();
      await presentedAtLeast(1);
      await Future<void>.delayed(const Duration(milliseconds: 100));
      api.holdRefreshes!.complete();

      expect(await Future.wait([first, second]), [true, true]);
      expect(api.presented, ['refresh-1']);
      expect(await store.readRefreshToken(), 'refresh-2');
    }, timeout: const Timeout(Duration(seconds: 20)));
  });

  // Advisor's review of Wave 7: a rotation's answer can take forty seconds and more, and nothing stopped it being
  // applied to a session that had ended or changed hands in the meantime.
  group('a rotation answered after its session ended or was replaced', () {
    test('95-C: a sign-out during the rotation stays a sign-out', () async {
      await signedIn();
      api.holdRefreshes = Completer<void>();

      final rotating = session.refresh();
      await presentedAtLeast(1);
      await session.signOut();
      api.holdRefreshes!.complete();

      expect(await rotating, isFalse);
      expect(api.presented, ['refresh-1']);
      expect(session.state.isSignedIn, isFalse);
      expect(await store.readRefreshToken(), isNull);
      expect(store.accessToken, isNull);
    }, timeout: const Timeout(Duration(seconds: 20)));

    test('95-D: a new session begun meanwhile keeps its own tokens', () async {
      await signedIn();
      api.holdRefreshes = Completer<void>();

      final rotating = session.refresh();
      await presentedAtLeast(1);
      await session.signOut();
      await session.adoptTokens(_StrictApi.tokensOf(9));
      api.holdRefreshes!.complete();

      expect(await rotating, isFalse);
      expect(await store.readRefreshToken(), 'refresh-9');
      expect(store.accessToken, 'access-9');
      expect(session.state.isSignedIn, isTrue);
    }, timeout: const Timeout(Duration(seconds: 20)));

    test('95-E: a password change during a rotation the server refuses keeps the new pair', () async {
      await signedIn();
      api
        ..holdRefreshes = Completer<void>()
        // The change revoked every other family, so the old token is refused.
        ..refreshFailures.add(_Failure.unauthorized);

      final rotating = session.refresh();
      await presentedAtLeast(1);
      await session.adoptTokens(_StrictApi.tokensOf(9));
      api.holdRefreshes!.complete();

      expect(await rotating, isFalse);
      expect(session.state.isSignedIn, isTrue, reason: 'a verdict on the old session is not one on this');
      expect(await store.readRefreshToken(), 'refresh-9');
    }, timeout: const Timeout(Duration(seconds: 20)));

    test('a retry refused after the token moved again does not end the new session', () async {
      await signedIn();
      api
        ..refuseOnce = 'GET /api/v1/bookings'
        ..refuseRetry = 'GET /api/v1/bookings'
        ..retryHeld = Completer<void>();

      final bookings = client.get<dynamic>('/api/v1/bookings');
      await api.retryArrived.future;
      // A password change lands while the retry, on access-2, is out.
      await session.adoptTokens(_StrictApi.tokensOf(9));
      api.retryHeld!.complete();

      await expectLater(bookings, throwsA(isA<ApiFailure>()));
      expect(session.state.isSignedIn, isTrue);
      expect(store.accessToken, 'access-9');
    }, timeout: const Timeout(Duration(seconds: 20)));
  });

  // ── 126: the newest token, whatever the disk did ───────────────────────────────────────────

  group('126: the token presented is the newest one, whatever the secure store did', () {
    // Outside the grace: presenting anything but the newest token revokes the family.
    setUp(() => api.graceOpen = false);

    test('126-A: a write that failed still presents the token the server just issued', () async {
      await signedIn();
      keychain.writesFail = true;

      expect(await session.refresh(), isTrue);
      expect(await session.refresh(), isTrue);

      expect(api.presented, ['refresh-1', 'refresh-2']);
      expect(api.revoked, isFalse);
      expect(session.state.isSignedIn, isTrue);
    }, timeout: const Timeout(Duration(seconds: 20)));

    test('126-B: so does a write that never answers', () async {
      await signedIn();
      keychain.hangNextTokenWrite = true;

      final rotating = session.refresh();
      await keychain.tokenWriteStarted.future;
      // While the disk is stuck, any request asking which token to present is told the new one.
      expect(await store.readRefreshToken(), 'refresh-2');
      expect(await rotating, isTrue, reason: 'the store gives up on the write after its bound');

      expect(await session.refresh(), isTrue);
      expect(api.presented, ['refresh-1', 'refresh-2']);
      expect(api.revoked, isFalse);
    }, timeout: const Timeout(Duration(seconds: 30)));

    test('126-C: rotation after rotation, each presents its predecessor\'s replacement', () async {
      await signedIn();
      keychain.writesFail = true;

      for (var i = 0; i < 3; i++) {
        expect(await session.refresh(), isTrue);
      }

      expect(api.presented, ['refresh-1', 'refresh-2', 'refresh-3']);
      expect(api.revoked, isFalse);
    }, timeout: const Timeout(Duration(seconds: 20)));

    test('126-D: clear() forgets the token in memory too, even when the disk keeps it', () async {
      // A device with no preferences cannot record ownership, so only the memory can say
      // the session ended.
      final bare = SessionStore(secureStorage: keychain);
      await bare.saveRefreshToken('refresh-1', DateTime.now().toUtc().add(const Duration(days: 14)));
      keychain.deletesFail = true;

      await bare.clear();

      expect(keychain.values['khadra.refresh_token'], 'refresh-1', reason: 'the delete did not take');
      expect(await bare.readRefreshToken(), isNull);
      expect(await bare.readRefreshExpiry(), isNull);
    });
  });

  // ── 127: an upload refused on a stale token ────────────────────────────────────────────────

  test('127: an upload refused on a stale token is rotated and sent again, byte for byte', () async {
    await signedIn();
    api.refuseOnce = 'POST /api/v1/customers/me/documents';

    final document = await KhadraApi(client).uploadDocument(
      type: 'DrivingLicenceFront',
      bytes: List<int>.generate(4096, (i) => i % 251),
      fileName: 'licence.jpg',
      contentType: 'image/jpeg',
    );

    expect(document.type, 'DrivingLicenceFront');
    expect(api.uploads, hasLength(2), reason: 'refused once, then sent again');
    expect(api.uploads.last.body, api.uploads.first.body, reason: 'the same multipart body, boundary and all');
    expect(api.uploads.last.contentType, api.uploads.first.contentType);
    expect(api.uploads.last.bearer, 'access-2');
    expect(api.presented, ['refresh-1']);
  }, timeout: const Timeout(Duration(seconds: 20)));

  // ── 128: a rotation that timed out ────────────────────────────────────────────────────────

  group('128: a rotation that timed out is presented again at once, and nothing else is', () {
    test('128-A: a timeout is retried immediately with the same token, and the new pair kept', () async {
      await signedIn();
      // The server rotates and its answer is lost on the way back.
      api.unansweredRefreshes = 1;

      expect(await session.refresh(), isTrue);

      expect(api.presented, ['refresh-1', 'refresh-1']);
      // The in-grace retry is handed a NEW pair; the lost answer's refresh-2 is retired.
      expect(await store.readRefreshToken(), 'refresh-3');
      expect(store.accessToken, 'access-3');
      expect(api.revoked, isFalse);
    }, timeout: const Timeout(Duration(seconds: 20)));

    test('128-B: two timeouts are two presentations, and the session survives them', () async {
      await signedIn();
      api.unansweredRefreshes = 2;

      expect(await session.refresh(), isFalse);

      expect(api.presented, ['refresh-1', 'refresh-1']);
      expect(session.state.isSignedIn, isTrue, reason: 'a timeout is not a verdict');
      expect(await store.readRefreshToken(), isNotNull);
    }, timeout: const Timeout(Duration(seconds: 20)));

    for (final (label, failure) in [
      ('a 401', _Failure.unauthorized),
      ('a 500', _Failure.serverError),
      ('a dropped connection', _Failure.dropped),
      // Item 240: the server's answer to the loser of a concurrent rotation. A network event, not a verdict.
      ('a lost race (503 auth.refresh_conflict)', _Failure.raceLost),
    ]) {
      test('128-C: $label is presented once, never retried', () async {
        await signedIn();
        api.refreshFailures.add(failure);

        expect(await session.refresh(), isFalse);

        expect(api.presented, ['refresh-1']);
        // Only the 401 is a verdict. Every other answer leaves the token exactly where it was, to present again.
        expect(session.state.isSignedIn, failure != _Failure.unauthorized);
        if (failure != _Failure.unauthorized) expect(await store.readRefreshToken(), 'refresh-1');
      }, timeout: const Timeout(Duration(seconds: 20)));
    }

    test('128-E: a timeout spent connecting is not retried: nothing reached the server', () async {
      final api = _ConnectTimingOutApi();
      final controller = SessionController(api: api, store: FakeSessionStore());
      await controller.adoptTokens(FakeApi.fakeTokens());

      expect(await controller.refresh(), isFalse);

      expect(api.refreshCalls, 1);
      expect(controller.state.isSignedIn, isTrue);
    });

    test('a connect timeout is told apart from a lost answer, and both still read as a timeout', () {
      ApiFailure of(DioExceptionType type) =>
          ApiFailure.from(DioException(requestOptions: RequestOptions(path: '/api/v1/auth/refresh'), type: type));

      expect(of(DioExceptionType.connectionTimeout).neverConnected, isTrue);
      expect(of(DioExceptionType.receiveTimeout).neverConnected, isFalse);
      expect(of(DioExceptionType.sendTimeout).neverConnected, isFalse);
      for (final type in [DioExceptionType.connectionTimeout, DioExceptionType.receiveTimeout]) {
        expect(of(type).kind, ApiFailureKind.timeout);
      }
    });

    test('128-D: the retry happens inside the one rotation; a request arriving meanwhile waits for it', () async {
      await signedIn(accessStale: true);
      api.unansweredRefreshes = 1;

      final rotating = session.refresh();
      await presentedAtLeast(1);
      final bookings = client.get<dynamic>('/api/v1/bookings');
      await Future<void>.delayed(const Duration(milliseconds: 150));
      // Still inside the first attempt's timeout: nobody else has presented anything.
      expect(api.presented, ['refresh-1']);

      expect(await rotating, isTrue);
      await bookings;
      expect(api.presented, ['refresh-1', 'refresh-1']);
      expect(api.bearerOf('GET /api/v1/bookings'), 'access-3');
      expect(api.revoked, isFalse);
    }, timeout: const Timeout(Duration(seconds: 20)));
  });

  // ── 4(a): "did not answer" is not "no token" ──────────────────────────────────────────────

  group('4(a): a secure store that did not answer ends nothing', () {
    test('a rotation asked of a silent store leaves the session alone', () async {
      final silent = FakeSessionStore(refreshToken: 'refresh-1');
      final controller = SessionController(api: FakeApi(), store: silent);
      await controller.adoptTokens(FakeApi.fakeTokens());
      silent.unreadable = true;

      expect(await controller.refresh(), isFalse);

      expect(controller.state.isSignedIn, isTrue);
      expect(silent.clearCalls, 0);
    });

    test('a 401 met while the store is silent does not end the session', () async {
      final silent = FakeSessionStore(refreshToken: 'refresh-1')..unreadable = true;
      var ended = 0;
      final local = Dio(BaseOptions(baseUrl: api.baseUrl));
      local.interceptors.add(AuthInterceptor(
        store: silent,
        refresh: () async => false,
        onSessionEnded: () async => ended++,
        resend: (options) => local.fetch<dynamic>(options),
      ));
      api.refuseOnce = 'GET /api/v1/bookings';

      await expectLater(local.get<dynamic>('/api/v1/bookings'), throwsA(isA<DioException>()));

      expect(ended, 0);
    }, timeout: const Timeout(Duration(seconds: 20)));
  });

  // Found beside these in Wave 7: since 1.4.0 re-reads the account on every sign-in (the
  // consent check), a sign-out moments later raced the read.
  test('an account read that answers after a sign-out does not sign back in', () async {
    final slow = _SlowMeApi();
    final controller = SessionController(api: slow, store: FakeSessionStore());
    await controller.adoptTokens(FakeApi.fakeTokens());

    final reading = controller.reload();
    await controller.signOut();
    slow.answer.complete();
    await reading;

    expect(controller.state.isSignedIn, isFalse);
  });
}

/// A rotation that never reaches the server: the connect itself times out.
class _ConnectTimingOutApi extends FakeApi {
  int refreshCalls = 0;

  @override
  Future<AuthTokens> refresh(String refreshToken) async {
    refreshCalls++;
    throw const ApiFailure(kind: ApiFailureKind.timeout, neverConnected: true);
  }
}

class _SlowMeApi extends FakeApi {
  final answer = Completer<void>();

  @override
  Future<AuthUser> me() async {
    await answer.future;
    return FakeApi.fakeUser();
  }
}

enum _Failure { unauthorized, serverError, dropped, raceLost }

/// One upload as the server received it.
typedef _Upload = ({List<int> body, String? contentType, String? bearer});

/// A loopback API that rotates refresh tokens the way `RefreshTokensHandler` does.
class _StrictApi {
  _StrictApi(this._server);

  final HttpServer _server;

  /// The newest refresh token is `refresh-$_generation`.
  int _generation = 1;
  final Set<String> _consumed = <String>{};

  /// Whether a consumed token presented again is inside the sixty-second grace.
  bool graceOpen = true;

  /// Set when a replay outside the grace revoked the family: every rotation is refused.
  bool revoked = false;

  /// Every refresh token presented, in order.
  final List<String> presented = <String>[];

  final List<String> calls = <String>[];
  final List<String?> bearers = <String?>[];
  final List<_Upload> uploads = <_Upload>[];

  /// When set, rotations are answered only once it completes.
  Completer<void>? holdRefreshes;

  /// The next this-many rotations are carried out and never answered: the lost reply.
  int unansweredRefreshes = 0;

  /// Failures for the next rotations, in order, instead of a rotation.
  final List<_Failure> refreshFailures = <_Failure>[];

  /// When set, the first request matching it comes back 401.
  String? refuseOnce;

  /// When set, the next request matching it (after any [refuseOnce]) is held on [retryHeld],
  /// then refused 401: a retry that is out while something else changes.
  String? refuseRetry;
  Completer<void>? retryHeld;
  final Completer<void> retryArrived = Completer<void>();

  final List<HttpRequest> _unanswered = <HttpRequest>[];

  String get baseUrl => 'http://${_server.address.host}:${_server.port}';

  String? bearerOf(String call) {
    final at = calls.lastIndexOf(call);
    return at < 0 ? null : bearers[at];
  }

  static Future<_StrictApi> start() async {
    final api = _StrictApi(await HttpServer.bind(InternetAddress.loopbackIPv4, 0));
    // Each request on its own: a held or unanswered rotation must not hold up the rest.
    api._server.listen((request) => unawaited(api._handle(request)));
    return api;
  }

  Future<void> _handle(HttpRequest request) async {
    final call = '${request.method} ${request.uri.path}';
    final bearer = request.headers.value(HttpHeaders.authorizationHeader)?.replaceFirst('Bearer ', '');
    final body = await request.fold<List<int>>(<int>[], (all, chunk) => all..addAll(chunk));
    calls.add(call);
    bearers.add(bearer);

    if (refuseOnce == call) {
      refuseOnce = null;
      if (call.endsWith('/documents')) {
        uploads.add((body: body, contentType: request.headers.contentType?.toString(), bearer: bearer));
      }
      return _answer(request, 401, {'title': 'Unauthorized'});
    }

    if (refuseRetry == call) {
      refuseRetry = null;
      retryArrived.complete();
      await retryHeld?.future;
      return _answer(request, 401, {'title': 'Unauthorized'});
    }

    if (request.uri.path.endsWith('/auth/refresh')) return _rotate(request, body);

    if (call.endsWith('/documents')) {
      uploads.add((body: body, contentType: request.headers.contentType?.toString(), bearer: bearer));
      return _answer(request, 201, {
        'documentId': 'd-1',
        'type': 'DrivingLicenceFront',
        'status': 'PendingReview',
        'contentType': 'image/jpeg',
        'sizeBytes': 4096,
        'uploadedAt': '2026-10-10T08:00:00Z',
        'reviewNote': null,
      });
    }

    return _answer(request, 200, const {'items': <dynamic>[], 'page': 1, 'pageSize': 20, 'totalCount': 0});
  }

  Future<void> _rotate(HttpRequest request, List<int> body) async {
    final token = (jsonDecode(utf8.decode(body)) as Map<String, dynamic>)['refreshToken'] as String;
    presented.add(token);
    // Held as it arrives, whatever its answer will be.
    await holdRefreshes?.future;

    if (refreshFailures.isNotEmpty) {
      switch (refreshFailures.removeAt(0)) {
        case _Failure.unauthorized:
          return _answer(request, 401, {'code': 'auth.invalid_refresh_token'});
        case _Failure.serverError:
          return _answer(request, 500, {'title': 'Internal Server Error'});
        case _Failure.raceLost:
          return _answer(request, 503, {'code': 'auth.refresh_conflict', 'status': 503});
        case _Failure.dropped:
          final socket = await request.response.detachSocket(writeHeaders: false);
          socket.destroy();
          return;
      }
    }

    if (revoked) return _answer(request, 401, {'code': 'auth.invalid_refresh_token'});
    if (token == 'refresh-$_generation') {
      _consumed.add(token);
      _generation++;
    } else if (_consumed.contains(token) && graceOpen) {
      // In the grace: a NEW pair, and the replacement handed to the first caller retired.
      _generation++;
    } else {
      revoked = true;
      return _answer(request, 401, {'code': 'auth.invalid_refresh_token'});
    }

    if (unansweredRefreshes > 0) {
      unansweredRefreshes--;
      _unanswered.add(request);
      return;
    }
    return _answer(request, 200, tokensJson(_generation));
  }

  static void _answer(HttpRequest request, int status, Map<String, dynamic> body) {
    request.response
      ..statusCode = status
      ..headers.contentType = ContentType.json
      ..write(jsonEncode(body));
    unawaited(request.response.close());
  }

  static Map<String, dynamic> tokensJson(int generation) => {
        'accessToken': 'access-$generation',
        'accessTokenExpiresAt': DateTime.now().toUtc().add(const Duration(minutes: 15)).toIso8601String(),
        'refreshToken': 'refresh-$generation',
        'refreshTokenExpiresAt': DateTime.now().toUtc().add(const Duration(days: 14)).toIso8601String(),
        'user': {
          'id': '01a07e16-de7b-7673-b1a5-c47bc1d437f4',
          'email': 'layla@example.jo',
          'fullName': 'Layla Odeh',
          'phone': '+962791234567',
          'role': 'Customer',
          'isEmailVerified': true,
          'mustChangePassword': false,
          'createdAt': '2026-01-05T00:00:00Z',
        },
      };

  static AuthTokens tokensOf(int generation) => AuthTokens.fromJson(tokensJson(generation));

  Future<void> stop() async {
    for (final request in _unanswered) {
      try {
        await request.response.close();
      } on Object {
        // Already gone with its client.
      }
    }
    await _server.close(force: true);
  }
}

/// The secure store, in memory, which can be told to fail or hang its writes and deletes.
class _Keychain extends FlutterSecureStorage {
  final Map<String, String> values = <String, String>{};

  bool writesFail = false;
  bool deletesFail = false;

  /// The next write of the refresh token never answers; the store's own bound ends it.
  bool hangNextTokenWrite = false;
  final Completer<void> tokenWriteStarted = Completer<void>();

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
    if (key == 'khadra.refresh_token' && hangNextTokenWrite) {
      hangNextTokenWrite = false;
      if (!tokenWriteStarted.isCompleted) tokenWriteStarted.complete();
      await Completer<void>().future;
    }
    if (writesFail) throw Exception('the keystore refused the write');
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
  }) async =>
      values[key];

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
    if (deletesFail) throw Exception('the keystore is not available');
    values.remove(key);
  }
}
