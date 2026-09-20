import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/format/booking_presentation.dart';
import 'package:khadra_mobile/l10n/app_localizations_ar.dart';
import 'package:khadra_mobile/l10n/app_localizations_en.dart';

/// The clock under "Deposit of 44.000 JOD is due".
///
/// It became the most-read surface of a business rule on 2026-09-11, when the
/// payment window went from twenty-four hours to two. A countdown that used to
/// open at "23h" and be glanced at once now opens at "2h" and is watched, and it
/// spends its whole life in the one range where Arabic inflects the unit most.
///
/// The deadline is the SERVER's instant on the booking. These tests build one
/// relative to now, because that is the only way to exercise a display-only clock
/// comparison — no business number is being restated here.
void main() {
  final en = AppLocalizationsEn();
  final ar = AppLocalizationsAr();

  DateTime inFuture(Duration d) => DateTime.now().toUtc().add(d + const Duration(seconds: 2));

  group('the payment window, at the length the owner set', () {
    test('a fresh two-hour window is a dual in Arabic, not a digit', () {
      final text = BookingPresentation.countdown(ar, inFuture(const Duration(hours: 2)));
      expect(text, contains('ساعتان'));
      expect(text, isNot(contains('2 ساعة')));
      expect(text, startsWith('المتبقي'));
    });

    test('the same moment abbreviates in English', () {
      expect(
        BookingPresentation.countdown(en, inFuture(const Duration(hours: 2))),
        '2h 0m left',
      );
    });

    test('one hour left is singular in Arabic', () {
      final text = BookingPresentation.countdown(ar, inFuture(const Duration(hours: 1, minutes: 30)));
      expect(text, contains('ساعة واحدة'));
      expect(text, contains('30 دقيقة'));
    });

    test('the last minutes carry the right plural for three to ten', () {
      expect(
        BookingPresentation.countdown(ar, inFuture(const Duration(minutes: 5))),
        'المتبقي 5 دقائق',
      );
      expect(
        BookingPresentation.countdown(en, inFuture(const Duration(minutes: 5))),
        '5m left',
      );
    });

    test('a single minute is not "1 دقيقة"', () {
      expect(
        BookingPresentation.countdown(ar, inFuture(const Duration(minutes: 1))),
        'المتبقي دقيقة واحدة',
      );
    });

    test('two minutes takes the dual', () {
      expect(
        BookingPresentation.countdown(ar, inFuture(const Duration(minutes: 2))),
        'المتبقي دقيقتان',
      );
    });
  });

  group("the gallery's answer window, which is still forty-eight hours", () {
    test('days and hours read correctly in both', () {
      final deadline = inFuture(const Duration(days: 1, hours: 23));
      expect(BookingPresentation.countdown(en, deadline), '1d 23h left');

      final arabic = BookingPresentation.countdown(ar, deadline);
      expect(arabic, contains('يوم واحد'));
      expect(arabic, contains('23 ساعة'));
    });

    test('two days takes the dual in Arabic', () {
      final arabic = BookingPresentation.countdown(ar, inFuture(const Duration(days: 2, hours: 3)));
      expect(arabic, contains('يومان'));
      expect(arabic, contains('3 ساعات'));
    });
  });

  group('when it runs out', () {
    test('a passed deadline says so rather than counting backwards', () {
      final passed = DateTime.now().toUtc().subtract(const Duration(minutes: 1));
      expect(BookingPresentation.countdown(en, passed), 'The time has run out');
      expect(BookingPresentation.countdown(ar, passed), 'انتهى الوقت');
    });

    test('the final seconds round down to zero rather than going negative', () {
      final almost = DateTime.now().toUtc().add(const Duration(seconds: 20));
      expect(BookingPresentation.countdown(en, almost), '0m left');
      expect(BookingPresentation.countdown(ar, almost), contains('0'));
    });
  });
}
