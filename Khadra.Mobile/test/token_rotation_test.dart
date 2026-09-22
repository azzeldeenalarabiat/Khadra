import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/khadra_api.dart';
import 'package:khadra_mobile/core/api/api_client.dart';
import 'package:khadra_mobile/core/api/auth_interceptor.dart';
import 'package:khadra_mobile/core/session/session_controller.dart';
import 'package:khadra_mobile/core/session/session_store.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// The token rotation, and the deadlock that used to be in it.
///
/// [AuthInterceptor] used to be a `QueuedInterceptor`: every request took its turn
/// in one queue, and `onRequest` HELD that turn while it awaited a refresh. The
/// rotation is itself a request, so it waited for a slot that could not free until
/// it had finished. Neither ever moved — and because the queue was shared, it was
/// not one request that stalled but every request the app made from that moment
/// on.
///
/// It was reported as "the bookings list stays loading and the requests repeat".
/// It was neither a loading bug nor a repeat: the app was making NO requests at
/// all, from roughly four minutes after each sign-in — the access token's life
/// less the sixty-second stale margin — until it was killed. A spinner is simply
/// what a screen shows when its fetch never answers.
///
/// Only the PROACTIVE rotation deadlocked, the one `onRequest` performs. The 401
/// path was fine, because by the time `onError` ran the request that failed had
/// already left the queue. Both are asserted below, because "the reactive path is
/// safe" is the kind of fact that is true until somebody moves a line.
///
/// The queue is gone. It was buying ORDERING; what this needs is SINGLE FLIGHT,
/// which `_refreshOnce` provides — so the tests that matter most here are the ones
/// counting rotations, not the ones counting milliseconds.
///
/// **These talk to a REAL socket**, a throwaway HTTP server on the loopback
/// address, rather than to a Dio adapter. That is deliberate. `httpClientAdapter`
/// belongs to a `Dio` INSTANCE and not to its `BaseOptions`, so a test that fakes
/// the adapter can only see the requests made on the Dio it faked — and the whole
/// question here was WHICH client each request goes out on. A socket sees every
/// request whichever object sent it, and it caught a first version of this fix
/// whose rotation was quietly escaping to the real network.
///
/// **The `Timeout` is the assertion** for the deadlock tests. Without the fix they
/// do not fail an expectation, they hang — so a generous-but-finite bound is what
/// turns the defect into a red test rather than a stuck suite.
void main() {
  late _Api api; // the throwaway server
  late SessionStore store;
  late ApiClient client;
  late SessionController session;

  setUp(() async {
    api = await _Api.start();

    SharedPreferences.setMockInitialValues(<String, Object>{});
    final preferences = await SharedPreferences.getInstance();
    store = SessionStore(secureStorage: _Keychain(), preferences: preferences);

    // The real composition, from `apiClientProvider`. Only the address differs.
    final dio = ApiClient.createDio()..options.baseUrl = api.baseUrl;
    client = ApiClient(dio);
    session = SessionController(api: KhadraApi(client), store: store);
    dio.interceptors.add(AuthInterceptor(
      store: store,
      refresh: session.refresh,
      onSessionEnded: () async {},
      resend: (options) => dio.fetch<dynamic>(options),
    ));
  });

  tearDown(() => api.stop());

  Future<void> storeARefreshToken() => store.saveRefreshToken(
        'refresh-1',
        DateTime.now().toUtc().add(const Duration(days: 14)),
      );

  /// An access token inside the last minute of its life, which is the window
  /// `SessionStore.accessTokenIsStale` refreshes in.
  Future<void> signedInWithAnExpiringToken() async {
    await storeARefreshToken();
    store.setAccessToken(
      'access-1',
      DateTime.now().toUtc().add(const Duration(seconds: 30)),
    );
  }

  test(
    'a request sent with a stale token completes instead of deadlocking',
    () async {
      await signedInWithAnExpiringToken();

      await client.get<dynamic>('/api/v1/bookings', query: {'tab': 'all'});

      // The rotation went first, and the original request followed it out.
      expect(api.calls, [
        'POST /api/v1/auth/refresh',
        'GET /api/v1/bookings',
      ]);

      // And it carried the NEW token, not the one that was about to expire. A
      // request that refreshed and then sent the stale bearer anyway would have
      // come back 401 and started the whole dance again.
      expect(api.bearers.last, 'access-2');
    },
    timeout: const Timeout(Duration(seconds: 20)),
  );

  test(
    'a 401 is refreshed and retried instead of deadlocking',
    () async {
      await storeARefreshToken();
      // NOT stale — the server refuses it anyway. This is the reactive path, which
      // exists because device clocks lie.
      store.setAccessToken(
        'access-1',
        DateTime.now().toUtc().add(const Duration(minutes: 10)),
      );
      api.refuseOnce = 'GET /api/v1/bookings';

      await client.get<dynamic>('/api/v1/bookings', query: {'tab': 'all'});

      expect(api.calls, [
        'GET /api/v1/bookings', // refused
        'POST /api/v1/auth/refresh',
        'GET /api/v1/bookings', // retried, and answered
      ]);
      expect(api.bearers.last, 'access-2');
    },
    timeout: const Timeout(Duration(seconds: 20)),
  );

  test(
    'several requests arriving on a stale token rotate exactly once',
    () async {
      // This is why the rotation is single-flight, and why skipping the queue must
      // not quietly undo it. Refresh tokens ROTATE: presenting one consumes it,
      // and presenting a consumed one outside the grace revokes the whole family
      // and signs the customer out of every device. Three screens opening at once
      // must produce one rotation, not three.
      await signedInWithAnExpiringToken();

      await Future.wait([
        client.get<dynamic>('/api/v1/bookings', query: {'tab': 'all'}),
        client.get<dynamic>('/api/v1/bookings/tab-counts'),
        client.get<dynamic>('/api/v1/bookings/next'),
      ]);

      expect(api.calls.where((c) => c.endsWith('/auth/refresh')).length, 1);
      expect(api.calls.length, 4);

      // Every one of them carried the rotated token. None went out on the stale
      // one and none went out with no bearer at all.
      expect(api.bearers.where((b) => b == 'access-2').length, 3);
    },
    timeout: const Timeout(Duration(seconds: 20)),
  );

  test(
    'two requests refused on the same token rotate once between them',
    () async {
      // The reactive twin of the test above, and the one the QUEUE used to get
      // wrong in the other direction: it made these SEQUENTIAL rotations, because
      // `onError` never asked whether the token had already moved. Now the second
      // one either joins the rotation in flight or notices it has already
      // happened, and either way one token is presented once.
      await storeARefreshToken();
      store.setAccessToken(
        'access-1',
        DateTime.now().toUtc().add(const Duration(minutes: 10)),
      );
      api.refuseBearer = 'access-1';

      await Future.wait([
        client.get<dynamic>('/api/v1/bookings', query: {'tab': 'all'}),
        client.get<dynamic>('/api/v1/bookings/tab-counts'),
      ]);

      expect(api.calls.where((c) => c.endsWith('/auth/refresh')).length, 1);
      // Two refused, one rotation, two retried. Nothing else.
      expect(api.calls.length, 5);
    },
    timeout: const Timeout(Duration(seconds: 20)),
  );

  test(
    'the rotation itself goes out carrying no bearer',
    () async {
      // `AuthInterceptor.anonymous` on that one call is load-bearing, not tidiness:
      // without it the rotation enters `onRequest`, finds the token stale, and
      // awaits the completer it is itself on its way to completing.
      await signedInWithAnExpiringToken();

      await client.get<dynamic>('/api/v1/bookings', query: {'tab': 'all'});

      final rotation = api.calls.indexOf('POST /api/v1/auth/refresh');
      expect(rotation, isNot(-1), reason: 'the rotation should have happened');
      expect(api.bearers[rotation], isNull);
    },
    timeout: const Timeout(Duration(seconds: 20)),
  );

  test(
    'the cold-start rotation completes',
    () async {
      // `restore()` runs before the first screen asks for anything, so there is no
      // queued request for it to deadlock against today. It is here so that the day
      // somebody routes the rotation back through the shared client, this says so
      // too rather than waiting for a customer to find it.
      await storeARefreshToken();

      await session.restore();

      expect(session.state.isSignedIn, isTrue);
      expect(api.calls, ['POST /api/v1/auth/refresh']);
    },
    timeout: const Timeout(Duration(seconds: 20)),
  );
}

/// A throwaway API on the loopback address.
class _Api {
  _Api(this._server);

  final HttpServer _server;

  final List<String> calls = <String>[];

  /// The bearer each call arrived with, in the same order as [calls]. Null means
  /// the request carried no `Authorization` header at all.
  final List<String?> bearers = <String?>[];

  /// When set, the first request matching it comes back 401 — a security stamp
  /// that moved, or a clock that lied.
  String? refuseOnce;

  /// When set, EVERY request carrying this bearer comes back 401. What a server
  /// does with a token it no longer accepts.
  String? refuseBearer;

  int _rotations = 1;

  String get baseUrl => 'http://${_server.address.host}:${_server.port}';

  static Future<_Api> start() async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final api = _Api(server);
    unawaited(api._serve());
    return api;
  }

  Future<void> _serve() async {
    await for (final request in _server) {
      final call = '${request.method} ${request.uri.path}';
      final bearer = request.headers
          .value(HttpHeaders.authorizationHeader)
          ?.replaceFirst('Bearer ', '');
      calls.add(call);
      bearers.add(bearer);

      // Drained whether or not the body matters, so the socket can be reused.
      await request.drain<void>();

      if (refuseOnce == call || (bearer != null && bearer == refuseBearer)) {
        if (refuseOnce == call) refuseOnce = null;
        _answer(request, 401, {'title': 'Unauthorized'});
        continue;
      }

      if (request.uri.path.endsWith('/auth/refresh')) {
        _rotations++;
        _answer(request, 200, _tokens(_rotations));
        continue;
      }

      _answer(request, 200, const {
        'items': <dynamic>[],
        'page': 1,
        'pageSize': 20,
        'totalCount': 0,
        'hasNext': false,
      });
    }
  }

  static void _answer(HttpRequest request, int status, Map<String, dynamic> body) {
    request.response
      ..statusCode = status
      ..headers.contentType = ContentType.json
      ..write(jsonEncode(body));
    unawaited(request.response.close());
  }

  static Map<String, dynamic> _tokens(int generation) => {
        'accessToken': 'access-$generation',
        'accessTokenExpiresAt': DateTime.now()
            .toUtc()
            .add(const Duration(minutes: 15))
            .toIso8601String(),
        'refreshToken': 'refresh-$generation',
        'refreshTokenExpiresAt':
            DateTime.now().toUtc().add(const Duration(days: 14)).toIso8601String(),
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

  Future<void> stop() => _server.close(force: true);
}

/// The secure store, in memory. Same shape as the one in `session_store_test`.
class _Keychain extends FlutterSecureStorage {
  final Map<String, String> values = <String, String>{};

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
    values.remove(key);
  }

  @override
  Future<void> deleteAll({
    AppleOptions? iOptions,
    AndroidOptions? aOptions,
    LinuxOptions? lOptions,
    WebOptions? webOptions,
    AppleOptions? mOptions,
    WindowsOptions? wOptions,
  }) async {
    values.clear();
  }
}
