import 'dart:async';

import 'package:flutter/foundation.dart';

import '../../api/khadra_api.dart';
import '../api/api_failure.dart';
import 'push_messaging.dart';

/// Where tapping a push leads, from its data. Null when it leads nowhere in particular.
///
/// A dispute update opens the dispute (its subject is the TICKET); everything else about
/// a booking opens the booking. The routes are the app's own guarded routes, so a push
/// tapped while signed out goes through sign-in exactly as a typed link would.
String? pushRoute(Map<String, String> data) {
  final subject = data['subjectId'];
  if (subject == null || subject.isEmpty) return null;
  return data['kind'] == 'YourDisputeUpdated' ? '/disputes/$subject' : '/bookings/$subject';
}

/// Owns this phone's push registration for the life of the app.
///
/// The rules, each one tested:
///
/// - **Registered only while signed in**, and only once the person has allowed
///   notifications. The server ties the registration to the session, so signing in
///   again — or as somebody else — re-registers the same token to the new session.
/// - **Registered again** when the push token rotates and when the language changes, so
///   a push is always written in the language the app is showing.
/// - **Removed before sign-out**, from the server and from the device. Best effort: the
///   server also refuses to push to a session that has ended, so a failure here cannot
///   leave a signed-out phone being woken.
/// - **A push that arrives in front** is shown (Android does not show those itself) and
///   the screens that might be showing its booking are refreshed.
/// - **A tap** opens the booking or dispute it is about.
class PushCoordinator {
  PushCoordinator({
    required PushMessaging messaging,
    required KhadraApi Function() api,
    required String? appVersion,
  })  // Private fields, public parameters: the same choice SessionController makes.
        // ignore: prefer_initializing_formals
        : _messaging = messaging,
        // ignore: prefer_initializing_formals
        _api = api,
        // ignore: prefer_initializing_formals
        _appVersion = appVersion;

  final PushMessaging _messaging;
  final KhadraApi Function() _api;
  final String? _appVersion;

  bool _available = false;
  bool _signedIn = false;
  String _language = 'en';
  String? _registeredToken;
  final List<StreamSubscription<Object?>> _subscriptions = [];

  /// Opens a route. Set by the app once its router exists.
  void Function(String location)? navigate;

  /// Refreshes whatever might be showing the booking a push is about.
  void Function(Map<String, String> data)? refresh;

  bool get isAvailable => _available;

  /// Starts listening. Safe to call when Firebase is not configured: push is then off.
  Future<void> start() async {
    _available = await _messaging.initialize();
    if (!_available) return;

    _subscriptions
      ..add(_messaging.foreground.listen((event) {
        unawaited(_messaging.show(event));
        refresh?.call(event.data);
      }))
      ..add(_messaging.taps.listen(_open))
      ..add(_messaging.tokenRefreshes.listen((token) {
        if (_signedIn) unawaited(_register(token));
      }));

    final launch = await _messaging.launchTap();
    if (launch != null) _open(launch);
  }

  /// The session is signed in: ask for permission once, and register this phone.
  Future<void> signedIn(String language) async {
    _signedIn = true;
    _language = language;
    // The account learns the language even when push is off: reminder emails read it.
    await _quietly(() => _api().setLanguage(language));
    if (!_available) return;
    if (!await _messaging.requestPermission()) return;
    final token = await _messaging.token();
    if (token != null) await _register(token);
  }

  /// The app's language changed: pushes follow it, and so do emails.
  Future<void> languageChanged(String language) async {
    if (language == _language) return;
    _language = language;
    if (!_signedIn) return;
    await _quietly(() => _api().setLanguage(language));
    final token = _registeredToken;
    if (token != null) await _register(token);
  }

  /// About to sign out: stop pushes to this phone for this account.
  Future<void> signingOut() async {
    _signedIn = false;
    if (!_available) return;
    await _quietly(() => _api().removePushDevice());
    _registeredToken = null;
    try {
      await _messaging.deleteToken();
    } on Object catch (error) {
      debugPrint('Could not delete the push token: $error');
    }
  }

  /// The session ended without a sign-out (expired, revoked elsewhere): nothing to tell the
  /// server, which already refuses to push to a dead session.
  void sessionEnded() => _signedIn = false;

  Future<void> _register(String token) async {
    final ok = await _quietly(() => _api().registerPushDevice(
          token: token,
          platform: 'Android',
          language: _language,
          appVersion: _appVersion,
        ));
    if (ok) _registeredToken = token;
  }

  void _open(PushEvent event) {
    final route = pushRoute(event.data);
    if (route != null) navigate?.call(route);
  }

  /// A registration that fails is retried at the next sign-in, token refresh or language
  /// change. It must never surface as an error to someone who did not ask for anything.
  static Future<bool> _quietly(Future<void> Function() call) async {
    try {
      await call();
      return true;
    } on ApiFailure catch (failure) {
      debugPrint('Push registration call failed: ${failure.code}');
      return false;
    }
  }

  void dispose() {
    for (final subscription in _subscriptions) {
      unawaited(subscription.cancel());
    }
  }
}
