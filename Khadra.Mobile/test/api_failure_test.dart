import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/api/api_failure_messages.dart';

/// Reading the platform's ProblemDetails.
///
/// The distinction that matters most here is [ApiFailure.isTransport]: it decides
/// whether a session ends. Only a definite answer from the server may sign
/// somebody out, and a timeout must not.
void main() {
  DioException responseWith(int status, Object? body) => DioException(
        requestOptions: RequestOptions(path: '/api/v1/bookings'),
        type: DioExceptionType.badResponse,
        response: Response<dynamic>(
          requestOptions: RequestOptions(path: '/api/v1/bookings'),
          statusCode: status,
          data: body,
        ),
      );

  test('reads the code, title and traceId out of ProblemDetails', () {
    final failure = ApiFailure.from(responseWith(409, {
      'title': 'That car is no longer free for those dates.',
      'code': 'booking.vehicle_unavailable',
      'traceId': '00-abc-123',
    }));

    expect(failure.kind, ApiFailureKind.conflict);
    expect(failure.hasCode('booking.vehicle_unavailable'), isTrue);
    expect(failure.traceId, '00-abc-123');
    expect(failure.isTransport, isFalse);
  });

  test('reads FluentValidation field errors', () {
    final failure = ApiFailure.from(responseWith(400, {
      'code': 'validation.failed',
      'errors': {
        'Phone': ['Enter a Jordanian mobile number.'],
      },
    }));

    expect(failure.kind, ApiFailureKind.validation);
    expect(failure.fieldMessage('phone'), 'Enter a Jordanian mobile number.');
    expect(failure.fieldMessage('email'), isNull);
  });

  test('a 401 challenge with no body is still an unauthorized verdict', () {
    // The API answers a failed security-stamp check with a bodiless challenge;
    // only a domain-level forbid carries ProblemDetails.
    final failure = ApiFailure.from(responseWith(401, null));

    expect(failure.kind, ApiFailureKind.unauthorized);
    expect(failure.isUnauthorized, isTrue);
    expect(failure.code, isNull);
  });

  group('transport failures are never verdicts', () {
    test('a timeout', () {
      final failure = ApiFailure.from(DioException(
        requestOptions: RequestOptions(path: '/'),
        type: DioExceptionType.receiveTimeout,
      ));

      expect(failure.kind, ApiFailureKind.timeout);
      expect(failure.isTransport, isTrue);
      expect(failure.isUnauthorized, isFalse);
    });

    test('no connection at all', () {
      final failure = ApiFailure.from(DioException(
        requestOptions: RequestOptions(path: '/'),
        type: DioExceptionType.connectionError,
      ));

      expect(failure.kind, ApiFailureKind.offline);
      expect(failure.isTransport, isTrue);
    });

    test('a 500 from the server', () {
      expect(ApiFailure.from(responseWith(503, null)).isTransport, isTrue);
    });
  });

  test('rate limiting is its own thing, and does not end a session', () {
    final failure = ApiFailure.from(responseWith(429, null));

    expect(failure.kind, ApiFailureKind.rateLimited);
    expect(failure.isUnauthorized, isFalse);
  });
}
