import 'dart:async';

import 'package:dio/dio.dart';

import '../session/session_store.dart';

/// Attaches the bearer token, and refreshes it exactly once when several requests
/// discover at the same moment that it has expired.
///
/// **Single flight is the requirement, not an optimisation.** The API's refresh
/// tokens rotate: presenting one consumes it and returns its replacement, and
/// presenting a consumed one again is REPLAY — outside a 60-second grace, the
/// server revokes the whole family and the person is signed out of every device.
/// Six screens each refreshing on their own 401 is six presentations of the same
/// token, and the sixth is well past the grace.
///
/// The rules this follows, and why each one is here:
///
/// - **Read the token at send time**, never from a closure. A token captured a
///   minute ago and sent after a rotation is a replay the user did not commit.
/// - **Refresh proactively** when the access token is nearly expired, so the 401
///   path is the exception. The reactive path stays anyway, because device clocks
///   lie and a proactive check trusts one.
/// - **Retry the original request once.** A second 401 means the security stamp
///   moved — a password change, a suspension, a deletion — and no amount of
///   refreshing will help.
/// - **A 401 from the refresh endpoint is a verdict.** Presenting the token again
///   is exactly what burns the family, so it ends the session and never retries.
/// - **A timeout is not a verdict.** Only a definite answer signs anybody out; a
///   network failure leaves the token alone and surfaces as "offline".
class AuthInterceptor extends QueuedInterceptor {
  AuthInterceptor({
    required SessionStore store,
    required Future<bool> Function() refresh,
    required Future<void> Function() onSessionEnded,
    // ignore_for_file: prefer_initializing_formals
    // The fields are private and the parameters are not; `this._store` would put an
    // underscore in the public constructor signature of the class every request on
    // this app passes through.
  })  : _store = store,
        _refresh = refresh,
        _onSessionEnded = onSessionEnded;

  final SessionStore _store;

  /// Performs one rotation. True if a fresh access token is now installed.
  final Future<bool> Function() _refresh;

  /// Called when the session is definitively over, so the app can show the door.
  final Future<void> Function() _onSessionEnded;

  Completer<bool>? _inFlight;

  /// Marks a request that must never carry a bearer token or trigger a refresh.
  ///
  /// The refresh call itself is the obvious one: refreshing inside a refresh is an
  /// infinite regress. Sign-in and registration are here because a 401 from them
  /// means "wrong password", and treating that as an expired session would clear a
  /// perfectly good stored token belonging to whoever was already signed in.
  static const anonymousExtra = 'khadra.anonymous';

  static Options anonymous([Options? options]) {
    final base = options ?? Options();
    return base.copyWith(extra: {...?base.extra, anonymousExtra: true});
  }

  static bool _isAnonymous(RequestOptions options) =>
      options.extra[anonymousExtra] == true;

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    if (_isAnonymous(options)) {
      handler.next(options);
      return;
    }

    // Nothing stored at all: let it go out unauthenticated. Plenty of the API is
    // anonymous, and an endpoint that does need a token will say 401, which is a
    // better answer than the app inventing one.
    final hasRefresh = await _store.readRefreshToken() != null;

    if (hasRefresh && _store.accessTokenIsStale) {
      await _refreshOnce();
    }

    final token = _store.accessToken;
    if (token != null) {
      options.headers['Authorization'] = 'Bearer $token';
    }

    handler.next(options);
  }

  @override
  // `err` is what the base class calls it and says nothing. This method is long
  // enough that the reader needs the name to mean something.
  Future<void> onError(
    // ignore: avoid_renaming_method_parameters
    DioException error,
    ErrorInterceptorHandler handler,
  ) async {
    final request = error.requestOptions;
    final isUnauthorized = error.response?.statusCode == 401;
    final alreadyRetried = request.extra['khadra.retried'] == true;

    if (!isUnauthorized || _isAnonymous(request) || alreadyRetried) {
      handler.next(error);
      return;
    }

    if (await _store.readRefreshToken() == null) {
      await _onSessionEnded();
      handler.next(error);
      return;
    }

    final refreshed = await _refreshOnce();
    if (!refreshed) {
      handler.next(error);
      return;
    }

    try {
      final retry = await _retry(request);
      handler.resolve(retry);
    } on DioException catch (retryError) {
      // A second 401 is the security stamp having moved under us. Nothing a token
      // can fix.
      if (retryError.response?.statusCode == 401) {
        await _onSessionEnded();
      }
      handler.next(retryError);
    }
  }

  /// One rotation at a time. Everyone who arrives while it is running waits for
  /// the same answer rather than starting a second one.
  Future<bool> _refreshOnce() {
    final existing = _inFlight;
    if (existing != null) return existing.future;

    final completer = Completer<bool>();
    _inFlight = completer;

    unawaited(() async {
      bool result;
      try {
        result = await _refresh();
      } on Object {
        // The refresh function owns the decision about ending the session; an
        // exception escaping it means the attempt failed, not that the session is
        // over. Reporting false lets the original request fail on its own terms.
        result = false;
      } finally {
        _inFlight = null;
      }
      completer.complete(result);
    }());

    return completer.future;
  }

  Future<Response<dynamic>> _retry(RequestOptions request) {
    final token = _store.accessToken;
    return Dio(BaseOptions(
      baseUrl: request.baseUrl,
      connectTimeout: request.connectTimeout,
      receiveTimeout: request.receiveTimeout,
      sendTimeout: request.sendTimeout,
    )).fetch<dynamic>(
      request.copyWith(
        headers: {
          ...request.headers,
          if (token != null) 'Authorization': 'Bearer $token',
        },
        extra: {...request.extra, 'khadra.retried': true},
      ),
    );
  }
}
