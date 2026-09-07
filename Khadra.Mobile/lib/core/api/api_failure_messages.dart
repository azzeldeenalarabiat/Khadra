import '../../l10n/app_localizations.dart';
import 'api_failure.dart';

/// Turns a failure into a sentence in the reader's language.
///
/// **Mapped by CODE, never by the server's `title`.** Those titles are English —
/// the backend half of localising them is still open — so showing one puts English
/// in front of an Arabic reader, on the screen where something has just gone
/// wrong. The code is a stable contract; the sentence is the app's.
///
/// A code this version has never seen falls through to a generic message plus the
/// `traceId`, which is the honest answer: the app does not know what happened, and
/// the reference is what lets somebody find out.
extension ApiFailureMessages on ApiFailure {
  String messageFor(AppLocalizations l10n) {
    // Transport first: an offline phone has no code to map, and telling somebody
    // their password is wrong when the request never arrived is worse than saying
    // nothing useful.
    switch (kind) {
      case ApiFailureKind.offline:
        return l10n.errorOffline;
      case ApiFailureKind.timeout:
        return l10n.errorTimeout;
      case ApiFailureKind.server:
        return l10n.errorServer;
      case ApiFailureKind.rateLimited:
        return l10n.errorRateLimited;
      default:
        break;
    }

    final mapped = switch (code) {
      'auth.invalid_credentials' => l10n.errorAuthInvalidCredentials,
      'auth.email_taken' => l10n.errorAuthEmailTaken,
      'auth.phone_taken' => l10n.errorAuthPhoneTaken,
      'auth.invalid_phone' => l10n.errorAuthInvalidPhone,
      'auth.invalid_email' => l10n.errorAuthInvalidEmail,
      'auth.weak_password' => l10n.errorAuthWeakPassword,
      'auth.account_suspended' => l10n.errorAuthAccountSuspended,
      'auth.email_not_verified' => l10n.errorAuthEmailNotVerified,
      'auth.underage' => l10n.errorAuthUnderage,
      'auth.invalid_token' => l10n.errorAuthInvalidToken,
      'auth.invalid_refresh_token' => l10n.authSessionExpired,
      'booking.vehicle_unavailable' => l10n.errorBookingVehicleUnavailable,
      'booking.documents_incomplete' => l10n.errorBookingDocumentsIncomplete,
      'booking.email_not_verified' => l10n.errorBookingEmailNotVerified,
      'booking.period_in_past' => l10n.errorBookingPeriodInPast,
      'booking.not_found' => l10n.errorBookingNotFound,
      'booking.cannot_cancel' => l10n.errorBookingCannotCancel,
      'booking.non_delivery_too_early' => l10n.nonDeliveryTooEarly,
      'review.already_reviewed' => l10n.reviewAlreadyLeft,
      'review.booking_not_completed' => l10n.reviewOnlyWhenFinished,
      'dispute.already_open' => l10n.disputeExisting,
      'dispute.window_closed' => l10n.disputeCannotOpen,
      _ => null,
    };

    if (mapped != null) return mapped;

    // Codes the app has no wording for, but whose server sentence carries a figure
    // the customer needs to act on -- "a rental must start at least 2 hours from
    // now", "cannot run longer than 90 days". Those numbers are configured and the
    // app must not repeat them beside the server's, so the server's own sentence
    // is better than a generic one, even in English. Recorded as the reason the
    // backend half of item 49 matters.
    final serverSentence = title;
    if (serverSentence != null && serverSentence.isNotEmpty) return serverSentence;

    final reference = traceId;
    return reference == null || reference.isEmpty
        ? l10n.errorGeneric
        : '${l10n.errorGeneric} ${l10n.errorReference(reference)}';
  }

  /// The message for one form field, where the server named one.
  String? fieldMessage(String field) {
    for (final entry in fieldErrors.entries) {
      if (entry.key.toLowerCase() == field.toLowerCase() && entry.value.isNotEmpty) {
        return entry.value.first;
      }
    }
    return null;
  }
}
