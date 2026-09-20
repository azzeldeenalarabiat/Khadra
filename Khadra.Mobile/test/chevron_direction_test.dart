import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';

/// Which way the chevrons actually POINT, in both languages.
///
/// This exists because the obvious way to test them is wrong. Asserting that the
/// widget chose `Icons.chevron_left` says nothing, because `Icon` may flip the
/// glyph before it reaches the screen: both chevrons are declared with
/// `matchTextDirection: true`, and `Icon` mirrors any such glyph under an RTL
/// `Directionality`.
///
/// Missing that is what produced the bug this file is the answer to. Four screens
/// named `Icons.chevron_right` for a trailing chevron and were correct; a
/// "fix" replaced them with a widget that chose `chevron_left` in Arabic, which
/// Flutter then mirrored back into pointing right. The same mistake was already
/// in `KhadraBack`, where it had been pointing the back button the wrong way on
/// every Arabic screen for as long as the widget had existed.
///
/// So the question asked here is the one a customer asks: given the direction the
/// page is laid out in, which way does the arrow end up pointing?
void main() {
  /// True when the glyph reaches the screen pointing right.
  ///
  /// `chevron_right` points right on its own and `chevron_left` points left; a
  /// mirrored glyph points the other way. That is the whole of it, and it is the
  /// step the naive test leaves out.
  bool pointsRight(IconData icon, TextDirection direction) {
    final drawnRight = icon == Icons.chevron_right;
    final mirrored =
        icon.matchTextDirection && direction == TextDirection.rtl;
    return drawnRight != mirrored;
  }

  Future<IconData> glyphOf(
    WidgetTester tester,
    Widget widget,
    TextDirection direction,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Directionality(
          textDirection: direction,
          child: Scaffold(body: Center(child: widget)),
        ),
      ),
    );
    await tester.pump();
    return tester.widget<Icon>(find.byType(Icon)).icon!;
  }

  testWidgets('a disclosure chevron points the way the page ENDS', (tester) async {
    // It says "this row opens something", so it points towards the edge the
    // language runs to: the right in English, the left in Arabic.
    for (final (direction, expected) in <(TextDirection, bool)>[
      (TextDirection.ltr, true),
      (TextDirection.rtl, false),
    ]) {
      final icon = await glyphOf(tester, const KhadraDisclosure(), direction);
      expect(
        pointsRight(icon, direction),
        expected,
        reason: 'in ${direction.name} the disclosure chevron should point '
            '${expected ? 'right' : 'left'}, and it points '
            '${pointsRight(icon, direction) ? 'right' : 'left'}',
      );
    }
  });

  testWidgets('a back chevron points the way the page STARTS', (tester) async {
    // "Back" is where you came from, which is the leading edge: left in English,
    // right in Arabic.
    for (final (direction, expected) in <(TextDirection, bool)>[
      (TextDirection.ltr, false),
      (TextDirection.rtl, true),
    ]) {
      final icon = await glyphOf(
        tester,
        const KhadraBack(fallback: '/'),
        direction,
      );
      expect(
        pointsRight(icon, direction),
        expected,
        reason: 'in ${direction.name} the back chevron should point '
            '${expected ? 'right' : 'left'}, and it points '
            '${pointsRight(icon, direction) ? 'right' : 'left'}',
      );
    }
  });

  test('both chevrons are glyphs Flutter mirrors, which is why none is chosen',
      () {
    // The premise the two tests rest on, stated so that a Flutter release which
    // changed it would fail here rather than silently turn every arrow around.
    expect(Icons.chevron_left.matchTextDirection, isTrue);
    expect(Icons.chevron_right.matchTextDirection, isTrue);
  });
}
