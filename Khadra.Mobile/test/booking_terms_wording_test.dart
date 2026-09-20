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
}
