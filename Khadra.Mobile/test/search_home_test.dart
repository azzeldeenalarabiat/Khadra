import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/features/catalogue/search_providers.dart';
import 'package:khadra_mobile/features/catalogue/search_screen.dart';
import 'package:khadra_mobile/features/notifications/notification_providers.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// Home, laid out the way a rental is thought about: where and when, then what
/// kind, then how many — and every one of those choices built from the platform's
/// own data rather than from anything written into the app.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  const sedan = Lookup(id: 'type-sedan', nameEn: 'Sedan', nameAr: 'سيدان', isActive: true);
  const suv = Lookup(id: 'type-suv', nameEn: 'SUV', nameAr: 'دفع رباعي', isActive: true);
  const van = Lookup(id: 'type-van', nameEn: 'Van', nameAr: 'فان', isActive: true);
  const amman = Lookup(id: 'city-amman', nameEn: 'Amman', nameAr: 'عمّان', isActive: true);
  const irbid = Lookup(id: 'city-irbid', nameEn: 'Irbid', nameAr: 'إربد', isActive: true);

  final en = lookupAppLocalizations(const Locale('en'));

  Future<ProviderContainer> pumpHome(
    WidgetTester tester, {
    required FakeApi api,
    SearchFilter filter = const SearchFilter(),
    Locale locale = const Locale('en'),
  }) async {
    tester.view.physicalSize = const Size(412, 915);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore(owned: false)),
      sharedPreferencesProvider.overrideWithValue(null),
      isArabicProvider.overrideWithValue(locale.languageCode == 'ar'),
      unreadNotificationCountProvider.overrideWith((ref) => Stream.value(0)),
    ]);
    addTearDown(container.dispose);

    container.read(searchFilterProvider.notifier).state = filter;
    // The config carries the zone and the currency every date and price here is
    // rendered through.
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
          home: const SearchScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();

    return container;
  }

  CatalogueListing listing() => CatalogueListing.fromJson(const {
        'vehicleId': 'vehicle-1',
        'make': 'Toyota',
        'model': 'Corolla',
        'year': 2024,
        'transmission': 'Automatic',
        'fuelType': 'Petrol',
        'seats': 5,
        'dailyRate': {'amount': 30, 'currency': 'JOD'},
        'isDeliveryAvailable': false,
        'gallery': {'dealerId': 'dealer-1', 'businessName': 'Al-Nadeem Rentals', 'reviewCount': 0},
      });

  testWidgets('the pickup location is "any city" until one of the platform\'s is chosen',
      (tester) async {
    final api = FakeApi()..cityLookups = [amman, irbid];
    final container = await pumpHome(tester, api: api);

    expect(find.text(en.searchPickupLocation), findsOneWidget);
    expect(find.text(en.searchAnyCity), findsOneWidget);

    await tester.tap(find.text(en.searchPickupLocation));
    await tester.pumpAndSettle();

    // The lookup's cities, in the lookup's own order.
    expect(
      tester.getTopLeft(find.text('Amman')).dy,
      lessThan(tester.getTopLeft(find.text('Irbid')).dy),
    );
    await tester.tap(find.text('Irbid'));
    await tester.pumpAndSettle();

    expect(container.read(searchFilterProvider).cityId, 'city-irbid');
    expect(find.text('Irbid'), findsOneWidget);
    expect(find.text(en.searchAnyCity), findsNothing);
  });

  testWidgets('closing the city sheet without choosing changes nothing', (tester) async {
    final api = FakeApi()..cityLookups = [amman, irbid];
    final container = await pumpHome(
      tester,
      api: api,
      filter: const SearchFilter(cityId: 'city-amman'),
    );

    await tester.tap(find.text(en.searchPickupLocation));
    await tester.pumpAndSettle();
    await tester.tapAt(const Offset(200, 40)); // The scrim, above the sheet.
    await tester.pumpAndSettle();

    expect(container.read(searchFilterProvider).cityId, 'city-amman');
  });

  testWidgets('the rental period states the chosen dates, pickup first', (tester) async {
    final pickup = DateTime.utc(2026, 10, 3, 7);
    final dropoff = DateTime.utc(2026, 10, 6, 7);
    final container = await pumpHome(
      tester,
      api: FakeApi(),
      filter: SearchFilter(pickupAt: pickup, returnAt: dropoff),
    );
    final formats = container.read(formatsProvider)!;

    expect(
      find.text(en.searchPeriodValue(formats.dateTime(pickup), formats.dateTime(dropoff))),
      findsOneWidget,
    );
    expect(find.text(en.searchAnyDates), findsNothing);
  });

  testWidgets('the categories are the active types that have cars, in the platform\'s order',
      (tester) async {
    final api = FakeApi()
      ..carTypeLookups = [sedan, suv, van]
      ..facets = const CatalogueFacets(seats: [5], carTypeIds: {'type-van', 'type-sedan'});
    final container = await pumpHome(tester, api: api);

    expect(find.widgetWithText(KhadraChoiceChip, en.searchAllCarTypes), findsOneWidget);
    expect(find.widgetWithText(KhadraChoiceChip, 'Sedan'), findsOneWidget);
    expect(find.widgetWithText(KhadraChoiceChip, 'Van'), findsOneWidget);
    // Listed as a category, with no car behind it: not offered.
    expect(find.text('SUV'), findsNothing);
    expect(
      tester.getTopLeft(find.text('Sedan')).dx,
      lessThan(tester.getTopLeft(find.text('Van')).dx),
    );

    await tester.tap(find.text('Van'));
    await tester.pumpAndSettle();
    expect(container.read(searchFilterProvider).carTypeId, 'type-van');

    // Tapping the chosen one again takes the choice back.
    await tester.tap(find.text('Van'));
    await tester.pumpAndSettle();
    expect(container.read(searchFilterProvider).carTypeId, isNull);
  });

  testWidgets('a chosen category stays offered after its last car has gone', (tester) async {
    // Otherwise the customer is filtered by a chip they can no longer see, and
    // cannot take the choice back.
    final api = FakeApi()
      ..carTypeLookups = [sedan, suv]
      ..facets = const CatalogueFacets(seats: [], carTypeIds: {'type-sedan'});
    await pumpHome(tester, api: api, filter: const SearchFilter(carTypeId: 'type-suv'));

    expect(find.widgetWithText(KhadraChoiceChip, 'SUV'), findsOneWidget);
  });

  testWidgets('without facets every active type is offered and no seat choice is invented',
      (tester) async {
    // What an older API without the endpoint answers.
    final api = FakeApi()
      ..carTypeLookups = [sedan, suv, van]
      ..facetsFailure = const ApiFailure(kind: ApiFailureKind.notFound, statusCode: 404);
    await pumpHome(tester, api: api);

    expect(find.text('Sedan'), findsOneWidget);
    expect(find.text('SUV'), findsOneWidget);
    expect(find.text('Van'), findsOneWidget);

    await tester.tap(find.text(en.searchFilters));
    await tester.pumpAndSettle();
    expect(find.text(en.searchSeats), findsNothing);
  });

  testWidgets('the seat choices are the counts the catalogue has, and no others',
      (tester) async {
    final api = FakeApi()..facets = const CatalogueFacets(seats: [4, 7], carTypeIds: <String>{});
    await pumpHome(tester, api: api);

    await tester.tap(find.text(en.searchFilters));
    await tester.pumpAndSettle();

    expect(find.text(en.searchMinimumSeats(4)), findsOneWidget);
    expect(find.text(en.searchMinimumSeats(7)), findsOneWidget);
    // The counts the old typed-in list offered and this catalogue does not have.
    expect(find.text(en.searchMinimumSeats(2)), findsNothing);
    expect(find.text(en.searchMinimumSeats(5)), findsNothing);
  });

  testWidgets('the count is the server\'s total over the whole search', (tester) async {
    final api = FakeApi()
      ..searchResult = Paged(items: [listing()], page: 1, pageSize: 20, totalCount: 37);
    await pumpHome(tester, api: api);

    // Thirty-seven, not the one card loaded — and without dates, no "available".
    expect(find.text(en.searchResults(37)), findsOneWidget);
    expect(find.text(en.searchResultsAvailable(37)), findsNothing);
  });

  testWidgets('with dates, the count is of cars available across them', (tester) async {
    final api = FakeApi()
      ..searchResult = Paged(items: [listing()], page: 1, pageSize: 20, totalCount: 37);
    await pumpHome(
      tester,
      api: api,
      filter: SearchFilter(
        pickupAt: DateTime.utc(2026, 10, 3, 7),
        returnAt: DateTime.utc(2026, 10, 6, 7),
      ),
    );

    expect(find.text(en.searchResultsAvailable(37)), findsOneWidget);
  });

  testWidgets('the Filters button counts only what is in its sheet', (tester) async {
    final container = await pumpHome(
      tester,
      api: FakeApi(),
      filter: SearchFilter(
        cityId: 'city-amman',
        carTypeId: 'type-sedan',
        pickupAt: DateTime.utc(2026, 10, 3, 7),
        returnAt: DateTime.utc(2026, 10, 6, 7),
      ),
    );

    // The city, the category and the dates are already in plain view on Home.
    expect(find.text(en.searchFilters), findsOneWidget);

    container
        .read(searchFilterProvider.notifier)
        .update((filter) => filter.copyWith(transmission: 'Automatic'));
    await tester.pumpAndSettle();

    expect(find.text(en.searchFiltersApplied(1)), findsOneWidget);
  });

  testWidgets('in Arabic, the city and the categories are named in Arabic', (tester) async {
    final api = FakeApi()
      ..cityLookups = [amman]
      ..carTypeLookups = [sedan]
      ..facets = const CatalogueFacets(seats: [], carTypeIds: {'type-sedan'});
    await pumpHome(
      tester,
      api: api,
      locale: const Locale('ar'),
      filter: const SearchFilter(cityId: 'city-amman'),
    );
    final ar = lookupAppLocalizations(const Locale('ar'));

    expect(find.text('عمّان'), findsOneWidget);
    expect(find.widgetWithText(KhadraChoiceChip, 'سيدان'), findsOneWidget);
    expect(find.widgetWithText(KhadraChoiceChip, ar.searchAllCarTypes), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  test('clearing the filter sheet keeps everything Home shows', () {
    final pickup = DateTime.utc(2026, 10, 3, 7);
    final dropoff = DateTime.utc(2026, 10, 6, 7);
    final filter = SearchFilter(
      text: 'corolla',
      cityId: 'city-amman',
      carTypeId: 'type-sedan',
      transmission: 'Automatic',
      minSeats: 5,
      minDailyRate: 10,
      maxDailyRate: 50,
      deliveryOnly: true,
      pickupAt: pickup,
      returnAt: dropoff,
    );

    final cleared = filter.cleared();

    expect(
      cleared,
      SearchFilter(
        text: 'corolla',
        cityId: 'city-amman',
        carTypeId: 'type-sedan',
        pickupAt: pickup,
        returnAt: dropoff,
      ),
    );
    expect(cleared.sheetCount, 0);
    expect(filter.sheetCount, 5);
  });
}
