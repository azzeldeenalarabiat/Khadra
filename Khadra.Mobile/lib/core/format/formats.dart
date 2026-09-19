import 'package:intl/intl.dart';
import 'package:timezone/timezone.dart' as tz;

import '../../api/dtos.dart';

/// Every number and date the app shows a customer goes through here.
///
/// Two rules it exists to keep, both of which have bitten this platform before:
///
/// **Money has THREE decimals.** The dinar's minor unit is the fils, and the
/// figure comes from `/app-config` rather than a constant, so a screen renders
/// 12.750 where a developer's instinct would render 12.75 — a different number on
/// an invoice.
///
/// **Calendar answers are in AMMAN.** Rentals are billed in Amman calendar days,
/// and a date rendered in the device's zone can sit on the wrong side of midnight
/// from the one the rental was priced against. The zone is the server's, named on
/// `/app-config`, not `DateTime.now().timeZoneName`.
///
/// Digits stay LATIN under Arabic, matching the console's decision: a price is
/// read and compared far more often than it is read aloud, and Latin digits are
/// what a Jordanian price list, a bank statement and a car's odometer all use.
class Formats {
  Formats({
    required this.locale,
    required this.currency,
    required tz.Location zone,
    // Private field, public parameter: nothing outside reads the zone, and callers
    // should not be typing an underscore.
    // ignore: prefer_initializing_formals
  }) : _zone = zone;

  /// 'en' or 'ar'. Chosen by the app, not by the device, because the app has a
  /// language switch of its own.
  final String locale;
  final CurrencyConfig currency;
  final tz.Location _zone;

  bool get isArabic => locale == 'ar';

  /// The instant, as a wall clock in Amman.
  tz.TZDateTime toAmman(DateTime instant) =>
      tz.TZDateTime.from(instant.toUtc(), _zone);

  /// An amount with its own currency code, padded to the platform's minor units.
  ///
  /// The code comes from the VALUE, never from a literal beside it: every Money on
  /// this API carries the currency it is in, and a screen that wrote "JOD" would
  /// be right until the day it was not.
  String money(Money value) => _money(value.amount, value.currencyCode);

  String moneyOf(num amount, String currencyCode) => _money(amount, currencyCode);

  String _money(num amount, String currencyCode) {
    final digits = NumberFormat.decimalPatternDigits(
      locale: 'en',
      decimalDigits: currency.minorUnits,
    ).format(amount);

    // The code trails in Arabic. Leading it there reads as an instruction rather
    // than a price.
    final text = isArabic ? '$digits $currencyCode' : '$currencyCode $digits';

    // ISOLATED, which this method said was necessary and did not do.
    //
    // A price is Latin digits beside a Latin currency code, and the bidi
    // algorithm resolves that run against whatever sits next to it. Alone in an
    // Arabic paragraph it came out right by luck; the moment anything joined it
    // — "30.000 JOD × 4 أيام" on the price breakdown — the code detached from its
    // amount and landed against the multiplication sign instead. One line read
    // "30.000 JOD" and the line under it read "JOD 120.000", on the same card.
    //
    // FSI rather than LRI: it takes its direction from the first strong
    // character, so the same wrapper is correct whichever way round the code and
    // the digits are, and it stays correct if a currency is ever written in
    // Arabic script.
    return isolate(text);
  }

  /// Wraps a run so the bidi algorithm cannot reorder it against its neighbours.
  ///
  /// U+2068 FIRST STRONG ISOLATE and U+2069 POP DIRECTIONAL ISOLATE, written as
  /// ESCAPES rather than as themselves: an invisible character in source reads as
  /// nothing at all, and the analyzer refuses it for that reason.
  ///
  /// The isolate measures zero width and travels inside the string — which is
  /// what makes it work in an interpolated sentence, where a widget-level
  /// `Directionality` cannot reach.
  static String isolate(String text) => '\u2068$text\u2069';

  /// A percentage as the server stated it: 20 renders "20%", 12.5 renders "12.5%".
  ///
  /// Trailing zeros are trimmed rather than padded, because these are rules a
  /// human chose (20%, 25%) and "20.00%" reads like a computed figure.
  ///
  /// THE SIGN BELONGS HERE, not in the message. A message that carried its own
  /// literal `%` beside a value from this method rendered "Deposit (20%%)".
  String percent(num value) {
    final text = value == value.roundToDouble()
        ? value.round().toString()
        : value.toString();
    return '$text%';
  }

  /// A day and month, Amman.
  String date(DateTime instant) =>
      DateFormat.MMMEd(locale).format(toAmman(instant));

  /// A day, month and year, for anything a customer might quote back.
  String longDate(DateTime instant) =>
      DateFormat.yMMMMd(locale).format(toAmman(instant));

  String time(DateTime instant) => DateFormat.jm(locale).format(toAmman(instant));

  String dateTime(DateTime instant) =>
      '${date(instant)} · ${time(instant)}';

  /// A rental's span, collapsing the month when both ends share one.
  String dateRange(DateTime from, DateTime to) {
    final start = toAmman(from);
    final end = toAmman(to);
    if (start.year == end.year && start.month == end.month) {
      return '${DateFormat.d(locale).format(start)} – '
          '${DateFormat.MMMd(locale).format(end)}';
    }
    return '${DateFormat.MMMd(locale).format(start)} – '
        '${DateFormat.MMMd(locale).format(end)}';
  }

  /// A `yyyy-MM-dd` the server sent, rendered without pretending it is an instant.
  ///
  /// `BookingPricing.pickupDate` is a DATE — the Amman calendar day the rental was
  /// priced from. Parsing it as a timestamp and formatting it in a zone is how a
  /// booking ends up displaying the day before the one it was priced for.
  String calendarDate(String isoDate) {
    final parts = isoDate.split('-');
    if (parts.length != 3) return isoDate;
    final year = int.tryParse(parts[0]);
    final month = int.tryParse(parts[1]);
    final day = int.tryParse(parts[2]);
    if (year == null || month == null || day == null) return isoDate;
    return DateFormat.yMMMd(locale).format(DateTime(year, month, day));
  }

  /// The platform's own `DayOfWeek` name, as a weekday in the reader's language.
  ///
  /// `DateFormat.EEEE` on an anchor date, never a full date with its separator
  /// sliced off: Arabic's date separator is U+060C (`،`) rather than a Latin
  /// comma, so splitting on `,` returned the entire date string as the day name.
  /// 1 January 2024 was a Monday, which is what the index counts from.
  ///
  /// An unrecognised name comes back unchanged — a day the platform adds later
  /// should read as itself rather than vanish from an opening-hours table.
  String weekday(String dayOfWeek) {
    final index = _weekdays.indexOf(dayOfWeek);
    if (index < 0) return dayOfWeek;
    return DateFormat.EEEE(locale).format(DateTime(2024, 1, 1 + index));
  }

  /// The API's own name for the day [instant] falls on IN AMMAN.
  ///
  /// Amman, not the phone: an office's opening hours are its own day's, and a
  /// traveller whose phone is still on another continent's clock must not be told
  /// it is shut. The name is the wire vocabulary — never shown to anybody, only
  /// matched against a schedule and then rendered through [weekday].
  String weekdayInAmman(DateTime instant) =>
      _weekdays[toAmman(instant).weekday - 1];

  /// The day names this API uses, Monday first, which is the order
  /// `DateTime.weekday` counts in.
  static const List<String> _weekdays = <String>[
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday',
    'Sunday',
  ];

  /// A `HH:mm[:ss]` opening time, without a date attached to it.
  String clock(String? isoTime) {
    if (isoTime == null || isoTime.isEmpty) return '';
    final parts = isoTime.split(':');
    if (parts.length < 2) return isoTime;
    final hour = int.tryParse(parts[0]) ?? 0;
    final minute = int.tryParse(parts[1]) ?? 0;
    return DateFormat.jm(locale).format(DateTime(2000, 1, 1, hour, minute));
  }

  /// Whole days between two instants, in AMMAN calendar terms.
  ///
  /// Used ONLY for a date picker's own bounds. It is never used to price anything:
  /// the billed day count is `BookingPricing.days`, which the server froze onto the
  /// booking, and a screen that recomputed it would contradict the invoice.
  int calendarDaysBetween(DateTime from, DateTime to) {
    final start = toAmman(from);
    final end = toAmman(to);
    final startDay = DateTime.utc(start.year, start.month, start.day);
    final endDay = DateTime.utc(end.year, end.month, end.day);
    return endDay.difference(startDay).inDays;
  }

  /// The Amman calendar day an instant falls on, as a plain local `DateTime` the
  /// Material date picker can work with.
  DateTime ammanDay(DateTime instant) {
    final amman = toAmman(instant);
    return DateTime(amman.year, amman.month, amman.day);
  }

  /// Builds the instant for a wall-clock moment in AMMAN.
  ///
  /// This is the one direction that matters for booking: a customer picks "the
  /// 14th at 09:00" meaning nine in the morning in Amman, and the server has to
  /// receive the instant that is. Constructing a local `DateTime` and calling
  /// `toUtc()` would send nine in the morning wherever the phone happens to be.
  DateTime ammanInstant(DateTime day, int hour, int minute) =>
      tz.TZDateTime(_zone, day.year, day.month, day.day, hour, minute).toUtc();
}
