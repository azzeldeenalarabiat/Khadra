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

    // The code trails in both languages. Leading it in Arabic reads as an
    // instruction rather than a price, and mixing a Latin code into an RTL run
    // without an isolate makes the digits jump.
    return isArabic ? '$digits $currencyCode' : '$currencyCode $digits';
  }

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
