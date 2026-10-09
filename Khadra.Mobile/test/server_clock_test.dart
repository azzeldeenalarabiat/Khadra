import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/api/server_clock.dart';

/// The server's "now", for deciding when something the SERVER judges has become
/// possible (pre-launch item 225). A phone's clock can be minutes out either way;
/// the offset comes from the `Date` of the API's own answers.
void main() {
  group('an HTTP Date header', () {
    test('reads an IMF-fixdate', () {
      expect(ServerClock.parseHttpDate('Sat, 10 Oct 2026 07:41:09 GMT'), DateTime.utc(2026, 10, 10, 7, 41, 9));
    });

    test('reads nothing from anything else, so an unreadable header moves nothing', () {
      for (final value in [
        null,
        '',
        'Saturday, 10-Oct-26 07:41:09 GMT', // RFC 850, obsolete
        'Sat Oct 10 07:41:09 2026', // asctime
        'Sat, 10 Okt 2026 07:41:09 GMT',
        '2026-10-10T07:41:09Z',
      ]) {
        expect(ServerClock.parseHttpDate(value), isNull, reason: '$value');
      }
    });
  });

  group('the clock', () {
    final device = DateTime.utc(2026, 10, 12, 6, 50);

    test('is the device clock until the server has said anything', () {
      final clock = ServerClock(deviceNow: () => device);

      expect(clock.offset, Duration.zero);
      expect(clock.now(), device);
    });

    test('corrects a phone running slow, and one running fast', () {
      final clock = ServerClock(deviceNow: () => device);

      clock.observe(device.add(const Duration(minutes: 7)));
      expect(clock.now(), DateTime.utc(2026, 10, 12, 6, 57));

      clock.observe(device.subtract(const Duration(minutes: 4)));
      expect(clock.offset, const Duration(minutes: -4));
      expect(clock.now(), DateTime.utc(2026, 10, 12, 6, 46));
    });

    test('keeps the offset as the device clock moves on', () {
      var now = device;
      final clock = ServerClock(deviceNow: () => now)..observe(device.add(const Duration(minutes: 7)));

      now = now.add(const Duration(minutes: 3));
      expect(clock.now(), DateTime.utc(2026, 10, 12, 7, 0));
    });
  });

  group('the interceptor', () {
    const api = 'https://api.khadra.test';
    final device = DateTime.utc(2026, 10, 12, 6, 50);
    late ServerClock clock;
    late _Adapter adapter;
    late Dio dio;

    setUp(() {
      clock = ServerClock(deviceNow: () => device);
      adapter = _Adapter();
      dio = Dio(BaseOptions(baseUrl: api))
        ..httpClientAdapter = adapter
        ..interceptors.add(ServerClockInterceptor(clock, apiBaseUrl: api));
    });

    test("reads the API's own answers", () async {
      adapter.date = 'Mon, 12 Oct 2026 06:57:00 GMT';

      await dio.get<dynamic>('/api/v1/bookings/b-1');

      expect(clock.offset, const Duration(minutes: 7));
    });

    test('reads a refusal too: a 409 carries the time as much as a 200 does', () async {
      adapter
        ..date = 'Mon, 12 Oct 2026 06:45:00 GMT'
        ..status = 409;

      await expectLater(dio.post<dynamic>('/api/v1/bookings/b-1/handover-code'), throwsA(isA<DioException>()));

      expect(clock.offset, const Duration(minutes: -5));
    });

    test("never reads another host's clock", () async {
      adapter.date = 'Mon, 12 Oct 2026 09:00:00 GMT';

      await dio.put<dynamic>('https://storage.khadra.test/evidence/abc', data: 'x');

      expect(clock.offset, Duration.zero);
    });

    test('leaves the offset alone when the answer carries no readable Date', () async {
      adapter.date = 'Mon, 12 Oct 2026 06:57:00 GMT';
      await dio.get<dynamic>('/api/v1/bookings/b-1');

      adapter.date = null;
      await dio.get<dynamic>('/api/v1/bookings/b-1');
      adapter.date = 'yesterday';
      await dio.get<dynamic>('/api/v1/bookings/b-1');

      expect(clock.offset, const Duration(minutes: 7));
    });
  });
}

class _Adapter implements HttpClientAdapter {
  String? date;
  int status = 200;

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream, Future<void>? cancelFuture) async =>
      ResponseBody.fromString(
        jsonEncode(status == 200 ? <String, dynamic>{} : {'code': 'booking.pickup_too_early'}),
        status,
        headers: {
          Headers.contentTypeHeader: [Headers.jsonContentType],
          if (date != null) 'date': [date!],
        },
      );

  @override
  void close({bool force = false}) {}
}
