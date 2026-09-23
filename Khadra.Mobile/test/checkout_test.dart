import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/format/formats.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/features/bookings/checkout_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;
import 'package:timezone/timezone.dart' as tz;

import 'support/fake_api.dart';

/// The in-app checkout: it hosts whatever page the server minted and learns the outcome from the
/// BOOKING, never from the page. Nothing here is specific to the sandbox; the same rules have to
/// hold for a hosted provider checkout.
void main() {
  setUpAll(() async {
    tz_data.initializeTimeZones();
    await initializeDateFormatting('en');
  });

  final en = lookupAppLocalizations(const Locale('en'));
  final expires = DateTime.now().toUtc().add(const Duration(minutes: 30));

  Map<String, dynamic> attemptJson(String id, {String status = 'Pending'}) => {
        'paymentId': id,
        'status': status,
        'amount': {'amount': 33, 'currency': 'JOD'},
        'checkoutUrl': 'https://checkout.example/session/$id',
        'expiresAt': expires.toIso8601String(),
        'failureCode': null,
        'isSandbox': true,
      };

  final opened = PaymentAttempt.maybe(attemptJson('p-1'))!;

  /// A booking as the detail endpoint sends one, reduced to what these rules read.
  Booking bookingWith({
    String status = 'Approved',
    bool awaitingPayment = true,
    String? liveAttemptId,
  }) {
    final now = DateTime.utc(2026, 9, 20, 9);
    return Booking.fromJson({
      'bookingId': 'b-1',
      'reference': 'KH-24-0007',
      'status': status,
      'isTerminal': false,
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
      },
      'terms': const <String, dynamic>{},
      'createdAt': now.toIso8601String(),
      'paymentDeadline': now.add(const Duration(hours: 2)).toIso8601String(),
      'depositPaid': !awaitingPayment,
      'isAwaitingDecision': false,
      'isAwaitingPayment': awaitingPayment,
      'cancellation': {'canCancel': false},
      'canReportNonDelivery': false,
      'nonDeliveryReportableFrom': now.toIso8601String(),
      'vehicle': {'vehicleId': 'v-1', 'make': 'Kia', 'model': 'Sportage', 'year': 2024},
      'dealerName': 'Petra Rentals',
      'handovers': const <dynamic>[],
      'history': const <dynamic>[],
      'payment': {
        'canPay': awaitingPayment,
        'unavailableReason': null,
        'amountDue': {'amount': 33, 'currency': 'JOD'},
        'payBy': now.add(const Duration(hours: 2)).toIso8601String(),
        'liveAttempt': liveAttemptId == null ? null : attemptJson(liveAttemptId),
      },
    });
  }

  group('when waiting is over', () {
    test('still the live attempt: keep waiting', () {
      expect(checkoutSettled(bookingWith(liveAttemptId: 'p-1'), 'p-1'), isFalse);
    });

    test('confirmed: over', () {
      expect(
          checkoutSettled(bookingWith(status: 'Confirmed', awaitingPayment: false), 'p-1'),
          isTrue);
    });

    test('the attempt is no longer the live one (declined, failed, lapsed): over', () {
      expect(checkoutSettled(bookingWith(), 'p-1'), isTrue);
    });

    test('a DIFFERENT attempt is live now: over, this one is not the one in flight', () {
      expect(checkoutSettled(bookingWith(liveAttemptId: 'p-2'), 'p-1'), isTrue);
    });

    test('the booking expired while the page was open: over', () {
      expect(checkoutSettled(bookingWith(status: 'Expired', awaitingPayment: false), 'p-1'),
          isTrue);
    });
  });

  test('it reads fast while a result is likely, then backs off', () {
    expect(checkoutPollInterval(Duration.zero), CheckoutScreen.fastPoll);
    expect(checkoutPollInterval(const Duration(seconds: 59)), CheckoutScreen.fastPoll);
    expect(checkoutPollInterval(const Duration(seconds: 60)), CheckoutScreen.slowPoll);
    expect(CheckoutScreen.fastPoll, lessThan(CheckoutScreen.slowPoll));
  });

  group('the page may go anywhere on the web, and nowhere else', () {
    test('web content is followed, whatever the host', () {
      for (final url in [
        'https://checkout.example/pay',
        'https://acs.some-bank.example/3ds',
        'http://10.0.2.2:5012/api/v1/sandbox',
        'about:blank',
        'data:text/html,hi',
        'blob:https://checkout.example/123',
      ]) {
        expect(allowCheckoutNavigation(Uri.parse(url)), isTrue, reason: url);
      }
    });

    test('anything that would hand the customer to another app is refused', () {
      for (final url in [
        'intent://pay#Intent;scheme=bank;end',
        'market://details?id=x',
        'tel:+962790000000',
        'mailto:x@example.com',
        'bankapp://confirm',
        'javascript:alert(1)',
      ]) {
        expect(allowCheckoutNavigation(Uri.parse(url)), isFalse, reason: url);
      }
    });
  });

  group('what the booking screen says afterwards', () {
    // Late: the zone database is loaded in setUpAll, after this body has run.
    late final formats = Formats(
      locale: 'en',
      currency: const CurrencyConfig('JOD', 3),
      zone: tz.getLocation('Asia/Amman'),
    );

    test('confirmed: nothing — the screen itself shows it', () {
      expect(
        checkoutReturnMessage(en, CheckoutExit.settled,
            bookingWith(status: 'Confirmed', awaitingPayment: false), opened, formats),
        isNull,
      );
    });

    test('closed by hand with the session still open: until when, never "cancelled"', () {
      final message = checkoutReturnMessage(
          en, CheckoutExit.closed, bookingWith(liveAttemptId: 'p-1'), opened, formats);
      expect(message, en.checkoutStillOpen(formats.time(expires)));
      expect(message!.toLowerCase(), isNot(contains('cancel')));
    });

    test('the attempt ended without confirming: says exactly that, never "declined"', () {
      final message =
          checkoutReturnMessage(en, CheckoutExit.settled, bookingWith(), opened, formats);
      expect(message, en.checkoutAttemptEnded);
      expect(message!.toLowerCase(), isNot(contains('declin')));
    });

    test('closed by hand after the attempt already ended: the same honest sentence', () {
      expect(checkoutReturnMessage(en, CheckoutExit.closed, bookingWith(), opened, formats),
          en.checkoutAttemptEnded);
    });

    test('the booking expired meanwhile: nothing — the screen shows Expired', () {
      expect(
        checkoutReturnMessage(en, CheckoutExit.settled,
            bookingWith(status: 'Expired', awaitingPayment: false), opened, formats),
        isNull,
      );
    });
  });

  group('the screen', () {
    late FakeApi api;
    CheckoutExit? exit;

    Future<void> pump(WidgetTester tester) async {
      api = FakeApi()..bookingById = bookingWith(liveAttemptId: 'p-1');
      exit = null;
      final container = ProviderContainer(overrides: [
        apiProvider.overrideWithValue(api),
        sharedPreferencesProvider.overrideWithValue(null),
      ]);
      addTearDown(container.dispose);

      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: MaterialApp(
            supportedLocales: AppLocalizations.supportedLocales,
            localizationsDelegates: const [
              AppLocalizations.delegate,
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            home: Builder(
              builder: (context) => TextButton(
                onPressed: () async {
                  exit = await Navigator.of(context).push<CheckoutExit>(MaterialPageRoute(
                    builder: (_) => CheckoutScreen(
                      bookingId: 'b-1',
                      attempt: opened,
                      pageBuilder: (url) => Text('page at $url'),
                    ),
                  ));
                },
                child: const Text('pay'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('pay'));
      await tester.pumpAndSettle();
    }

    testWidgets('hosts the server\'s URL and shows whose page it is', (tester) async {
      await pump(tester);

      expect(find.text('page at https://checkout.example/session/p-1'), findsOneWidget);
      expect(find.text('checkout.example'), findsOneWidget);
      expect(find.text(en.checkoutTitle), findsOneWidget);

      await tester.tap(find.byTooltip(en.checkoutClose));
      await tester.pumpAndSettle();
    });

    testWidgets('keeps reading while the attempt is live, and closes itself when it is not',
        (tester) async {
      await pump(tester);

      await tester.pump(CheckoutScreen.fastPoll);
      await tester.pump(CheckoutScreen.fastPoll);
      expect(api.bookingReads, 2);
      expect(find.byType(CheckoutScreen), findsOneWidget);

      // The webhook landed on the server.
      api.bookingById = bookingWith(status: 'Confirmed', awaitingPayment: false);
      await tester.pump(CheckoutScreen.fastPoll);
      await tester.pumpAndSettle();

      expect(find.byType(CheckoutScreen), findsNothing);
      expect(exit, CheckoutExit.settled);
    });

    testWidgets('a failed read is not an answer: the page stays and asks again', (tester) async {
      await pump(tester);
      api.bookingById = null; // the fake throws, like a dropped connection

      await tester.pump(CheckoutScreen.fastPoll);
      await tester.pump(CheckoutScreen.fastPoll);
      expect(find.byType(CheckoutScreen), findsOneWidget);

      api.bookingById = bookingWith(); // attempt no longer live
      await tester.pump(CheckoutScreen.fastPoll);
      await tester.pumpAndSettle();
      expect(exit, CheckoutExit.settled);
    });

    testWidgets('closing by hand answers "closed" and stops reading', (tester) async {
      await pump(tester);

      await tester.tap(find.byTooltip(en.checkoutClose));
      await tester.pumpAndSettle();
      expect(exit, CheckoutExit.closed);

      final reads = api.bookingReads;
      await tester.pump(const Duration(seconds: 30));
      expect(api.bookingReads, reads);
    });

    testWidgets('Android Back leaves the checkout rather than stepping back inside it',
        (tester) async {
      await pump(tester);

      await tester.binding.handlePopRoute();
      await tester.pumpAndSettle();
      expect(exit, CheckoutExit.closed);
    });

    testWidgets('nothing is read while the app is in the background', (tester) async {
      await pump(tester);

      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      final reads = api.bookingReads;
      await tester.pump(const Duration(seconds: 30));
      expect(api.bookingReads, reads);

      // Coming back reads at once — the moment a customer returns from their bank app is the
      // moment a result is most likely waiting.
      api.bookingById = bookingWith(status: 'Confirmed', awaitingPayment: false);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await tester.pumpAndSettle();
      expect(exit, CheckoutExit.settled);
    });
  });
}
