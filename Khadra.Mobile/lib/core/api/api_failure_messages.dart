import '../../api/dtos.dart';
import '../../l10n/app_localizations.dart';
import 'api_failure.dart';

/// Turns a failure into a sentence in the reader's language.
///
/// **Mapped by CODE, never by the server's `title`.** Those titles are English —
/// the backend half of localising them is still open (pre-launch checklist item
/// 49) — so showing one puts English in front of an Arabic reader, on the screen
/// where something has just gone wrong. The code is a stable contract; the
/// sentence is the app's.
///
/// **The codes are the ones the SERVER emits**, checked against
/// `*Errors.cs`. Three in this map were not: `auth.weak_password`,
/// `auth.underage` and `dispute.window_closed` are codes this platform has never
/// emitted, so the three failures they covered — a password the policy refuses,
/// an under-age registration, and a booking past its dispute window — fell
/// through to the server's English every time, including for Arabic readers. The
/// real codes are `auth.password_policy`, `auth.under_minimum_age` and
/// `dispute.booking_not_disputable`. A typo in a map is invisible until somebody
/// reads the screen it breaks, which is why the audit is worth repeating whenever
/// a code is added.
///
/// [config] is optional and carries the three figures a few messages need — the
/// lead time, the booking horizon, the longest rental. Those are the platform's
/// own published numbers, so quoting them is rendering the server's answer rather
/// than holding a second copy of a rule. Without it those codes still get a
/// sentence, just a less specific one.
///
/// A code this version has never seen falls through to the server's own sentence
/// where there is one — English, but carrying a figure the customer needs — and
/// otherwise to a generic message plus the `traceId`, which is the honest answer:
/// the app does not know what happened, and the reference is what lets somebody
/// find out.
extension ApiFailureMessages on ApiFailure {
  String messageFor(AppLocalizations l10n, {AppConfig? config}) {
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

    final mapped = _byCode(l10n, config);
    if (mapped != null) return mapped;

    // A code this build has no wording for. The server's sentence is English and
    // is still better than a generic one when it carries a figure the customer
    // has to act on.
    final serverSentence = title;
    if (serverSentence != null && serverSentence.isNotEmpty) return serverSentence;

    final reference = traceId;
    return reference == null || reference.isEmpty
        ? l10n.errorGeneric
        : '${l10n.errorGeneric} ${l10n.errorReference(reference)}';
  }

  String? _byCode(AppLocalizations l10n, AppConfig? config) => switch (code) {
        // ── Identity ─────────────────────────────────────────────────────────
        'auth.invalid_credentials' => l10n.errorAuthInvalidCredentials,
        'auth.email_taken' => l10n.errorAuthEmailTaken,
        'auth.phone_taken' => l10n.errorAuthPhoneTaken,
        'auth.invalid_phone' => l10n.errorAuthInvalidPhone,
        'auth.invalid_email' => l10n.errorAuthInvalidEmail,
        'auth.invalid_name' => l10n.errorAuthInvalidName,
        'auth.password_policy' => l10n.errorAuthWeakPassword,
        'auth.password_unchanged' => l10n.errorAuthPasswordUnchanged,
        'auth.account_suspended' => l10n.errorAuthAccountSuspended,
        'auth.email_not_verified' => l10n.errorAuthEmailNotVerified,
        'auth.date_of_birth_required' => l10n.validationDateOfBirth,
        'auth.invalid_date_of_birth' => l10n.errorAuthInvalidDateOfBirth,
        // The minimum is the platform's, published on /app-config, so the
        // sentence names it rather than saying "you are too young" and leaving
        // somebody to guess by how much. Null is a real state — the owner has set
        // no limit — and in that case the server would not have refused, so the
        // figure-less sentence is the safe read.
        'auth.under_minimum_age' => switch (config?.minimumRenterAge) {
            final age? => l10n.errorAuthUnderageBy(age),
            null => l10n.errorAuthUnderage,
          },
        'auth.invalid_token' => l10n.errorAuthInvalidToken,
        'auth.invalid_refresh_token' => l10n.authSessionExpired,
        // The account exists; the message did not go out. Different from a
        // refusal, and the remedy is to ask again rather than to correct
        // anything.
        'auth.verification_email_not_sent' ||
        'auth.password_reset_email_not_sent' =>
          l10n.authEmailNotDelivered,

        // ── Booking ──────────────────────────────────────────────────────────
        'booking.vehicle_unavailable' => l10n.errorBookingVehicleUnavailable,
        'booking.documents_incomplete' => l10n.errorBookingDocumentsIncomplete,
        'booking.email_not_verified' => l10n.errorBookingEmailNotVerified,
        'booking.period_in_past' => l10n.errorBookingPeriodInPast,
        'booking.not_found' => l10n.errorBookingNotFound,
        'booking.cannot_cancel' => l10n.errorBookingCannotCancel,
        'booking.non_delivery_too_early' => l10n.nonDeliveryTooEarly,
        'booking.account_cannot_book' => l10n.errorBookingAccountCannotBook,
        'booking.not_a_party' => l10n.errorBookingNotYours,
        'booking.already_finished' => l10n.errorBookingAlreadyFinished,
        'booking.decision_window_elapsed' => l10n.bookingExpiredTitle,
        'booking.not_awaiting_payment' => l10n.errorBookingNotAwaitingPayment,
        'booking.dispute_open' => l10n.errorBookingDisputeOpen,
        'booking.reason_required' => l10n.validationChooseReason,

        // The three period bounds. Each is a figure /app-config publishes, so the
        // app can state it in Arabic instead of falling through to the server's
        // English.
        'booking.too_soon' => config == null
            ? l10n.errorBookingTooSoon
            : l10n.errorBookingTooSoonBy(config.minimumBookingLeadTimeMinutes),
        'booking.rental_too_long' => config == null
            ? l10n.errorBookingRentalTooLong
            : l10n.searchMaxRentalDays(config.maxRentalDays),
        'booking.beyond_horizon' => config == null
            ? l10n.errorBookingBeyondHorizon
            : l10n.errorBookingBeyondHorizonBy(config.maxAdvanceBookingDays),

        // The server's sentence for these two names the gallery's own schedule,
        // in English. The app cannot reproduce that string, but it does not need
        // to: the gallery's opening hours are on its own page, and pointing there
        // is more use than a translated timetable.
        'booking.pickup_outside_opening_hours' ||
        'booking.return_outside_opening_hours' =>
          l10n.errorBookingOutsideOpeningHours,

        // ── Delivery ─────────────────────────────────────────────────────────
        'booking.delivery_out_of_range' => l10n.errorBookingDeliveryOutOfRange,
        'booking.delivery_location_required' =>
          l10n.errorBookingDeliveryLocationRequired,
        'booking.vehicle_not_delivery_eligible' => l10n.bookDeliveryNotForThisCar,

        // ── Documents ────────────────────────────────────────────────────────
        'documents.too_large' => l10n.errorDocumentTooLarge,
        'documents.unsupported_type' ||
        'documents.invalid_content' =>
          l10n.errorDocumentUnsupportedType,
        'documents.not_found' => l10n.errorDocumentNotFound,

        // ── Reviews ──────────────────────────────────────────────────────────
        'review.already_reviewed' => l10n.reviewAlreadyLeft,
        'review.booking_not_completed' => l10n.reviewOnlyWhenFinished,
        'review.window_closed' ||
        'review.edit_window_closed' =>
          l10n.errorReviewWindowClosed,
        'review.invalid_rating' => l10n.errorReviewInvalidRating,
        'review.comment_too_long' => l10n.errorReviewCommentTooLong,

        // ── Disputes ─────────────────────────────────────────────────────────
        'dispute.already_open' => l10n.disputeExisting,
        'dispute.booking_not_disputable' => l10n.disputeCannotOpen,
        'dispute.not_found' => l10n.errorDisputeNotFound,
        'dispute.not_open' ||
        'dispute.already_resolved' ||
        'dispute.already_withdrawn' =>
          l10n.errorDisputeNotOpen,
        'dispute.only_opener_can_withdraw' =>
          l10n.errorDisputeNotYoursToWithdraw,
        'dispute.reason_required' => l10n.errorDisputeReasonRequired,
        'dispute.statement_required' => l10n.errorDisputeStatementRequired,
        'dispute.invalid_evidence_type' => l10n.errorDocumentUnsupportedType,
        'dispute.evidence_not_uploaded' ||
        'dispute.evidence_outside_booking' =>
          l10n.errorDisputeEvidenceFailed,

        // ── Shortlist ────────────────────────────────────────────────────────
        'shortlist.full' => l10n.errorShortlistFull,
        // The catalogue answers the same to a draft, a hidden car, one in
        // maintenance, a suspended gallery's and an unknown id, so the sentence
        // says the one thing true of all of them.
        'shortlist.vehicle_not_found' => l10n.errorShortlistVehicleNotFound,

        // ── Payments ─────────────────────────────────────────────────────────
        //
        // The booking screen normally shows this as a NOTICE rather than an
        // error, from `payment.canPay` — but the checkout call can still answer
        // 503 in the gap between reading the booking and pressing the button.
        'payments.provider_unavailable' ||
        'payments.not_live' =>
          l10n.bookingPaymentNotAvailableBody,
        'payments.provider_refused' => l10n.errorPaymentRefused,

        _ => null,
      };

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
