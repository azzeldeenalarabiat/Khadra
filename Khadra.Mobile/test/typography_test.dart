import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';

/// The two faces the approved design is set in, and the reason there are two.
///
/// **Manrope contains no Arabic.** Not one letter, not the Arabic comma. So an
/// Arabic interface set in Manrope alone is an interface of empty boxes, and the
/// fallback to Noto Kufi Arabic is not a nicety — it is the only thing rendering
/// half this app's users' language.
///
/// These tests read the actual bundled `.ttf` files rather than trusting the
/// pubspec, because the failure they exist to catch is a font file quietly
/// replaced by one with different coverage: nothing about that shows up in an
/// analyzer, a widget test, or any screen a developer happens to open in English.
void main() {
  const latin = 'assets/fonts/Manrope-400.ttf';
  const arabic = 'assets/fonts/NotoKufiArabic-400.ttf';

  /// Code points the app actually renders.
  const samples = <String, int>{
    'Latin capital K': 0x004B,
    'Latin small a': 0x0061,
    'digit 5': 0x0035,
    'interpunct ·': 0x00B7,
    'en dash –': 0x2013,
  };

  const arabicSamples = <String, int>{
    'ALEF ا': 0x0627,
    'BEH ب': 0x0628,
    'TEH MARBUTA ة': 0x0629,
    'LAM ل': 0x0644,
    'YEH ي': 0x064A,
    'Arabic comma ،': 0x060C,
    'Arabic-Indic five ٥': 0x0665,
    'LAM-ALEF ligature لا': 0xFEFB,
  };

  group('the bundled faces', () {
    test('every weight the theme can ask for is on disk', () {
      const weights = <String>['400', '500', '600', '700', '800'];
      for (final weight in weights) {
        expect(File('assets/fonts/Manrope-$weight.ttf').existsSync(), isTrue,
            reason: 'Manrope $weight is registered in pubspec.yaml');
      }
      // Kufi stops at 700: the design asks for 400/500/700 and Flutter picks the
      // nearest weight for anything heavier, so an 800 Arabic heading renders at
      // 700 rather than synthesising a bolder face.
      for (final weight in <String>['400', '500', '600', '700']) {
        expect(File('assets/fonts/NotoKufiArabic-$weight.ttf').existsSync(), isTrue);
      }
    });

    test('Manrope sets the Latin, the figures and the punctuation', () {
      final has = _coverage(latin);
      for (final entry in samples.entries) {
        expect(has(entry.value), isTrue, reason: 'Manrope is missing ${entry.key}');
      }
    });

    test('Manrope has NO Arabic, which is why the fallback exists', () {
      final has = _coverage(latin);
      for (final entry in arabicSamples.entries) {
        expect(has(entry.value), isFalse,
            reason: 'Manrope unexpectedly covers ${entry.key} — if a build of it '
                'ever does, the fallback ordering below needs rechecking');
      }
    });

    test('Noto Kufi Arabic covers every Arabic character the app renders', () {
      final has = _coverage(arabic);
      for (final entry in arabicSamples.entries) {
        expect(has(entry.value), isTrue,
            reason: 'Noto Kufi Arabic is missing ${entry.key} — Arabic would render '
                'as empty boxes');
      }
    });
  });

  group('how the theme names them', () {
    test('Manrope is the family and Kufi is the first fallback', () {
      final theme = KhadraTheme.light();
      final body = theme.textTheme.bodyMedium!;

      expect(body.fontFamily, 'Manrope');
      // ORDERING is load-bearing. Noto Kufi Arabic carries Latin and digits too, so
      // ahead of Manrope it would silently re-set the entire English interface.
      expect(body.fontFamilyFallback!.first, 'Noto Kufi Arabic');
      expect(body.fontFamilyFallback, contains('Noto Naskh Arabic'));
    });

    test('every explicit style in the theme carries the Arabic fallback', () {
      // A style that names Manrope and forgets the fallback renders Arabic as
      // boxes on exactly one widget, which is the kind of thing nobody sees until
      // it ships. These are the styles the theme sets by hand.
      final theme = KhadraTheme.light();
      final styles = <String, TextStyle?>{
        'appBar title': theme.appBarTheme.titleTextStyle,
        'snackBar': theme.snackBarTheme.contentTextStyle,
        'chip label': theme.chipTheme.labelStyle,
        'input label': theme.inputDecorationTheme.labelStyle,
        'input hint': theme.inputDecorationTheme.hintStyle,
        'headline': theme.textTheme.headlineSmall,
        'title large': theme.textTheme.titleLarge,
        'body small': theme.textTheme.bodySmall,
        'label small': theme.textTheme.labelSmall,
      };

      for (final entry in styles.entries) {
        expect(entry.value, isNotNull, reason: '${entry.key} is set by the theme');


        // A style can depend on the widget's STATE, and a state the theme forgot
        // to give a family to renders Arabic as boxes on that state alone --
        // which is the version nobody screenshots. So every state is checked,
        // not just the resting one.
        for (final states in <Set<WidgetState>>{
          <WidgetState>{},
          <WidgetState>{WidgetState.selected},
          <WidgetState>{WidgetState.disabled},
        }) {
          final style = entry.value is WidgetStateTextStyle
              ? (entry.value! as WidgetStateTextStyle).resolve(states)
              : entry.value!;
          final where = '${entry.key} ${states.isEmpty ? '(resting)' : states}';
          expect(style.fontFamily, 'Manrope', reason: where);
          expect(style.fontFamilyFallback, contains('Noto Kufi Arabic'),
              reason: '$where would render Arabic as empty boxes');
        }
      }
    });
  });

  /// Arabic is a JOINED script.
  ///
  /// Every tracking figure in this design was chosen for Manrope's Latin
  /// letterforms — headings pulled in, small upper-case labels opened out. Applied
  /// to Arabic it does not space the letters, because Arabic letters are not
  /// separate: it breaks the joins inside a word, and a heading arrives as a row of
  /// disconnected marks. The app was shipping that on every Arabic screen.
  group('letter spacing follows the script', () {
    /// Every style in a scale that names a tracking figure.
    Map<String, double?> trackingIn(TextTheme scale) => <String, double?>{
          'displayLarge': scale.displayLarge?.letterSpacing,
          'displayMedium': scale.displayMedium?.letterSpacing,
          'displaySmall': scale.displaySmall?.letterSpacing,
          'headlineLarge': scale.headlineLarge?.letterSpacing,
          'headlineMedium': scale.headlineMedium?.letterSpacing,
          'headlineSmall': scale.headlineSmall?.letterSpacing,
          'titleLarge': scale.titleLarge?.letterSpacing,
          'titleMedium': scale.titleMedium?.letterSpacing,
          'titleSmall': scale.titleSmall?.letterSpacing,
          'bodyLarge': scale.bodyLarge?.letterSpacing,
          'bodyMedium': scale.bodyMedium?.letterSpacing,
          'bodySmall': scale.bodySmall?.letterSpacing,
          'labelLarge': scale.labelLarge?.letterSpacing,
          'labelMedium': scale.labelMedium?.letterSpacing,
          'labelSmall': scale.labelSmall?.letterSpacing,
        };

    test('the English scale keeps every figure the design asks for', () {
      final scale = KhadraTheme.light().textTheme;

      expect(scale.headlineMedium!.letterSpacing, -0.6);
      expect(scale.headlineSmall!.letterSpacing, -0.4);
      expect(scale.titleLarge!.letterSpacing, -0.2);
      expect(KhadraTheme.light().appBarTheme.titleTextStyle!.letterSpacing, -0.2);
    });

    test('the Arabic scale carries no tracking anywhere in it', () {
      // Including the styles this app never overrides: Material brings its own
      // figures, and one factor over the whole scale is what stops a style being
      // missed the day a screen starts using it.
      final tracked = <String>[
        for (final entry in trackingIn(KhadraTheme.light(arabic: true).textTheme).entries)
          if ((entry.value ?? 0) != 0) '${entry.key}=${entry.value}',
      ];

      expect(tracked, isEmpty);
      expect(
        KhadraTheme.light(arabic: true).appBarTheme.titleTextStyle!.letterSpacing,
        anyOf(isNull, 0),
      );
    });

    Future<TextStyle> styleOf(
      WidgetTester tester, {
      required String language,
      required Widget child,
      required String text,
    }) async {
      await tester.pumpWidget(MaterialApp(
        locale: Locale(language),
        theme: KhadraTheme.light(arabic: language == 'ar'),
        supportedLocales: AppLocalizations.supportedLocales,
        localizationsDelegates: const [
          AppLocalizations.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        home: Scaffold(body: child),
      ));

      return tester.widget<Text>(find.text(text)).style!;
    }

    // The widgets that name a figure of their own. They cannot read the theme's
      // answer back out, so each asks `KhadraType`, and `rtl_audit_test` forbids
    // the literal that would go round it.
    final namesItsOwn = <String, ({Widget widget, String text, double latin})>{
      'a screen title': (
        widget: const KhadraLargeTitle('Bookings'),
        text: 'Bookings',
        latin: -0.4,
      ),
      'a section heading': (
        widget: const KhadraSectionTitle('Delivery'),
        text: 'Delivery',
        latin: -0.2,
      ),
      'a status badge': (
        widget: const KhadraBadge(label: 'PENDING', colour: KhadraColors.warn),
        text: 'PENDING',
        latin: 0.3,
      ),
    };

    for (final entry in namesItsOwn.entries) {
      testWidgets('${entry.key} is tracked in English', (tester) async {
        final style = await styleOf(tester,
            language: 'en', child: entry.value.widget, text: entry.value.text);
        expect(style.letterSpacing, entry.value.latin);
      });

      testWidgets('${entry.key} is not tracked in Arabic', (tester) async {
        final style = await styleOf(tester,
            language: 'ar', child: entry.value.widget, text: entry.value.text);
        expect(style.letterSpacing, anyOf(isNull, 0));
      });
    }
  });
}

/// Whether a TrueType file has a glyph for a code point.
///
/// Enough of the `cmap` table to answer that and nothing more — a font package
/// for one assertion would be a dependency this app ships to every phone.
bool Function(int) _coverage(String path) {
  final bytes = ByteData.sublistView(Uint8List.fromList(File(path).readAsBytesSync()));

  int? cmap;
  final tables = bytes.getUint16(4);
  for (var i = 0; i < tables; i++) {
    final record = 12 + i * 16;
    final tag = String.fromCharCodes([
      for (var b = 0; b < 4; b++) bytes.getUint8(record + b),
    ]);
    if (tag == 'cmap') cmap = bytes.getUint32(record + 8);
  }
  expect(cmap, isNotNull, reason: '$path has a cmap table');

  int? subtable;
  final encodings = bytes.getUint16(cmap! + 2);
  for (var i = 0; i < encodings; i++) {
    final record = cmap + 4 + i * 8;
    final platform = bytes.getUint16(record);
    final encoding = bytes.getUint16(record + 2);
    final offset = cmap + bytes.getUint32(record + 4);
    // Windows Unicode BMP, format 4 — the one every Google font ships.
    if (platform == 3 && (encoding == 1 || encoding == 10) && bytes.getUint16(offset) == 4) {
      subtable = offset;
    }
  }
  expect(subtable, isNotNull, reason: '$path has a Unicode cmap');

  final segments = bytes.getUint16(subtable! + 6) ~/ 2;
  final ends = subtable + 14;
  final starts = ends + segments * 2 + 2;
  final deltas = starts + segments * 2;
  final ranges = deltas + segments * 2;

  return (int codePoint) {
    for (var s = 0; s < segments; s++) {
      final end = bytes.getUint16(ends + s * 2);
      if (codePoint > end) continue;
      final start = bytes.getUint16(starts + s * 2);
      if (codePoint < start) return false;

      final rangeOffset = bytes.getUint16(ranges + s * 2);
      if (rangeOffset == 0) {
        final delta = bytes.getInt16(deltas + s * 2);
        return ((codePoint + delta) & 0xFFFF) != 0;
      }
      final glyphIndex = ranges + s * 2 + rangeOffset + (codePoint - start) * 2;
      if (glyphIndex + 1 >= bytes.lengthInBytes) return false;
      return bytes.getUint16(glyphIndex) != 0;
    }
    return false;
  };
}
