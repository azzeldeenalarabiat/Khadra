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

    try {
      final updated = await ref.read(apiProvider).cancelBooking(
            widget.booking.bookingId,
            reasonCode: code,
            details: _details.text.trim().isEmpty ? null : _details.text.trim(),
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
      setState(() {
        _busy = false;
        _error = failure.messageFor(l10n);
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

    final preview = widget.booking.cancellation;

    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(Space.xl),
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
                        fontSize: 18, fontWeight: FontWeight.w700),
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

            // The consequence, from the server, before the tap rather than after.
            if (preview.isFree)
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
                  ChoiceChip(
                    label: Text(reason.labelFor(arabic)),
                    selected: _reasonCode == reason.name,
                    onSelected: _busy
                        ? null
                        : (_) => setState(() {
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
