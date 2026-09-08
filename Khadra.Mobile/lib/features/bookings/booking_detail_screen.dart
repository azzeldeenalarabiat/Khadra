import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/booking_presentation.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';
import 'booking_providers.dart';
import 'cancel_booking_sheet.dart';

/// One booking, in full.
///
/// Two rules run through this screen:
///
/// **Liveness is the server's verdict, never a clock comparison.** A request past
/// its decision deadline is over — the car went back on the market at that instant
/// — but the row still reads `Requested` until the settlement pass reaches it. The
/// app renders `isAwaitingDecision` and `isAwaitingPayment` rather than comparing
/// a deadline against a phone's clock, which on a device with the wrong time would
/// show a dead booking as live, or the reverse. The countdown IS a clock
/// comparison, and it is display only.
///
/// **The terms shown are the ones this booking froze.** Not today's settings. A
/// screen that showed the current deposit percentage against last month's booking
/// would be wrong in exactly the way the freezing exists to prevent.
class BookingDetailScreen extends ConsumerStatefulWidget {
  const BookingDetailScreen({super.key, required this.bookingId});

  final String bookingId;

  @override
  ConsumerState<BookingDetailScreen> createState() =>
      _BookingDetailScreenState();
}

class _BookingDetailScreenState extends ConsumerState<BookingDetailScreen> {
  Timer? _tick;

  @override
  void initState() {
    super.initState();
    // The countdown is rendered from the server's deadline; this only repaints it.
    _tick = Timer.periodic(const Duration(seconds: 30), (_) {
      if (mounted) setState(() {});
    });
  }

  @override
  void dispose() {
    _tick?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final booking = ref.watch(bookingProvider(widget.bookingId));
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(
        leading: const KhadraBack(fallback: Routes.bookings),
        title: Text(l10n.bookingsTitle),
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          invalidateBookings(ref, bookingId: widget.bookingId);
          await ref.read(bookingProvider(widget.bookingId).future);
        },
        child: switch (booking) {
          AsyncLoading() => const KhadraLoading(),
          AsyncError(:final error) => KhadraError(
              message: ApiFailure.from(error).messageFor(l10n),
              onRetry: () => ref.invalidate(bookingProvider(widget.bookingId)),
            ),
          AsyncData(:final value) when formats != null =>
            _Body(booking: value, formats: formats),
          _ => const KhadraLoading(),
        },
      ),
    );
  }
}

class _Body extends ConsumerWidget {
  const _Body({required this.booking, required this.formats});

  final Booking booking;
  final Formats formats;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);

    return ListView(
      padding: const EdgeInsets.fromLTRB(
          Space.lg, Space.lg, Space.lg, Space.bottomInset),
      children: [
        _Header(booking: booking, formats: formats),

        const SizedBox(height: Space.lg),
        _StateNotice(booking: booking, formats: formats),

        const SizedBox(height: Space.xl),
        _Actions(booking: booking),

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.bookingCar),
        _VehicleCard(booking: booking),

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.bookingPrice),
        _Price(booking: booking, formats: formats),

        if (booking.penalty != null && !booking.penalty!.isNothingOwed) ...[
          const SizedBox(height: Space.lg),
          _Penalty(penalty: booking.penalty!, formats: formats),
        ],

        if (booking.handovers.isNotEmpty) ...[
          const SizedBox(height: Space.xl),
          KhadraSectionTitle(l10n.bookingHistory),
          _Handovers(handovers: booking.handovers, formats: formats),
        ],

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.bookingHistory),
        _Timeline(booking: booking, formats: formats),
      ],
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({required this.booking, required this.formats});

  final Booking booking;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return KhadraCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              KhadraBadge(
                label: BookingPresentation.label(l10n, booking.status),
                colour: BookingPresentation.colour(booking.status),
                icon: BookingPresentation.icon(booking.status),
              ),
              const Spacer(),
              LatinRun(
                booking.reference,
                style: const TextStyle(
                  fontSize: 12,
                  color: KhadraColors.neutral500,
                  letterSpacing: 0.4,
                ),
              ),
            ],
          ),
          const SizedBox(height: Space.md),
          KhadraDetailRow(
            label: l10n.bookingWhen,
            value: Text(
              formats.dateRange(booking.periodStart, booking.periodEnd),
            ),
          ),
          KhadraDetailRow(
            label: l10n.searchPickup,
            value: Text(formats.dateTime(booking.periodStart)),
          ),
          KhadraDetailRow(
            label: l10n.searchReturn,
            value: Text(formats.dateTime(booking.periodEnd)),
          ),
          KhadraDetailRow(
            label: l10n.bookingWhere,
            value: Text(booking.isDelivery
                ? l10n.bookingWhereDelivery
                : l10n.bookingWhereSelfPickup),
          ),
          KhadraDetailRow(
            label: l10n.bookingGallery,
            value: Text(booking.dealerName),
          ),
          const SizedBox(height: Space.xs),
          Text(
            l10n.timeAmmanNote,
            style: const TextStyle(color: KhadraColors.neutral500, fontSize: 11),
          ),
        ],
      ),
    );
  }
}

/// The one thing the customer most needs to know right now.
class _StateNotice extends StatelessWidget {
  const _StateNotice({required this.booking, required this.formats});

  final Booking booking;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    // Server verdicts, in order of what matters most to the reader.
    if (booking.isAwaitingDecision) {
      return KhadraNotice(
        title: l10n.bookingAwaitingDecisionTitle(booking.dealerName),
        body: '${l10n.bookingAwaitingDecisionBody(formats.dateTime(booking.decisionDeadline))}\n'
            '${BookingPresentation.countdown(l10n, booking.decisionDeadline)}',
        tone: NoticeTone.warn,
        icon: Icons.hourglass_bottom_outlined,
      );
    }

    if (booking.isAwaitingPayment) {
      return _PaymentDue(booking: booking, formats: formats);
    }

    return switch (booking.status) {
      'Expired' => KhadraNotice(
          title: l10n.bookingExpiredTitle,
          body: l10n.bookingExpiredBody,
          tone: NoticeTone.neutral,
          icon: Icons.timer_off_outlined,
        ),
      'Rejected' => KhadraNotice(
          title: l10n.bookingRejectedTitle,
          body: _rejectionReason(l10n),
          tone: NoticeTone.bad,
        ),
      'Cancelled' => KhadraNotice(
          title: l10n.bookingCancelledTitle,
          body: _cancellationReason(l10n),
          tone: NoticeTone.neutral,
          icon: Icons.cancel_outlined,
        ),
      'NoShow' => KhadraNotice(
          title: l10n.bookingNoShowTitle,
          tone: NoticeTone.bad,
          icon: Icons.person_off_outlined,
        ),
      'Completed' => KhadraNotice(
          title: l10n.bookingCompletedTitle,
          tone: NoticeTone.accent,
        ),
      // Approved but no longer awaiting payment, or Confirmed / PickedUp /
      // Returned: the status badge in the header already says it, and a second
      // block repeating it would be noise.
      _ => const SizedBox.shrink(),
    };
  }

  /// The gallery's reason, in the READER's language.
  ///
  /// The code is stored on the status change and the sentence is chosen here.
  /// Until 2026-09-08 the platform composed an English sentence into the row, so
  /// an Arabic-speaking customer read English on their own booking.
  String? _rejectionReason(AppLocalizations l10n) {
    for (final change in booking.history.reversed) {
      if (change.toStatus != 'Rejected') continue;
      final typed = change.reason;
      return typed == null || typed.isEmpty ? null : typed;
    }
    return null;
  }

  String? _cancellationReason(AppLocalizations l10n) {
    final label =
        BookingPresentation.cancellationReason(l10n, booking.cancellationReasonCode);
    final party = BookingPresentation.party(l10n, booking.cancelledBy);
    final typed = booking.cancellationReason;

    final parts = <String>[
      if (booking.cancelledBy != null) l10n.bookingCancelledBy(party),
      if (label != null) label,
      if (typed != null && typed.isNotEmpty) typed,
    ];
    return parts.isEmpty ? null : parts.join(' · ');
  }
}

/// The payment step, told honestly.
///
/// **There is no Pay button, because there is nothing behind one.** The Payments
/// context is not built and is blocked on owner decisions, so this booking will
/// expire at the deadline and the car will go back on the market. Showing a
/// disabled button, or a "pay at the counter" that the domain does not support,
/// would be worse than saying so: the deposit is what makes a booking a booking,
/// and no deposit is taken out of band.
///
/// The amount and the deadline come from the BOOKING — never from
/// `/app-config`'s current payment window, which is today's setting and not the
/// one this booking froze.
class _PaymentDue extends StatelessWidget {
  const _PaymentDue({required this.booking, required this.formats});

  final Booking booking;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final deadline = booking.paymentDeadline;

    return Column(
      children: [
        KhadraNotice(
          title: l10n.bookingAwaitingPaymentTitle(
              formats.money(booking.pricing.depositAmount)),
          body: deadline == null
              ? null
              : '${l10n.bookingAwaitingPaymentBy(formats.dateTime(deadline))}\n'
                  '${BookingPresentation.countdown(l10n, deadline)}',
          tone: NoticeTone.warn,
          icon: Icons.payments_outlined,
        ),
        const SizedBox(height: Space.md),
        KhadraNotice(
          title: l10n.bookingPaymentNotAvailableTitle,
          body: l10n.bookingPaymentNotAvailableBody,
          tone: NoticeTone.neutral,
          icon: Icons.credit_card_off_outlined,
        ),
      ],
    );
  }
}

/// What a customer may do with this booking right now.
///
/// Every button here is gated on a SERVER flag — `cancellation.canCancel`,
/// `canBeDisputed`, `canBeReviewed` — rather than on a status the app interprets.
/// A control that finds out it cannot work by being refused has already wasted the
/// tap, and on a phone that is the difference between a decision and a dead end.
class _Actions extends ConsumerWidget {
  const _Actions({required this.booking});

  final Booking booking;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final actions = <Widget>[];

    if (booking.cancellation.canCancel) {
      actions.add(
        OutlinedButton.icon(
          onPressed: () => _cancel(context, ref),
          icon: const Icon(Icons.cancel_outlined, size: 18),
          label: Text(l10n.cancelTitle),
          style: OutlinedButton.styleFrom(
            foregroundColor: KhadraColors.bad,
            side: const BorderSide(color: KhadraColors.bad),
          ),
        ),
      );
    }

    // Spec 5.5. Gated on the SERVER's verdict, not on the status: the gallery gets
    // a grace period after the agreed start before it can be called a no-show, that
    // grace is frozen per booking, and only the server knows whether it has run out.
    // Until it has, the screen says WHEN rather than offering a refusable button.
    if (booking.canReportNonDelivery) {
      actions.add(
        OutlinedButton.icon(
          onPressed: () => _reportNonDelivery(context, ref),
          icon: const Icon(Icons.report_gmailerrorred_outlined, size: 18),
          label: Text(l10n.nonDeliveryTitle),
        ),
      );
    } else if (booking.status == 'Confirmed') {
      actions.add(
        Tooltip(
          // Falls back to the general sentence when the config has not arrived and
          // there is no formatter yet: a tooltip without a time still says why the
          // button is off, which is more than a bare disabled control does.
          message: switch (ref.watch(formatsProvider)) {
            final formats? =>
              l10n.nonDeliveryNotYet(formats.dateTime(booking.nonDeliveryReportableFrom)),
            null => l10n.nonDeliveryTooEarly,
          },
          child: OutlinedButton.icon(
            onPressed: null,
            icon: const Icon(Icons.report_gmailerrorred_outlined, size: 18),
            label: Text(l10n.nonDeliveryTitle),
          ),
        ),
      );
    }

    if (booking.liveDisputeId != null) {
      actions.add(
        OutlinedButton.icon(
          onPressed: () => context.push(Routes.dispute(booking.liveDisputeId!)),
          icon: const Icon(Icons.gavel_outlined, size: 18),
          label: Text(l10n.disputeView),
        ),
      );
    } else if (booking.canBeDisputed) {
      actions.add(
        OutlinedButton.icon(
          onPressed: () => context.push(Routes.openDispute(booking.bookingId)),
          icon: const Icon(Icons.gavel_outlined, size: 18),
          label: Text(l10n.disputeTitle),
        ),
      );
    }

    if (booking.canBeReviewed) {
      final reviewed = booking.myReviewId != null;
      actions.add(
        FilledButton.icon(
          onPressed: reviewed
              ? null
              : () => context.push(Routes.review(booking.bookingId)),
          icon: const Icon(Icons.star_outline_rounded, size: 18),
          label: Text(reviewed ? l10n.reviewAlreadyLeft : l10n.reviewTitle),
        ),
      );
    }

    if (actions.isEmpty) return const SizedBox.shrink();

    return Column(
      children: [
        for (final action in actions)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.sm),
            child: SizedBox(width: double.infinity, child: action),
          ),
      ],
    );
  }

  Future<void> _cancel(BuildContext context, WidgetRef ref) async {
    final l10n = AppLocalizations.of(context);
    final outcome = await showCancelBookingSheet(
      context: context,
      booking: booking,
    );

    if (outcome == null || !context.mounted) return;

    showKhadraMessage(
      context,
      // "You cancelled this" and "the time ran out" are different things to be
      // told, and the server decides which happened.
      outcome == CancelOutcome.expired
          ? l10n.cancelExpiredInstead
          : l10n.cancelDone,
    );
  }

  Future<void> _reportNonDelivery(BuildContext context, WidgetRef ref) async {
    final l10n = AppLocalizations.of(context);
    final details = await showDialog<String>(
      context: context,
      builder: (_) => const _NonDeliveryDialog(),
    );

    if (details == null || !context.mounted) return;

    try {
      await ref.read(apiProvider).reportNonDelivery(booking.bookingId, details);
      invalidateBookings(ref, bookingId: booking.bookingId);
      if (context.mounted) showKhadraMessage(context, l10n.nonDeliveryDone);
    } on ApiFailure catch (failure) {
      if (context.mounted) {
        showKhadraMessage(context, failure.messageFor(l10n), isError: true);
      }
    }
  }
}

class _NonDeliveryDialog extends StatefulWidget {
  const _NonDeliveryDialog();

  @override
  State<_NonDeliveryDialog> createState() => _NonDeliveryDialogState();
}

class _NonDeliveryDialogState extends State<_NonDeliveryDialog> {
  final _details = TextEditingController();

  @override
  void dispose() {
    _details.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return AlertDialog(
      title: Text(l10n.nonDeliveryTitle),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            l10n.nonDeliveryBody,
            style: const TextStyle(fontSize: 14, height: 1.5),
          ),
          const SizedBox(height: Space.lg),
          KhadraField(
            controller: _details,
            label: l10n.nonDeliveryDetails,
            maxLines: 4,
            maxLength: 1000,
          ),
        ],
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.actionCancel),
        ),
        FilledButton(
          onPressed: _details.text.trim().isEmpty
              ? null
              : () => Navigator.of(context).pop(_details.text.trim()),
          child: Text(l10n.nonDeliveryReport),
        ),
      ],
    );
  }
}

class _VehicleCard extends StatelessWidget {
  const _VehicleCard({required this.booking});

  final Booking booking;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final vehicle = booking.vehicle;

    return KhadraCard(
      onTap: vehicle == null
          ? null
          : () => context.push(Routes.vehicle(vehicle.vehicleId)),
      child: Row(
        children: [
          SizedBox(
            width: 80,
            height: 60,
            child: KhadraImage(
              url: vehicle?.coverImageUrl,
              borderRadius: const BorderRadius.all(Radii.md),
            ),
          ),
          const SizedBox(width: Space.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  // A booking outlives the listing behind it, so the car can be
                  // gone. The gallery's name is what is left to identify it by.
                  vehicle?.title ?? booking.dealerName,
                  style: const TextStyle(
                      fontSize: 15, fontWeight: FontWeight.w700),
                ),
                if (vehicle != null) ...[
                  const SizedBox(height: 2),
                  Row(
                    children: [
                      Text(
                        '${vehicle.year}',
                        style: const TextStyle(
                            color: KhadraColors.neutral600, fontSize: 12),
                      ),
                      const SizedBox(width: Space.sm),
                      Text(
                        '${l10n.bookingPlate}: ',
                        style: const TextStyle(
                            color: KhadraColors.neutral600, fontSize: 12),
                      ),
                      LatinRun(
                        vehicle.plateNumber,
                        style: const TextStyle(
                            color: KhadraColors.neutral600, fontSize: 12),
                      ),
                    ],
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _Price extends StatelessWidget {
  const _Price({required this.booking, required this.formats});

  final Booking booking;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final pricing = booking.pricing;

    return KhadraCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          KhadraDetailRow(
            label: l10n.bookDailyRate,
            value: Text(
              // The frozen figures: the rate, the calendar dates it was priced
              // between, and the count they produced.
              '${formats.money(pricing.dailyRate)} × ${l10n.bookDays(pricing.days)}',
            ),
          ),
          KhadraDetailRow(
            label: l10n.bookRentalTotal,
            value: Text(formats.money(pricing.rentalTotal)),
          ),
          if (!pricing.deliveryFee.isZero)
            KhadraDetailRow(
              label: l10n.bookDeliveryFee,
              value: Text(formats.money(pricing.deliveryFee)),
            ),
          const Padding(
            padding: EdgeInsets.symmetric(vertical: Space.sm),
            child: Divider(height: 1),
          ),
          KhadraDetailRow(
            label: l10n.bookTotal,
            value: Text(formats.money(pricing.totalPrice)),
            valueStyle: const TextStyle(
                fontSize: 16, fontWeight: FontWeight.w700),
          ),
          KhadraDetailRow(
            label: l10n.bookDepositNow(formats.percent(pricing.depositPercent)),
            value: Text(formats.money(pricing.depositAmount)),
          ),
          KhadraDetailRow(
            label: l10n.bookBalanceAtPickup,
            value: Text(formats.money(pricing.balanceDue)),
          ),
          KhadraDetailRow(
            label: l10n.vehicleSecurityDeposit,
            value: Text(formats.money(pricing.securityDeposit)),
          ),
          const SizedBox(height: Space.md),
          Text(
            l10n.bookingTermsFrozen,
            style: const TextStyle(
                color: KhadraColors.neutral500, fontSize: 12, height: 1.45),
          ),
        ],
      ),
    );
  }
}

/// A penalty as an ASSESSMENT.
///
/// Spec 3.3: with no dispute ticket, nothing is applied at all. The sentence
/// saying so is conditional on the server's own `requiresTicketToEnforce` rather
/// than typed in beside the amount as a promise the app is making by itself.
class _Penalty extends StatelessWidget {
  const _Penalty({required this.penalty, required this.formats});

  final PenaltyAssessment penalty;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final party = BookingPresentation.party(l10n, penalty.attributedTo);

    return KhadraNotice(
      title: penalty.isRange
          ? l10n.bookingPenaltyRange(
              formats.money(penalty.minAmount),
              formats.money(penalty.maxAmount),
              party,
            )
          : l10n.bookingPenaltyAssessed(formats.money(penalty.maxAmount), party),
      body: penalty.requiresTicketToEnforce
          ? l10n.bookingPenaltyNotCharged
          : null,
      tone: NoticeTone.warn,
      icon: Icons.balance_outlined,
    );
  }
}

class _Handovers extends StatelessWidget {
  const _Handovers({required this.handovers, required this.formats});

  final List<Handover> handovers;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return KhadraCard(
      child: Column(
        children: [
          for (final handover in handovers) ...[
            Row(
              children: [
                Icon(
                  handover.type == 'Pickup'
                      ? Icons.key_outlined
                      : Icons.assignment_turned_in_outlined,
                  size: 18,
                  color: KhadraColors.accent,
                ),
                const SizedBox(width: Space.sm),
                Expanded(
                  child: Text(
                    handover.type == 'Pickup'
                        ? l10n.bookingHandoverPickup
                        : l10n.bookingHandoverReturn,
                    style: const TextStyle(
                        fontSize: 15, fontWeight: FontWeight.w600),
                  ),
                ),
                Text(
                  formats.dateTime(handover.recordedAt),
                  style: const TextStyle(
                      color: KhadraColors.neutral600, fontSize: 12),
                ),
              ],
            ),
            const SizedBox(height: Space.sm),
            if (handover.odometerKm != null)
              KhadraDetailRow(
                dense: true,
                label: l10n.bookingOdometer(handover.odometerKm.toString()),
                value: const SizedBox.shrink(),
              ),
            if (handover.fuelLevel != null)
              KhadraDetailRow(
                dense: true,
                label: l10n.bookingFuelLevel(
                    (handover.fuelLevel! * 100).round().toString()),
                value: const SizedBox.shrink(),
              ),
            if (handover.cashCollected != null)
              KhadraDetailRow(
                dense: true,
                label: l10n.bookingCashCollected(
                    formats.money(handover.cashCollected!)),
                value: const SizedBox.shrink(),
              ),
            if (handover.notes != null && handover.notes!.isNotEmpty)
              Align(
                alignment: AlignmentDirectional.centerStart,
                child: Text(
                  handover.notes!,
                  style: const TextStyle(fontSize: 13, height: 1.45),
                ),
              ),
            if (handover != handovers.last) ...[
              const SizedBox(height: Space.md),
              const Divider(height: 1),
              const SizedBox(height: Space.md),
            ],
          ],
        ],
      ),
    );
  }
}

/// Everything that has happened, oldest first.
class _Timeline extends ConsumerWidget {
  const _Timeline({required this.booking, required this.formats});

  final Booking booking;
  final Formats formats;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final arabic = ref.watch(isArabicProvider);
    final rejectionReasons = ref
            .watch(appConfigProvider)
            .valueOrNull
            ?.vocabularies
            .rejectionReasons ??
        const <VocabularyEntry>[];

    return KhadraCard(
      child: Column(
        children: [
          for (final change in booking.history) ...[
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Padding(
                  padding: const EdgeInsetsDirectional.only(top: 2, end: Space.md),
                  child: Icon(
                    BookingPresentation.icon(change.toStatus),
                    size: 18,
                    color: BookingPresentation.colour(change.toStatus),
                  ),
                ),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        BookingPresentation.label(l10n, change.toStatus),
                        style: const TextStyle(
                            fontSize: 14, fontWeight: FontWeight.w600),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        formats.dateTime(change.occurredAt),
                        style: const TextStyle(
                            color: KhadraColors.neutral500, fontSize: 12),
                      ),
                      if (_reasonLine(l10n, change, rejectionReasons, arabic)
                          case final line?) ...[
                        const SizedBox(height: Space.xs),
                        Text(
                          line,
                          style: const TextStyle(
                            fontSize: 13,
                            height: 1.45,
                            color: KhadraColors.neutral700,
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
            if (change != booking.history.last)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: Space.md),
                child: Divider(height: 1),
              ),
          ],
        ],
      ),
    );
  }

  /// The reason on one transition, in the reader's language.
  ///
  /// The closed-set CODE is what the platform stored; its label is chosen here.
  /// Whatever the actor typed follows it, unchanged and in whatever language they
  /// typed it — their words are theirs, and translating them would be inventing
  /// what they said.
  String? _reasonLine(
    AppLocalizations l10n,
    BookingStatusChange change,
    List<VocabularyEntry> rejectionReasons,
    bool arabic,
  ) {
    final label = _codeLabel(l10n, change, rejectionReasons, arabic);
    final typed = change.reason;

    if (label == null && (typed == null || typed.isEmpty)) return null;
    if (label == null) return typed;
    if (typed == null || typed.isEmpty) return label;
    return '$label — $typed';
  }

  /// The label for one closed-set code, in the READER's language.
  ///
  /// Cancellation codes have their own wording in the app. A rejection code is the
  /// gallery's word and comes from /app-config, in both languages -- which is the
  /// whole reason the platform stopped composing an English sentence into the row.
  String? _codeLabel(
    AppLocalizations l10n,
    BookingStatusChange change,
    List<VocabularyEntry> rejectionReasons,
    bool arabic,
  ) {
    final code = change.reasonCode;
    if (code == null) return null;

    final mine = BookingPresentation.cancellationReason(l10n, code);
    if (mine != null) return mine;

    // Falls back to the code itself for one the platform has not published, which
    // is at least a word somebody can search for.
    return Vocabularies.label(rejectionReasons, code, arabic);
  }
}
