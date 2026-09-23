import 'dart:async';
import 'dart:convert';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';

/// A push as the app sees it: two lines to show, and the data that says where it leads.
@immutable
class PushEvent {
  const PushEvent({this.title, this.body, required this.data});

  final String? title;
  final String? body;

  /// `notificationId`, `kind`, `subjectId`, `subjectReference` — all strings, as FCM delivers them.
  final Map<String, String> data;
}

/// Everything the app asks of the push platform, and nothing else.
///
/// An interface so the rules around it — when to register, when to forget, where a tap
/// leads — are tested without Firebase, and so a checkout with no Firebase project
/// (no google-services.json) still runs: [initialize] answers false and push is simply
/// off.
abstract class PushMessaging {
  /// Starts the platform. False when it is not configured or not supported here.
  Future<bool> initialize();

  /// Asks the person for permission to show notifications (Android 13+). True when granted.
  Future<bool> requestPermission();

  Future<String?> token();

  Stream<String> get tokenRefreshes;

  /// Forgets this install's token, so nothing addressed to it is delivered again.
  Future<void> deleteToken();

  /// Pushes that arrive while the app is in front. The platform does NOT show these itself.
  Stream<PushEvent> get foreground;

  /// Taps on a notification, from any state the app was in.
  Stream<PushEvent> get taps;

  /// The tap that launched the app from closed, if one did. Asked once, at start.
  Future<PushEvent?> launchTap();

  /// Shows a push that arrived while the app was in front, on the same channel and with the
  /// same tag as the ones Android shows by itself.
  Future<void> show(PushEvent event);
}

/// The Android notification channel every Khadra push is posted on. Created with sound and
/// high importance; its id is named by the server in every message (FcmPushSender).
const pushChannelId = 'booking_updates';

/// Called by Android for a push that arrives while the app is in the background or closed.
///
/// Nothing to do: the message carries a `notification` block, so Android draws it itself
/// on the channel above, and tapping it opens the app with the data. The function has to
/// exist — and be a top-level entry point — for the plugin to wake the app at all.
@pragma('vm:entry-point')
Future<void> khadraBackgroundPush(RemoteMessage message) async {}

/// The real one: Firebase Cloud Messaging, with flutter_local_notifications for foreground pushes.
class FirebasePushMessaging implements PushMessaging {
  final _local = FlutterLocalNotificationsPlugin();
  final _taps = StreamController<PushEvent>.broadcast();
  bool _ready = false;

  @override
  Future<bool> initialize() async {
    if (kIsWeb || defaultTargetPlatform != TargetPlatform.android) return false;
    try {
      await Firebase.initializeApp();
    } on Object catch (error) {
      // No google-services.json for this flavor: a build made before Firebase was set up.
      debugPrint('Push notifications are off: Firebase is not configured ($error).');
      return false;
    }

    FirebaseMessaging.onBackgroundMessage(khadraBackgroundPush);

    await _local.initialize(
      const InitializationSettings(android: AndroidInitializationSettings('@drawable/ic_notification')),
      onDidReceiveNotificationResponse: (response) {
        final data = _decode(response.payload);
        if (data != null) _taps.add(PushEvent(data: data));
      },
    );
    // Created once and then owned by the person: they may silence it in Settings, and
    // re-creating it with other settings would not override that — which is right.
    await _local
        .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
        ?.createNotificationChannel(const AndroidNotificationChannel(
          pushChannelId,
          'Booking updates · تحديثات الحجز',
          description: 'Approvals, reminders and handovers for your bookings.',
          importance: Importance.high,
          playSound: true,
        ));

    FirebaseMessaging.onMessageOpenedApp.listen((message) => _taps.add(_event(message)));
    _ready = true;
    return true;
  }

  @override
  Future<bool> requestPermission() async {
    if (!_ready) return false;
    final settings = await FirebaseMessaging.instance.requestPermission();
    return settings.authorizationStatus == AuthorizationStatus.authorized ||
        settings.authorizationStatus == AuthorizationStatus.provisional;
  }

  @override
  Future<String?> token() async => _ready ? FirebaseMessaging.instance.getToken() : null;

  @override
  Stream<String> get tokenRefreshes =>
      _ready ? FirebaseMessaging.instance.onTokenRefresh : const Stream.empty();

  @override
  Future<void> deleteToken() async {
    if (_ready) await FirebaseMessaging.instance.deleteToken();
  }

  @override
  Stream<PushEvent> get foreground =>
      _ready ? FirebaseMessaging.onMessage.map(_event) : const Stream.empty();

  @override
  Stream<PushEvent> get taps => _taps.stream;

  @override
  Future<PushEvent?> launchTap() async {
    if (!_ready) return null;
    final message = await FirebaseMessaging.instance.getInitialMessage();
    if (message != null) return _event(message);
    final launch = await _local.getNotificationAppLaunchDetails();
    final data = launch?.didNotificationLaunchApp == true
        ? _decode(launch?.notificationResponse?.payload)
        : null;
    return data == null ? null : PushEvent(data: data);
  }

  @override
  Future<void> show(PushEvent event) async {
    if (!_ready) return;
    final tag = event.data['notificationId'];
    await _local.show(
      (tag ?? '${DateTime.now().millisecondsSinceEpoch}').hashCode,
      event.title,
      event.body,
      NotificationDetails(
        android: AndroidNotificationDetails(
          pushChannelId,
          'Booking updates · تحديثات الحجز',
          importance: Importance.high,
          priority: Priority.high,
          playSound: true,
          tag: tag,
          icon: '@drawable/ic_notification',
        ),
      ),
      payload: jsonEncode(event.data),
    );
  }

  static PushEvent _event(RemoteMessage message) => PushEvent(
        title: message.notification?.title,
        body: message.notification?.body,
        data: {for (final entry in message.data.entries) entry.key: '${entry.value}'},
      );

  static Map<String, String>? _decode(String? payload) {
    if (payload == null || payload.isEmpty) return null;
    try {
      final decoded = jsonDecode(payload);
      return decoded is Map
          ? {for (final entry in decoded.entries) '${entry.key}': '${entry.value}'}
          : null;
    } on FormatException {
      return null;
    }
  }
}
