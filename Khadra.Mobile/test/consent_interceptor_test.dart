import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/api/consent_interceptor.dart';

/// From 1.4.0 the API refuses a signed-in call with `403 legal.consent_pending`
/// while a text in force is still to accept (pre-launch item 238). Seen on any call,
/// that puts the consent prompt in front of the app. Keyed on the code AND the
/// status: a 403 for a role or a policy is not a reason to ask for consent.
void main() {
  late _Adapter adapter;
  late Dio dio;
  late int raised;

  setUp(() {
    raised = 0;
    adapter = _Adapter();
    dio = Dio(BaseOptions(baseUrl: 'https://api.khadra.test'))
      ..httpClientAdapter = adapter
      ..interceptors.add(ConsentInterceptor(onConsentPending: () => raised++));
  });

  Future<void> call() async {
    try {
      await dio.get<dynamic>('/api/v1/bookings');
    } on DioException {
      // The failure still reaches its caller; the prompt covers that screen.
    }
  }

  test('raises the prompt on the code, and still hands the failure to the caller', () async {
    adapter.answer = (403, {'code': 'legal.consent_pending', 'status': 403});

    await expectLater(dio.get<dynamic>('/api/v1/bookings'), throwsA(isA<DioException>()));

    expect(raised, 1);
  });

  test('ignores a 403 for anything else', () async {
    adapter.answer = (403, {'code': 'auth.forbidden'});
    await call();
    adapter.answer = (403, {'title': 'Forbidden'});
    await call();

    expect(raised, 0);
  });

  test('ignores the code under any other status', () async {
    adapter.answer = (409, {'code': 'legal.consent_pending'});
    await call();
    adapter.answer = (200, {'code': 'legal.consent_pending'});
    await call();

    expect(raised, 0);
  });

  test('ignores a body that is not a problem', () async {
    adapter.answer = (403, 'legal.consent_pending');
    await call();

    expect(raised, 0);
  });
}

class _Adapter implements HttpClientAdapter {
  (int, Object) answer = (200, const <String, dynamic>{});

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream, Future<void>? cancelFuture) async {
    final (status, body) = answer;
    return ResponseBody.fromString(
      body is String ? body : jsonEncode(body),
      status,
      headers: {
        Headers.contentTypeHeader: [body is String ? 'text/plain' : Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}
