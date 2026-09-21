import 'package:flutter/material.dart';

import '../../l10n/app_localizations.dart';
import '../theme/khadra_theme.dart';

/// Anything that names a dealership and knows whether it is still one.
///
/// Both `Booking` and `BookingListItem` carry the pair, and both are read by
/// screens that must not print the server's English stand-in.
abstract interface class HasDealerLabel {
  String get dealerName;
  bool get dealerRemoved;
}

/// How a booking status reads and looks.
///
/// Kept in one place because four screens render the same status and they must
/// agree. The mapping is over the platform's own status NAMES, which are part of
/// the API contract — the app compares against them and never against a sentence.
///
/// An unknown status falls through to its own name rather than to nothing: a
/// status added to the platform tomorrow should show as itself on an old build,
/// not vanish.
abstract final class BookingPresentation {
  /// The office's name, or THIS APP's words for an office that is gone.
  ///
  /// `dealerName` is a string the server always fills, and when the dealership
  /// no longer resolves it fills it with an English stand-in — deliberately, so
  /// that a shipped client which prints it raw shows something rather than
  /// nothing. `dealerRemoved` is the flag that says which it is, and a client
  /// that reads the flag is expected to word the case itself. This app reads it:
  /// an Arabic screen said "Dealer no longer on the platform" in Latin script in
  /// the middle of a sentence about a customer's own booking.
  static String dealerName(AppLocalizations l10n, HasDealerLabel booking) =>
      booking.dealerRemoved ? l10n.bookingDealerRemoved : booking.dealerName;

  static String label(AppLocalizations l10n, String status) => switch (status) {
        'Requested' => l10n.statusRequested,
        'Approved' => l10n.statusApproved,
        'Confirmed' => l10n.statusConfirmed,
        'PickedUp' => l10n.statusPickedUp,
        'Returned' => l10n.statusReturned,
        'Completed' => l10n.statusCompleted,
        'Cancelled' => l10n.statusCancelled,
        'Rejected' => l10n.statusRejected,
        'NoShow' => l10n.statusNoShow,
        'Expired' => l10n.statusExpired,
        _ => status,
      };

  static Color colour(String status) => switch (status) {
        'Requested' => KhadraColors.warn,
        'Approved' => KhadraColors.warn,
        'Confirmed' => KhadraColors.accent,
        'PickedUp' => KhadraColors.accentBright,
        'Returned' => KhadraColors.neutral600,
        'Completed' => KhadraColors.accent,
        'Cancelled' || 'Rejected' || 'NoShow' => KhadraColors.bad,
        'Expired' => KhadraColors.neutral600,
        _ => KhadraColors.neutral600,
      };

  static IconData icon(String status) => switch (status) {
        'Requested' => Icons.hourglass_empty_rounded,
        'Approved' => Icons.pending_actions_outlined,
        'Confirmed' => Icons.verified_outlined,
        'PickedUp' => Icons.directions_car_filled_outlined,
        'Returned' => Icons.assignment_turned_in_outlined,
        'Completed' => Icons.check_circle_outline,
        'Cancelled' => Icons.cancel_outlined,
        'Rejected' => Icons.do_not_disturb_on_outlined,
        'NoShow' => Icons.person_off_outlined,
        'Expired' => Icons.timer_off_outlined,
        _ => Icons.circle_outlined,
      };

  /// Who a party is, in the second person where it is the reader.
  static String party(AppLocalizations l10n, String? party) => switch (party) {
        'Customer' => l10n.bookingPartyCustomer,
        'Dealer' => l10n.bookingPartyDealer,
        'Admin' => l10n.bookingPartyAdmin,
        'System' => l10n.bookingPartySystem,
        'Unattributed' => l10n.bookingPartyUnattributed,
        _ => party ?? '',
      };

  /// A countdown to a server-sent deadline.
  ///
  /// Display only, and never a verdict. Whether a booking is still live is
  /// `isAwaitingDecision` / `isAwaitingPayment` — the server's answer against its
  /// own clock. This is what makes a phone with a skewed clock show a slightly
  /// wrong number of hours instead of showing a live booking as dead.
  /// Built from PARTS, one per unit, rather than from a sentence per shape.
  ///
  /// English abbreviates — "1h 30m left" — and needs no plural at all. Arabic
  /// spells the unit out, and spelling it out means inflecting it: one hour is
  /// ساعة واحدة, two is ساعتان, three to ten takes ساعات, and eleven upwards
  /// takes ساعة again. The old strings interpolated a digit in front of a fixed
  /// noun, which was right for the eleven-and-up case and wrong everywhere else.
  ///
  /// It stopped being a corner in 2026-09-11, when the payment window went from
  /// twenty-four hours to two: this countdown now spends its whole life in the
  /// one-and-two range, and "2 ساعة" would have been the first thing a customer
  /// read on the screen that decides whether they keep the car.
  ///
  /// The wrapper is a NOUN phrase in Arabic (المتبقي) rather than a verb, because
  /// an Arabic verb would have to agree in gender with whichever unit happened to
  /// come first — masculine for يوم, feminine for ساعة.
  static String countdown(AppLocalizations l10n, DateTime deadline) {
    final left = deadline.toUtc().difference(DateTime.now().toUtc());
    if (left.isNegative) return l10n.bookingCountdownOver;

    final String time;
    if (left.inDays >= 1) {
      time = l10n.countdownPair(
        l10n.countdownDays(left.inDays),
        l10n.countdownHours(left.inHours % 24),
      );
    } else if (left.inHours >= 1) {
      time = l10n.countdownPair(
        l10n.countdownHours(left.inHours),
        l10n.countdownMinutes(left.inMinutes % 60),
      );
    } else {
      time = l10n.countdownMinutes(left.inMinutes.clamp(0, 59));
    }
    return l10n.bookingCountdownLeft(time);
  }

  /// A "3 hours ago" for a notification feed.
  static String relative(AppLocalizations l10n, DateTime instant) {
    final elapsed = DateTime.now().toUtc().difference(instant.toUtc());
    if (elapsed.inMinutes < 1) return l10n.timeJustNow;
    if (elapsed.inMinutes < 60) return l10n.timeMinutesAgo(elapsed.inMinutes);
    if (elapsed.inHours < 24) return l10n.timeHoursAgo(elapsed.inHours);
    return l10n.timeDaysAgo(elapsed.inDays);
  }

  /// The sentence for a fuel policy, chosen from the platform's own member name.
  static String fuelPolicy(AppLocalizations l10n, String policy) => switch (policy) {
        'FullToFull' => l10n.vehicleFuelPolicyFullToFull,
        'SameToSame' => l10n.vehicleFuelPolicySameToSame,
        'Prepaid' => l10n.vehicleFuelPolicyPrepaid,
        _ => policy,
      };

  /// A cancellation reason code, in the reader's language.
  ///
  /// The app has its own wording for the codes it knows, so the reason on a
  /// customer's own booking reads naturally in their language. `/app-config` also
  /// publishes both languages, and that is the fallback for a code added to the
  /// platform after this build shipped.
  static String? cancellationReason(AppLocalizations l10n, String? code) =>
      switch (code) {
        'PlansChanged' => l10n.reasonPlansChanged,
        'FoundBetterPrice' => l10n.reasonFoundBetterPrice,
        'TravelCancelled' => l10n.reasonTravelCancelled,
        'BookedByMistake' => l10n.reasonBookedByMistake,
        'DealerUnresponsive' => l10n.reasonDealerUnresponsive,
        'Other' => l10n.reasonOther,
        _ => null,
      };
}
