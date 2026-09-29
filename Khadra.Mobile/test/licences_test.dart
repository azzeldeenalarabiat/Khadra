import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:khadra_mobile/core/fonts/font_licences.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// The app's licences (pre-launch item 201; owner, 2026-09-29): a row under About on the Profile tab —
/// "Licences" / «التراخيص» — opens Flutter's licence page, where the fonts' licences registered at start stand
/// beside every package's. A guest can open it: About is not an account's, and neither is reading a licence.
///
/// A test build carries no package list (Flutter collects that when it builds an app), so only the fonts are
/// here — which is exactly the half this app is responsible for.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  setUp(() {
    // Each test starts from the registry and the asset cache a launch starts from.
    LicenseRegistry.reset();
    rootBundle.clear();
    registerFontLicences();
  });

  Future<GoRouter> pumpGuest(WidgetTester tester, Locale locale) async {
    // A phone: Flutter's licence page lists the packages on one screen and opens each on the next, as it does
    // on a customer's phone (from 840 wide it shows both side by side).
    tester.view.physicalSize = const Size(400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    SharedPreferences.setMockInitialValues({'khadra.entry_chosen': true});
    final preferences = await SharedPreferences.getInstance();

    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(FakeApi()),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(preferences),
      installedAppVersionProvider.overrideWithValue('1.3.0+4'),
    ]);
    addTearDown(container.dispose);
    await container.read(sessionProvider.notifier).restore();

    final router = container.read(routerProvider);
    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(
          locale: locale,
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

    router.go(Routes.profile);
    await tester.pumpAndSettle();
    return router;
  }

  /// Real time for [until] to appear: the page reads the registry, and the fonts' texts are assets loaded
  /// outside the test's fake clock, so a spinner turns until they arrive.
  Future<void> loaded(WidgetTester tester, Finder until) async {
    for (var attempt = 0; attempt < 40 && until.evaluate().isEmpty; attempt++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 50)));
      await tester.pump(const Duration(milliseconds: 50));
    }
    await tester.pumpAndSettle();
  }

  Future<void> openLicences(WidgetTester tester, String row) async {
    await tester.ensureVisible(find.text(row));
    await tester.pumpAndSettle();
    await tester.tap(find.text(row));
    await tester.pump();
    await loaded(tester, find.text('Noto Kufi Arabic'));
  }

  testWidgets('a guest opens the licences from About, in English, and finds both fonts\' licences', (tester) async {
    await pumpGuest(tester, const Locale('en'));

    await openLicences(tester, 'Licences');

    expect(find.byType(LicensePage), findsOneWidget);
    // Material's British strings on an English page, as the row that opened it is spelled.
    expect(find.text('Licences'), findsWidgets);
    expect(find.text('Khadra'), findsOneWidget);
    expect(find.text('1.3.0+4'), findsOneWidget);
    expect(find.text('Manrope'), findsOneWidget);
    expect(find.text('Noto Kufi Arabic'), findsOneWidget);

    // Each opens its own licence text.
    await tester.tap(find.text('Manrope'));
    await tester.pump();
    await loaded(tester, find.textContaining('SIL Open Font License, Version 1.1'));
    expect(find.textContaining('SIL Open Font License, Version 1.1'), findsWidgets);
    expect(find.textContaining('The Manrope Project Authors'), findsWidgets);
  });

  testWidgets('and in Arabic, «التراخيص» opens the same page in Arabic', (tester) async {
    await pumpGuest(tester, const Locale('ar'));

    await openLicences(tester, 'التراخيص');

    expect(find.byType(LicensePage), findsOneWidget);
    expect(find.text('خضرا'), findsOneWidget);
    // The page's own title, in Material's Arabic, beside the row's.
    expect(find.descendant(of: find.byType(AppBar), matching: find.text('التراخيص')), findsOneWidget);
    expect(find.text('Manrope'), findsOneWidget);
    expect(find.text('Noto Kufi Arabic'), findsOneWidget);
    expect(Directionality.of(tester.element(find.text('Manrope'))), TextDirection.rtl);
  });
}
