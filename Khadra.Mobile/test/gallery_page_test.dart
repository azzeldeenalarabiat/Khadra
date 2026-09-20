import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/format/formats.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/features/catalogue/gallery_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// The rental office's page.
///
/// Two things it has to get right, and they pull in opposite directions. What the
/// PLATFORM knows — the hours a pickup is held to, whether the office delivers and
/// for how much, where it is, its rating — is always on the page and the office
/// cannot take it off. What the office WROTE is on the page only where it wrote
/// something and chose to show it, under a heading saying whose words these are.
///
/// And the page must not become a way to ask what an office is keeping back: a
/// section the server left out renders NOTHING — no heading, no empty card, no
/// "not provided" — so hidden and never-written are the same silence.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  const amman =
      Lookup(id: 'city-amman', nameEn: 'Amman', nameAr: 'عمّان', isActive: true);
  final en = lookupAppLocalizations(const Locale('en'));

  /// Every day of the week open, so the collapsed card always has today.
  List<Map<String, dynamic>> wholeWeek() => [
        for (final day in const [
          'Monday',
          'Tuesday',
          'Wednesday',
          'Thursday',
          'Friday',
          'Saturday',
          'Sunday',
        ])
          {'day': day, 'isClosed': false, 'opens': '08:00', 'closes': '20:00'},
      ];

  /// The JSON shape `GET /api/v1/galleries/{id}` answers with, so these tests fail
  /// if the contract moves rather than agreeing with a hand-built object.
  PublicGalleryPage page({
    Map<String, dynamic>? sections,
    Map<String, dynamic>? address =
        const {'area': 'Abdali', 'street': 'Street 12'},
    bool delivers = true,
    List<Map<String, dynamic>>? hours,
    num? rating = 4.5,
    int reviews = 12,
  }) =>
      PublicGalleryPage.fromJson({
        'dealerId': 'dealer-1',
        'businessName': 'Zahra Rentals',
        'cityId': amman.id,
        'address': address,
        'latitude': 31.9539,
        'longitude': 35.9106,
        'logoUrl': null,
        'coverUrl': null,
        'operatingHours': hours ?? wholeWeek(),
        'delivery': {
          'isEnabled': delivers,
          'radiusKm': 15,
          'fee': delivers ? {'amount': 5, 'currency': 'JOD'} : null,
        },
        'averageRating': rating,
        'reviewCount': reviews,
        'sections': sections ?? const <String, dynamic>{},
      });

  Future<void> pumpPage(
    WidgetTester tester, {
    required FakeApi api,
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
    ]);
    addTearDown(container.dispose);

    // The config carries the zone every opening hour is judged in.
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
          home: const GalleryScreen(dealerId: 'dealer-1'),
        ),
      ),
    );

    // Not `pumpAndSettle`: the location card holds a map, and a map in a test asks
    // the network for tiles it will never get. The page's own futures are answered
    // from memory by the frame after the first.
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 400));
  }

  /// A SECTION HEADING, as opposed to the jump button in the action row that
  /// carries the same word. Both are meant to read alike; only one is the section.
  Finder heading(String title) => find.descendant(
        of: find.byType(KhadraSectionTitle),
        matching: find.text(title),
      );

  /// Scrolls until [finder] is on screen, or gives up quietly — a section that is
  /// absent is exactly what several of these tests are asserting.
  Future<void> scrollTo(WidgetTester tester, Finder finder) async {
    final list = find.byType(Scrollable).first;
    for (var attempt = 0; attempt < 12 && finder.evaluate().isEmpty; attempt++) {
      await tester.drag(list, const Offset(0, -400));
      await tester.pump();
    }
  }

  testWidgets('what the office wrote appears under its own name', (tester) async {
    final api = FakeApi()
      ..cityLookups = const [amman]
      ..galleryPage = page(sections: const {
        'about': 'A family office in Abdali since 1998.',
        'rentalConditions': 'No smoking in any car.',
        'insurance': 'Comprehensive cover.',
        'pickupInstructions': 'Bring your original licence to the counter.',
        'deliveryNotes': 'We deliver between 9am and 6pm.',
        'customerNotes': 'Ask for Hani.',
      });

    await pumpPage(tester, api: api);

    expect(find.text(en.galleryAbout), findsOneWidget);
    expect(find.text('A family office in Abdali since 1998.'), findsOneWidget);

    await scrollTo(tester, heading(en.galleryPickupInstructions));
    expect(
        find.text('Bring your original licence to the counter.'), findsOneWidget);

    await scrollTo(tester, heading(en.galleryFromTheOffice));
    // Said once, above the three sections it covers: these are the office's words,
    // not the platform's.
    expect(find.text(en.galleryOfficeOwnWords), findsOneWidget);
    expect(find.text(en.galleryRentalConditions), findsOneWidget);
    expect(find.text('No smoking in any car.'), findsOneWidget);
    expect(find.text(en.galleryInsurance), findsOneWidget);
    expect(find.text(en.galleryNotes), findsOneWidget);
  });

  testWidgets('a section the server left out leaves no trace at all',
      (tester) async {
    final api = FakeApi()
      ..cityLookups = const [amman]
      // Hidden and never-written arrive identically: as nothing.
      ..galleryPage = page(sections: const {'about': 'A family office in Abdali.'});

    await pumpPage(tester, api: api);
    await scrollTo(tester, heading(en.galleryCars));

    for (final absent in [
      en.galleryPickupInstructions,
      en.galleryFromTheOffice,
      en.galleryOfficeOwnWords,
      en.galleryRentalConditions,
      en.galleryInsurance,
      en.galleryNotes,
    ]) {
      expect(find.text(absent), findsNothing, reason: absent);
    }

    // And the platform's own facts are all still there.
    expect(heading(en.galleryOpeningHours), findsOneWidget);
    expect(heading(en.galleryDelivery), findsOneWidget);
    expect(heading(en.galleryLocation), findsOneWidget);
    expect(heading(en.galleryCars), findsOneWidget);
  });

  testWidgets('an office that does not deliver shows no delivery notes',
      (tester) async {
    final api = FakeApi()
      ..cityLookups = const [amman]
      // The server never sends notes about a service that is off; the page must
      // not have a second opinion about that.
      ..galleryPage = page(delivers: false, sections: const {
        'deliveryNotes': null,
        'pickupInstructions': 'Collect from the counter.',
      });

    await pumpPage(tester, api: api);

    expect(find.text(en.galleryDeliveryNotOffered), findsOneWidget);
    expect(find.textContaining('deliver between'), findsNothing);
    expect(find.text('Collect from the counter.'), findsOneWidget);
  });

  testWidgets('the address is on the page when the office recorded one',
      (tester) async {
    final api = FakeApi()
      ..cityLookups = const [amman]
      ..galleryPage = page();

    await pumpPage(tester, api: api);

    // The area reads beside the name; the street belongs to the location card with
    // the map, where "exactly where" is the question being asked.
    expect(find.textContaining('Abdali'), findsWidgets);
    expect(find.textContaining('Street 12'), findsOneWidget);
  });

  testWidgets('an office with no address recorded shows its city alone',
      (tester) async {
    final api = FakeApi()
      ..cityLookups = const [amman]
      ..galleryPage = page(address: null);

    await pumpPage(tester, api: api);

    // Isolated, because beside an Arabic city a Latin street reorders into
    // nonsense — so the city arrives fenced even when it is alone.
    expect(find.text(Formats.isolate(amman.nameEn)), findsOneWidget);
    expect(find.textContaining('Abdali'), findsNothing);
  });

  testWidgets('a long About folds, and opens on Show more', (tester) async {
    final api = FakeApi()
      ..cityLookups = const [amman]
      ..galleryPage = page(sections: {
        'about': List.filled(60, 'Cars for every road in Jordan.').join(' '),
      });

    await pumpPage(tester, api: api);

    expect(find.text(en.actionShowMore), findsOneWidget);
    await tester.tap(find.text(en.actionShowMore));
    await tester.pump();

    expect(find.text(en.actionShowLess), findsOneWidget);
    expect(find.text(en.actionShowMore), findsNothing);
  });

  testWidgets('a short About is not offered a fold that would open nothing',
      (tester) async {
    final api = FakeApi()
      ..cityLookups = const [amman]
      ..galleryPage = page(sections: const {'about': 'Family run since 1998.'});

    await pumpPage(tester, api: api);

    expect(find.text('Family run since 1998.'), findsOneWidget);
    expect(find.text(en.actionShowMore), findsNothing);
  });

  testWidgets('opening hours show today, and the whole week on request',
      (tester) async {
    final api = FakeApi()
      ..cityLookups = const [amman]
      ..galleryPage = page();

    await pumpPage(tester, api: api);
    await scrollTo(tester, heading(en.galleryOpeningHours));

    // One row collapsed — today's, in Amman, whichever day this test runs on.
    expect(find.textContaining('Open today'), findsOneWidget);

    await tester.tap(find.text(en.galleryAllWeek));
    await tester.pump();

    expect(find.textContaining('Open today'), findsNothing);
    expect(find.text('Monday'), findsOneWidget);
    expect(find.text('Sunday'), findsOneWidget);
    expect(find.text(en.galleryTodayOnly), findsOneWidget);
  });

  testWidgets('a week that does not name today falls back to the whole week',
      (tester) async {
    // A schedule the platform could return after a day is added or removed: the
    // page shows what it was given rather than inventing a row for today.
    final api = FakeApi()
      ..cityLookups = const [amman]
      ..galleryPage = page(hours: const [
        {'day': 'Nameday', 'isClosed': false, 'opens': '08:00', 'closes': '20:00'},
      ]);

    await pumpPage(tester, api: api);
    await scrollTo(tester, heading(en.galleryOpeningHours));

    expect(find.textContaining('Open today'), findsNothing);
    // An unrecognised name reads as itself rather than vanishing.
    expect(find.text('Nameday'), findsOneWidget);
  });

  testWidgets('the newest three reviews are shown, and no more', (tester) async {
    final api = FakeApi()
      ..cityLookups = const [amman]
      ..galleryPage = page(reviews: 9)
      ..galleryReviewPage = Paged(
        items: [
          for (var index = 0; index < 9; index++)
            GalleryReview.fromJson({
              'reviewId': 'review-$index',
              'rating': 5,
              'comment': 'Comment number $index.',
              'isHidden': false,
              'createdAt': DateTime.utc(2026, 9, 10 - index).toIso8601String(),
            }),
        ],
        page: 1,
        pageSize: 20,
        totalCount: 9,
      );

    await pumpPage(tester, api: api);
    await scrollTo(tester, heading(en.galleryCars));

    expect(find.text('Comment number 0.'), findsOneWidget);
    expect(find.text('Comment number 2.'), findsOneWidget);
    expect(find.text('Comment number 3.'), findsNothing);
    expect(find.text(en.actionSeeAll), findsOneWidget);
  });

  testWidgets('an office nobody has rated says so, and offers no See all',
      (tester) async {
    final api = FakeApi()
      ..cityLookups = const [amman]
      ..galleryPage = page(rating: null, reviews: 0);

    await pumpPage(tester, api: api);

    expect(find.text(en.galleryNotRatedYet), findsOneWidget);

    await scrollTo(tester, heading(en.galleryCars));
    expect(find.text(en.reviewsEmpty), findsOneWidget);
    expect(find.text(en.actionSeeAll), findsNothing);
  });
}
