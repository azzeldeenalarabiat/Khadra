import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/format/formats.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;
import 'package:timezone/timezone.dart' as tz;

/// The two rules the whole app depends on getting right: the dinar has three
/// decimals, and every calendar answer is in Amman.
void main() {
  setUpAll(() async {
    tz_data.initializeTimeZones();
    await initializeDateFormatting('en');
    await initializeDateFormatting('ar');
  });

  Formats formatsFor(String locale, {int minorUnits = 3}) => Formats(
        locale: locale,
        currency: CurrencyConfig('JOD', minorUnits),
        zone: tz.getLocation('Asia/Amman'),
      );

  group('money', () {
    test('is padded to the platform minor units, not to two', () {
      // The fils is a THOUSANDTH. Rendering 12.75 against a contract that says
      // 12.750 is a different number on an invoice.
      expect(formatsFor('en').money(const Money(12.75, 'JOD')), 'JOD 12.750');
      expect(formatsFor('en').money(const Money(40, 'JOD')), 'JOD 40.000');
    });

    test('carries the currency code the VALUE named, not a literal', () {
      expect(formatsFor('en').money(const Money(5, 'USD')), 'USD 5.000');
    });

    test('keeps Latin digits under Arabic', () {
      // A price is compared far more often than it is read aloud, and Latin
      // digits are what a Jordanian price list and a bank statement both use.
      final arabic = formatsFor('ar').money(const Money(28, 'JOD'));
      expect(arabic, contains('28.000'));
      expect(arabic, contains('JOD'));
    });

    test('follows the minor units the server names rather than assuming three', () {
      expect(
        formatsFor('en', minorUnits: 2).money(const Money(12.5, 'USD')),
        'USD 12.50',
      );
    });
  });

  group('percent', () {
    test('renders a whole rule as a whole number', () {
      // These are figures a human chose. "20.00%" reads like a computed value.
      expect(formatsFor('en').percent(20), '20%');
      expect(formatsFor('en').percent(100), '100%');
    });

    test('keeps a fractional rule intact', () {
      expect(formatsFor('en').percent(12.5), '12.5%');
    });
  });

  group('Amman calendar', () {
    test('an instant late in a UTC day is already the NEXT day in Amman', () {
      // Amman runs UTC+3. 22:00 UTC on the 5th is 01:00 on the 6th there, which
      // is the day the rental would be billed against.
      final instant = DateTime.utc(2026, 3, 5, 22);
      expect(formatsFor('en').ammanDay(instant), DateTime(2026, 3, 6));
    });

    test('a wall-clock moment is built in Amman, not in the device zone', () {
      // 09:00 in Amman is 06:00 UTC. Constructing a local DateTime and calling
      // toUtc() would send nine in the morning wherever the phone happens to be.
      final instant =
          formatsFor('en').ammanInstant(DateTime(2026, 3, 5), 9, 0);
      expect(instant.isUtc, isTrue);
      expect(instant, DateTime.utc(2026, 3, 5, 6));
    });

    test('counts CALENDAR days, not elapsed twenty-four-hour periods', () {
      // Monday 09:00 to Thursday 11:00 is three calendar days, which is the rule
      // the platform bills by. Elapsed time would say three-and-a-bit.
      final formats = formatsFor('en');
      final from = formats.ammanInstant(DateTime(2026, 3, 2), 9, 0);
      final to = formats.ammanInstant(DateTime(2026, 3, 5), 11, 0);
      expect(formats.calendarDaysBetween(from, to), 3);
    });

    test('a same-day rental spans zero calendar days', () {
      // The platform bills a MINIMUM of one; this method does not apply that
      // rule, and must not, because the billed count is the server's.
      final formats = formatsFor('en');
      final from = formats.ammanInstant(DateTime(2026, 3, 2), 9, 0);
      final to = formats.ammanInstant(DateTime(2026, 3, 2), 18, 0);
      expect(formats.calendarDaysBetween(from, to), 0);
    });
  });

  group('calendarDate', () {
    test('renders a yyyy-MM-dd without moving it through a time zone', () {
      // BookingPricing.pickupDate is a DATE. Parsing it as an instant and
      // formatting it in a zone is how a booking displays the day before the one
      // it was priced for.
      final rendered = formatsFor('en').calendarDate('2026-03-05');
      expect(rendered, contains('5'));
      expect(rendered, contains('2026'));
    });

    test('hands back anything that is not a date unchanged', () {
      expect(formatsFor('en').calendarDate('not-a-date'), 'not-a-date');
    });
  });
}
