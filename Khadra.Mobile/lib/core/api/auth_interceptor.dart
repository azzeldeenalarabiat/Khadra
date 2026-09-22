import 'dart:async';

import 'package:dio/dio.dart';

import '../session/session_store.dart';

/// Attaches the bearer token, and refreshes it exactly once when several requests
/// discover at the same moment that it has expired.
///
/// **Single flight is the requirement, not an optimisation.** The API's refresh
/// tokens rotate: presenting one consumes it and returns its replacement, and
/// presenting a consumed one again is REPLAY — outside a 60-second grace, the
/// server revokes that token's whole family. A family starts at login, so that is
/// this device's session: the customer is signed out here, mid-task, with no
/// explanation. Six screens each refreshing on their own 401 is six presentations
/// of the same token, and the sixth is well past the grace.
///
/// **This is NOT a `QueuedInterceptor`, and must not become one again.** It was,
/// and the queue deadlocked the whole app. Every request took its turn in one
/// queue; `onRequest` held that turn while it awaited a rotation; and the rotation
/// was itself a request, waiting for a turn that could not free until it finished.
/// Neither ever moved, and because the queue was shared it was not one request
/// that stalled but every request the app made from that moment on — roughly four
/// minutes after each sign-in, for as long as the app stayed open.
///
/// The queue was buying ORDERING. What this actually needs is SINGLE FLIGHT, and
/// [_refreshOnce] is what provides it: every caller that arrives while a rotation
/// is running waits on the same completer, so one token is presented once however
/// many requests noticed at once. `token_rotation_test` holds this down.
///
/// The rules this follows, and why each one is here:
///
/// - **Read the token at send time**, never from a closure. A token captured a
///   minute ago and sent after a rotation is a replay the user did not commit.
/// - **Refresh proactively** when the access token is nearly expired, so the 401
///   path is the exception. The reactive path stays anyway, because device clocks
///   lie and a proactive check trusts one.
/// - **A 401 on a token that has already moved is stale, not expired.** It is
///   retried with the current token and no rotation is spent. Without that check,
///   a handful of requests sent just before a rotation each come back 401 just
///   after it and each start another one.
/// - **Retry the original request once.** A second 401 means the security stamp
///   moved — a password change, a suspension, a deletion — and no amount of
///   refreshing will help.
/// - **A 401 from the refresh endpoint is a verdict.** Presenting the token again
///   is exactly what burns the family, so it ends the session and never retries.
/// - **A timeout is not a verdict.** Only a definite answer signs anybody out; a
///   network failure leaves the token alone and surfaces as "offline".
class AuthInterceptor extends Interceptor {
  AuthInterceptor({
    required SessionStore store,
    required Future<bool> Function() refresh,
    required Future<void> Function() onSessionEnded,
    required Future<Response<dynamic>> Function(RequestOptions) resend,
    // ignore_for_file: prefer_initializing_formals
    // The fields are private and the parameters are not; `this._store` would put an
    // underscore in the public constructor signature of the class every request on
    // this app passes through.
  })  : _store = store,
        _refresh = refresh,
        _onSessionEnded = onSessionEnded,
        _resend = resend;

  final SessionStore _store;

  /// Performs one rotation. True if a fresh access token is now installed.
  final Future<bool> Function() _refresh;

  /// Called when the session is definitively over, so the app can show the door.
  final Future<void> Function() _onSessionEnded;

  /// Sends a request again, through this same client.
  ///
  /// Injected rather than built here, for the same reason `_refresh` is: the
  /// interceptor is constructed before the `Dio` it belongs to is finished. It was
  /// once a `Dio` of its own, made to dodge the queue — and a second client silently
  /// carries none of the first's instance configuration, so the one request holding
  /// a fourteen-day credential would have been the one skipping, say, a pinned
  /// certificate. With the queue gone there is nothing left to dodge.
  final Future<Response<dynamic>> Function(RequestOptions) _resend;

  Completer<bool>? _inFlight;

  /// Marks a request that must never carry a bearer token or trigger a refresh.
  ///
  /// The refresh call itself is the obvious one, and here it is LOAD-BEARING rather
  /// than tidy: without it the rotation enters [onRequest], finds the token stale,
  /// calls [_refreshOnce], and awaits the completer it is itself about to complete.
  /// Sign-in and registration are here because a 401 from them means "wrong
  /// password", and treating that as an expired session would clear a perfectly
  /// good stored token belonging to whoever was already signed in.
  static const anonymousExtra = 'khadra.anonymous';

  /// Marks the one retry a request is allowed after a rotation.
  static const retriedExtra = 'khadra.retried';

  static Options anonymous([Options? options]) {
    final base = options ?? Options();
    return base.copyWith(extra: {...?base.extra, anonymousExtra: true});
  }

  static bool _isAnonymous(RequestOptions options) =>
      options.extra[anonymousExtra] == true;

  static bool _isRetry(RequestOptions options) =>
      options.extra[retriedExtra] == true;

  static String? _bearerOf(Map<String, dynamic> headers) =>
      headers['Authorization'] as String?;

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

    // A retry is here BECAUSE a rotation just happened, so it already has the
    // freshest token there is. Asking again would be a second rotation started
    // from inside the handling of the first.
    if (hasRefresh && !_isRetry(options) && _store.accessTokenIsStale) {
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

    if (!isUnauthorized || _isAnonymous(request) || _isRetry(request)) {
      handler.next(error);
      return;
    }

    if (await _store.readRefreshToken() == null) {
      await _onSessionEnded();
      handler.next(error);
      return;
    }

    // Did the token move while this request was in flight? Then this 401 is about
    // a token the app has already replaced, and there is nothing to rotate: send
    // it again with the current one. A handful of requests that went out just
    // before a rotation all come back just after it, and without this each would
    // spend a rotation of its own answering a question already answered.
    final current = _store.accessToken;
    final alreadyRotated =
        current != null && _bearerOf(request.headers) != 'Bearer $current';

    if (!alreadyRotated && !await _refreshOnce()) {
      handler.next(error);
      return;
    }

    try {
      final retry = await _resend(request.copyWith(
        extra: {...request.extra, retriedExtra: true},
      ));
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
}
