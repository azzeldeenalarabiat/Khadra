import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/features/invoices/document_content.dart';
import 'package:khadra_mobile/features/invoices/invoice_content.dart';
import 'package:khadra_mobile/features/invoices/invoice_presentation.dart';

/// A name as registered takes the direction of its FIRST strong character on the
/// phone, as the website's and the console's `<bdi>` give it (the reader's
/// contract, docs/contracts/README.md) — never the word-count estimate a typed
/// paragraph uses, which lays a mostly-Latin name that starts in Arabic out the
/// other way round, so the phone and the website showed its halves in opposite
/// order.
void main() {
  // Starts in Arabic; four of its six words are Latin.
  const arabicFirst = 'أوتو رنت — Auto Rent Jordan LLC';
  const latinFirst = 'Auto Rent — أوتو رنت';

  test('the case this exists for: the estimate and the first strong character disagree', () {
    expect(UserText.directionOf(arabicFirst), TextDirection.ltr);
    expect(FirstStrongRun.directionOf(arabicFirst), TextDirection.rtl);
  });

  test('a name takes the direction of its first strong character', () {
    expect(FirstStrongRun.directionOf(arabicFirst), TextDirection.rtl);
    expect(FirstStrongRun.directionOf(latinFirst), TextDirection.ltr);
    expect(FirstStrongRun.directionOf('مكتب النديم'), TextDirection.rtl);
    // A neutral lead-in decides nothing.
    expect(FirstStrongRun.directionOf('(1) أوتو رنت — Auto Rent'), TextDirection.rtl);
  });

  for (final interface in [TextDirection.ltr, TextDirection.rtl]) {
    testWidgets('a document value written with an Arabic letter follows its first strong character on a $interface screen',
        (tester) async {
      Future<TextDirection?> rendered(String name) async {
        await tester.pumpWidget(Directionality(
          textDirection: interface,
          child: DocumentValueText(name, direction: directionOf(PlainValue(name))),
        ));
        return tester.widget<Text>(find.text(name)).textDirection;
      }

      expect(await rendered(arabicFirst), TextDirection.rtl);
      expect(await rendered(latinFirst), TextDirection.ltr);
    });
  }
}
