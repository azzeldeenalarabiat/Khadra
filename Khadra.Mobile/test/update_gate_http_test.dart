import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/khadra_api.dart';
import 'package:khadra_mobile/core/api/api_client.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/api/app_version_interceptor.dart';
import 'package:khadra_mobile/core/api/auth_interceptor.dart';
import 'package:khadra_mobile/core/config/update_requirement.dart';
import 'package:khadra_mobile/core/session/session_controller.dart';

import 'support/fake_api.dart';

/// The version gate at the wire: the header this build sends, and what a 426 does
/// to a signed-in session.
///
/// Over a REAL socket, so the real Dio and the real interceptors run — a fake
/// adapter only ever sees its own Dio. And in a file of its own, deliberately:
/// any `testWidgets` in the same file installs flutter_test's HTTP stub for the
/// whole file, which answers every request 400 before it reaches the socket — a
/// 400 the session would, correctly, treat as a verdict and sign out over.
void main() {
  group('the header', () {
    late _Server api;
    late _Server storage;

    setUp(() async {
      api = await _Server.start();
      storage = await _Server.start();
    });
    tearDown(() async {
      await api.stop();
      await storage.stop();
    });

    Dio client(String? version, {void Function(UpdateRequirement)? onRefusal}) {
      final dio = ApiClient.createDio()..options.baseUrl = api.baseUrl;
      dio.interceptors.add(AppVersionInterceptor(
        installedVersion: version,
        apiBaseUrl: api.baseUrl,
        onUpdateRequired: onRefusal ?? (_) {},
      ));
      return dio;
    }

    test('carries the installed version, build number and all, to the API', () async {
      await client('1.1.0+2').get<dynamic>('/api/v1/vehicles');

      expect(api.versions.single, '1.1.0+2');
    });

    test('is never handed to another host', () async {
      // A server-minted upload URL on another origin: no header about the app for
      // a third party, and no CORS preflight forced against a host we do not own.
      await client('1.1.0+2').put<dynamic>('${storage.baseUrl}/evidence/abc', data: 'x');

      expect(storage.versions.single, isNull);
    });

    test('is left off rather than invented when the platform would not say', () async {
      await client(null).get<dynamic>('/api/v1/vehicles');

      expect(api.versions.single, isNull);
    });

    test('a refusal with the code raises the update requirement', () async {
      UpdateRequirement? raised;
      api.refuse = _Server.updateRequired;

      await expectLater(
        client('1.0.0+1', onRefusal: (r) => raised = r).get<dynamic>('/api/v1/vehicles'),
        throwsA(isA<DioException>()),
      );

      expect(raised, isNotNull);
      expect(raised!.minimum.toString(), '1.1.0');
      expect(raised!.installed.toString(), '1.0.0');
      expect(raised!.updateUrl, Uri.parse('https://example.org/khadra.apk'));
    });

    test('a 426 WITHOUT the code does not lock the app', () async {
      // Keyed on the code, never on the status: a 426 from a proxy, or from a
      // future use of the status, is not a reason to take the app away.
      UpdateRequirement? raised;
      api.refuse = const _Refusal(426, {'title': 'Upgrade Required'});

      await expectLater(
        client('1.0.0', onRefusal: (r) => raised = r).get<dynamic>('/api/v1/vehicles'),
        throwsA(isA<DioException>()),
      );

      expect(raised, isNull);
    });
  });

  group('a signed-in session that meets a 426', () {
    late _Server api;
    late FakeSessionStore store;
    late ApiClient client;
    late SessionController session;
    var sessionEndedCalls = 0;
    UpdateRequirement? raised;

    setUp(() async {
      api = await _Server.start();
      store = FakeSessionStore(refreshToken: 'refresh-1')
        ..refreshExpiry = DateTime.now().toUtc().add(const Duration(days: 14));
      sessionEndedCalls = 0;
      raised = null;

      // The real composition, from `apiClientProvider`; only the address differs.
      final dio = ApiClient.createDio()..options.baseUrl = api.baseUrl;
      client = ApiClient(dio);
      session = SessionController(api: KhadraApi(client), store: store);
      dio.interceptors
        ..add(AppVersionInterceptor(
          installedVersion: '1.0.0+1',
          apiBaseUrl: api.baseUrl,
          onUpdateRequired: (r) => raised = r,
        ))
        ..add(AuthInterceptor(
          store: store,
          refresh: session.refresh,
          onSessionEnded: () async => sessionEndedCalls++,
          resend: (options) => dio.fetch<dynamic>(options),
        ));
    });
    tearDown(() => api.stop());

    test('keeps its stored sign-in when the token rotation is refused', () async {
      api.refuse = _Server.updateRequired;

      final rotated = await session.refresh();

      expect(rotated, isFalse);
      // The refresh token is exactly where it was: the new build opens signed in.
      expect(store.refreshToken, 'refresh-1');
      expect(store.clearCalls, 0);
      expect(session.state.endedReason, isNull);
      expect(raised, isNotNull, reason: 'and the update screen goes up');
    });

    test('keeps it through a cold start, too', () async {
      // `restore()` is what main() runs; it rotates once, and the rotation is what
      // the server refuses.
      api.refuse = _Server.updateRequired;

      await session.restore();

      expect(store.refreshToken, 'refresh-1');
      expect(store.clearCalls, 0);
      expect(session.state.endedReason, isNull);
    });

    test('never refreshes or signs out over a refused ordinary call', () async {
      store.setAccessToken('access-1', DateTime.now().toUtc().add(const Duration(minutes: 10)));
      api.refuse = _Server.updateRequired;

      await expectLater(
        client.get<dynamic>('/api/v1/bookings'),
        throwsA(isA<ApiFailure>().having((f) => f.isUpdateRequired, 'isUpdateRequired', isTrue)),
      );

      // No rotation was attempted — a 426 is not a 401 — and nothing ended.
      expect(api.calls, ['GET /api/v1/bookings']);
      expect(sessionEndedCalls, 0);
      expect(store.refreshToken, 'refresh-1');
    });
  });
}

class _Refusal {
  const _Refusal(this.status, this.body);
  final int status;
  final Map<String, dynamic> body;
}

/// A throwaway HTTP server on the loopback, so the real Dio and the real
/// interceptors run — a fake adapter would only ever see its own Dio.
class _Server {
  _Server(this._server);

  final HttpServer _server;

  /// What each request carried as `X-Khadra-App-Version` (null when absent).
  final List<String?> versions = <String?>[];
  final List<String> calls = <String>[];

  /// Answer every request with this, when set.
  _Refusal? refuse;

  static const updateRequired = _Refusal(426, {
    'type': 'https://httpstatuses.com/426',
    'title': 'Update the Khadra app to continue',
    'status': 426,
    'code': 'app.update_required',
    'minimumSupportedVersion': '1.1.0',
    'updateUrl': 'https://example.org/khadra.apk',
  });

  String get baseUrl => 'http://${_server.address.host}:${_server.port}';

  static Future<_Server> start() async {
    final server = _Server(await HttpServer.bind(InternetAddress.loopbackIPv4, 0));
    unawaited(server._serve());
    return server;
  }

  Future<void> _serve() async {
    await for (final request in _server) {
      calls.add('${request.method} ${request.uri.path}');
      versions.add(request.headers.value('X-Khadra-App-Version'));
      await request.drain<void>();

      final refusal = refuse;
      request.response
        ..statusCode = refusal?.status ?? 200
        ..headers.contentType = ContentType.json
        ..write(jsonEncode(refusal?.body ?? const {'ok': true}));
      unawaited(request.response.close());
    }
  }

  Future<void> stop() => _server.close(force: true);
}
