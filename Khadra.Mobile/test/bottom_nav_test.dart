import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/features/auth/account_required.dart';
import 'package:khadra_mobile/features/bookings/bookings_screen.dart';
import 'package:khadra_mobile/features/catalogue/search_screen.dart';
import 'package:khadra_mobile/features/notifications/notification_providers.dart';
import 'package:khadra_mobile/features/shortlist/shortlist_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// The bottom bar, once Saved joined it.
///
/// Five destinations where there were four, which is the edit that breaks a bar:
/// `NavigationBar` gives each an equal share of the width, so 375 logical pixels
/// buys 75 each, and "التنبيهات" in Noto Kufi is not the same width as "Alerts"
/// in Manrope. A screenshot in one language at one text size would not have
/// shown it, so the layout is asked directly.
///
/// And the Back button, which on a tab has nothing under it but the app itself.
void main() {
  setUpAll(() async {
    tz_data.initializeTimeZones();
    TestWidgetsFlutterBinding.ensureInitialized();

    // Without the real faces every glyph is a same-sized box in Flutter's test
    // font, and a width measured here would say nothing about either language.
    for (final family in <String, List<String>>{
      'Manrope': ['Manrope-400.ttf', 'Manrope-500.ttf', 'Manrope-600.ttf', 'Manrope-700.ttf'],
      'Noto Kufi Arabic': ['NotoKufiArabic-400.ttf', 'NotoKufiArabic-500.ttf', 'NotoKufiArabic-700.ttf'],
    }.entries) {
      final loader = FontLoader(family.key);
      for (final file in family.value) {
        loader.addFont(Future<ByteData>.value(
            ByteData.sublistView(File('assets/fonts/$file').readAsBytesSync())));
      }
      await loader.load();
    }
  });

  /// The narrowest phone this app is built for, which is where five tabs bite.
  const narrowPhone = Size(375, 812);

  Future<(GoRouter, ProviderContainer)> pump(
    WidgetTester tester, {
    required bool signedIn,
    Locale locale = const Locale('en'),
    Size size = narrowPhone,
    double textScale = 1,
    List<SavedVehicle> saved = const [],
  }) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    SharedPreferences.setMockInitialValues({'khadra.entry_chosen': true});
    final preferences = await SharedPreferences.getInstance();

    final api = FakeApi()..savedCars = saved;
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(preferences),
      isArabicProvider.overrideWithValue(locale.languageCode == 'ar'),
      unreadNotificationCountProvider.overrideWith((ref) => Stream.value(0)),
    ]);
    addTearDown(container.dispose);

    if (signedIn) {
      await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
    } else {
      await container.read(sessionProvider.notifier).restore();
    }
    await container.read(appConfigProvider.future);

    final router = container.read(routerProvider);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(
          locale: locale,
          theme: KhadraTheme.light(),
          routerConfig: router,
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          builder: (context, child) => MediaQuery.withClampedTextScaling(
            minScaleFactor: textScale,
            maxScaleFactor: textScale,
            child: child!,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    return (router, container);
  }

  /// Whether [label] arrived WHOLE: laid out against the same words in the same
  /// style with nowhere to wrap. A label squeezed into less width than it needs
  /// wraps and is taller, or is ellipsised and is narrower — the same measure
  /// `responsive_layout_test.dart` uses, and the reason a bar that "looks fine"
  /// in a screenshot is not evidence.
  bool whole(WidgetTester tester, String label) {
    final finder = find.descendant(
      of: find.byType(NavigationBar),
      matching: find.text(label),
    );
    expect(finder, findsOneWidget, reason: 'the bar does not show "$label"');

    final context = tester.element(finder);
    final painter = TextPainter(
      text: TextSpan(
        text: label,
        style: DefaultTextStyle.of(context).style.merge(tester.widget<Text>(finder).style),
      ),
      textDirection: Directionality.of(context),
      textScaler: MediaQuery.textScalerOf(context),
      maxLines: 1,
    )..layout();
    final oneLine = painter.size;
    painter.dispose();

    final drawn = tester.getSize(finder);
    return drawn.height <= oneLine.height + 0.5 && drawn.width >= oneLine.width - 0.5;
  }

  void expectNothingTruncated(WidgetTester tester, AppLocalizations l10n) {
    for (final name in [
      l10n.navHome,
      l10n.navBookings,
      l10n.navNotifications,
      l10n.navSaved,
      l10n.navProfile,
    ]) {
      expect(whole(tester, name), isTrue,
          reason: '"$name" is being given less width than the word needs');
    }
    // An overflow paints a stripe rather than throwing on every frame, so ask.
    expect(tester.takeException(), isNull);
  }

  group('five destinations fit', () {
    testWidgets('on a 375 phone in English', (tester) async {
      await pump(tester, signedIn: true);
      expectNothingTruncated(tester, lookupAppLocalizations(const Locale('en')));
    });

    testWidgets('and in Arabic, where the words are longer', (tester) async {
      await pump(tester, signedIn: true, locale: const Locale('ar'));
      expectNothingTruncated(tester, lookupAppLocalizations(const Locale('ar')));
    });

    testWidgets('and on the narrowest Android in common use, 360 wide',
        (tester) async {
      await pump(
        tester,
        signedIn: true,
        locale: const Locale('ar'),
        size: const Size(360, 640),
      );
      expectNothingTruncated(tester, lookupAppLocalizations(const Locale('ar')));
    });
  });

  testWidgets('the Saved tab opens the saved cars screen', (tester) async {
    final (router, _) = await pump(
      tester,
      signedIn: true,
      saved: [
        SavedVehicle.fromJson(const {
          'vehicleId': 'vehicle-1',
          'savedAt': '2026-09-01T09:00:00Z',
          'listing': {
            'vehicleId': 'vehicle-1',
            'make': 'Toyota',
            'model': 'Corolla',
            'year': 2022,
            'transmission': 'Automatic',
            'seats': 5,
            'carType': {'id': 'type-sedan', 'nameEn': 'Sedan', 'nameAr': 'سيدان'},
            'city': {'id': 'city-amman', 'nameEn': 'Amman', 'nameAr': 'عمّان'},
            'dailyRate': {'amount': 25, 'currency': 'JOD'},
            'isDeliveryAvailable': false,
            'gallery': {
              'dealerId': 'dealer-1',
              'businessName': 'Petra Rentals',
              'averageRating': 4.5,
              'reviewCount': 12,
            },
          },
        }),
      ],
    );

    await tester.tap(find.text(lookupAppLocalizations(const Locale('en')).navSaved));
    await tester.pumpAndSettle();

    expect(find.byType(ShortlistScreen), findsOneWidget);
    expect(router.routerDelegate.currentConfiguration.uri.path, Routes.saved);
    // The rows are the server's, through the same provider My Account uses.
    expect(find.text('Toyota Corolla'), findsOneWidget);
  });

  testWidgets('a guest sees the account panel on the Saved tab, not a sign-in form',
      (tester) async {
    // The 2026-09-12 rule: a TAB is never redirected. Being thrown into a form
    // by the bar leaves the shell, and the bottom bar with it.
    await pump(tester, signedIn: false);

    await tester.tap(find.text(lookupAppLocalizations(const Locale('en')).navSaved));
    await tester.pumpAndSettle();

    expect(find.byType(AccountRequired), findsOneWidget);
    expect(find.byType(NavigationBar), findsOneWidget);
  });

  group('the Back button on a tab', () {
    /// Android's Back, as the framework delivers it.
    Future<void> pressBack(WidgetTester tester) async {
      await tester.binding.handlePopRoute();
      await tester.pumpAndSettle();
    }

    testWidgets('returns to Home from another tab rather than leaving',
        (tester) async {
      final (router, _) = await pump(tester, signedIn: true);
      await tester.tap(find.text(lookupAppLocalizations(const Locale('en')).navBookings));
      await tester.pumpAndSettle();
      expect(find.byType(BookingsScreen), findsOneWidget);

      await pressBack(tester);

      expect(find.byType(SearchScreen), findsOneWidget);
      expect(router.routerDelegate.currentConfiguration.uri.path, Routes.search);
    });

    testWidgets('asks once on Home before it will close the app', (tester) async {
      final exits = <MethodCall>[];
      tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
        SystemChannels.platform,
        (call) async {
          if (call.method == 'SystemNavigator.pop') exits.add(call);
          return null;
        },
      );
      addTearDown(() => tester.binding.defaultBinaryMessenger
          .setMockMethodCallHandler(SystemChannels.platform, null));

      await pump(tester, signedIn: true, locale: const Locale('ar'));

      await pressBack(tester);
      expect(find.text(lookupAppLocalizations(const Locale('ar')).backAgainToExit),
          findsOneWidget);
      expect(exits, isEmpty, reason: 'the first press must not close the app');

      await pressBack(tester);
      expect(exits, hasLength(1));
    });
  });
}
