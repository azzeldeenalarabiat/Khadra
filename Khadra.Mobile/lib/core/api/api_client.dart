import 'package:dio/dio.dart';

import '../config/app_environment.dart';
import 'api_failure.dart';

/// The one way this app talks to the Khadra API.
///
/// Thin on purpose. It carries no business logic, because the server owns every
/// judgment on this platform — a price, a day count, a penalty, whether a car is
/// free — and a client that started deciding things would become a second source
/// of truth for figures that already have one.
///
/// Every method funnels failures through [ApiFailure], so a caller never sees a
/// `DioException` and never has to know what an HTTP status is.
class ApiClient {
  ApiClient(this._dio);

  final Dio _dio;

  Dio get raw => _dio;

  static Dio createDio() => Dio(
        BaseOptions(
          baseUrl: AppEnvironment.apiBaseUrl,
          connectTimeout: const Duration(seconds: 15),
          receiveTimeout: const Duration(seconds: 25),
          // NO sendTimeout here. It only means anything for a request with a body,
          // and on the web Dio warns about it on every GET -- which is most of
          // them. The two calls that upload set their own; see `upload`.
          headers: const {'Accept': 'application/json'},
          // Handled by ApiFailure rather than thrown as a status check, so one
          // reader turns every ProblemDetails body into a code the app can act on.
          validateStatus: (status) => status != null && status < 400,
        ),
      );

  Future<T> get<T>(
    String path, {
    Map<String, dynamic>? query,
    Options? options,
    CancelToken? cancelToken,
  }) =>
      _send(() => _dio.get<T>(
            path,
            queryParameters: _clean(query),
            options: options,
            cancelToken: cancelToken,
          ));

  Future<T> post<T>(
    String path, {
    Object? body,
    Map<String, dynamic>? query,
    Options? options,
    CancelToken? cancelToken,
  }) =>
      _send(() => _dio.post<T>(
            path,
            data: body,
            queryParameters: _clean(query),
            options: options,
            cancelToken: cancelToken,
          ));

  Future<T> put<T>(
    String path, {
    Object? body,
    Options? options,
    CancelToken? cancelToken,
  }) =>
      _send(() => _dio.put<T>(
            path,
            data: body,
            options: options,
            cancelToken: cancelToken,
          ));

  /// A PUT to an ABSOLUTE url the server minted, with the same failure mapping as
  /// everything else.
  ///
  /// Used for the one-shot evidence upload, whose address comes from
  /// `/disputes/evidence/upload-url` rather than from a route this app knows. It
  /// exists so that call cannot bypass [ApiFailure]: going through `raw` threw a
  /// `DioException` straight past every `on ApiFailure` catch in the app, and the
  /// screen sat on a spinner that never stopped.
  Future<T> putAbsolute<T>(
    String url, {
    Object? body,
    Options? options,
  }) =>
      _send(() => _dio.putUri<T>(Uri.parse(url), data: body, options: options));

  /// The options an upload needs: a generous send timeout, because a photograph
  /// over a mobile network is the one request on this app that legitimately takes
  /// a while, and the default receive timeout would cut it off.
  static Options upload({String? contentType, Map<String, dynamic>? headers}) =>
      Options(
        sendTimeout: const Duration(seconds: 120),
        receiveTimeout: const Duration(seconds: 120),
        contentType: contentType,
        headers: headers,
      );

  Future<T> _send<T>(Future<Response<T>> Function() request) async {
    try {
      final response = await request();
      return response.data as T;
    } on DioException catch (error) {
      throw ApiFailure.from(error);
    }
  }

  /// Drops nulls, so an unset filter is an absent parameter rather than the string
  /// "null" — which the API would try to parse and refuse.
  static Map<String, dynamic>? _clean(Map<String, dynamic>? query) {
    if (query == null) return null;
    final cleaned = <String, dynamic>{};
    query.forEach((key, value) {
      if (value != null) cleaned[key] = value;
    });
    return cleaned.isEmpty ? null : cleaned;
  }
}
