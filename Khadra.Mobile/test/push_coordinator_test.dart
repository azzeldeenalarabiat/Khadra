import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/push/push_coordinator.dart';
import 'package:khadra_mobile/core/push/push_messaging.dart';

import 'support/fake_api.dart';

/// A push platform that answers from memory, so the rules are tested without Firebase.
class FakePushMessaging implements PushMessaging {
  FakePushMessaging({this.available = true, this.permitted = true, this.currentToken = 'token-1'});

  bool available;
  bool permitted;
  String? currentToken;
  int permissionRequests = 0;
  int deletions = 0;
  PushEvent? launch;
  final List<PushEvent> shown = [];
  final foregroundController = StreamController<PushEvent>.broadcast();
  final tapController = StreamController<PushEvent>.broadcast();
  final refreshController = StreamController<String>.broadcast();

  @override
  Future<bool> initialize() async => available;

  @override
  Future<bool> requestPermission() async {
    permissionRequests++;
    return permitted;
  }

  @override
  Future<String?> token() async => currentToken;

  @override
  Stream<String> get tokenRefreshes => refreshController.stream;

  @override
  Future<void> deleteToken() async {
    deletions++;
    currentToken = null;
  }

  @override
  Stream<PushEvent> get foreground => foregroundController.stream;

  @override
  Stream<PushEvent> get taps => tapController.stream;

  @override
  Future<PushEvent?> launchTap() async => launch;

  @override
  Future<void> show(PushEvent event) async => shown.add(event);
}

void main() {
  late FakeApi api;
  late FakePushMessaging messaging;
  late PushCoordinator push;
  late List<String> opened;
  late List<Map<String, String>> refreshed;

  Future<void> start({bool available = true, bool permitted = true, PushEvent? launch}) async {
    api = FakeApi();
    messaging = FakePushMessaging(available: available, permitted: permitted)..launch = launch;
    opened = [];
    refreshed = [];
    push = PushCoordinator(messaging: messaging, api: () => api, appVersion: '1.2.0+3')
      ..navigate = opened.add
      ..refresh = refreshed.add;
    await push.start();
  }

  Future<void> settle() => Future<void>.delayed(Duration.zero);

  group('where a tap leads', () {
    test('a booking kind opens the booking', () {
      expect(pushRoute({'kind': 'YourBookingApproved', 'subjectId': 'b-1'}), '/bookings/b-1');
      expect(pushRoute({'kind': 'YourPickupReminder', 'subjectId': 'b-2'}), '/bookings/b-2');
    });

    test('a dispute update opens the dispute, whose id is the subject', () {
      expect(pushRoute({'kind': 'YourDisputeUpdated', 'subjectId': 't-9'}), '/disputes/t-9');
    });

    test('a push about nothing in particular leads nowhere', () {
      expect(pushRoute({'kind': 'YourBookingApproved'}), isNull);
      expect(pushRoute({'kind': 'YourBookingApproved', 'subjectId': ''}), isNull);
    });
  });

  group('registration', () {
    test('signing in asks once, then registers this phone in the app language', () async {
      await start();

      await push.signedIn('ar');

      expect(messaging.permissionRequests, 1);
      expect(api.pushRegistrations, [('token-1', 'ar')]);
      expect(api.languagesSet, ['ar']);
    });

    test('nothing is registered while signed out, however the token rotates', () async {
      await start();

      messaging.refreshController.add('token-2');
      await settle();

      expect(api.pushRegistrations, isEmpty);
    });

    test('a rotated token is registered again while signed in', () async {
      await start();
      await push.signedIn('en');

      messaging.refreshController.add('token-2');
      await settle();

      expect(api.pushRegistrations, [('token-1', 'en'), ('token-2', 'en')]);
    });

    test('switching language re-registers, and tells the account for its emails', () async {
      await start();
      await push.signedIn('en');

      await push.languageChanged('ar');

      expect(api.pushRegistrations.last, ('token-1', 'ar'));
      expect(api.languagesSet, ['en', 'ar']);
    });

    test('declining notifications registers nothing, but the account still learns the language', () async {
      await start(permitted: false);

      await push.signedIn('ar');

      expect(api.pushRegistrations, isEmpty);
      expect(api.languagesSet, ['ar']);
    });

    test('with no Firebase project, push is simply off', () async {
      await start(available: false);

      await push.signedIn('en');
      await push.signingOut();

      expect(push.isAvailable, isFalse);
      expect(messaging.permissionRequests, 0);
      expect(api.pushRegistrations, isEmpty);
      expect(api.pushRemovals, 0);
    });

    test('a failed registration never surfaces as an error', () async {
      await start();
      api.pushFailure = const ApiFailure(kind: ApiFailureKind.offline);

      await push.signedIn('en');

      expect(api.pushRegistrations, isEmpty);
    });
  });

  test('a sign-in restored before push has started still registers once it has', () async {
    api = FakeApi();
    messaging = FakePushMessaging();
    push = PushCoordinator(messaging: messaging, api: () => api, appVersion: '1.2.0+3');

    // The cold-start race: the session resolves from disk first.
    final signingIn = push.signedIn('ar');
    await settle();
    expect(api.pushRegistrations, isEmpty);

    await push.start();
    await signingIn;

    expect(api.pushRegistrations, [('token-1', 'ar')]);
  });

  group('signing out', () {
    test('removes the registration from the server and forgets the token on the phone', () async {
      await start();
      await push.signedIn('en');

      await push.signingOut();

      expect(api.pushRemovals, 1);
      expect(messaging.deletions, 1);
    });

    test('a token that rotates after sign-out is not registered to anybody', () async {
      await start();
      await push.signedIn('en');
      await push.signingOut();

      messaging.refreshController.add('token-3');
      await settle();

      expect(api.pushRegistrations, [('token-1', 'en')]);
    });

    test('a session that ended elsewhere stops re-registration too', () async {
      await start();
      await push.signedIn('en');
      push.sessionEnded();

      messaging.refreshController.add('token-4');
      await settle();

      expect(api.pushRegistrations, [('token-1', 'en')]);
    });
  });

  group('arriving and tapping', () {
    test('a push in front is shown and refreshes its booking', () async {
      await start();
      final event = PushEvent(title: 'Booking confirmed', body: 'KH-1', data: const {'kind': 'YourBookingConfirmed', 'subjectId': 'b-1'});

      messaging.foregroundController.add(event);
      await settle();

      expect(messaging.shown, [event]);
      expect(refreshed.single['subjectId'], 'b-1');
      expect(opened, isEmpty);
    });

    test('a tap opens what it is about', () async {
      await start();

      messaging.tapController.add(const PushEvent(data: {'kind': 'YourDisputeUpdated', 'subjectId': 't-1'}));
      await settle();

      expect(opened, ['/disputes/t-1']);
    });

    test('a tap that launched the app from closed is followed once it starts', () async {
      await start(launch: const PushEvent(data: {'kind': 'YourReturnReminder', 'subjectId': 'b-7'}));

      expect(opened, ['/bookings/b-7']);
    });
  });
}
