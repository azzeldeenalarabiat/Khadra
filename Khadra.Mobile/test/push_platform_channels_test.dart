import 'dart:async';

import 'package:firebase_core_platform_interface/test.dart';
import 'package:flutter/services.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/push/push_trace.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/features/documents/documents_screen.dart';
import 'package:khadra_mobile/main.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// Push taps through the REAL app, end to end, against what Android actually sends.
///
/// W4-9 on Staging: a rejected-document push reached a physical phone and tapping it did
/// nothing, while the tests of the same path passed — they handed `PushCoordinator` a fake
/// platform. These run `KhadraApp` from `main.dart` (its cold-start restore and its push
/// wiring), the real `FirebasePushMessaging` and the real router, and script only the
/// native end of the two plugin channels, with the maps the Android plugins put on them:
/// `FlutterFirebaseMessagingUtils.remoteMessageToMap` for FCM, and flutter_local_notifications'
/// `didReceiveNotificationResponse` for a push shown in front.
class _Api extends FakeApi {
  /// How long the cold-start token rotation takes: the network, on a phone.
  Duration refreshTakes = Duration.zero;

  /// Runs inside `markNotificationRead`: what AuthInterceptor does with a stale token.
  Future<void> Function()? duringMarkRead;

  @override
  Future<AuthTokens> refresh(String refreshToken) async {
    await Future<void>.delayed(refreshTakes);
    return FakeApi.fakeTokens();
  }

  @override
  Future<void> markNotificationRead(String notificationId) async {
    await duringMarkRead?.call();
    await super.markNotificationRead(notificationId);
  }
}

const _fcm = MethodChannel('plugins.flutter.io/firebase_messaging');
const _local = MethodChannel('dexterous.com/flutter/local_notifications');
const _codec = StandardMethodCodec();
const _reason = 'The photo is too blurred to read.';

/// The server's message (FcmPushSender.Envelope, with DeliverNotificationsHandler's data)
/// as FlutterFire's Android side hands it to Dart: a `notification` block AND a `data`
/// block, and in the data only the notification's id and its kind.
Map<String, Object?> _rejectedDocumentMessage() => {
      'senderId': '1234567890',
      'category': null,
      'collapseKey': 'com.khadra.khadra_mobile.staging',
      'contentAvailable': false,
      'data': <String, Object?>{'notificationId': '6f1c2d3e-0000-4000-8000-000000000007', 'kind': 'YourDocumentRejected'},
      'from': '1234567890',
      'messageId': '0:1759850000000000%abcdef',
      'messageType': null,
      'mutableContent': false,
      'notification': <String, Object?>{
        'title': 'A document needs a new upload',
        'body': 'Khadra could not accept one of your documents. Open your documents to see which and why, and upload a new one.',
        'android': <String, Object?>{'channelId': 'booking_updates', 'tag': '6f1c2d3e-0000-4000-8000-000000000007', 'sound': 'default'},
      },
      'sentTime': 1759850000000,
      'threadId': null,
      'ttl': 2419200,
    };

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  // The real Firebase.initializeApp(), answered by Firebase's own test host.
  setupFirebaseCoreMocks();
  // What the engine does on an Android phone at start-up for this plugin's Dart half.
  AndroidFlutterLocalNotificationsPlugin.registerWith();
  setUpAll(tz_data.initializeTimeZones);

  Map<String, Object?>? initialMessage;
  var initialMessageTakes = Duration.zero;
  final shown = <Map<Object?, Object?>>[];

  setUp(() {
    initialMessage = null;
    initialMessageTakes = Duration.zero;
    shown.clear();
    PushTrace.enabled = true;
    final messenger = TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
      ..setMockMethodCallHandler(_fcm, (call) async {
        switch (call.method) {
          case 'Messaging#getInitialMessage':
            await Future<void>.delayed(initialMessageTakes);
            return initialMessage;
          case 'Messaging#requestPermission':
            return {'authorizationStatus': 1, 'alert': 1, 'announcement': 0, 'badge': 1, 'carPlay': 0, 'lockScreen': 1,
              'notificationCenter': 1, 'showPreviews': 1, 'timeSensitive': 0, 'criticalAlert': 0, 'sound': 1,
              'providesAppNotificationSettings': 0};
          case 'Messaging#getToken':
            return {'token': 'fcm-token'};
        }
        return null;
      })
      ..setMockMethodCallHandler(_local, (call) async {
        switch (call.method) {
          case 'initialize':
            return true;
          case 'show':
            shown.add(call.arguments as Map<Object?, Object?>);
            return null;
          case 'getNotificationAppLaunchDetails':
            return {'notificationLaunchedApp': false};
        }
        return null;
      });
    addTearDown(() {
      messenger
        ..setMockMethodCallHandler(_fcm, null)
        ..setMockMethodCallHandler(_local, null);
      PushTrace.enabled = false;
    });
  });

  Future<void> fromAndroid(MethodChannel channel, String method, Object? arguments) async {
    final answered = Completer<void>();
    await TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.handlePlatformMessage(
        channel.name, _codec.encodeMethodCall(MethodCall(method, arguments)), (_) => answered.complete());
    await answered.future;
  }

  /// The app as `main()` starts it, on a phone with a stored session.
  Future<_Api> launch(WidgetTester tester, {Duration refreshTakes = Duration.zero}) async {
    tester.view.physicalSize = const Size(1000, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    SharedPreferences.setMockInitialValues({'khadra.entry_chosen': true});
    await PushTrace.clear();
    final api = _Api()
      ..refreshTakes = refreshTakes
      ..documents = CustomerDocuments(
        documents: [
          CustomerDocument(documentId: 'd-1', type: 'DrivingLicenceFront', status: 'Rejected', contentType: 'image/jpeg',
              sizeBytes: 1000, uploadedAt: DateTime.utc(2026, 10, 6), reviewNote: _reason),
        ],
        isComplete: false,
        missing: const ['DrivingLicenceFront'],
      );
    await tester.pumpWidget(ProviderScope(
      overrides: [
        sharedPreferencesProvider.overrideWithValue(await SharedPreferences.getInstance()),
        installedAppVersionProvider.overrideWithValue('1.3.0+5'),
        apiProvider.overrideWithValue(api),
        sessionStoreProvider.overrideWithValue(FakeSessionStore(refreshToken: 'stored-refresh-token')),
      ],
      child: const KhadraApp(),
    ));
    for (var i = 0; i < 20; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    return api;
  }

  /// Lets every queued timer run out (the trace's own "which screen is on top" check).
  Future<void> settle(WidgetTester tester) async {
    await tester.pumpAndSettle();
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();
  }

  group('a rejected-document push, with the payload Android delivers', () {
    testWidgets('closed: the launching tap (getInitialMessage) opens My Documents', (tester) async {
      initialMessage = _rejectedDocumentMessage();
      await launch(tester);
      await settle(tester);

      expect(find.byType(DocumentsScreen), findsOneWidget);
      expect(find.text(_reason), findsOneWidget);
    });

    for (final (restore, launchTap) in [(800, 0), (0, 300), (300, 300), (300, 280), (300, 320)]) {
      testWidgets('closed, the session answering in ${restore}ms and the launch tap in ${launchTap}ms: still opens',
          (tester) async {
        initialMessage = _rejectedDocumentMessage();
        initialMessageTakes = Duration(milliseconds: launchTap);
        await launch(tester, refreshTakes: Duration(milliseconds: restore));
        await settle(tester);

        expect(find.byType(DocumentsScreen), findsOneWidget);
      });
    }

    testWidgets('in the background: onMessageOpenedApp opens My Documents', (tester) async {
      await launch(tester);
      await settle(tester);

      await fromAndroid(_fcm, 'Messaging#onMessageOpenedApp', _rejectedDocumentMessage());
      await settle(tester);

      expect(find.byType(DocumentsScreen), findsOneWidget);
    });

    testWidgets('in the background, with the access token rotating as the app resumes', (tester) async {
      await launch(tester);
      await settle(tester);
      final container = ProviderScope.containerOf(tester.element(find.byType(KhadraApp)));

      unawaited(Future<void>.delayed(const Duration(milliseconds: 50),
          () => container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens())));
      await fromAndroid(_fcm, 'Messaging#onMessageOpenedApp', _rejectedDocumentMessage());
      await settle(tester);

      expect(find.byType(DocumentsScreen), findsOneWidget);
    });

    testWidgets('in front: shown on arrival, and its tap opens My Documents', (tester) async {
      await launch(tester);
      await settle(tester);

      await fromAndroid(_fcm, 'Messaging#onMessage', _rejectedDocumentMessage());
      await settle(tester);
      expect(shown, hasLength(1));
      expect(find.byType(DocumentsScreen), findsNothing, reason: 'Arriving is not a tap.');

      await fromAndroid(_local, 'didReceiveNotificationResponse', {
        'notificationId': shown.single['id'],
        'actionId': null,
        'input': null,
        'payload': shown.single['payload'],
        'notificationResponseType': 0,
      });
      await settle(tester);

      expect(find.byType(DocumentsScreen), findsOneWidget);
    });
  });

  group('its row in Alerts', () {
    Future<_Api> onAlerts(WidgetTester tester) async {
      final api = await launch(tester);
      api.notificationFeed = NotificationFeed(items: [
        NotificationItem(notificationId: 'n-7', kind: 'YourDocumentRejected', subjectId: null, subjectReference: null,
            actorName: 'Khadra', occurredAt: DateTime.now().toUtc(), readAt: null),
      ], page: 1, pageSize: 25, totalCount: 1, unreadCount: 1);
      ProviderScope.containerOf(tester.element(find.byType(KhadraApp))).read(routerProvider).go(Routes.notifications);
      await settle(tester);
      return api;
    }

    // Reproduced from the device: the tap marked the row read and opened nothing.
    testWidgets('opens My Documents even when marking it read rotates the access token', (tester) async {
      final api = await onAlerts(tester);
      final container = ProviderScope.containerOf(tester.element(find.byType(KhadraApp)));
      api.duringMarkRead = () async {
        await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
        // The request itself, over a real network: frames are drawn while it is in flight.
        await Future<void>.delayed(const Duration(milliseconds: 300));
      };

      await tester.tap(find.text('Khadra could not accept one of your documents — upload a new one'));
      await settle(tester);

      expect(find.byType(DocumentsScreen), findsOneWidget);
      expect(api.markedRead, ['n-7']);
      // The device diagnostics for this tap: the handler fired, the kind, the route, the push, the screen.
      final trace = PushTrace.lines.join('\n');
      expect(trace, contains('alerts-tap keys=[kind] kind=YourDocumentRejected subject=no route=/profile/documents'));
      expect(trace, contains('alerts-navigate push /profile/documents'));
      expect(trace, contains('alerts-screen top=/profile/documents'));
    });

    testWidgets('opens My Documents however slow the read is', (tester) async {
      final api = await onAlerts(tester);
      api.duringMarkRead = () => Future<void>.delayed(const Duration(seconds: 3));

      await tester.tap(find.text('Khadra could not accept one of your documents — upload a new one'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));

      expect(find.byType(DocumentsScreen), findsOneWidget, reason: 'Opened before the read was recorded.');
      expect(api.markedRead, isEmpty, reason: 'The read is still in flight.');
      await settle(tester);
      await tester.pump(const Duration(seconds: 3));
    });
  });

  group('the temporary Staging trace (PushTrace)', () {
    testWidgets('says where a tap went, step by step, and never what was rejected or why', (tester) async {
      await launch(tester);
      await settle(tester);
      await fromAndroid(_fcm, 'Messaging#onMessageOpenedApp', _rejectedDocumentMessage());
      await settle(tester);

      final trace = PushTrace.lines.join('\n');
      expect(trace, contains('start available=true'));
      expect(trace, contains('tap-system keys=[kind,notificationId] kind=YourDocumentRejected subject=no notification=true'));
      expect(trace, contains('open keys=[kind,notificationId] kind=YourDocumentRejected subject=no route=/profile/documents navigate=true'));
      expect(trace, contains('navigate push /profile/documents'));
      expect(trace, contains('screen top=/profile/documents'));
      for (final secret in [_reason, 'DrivingLicenceFront', '6f1c2d3e', 'fcm-token', 'access-token', 'refresh-token']) {
        expect(trace, isNot(contains(secret)), reason: 'The trace must never hold $secret.');
      }
    });

    test('keeps a route\'s shape and drops every id in it', () {
      expect(PushTrace.redact('/bookings/0b0f5a3c-1111-4000-8000-000000000001'), '/bookings/:id');
      expect(PushTrace.redact('/profile/documents'), '/profile/documents');
      expect(PushTrace.redact(null), '(none)');
    });

    test('reduces a push to its keys, its kind and whether it had a subject', () {
      expect(PushTrace.describe({'kind': 'YourBookingApproved', 'subjectId': 'b-1', 'subjectReference': 'KH-1'}),
          'keys=[kind,subjectId,subjectReference] kind=YourBookingApproved subject=yes');
      expect(PushTrace.describe({'kind': 'not a kind; DROP'}), 'keys=[kind] kind=(not a kind) subject=no');
    });
  });
}
