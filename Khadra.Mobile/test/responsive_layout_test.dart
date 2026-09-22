import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/features/catalogue/search_providers.dart';
import 'package:khadra_mobile/features/catalogue/search_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:khadra_mobile/l10n/app_localizations_ar.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// Labels that fit in English and do not fit in Arabic.
///
/// The controls above the results were two `Expanded` halves — each gets exactly
/// half the width whatever is written in it. "1 filter" fits in half a 375-wide
/// phone; "عامل تصفية واحد" does not, so the one label a customer most needs to
/// read, the one telling them a filter is hiding results, arrived as "عامل تص…".
/// Turning the text size up does the same thing to English.
///
/// A screenshot would have shown it. Nobody takes screenshots in the other
/// language at the other text size, so the layout is asked directly instead: was
/// the label truncated, yes or no.
void main() {
  setUpAll(() async {
    tz_data.initializeTimeZones();
    TestWidgetsFlutterBinding.ensureInitialized();

    // Without the real faces every glyph is a same-sized box in Flutter's test
    // font, and a width measured here would say nothing about either language --
    // least of all about Noto Kufi Arabic, whose deeper line box is half the point.
    for (final family in <String, List<String>>{
      'Manrope': ['Manrope-400.ttf', 'Manrope-500.ttf', 'Manrope-600.ttf'],
      'Noto Kufi Arabic': ['NotoKufiArabic-400.ttf', 'NotoKufiArabic-500.ttf'],
    }.entries) {
      final loader = FontLoader(family.key);
      for (final file in family.value) {
        loader.addFont(Future<ByteData>.value(ByteData.sublistView(
            File('assets/fonts/$file').readAsBytesSync())));
      }
      await loader.load();
    }
  });

  /// The narrowest phone this app is built for, which is where every one of these
  /// bites first.
  const phone = Size(375, 812);

  Future<ProviderContainer> pumpSearch(
    WidgetTester tester, {
    required Locale locale,
    double textScale = 1,
    SearchFilter filter = const SearchFilter(),
  }) async {
    tester.view.physicalSize = phone;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(FakeApi()),
      sessionStoreProvider.overrideWithValue(FakeSessionStore(owned: false)),
      sharedPreferencesProvider.overrideWithValue(null),
      searchFilterProvider.overrideWith((ref) => filter),
    ]);
    addTearDown(container.dispose);

    // The config carries the currency and the time zone every price and date on
    // this screen is rendered through.
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
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context)
                .copyWith(textScaler: TextScaler.linear(textScale)),
            child: child ?? const SizedBox.shrink(),
          ),
          home: const SearchScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();

    return container;
  }

  /// Whether [label] arrived on ONE line: measured against the same words, in the
  /// style they were set in and at the same text size, laid out with nowhere to
  /// wrap. A label squeezed into less width than it needs breaks onto a second
  /// line and is taller than that — or is cut, which is shorter than the words.
  bool whole(WidgetTester tester, String label) {
    final text = find.text(label);
    final context = tester.element(text);
    final painter = TextPainter(
      text: TextSpan(
        text: label,
        style: DefaultTextStyle.of(context).style.merge(tester.widget<Text>(text).style),
      ),
      textDirection: Directionality.of(context),
      textScaler: MediaQuery.textScalerOf(context),
      maxLines: 1,
    )..layout();
    final oneLine = painter.size;
    painter.dispose();

    final drawn = tester.getSize(text);
    return drawn.height <= oneLine.height + 0.5 && drawn.width >= oneLine.width - 0.5;
  }

  final ar = AppLocalizationsAr();

  testWidgets('the Arabic filter label is whole on a 375 phone', (tester) async {
    // One filter, which is the longest of the Arabic plural forms and the one a
    // customer sees most.
    await pumpSearch(
      tester,
      locale: const Locale('ar'),
      filter: const SearchFilter(transmission: 'Automatic'),
    );

    final label = ar.searchFiltersApplied(1);
    expect(find.text(label), findsOneWidget,
        reason: 'the Filters button should be showing "$label"');
    expect(whole(tester, label), isTrue,
        reason: '"$label" is being given less width than the words need');
  });

  testWidgets('nor at the largest text size the app honours', (tester) async {
    // The app clamps scaling to 1.4. At that size the button can no longer sit
    // beside the count, so it moves under it — the label still arrives whole.
    await pumpSearch(
      tester,
      locale: const Locale('ar'),
      textScale: 1.4,
      filter: const SearchFilter(transmission: 'Automatic'),
    );

    expect(whole(tester, ar.searchFiltersApplied(1)), isTrue);
    expect(whole(tester, ar.searchAnyDates), isTrue);
  });

  testWidgets('and English is not broken by fixing Arabic', (tester) async {
    await pumpSearch(
      tester,
      locale: const Locale('en'),
      textScale: 1.4,
      filter: const SearchFilter(transmission: 'Automatic', deliveryOnly: true),
    );

    expect(whole(tester, 'Any dates'), isTrue);
    expect(whole(tester, '2 filters'), isTrue);
  });

  testWidgets('the count and the Filters button share a line when they fit',
      (tester) async {
    // In English at the default size these sit side by side, and stacking them
    // would push the first result further down for no reason.
    await pumpSearch(tester, locale: const Locale('en'));

    final count = tester.getCenter(find.text('No cars'));
    final filters = tester.getCenter(find.text('Filters'));
    expect(count.dy, closeTo(filters.dy, 1),
        reason: 'the two should share a line at the default text size');
  });

  // An overflow paints a yellow-and-black bar in debug and silently crops in
  // release. `takeException` is how a test sees the one the customer would not.
  //
  // One locale per test, not a loop: pumping a second tree over the first leaves
  // the first screen's timers running into the teardown, and the failure that
  // reports is about the test, not about the layout.
  for (final locale in const [Locale('en'), Locale('ar')]) {
    testWidgets('the search screen does not overflow at 375 in '
        '${locale.languageCode} at the largest text size', (tester) async {
      await pumpSearch(tester, locale: locale, textScale: 1.4);
      expect(tester.takeException(), isNull);
    });
  }
}
