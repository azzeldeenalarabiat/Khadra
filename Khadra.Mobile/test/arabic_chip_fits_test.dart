import 'dart:io';

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
/// It was fixed with the number 42, measured once at the default text size. That
/// is the same mistake one step along: this app honours text scaling to 1.4, and
/// at 1.4 the Arabic chips were cropped again for every customer who had turned
/// text up. The row now ASKS the chip how tall it is, so these tests ask the same
/// question at both ends of the range instead of restating an answer.
void main() {
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

  /// Builds one chip and reports the three numbers that have to agree: what the
  /// row reserves, what the chip takes, and what the words inside it need.
  Future<({double row, double chip, double text})> measure(
    WidgetTester tester,
    Locale locale,
    String label, {
    double textScale = 1,
  }) async {
    late double row;

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
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: TextScaler.linear(textScale)),
          child: child ?? const SizedBox.shrink(),
        ),
        home: Scaffold(
          // UNCONSTRAINED on purpose: this is the height the chip wants, which is
          // the number the row has to be able to hold.
          body: Align(
            alignment: Alignment.topLeft,
            child: Builder(builder: (context) {
              // Read from INSIDE the tree, so it sees the same faces, the same
              // default text style and the same text size the chip beside it does.
              row = KhadraChoiceChip.heightIn(context);
              return KhadraChoiceChip(label: label, onTap: () {});
            }),
          ),
        ),
      ),
    );
    await tester.pump();

    return (
      row: row,
      chip: tester.getSize(find.byType(KhadraChoiceChip)).height,
      text: tester
          .getSize(find.descendant(
            of: find.byType(KhadraChoiceChip),
            matching: find.byType(Text),
          ))
          .height,
    );
  }

  /// Real labels, in both scripts: a city, a car type, and the longest "any".
  const labels = <(Locale, String)>[
    (Locale('ar'), 'عمّان'),
    (Locale('ar'), 'دفع رباعي'),
    (Locale('ar'), 'كل الأنواع'),
    (Locale('en'), 'Amman'),
    (Locale('en'), 'Any type'),
    (Locale('en'), 'Four wheel drive'),
  ];

  /// The two ends of what this app allows. `main.dart` clamps scaling to
  /// [0.9, 1.4], and both ends have to hold.
  const scales = <double>[0.9, 1.0, 1.4];

  testWidgets('the row reserves enough for any chip, at any text size',
      (tester) async {
    for (final scale in scales) {
      for (final (locale, label) in labels) {
        final size = await measure(tester, locale, label, textScale: scale);
        expect(
          size.chip,
          lessThanOrEqualTo(size.row),
          reason: '"$label" at ${scale}x needs ${size.chip}px and the row gives '
              '${size.row} — it will be clipped, and only in that combination',
        );
      }
    }
  });

  testWidgets('the chip grows to its words rather than cropping them',
      (tester) async {
    // The chip must take its height FROM the text, in either script. A box sized
    // from a number somebody wrote down while looking at the English screen is
    // how the Arabic city chips lost the top and bottom of every word.
    for (final scale in scales) {
      for (final (locale, label) in labels) {
        final size = await measure(tester, locale, label, textScale: scale);
        // 9 of padding each side, and a 1px border each side.
        final slack = size.chip - size.text - 20;
        expect(
          slack,
          greaterThanOrEqualTo(0),
          reason: '"$label" at ${scale}x is painting into less height than it needs',
        );
      }
    }
  });

  testWidgets('the reserved height does not depend on which language is showing',
      (tester) async {
    // The row is built once and holds whatever the lookup returns, which in
    // Arabic is Arabic and in English is English — and a customer can switch
    // between them without the list being rebuilt from scratch. So the number has
    // to be the worst case over BOTH faces, not the one that happens to be on
    // screen. Measuring a single alphabet is what made it a pixel short.
    final arabic = await measure(tester, const Locale('ar'), 'عمّان');
    final latin = await measure(tester, const Locale('en'), 'Amman');

    expect(arabic.row, latin.row);
    expect(arabic.chip, lessThanOrEqualTo(arabic.row));
    expect(latin.chip, lessThanOrEqualTo(latin.row));
  });
}
