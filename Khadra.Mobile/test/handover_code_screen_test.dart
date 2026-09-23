import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/features/bookings/handover_code_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:qr_flutter/qr_flutter.dart';

import 'support/fake_api.dart';

/// The code the customer shows at the counter: fresh on every opening, both as digits and as a
/// QR, and gone by itself once the dealer records the handover.
void main() {
  final en = lookupAppLocalizations(const Locale('en'));

  HandoverCodeGrant grant(String code, {String type = 'Pickup', Duration validFor = const Duration(minutes: 15)}) =>
      HandoverCodeGrant(
        type: type,
        code: code,
        qrPayload: 'khadra-handover:v1:KH-24-0007:$code',
        expiresAt: DateTime.now().toUtc().add(validFor),
      );

  Booking bookingIn(String status) {
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
      'depositPaid': true,
      'isAwaitingDecision': false,
      'isAwaitingPayment': false,
      'cancellation': {'canCancel': false},
      'canReportNonDelivery': false,
      'nonDeliveryReportableFrom': now.toIso8601String(),
      'vehicle': {'vehicleId': 'v-1', 'make': 'Kia', 'model': 'Sportage', 'year': 2024},
      'dealerName': 'Petra Rentals',
      'handovers': const <dynamic>[],
      'history': const <dynamic>[],
    });
  }

  late FakeApi api;
  bool? result;

  Future<void> open(WidgetTester tester, {String status = 'Confirmed'}) async {
    api.bookingById = bookingIn(status);
    result = null;
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sharedPreferencesProvider.overrideWithValue(null),
    ]);
    addTearDown(container.dispose);

    await tester.pumpWidget(UncontrolledProviderScope(
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
            onPressed: () async =>
                result = await HandoverCodeScreen.open(context, bookingId: 'b-1', status: status),
            child: const Text('open'),
          ),
        ),
      ),
    ));
    await tester.tap(find.text('open'));
    await tester.pump();
    await tester.pump();
  }

  Future<void> close(WidgetTester tester) async {
    if (find.byType(HandoverCodeScreen).evaluate().isNotEmpty) {
      await tester.tap(find.byTooltip(en.actionClose));
    }
    await tester.pumpAndSettle();
  }

  setUp(() => api = FakeApi());

  testWidgets('shows a fresh code as six digits and as a QR', (tester) async {
    api.handoverGrants = [grant('482913')];
    await open(tester);

    expect(find.text('482 913'), findsOneWidget);
    expect(find.byType(QrImageView), findsOneWidget);
    expect(find.text(en.handoverPickupTitle), findsOneWidget);
    expect(find.textContaining('Valid for'), findsOneWidget);
    expect(api.handoverCalls, 1);

    await close(tester);
  });

  testWidgets('asking again shows the new code', (tester) async {
    api.handoverGrants = [grant('111222'), grant('333444')];
    await open(tester);

    await tester.tap(find.text(en.handoverNewCode));
    await tester.pump();
    await tester.pump();

    expect(find.text('333 444'), findsOneWidget);
    expect(api.handoverCalls, 2);

    await close(tester);
  });

  testWidgets('an expired code says so and offers a new one', (tester) async {
    api.handoverGrants = [grant('555666', validFor: const Duration(seconds: -1))];
    await open(tester);

    expect(find.text(en.handoverExpired), findsOneWidget);
    expect(find.text(en.handoverNewCode), findsOneWidget);

    await close(tester);
  });

  testWidgets('the return code is titled as one', (tester) async {
    api.handoverGrants = [grant('777888', type: 'Return')];
    await open(tester, status: 'PickedUp');

    expect(find.text(en.handoverReturnTitle), findsOneWidget);

    await close(tester);
  });

  testWidgets('it closes by itself once the dealer records the handover', (tester) async {
    api.handoverGrants = [grant('482913')];
    await open(tester);

    await tester.pump(HandoverCodeScreen.poll);
    await tester.pump();
    expect(find.byType(HandoverCodeScreen), findsOneWidget);

    api.bookingById = bookingIn('PickedUp');
    await tester.pump(HandoverCodeScreen.poll);
    await tester.pumpAndSettle();

    expect(find.byType(HandoverCodeScreen), findsNothing);
    expect(result, isTrue);
  });

  testWidgets('closing by hand answers false', (tester) async {
    api.handoverGrants = [grant('482913')];
    await open(tester);

    await close(tester);

    expect(result, isFalse);
  });
}
