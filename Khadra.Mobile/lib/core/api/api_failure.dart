import 'package:dio/dio.dart';

/// Why a request did not produce what was asked for.
///
/// Built from RFC 9457 ProblemDetails, which every failing endpoint on this API
/// returns with a stable machine `code` and a `traceId`.
///
/// **The app holds the CODE, never a resolved sentence.** The server's `title` is
/// English — the backend half of localising those is still open — so a screen that
/// showed it would put English in front of an Arabic reader. Every message the
/// customer sees is chosen from the code by `ApiFailureMessages`, and the title is
/// a last resort for a code this version of the app has never heard of.
final class ApiFailure implements Exception {
  const ApiFailure({
    required this.kind,
    this.code,
    this.title,
    this.traceId,
    this.statusCode,
    this.fieldErrors = const <String, List<String>>{},
  });

  final ApiFailureKind kind;

  /// The platform's stable code, e.g. `booking.vehicle_unavailable`.
  final String? code;

  /// The server's own English sentence. A fallback, not a message.
  final String? title;

  /// Names the request in the API's logs. Shown only when nothing else can be.
  final String? traceId;

  final int? statusCode;

  /// FluentValidation's per-field messages, keyed by field name.
  final Map<String, List<String>> fieldErrors;

  bool get isUnauthorized => kind == ApiFailureKind.unauthorized;
  bool get isNotFound => kind == ApiFailureKind.notFound;
  bool get isConflict => kind == ApiFailureKind.conflict;

  /// A network problem rather than a verdict.
  ///
  /// The distinction decides whether a session ends: only a definite answer from
  /// the server may sign somebody out. A timeout must not.
  bool get isTransport =>
      kind == ApiFailureKind.offline ||
      kind == ApiFailureKind.timeout ||
      kind == ApiFailureKind.server;

  bool hasCode(String candidate) => code == candidate;

  static ApiFailure from(Object error) {
    if (error is ApiFailure) return error;
    if (error is! DioException) {
      return const ApiFailure(kind: ApiFailureKind.unknown);
    }

    switch (error.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
        return const ApiFailure(kind: ApiFailureKind.timeout);
      case DioExceptionType.connectionError:
      case DioExceptionType.unknown:
        return const ApiFailure(kind: ApiFailureKind.offline);
      case DioExceptionType.cancel:
        return const ApiFailure(kind: ApiFailureKind.cancelled);
      case DioExceptionType.badCertificate:
        return const ApiFailure(kind: ApiFailureKind.offline);
      case DioExceptionType.badResponse:
        break;
      // Any transport case this version of Dio adds later. A response body is what
      // distinguishes a verdict from a network event, and these have none.
      default:
        return const ApiFailure(kind: ApiFailureKind.offline);
    }

    final response = error.response;
    final status = response?.statusCode ?? 0;
    final body = response?.data;

    String? code;
    String? title;
    String? traceId;
    final fields = <String, List<String>>{};

    if (body is Map) {
      code = body['code'] as String?;
      title = body['title'] as String?;
      traceId = body['traceId'] as String?;

      // Two shapes reach here. `errors` is FluentValidation's, mapped onto
      // ProblemDetails by ApiControllerBase; ASP.NET's own model-binding
      // validation produces the same key with the same shape, so one reader
      // serves both.
      final errors = body['errors'];
      if (errors is Map) {
        errors.forEach((key, value) {
          if (value is List) {
            fields['$key'] = value.map((entry) => '$entry').toList();
          } else if (value is String) {
            fields['$key'] = <String>[value];
          }
        });
      }
    }

    return ApiFailure(
      kind: _kindFor(status),
      code: code,
      title: title,
      traceId: traceId,
      statusCode: status,
      fieldErrors: fields,
    );
  }

  static ApiFailureKind _kindFor(int status) => switch (status) {
        400 || 422 => ApiFailureKind.validation,
        401 => ApiFailureKind.unauthorized,
        403 => ApiFailureKind.forbidden,
        404 => ApiFailureKind.notFound,
        409 => ApiFailureKind.conflict,
        429 => ApiFailureKind.rateLimited,
        >= 500 => ApiFailureKind.server,
        _ => ApiFailureKind.unknown,
      };

  @override
  String toString() => 'ApiFailure($kind, code: $code, status: $statusCode)';
}

enum ApiFailureKind {
  validation,
  unauthorized,
  forbidden,
  notFound,
  conflict,
  rateLimited,
  server,
  offline,
  timeout,
  cancelled,
  unknown,
}
