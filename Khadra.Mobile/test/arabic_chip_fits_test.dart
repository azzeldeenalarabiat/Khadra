import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';

/// The city and car-type chips on the landing screen live in a HORIZONTAL list,
/// and a horizontal list has to be given a height before it knows what is in it.
///
/// The height that fits an English chip is not the height that fits an Arabic
/// one: Noto Kufi Arabic's line box is deeper than Manrope's at the same point
/// size, and the chip that does not fit is clipped rather than growing. It looked
/// right in English and lost the top and bottom of every Arabic word, which is
/// the kind of thing that only shows up on a screenshot in the other language.
///
/// So the test is the question the layout actually asks: is the row tall enough
/// for the tallest chip either language can produce?
void main() {
  /// The height `_ChipRow` gives its list. Kept here as the thing under test --
  /// if somebody tightens it, this fails before a screenshot does.
  const rowHeight = 42.0;

  // Without this the test renders in Flutter's own test face, where every glyph
  // is a square of the same size -- so a measurement taken here would say nothing
  // about the two real faces whose different line boxes are the whole point.
  setUpAll(() async {
    TestWidgetsFlutterBinding.ensureInitialized();
    for (final family in <String, List<String>>{
      'Manrope': <String>[
        'Manrope-600.ttf',
        'Manrope-700.ttf',
        'Manrope-800.ttf',
      ],
      'Noto Kufi Arabic': <String>[
        'NotoKufiArabic-600.ttf',
        'NotoKufiArabic-700.ttf',
      ],
    }.entries) {
      final loader = FontLoader(family.key);
      for (final file in family.value) {
        final bytes = File('assets/fonts/$file').readAsBytesSync();
        loader.addFont(
            Future<ByteData>.value(ByteData.sublistView(Uint8List.fromList(bytes))));
      }
      await loader.load();
    }
  });

  Future<double> chipHeight(WidgetTester tester, Locale locale, String label) async {
    await tester.pumpWidget(
      MaterialApp(
        locale: locale,
        theme: KhadraTheme.light(),
        localizationsDelegates: const [
          AppLocalizations.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          // UNCONSTRAINED on purpose: this is the height the chip wants, which is
          // the number the row has to be able to hold.
          body: Align(
            alignment: Alignment.topLeft,
            child: KhadraChoiceChip(label: label, onTap: () {}),
          ),
        ),
      ),
    );
    await tester.pump();
    return tester.getSize(find.byType(KhadraChoiceChip)).height;
  }

  /// How much taller the chip is than the words inside it.
  ///
  /// Negative means the label has been squeezed into a box smaller than the text
  /// it is painting, which is exactly what Material's own chip did to Arabic.
  Future<double> slack(WidgetTester tester, Locale locale, String label) async {
    await chipHeight(tester, locale, label);
    final chip = tester.getSize(find.byType(KhadraChoiceChip)).height;
    final text = tester
        .getSize(find.descendant(
          of: find.byType(KhadraChoiceChip),
          matching: find.byType(Text),
        ))
        .height;
    // 9 of padding each side, and a 1px border each side.
    return chip - text - 20;
  }

  testWidgets('the chip row is tall enough for Arabic, not just for English',
      (tester) async {
    // Real labels: a city, a car type, and the longest "any" option.
    for (final label in <String>['عمّان', 'دفع رباعي', 'كل الأنواع']) {
      final height = await chipHeight(tester, const Locale('ar'), label);
      expect(
        height,
        lessThanOrEqualTo(rowHeight),
        reason: '"$label" needs ${height}px and the row gives $rowHeight — it '
            'will be clipped, and only in Arabic',
      );
    }

    for (final label in <String>['Amman', 'Any type', 'Four wheel drive']) {
      final height = await chipHeight(tester, const Locale('en'), label);
      expect(height, lessThanOrEqualTo(rowHeight), reason: label);
    }
  });

  testWidgets('the chip grows to its words rather than cropping them',
      (tester) async {
    // The chip must take its height FROM the text, in either script. A box sized
    // from a number somebody wrote down while looking at the English screen is
    // how the Arabic city chips lost the top and bottom of every word.
    for (final (locale, label) in <(Locale, String)>[
      (const Locale('ar'), 'عمّان'),
      (const Locale('ar'), 'دفع رباعي'),
      (const Locale('en'), 'Amman'),
      (const Locale('en'), 'Four wheel drive'),
    ]) {
      expect(
        await slack(tester, locale, label),
        greaterThanOrEqualTo(0),
        reason: '"$label" is painting into less height than it needs',
      );
    }
  });
}
