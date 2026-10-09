import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/l10n/app_localizations_ar.dart';
import 'package:khadra_mobile/l10n/app_localizations_en.dart';

/// The sentences that state a booking's clocks, at the numbers the platform
/// actually ships.
///
/// These were plain interpolations — "{hours} hours" and "{hours} ساعة" — for as
/// long as every window was a large round number. Two of the three stopped being
/// one: free cancellation is an hour, and on 2026-09-11 the payment window became
/// two. English read "for 1 hours" and Arabic would have read "2 ساعة", which is
/// not how the language counts.
///
/// The counts here are written as literals ON PURPOSE, and they are not business
/// rules being restated: they are the arguments a caller passes, and the point of
/// each case is the WORDING that comes back for that argument. The app itself
/// never types these numbers — every call site reads them off the terms the server
/// froze onto the booking.
void main() {
  final en = AppLocalizationsEn();
  final ar = AppLocalizationsAr();

  group('the payment window', () {
    test('two hours reads as a count in English', () {
      expect(en.bookTermsPaymentWindow(2), contains('2 hours'));
    });

    test('two hours takes the dual in Arabic, not a digit', () {
      final sentence = ar.bookTermsPaymentWindow(2);
      expect(sentence, contains('ساعتان'));
      expect(sentence, isNot(contains('2')));
    });

    test('one hour is singular in both', () {
      expect(en.bookTermsPaymentWindow(1), contains('1 hour to pay'));
      expect(en.bookTermsPaymentWindow(1), isNot(contains('hours')));
      expect(ar.bookTermsPaymentWindow(1), contains('ساعة واحدة'));
    });

    test('a few hours takes the plural Arabic needs between three and ten', () {
      expect(ar.bookTermsPaymentWindow(3), contains('3 ساعات'));
    });

    test('the old twenty-four still reads correctly, so the owner can move it back', () {
      expect(en.bookTermsPaymentWindow(24), contains('24 hours'));
      expect(ar.bookTermsPaymentWindow(24), contains('24 ساعة'));
    });

    test('a half-hour window renders as itself rather than rounding', () {
      expect(en.bookTermsPaymentWindow(0.5), contains('0.5 hours'));
    });
  });

  group('the other two clocks', () {
    test('free cancellation is one hour, and says hour', () {
      expect(en.bookTermsFreeCancellation(1), contains('1 hour after'));
      expect(en.bookTermsFreeCancellation(1), isNot(contains('1 hours')));
      expect(ar.bookTermsFreeCancellation(1), contains('ساعة واحدة'));
    });

    // The window starts when a PAYMENT confirms the booking - the deposit or
    // the whole amount - and never runs past the rental start (owner,
    // 2026-09-25). It used to say "after the deposit clears".
    test('free cancellation is measured from payment, never the deposit or approval', () {
      expect(
        en.bookTermsFreeCancellation(1),
        'Free cancellation within 1 hour after payment, as long as the rental has not started.',
      );
      expect(
        ar.bookTermsFreeCancellation(1),
        'الإلغاء مجاني خلال ساعة واحدة من وقت الدفع، ما دام الإيجار لم يبدأ.',
      );
      expect(ar.bookTermsFreeCancellation(2), contains('خلال ساعتين من وقت الدفع'));
      for (final hours in [1, 2, 3, 24]) {
        expect(en.bookTermsFreeCancellation(hours), isNot(contains('deposit')));
        expect(en.bookTermsFreeCancellation(hours), isNot(contains('approval')));
        expect(ar.bookTermsFreeCancellation(hours), isNot(contains('العربون')));
      }
    });

    test("the gallery's answer window is forty-eight and is untouched", () {
      expect(en.bookTermsAnswerWindow(48), contains('48 hours'));
      expect(ar.bookTermsAnswerWindow(48), contains('48 ساعة'));
    });

    test('the sent-request dialog quotes the answer window, in both languages', () {
      expect(
        en.bookDoneBody('Petra Rentals', 48),
        allOf(contains('Petra Rentals'), contains('48 hours')),
      );
      expect(
        ar.bookDoneBody('Petra Rentals', 48),
        allOf(contains('Petra Rentals'), contains('48 ساعة')),
      );
    });
  });

  // Pre-launch item 208: since item 164 an assessed penalty IS kept from the deposit when the dispute window
  // closes, so no sentence may promise that nothing is charged without a dispute. The owner signed these off,
  // word for word, on 2026-10-09; the website carries the same sentences (`cancel.penalty`, `book.termsPenalty`).
  group('what a late cancellation costs (item 208, as approved)', () {
    test('the cancellation sheet', () {
      expect(
        en.cancelPenaltyNotice('JOD 18.000'),
        'Cancelling now incurs a penalty of JOD 18.000. It will be deducted from your deposit when the dispute '
        'window closes, unless the dispute outcome changes this.',
      );
      expect(
        ar.cancelPenaltyNotice('18.000 JOD'),
        'الإلغاء الآن يترتب عليه غرامة قدرها 18.000 JOD. سيتم حسمها من عربونك عند انتهاء مهلة النزاع، '
        'إلا إذا صدر قرار مختلف في النزاع.',
      );
    });

    test('the booking terms', () {
      expect(
        en.bookTermsCancellationPenalty('100%'),
        'Cancelling after that incurs a penalty of 100% of the deposit. It will be deducted from your deposit '
        'when the dispute window closes, unless the dispute outcome changes this.',
      );
      expect(
        ar.bookTermsCancellationPenalty('100%'),
        'الإلغاء بعد ذلك يترتب عليه غرامة قدرها 100% من العربون. سيتم حسمها من عربونك عند انتهاء مهلة النزاع، '
        'إلا إذا صدر قرار مختلف في النزاع.',
      );
    });

    test('neither promises that nothing is charged', () {
      for (final sentence in [
        en.cancelPenaltyNotice('JOD 18.000'),
        en.bookTermsCancellationPenalty('100%'),
      ]) {
        expect(sentence, isNot(contains('Nothing is charged')));
        expect(sentence, isNot(contains('assess')));
      }
    });
  });

  // E2E F26 (owner, 2026-10-09): on a booking paid in full online, the deposit is not "held until you collect
  // the car"; it is part of what was paid. The website's payments.deposit.HeldInFullPayment.
  test('a deposit held on a booking paid in full, as approved', () {
    expect(en.paymentsDepositHeldInFullPayment('JOD 18.000'),
        'Your deposit of JOD 18.000 is part of the full amount you paid online.');
    expect(ar.paymentsDepositHeldInFullPayment('18.000 JOD'),
        'عربونك البالغ 18.000 JOD جزء من المبلغ الكامل الذي دفعته عبر الإنترنت.');
  });
}
