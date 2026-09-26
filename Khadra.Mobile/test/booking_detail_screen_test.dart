import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
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
    Map<String, dynamic>? payment,
    num balanceDue = 132,
    bool isPaidInFull = false,
    bool willRefundDeposit = false,
    Map<String, dynamic>? confirmingPayment,
    Map<String, dynamic>? depositRefund,
    Map<String, dynamic>? cancellationPenalty,
    Map<String, dynamic>? refundAmount,
    List<Map<String, dynamic>>? refunds,
    Map<String, dynamic>? refundedAmount,
    Map<String, dynamic>? refundOutstandingAmount,
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
          'balanceDue': {'amount': balanceDue, 'currency': 'JOD'},
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
        'cancellation': {
          'canCancel': canCancel,
          'isFree': willRefundDeposit,
          'willRefundDeposit': willRefundDeposit,
          if (cancellationPenalty != null) 'penalty': cancellationPenalty,
          if (refundAmount != null) 'refundAmount': refundAmount,
        },
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
        'payment': payment,
        'isPaidInFull': isPaidInFull,
        'confirmingPayment': confirmingPayment,
        'depositRefund': depositRefund,
        if (refunds != null) 'refunds': refunds,
        if (refundedAmount != null) 'refundedAmount': refundedAmount,
        if (refundOutstandingAmount != null) 'refundOutstandingAmount': refundOutstandingAmount,
      });

  Future<FakeApi> pump(WidgetTester tester, Booking booking,
      {Locale locale = const Locale('en'), FakeApi? api, double width = 412}) async {
    tester.view.physicalSize = Size(width, 915);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    api = (api ?? FakeApi())..bookingById = booking;
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
    return api;
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
  // The card's plate line was a Row that could not wrap, and at 360 and 375 in
  // Arabic it overflowed beside the car's photo. Every state was only ever
  // rendered at 412, which is why nothing caught it.
  group('the booking card fits a narrow phone', () {
    for (final locale in [const Locale('en'), const Locale('ar')]) {
      for (final width in [360.0, 375.0]) {
        screenTest('at ${width.toInt()} in ${locale.languageCode}', (tester) async {
          await pump(
            tester,
            bookingOf(status: 'Confirmed', history: pathTo('Confirmed'), depositPaid: true),
            locale: locale,
            width: width,
          );

          expect(tester.takeException(), isNull);
          expect(find.textContaining('12-34567', findRichText: true), findsWidgets);
        });
      }
    }
  });

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
  // The two ways to pay an approved booking (owner, 2026-09-24). The figures are
  // the server's own, the owner's example: 165 JOD booking, 33 deposit, and a
  // 1.5% fee on the full amount so the fee line is exercised too.
  group('paying an approved booking', () {
    Map<String, dynamic> m(num amount) => {'amount': amount, 'currency': 'JOD'};
    final payment = {
      'canPay': true,
      'unavailableReason': null,
      'amountDue': m(33),
      'payBy': now.add(const Duration(hours: 2)).toIso8601String(),
      'liveAttempt': null,
      'options': [
        {
          'purpose': 'Deposit',
          'selectedPaymentAmount': m(33),
          'processingFee': m(0),
          'totalChargedNow': m(33),
          'remainingBalanceAfter': m(132),
        },
        {
          'purpose': 'FullPayment',
          'selectedPaymentAmount': m(165),
          'processingFee': m(2.475),
          'totalChargedNow': m(167.475),
          'remainingBalanceAfter': m(0),
        },
      ],
    };

    Booking approved() => bookingOf(
          status: 'Approved',
          history: pathTo('Approved'),
          isAwaitingPayment: true,
          payment: payment,
        );

    for (final locale in [const Locale('en'), const Locale('ar')]) {
      final l10n = locale.languageCode == 'ar' ? ar : en;

      screenTest('both choices are offered, the deposit chosen (${locale.languageCode})', (tester) async {
        await pump(tester, approved(), locale: locale);

        expect(find.text(l10n.paymentChooseTitle), findsOneWidget);
        expect(find.text(l10n.paymentDepositTitle), findsWidgets);
        expect(find.text(l10n.paymentFullTitle), findsOneWidget);
        // The button carries the deposit's figure until the choice changes.
        expect(find.textContaining(RegExp(r'33(\.000)?')), findsWidgets);
        expect(find.byType(FilledButton), findsOneWidget);
        final button = tester.widget<FilledButton>(find.byType(FilledButton));
        expect(button.onPressed, isNotNull);
      });
    }

    screenTest('switching to the full amount changes the charge and shows the fee', (tester) async {
      await pump(tester, approved());

      await tester.tap(find.text(en.paymentFullTitle));
      await tester.pumpAndSettle();

      expect(find.textContaining('167.475'), findsWidgets);
      expect(find.text(en.paymentSummaryFee), findsOneWidget);
      expect(find.textContaining('2.475'), findsWidgets);
    });

    screenTest('paying asks the server by PURPOSE, never by amount', (tester) async {
      final api = await pump(tester, approved());
      // A checkout with no page to open: the screen says so and stays put, which
      // keeps the in-app browser out of a widget test.
      api.checkoutAttempt = PaymentAttempt.maybe({
        'paymentId': 'p-1',
        'status': 'Pending',
        'amount': m(167.475),
        'checkoutUrl': null,
        'expiresAt': now.add(const Duration(minutes: 30)).toIso8601String(),
        'failureCode': null,
        'isSandbox': true,
        'purpose': 'FullPayment',
      });

      await tester.tap(find.text(en.paymentFullTitle));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byType(FilledButton));
      await tester.tap(find.byType(FilledButton));
      await tester.pumpAndSettle();

      expect(api.checkoutPurposes, ['FullPayment']);
    });

    screenTest('an older server with no choices keeps the single deposit button', (tester) async {
      await pump(
        tester,
        bookingOf(
          status: 'Approved',
          history: pathTo('Approved'),
          isAwaitingPayment: true,
          payment: {...payment, 'options': const <dynamic>[]},
        ),
      );

      expect(find.text(en.paymentFullTitle), findsNothing);
      expect(find.text(en.bookingPayDeposit), findsOneWidget);
    });
  });

  // Paid by deposit or in full (owner, 2026-09-25). The server's figures were
  // right after a full payment, but every screen still called it a deposit. The
  // app must tell the two apart, in both languages, from the server's own facts.
  group('paid by deposit or in full', () {
    Map<String, dynamic> m(num amount) => {'amount': amount, 'currency': 'JOD'};
    Map<String, dynamic> confirming(String purpose, num charged) => {
          'purpose': purpose,
          'amountCharged': m(charged),
          'processingFee': m(0),
          'appliedToBooking': m(charged),
          'paidAt': now.toIso8601String(),
          'refundOnFreeCancellation': m(charged),
        };

    // The builder's 165.000 booking: 33.000 deposit, 132.000 cash at pickup.
    Booking paid({required bool full, bool cancellable = false, Map<String, dynamic>? refund}) =>
        bookingOf(
          status: refund == null ? 'Confirmed' : 'Cancelled',
          history: refund == null
              ? pathTo('Confirmed')
              : [
                  ...pathTo('Confirmed'),
                  changeJson('Cancelled', now.add(const Duration(hours: 3)), from: 'Confirmed'),
                ],
          depositPaid: true,
          canCancel: cancellable,
          willRefundDeposit: cancellable,
          balanceDue: full ? 0 : 132,
          isPaidInFull: full,
          confirmingPayment: confirming(full ? 'FullPayment' : 'Deposit', full ? 165 : 33),
          depositRefund: refund,
        );

    /// The figure beside a label in the "How it is paid" card.
    Finder valueOf(String label) => find.descendant(
          of: find.widgetWithText(KhadraDetailRow, label),
          matching: find.byType(Text),
        );

    bool rowShows(WidgetTester tester, String label, RegExp figure) => tester
        .widgetList<Text>(valueOf(label))
        .any((text) => figure.hasMatch(text.data ?? ''));

    for (final locale in [const Locale('en'), const Locale('ar')]) {
      final l10n = locale.languageCode == 'ar' ? ar : en;
      final tag = locale.languageCode;

      screenTest('a deposit keeps the deposit wording ($tag)', (tester) async {
        await pump(tester, paid(full: false), locale: locale);

        expect(find.text(l10n.bookingStageConfirmed), findsWidgets);
        expect(find.text(l10n.bookingStagePaidInFull), findsNothing);
        expect(find.text(l10n.bookingDepositPaidNote), findsOneWidget);
        expect(find.text(l10n.bookBalanceAtPickup), findsOneWidget);
        expect(rowShows(tester, l10n.bookBalanceAtPickup, RegExp(r'132')), isTrue);
        expect(find.text(l10n.bookingPaymentType), findsNothing);
        expect(find.text(l10n.bookingPaidInFullNote), findsNothing);
      });

      screenTest('a full payment says paid in full, what was charged and that nothing is left ($tag)',
          (tester) async {
        await pump(tester, paid(full: true), locale: locale);

        // The stage, in the lifecycle and in the activity log alike.
        expect(find.text(l10n.bookingStagePaidInFull), findsWidgets);
        expect(find.text(l10n.bookingStageConfirmed), findsNothing);
        // The card: the payment's type, what it charged, and nothing remaining.
        expect(find.text(l10n.bookingPaymentType), findsOneWidget);
        expect(find.text(l10n.bookingPaymentTypeFull), findsOneWidget);
        // The badge, in its own row: in English it shares its words with the stage.
        expect(
          find.descendant(
            of: find.widgetWithText(KhadraDetailRow, l10n.bookingPaymentType),
            matching: find.text(l10n.bookingPaidInFullNote),
          ),
          findsOneWidget,
        );
        expect(rowShows(tester, l10n.bookingAmountCharged, RegExp(r'165')), isTrue);
        expect(rowShows(tester, l10n.bookingRemainingBalance, RegExp(r'(^|[^0-9.])0(\.000)?([^0-9]|$)')), isTrue);
        expect(find.text(l10n.bookingPaidInFullNothingDue), findsOneWidget);
        // Never the deposit wording, and never "cash at pickup".
        expect(find.text(l10n.bookingDepositPaidNote), findsNothing);
        expect(find.text(l10n.bookBalanceAtPickup), findsNothing);
      });
    }

    screenTest('a free cancellation of a full payment promises the server refund figure', (tester) async {
      await pump(tester, paid(full: true, cancellable: true));

      await tester.ensureVisible(find.text(en.cancelTitle));
      await tester.tap(find.text(en.cancelTitle));
      await tester.pumpAndSettle();

      expect(find.textContaining('will be refunded to your original payment method'), findsOneWidget);
      expect(find.textContaining(RegExp(r'Free cancellation\. .*165')), findsOneWidget);
      expect(find.text(en.cancelFreeRefundNotice), findsNothing);
    });

    // ── Phase 3 (owner, 2026-09-26): a full payment ending before pickup ──
    final latePenalty = {
      'attributedTo': 'Customer', 'minPercent': 100, 'maxPercent': 100, 'minAmount': m(33), 'maxAmount': m(33),
      'isRange': false, 'isNothingOwed': false, 'requiresTicketToEnforce': true, 'reason': '',
    };
    Booking lateCancellable() => bookingOf(
          status: 'Confirmed',
          history: pathTo('Confirmed'),
          depositPaid: true,
          canCancel: true,
          balanceDue: 0,
          isPaidInFull: true,
          confirmingPayment: confirming('FullPayment', 165),
          cancellationPenalty: latePenalty,
          refundAmount: m(132),
        );
    FakeApi withReasons() => FakeApi()
      ..cancellationReasons = [
        {'name': 'PlansChanged', 'labelEn': 'Plans changed', 'labelAr': 'تغيّرت الخطط'},
      ];

    Future<void> openSheetAndConfirm(WidgetTester tester) async {
      await tester.ensureVisible(find.text(en.cancelTitle));
      await tester.tap(find.text(en.cancelTitle));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Plans changed'));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text(en.cancelConfirm));
      await tester.tap(find.text(en.cancelConfirm));
      await tester.pumpAndSettle();
    }

    screenTest('a late cancellation of a full payment says what comes back above the deposit, and sends it',
        (tester) async {
      final api = await pump(tester, lateCancellable(), api: withReasons());

      await tester.ensureVisible(find.text(en.cancelTitle));
      await tester.tap(find.text(en.cancelTitle));
      await tester.pumpAndSettle();
      expect(find.textContaining(RegExp(r'You will get .*132.* everything you paid above the deposit')), findsOneWidget);
      expect(find.textContaining('Free cancellation'), findsNothing);

      await tester.tap(find.text('Plans changed'));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text(en.cancelConfirm));
      await tester.tap(find.text(en.cancelConfirm));
      await tester.pumpAndSettle();

      expect(api.cancellations.single.$3, 132);
    });

    screenTest('a refund that changed while the sheet was open is shown, and nothing is cancelled', (tester) async {
      final api = withReasons()
        ..cancelFailure = const ApiFailure(
          kind: ApiFailureKind.conflict,
          code: 'booking.refund_changed',
          statusCode: 409,
          extensions: {
            'currentRefund': {'amount': 99, 'currency': 'JOD'},
          },
        );
      await pump(tester, lateCancellable(), api: api);

      await openSheetAndConfirm(tester);

      expect(find.textContaining(RegExp(r'has changed to .*99')), findsOneWidget);
      expect(find.text(en.cancelConfirm), findsOneWidget);

      // Confirmed again, the sheet sends the figure it was just given.
      await tester.ensureVisible(find.text(en.cancelConfirm));
      await tester.tap(find.text(en.cancelConfirm));
      await tester.pumpAndSettle();
      expect(api.cancellations.map((sent) => sent.$3), [132, 99]);
    });

    for (final locale in [const Locale('en'), const Locale('ar')]) {
      final l10n = locale.languageCode == 'ar' ? ar : en;
      final tag = locale.languageCode;

      screenTest('every refund is listed with its reason, amount and status ($tag)', (tester) async {
        Map<String, dynamic> row(String reason, String status, num amount) => {
              'refundId': 'r-$reason',
              'paymentId': 'p-1',
              'reason': reason,
              'amount': m(amount),
              'status': status,
              'requestedAt': now.add(const Duration(hours: 3)).toIso8601String(),
              'settledAt': status == 'Settled' ? now.add(const Duration(hours: 4)).toIso8601String() : null,
              'failedAt': null,
            };
        await pump(
          tester,
          bookingOf(
            status: 'Cancelled',
            history: [
              ...pathTo('Confirmed'),
              changeJson('Cancelled', now.add(const Duration(hours: 3)), from: 'Confirmed', actor: 'Dealer'),
            ],
            depositPaid: true,
            balanceDue: 0,
            isPaidInFull: true,
            confirmingPayment: confirming('FullPayment', 165),
            refunds: [row('EndedBeforePickup', 'Settled', 132), row('DisputeWindowClosed', 'Sent', 33)],
            refundedAmount: m(132),
            refundOutstandingAmount: m(33),
          ),
          locale: locale,
        );

        // The notice is at the top; the refunds card is below the price, past the
        // list's build window, so the page is scrolled to it.
        expect(find.textContaining(tag == 'ar' ? 'فوق العربون' : 'above the deposit'), findsWidgets);
        await tester.scrollUntilVisible(
          find.text(l10n.bookingRefundsTitle),
          300,
          scrollable: find.descendant(of: find.byType(ListView), matching: find.byType(Scrollable)).first,
        );
        await tester.pumpAndSettle();
        expect(find.text(l10n.bookingRefundsTitle), findsOneWidget);
        expect(find.textContaining(l10n.refundReasonEndedBeforePickup), findsOneWidget);
        expect(find.textContaining(l10n.refundReasonDisputeWindowClosed), findsOneWidget);
        expect(find.text(l10n.bookingRefundedTotal), findsOneWidget);
        expect(find.text(l10n.bookingRefundOutstanding), findsOneWidget);
        expect(find.text(l10n.bookingRefunded), findsOneWidget);
        expect(find.text(l10n.bookingRefundInitiated), findsOneWidget);
        // Part of it came back: never "Paid in full".
        expect(find.text(l10n.bookingPaidInFullNote), findsNothing);
        expect(find.text(l10n.bookingPaidInFullNothingDue), findsNothing);
      });
    }

    screenTest('the refund of a full payment speaks of the payment, in Arabic', (tester) async {
      await pump(
        tester,
        paid(
          full: true,
          refund: {
            'status': 'Sent',
            'amount': m(165),
            'requestedAt': now.add(const Duration(hours: 3)).toIso8601String(),
            'settledAt': null,
          },
        ),
        locale: const Locale('ar'),
      );

      expect(find.textContaining('من دفعتك'), findsOneWidget);
      expect(find.textContaining('عربونك'), findsNothing);
      expect(find.text(ar.bookingRefundInitiated), findsOneWidget);
    });
  });

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
