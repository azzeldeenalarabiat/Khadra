import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/features/bookings/booking_detail_screen.dart';
import 'package:khadra_mobile/features/bookings/booking_timeline.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// The redesigned Booking Details screen, in every state a booking can be in.
///
/// Four of those states cannot be reached on this platform at all right now — a
/// deposit cannot be paid while no payment provider is configured, so Confirmed,
/// PickedUp, Returned and Completed are unreachable end to end. They are
/// rendered here from the DTO shape the server would send, which is the only
/// honest way to check a screen for a state the running system cannot produce.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  final now = DateTime.utc(2026, 9, 20, 9);
  final en = lookupAppLocalizations(const Locale('en'));
  final ar = lookupAppLocalizations(const Locale('ar'));

  Map<String, dynamic> changeJson(String to, DateTime at,
          {String? from, String actor = 'Customer', String? reason, String? code}) =>
      {
        'fromStatus': from,
        'toStatus': to,
        'actorParty': actor,
        'reasonCode': code,
        'reason': reason,
        'occurredAt': at.toIso8601String(),
      };

  /// A booking as `/api/v1/bookings/{id}` sends one. Every figure is in the
  /// shape the server produces; nothing here is a number a screen invented.
  Booking bookingOf({
    required String status,
    required List<Map<String, dynamic>> history,
    bool isAwaitingDecision = false,
    bool isAwaitingPayment = false,
    bool depositPaid = false,
    bool canCancel = false,
    bool canBeReviewed = false,
    bool canBeDisputed = false,
    bool dealerRemoved = false,
    String? dealerCityId = 'city-amman',
    Map<String, dynamic>? penalty,
  }) =>
      Booking.fromJson({
        'bookingId': 'b-1',
        'reference': 'KH-24-0007',
        'status': status,
        'isTerminal': const {'Rejected', 'Cancelled', 'NoShow', 'Expired', 'Completed'}
            .contains(status),
        'dealerId': 'd-1',
        'vehicleId': 'v-1',
        'periodStart': now.add(const Duration(days: 3)).toIso8601String(),
        'periodEnd': now.add(const Duration(days: 6)).toIso8601String(),
        'pickupMethod': 'SelfPickup',
        'pricing': {
          'dailyRate': {'amount': 55, 'currency': 'JOD'},
          'pickupDate': '2026-09-23',
          'returnDate': '2026-09-26',
          'days': 3,
          'rentalTotal': {'amount': 165, 'currency': 'JOD'},
          'deliveryFee': {'amount': 0, 'currency': 'JOD'},
          'totalPrice': {'amount': 165, 'currency': 'JOD'},
          'depositPercent': 20,
          'depositAmount': {'amount': 33, 'currency': 'JOD'},
          'balanceDue': {'amount': 132, 'currency': 'JOD'},
          'securityDeposit': {'amount': 150, 'currency': 'JOD'},
          'mileageUnlimited': true,
          'fuelPolicy': 'SameToSame',
        },
        'terms': const <String, dynamic>{},
        'penalty': penalty,
        'createdAt': now.toIso8601String(),
        'decisionDeadline': now.add(const Duration(hours: 4)).toIso8601String(),
        'paymentDeadline': now.add(const Duration(hours: 2)).toIso8601String(),
        'depositPaid': depositPaid,
        'canBeDisputed': canBeDisputed,
        'isAwaitingDecision': isAwaitingDecision,
        'isAwaitingPayment': isAwaitingPayment,
        'cancellation': {'canCancel': canCancel},
        'canReportNonDelivery': false,
        'nonDeliveryReportableFrom': now.toIso8601String(),
        'canBeReviewed': canBeReviewed,
        'vehicle': {
          'vehicleId': 'v-1',
          'make': 'Kia',
          'model': 'Sportage',
          'year': 2024,
          'color': 'White',
          'plateNumber': '12-34567',
        },
        'dealerName': dealerRemoved ? 'Dealer no longer on the platform' : 'Petra Rentals',
        'dealerRemoved': dealerRemoved,
        'dealerCityId': dealerCityId,
        'handovers': const <dynamic>[],
        'history': history,
      });

  Future<void> pump(WidgetTester tester, Booking booking,
      {Locale locale = const Locale('en')}) async {
    tester.view.physicalSize = const Size(412, 915);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final api = FakeApi()..bookingById = booking;
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(null),
      isArabicProvider.overrideWithValue(locale.languageCode == 'ar'),
    ]);
    addTearDown(container.dispose);

    await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
    await container.read(appConfigProvider.future);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp(
          locale: locale,
          theme: KhadraTheme.light(),
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          home: const BookingDetailScreen(bookingId: 'b-1'),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  /// The history a booking has when it reached [status] the ordinary way.
  List<Map<String, dynamic>> pathTo(String status) {
    const path = ['Requested', 'Approved', 'Confirmed', 'PickedUp', 'Returned', 'Completed'];
    final stop = path.indexOf(status);
    return [
      for (var i = 0; i <= stop; i++)
        changeJson(path[i], now.add(Duration(hours: i)),
            from: i == 0 ? null : path[i - 1]),
    ];
  }

  /// A widget test that ends by tearing the screen down.
  ///
  /// Two things outlive the test body otherwise, and a live timer at the end of
  /// a widget test is an assertion failure. The screen's own 30-second repaint
  /// timer for the deadline countdown — production code, and correct — which
  /// `dispose` cancels once the tree is gone; and Riverpod's zero-duration
  /// scheduler timer, queued when the autoDispose booking provider loses its
  /// last listener, which needs one more frame to run.
  void screenTest(String name, Future<void> Function(WidgetTester) body) {
    testWidgets(name, (tester) async {
      await body(tester);
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
    });
  }

  // One test per state PER LANGUAGE, not a loop inside one body: two screens in
  // one test would unmount the first tree mid-body and leave Riverpod's disposal
  // timer queued behind the assertions.
  group('every state renders', () {
    for (final locale in [const Locale('en'), const Locale('ar')]) {
      final tag = locale.languageCode;

      for (final status in [
        'Requested',
        'Approved',
        'Confirmed',
        'PickedUp',
        'Returned',
        'Completed',
      ]) {
        screenTest('$status in $tag', (tester) async {
          await pump(
            tester,
            bookingOf(
              status: status,
              history: pathTo(status),
              isAwaitingDecision: status == 'Requested',
              isAwaitingPayment: status == 'Approved',
              depositPaid: const {'Confirmed', 'PickedUp', 'Returned', 'Completed'}
                  .contains(status),
            ),
            locale: locale,
          );

          expect(tester.takeException(), isNull, reason: '$status overflowed in $tag');
          // The reference identifies the screen in both languages.
          expect(find.text('KH-24-0007'), findsWidgets);
          // The lifecycle is drawn.
          expect(find.byType(BookingTimeline), findsOneWidget);
        });
      }

      for (final status in ['Rejected', 'Cancelled', 'NoShow', 'Expired']) {
        screenTest('$status in $tag', (tester) async {
          await pump(
            tester,
            bookingOf(status: status, history: [
              changeJson('Requested', now),
              changeJson(status, now.add(const Duration(hours: 2)),
                  from: 'Requested', actor: 'System'),
            ]),
            locale: locale,
          );

          expect(tester.takeException(), isNull, reason: '$status overflowed in $tag');
          expect(find.byType(BookingTimeline), findsOneWidget);
        });
      }
    }
  });

  screenTest('a past stage is named in the past, not as a thing still owed',
      (tester) async {
    // The status badge for Approved reads "Approved — deposit due". On a
    // finished rental that stage is history and its deposit was paid weeks ago,
    // so the timeline must not repeat the badge's present tense.
    await pump(tester, bookingOf(status: 'Completed', history: pathTo('Completed')));

    expect(find.text(en.bookingStageApproved), findsOneWidget);
    expect(find.text(en.statusApproved), findsNothing);
    expect(find.text(en.bookingStageConfirmed), findsOneWidget);
  });

  screenTest('the security deposit is not in the same column as the total',
      (tester) async {
    await pump(tester, bookingOf(status: 'Confirmed', history: pathTo('Confirmed'), depositPaid: true));

    // It is on screen, and the sentence that says whose it is and when it comes
    // back is with it. Without that, an aligned column of 165 / 33 / 132 / 150
    // reads either as money owed on top of the total or as a total that is wrong.
    expect(find.text(en.vehicleSecurityDeposit), findsOneWidget);
    expect(find.text(en.vehicleSecurityDepositHelp), findsOneWidget);
  });

  screenTest('a window that has closed says so instead of showing nothing',
      (tester) async {
    // Approved, payment window closed, sweep has not run. The badge still reads
    // "Approved — deposit due"; the screen used to render nothing beneath it.
    await pump(
      tester,
      bookingOf(status: 'Approved', history: pathTo('Approved'), isAwaitingPayment: false),
      locale: const Locale('ar'),
    );

    expect(find.text(ar.bookingLapsedPaymentTitle), findsOneWidget);
    // And no deposit prompt, which would be an offer that cannot be taken.
    expect(find.text(ar.bookingPayDeposit), findsNothing);
  });

  screenTest('an office that has left is named in Arabic, not in English',
      (tester) async {
    await pump(
      tester,
      bookingOf(
        status: 'Cancelled',
        dealerRemoved: true,
        history: [
          changeJson('Requested', now),
          changeJson('Cancelled', now.add(const Duration(hours: 1)), from: 'Requested'),
        ],
      ),
      locale: const Locale('ar'),
    );

    expect(find.text(ar.bookingDealerRemoved), findsWidgets);
    expect(find.textContaining('Dealer no longer', findRichText: true), findsNothing);
  });

  screenTest("the platform's own English sentences stay off an Arabic screen",
      (tester) async {
    // A system transition writes untranslatable prose with no reason code. The
    // status name beside it already says the same thing in the reader's language.
    await pump(
      tester,
      bookingOf(status: 'Expired', history: [
        changeJson('Requested', now),
        changeJson('Expired', now.add(const Duration(hours: 4)),
            from: 'Requested', actor: 'System', reason: 'Dealer did not respond.'),
      ]),
      locale: const Locale('ar'),
    );

    expect(find.textContaining('Dealer did not respond', findRichText: true), findsNothing);
    expect(find.text(ar.bookingStageExpired), findsWidgets);
  });

  screenTest('a reason a person typed is still shown, whatever the language',
      (tester) async {
    await pump(
      tester,
      bookingOf(status: 'Rejected', history: [
        changeJson('Requested', now),
        changeJson('Rejected', now.add(const Duration(hours: 1)),
            from: 'Requested', actor: 'Dealer', reason: 'السيارة في الصيانة'),
      ]),
      locale: const Locale('ar'),
    );

    expect(find.textContaining('السيارة في الصيانة', findRichText: true), findsWidgets);
  });

  // The handover code: offered for exactly the two statuses the server issues one for.
  group('the handover code button', () {
    for (final (status, label) in [
      ('Confirmed', 'pickup'),
      ('PickedUp', 'return'),
    ]) {
      screenTest('is there for a $status booking, for the $label', (tester) async {
        await pump(tester, bookingOf(status: status, history: pathTo(status), depositPaid: true));
        await tester.scrollUntilVisible(find.byKey(const ValueKey('handover-code-button')), 200,
            scrollable: find.byType(Scrollable).first);
        expect(
          find.text(status == 'Confirmed' ? en.handoverShowPickupCode : en.handoverShowReturnCode),
          findsOneWidget,
        );
      });
    }

    for (final status in ['Requested', 'Approved', 'Returned', 'Completed']) {
      screenTest('is absent for a $status booking', (tester) async {
        await pump(
          tester,
          bookingOf(
            status: status,
            history: pathTo(status),
            isAwaitingDecision: status == 'Requested',
            isAwaitingPayment: status == 'Approved',
          ),
        );
        expect(find.byKey(const ValueKey('handover-code-button')), findsNothing);
      });
    }
  });
}
