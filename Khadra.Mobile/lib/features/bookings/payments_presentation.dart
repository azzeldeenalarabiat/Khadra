import 'package:flutter/material.dart';

import '../../api/dtos.dart';
import '../../core/format/formats.dart';
import '../../core/theme/khadra_theme.dart';
import '../../l10n/app_localizations.dart';

/// The booking's "Payments" section in words (payments Phase 4, owner
/// 2026-09-26). Every figure, state and status is the server's financial state;
/// these only choose sentences and never add, subtract or compare two amounts.

/// Where the deposit is, as one sentence; null when there is nothing to say.
String? depositSentence(AppLocalizations l10n, Formats formats, FinancialDeposit deposit) {
  final amount = deposit.amount == null ? '' : formats.money(deposit.amount!);
  final date = deposit.windowEndsAt == null ? '' : formats.dateTime(deposit.windowEndsAt!);
  return switch (deposit.state) {
    'Held' => l10n.paymentsDepositHeld(amount),
    'AppliedToRental' => l10n.paymentsDepositAppliedToRental(amount),
    'InSettlementWindow' => l10n.paymentsDepositInSettlementWindow(amount, date),
    'UnderDispute' => l10n.paymentsDepositUnderDispute(amount),
    'SettledWithRental' => l10n.paymentsDepositSettledWithRental(amount),
    'ReturnedWithPayment' => l10n.paymentsDepositReturnedWithPayment,
    'HeldUntilWindowCloses' => l10n.paymentsDepositHeldUntilWindowCloses(amount, date),
    'HeldForAssessedPenalty' => l10n.paymentsDepositHeldForAssessedPenalty(amount, date),
    // Pre-launch item 164, the owner's own sentence: promising nothing to either side.
    'HeldUnresolved' => l10n.paymentsDepositHeldUnresolved,
    'Released' => l10n.paymentsDepositReleased(amount),
    'DecidedByDispute' => _decided(l10n, formats, deposit, amount),
    _ => null,
  };
}

/// Only the customer's OWN share of a dispute decision is named (owner, 2026-09-26).
String _decided(AppLocalizations l10n, Formats formats, FinancialDeposit deposit, String amount) {
  final share = deposit.toCustomer;
  return share != null && !share.isZero
      ? l10n.paymentsDepositDecidedByDispute(formats.money(share), amount)
      : l10n.paymentsDepositDecidedByDisputeNothing(amount);
}

/// What a payment in the history is called.
String paymentTitle(AppLocalizations l10n, FinancialPayment payment) => payment.isOrphaned
    ? l10n.paymentsKindOrphaned
    : switch (payment.purpose) {
        'Deposit' => l10n.paymentsKindDeposit,
        'FullPayment' => l10n.paymentsKindFullPayment,
        _ => l10n.paymentsKindOther,
      };

/// A payment's refunds read as one status, with its badge colour. Null for a
/// progress this build does not know: the codes may grow, and a newer one is
/// left unsaid rather than read as "paid".
({String label, Color colour})? paymentProgress(AppLocalizations l10n, String progress) => switch (progress) {
      'None' => (label: l10n.bookingDepositPaidNote, colour: KhadraColors.ok),
      'InProgress' => (label: l10n.bookingRefundOutstanding, colour: KhadraColors.warn),
      'Delayed' => (label: l10n.bookingRefundDelayed, colour: KhadraColors.bad),
      'Partial' => (label: l10n.paymentsPartlyRefunded, colour: KhadraColors.neutral600),
      'Complete' => (label: l10n.bookingRefunded, colour: KhadraColors.neutral600),
      _ => null,
    };

/// The key the section re-reads on: the booking's money-relevant facts as the
/// screen last read them. The figures come from the server; this only says
/// when they may have moved.
String paymentsVersion(Booking booking) => [
      booking.status,
      booking.depositPaid,
      booking.isPaidInFull,
      for (final refund in booking.refunds ?? const <Refund>[]) '${refund.refundId}:${refund.status}',
      booking.handovers.length,
      booking.liveDisputeId ?? '',
    ].join('|');
