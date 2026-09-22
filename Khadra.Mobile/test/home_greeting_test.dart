import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/format/greeting.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/features/catalogue/search_providers.dart';
import 'package:khadra_mobile/features/catalogue/search_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// What tells a signed-in customer's Home apart from a guest's.
///
/// Two lines, and both of them have to be earned: the name comes from the
/// session the server established, and a guest gets nothing rather than a
/// greeting addressed to an invented person.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  final en = lookupAppLocalizations(const Locale('en'));
  final ar = lookupAppLocalizations(const Locale('ar'));

  Future<void> pumpHome(
    WidgetTester tester, {
    required bool signedIn,
    String name = 'Layla Odeh',
    Locale locale = const Locale('en'),
  }) async {
    tester.view.physicalSize = const Size(375, 812);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final api = FakeApi()..signInAs = FakeApi.fakeUser(name: name);
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore(owned: false)),
      sharedPreferencesProvider.overrideWithValue(null),
      isArabicProvider.overrideWithValue(locale.languageCode == 'ar'),
    ]);
    addTearDown(container.dispose);

    if (signedIn) {
      await container.read(sessionProvider.notifier).signIn('layla@example.jo', 'x');
    }
    container.read(searchFilterProvider.notifier).state = const SearchFilter();
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
  }

  /// The greeting this run of the test should be looking for — the clock is the
  /// machine's, and a test that pinned one bucket would fail after five o'clock.
  String expected(AppLocalizations l10n, String name) =>
      switch (greetingBucket(DateTime.now())) {
        DayPart.morning => l10n.homeGreetingMorning(name),
        DayPart.afternoon => l10n.homeGreetingAfternoon(name),
        DayPart.evening => l10n.homeGreetingEvening(name),
      };

  testWidgets('greets a signed-in customer by the name the server holds',
      (tester) async {
    await pumpHome(tester, signedIn: true);

    expect(find.text('${expected(en, 'Layla')} 👋'), findsOneWidget);
    expect(find.text(en.homeGreetingPrompt), findsOneWidget);
  });

  testWidgets('in Arabic, with the prompt in the register the app speaks',
      (tester) async {
    await pumpHome(tester, signedIn: true, name: 'ليلى عودة', locale: const Locale('ar'));

    expect(find.text('${expected(ar, 'ليلى')} 👋'), findsOneWidget);
    expect(find.text(ar.homeGreetingPrompt), findsOneWidget);
  });

  testWidgets('never shortens a compound Arabic name to its first word',
      (tester) async {
    // "صباح الخير، عبد" addresses somebody as "servant". The whole name is one
    // name, and the greeting has to know it.
    await pumpHome(
      tester,
      signedIn: true,
      name: 'عبد الله الخطيب',
      locale: const Locale('ar'),
    );

    expect(find.text('${expected(ar, 'عبد الله')} 👋'), findsOneWidget);
  });

  testWidgets('says nothing at all to a guest', (tester) async {
    await pumpHome(tester, signedIn: false);

    expect(find.text(en.homeGreetingPrompt), findsNothing);
    expect(find.textContaining('Good ', findRichText: true), findsNothing);
    // The screen a guest came for is still there, unchanged.
    expect(find.text(en.searchPickupLocation), findsOneWidget);
  });
}
