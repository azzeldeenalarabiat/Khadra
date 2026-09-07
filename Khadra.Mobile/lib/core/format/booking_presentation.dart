import 'package:flutter/material.dart';

import '../../l10n/app_localizations.dart';
import '../theme/khadra_theme.dart';

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
  static String countdown(AppLocalizations l10n, DateTime deadline) {
    final left = deadline.toUtc().difference(DateTime.now().toUtc());
    if (left.isNegative) return l10n.bookingCountdownOver;

    if (left.inDays >= 1) {
      return l10n.bookingCountdownDays(left.inDays, left.inHours % 24);
    }
    if (left.inHours >= 1) {
      return l10n.bookingCountdownHours(left.inHours, left.inMinutes % 60);
    }
    return l10n.bookingCountdownMinutes(left.inMinutes.clamp(0, 59));
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
