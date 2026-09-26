import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';
import 'booking_providers.dart';

/// What a cancellation actually did.
///
/// [expired] is not a failure. If the window closed while the customer was
/// deciding, the clock had already ended the booking and the platform had released
/// the car; the server records an expiry rather than writing "you cancelled this"
/// over an outcome that was no longer anyone's to decide. The customer is told
/// which happened, because those are different things to be told.
enum CancelOutcome { cancelled, expired }

/// The cancellation sheet.
///
/// The consequence shown here is the SERVER's — `booking.cancellation`, computed
/// from this booking's own frozen terms, sharing the same code path as the real
/// cancellation. So the figure on this sheet is the figure that gets recorded, and
/// nothing on the phone multiplies a percentage by an amount to find it.
Future<CancelOutcome?> showCancelBookingSheet({
  required BuildContext context,
  required Booking booking,
}) =>
    showModalBottomSheet<CancelOutcome>(
      context: context,
      isScrollControlled: true,
      builder: (_) => Padding(
        padding: EdgeInsets.only(
          bottom: MediaQuery.of(context).viewInsets.bottom,
        ),
        child: _CancelSheet(booking: booking),
      ),
    );

class _CancelSheet extends ConsumerStatefulWidget {
  const _CancelSheet({required this.booking});

  final Booking booking;

  @override
  ConsumerState<_CancelSheet> createState() => _CancelSheetState();
}

class _CancelSheetState extends ConsumerState<_CancelSheet> {
  final _details = TextEditingController();
  String? _reasonCode;
  bool _busy = false;
  String? _error;

  /// The refund changed while the sheet was open (owner, 2026-09-26): the
  /// server's new figure, shown until the customer confirms again.
  Money? _refundChangedTo;

  /// The booking as last read. The server's re-read replaces it after a
  /// changed refund, so the sheet states the new consequence before a second tap.
  Booking _booking(WidgetRef ref) =>
      ref.watch(bookingProvider(widget.booking.bookingId)).valueOrNull ?? widget.booking;

  @override
  void dispose() {
    _details.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final l10n = AppLocalizations.of(context);
    final code = _reasonCode;

    if (code == null) {
      setState(() => _error = l10n.validationChooseReason);
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    // The refund the sheet showed. After a changed refund it is the server's
    // new figure: that is what the customer is now agreeing to.
    final preview = _booking(ref).cancellation;
    final shown = _refundChangedTo?.amount ??
        (preview.publishesRefundAmount ? (preview.refundAmount?.amount ?? 0) : null);

    try {
      final updated = await ref.read(apiProvider).cancelBooking(
            widget.booking.bookingId,
            reasonCode: code,
            details: _details.text.trim().isEmpty ? null : _details.text.trim(),
            expectedRefund: shown,
          );

      invalidateBookings(ref, bookingId: widget.booking.bookingId);

      if (!mounted) return;
      Navigator.of(context).pop(
        // The server may have expired it instead. Reading the status it sends
        // back rather than assuming the one that was asked for is what lets the
        // customer be told "the time ran out" instead of "you cancelled".
        updated.status == 'Expired'
            ? CancelOutcome.expired
            : CancelOutcome.cancelled,
      );
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      // Nothing was cancelled: the refund moved on. Say to what, and read the
      // booking again so the rest of the sheet states the new consequence.
      final changed = failure.hasCode('booking.refund_changed')
          ? Money.maybe(failure.extensions['currentRefund'])
          : null;
      if (changed != null) invalidateBookings(ref, bookingId: widget.booking.bookingId);
      setState(() {
        _busy = false;
        _refundChangedTo = changed;
        _error = changed == null ? failure.messageFor(l10n) : null;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final arabic = ref.watch(isArabicProvider);
    final formats = ref.watch(formatsProvider);
    final reasons =
        ref.watch(appConfigProvider).valueOrNull?.vocabularies.cancellationReasons ??
            const <VocabularyEntry>[];

    final booking = _booking(ref);
    final preview = booking.cancellation;
    // Everything above the deposit, promised after the free window (Phase 3).
    // Inside it the free-cancellation sentence already names the whole figure.
    final aboveDeposit = !preview.willRefundDeposit &&
            preview.refundAmount != null &&
            !preview.refundAmount!.isZero
        ? preview.refundAmount
        : null;

    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(Space.lg, 0, Space.lg, Space.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    l10n.cancelTitle,
                    style: const TextStyle(
                        fontSize: 18, fontWeight: FontWeight.w800),
                  ),
                ),
                IconButton(
                  onPressed: _busy ? null : () => Navigator.of(context).pop(),
                  icon: const Icon(Icons.close),
                  tooltip: l10n.actionClose,
                ),
              ],
            ),
            const SizedBox(height: Space.lg),

            if (_refundChangedTo case final changed? when formats != null) ...[
              KhadraNotice(
                title: changed.isZero
                    ? l10n.cancelRefundChangedNone
                    : l10n.cancelRefundChanged(formats.money(changed)),
                tone: NoticeTone.warn,
                icon: Icons.sync_problem_outlined,
              ),
              const SizedBox(height: Space.md),
            ],
            if (aboveDeposit != null && formats != null) ...[
              KhadraNotice(
                title: l10n.cancelRefundAboveDeposit(formats.money(aboveDeposit)),
                tone: NoticeTone.accent,
              ),
              const SizedBox(height: Space.md),
            ],

            // The consequence, from the server, before the tap rather than after.
            // A booking paid in full promises the server's refund figure — the
            // whole payment, less a fee taken as non-refundable — never "your
            // deposit" (owner, 2026-09-25).
            if (preview.willRefundDeposit)
              KhadraNotice(
                title: switch ((booking.confirmingPayment, formats)) {
                  (final payment?, final formats?) when payment.isFullPayment =>
                    l10n.cancelFreeRefundPaymentNotice(formats.money(payment.refundOnFreeCancellation)),
                  _ => l10n.cancelFreeRefundNotice,
                },
                tone: NoticeTone.accent,
              )
            else if (preview.isFree)
              KhadraNotice(
                title: l10n.cancelFreeNotice,
                tone: NoticeTone.accent,
              )
            else if (preview.penalty != null && formats != null) ...[
              KhadraNotice(
                title: l10n.cancelPenaltyNotice(
                    formats.money(preview.penalty!.maxAmount)),
                tone: NoticeTone.warn,
              ),
              if (preview.penalty!.requiresTicketToEnforce) ...[
                const SizedBox(height: Space.sm),
                Text(
                  l10n.bookingPenaltyNotCharged,
                  style: const TextStyle(
                      color: KhadraColors.neutral600, fontSize: 12, height: 1.45),
                ),
              ],
            ],

            const SizedBox(height: Space.xl),
            Text(
              l10n.cancelReasonQuestion,
              style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: Space.md),

            // A closed list, and its words are the PLATFORM's in both languages.
            // A required free-text box produces "asdf"; a code is something the
            // gallery and the owner can count.
            Wrap(
              spacing: Space.sm,
              runSpacing: Space.sm,
              children: [
                for (final reason in reasons)
                  KhadraChoiceChip(
                    label: reason.labelFor(arabic),
                    selected: _reasonCode == reason.name,
                    onTap: _busy
                        ? () {}
                        : () => setState(() {
                              _reasonCode = reason.name;
                              _error = null;
                            }),
                  ),
              ],
            ),

            const SizedBox(height: Space.lg),
            KhadraField(
              controller: _details,
              label: l10n.cancelDetailsLabel,
              hint: l10n.cancelDetailsHint,
              maxLines: 3,
              maxLength: 500,
              enabled: !_busy,
            ),

            if (_error != null) ...[
              KhadraNotice(title: _error!, tone: NoticeTone.bad),
              const SizedBox(height: Space.lg),
            ],

            KhadraSubmitButton(
              label: l10n.cancelConfirm,
              busy: _busy,
              onPressed: _submit,
            ),
            const SizedBox(height: Space.sm),
            TextButton(
              onPressed: _busy ? null : () => Navigator.of(context).pop(),
              child: Text(l10n.cancelKeep),
            ),
          ],
        ),
      ),
    );
  }
}
