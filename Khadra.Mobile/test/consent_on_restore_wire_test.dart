import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/features/legal/consent_prompt_screen.dart';
import 'package:khadra_mobile/main.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fake_api.dart';
import 'support/fake_push_messaging.dart';

/// Wave 7 blocker (10 Oct, second Android device): a 1.3.0 session upgraded in place to
/// "1.4.0+7", on an account with both texts pending, opened without the consent prompt, and
/// Staging received no `GET /api/v1/auth/me` from any such build all day.
///
/// The cause was the APK, not this code: its compiled Dart was an older build's (see
/// `tools/verify_apk_dart.js`). But nothing proved this path over the wire either. The
/// whole-app tests in `legal_consent_test` replace `KhadraApi.me()` itself, so they never
/// send a request. Here the REAL composition runs — `apiClientProvider`, every interceptor,
/// the real `KhadraApi` — and only the transport is replaced, by a recorder that answers the
/// way Staging answers this account. Both ways of arriving signed in are covered: a session
/// restored after an in-place upgrade, and a fresh sign-in.
void main() {
  // The two ways a 1.4.0 build comes to be signed in on this account.
  for (final path in ['restored from 1.3.0 (in-place upgrade)', 'signed in afresh (fresh install)']) {
    testWidgets('$path: reads /auth/me as 1.4.0 and raises the prompt for a pending account', (tester) async {
      tester.view.physicalSize = const Size(1000, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      SharedPreferences.setMockInitialValues({'khadra.entry_chosen': true});

      final restoring = path.startsWith('restored');
      final wire = _StagingWire();
      final container = ProviderContainer(
        overrides: [
          sessionStoreProvider.overrideWithValue(
            restoring
                ? (FakeSessionStore(refreshToken: 'stored-by-1.3.0')
                    ..refreshExpiry = DateTime.now().toUtc().add(const Duration(days: 14)))
                : FakeSessionStore(),
          ),
          sharedPreferencesProvider.overrideWithValue(await SharedPreferences.getInstance()),
          installedAppVersionProvider.overrideWithValue('1.4.0+7'),
          pushMessagingProvider.overrideWithValue(FakePushMessaging()),
        ],
      );
      container.read(apiClientProvider).raw.httpClientAdapter = wire;

      await tester.pumpWidget(UncontrolledProviderScope(container: container, child: const KhadraApp()));
      await _settle(tester);
      if (!restoring) {
        // Started, then the clock is pumped: awaited directly it never completes under the test's fake time.
        final signedIn = container.read(sessionProvider.notifier).signIn('fresh@example.com', 'not-a-real-password');
        await _settle(tester);
        await signedIn;
        await _settle(tester);
      }

      expect(container.read(sessionProvider).isSignedIn, isTrue);
      expect(wire.log, contains('GET /api/v1/auth/me'), reason: 'the session must read what is still to accept');
      expect(wire.versions['GET /api/v1/auth/me'], '1.4.0+7', reason: 'judged as the build it is');
      expect(find.byType(ConsentPromptScreen), findsOneWidget);

      // Unmounted and disposed here, so the config retry a 503 schedules goes with it.
      await tester.pumpWidget(const SizedBox());
      container.dispose();
    });
  }
}

Future<void> _settle(WidgetTester tester) async {
  for (var i = 0; i < 20; i++) {
    await tester.pump(const Duration(milliseconds: 100));
  }
}

/// Answers like Staging for an account registered from 1.3.0: both texts pending.
class _StagingWire implements HttpClientAdapter {
  final List<String> log = [];
  final Map<String, String?> versions = {};

  static final _pending = [
    {
      'kind': 'Terms',
      'slug': 'terms',
      'versionId': '01a117c7-e2dc-7f13-984c-52ccd03334b0',
      'versionLabel': 'Terms 2026-10d DRAFT',
      'effectiveFrom': '2026-10-07T19:12:18Z',
    },
    {
      'kind': 'Privacy',
      'slug': 'privacy',
      'versionId': '01a117cb-56b7-716d-a36f-80f1f80959ec',
      'versionLabel': 'Privacy 2026-10d DRAFT',
      'effectiveFrom': '2026-10-07T19:16:04Z',
    },
  ];

  static Map<String, dynamic> _user({bool withPending = false}) => {
    'id': 'user-1',
    'email': 'fresh@example.com',
    'fullName': 'Fresh Customer',
    'phone': '+962790000000',
    'role': 'Customer',
    'isEmailVerified': true,
    'mustChangePassword': false,
    'createdAt': '2026-10-10T09:55:18Z',
    if (withPending) 'pendingConsents': _pending,
  };

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    final path = options.uri.path;
    log.add('${options.method} $path');
    versions['${options.method} $path'] = options.headers['X-Khadra-App-Version'] as String?;
    final now = DateTime.now().toUtc();
    return switch ((options.method, path)) {
      ('POST', '/api/v1/auth/refresh') || ('POST', '/api/v1/auth/login') => _json(200, {
        'accessToken': 'access-1',
        'accessTokenExpiresAt': now.add(const Duration(minutes: 15)).toIso8601String(),
        'refreshToken': 'refresh-2',
        'refreshTokenExpiresAt': now.add(const Duration(days: 14)).toIso8601String(),
        'user': _user(),
      }),
      ('GET', '/api/v1/auth/me') => _json(200, _user(withPending: true)),
      ('GET', '/api/v1/auth/me/legal-consents') => _json(200, {'pending': _pending}),
      ('PUT', '/api/v1/auth/me/language') => ResponseBody.fromString('', 204),
      // Every other signed-in call is gated for a 1.4.0 build with texts pending.
      ('PUT', '/api/v1/auth/sessions/current/push-device') => _consentPending(),
      _ => _json(503, {'code': 'test.not_served'}),
    };
  }

  static ResponseBody _json(int status, Object body) => ResponseBody.fromString(
    jsonEncode(body),
    status,
    headers: {
      Headers.contentTypeHeader: ['application/json; charset=utf-8'],
    },
  );

  static ResponseBody _consentPending() => ResponseBody.fromString(
    jsonEncode({'status': 403, 'code': 'legal.consent_pending', 'pending': _pending}),
    403,
    headers: {
      Headers.contentTypeHeader: ['application/problem+json'],
    },
  );

  @override
  void close({bool force = false}) {}
}
