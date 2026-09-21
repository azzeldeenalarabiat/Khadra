import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/booking_presentation.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../core/widgets/sandbox_banner.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';
import 'booking_providers.dart';
import 'booking_timeline.dart';
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

  /// How many times a spent deadline has been re-read. See [_askTheServerWhenTheClockRunsOut].
  int _expiryReads = 0;

  @override
  void initState() {
    super.initState();
    // The countdown is rendered from the server's deadline; this only repaints it.
    _tick = Timer.periodic(const Duration(seconds: 30), (_) {
      if (!mounted) return;
      _askTheServerWhenTheClockRunsOut();
      setState(() {});
    });
  }

  /// Re-reads the booking once its own countdown has run out.
  ///
  /// The countdown is display only — the SERVER decides whether a booking is
  /// over, and its settlement pass runs on a minute of its own. Without this the
  /// screen sits on "Deposit of 24.000 JOD is due" above a clock reading zero
  /// until the customer thinks to pull down, which is the app contradicting
  /// itself on the one screen where the answer matters.
  ///
  /// It mattered less while the payment window was a day: nobody was watching
  /// when it ran out. At two hours they very well might be.
  ///
  /// Capped at three reads — ninety seconds, comfortably past the sweep's own
  /// minute — so a server that has not settled yet is asked a few times and then
  /// left alone. This is a detail screen, not a poller.
  void _askTheServerWhenTheClockRunsOut() {
    if (_expiryReads >= 3) return;
    final booking = ref.read(bookingProvider(widget.bookingId)).valueOrNull;
    if (booking == null) return;

    final deadline = booking.isAwaitingPayment
        ? booking.paymentDeadline
        : booking.isAwaitingDecision
            ? booking.decisionDeadline
            : null;
    if (deadline == null || DateTime.now().toUtc().isBefore(deadline.toUtc())) {
      return;
    }

    _expiryReads++;
    invalidateBookings(ref, bookingId: widget.bookingId);
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
        // The booking's own reference, which is what identifies this screen and
        // what somebody reads out on the phone to the office. Latin in both
        // languages, so it is isolated rather than left to bidi.
        title: switch (booking) {
          AsyncData(:final value) => LatinRun(
              value.reference,
              style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w800),
            ),
          _ => Text(l10n.bookingsTitle),
        },
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
        // FIRST, and it earns the place: the deposit window is two hours and
        // there is no push channel, so this screen is the only surface that can
        // tell a customer in time. It renders nothing when there is nothing
        // urgent to say, and then the summary below is what leads the page.
        //
        // A penalty stays welded to the outcome that caused it — "assessed, not
        // charged" is the sentence that answers the question a cancelled or
        // missed booking raises, and it must not be a scroll away from it.
        _StateNotice(booking: booking, formats: formats),
        if (_StateNotice.speaks(booking)) const SizedBox(height: Space.lg),

        if (booking.penalty != null && !booking.penalty!.isNothingOwed) ...[
          _Penalty(penalty: booking.penalty!, formats: formats),
          const SizedBox(height: Space.lg),
        ],

        // The car, the office and the dates, with the status on it: what this
        // booking IS, in one card.
        _VehicleCard(booking: booking, formats: formats),

        // Cancel, dispute and non-delivery stay above the cards. The dispute
        // window runs on a frozen settlement period and closes for good; a
        // customer who cannot find the button before it does has lost the right.
        if (_Actions.has(booking)) ...[
          const SizedBox(height: Space.xl),
          KhadraSectionTitle(l10n.bookingActions),
          _Actions(booking: booking),
        ],

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.bookingProgress),
        BookingTimeline(booking: booking, formats: formats),

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.bookingPaymentSummary),
        _Price(booking: booking, formats: formats),

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.bookingPickupReturn),
        _PickupAndReturn(booking: booking, formats: formats),

        if (booking.handovers.isNotEmpty) ...[
          const SizedBox(height: Space.xl),
          KhadraSectionTitle(l10n.bookingHandoversTitle),
          _Handovers(handovers: booking.handovers, formats: formats),
        ],

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.bookingHistory),
        _Activity(booking: booking, formats: formats),
      ],
    );
  }
}

/// Where and when the car is handed over, and how.
///
/// Separated from the summary above it because these are the facts somebody
/// opens the booking to check on the morning of the rental, and they were
/// previously mixed in with the reference and the status in one undifferentiated
/// list of rows.
class _PickupAndReturn extends StatelessWidget {
  const _PickupAndReturn({required this.booking, required this.formats});

  final Booking booking;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return KhadraCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          KhadraDetailRow(
            label: l10n.searchPickup,
            value: Text(formats.dateTime(booking.periodStart)),
          ),
          KhadraDetailRow(
            label: l10n.searchReturn,
            value: Text(formats.dateTime(booking.periodEnd)),
          ),
          const Padding(
            padding: EdgeInsets.symmetric(vertical: Space.sm),
            child: Divider(height: 1),
          ),
          KhadraDetailRow(
            label: l10n.bookingWhere,
            value: Text(booking.isDelivery
                ? l10n.bookingWhereDelivery
                : l10n.bookingWhereSelfPickup),
          ),
          KhadraDetailRow(
            label: l10n.bookingGallery,
            value: Text(BookingPresentation.dealerName(l10n, booking)),
          ),
          const SizedBox(height: Space.sm),
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

  /// Whether this has anything to say, so the page does not leave a gap under
  /// silence. The one status that renders nothing is a booking quietly in
  /// progress — Confirmed, PickedUp or Returned with its window still open —
  /// where the badge on the summary already says everything there is to say.
  static bool speaks(Booking booking) {
    if (booking.isAwaitingDecision || booking.isAwaitingPayment) return true;
    if (!booking.isTerminal &&
        ((booking.status == 'Requested' && !booking.isAwaitingDecision) ||
            (booking.status == 'Approved' && !booking.isAwaitingPayment))) {
      return true;
    }
    return const {'Expired', 'Rejected', 'Cancelled', 'NoShow', 'Completed'}
        .contains(booking.status);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    // The two states that used to render NOTHING.
    //
    // A request whose decision deadline has passed, or an approval whose payment
    // window has closed, still reads `Requested` / `Approved` until the
    // settlement sweep gets to it — up to a minute. The badge said "deposit due"
    // above an empty space, and the customer had no way to tell that the chance
    // was gone. The SERVER's verdict is what says so; the app never compares a
    // deadline with its own clock.
    if (!booking.isTerminal) {
      if (booking.status == 'Requested' && !booking.isAwaitingDecision) {
        return KhadraNotice(
          title: l10n.bookingLapsedDecisionTitle,
          body: l10n.bookingLapsedDecisionBody,
          tone: NoticeTone.neutral,
          icon: Icons.timer_off_outlined,
        );
      }
      if (booking.status == 'Approved' && !booking.isAwaitingPayment) {
        return KhadraNotice(
          title: l10n.bookingLapsedPaymentTitle,
          body: l10n.bookingLapsedPaymentBody,
          tone: NoticeTone.neutral,
          icon: Icons.timer_off_outlined,
        );
      }
    }

    // Server verdicts, in order of what matters most to the reader.
    if (booking.isAwaitingDecision) {
      return KhadraNotice(
        title: l10n.bookingAwaitingDecisionTitle(
            BookingPresentation.dealerName(l10n, booking)),
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

    // The CUSTOMER gets their own sentence rather than their pronoun dropped
    // into a shared one. Arabic attaches a pronoun to the preposition — "من
    // قِبلك", not "من قِبل" + a word for "you" — so composing the two produced
    // "أُلغي من قِبل عليك", which doubles the preposition and is not a sentence.
    final parts = <String>[
      if (booking.cancelledBy == 'Customer')
        l10n.bookingCancelledByYou
      else if (booking.cancelledBy != null)
        l10n.bookingCancelledBy(party),
      if (label != null) label,
      if (typed != null && typed.isNotEmpty) typed,
    ];
    return parts.isEmpty ? null : parts.join(' · ');
  }
}

/// The payment step, told honestly.
///
/// **Whether there is a Pay button is the SERVER's answer, not this screen's.**
/// It used to be a hard-coded "not available in this version", which was true and
/// is exactly the kind of truth that rots: the day a provider is configured, a
/// screen deciding for itself would still be refusing. `booking.payment` carries
/// the verdict and, when it is no, the CODE behind it -- because "your window has
/// closed" and "this platform cannot take cards yet" are different facts with
/// different remedies, and a customer shown the wrong one either gives up or
/// complains about the wrong thing.
///
/// What has not changed is that nothing here invents a way to pay. There is no
/// "pay at the counter": the deposit is what makes a booking a booking, and no
/// deposit is taken out of band.
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
        // Before the button, not after it. This is the screen where a customer is
        // about to act on a figure, so "no money moves" has to arrive before the
        // decision rather than as a footnote under it. Renders nothing unless the
        // server itself reported Sandbox.
        const SandboxPaymentsBanner(padding: EdgeInsets.only(top: Space.md)),
        const SizedBox(height: Space.md),
        _PaymentAction(booking: booking),
      ],
    );
  }
}

/// Pay, or the reason there is nothing to press.
class _PaymentAction extends ConsumerStatefulWidget {
  const _PaymentAction({required this.booking});

  final Booking booking;

  @override
  ConsumerState<_PaymentAction> createState() => _PaymentActionState();
}

class _PaymentActionState extends ConsumerState<_PaymentAction> {
  bool _opening = false;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final payment = widget.booking.payment;

    // A missing verdict means an older server, or a booking read by someone who
    // is not its customer. Say the safe thing rather than offering a button.
    if (payment == null || !payment.canPay) {
      return KhadraNotice(
        title: l10n.bookingPaymentNotAvailableTitle,
        body: payment != null && !payment.providerUnavailable
            ? l10n.bookingPaymentWindowClosedBody
            : l10n.bookingPaymentNotAvailableBody,
        tone: NoticeTone.neutral,
        icon: Icons.credit_card_off_outlined,
      );
    }

    return SizedBox(
      width: double.infinity,
      child: FilledButton.icon(
        onPressed: _opening ? null : _pay,
        icon: const Icon(Icons.credit_card, size: 18),
        label: Text(l10n.bookingPayDeposit),
      ),
    );
  }

  /// Opens a checkout and hands the customer to the provider.
  ///
  /// Repeating it is safe and is the intended way to recover: the server returns
  /// the session already in flight rather than opening a second one, so a
  /// customer who closed the tab lands back on the same card form.
  Future<void> _pay() async {
    final l10n = AppLocalizations.of(context);
    setState(() => _opening = true);
    try {
      final attempt =
          await ref.read(apiProvider).openDepositCheckout(widget.booking.bookingId);
      if (!mounted) return;

      final url = attempt.checkoutUrl;
      if (url == null) {
        showKhadraMessage(context, l10n.bookingPaymentNotAvailableBody, isError: true);
        return;
      }

      await launchUrl(Uri.parse(url), mode: LaunchMode.externalApplication);
      // Coming back from a provider confirms NOTHING -- only a signed webhook
      // does -- so the screen re-reads the booking rather than assuming.
      if (mounted) invalidateBookings(ref, bookingId: widget.booking.bookingId);
    } on ApiFailure catch (failure) {
      if (mounted) showKhadraMessage(context, failure.messageFor(l10n), isError: true);
    } finally {
      if (mounted) setState(() => _opening = false);
    }
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

  /// Whether there is anything to put under a heading. Asked by the page so an
  /// empty section title does not sit above nothing.
  static bool has(Booking booking) =>
      booking.cancellation.canCancel ||
      booking.canReportNonDelivery ||
      booking.status == 'Confirmed' ||
      booking.liveDisputeId != null ||
      booking.canBeDisputed ||
      booking.canBeReviewed;

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
      // Why it is off, as VISIBLE text under the control. It was a Tooltip,
      // which on a phone needs a long press nobody performs on a disabled
      // button — so the reason existed and could not be read.
      actions.add(
        Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            OutlinedButton.icon(
              onPressed: null,
              icon: const Icon(Icons.report_gmailerrorred_outlined, size: 18),
              label: Text(l10n.nonDeliveryTitle),
            ),
            const SizedBox(height: Space.xs),
            Text(
              // Falls back to the general sentence when the config has not
              // arrived and there is no formatter yet: a reason without a time
              // still says why the button is off.
              switch (ref.watch(formatsProvider)) {
                final formats? => l10n
                    .nonDeliveryNotYet(formats.dateTime(booking.nonDeliveryReportableFrom)),
                null => l10n.nonDeliveryTooEarly,
              },
              style: const TextStyle(
                  color: KhadraColors.neutral600, fontSize: 12, height: 1.4),
            ),
          ],
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
  void initState() {
    super.initState();
    // The Report button is gated on there being something typed, so the dialog
    // has to rebuild as it IS typed. Without this listener the gate reads an
    // empty controller for ever and the button never enables -- which made the
    // whole non-delivery report unreachable.
    _details.addListener(_onChanged);
  }

  void _onChanged() => setState(() {});

  @override
  void dispose() {
    _details.removeListener(_onChanged);
    _details.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final details = _details.text.trim();

    return AlertDialog(
      title: Text(l10n.nonDeliveryTitle),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
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
              autofocus: true,
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.actionCancel),
        ),
        FilledButton(
          onPressed: details.isEmpty
              ? null
              : () => Navigator.of(context).pop(details),
          child: Text(l10n.nonDeliveryReport),
        ),
      ],
    );
  }
}

/// What this booking IS: the car, who it is from, and when.
///
/// The screen's hero. It carries the status badge, because "which booking is
/// this and how is it going" is one question and used to be answered by two
/// blocks a scroll apart.
class _VehicleCard extends ConsumerWidget {
  const _VehicleCard({required this.booking, required this.formats});

  final Booking booking;
  final Formats formats;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final vehicle = booking.vehicle;
    final city = ref.watch(cityNameProvider(booking.dealerCityId));

    return KhadraCard(
      padding: const EdgeInsets.all(Space.md),
      onTap: vehicle == null
          ? null
          : () => context.push(Routes.vehicle(vehicle.vehicleId)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SizedBox(
                width: 96,
                height: 72,
                child: KhadraImage(
                  url: vehicle?.coverImageUrl,
                  borderRadius: Radii.field,
                ),
              ),
              const SizedBox(width: Space.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      // A booking outlives the listing behind it, so the car can
                      // be gone. The office's name is what identifies it then.
                      vehicle?.title ??
                          BookingPresentation.dealerName(l10n, booking),
                      style: const TextStyle(
                          fontSize: 16, fontWeight: FontWeight.w800),
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                    ),
                    if (vehicle != null) ...[
                      const SizedBox(height: 3),
                      Wrap(
                        spacing: Space.sm,
                        runSpacing: 2,
                        crossAxisAlignment: WrapCrossAlignment.center,
                        children: [
                          Text(
                            '${vehicle.year}',
                            style: const TextStyle(
                                color: KhadraColors.neutral600, fontSize: 12),
                          ),
                          Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
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
                      ),
                    ],
                    const SizedBox(height: 4),
                    Text(
                      // The office, and the city it is in where the platform
                      // knows one. `cityNameProvider` answers null for a city
                      // that has not loaded or has been retired, and then the
                      // line is simply the office.
                      city == null
                          ? BookingPresentation.dealerName(l10n, booking)
                          : '${BookingPresentation.dealerName(l10n, booking)} · $city',
                      style: const TextStyle(
                          color: KhadraColors.neutral700,
                          fontSize: 13,
                          fontWeight: FontWeight.w600),
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ],
                ),
              ),
            ],
          ),
          const Padding(
            padding: EdgeInsets.symmetric(vertical: Space.md),
            child: Divider(height: 1),
          ),
          Row(
            children: [
              const Icon(Icons.calendar_today_outlined,
                  size: 15, color: KhadraColors.neutral600),
              const SizedBox(width: Space.sm),
              Expanded(
                child: Text(
                  formats.dateRange(booking.periodStart, booking.periodEnd),
                  style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600),
                ),
              ),
              KhadraBadge(
                label: BookingPresentation.label(l10n, booking.status),
                colour: BookingPresentation.colour(booking.status),
                icon: BookingPresentation.icon(booking.status),
              ),
            ],
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

    return Column(
      children: [
        // BLOCK ONE: what the rental costs.
        KhadraCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _BlockLabel(l10n.bookingRentalCost),
              KhadraDetailRow(
                label: l10n.bookDailyRate,
                value: Text(
                  // ONE text run, not two cells: the rate, the multiplication
                  // sign and the day count reorder against each other in Arabic
                  // if bidi is left to resolve them separately. The figures are
                  // the frozen ones — the rate, and the count the Amman calendar
                  // dates produced.
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
                    fontSize: 17, fontWeight: FontWeight.w800),
              ),
            ],
          ),
        ),

        // BLOCK TWO: how that total is paid. These two figures ADD UP to the
        // total above, which is why they are together and apart from what
        // follows.
        const SizedBox(height: Space.md),
        KhadraCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _BlockLabel(l10n.bookingHowItIsPaid),
              KhadraDetailRow(
                label: l10n.bookDepositNow(formats.percent(pricing.depositPercent)),
                value: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(formats.money(pricing.depositAmount)),
                    // Whether it is already paid is a FACT on the booking, not
                    // a guess from the status.
                    if (booking.depositPaid) ...[
                      const SizedBox(width: Space.sm),
                      KhadraBadge(
                        label: l10n.bookingDepositPaidNote,
                        colour: KhadraColors.ok,
                        icon: Icons.check_rounded,
                      ),
                    ],
                  ],
                ),
              ),
              KhadraDetailRow(
                label: l10n.bookBalanceAtPickup,
                value: Text(formats.money(pricing.balanceDue)),
              ),
              if (!pricing.deliveryFee.isZero) ...[
                const SizedBox(height: 2),
                // Otherwise a reader adds the delivery line from the block above
                // a second time: the fee is inside the cash figure, because the
                // driver collects it.
                Text(
                  l10n.bookingBalanceIncludesDelivery,
                  style: const TextStyle(
                      color: KhadraColors.neutral500, fontSize: 12),
                ),
              ],
            ],
          ),
        ),

        // BLOCK THREE, visibly apart: the security deposit is NOT part of the
        // total and is not Khadra's. In one aligned column with the figures
        // above it reads either as money owed on top or as a total that does not
        // add up, so it gets its own card and the sentence that explains it.
        if (!pricing.securityDeposit.isZero) ...[
          const SizedBox(height: Space.md),
          KhadraCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                KhadraDetailRow(
                  label: l10n.vehicleSecurityDeposit,
                  value: Text(formats.money(pricing.securityDeposit)),
                ),
                const SizedBox(height: 2),
                Text(
                  l10n.vehicleSecurityDepositHelp,
                  style: const TextStyle(
                      color: KhadraColors.neutral600, fontSize: 12, height: 1.45),
                ),
              ],
            ),
          ),
        ],

        const SizedBox(height: Space.md),
        Text(
          l10n.bookingTermsFrozen,
          style: const TextStyle(
              color: KhadraColors.neutral500, fontSize: 12, height: 1.45),
        ),
      ],
    );
  }
}

/// The heading inside a card, for the blocks of the payment summary.
class _BlockLabel extends StatelessWidget {
  const _BlockLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: Space.xs),
        child: Text(
          text,
          style: const TextStyle(
            fontSize: 12,
            fontWeight: FontWeight.w800,
            color: KhadraColors.neutral600,
          ),
        ),
      );
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

    // Same reason as the cancellation line: "على" + "عليك" is not Arabic.
    final onCustomer = penalty.attributedTo == 'Customer';

    return KhadraNotice(
      title: penalty.isRange
          ? (onCustomer
              ? l10n.bookingPenaltyRangeOnYou(
                  formats.money(penalty.minAmount),
                  formats.money(penalty.maxAmount),
                )
              : l10n.bookingPenaltyRange(
                  formats.money(penalty.minAmount),
                  formats.money(penalty.maxAmount),
                  party,
                ))
          : (onCustomer
              ? l10n.bookingPenaltyAssessedOnYou(formats.money(penalty.maxAmount))
              : l10n.bookingPenaltyAssessed(formats.money(penalty.maxAmount), party)),
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
                child: UserText(
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

/// Everything that has happened, newest first.
///
/// The lifecycle card above summarises where the booking got to; this is the
/// evidence underneath it — every transition the platform recorded, with who
/// acted and why where a reason exists.
///
/// **Newest first**, unlike the lifecycle. A timeline is read forwards because
/// it is about progress; a log is read backwards because the last thing that
/// happened is the thing being looked for.
class _Activity extends ConsumerWidget {
  const _Activity({required this.booking, required this.formats});

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

    final entries = booking.history.reversed.toList();

    return KhadraCard(
      child: Column(
        children: [
          for (var i = 0; i < entries.length; i++) ...[
            _ActivityRow(
              change: entries[i],
              line: _reasonLine(l10n, entries[i], rejectionReasons, arabic),
              formats: formats,
              l10n: l10n,
            ),
            if (i != entries.length - 1)
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
  ///
  /// **The platform's OWN sentences are dropped.** A transition the system made
  /// on a timer carries an English sentence and no code — "Payment window
  /// elapsed.", "Dealer did not respond." — which is untranslatable prose the app
  /// would be printing into an Arabic screen. The status name beside it already
  /// says the same thing in the reader's language, so nothing is lost. A person's
  /// typed words still show, whoever they are.
  String? _reasonLine(
    AppLocalizations l10n,
    BookingStatusChange change,
    List<VocabularyEntry> rejectionReasons,
    bool arabic,
  ) {
    final label = _codeLabel(l10n, change, rejectionReasons, arabic);
    final actedByPlatform =
        change.actorParty == 'System' || change.actorParty == 'Admin';
    final typed =
        label == null && actedByPlatform ? null : change.reason;

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

/// One entry in the activity log: what changed, when, who did it, and why.
class _ActivityRow extends StatelessWidget {
  const _ActivityRow({
    required this.change,
    required this.line,
    required this.formats,
    required this.l10n,
  });

  final BookingStatusChange change;
  final String? line;
  final Formats formats;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final party = BookingPresentation.party(l10n, change.actorParty);

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          margin: const EdgeInsetsDirectional.only(top: 1, end: Space.md),
          padding: const EdgeInsets.all(6),
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: BookingPresentation.colour(change.toStatus)
                .withValues(alpha: 0.12),
          ),
          child: Icon(
            BookingPresentation.icon(change.toStatus),
            size: 16,
            color: BookingPresentation.colour(change.toStatus),
          ),
        ),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Text(
                      stageLabel(l10n, change.toStatus),
                      style: const TextStyle(
                          fontSize: 14, fontWeight: FontWeight.w700),
                    ),
                  ),
                  const SizedBox(width: Space.sm),
                  Text(
                    formats.dateTime(change.occurredAt),
                    style: const TextStyle(
                        color: KhadraColors.neutral500, fontSize: 11),
                  ),
                ],
              ),
              if (party.isNotEmpty) ...[
                const SizedBox(height: 2),
                Text(
                  l10n.bookingActivityBy(party),
                  style: const TextStyle(
                      color: KhadraColors.neutral600, fontSize: 12),
                ),
              ],
              if (line case final reason?) ...[
                const SizedBox(height: Space.xs),
                UserText(
                  reason,
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
    );
  }
}
