import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/features/catalogue/vehicle_row.dart';
import 'package:khadra_mobile/features/notifications/notification_providers.dart';
import 'package:khadra_mobile/features/shortlist/shortlist_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// The saved-cars screen, and the row the owner settled on 2026-09-11.
///
/// A saved car that can no longer be booked **stays**, is still recognisable, says
/// "Currently unavailable", and offers no way to start a booking. The last of
/// those is the one worth a widget test rather than a reading: "there is no tap
/// target" is a claim about a rendered tree, and the honest way to check it is to
/// render the tree.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  SavedVehicle unavailable({
    String vehicleId = 'gone-1',
    SavedVehicleIdentity? identity,
  }) =>
      SavedVehicle(
        vehicleId: vehicleId,
        savedAt: DateTime.utc(2026, 9, 1, 9),
        identity: identity ??
            const SavedVehicleIdentity(
              make: 'BMW',
              model: '525i',
              year: 2002,
              galleryName: 'Dead Sea Drive',
            ),
        listing: null,
      );

  SavedVehicle listed() => SavedVehicle(
        vehicleId: 'live-1',
        savedAt: DateTime.utc(2026, 9, 2, 9),
        identity: const SavedVehicleIdentity(
          make: 'Toyota',
          model: 'Corolla',
          year: 2024,
          galleryName: 'Petra Rentals',
        ),
        listing: CatalogueListing.fromJson(const {
          'vehicleId': 'live-1',
          'make': 'Toyota',
          'model': 'Corolla',
          'year': 2024,
          'transmission': 'Automatic',
          'fuelType': 'Petrol',
          'seats': 5,
          'dailyRate': {'amount': 30, 'currency': 'JOD'},
          'isDeliveryAvailable': false,
          'gallery': {
            'dealerId': 'dealer-1',
            'businessName': 'Petra Rentals',
            'averageRating': 4.5,
            'reviewCount': 12,
          },
        }),
      );

  Future<FakeApi> pumpSavedCars(
    WidgetTester tester, {
    required List<SavedVehicle> saved,
    String locale = 'en',
  }) async {
    tester.view.physicalSize = const Size(1000, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final api = FakeApi()..savedCars = saved;
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(null),
      unreadNotificationCountProvider.overrideWith((ref) => Stream.value(0)),
    ]);
    addTearDown(container.dispose);

    await container
        .read(sessionProvider.notifier)
        .adoptTokens(FakeApi.fakeTokens());

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
    await tester.pumpAndSettle();

    router.go(Routes.shortlist);
    await tester.pumpAndSettle();
    expect(find.byType(ShortlistScreen), findsOneWidget);

    return api;
  }

  testWidgets('an unavailable car keeps its name and its gallery',
      (tester) async {
    await pumpSavedCars(tester, saved: [unavailable()]);

    expect(find.text('BMW 525i'), findsOneWidget);
    expect(find.text('Dead Sea Drive'), findsOneWidget);
    expect(find.text('CURRENTLY UNAVAILABLE'), findsOneWidget);
  });

  testWidgets('it says nothing about WHY, in either language', (tester) async {
    await pumpSavedCars(tester, saved: [unavailable()]);

    // Every one of these is a reason the platform answers identically. None of
    // them may reach a screen.
    for (final reason in ['hidden', 'maintenance', 'deleted', 'suspended']) {
      expect(
        find.textContaining(reason, findRichText: true),
        findsNothing,
        reason: 'The saved list must never name why a car is unavailable.',
      );
    }
  });

  testWidgets('the Arabic row carries the owner’s wording', (tester) async {
    await pumpSavedCars(tester, saved: [unavailable()], locale: 'ar');

    expect(find.text('غير متاحة حاليًا'), findsOneWidget);
    // Still named: a car a customer saved is a car they already saw.
    expect(find.text('BMW 525i'), findsOneWidget);
  });

  testWidgets('there is no way to start a booking from an unavailable car',
      (tester) async {
    await pumpSavedCars(tester, saved: [unavailable()]);

    // No catalogue row, which is the thing that routes to the vehicle screen —
    // and the listed car above it in the same list DOES render one, so this is a
    // real absence rather than a widget nothing on the screen uses.
    expect(find.byType(VehicleRow), findsNothing);

    // And nothing else on the row is tappable except the remove button. An
    // InkWell or a GestureDetector here would be a route to a screen that 404s,
    // and from there to a booking that cannot exist.
    final taps = find.descendant(
      of: find.byType(ShortlistScreen),
      matching: find.byWidgetPredicate(
          (widget) => widget is InkWell || widget is GestureDetector),
    );
    for (final element in tester.elementList(taps)) {
      final inRemoveButton = find
          .ancestor(of: find.byElementPredicate((e) => e == element),
              matching: find.byType(IconButton))
          .evaluate();
      expect(
        inRemoveButton,
        isNotEmpty,
        reason: 'An unavailable saved car must have no tap target but Remove.',
      );
    }
  });

  testWidgets('a listed car in the same list still renders its row', (tester) async {
    await pumpSavedCars(tester, saved: [unavailable(), listed()]);

    // The control for the assertion above: one of these two is bookable, and it
    // gets the ordinary row.
    expect(find.byType(VehicleRow), findsOneWidget);
    expect(find.text('CURRENTLY UNAVAILABLE'), findsOneWidget);
  });

  testWidgets('removing one is the customer’s own tap, and only theirs',
      (tester) async {
    final api = await pumpSavedCars(tester, saved: [unavailable(), listed()]);

    // Nothing was removed by rendering the list. The owner's rule: never
    // silently drop a saved car.
    expect(api.forgotten, isEmpty);

    await tester.tap(find.byIcon(Icons.delete_outline));
    await tester.pumpAndSettle();

    expect(api.forgotten, ['gone-1']);
  });

  testWidgets('a car with no name left renders without inventing one',
      (tester) async {
    await pumpSavedCars(
        tester, saved: [SavedVehicle(
          vehicleId: 'gone-2',
          savedAt: DateTime.utc(2026, 9, 1, 9),
          identity: null,
          listing: null,
        )]);

    expect(find.text('Currently unavailable'), findsOneWidget);
    expect(find.textContaining('Saved on'), findsOneWidget);
  });
}
