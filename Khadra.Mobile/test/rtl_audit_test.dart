import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// Half this app's customers read right to left, and the mistakes that break that
/// are invisible in English.
///
/// None of them is a crash or an analyzer warning. A `left:` padding looks correct
/// on every screenshot anybody takes; a chevron pointing the wrong way reads as an
/// arrow back the way you came; a hard-coded sentence is simply English text in an
/// Arabic interface. They are all found by READING, which is why they had all
/// survived until somebody opened the app in Arabic.
///
/// So this reads the source instead, and states each rule with the reason it
/// exists. Where a line genuinely has to name a side it carries a marker saying
/// why, right there — an exemption anybody reading the code can see, rather than a
/// list of file paths somebody has to keep in step.
void main() {
  late List<({String path, String source})> dartFiles;

  /// A line carrying this is exempt, and has to say why on the same line.
  ///
  /// PER LINE rather than per file. Excusing `khadra_widgets.dart` wholesale —
  /// which is what this did first — covers three deliberate uses and every
  /// accident that lands in the same 900-line file afterwards. It had already
  /// happened: a text measurement added a fourth `textDirection:` there and the
  /// audit never looked at it.
  const marker = 'rtl-audit: allow';

  setUpAll(() {
    dartFiles = [
      for (final entity in Directory('lib').listSync(recursive: true))
        if (entity is File &&
            entity.path.endsWith('.dart') &&
            // Generated from the ARB files; not written by hand and not ours to
            // hold to a style rule.
            !entity.path.contains('app_localizations'))
          (
            path: entity.path.replaceAll(r'\', '/'),
            // An excused line is removed before anything is matched, and so is
            // the line below it: the marker sits above the code it excuses, the
            // way an `ignore:` comment does.
            source: _withoutExcusedLines(entity.readAsStringSync(), marker),
          ),
    ];

    expect(dartFiles, isNotEmpty, reason: 'no sources found to audit');
  });

  /// Reports every file matching [pattern].
  List<String> offenders(RegExp pattern) => [
        for (final file in dartFiles)
          if (pattern.hasMatch(file.source)) file.path,
      ];

  test('padding names a START and an END, never a left and a right', () {
    // `EdgeInsets.only(left:)` and an asymmetric `fromLTRB` both pin a gap to a
    // physical side. In Arabic the layout mirrors and the gap does not, so a
    // heading that sat on its margin in English floats off it in Arabic while the
    // control beside it presses into the edge — which is exactly what the filter
    // sheet's header did.
    expect(
      // Anywhere in the argument list, not just first: `EdgeInsets.only(top: 4,
      // left: 8)` is the same mistake and the narrower pattern walked past it.
      offenders(RegExp(r'EdgeInsets\.only\([^)]*\b(left|right)\s*:')),
      isEmpty,
      reason: 'use EdgeInsetsDirectional.only(start:/end:)',
    );

    // `Positioned` pins a child to a physical edge for the same reason. The app
    // uses `PositionedDirectional` today; this is what keeps it that way.
    expect(
      offenders(RegExp(r'\bPositioned\(')),
      isEmpty,
      reason: 'use PositionedDirectional(start:/end:)',
    );

    // A symmetric fromLTRB is the same in both directions and is fine. An
    // asymmetric one is not, so it is found here rather than by eye.
    final asymmetric = <String>[];
    final call = RegExp(r'EdgeInsets\.fromLTRB\(\s*([^,]+),\s*[^,]+,\s*([^,]+),');
    for (final file in dartFiles) {
      for (final match in call.allMatches(file.source)) {
        final start = match.group(1)!.trim();
        final end = match.group(2)!.trim();
        if (start != end) asymmetric.add('${file.path}: $start .. $end');
      }
    }
    expect(
      asymmetric,
      isEmpty,
      reason: 'use EdgeInsetsDirectional.fromSTEB for an uneven inset',
    );
  });

  test('alignment follows the reading direction', () {
    expect(
      offenders(RegExp(
          r'Alignment\.(centerLeft|centerRight|topLeft|topRight|bottomLeft|bottomRight)')),
      isEmpty,
      reason: 'use AlignmentDirectional.centerStart / centerEnd',
    );
  });

  test('no icon is mirrored by hand', () {
    // THE RULE IS THE OPPOSITE OF WHAT IT LOOKS LIKE, and getting it backwards is
    // what this test now exists to stop.
    //
    // `Icons.chevron_left`, `chevron_right`, `arrow_back` and their relatives are
    // all declared `matchTextDirection: true`, and the `Icon` widget flips any
    // such glyph under an RTL `Directionality`. So naming one directly is already
    // correct in both languages — and CHOOSING one by direction mirrors a glyph
    // that was going to be mirrored anyway, landing back where it started.
    //
    // That is not hypothetical: the back button pointed the wrong way on every
    // Arabic screen for as long as it had been written that way, and a "fix" to
    // the four trailing chevrons did the same thing to them.
    //
    // `chevron_direction_test.dart` asserts the rendered direction; this forbids
    // the idiom that broke it.
    final handMirrored = <String>[];
    final pattern = RegExp(
      r'Directionality\.of\([^)]*\)\s*==\s*TextDirection\.\w+[^;]*?Icons\.',
      dotAll: true,
    );
    for (final file in dartFiles) {
      if (pattern.hasMatch(file.source)) handMirrored.add(file.path);
    }

    expect(
      handMirrored,
      isEmpty,
      reason: 'name the icon once and let Icon mirror it; see KhadraDisclosure',
    );
  });

  test('text alignment and direction are not pinned to a side', () {
    // Three uses are legitimate and each says so on its own line: `LatinRun`,
    // which forces LTR so a booking reference is not reordered by the bidi
    // algorithm; `UserText`, which takes the direction from what somebody TYPED
    // rather than from the interface; and one text measurement.
    expect(
      offenders(
        RegExp(r'TextAlign\.(left|right)|textDirection: TextDirection\.'),
      ),
      isEmpty,
      reason: 'use TextAlign.start / TextAlign.end, or UserText for typed text',
    );
  });

  test('letter spacing is named in the theme, never in a screen', () {
    // Arabic is a JOINED script. Every tracking figure in this app was chosen for
    // Manrope's Latin letterforms, and applying it to Arabic does not space the
    // letters — it breaks the joins inside the word, so a heading arrives as a row
    // of disconnected marks. `KhadraType` answers null for Arabic and the design's
    // figure for Latin, and the theme gates its own scale the same way.
    //
    // A literal here escapes both, silently, in the one language nobody testing in
    // English would ever see it in. The exemptions are the Latin runs — a booking
    // reference is Latin whatever the interface is — and each says so on its line.
    final literal = RegExp(r'letterSpacing: [-0-9.]');
    expect(
      [
        for (final file in dartFiles)
          if (!file.path.endsWith('core/theme/khadra_theme.dart') &&
              literal.hasMatch(file.source))
            file.path,
      ],
      isEmpty,
      reason: 'use KhadraType.of(context, latinValue)',
    );
  });

  test('no list of numbers stands in for a choice the catalogue makes', () {
    // Not an RTL mistake, and found by the same reading. The seat filter offered
    // `const [2, 4, 5, 7]`: "at least 2 seats" on a platform with no two-seaters,
    // and never the nine-seat van an office lists tomorrow. Its choices are now the
    // facets the server sends, and a literal list of numbers in the catalogue's UI
    // is exactly how that list would come back.
    final literal = RegExp(r'const\s*(<int>)?\s*\[\s*\d+(\s*,\s*\d+)+\s*\]');
    expect(
      [
        for (final file in dartFiles)
          if (file.path.contains('/features/catalogue/') && literal.hasMatch(file.source))
            file.path,
      ],
      isEmpty,
      reason: 'build the choices from /api/v1/vehicles/facets',
    );
  });

  test('no sentence is written in Dart instead of in the ARB files', () {
    // A literal here is a string that can only ever appear in one language. The
    // pattern looks for a capitalised phrase in quotes — "Sign in", "No results" —
    // while leaving identifiers, asset paths, media types and locale codes alone.
    final literals = <String>[];
    final phrase = RegExp(r"'[A-Z][a-z]+(?: [a-z]+)+'");
    final ignorable = RegExp(
        r'assets/|package:|https?:|Asia/|application/|image/|text/|Bearer|khadra\.');

    for (final file in dartFiles) {
      for (final match in phrase.allMatches(file.source)) {
        final text = match.group(0)!;
        if (ignorable.hasMatch(text)) continue;
        literals.add('${file.path}: $text');
      }
    }

    expect(
      literals,
      isEmpty,
      reason: 'move it into app_en.arb and app_ar.arb',
    );
  });
}

/// Drops every line carrying [marker], and the line after it.
///
/// The marker sits ABOVE what it excuses, the way `// ignore:` does, so both go.
/// Removing them before matching is what lets a rule stay absolute while three
/// deliberate exceptions carry their reason in the source rather than in a list
/// of file paths somebody has to keep in step.
String _withoutExcusedLines(String source, String marker) {
  final lines = source.split('\n');
  final kept = <String>[];

  for (var i = 0; i < lines.length; i++) {
    if (lines[i].contains(marker)) {
      i++; // Skip the line it excuses too.
      continue;
    }
    kept.add(lines[i]);
  }

  return kept.join('\n');
}
