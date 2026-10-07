import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/live/live_refresh.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/push/push_coordinator.dart';
import 'package:khadra_mobile/core/push/push_messaging.dart';
import 'package:khadra_mobile/core/push/push_actions.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/core/uploads/document_picker.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/features/auth/sign_in_screen.dart';
import 'package:khadra_mobile/features/documents/document_providers.dart';
import 'package:khadra_mobile/features/documents/documents_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';
import 'support/fake_push_messaging.dart';

/// A document Khadra could not accept (Wave 4, W4-9), from the notice to the replacement.
///
/// Found on Staging: the customer received the notice and its email, and tapping it did
/// nothing. The notice carries no subject — deliberately, so that installed builds never
/// open `/bookings/null` — and both ways of tapping it chose their destination from the
/// subject alone. These walk the real router and the real screens, in both languages,
/// from every state a tap can arrive in.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  const englishReason = 'The photo is too blurred to read.';
  const arabicReason = 'الصورة غير واضحة، يرجى رفع صورة أوضح.';

  /// Exactly what the server sends with the push: no subject, no reference, no reason and
  /// no document type (DeliverNotificationsHandler).
  const notice = {'notificationId': 'n-7', 'kind': 'YourDocumentRejected'};

  CustomerDocument document(String type, String status, {String? reviewNote}) => CustomerDocument(
        documentId: 'doc-$type',
        type: type,
        status: status,
        contentType: 'image/jpeg',
        sizeBytes: 182000,
        uploadedAt: DateTime.utc(2026, 10, 6, 9),
        reviewNote: reviewNote,
      );

  /// What `/customers/me/documents` answers after the rejection: the front of the licence
  /// refused, and therefore counted as not filed.
  CustomerDocuments rejected({String reason = englishReason}) => CustomerDocuments(
        documents: [
          document(DocumentTypes.drivingLicenceFront, 'Rejected', reviewNote: reason),
          document(DocumentTypes.drivingLicenceBack, 'PendingReview'),
          document(DocumentTypes.nationalId, 'PendingReview'),
        ],
        isComplete: false,
        missing: const [DocumentTypes.drivingLicenceFront],
      );

  CustomerDocuments complete() => CustomerDocuments(
        documents: [
          document(DocumentTypes.drivingLicenceFront, 'PendingReview'),
          document(DocumentTypes.drivingLicenceBack, 'PendingReview'),
          document(DocumentTypes.nationalId, 'PendingReview'),
        ],
        isComplete: true,
        missing: const [],
      );

  NotificationFeed feedWithTheNotice() => NotificationFeed(
        items: [
          NotificationItem(
            notificationId: 'n-7',
            kind: 'YourDocumentRejected',
            subjectId: null,
            subjectReference: null,
            actorName: 'Khadra',
            occurredAt: DateTime.now().toUtc().subtract(const Duration(minutes: 5)),
            readAt: null,
          ),
        ],
        page: 1,
        pageSize: 25,
        totalCount: 1,
        unreadCount: 1,
      );

  /// The app as `main()` builds it, with the customer signed in unless told otherwise.
  /// [resolve] false leaves the session unknown, as it is in the first moments of a cold
  /// start, before the stored session has been restored.
  Future<(GoRouter, ProviderContainer, FakeApi)> pumpApp(
    WidgetTester tester, {
    required String locale,
    FakeApi? api,
    bool signedIn = true,
    bool resolve = true,
    List<Override> overrides = const [],
  }) async {
    tester.view.physicalSize = const Size(1000, 2600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final fake = api ?? FakeApi();
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(fake),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(null),
      ...overrides,
    ]);
    addTearDown(container.dispose);

    if (resolve) {
      if (signedIn) {
        await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
      } else {
        await container.read(sessionProvider.notifier).restore();
      }
    }

    final router = container.read(routerProvider);
    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(
          locale: Locale(locale),
          routerConfig: router,
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
        ),
      ),
    );
    if (resolve) {
      await tester.pumpAndSettle();
    } else {
      // The splash spins until the session answers, so it never settles.
      await tester.pump(const Duration(milliseconds: 100));
    }
    return (router, container, fake);
  }

  /// Push, wired exactly as `main()` wires it: a tap opens its route through the router's
  /// guards, and a push in front refreshes what it might be showing.
  Future<FakePushMessaging> startPush(ProviderContainer container, FakeApi api, {PushEvent? launch}) async {
    final messaging = FakePushMessaging()..launch = launch;
    final push = PushCoordinator(messaging: messaging, api: () => api, appVersion: '1.3.0+4')
      ..navigate = (location) {
        openForPush(
          location,
          router: container.read(routerProvider),
          sessionResolved: container.read(sessionProvider).isResolved,
        );
      }
      ..refresh = (data) => refreshForPush(
            data,
            invalidate: container.invalidate,
            live: container.read(liveRefreshProvider),
          );
    addTearDown(push.dispose);
    await push.start();
    return messaging;
  }

  final words = {
    'en': (
      line: 'Khadra could not accept one of your documents — upload a new one',
      status: 'Not accepted',
      why: 'Why:',
      action: 'Upload a new one',
      incomplete: 'Upload the missing documents before you can book.',
      notice: 'The rental office checks these in person',
      stale: 'Nobody has checked these yet',
      complete: 'You have everything you need to book.',
    ),
    'ar': (
      line: 'لم تتمكن خضرا من قبول أحد مستنداتك — ارفع نسخة جديدة',
      status: 'غير مقبول',
      why: 'السبب:',
      action: 'ارفع نسخة جديدة',
      incomplete: 'ارفع المستندات الناقصة قبل أن تتمكن من الحجز.',
      notice: 'يتحقق منها مكتب التأجير شخصياً',
      stale: 'لم يدقّق أحد هذه المستندات بعد',
      complete: 'لديك كل ما تحتاجه للحجز.',
    ),
  };

  for (final locale in ['en', 'ar']) {
    final text = words[locale]!;

    group('[$locale]', () {
      testWidgets('its row in Alerts names it, and a tap opens My Documents', (tester) async {
        final api = FakeApi()
          ..notificationFeed = feedWithTheNotice()
          ..documents = rejected();
        final (router, _, _) = await pumpApp(tester, locale: locale, api: api);

        router.go(Routes.notifications);
        await tester.pumpAndSettle();
        expect(find.text(text.line), findsOneWidget);

        await tester.tap(find.text(text.line));
        await tester.pumpAndSettle();

        expect(find.byType(DocumentsScreen), findsOneWidget);
        expect(find.text(englishReason), findsOneWidget);
        expect(api.markedRead, ['n-7']);
      });

      testWidgets('My Documents shows the refusal, its reason and the way to replace it', (tester) async {
        final api = FakeApi()..documents = rejected();
        final (router, _, _) = await pumpApp(tester, locale: locale, api: api);

        router.go(Routes.documents);
        await tester.pumpAndSettle();

        expect(find.text(text.status), findsOneWidget);
        expect(find.text(text.why), findsOneWidget);
        expect(find.text(englishReason), findsOneWidget);
        expect(find.text(text.action), findsOneWidget);
        expect(find.text(text.incomplete), findsOneWidget);
        // Nothing on the screen may still say that nobody checks these.
        expect(find.text(text.notice), findsOneWidget);
        expect(find.text(text.stale), findsNothing);
      });

      testWidgets('the replacement is uploaded from there, and the refusal clears', (tester) async {
        final api = FakeApi()
          ..documents = rejected()
          ..afterUpload = (_) => complete();
        final (router, _, _) = await pumpApp(tester, locale: locale, api: api, overrides: [
          documentChooserProvider.overrideWithValue((_, _) async => DocumentChosen(PickedDocument(
                bytes: Uint8List.fromList(List.filled(2048, 7)),
                fileName: 'licence-front.jpg',
                contentType: 'image/jpeg',
              ))),
        ]);

        router.go(Routes.documents);
        await tester.pumpAndSettle();
        await tester.tap(find.text(text.action));
        await tester.pumpAndSettle();

        expect(api.uploads, [(DocumentTypes.drivingLicenceFront, 'licence-front.jpg', 'image/jpeg')]);
        expect(find.text(englishReason), findsNothing);
        expect(find.text(text.action), findsNothing);
        expect(find.text(text.complete), findsOneWidget);
      });

      testWidgets('closed: the tap that launched the app lands on My Documents once the session is back',
          (tester) async {
        final api = FakeApi()..documents = rejected();
        final (_, container, _) = await pumpApp(tester, locale: locale, api: api, resolve: false);

        // The tap arrives before the stored session has been restored.
        await startPush(container, api, launch: const PushEvent(data: notice));
        await tester.pump(const Duration(milliseconds: 100));
        expect(find.byType(DocumentsScreen), findsNothing);

        await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
        await tester.pumpAndSettle();

        expect(find.byType(DocumentsScreen), findsOneWidget);
        expect(find.text(text.status), findsOneWidget);
        expect(find.text(englishReason), findsOneWidget);
      });

      testWidgets('in the background: a tap opens My Documents', (tester) async {
        final api = FakeApi()..documents = rejected();
        final (_, container, _) = await pumpApp(tester, locale: locale, api: api);
        final messaging = await startPush(container, api);
        expect(find.byType(DocumentsScreen), findsNothing);

        messaging.tapController.add(const PushEvent(data: notice));
        await tester.pumpAndSettle();

        expect(find.byType(DocumentsScreen), findsOneWidget);
        expect(find.text(englishReason), findsOneWidget);

        // Pushed, so the back arrow returns to where the customer was.
        await tester.tap(find.byType(KhadraBack));
        await tester.pumpAndSettle();
        expect(find.byType(DocumentsScreen), findsNothing);
      });

      testWidgets('in front: My Documents shows the refusal as it arrives, and a tap opens it',
          (tester) async {
        // The customer is already looking at their documents when Khadra refuses one.
        final api = FakeApi()..documents = complete();
        final (router, container, _) = await pumpApp(tester, locale: locale, api: api);
        final messaging = await startPush(container, api);
        router.go(Routes.documents);
        await tester.pumpAndSettle();
        expect(find.text(englishReason), findsNothing);

        api.documents = rejected();
        messaging.foregroundController.add(const PushEvent(
            title: 'A document needs a new upload',
            body: 'Khadra could not accept one of your documents.',
            data: notice));
        await tester.pumpAndSettle();

        expect(messaging.shown.single.data, notice);
        expect(find.text(englishReason), findsOneWidget, reason: 'Refreshed without a pull.');

        router.go(Routes.notifications);
        await tester.pumpAndSettle();
        messaging.tapController.add(PushEvent(
            data: FirebasePushMessaging.decodePayload(
                FirebasePushMessaging.encodePayload(messaging.shown.single.data))!));
        await tester.pumpAndSettle();

        expect(find.byType(DocumentsScreen), findsOneWidget);
      });

      testWidgets('signed out: a tap asks for sign-in, and keeps My Documents as the destination',
          (tester) async {
        final api = FakeApi()..documents = rejected();
        final (_, container, _) = await pumpApp(tester, locale: locale, api: api, signedIn: false);
        final messaging = await startPush(container, api);

        messaging.tapController.add(const PushEvent(data: notice));
        await tester.pumpAndSettle();

        expect(tester.widget<SignInScreen>(find.byType(SignInScreen)).next, Routes.documents);
      });
    });
  }

  // The cold-start loss was never particular to this notice: every push leads to a guarded
  // route, so a booking tapped from a closed app was sent Home too.
  testWidgets('a booking tapped from a closed app is not lost either', (tester) async {
    final api = FakeApi();
    final (router, container, _) = await pumpApp(tester, locale: 'en', api: api, resolve: false);

    await startPush(container, api,
        launch: const PushEvent(data: {'notificationId': 'n-1', 'kind': 'YourBookingApproved', 'subjectId': 'b-1'}));
    await tester.pump(const Duration(milliseconds: 100));
    await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
    await tester.pumpAndSettle();

    expect(router.routerDelegate.currentConfiguration.uri.path, Routes.booking('b-1'));
  });

  testWidgets('a reason is laid out in the direction it was typed in, whatever the app language',
      (tester) async {
    final api = FakeApi()..documents = rejected(reason: arabicReason);
    final (router, _, _) = await pumpApp(tester, locale: 'en', api: api);
    router.go(Routes.documents);
    await tester.pumpAndSettle();

    expect(tester.widget<Text>(find.text(arabicReason)).textDirection, TextDirection.rtl);
  });

  testWidgets('an English reason stays left-to-right in the Arabic app', (tester) async {
    final api = FakeApi()..documents = rejected();
    final (router, _, _) = await pumpApp(tester, locale: 'ar', api: api);
    router.go(Routes.documents);
    await tester.pumpAndSettle();

    expect(tester.widget<Text>(find.text(englishReason)).textDirection, TextDirection.ltr);
  });
}
