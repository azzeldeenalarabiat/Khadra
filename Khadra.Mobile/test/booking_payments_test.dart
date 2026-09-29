import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/format/formats.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/features/bookings/booking_detail_screen.dart';
import 'package:khadra_mobile/features/bookings/booking_providers.dart';
import 'package:khadra_mobile/features/bookings/payments_presentation.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;
import 'package:timezone/timezone.dart' as tz;

import 'support/fake_api.dart';

/// The booking's "Payments" section (payments Phase 4, owner 2026-09-26): what
/// was paid online, what went back and where it is, the balance and the deposit
/// as the server states them. The app renders the server's financial state and
/// computes nothing; against an API without it (a 404) it keeps what the
/// booking itself carries, so this build still runs against the API that is
/// live when it is published.
void main() {
  setUpAll(() async {
    tz_data.initializeTimeZones();
    await initializeDateFormatting('en');
    await initializeDateFormatting('ar');
  });

  final en = lookupAppLocalizations(const Locale('en'));
  final ar = lookupAppLocalizations(const Locale('ar'));
  Formats formatsFor(String locale) => Formats(
        locale: locale,
        currency: CurrencyConfig('JOD', 3),
        zone: tz.getLocation('Asia/Amman'),
      );
  String plain(String text) => text.replaceAll('\u2068', '').replaceAll('\u2069', '');
  Map<String, dynamic> jod(num amount) => {'amount': amount, 'currency': 'JOD'};

  Map<String, dynamic> refundJson(String reason, String status, num amount) => {
        'refundId': 'r-$reason',
        'paymentId': 'p-1',
        'reason': reason,
        'status': status,
        'amount': jod(amount),
        'feePart': jod(0),
        'requestedAt': '2026-09-26T10:00:00Z',
        'sentAt': null,
        'settledAt': status == 'Settled' ? '2026-09-27T10:00:00Z' : null,
        'failedAt': status == 'Failed' ? '2026-09-26T11:00:00Z' : null,
        'disputeTicketId': null,
      };

  /// The customer's projection, as the API serves it.
  Map<String, dynamic> financialsJson({
    String status = 'Confirmed',
    String purpose = 'Deposit',
    String paymentStatus = 'Applied',
    num charged = 18,
    num fee = 0,
    String balance = 'DueAtHandover',
    num balanceAmount = 72,
    String deposit = 'Held',
    Map<String, dynamic>? depositRefund,
    Map<String, dynamic>? decision,
    String progress = 'None',
    List<Map<String, dynamic>> refunds = const [],
    num refunded = 0,
    num inProgress = 0,
    num delayed = 0,
    bool needsReview = false,
    bool withPayment = true,
  }) =>
      {
        'bookingId': 'b-1',
        'bookingStatus': status,
        'currency': 'JOD',
        'generatedAt': '2026-09-26T10:00:00Z',
        'calculatorVersion': 1,
        'needsReview': needsReview,
        'summary': {
          'rentalSubtotal': jod(90),
          'deliveryFee': jod(0),
          'bookingTotal': jod(90),
          'requiredDeposit': jod(18),
          'securityDeposit': jod(150),
          'paidOnline': jod(paymentStatus == 'Applied' ? charged - fee : 0),
          'processingFees': jod(fee),
          'chargedOnline': jod(charged),
          'refunded': jod(refunded),
          'refundInProgress': jod(inProgress),
          'refundDelayed': jod(delayed),
        },
        'balance': {'state': balance, 'amount': jod(balanceAmount), 'cashRecorded': const <dynamic>[]},
        'deposit': {
          'state': deposit,
          'amount': jod(18),
          'windowEndsAt': '2026-09-28T05:14:00Z',
          'refund': depositRefund,
          'decision': decision,
        },
        'commission': null,
        'payments': withPayment
            ? [
                {
                  'paymentId': 'p-1',
                  'purpose': purpose,
                  'status': paymentStatus,
                  'refundProgress': progress,
                  'occurredAt': '2026-09-26T09:00:00Z',
                  'appliedToBooking': jod(paymentStatus == 'Applied' ? charged - fee : 0),
                  'amountCharged': jod(charged),
                  'processingFee': jod(fee),
                  'feeRefundable': true,
                  'refunds': refunds,
                  'createdAt': null,
                  'isSandbox': null,
                  'providerReference': null,
                  'failureCode': null,
                  'orphanReason': null,
                },
              ]
            : const <dynamic>[],
        'issues': null,
      };

  group('the financial state an API serves', () {
    test('is read in full', () {
      final state = BookingFinancials.fromJson(financialsJson(
        purpose: 'FullPayment',
        charged: 94.5,
        fee: 4.5,
        balance: 'PaidInFull',
        balanceAmount: 0,
        refunds: [refundJson('EndedBeforePickup', 'Settled', 76.5)],
        progress: 'Partial',
        refunded: 76.5,
      ));

      expect(state.bookingStatus, 'Confirmed');
      expect(state.summary.paidOnline!.amount, 90);
      expect(state.summary.processingFees!.amount, 4.5);
      expect(state.balance.state, 'PaidInFull');
      expect(state.deposit.state, 'Held');
      expect(state.deposit.windowEndsAt, DateTime.parse('2026-09-28T05:14:00Z'));
      final payment = state.payments.single;
      expect(payment.purpose, 'FullPayment');
      expect(payment.amountCharged!.amount, 94.5);
      expect(payment.refundProgress, 'Partial');
      expect(payment.refunds.single.reason, 'EndedBeforePickup');
      expect(payment.refunds.single.isRefunded, isTrue);
      expect(state.hasContent, isTrue);
    });

    test('degrades to "nothing to show" when fields are missing, never to a thrown cast', () {
      final sparse = BookingFinancials.fromJson({'bookingId': 'b-1'});

      expect(sparse.summary.paidOnline, isNull);
      expect(sparse.balance.state, '');
      expect(sparse.payments, isEmpty);
      expect(sparse.deposit.refund, isNull);
      // Nothing to show is no section at all, not an empty card.
      expect(sparse.hasContent, isFalse);
    });

    test('carries only the customer\'s own share of a dispute decision', () {
      final state = BookingFinancials.fromJson(financialsJson(
        deposit: 'DecidedByDispute',
        decision: {'ticketIds': ['t-1'], 'decidedAt': '2026-09-26T05:37:00Z', 'toCustomer': jod(9), 'toCustomerRefundStatus': 'Requested'},
      ));

      expect(state.deposit.toCustomer!.amount, 9);
    });
  });

  group('the section in words', () {
    FinancialDeposit deposit(String state, {Map<String, dynamic>? decision}) => FinancialDeposit.fromJson({
          'state': state,
          'amount': jod(18),
          'windowEndsAt': '2026-09-28T05:14:00Z',
          'decision': decision,
        });

    test('words the held deposit of pre-launch item 164 exactly as the owner decided, in both languages', () {
      expect(
        depositSentence(en, formatsFor('en'), deposit('HeldUnresolved')),
        'Your deposit remains held because a customer penalty was assessed and no dispute was opened. Final settlement is still pending.',
      );
      expect(
        depositSentence(ar, formatsFor('ar'), deposit('HeldUnresolved')),
        'لا يزال عربونك محتجزًا لأنّ غرامةً قُدِّرت على العميل ولم يُفتح أيّ نزاع. التسوية النهائية لا تزال معلّقة.',
      );
    });

    test('says a deposit the ledger kept as the penalty was kept, in both languages (payments Phase 8)', () {
      expect(
        plain(depositSentence(en, formatsFor('en'), deposit('KeptAsPenalty'))!),
        'Your deposit of JOD 18.000 was kept as the penalty assessed on this booking: the dispute window closed with no dispute opened.',
      );
      expect(plain(depositSentence(ar, formatsFor('ar'), deposit('KeptAsPenalty'))!), contains('احتُفظ بعربونك البالغ'));
    });

    test('names only the customer\'s own share of a dispute, in both languages', () {
      final decided = deposit('DecidedByDispute', decision: {'toCustomer': jod(9)});
      final nothing = deposit('DecidedByDispute', decision: {'toCustomer': jod(0)});

      expect(plain(depositSentence(en, formatsFor('en'), decided)!), 'A dispute decided that JOD 9.000 of your JOD 18.000 deposit is refunded to you.');
      expect(plain(depositSentence(en, formatsFor('en'), nothing)!), 'A dispute decided your JOD 18.000 deposit; none of it is refunded to you.');
      expect(plain(depositSentence(ar, formatsFor('ar'), decided)!), 'قرّر نزاع أن يُسترد لك 9.000 JOD من عربونك البالغ 18.000 JOD.');
    });

    test('words every deposit state it may be sent, and nothing for a deposit never paid', () {
      for (final state in [
        'Held', 'AppliedToRental', 'InSettlementWindow', 'UnderDispute', 'SettledWithRental',
        'ReturnedWithPayment', 'HeldUntilWindowCloses', 'HeldForAssessedPenalty', 'HeldUnresolved', 'KeptAsPenalty', 'Released',
      ]) {
        expect(depositSentence(en, formatsFor('en'), deposit(state)), isNotEmpty, reason: state);
        expect(depositSentence(ar, formatsFor('ar'), deposit(state)), isNotEmpty, reason: state);
      }
      expect(depositSentence(en, formatsFor('en'), deposit('NotPaid')), isNull);
    });

    test('titles each payment and reads its refunds as one status', () {
      FinancialPayment payment(String purpose, String status, String progress) =>
          FinancialPayment.fromJson({'purpose': purpose, 'status': status, 'refundProgress': progress});

      expect(paymentTitle(en, payment('Deposit', 'Applied', 'None')), 'Deposit payment');
      expect(paymentTitle(ar, payment('FullPayment', 'Applied', 'None')), 'الدفع الكامل');
      expect(paymentTitle(en, payment('Deposit', 'Orphaned', 'InProgress')), 'Payment not applied to the booking');
      expect(paymentProgress(en, 'None')!.label, 'Paid');
      expect(paymentProgress(en, 'Delayed')!.label, en.bookingRefundDelayed);
      expect(paymentProgress(ar, 'Partial')!.label, 'استُرد جزئيًا');
      expect(paymentProgress(en, 'Complete')!.label, en.bookingRefunded);
    });

    test('leaves a refund progress it does not know unsaid, rather than calling it paid', () {
      expect(paymentProgress(en, 'SomethingNewer'), isNull);
      expect(paymentProgress(en, ''), isNull);
    });
  });

  group('the screen', () {
    final now = DateTime.utc(2026, 9, 20, 9);

    Booking bookingOf({String status = 'Confirmed', bool isPaidInFull = false}) => Booking.fromJson({
          'bookingId': 'b-1',
          'reference': 'KH-24-0007',
          'status': status,
          'isTerminal': const {'Rejected', 'Cancelled', 'NoShow', 'Expired', 'Completed'}.contains(status),
          'dealerId': 'd-1',
          'vehicleId': 'v-1',
          'periodStart': now.add(const Duration(days: 3)).toIso8601String(),
          'periodEnd': now.add(const Duration(days: 6)).toIso8601String(),
          'pickupMethod': 'SelfPickup',
          'pricing': {
            'dailyRate': jod(30),
            'pickupDate': '2026-09-23',
            'returnDate': '2026-09-26',
            'days': 3,
            'rentalTotal': jod(90),
            'deliveryFee': jod(0),
            'totalPrice': jod(90),
            'depositPercent': 20,
            'depositAmount': jod(18),
            'balanceDue': jod(isPaidInFull ? 0 : 72),
            'securityDeposit': jod(150),
            'mileageUnlimited': true,
            'fuelPolicy': 'SameToSame',
          },
          'terms': const <String, dynamic>{},
          'penalty': null,
          'createdAt': now.toIso8601String(),
          'decisionDeadline': now.add(const Duration(hours: 4)).toIso8601String(),
          'paymentDeadline': now.add(const Duration(hours: 2)).toIso8601String(),
          'depositPaid': true,
          'canBeDisputed': false,
          'isAwaitingDecision': false,
          'isAwaitingPayment': false,
          'cancellation': {'canCancel': false, 'isFree': false},
          'canReportNonDelivery': false,
          'nonDeliveryReportableFrom': now.toIso8601String(),
          'canBeReviewed': false,
          'vehicle': {
            'vehicleId': 'v-1',
            'make': 'Kia',
            'model': 'Sportage',
            'year': 2024,
            'color': 'White',
            'plateNumber': '12-34567',
          },
          'dealerName': 'Petra Rentals',
          'dealerRemoved': false,
          'dealerCityId': null,
          'handovers': const <dynamic>[],
          'history': const <dynamic>[],
          'isPaidInFull': isPaidInFull,
        });

    Future<FakeApi> pump(
      WidgetTester tester, {
      required Locale locale,
      Map<String, dynamic>? financials,
      ApiFailure? failure,
      double width = 412,
      Booking? booking,
      BookingFinancialDocuments? documents,
      ApiFailure? documentsFailure,
    }) async {
      tester.view.physicalSize = Size(width, 915);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final api = FakeApi()
        ..bookingById = booking ?? bookingOf()
        ..financialsById = financials == null ? null : BookingFinancials.fromJson(financials)
        ..financialsFailure = failure
        ..bookingDocumentsById = documents
        ..bookingDocumentsFailure = documentsFailure;
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

    /// Ends by tearing the screen down, so its repaint timer and Riverpod's
    /// disposal timer run inside the test.
    void screenTest(String name, Future<void> Function(WidgetTester) body) {
      testWidgets(name, (tester) async {
        await body(tester);
        await tester.pumpWidget(const SizedBox());
        await tester.pump();
      });
    }

    Future<void> scrollTo(WidgetTester tester, Finder finder) async {
      await tester.scrollUntilVisible(finder, 300, scrollable: find.byType(Scrollable).first);
      await tester.pumpAndSettle();
    }

    for (final (locale, l10n) in [(const Locale('en'), en), (const Locale('ar'), ar)]) {
      final tag = locale.languageCode;

      for (final width in [360.0, 375.0, 412.0]) {
        screenTest('a paid deposit shows the Payments section at ${width.toInt()} in $tag', (tester) async {
          await pump(tester, locale: locale, width: width, financials: financialsJson());

          expect(tester.takeException(), isNull, reason: 'overflow at $width in $tag');
          await scrollTo(tester, find.text(l10n.paymentsHistory));
          expect(find.text(l10n.paymentsTitle), findsOneWidget);
          expect(find.text(l10n.paymentsPaidOnline), findsOneWidget);
          expect(find.text(l10n.bookBalanceAtPickup), findsOneWidget);
          // The section replaces the booking's own "how it is paid" block.
          expect(find.text(l10n.bookingHowItIsPaid), findsNothing);
        });
      }

      screenTest('an orphaned capture and a delayed refund are shown as such in $tag', (tester) async {
        await pump(
          tester,
          locale: locale,
          booking: bookingOf(status: 'Expired'),
          financials: financialsJson(
            status: 'Expired',
            paymentStatus: 'Orphaned',
            balance: 'NotDue',
            balanceAmount: 0,
            deposit: 'NotPaid',
            progress: 'Delayed',
            refunds: [refundJson('OrphanedCapture', 'Failed', 18)],
            delayed: 18,
          ),
        );

        await scrollTo(tester, find.text(l10n.paymentsOrphanNote));
        expect(tester.takeException(), isNull);
        expect(find.textContaining(l10n.paymentsKindOrphaned), findsOneWidget);
        expect(find.text(l10n.paymentsRefundDelayedTotal), findsOneWidget);
        expect(find.text(l10n.paymentsBalanceNotDue), findsOneWidget);
      });

      screenTest('the held deposit of item 164 reads as the owner decided in $tag', (tester) async {
        await pump(
          tester,
          locale: locale,
          booking: bookingOf(status: 'Cancelled'),
          financials: financialsJson(status: 'Cancelled', balance: 'NotDue', balanceAmount: 0, deposit: 'HeldUnresolved'),
        );

        await scrollTo(tester, find.text(l10n.paymentsDepositHeldUnresolved));
        expect(find.text(l10n.paymentsDepositHeldUnresolved), findsOneWidget);
      });

      screenTest('an API without the financial state keeps what the booking carries, in $tag', (tester) async {
        final api = await pump(tester, locale: locale);

        expect(api.financialsReads, greaterThan(0));
        await scrollTo(tester, find.text(l10n.bookingHowItIsPaid));
        expect(find.text(l10n.bookingHowItIsPaid), findsOneWidget);
        expect(find.text(l10n.paymentsTitle), findsNothing);
      });

      // ── The booking's issued documents (payments Phase 5b), from the shared fixture ──

      final documents = BookingFinancialDocuments.fromJson((jsonDecode(
        File('${Directory.current.parent.path}/docs/contracts/financial-documents-v1.json').readAsStringSync(),
      ) as Map<String, dynamic>)['bookingDocuments'] as Map<String, dynamic>);

      for (final width in [360.0, 375.0, 412.0]) {
        screenTest('the booking’s documents and what is being prepared sit under its payments at ${width.toInt()} in $tag',
            (tester) async {
          await pump(tester, locale: locale, width: width, financials: financialsJson(), documents: documents);

          await scrollTo(tester, find.text(l10n.invoicesPreparingBookingStatement));
          expect(tester.takeException(), isNull, reason: 'overflow at $width in $tag');
          expect(find.text(l10n.invoicesTitle), findsOneWidget);
          for (final row in documents.documents) {
            expect(find.text(row.number), findsOneWidget);
          }
          expect(find.text(l10n.invoicesCheckAgain), findsOneWidget);
        });
      }

      screenTest('"Check again" reads the booking’s documents once more, and nothing else, in $tag', (tester) async {
        final api = await pump(tester, locale: locale, financials: financialsJson(), documents: documents);
        final documentReads = api.bookingDocumentsReads;
        final financialReads = api.financialsReads;

        await scrollTo(tester, find.text(l10n.invoicesCheckAgain));
        await tester.tap(find.text(l10n.invoicesCheckAgain));
        await tester.pumpAndSettle();

        expect(api.bookingDocumentsReads, documentReads + 1);
        expect(api.financialsReads, financialReads);
      });

      screenTest('while "Check again" reads, the documents stay on screen and it cannot be tapped twice, in $tag', (tester) async {
        final api = await pump(tester, locale: locale, financials: financialsJson(), documents: documents);
        await scrollTo(tester, find.text(l10n.invoicesCheckAgain));

        final hold = Completer<void>();
        api.holdBookingDocuments = hold;
        await tester.tap(find.text(l10n.invoicesCheckAgain));
        await tester.pump();

        // Mid-read: the same rows, no spinner in their place, and the button off.
        expect(find.text(documents.documents.first.number), findsOneWidget);
        expect(find.byType(KhadraLoading), findsNothing);
        final button = tester.widget<TextButton>(find.widgetWithText(TextButton, l10n.invoicesCheckAgain));
        expect(button.onPressed, isNull);

        hold.complete();
        await tester.pumpAndSettle();
        expect(
          tester.widget<TextButton>(find.widgetWithText(TextButton, l10n.invoicesCheckAgain)).onPressed,
          isNotNull,
        );
      });

      screenTest('a failure listing the documents leaves the payment figures standing, in $tag', (tester) async {
        await pump(
          tester,
          locale: locale,
          financials: financialsJson(),
          documentsFailure: const ApiFailure(kind: ApiFailureKind.server, statusCode: 503),
        );

        await scrollTo(tester, find.text(l10n.invoicesTitle));
        expect(find.text(l10n.paymentsPaidOnline), findsOneWidget);
        expect(find.text(l10n.invoicesTitle), findsOneWidget);
        expect(find.text(l10n.actionRetry), findsOneWidget);
      });

      screenTest('an API without documents shows none and says nothing about them, in $tag', (tester) async {
        final api = await pump(tester, locale: locale, financials: financialsJson());

        await scrollTo(tester, find.text(l10n.paymentsHistory));
        expect(api.bookingDocumentsReads, greaterThan(0));
        expect(find.text(l10n.invoicesTitle), findsNothing);
      });
    }

    screenTest('a server error says so and offers to try again, with no invented figures', (tester) async {
      await pump(tester, locale: const Locale('en'), failure: const ApiFailure(kind: ApiFailureKind.server, statusCode: 500));

      await scrollTo(tester, find.text(en.actionRetry));
      expect(find.text(en.paymentsTitle), findsOneWidget);
      expect(find.text(en.actionRetry), findsOneWidget);
      // Neither the section's figures nor the older block are shown on a failure.
      expect(find.text(en.paymentsPaidOnline), findsNothing);
      expect(find.text(en.bookingHowItIsPaid), findsNothing);
    });

    screenTest('a progress and a refund status this build does not know get no badge', (tester) async {
      await pump(
        tester,
        locale: const Locale('en'),
        financials: financialsJson(
          purpose: 'FullPayment',
          charged: 90,
          progress: 'SomethingNewer',
          refunds: [refundJson('EndedBeforePickup', 'SomethingNewer', 72)],
        ),
      );

      await scrollTo(tester, find.text(en.refundReasonEndedBeforePickup));
      expect(tester.takeException(), isNull);
      expect(find.text(en.paymentsKindFullPayment), findsOneWidget);
      expect(find.text(en.bookingDepositPaidNote), findsNothing);
      expect(find.text(en.bookingRefundInitiated), findsNothing);
    });

    screenTest("the section reads again when the booking's money moves, and only then", (tester) async {
      final api = await pump(tester, locale: const Locale('en'), financials: financialsJson());
      final container = ProviderScope.containerOf(tester.element(find.byType(BookingDetailScreen)));
      final reads = api.financialsReads;

      // The same booking read again: nothing about its money moved.
      container.invalidate(bookingProvider('b-1'));
      await tester.pumpAndSettle();
      expect(api.bookingReads, greaterThan(1));
      expect(api.financialsReads, reads);

      // It comes back cancelled: its money may have moved, so the section follows.
      api.bookingById = bookingOf(status: 'Cancelled');
      container.invalidate(bookingProvider('b-1'));
      await tester.pumpAndSettle();
      expect(api.financialsReads, reads + 1);
    });

    screenTest('records under review get the review notice and no sentence about the deposit', (tester) async {
      // The item 165 shape: the rule returns the whole payment, and no refund was ever recorded.
      await pump(
        tester,
        locale: const Locale('en'),
        booking: bookingOf(status: 'Cancelled'),
        financials: financialsJson(status: 'Cancelled', balance: 'NotDue', balanceAmount: 0, deposit: 'ReturnedWithPayment', needsReview: true),
      );

      await scrollTo(tester, find.text(en.paymentsHistory));
      expect(find.text(en.paymentsReviewing), findsOneWidget);
      expect(find.text(en.paymentsPaidOnline), findsOneWidget);
      expect(find.text(en.paymentsDepositReturnedWithPayment), findsNothing);
    });

    screenTest('a booking nobody has paid shows no Payments section', (tester) async {
      await pump(
        tester,
        locale: const Locale('en'),
        booking: bookingOf(status: 'Expired'),
        financials: financialsJson(status: 'Expired', balance: 'NotDue', balanceAmount: 0, deposit: 'NotPaid', withPayment: false),
      );

      expect(find.text(en.paymentsTitle), findsNothing);
      expect(find.text(en.bookingHowItIsPaid), findsNothing);
    });
  });
}
