import 'package:flutter/material.dart';

import '../../api/dtos.dart';
import '../../core/format/booking_presentation.dart';
import '../../core/format/formats.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';

/// Where one stage of a booking stands.
enum StageState {
  /// Reached, and behind us. Carries the instant it happened.
  done,

  /// Where this booking is now.
  current,

  /// Ahead of it. No date, because nothing has happened yet and the screen must
  /// not imply that anything is scheduled.
  upcoming,
}

/// One stage of the lifecycle, as THIS booking actually lived it.
class BookingStage {
  const BookingStage({
    required this.status,
    required this.state,
    required this.reachedAt,
    this.isStalled = false,
    this.waitsOn,
  });

  /// The domain's own `BookingStatus` name, so the label, colour and icon come
  /// from the one place that words them.
  final String status;
  final StageState state;

  /// When it was reached, from the booking's own recorded transition. Null for
  /// a stage that has not happened.
  final DateTime? reachedAt;

  /// Reached, still current, and the SERVER says it is no longer live: a request
  /// past its decision deadline, or an approval past its payment window, in the
  /// minute or two before the settlement sweep writes `Expired`.
  ///
  /// Drawn without the "in progress" emphasis, because it is not in progress.
  /// The app must never work this out from a deadline and its own clock — see
  /// `Booking.isAwaitingDecision`.
  final bool isStalled;

  /// What an upcoming stage is waiting for, where the platform's own rule says.
  ///
  /// Only one stage has an answer worth giving: a returned rental becomes
  /// Finished after the frozen settlement window, unless a dispute is open, in
  /// which case it waits on the ticket instead. Never a DATE — the window is a
  /// frozen count of hours and the app does not add it to anything.
  final StageWait? waitsOn;
}

/// Why a stage has not happened yet.
enum StageWait { settlementWindow, disputeResolution }

/// The lifecycle, in the order the domain defines it.
///
/// `BookingStatus` in `Khadra.Domain/Bookings/BookingEnumerations.cs`: Requested
/// → Approved → Confirmed → PickedUp → Returned → Completed, forward only.
const List<String> _path = [
  'Requested',
  'Approved',
  'Confirmed',
  'PickedUp',
  'Returned',
  'Completed',
];

/// Whether this booking ended instead of finishing.
///
/// The SERVER's `isTerminal` flag, not a list of names kept here — a terminal
/// status the platform adds tomorrow is then handled by an app shipped today.
/// `Completed` is terminal too and is on the path, which is what the second half
/// distinguishes: a finished rental reached the end, it did not stop short of it.
bool _endedEarly(Booking booking) =>
    booking.isTerminal && !_path.contains(booking.status);

/// This booking's stages, built from its own recorded history.
///
/// **Every date here is one the server wrote.** `Booking.history` holds every
/// transition the aggregate ever made, including the `null → Requested` that
/// creates it, each with the instant it happened — so the timeline is read off
/// the record rather than assembled from a guess about what usually happens.
/// Nothing is invented for a stage that has not been reached: it gets a label
/// and no date.
///
/// **A booking that ENDED shows no future.** When it was rejected, cancelled,
/// missed or left to expire, the stages it never reached are dropped rather than
/// greyed: a cancelled booking is not waiting to be picked up, and drawing
/// "Returned" under it in grey promises a day that will never come.
List<BookingStage> bookingStages(Booking booking) {
  // First occurrence of each status. The server orders history oldest-first; a
  // status cannot be re-entered (every transition is forward-only), so the first
  // is the only.
  final reached = <String, DateTime>{};
  for (final change in booking.history) {
    reached.putIfAbsent(change.toStatus, () => change.occurredAt);
  }

  final status = booking.status;
  final ended = _endedEarly(booking);

  // Where the booking got to along the path. For one that ended, that is the
  // last path stage it actually reached.
  final currentIndex = _path.indexOf(status);

  final stages = <BookingStage>[];
  for (var i = 0; i < _path.length; i++) {
    final stage = _path[i];
    final at = reached[stage];

    if (ended) {
      // Only what really happened. `at == null` means this booking never got
      // here and now never will.
      if (at == null) continue;
      stages.add(BookingStage(status: stage, state: StageState.done, reachedAt: at));
      continue;
    }

    if (currentIndex == -1) {
      // A status this build has never heard of — the platform may add one. Show
      // the path as reached-so-far and stop guessing where it sits.
      if (at == null) continue;
      stages.add(BookingStage(status: stage, state: StageState.done, reachedAt: at));
      continue;
    }

    final state = i < currentIndex
        ? StageState.done
        : i == currentIndex
            ? StageState.current
            : StageState.upcoming;

    stages.add(BookingStage(
      status: stage,
      state: state,
      reachedAt: at,
      isStalled: i == currentIndex && _isStalled(booking, stage),
      // The one upcoming stage the platform can honestly say something about.
      // A returned rental finishes after the frozen settlement window — unless a
      // dispute is open, and then it waits on the ticket, which is a different
      // answer and the one the customer needs.
      waitsOn: state == StageState.upcoming && stage == 'Completed' && status == 'Returned'
          ? (booking.liveDisputeId != null
              ? StageWait.disputeResolution
              : StageWait.settlementWindow)
          : null,
    ));
  }

  if (ended) {
    stages.add(BookingStage(
      status: status,
      state: StageState.current,
      reachedAt: reached[status],
    ));
  } else if (currentIndex == -1) {
    stages.add(BookingStage(
      status: status,
      state: StageState.current,
      reachedAt: reached[status],
    ));
  }

  return stages;
}

/// Whether the current stage's own window has closed, on the SERVER's word.
///
/// `status` and liveness deliberately disagree for a moment: a request whose
/// decision deadline has passed stops holding the car at that instant, but the
/// row reads `Requested` until the sweep reaches it. `isAwaitingDecision` and
/// `isAwaitingPayment` are the server's verdicts and the only thing the app may
/// read — a clock comparison on a phone with the wrong time would draw a dead
/// booking as live, or the reverse.
bool _isStalled(Booking booking, String stage) => switch (stage) {
      'Requested' => !booking.isAwaitingDecision,
      'Approved' => !booking.isAwaitingPayment,
      _ => false,
    };

/// What to call a stage, which is NOT what to call the status.
///
/// A status badge says where a booking stands right now — "Approved — deposit
/// due", "بانتظار ردّ المكتب" — and those are present-tense claims. On a
/// timeline they land on stages that are over: a finished rental would read
/// "Approved — deposit due" against a step whose deposit was paid weeks ago, and
/// "awaiting the office's reply" against a request the office answered. A stage
/// is a thing that happened, so it is named in the past.
String stageLabel(AppLocalizations l10n, String status) => switch (status) {
      'Requested' => l10n.bookingStageRequested,
      'Approved' => l10n.bookingStageApproved,
      'Confirmed' => l10n.bookingStageConfirmed,
      'PickedUp' => l10n.bookingStagePickedUp,
      'Returned' => l10n.bookingStageReturned,
      'Completed' => l10n.bookingStageCompleted,
      'Rejected' => l10n.bookingStageRejected,
      'Cancelled' => l10n.bookingStageCancelled,
      'NoShow' => l10n.bookingStageNoShow,
      'Expired' => l10n.bookingStageExpired,
      // A status this build has never heard of falls back to the platform's own
      // name for it, which is at least a word somebody can ask about.
      _ => status,
    };

/// The lifecycle, drawn as a rail.
///
/// The rail is built with `Row`, so it sits on the start side and flips with the
/// language without a single direction being named here.
class BookingTimeline extends StatelessWidget {
  const BookingTimeline({super.key, required this.booking, required this.formats});

  final Booking booking;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final stages = bookingStages(booking);
    if (stages.isEmpty) return const SizedBox.shrink();

    return KhadraCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (var i = 0; i < stages.length; i++)
            _Stage(
              stage: stages[i],
              isLast: i == stages.length - 1,
              formats: formats,
              l10n: l10n,
            ),
        ],
      ),
    );
  }
}

class _Stage extends StatelessWidget {
  const _Stage({
    required this.stage,
    required this.isLast,
    required this.formats,
    required this.l10n,
  });

  final BookingStage stage;
  final bool isLast;
  final Formats formats;
  final AppLocalizations l10n;

  /// The height of one row's rail segment, so the line meets the next dot.
  static const double _railWidth = 28;
  static const double _dot = 12;

  @override
  Widget build(BuildContext context) {
    final upcoming = stage.state == StageState.upcoming;
    final current = stage.state == StageState.current;

    // A terminal ending keeps its own colour — a declined booking's dot is not
    // green — while the ordinary path is the accent. Both come from the one
    // place that colours a status.
    final reachedColour = BookingPresentation.colour(stage.status);
    final colour = upcoming ? KhadraColors.neutral300 : reachedColour;

    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            width: _railWidth,
            child: Column(
              children: [
                const SizedBox(height: 2),
                _Dot(colour: colour, filled: !upcoming, emphasised: current && !stage.isStalled),
                if (!isLast)
                  Expanded(
                    child: Container(
                      width: 2,
                      margin: const EdgeInsets.symmetric(vertical: 2),
                      color: upcoming ? KhadraColors.neutral200 : reachedColour.withValues(alpha: 0.35),
                    ),
                  ),
              ],
            ),
          ),
          Expanded(
            child: Padding(
              padding: EdgeInsetsDirectional.only(
                start: Space.sm,
                bottom: isLast ? 0 : Space.lg,
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    stageLabel(l10n, stage.status),
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: current ? FontWeight.w800 : FontWeight.w600,
                      color: upcoming ? KhadraColors.neutral500 : KhadraColors.text,
                    ),
                  ),
                  // A date only where one exists. An upcoming stage has not
                  // happened, and a time under it would be a schedule the
                  // platform never promised.
                  if (stage.reachedAt case final at?) ...[
                    const SizedBox(height: 2),
                    Text(
                      formats.dateTime(at),
                      style: const TextStyle(
                          color: KhadraColors.neutral600, fontSize: 12),
                    ),
                  ],
                  if (stage.waitsOn case final waits?) ...[
                    const SizedBox(height: 2),
                    Text(
                      switch (waits) {
                        StageWait.settlementWindow =>
                          l10n.bookingStageWaitingSettlement,
                        StageWait.disputeResolution =>
                          l10n.bookingStageWaitingDispute,
                      },
                      style: const TextStyle(
                          color: KhadraColors.neutral500, fontSize: 12),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Dot extends StatelessWidget {
  const _Dot({required this.colour, required this.filled, required this.emphasised});

  final Color colour;
  final bool filled;

  /// The stage the booking is ON, drawn with a ring around it. Not applied to a
  /// stalled stage: a request whose window has closed is where the booking sits,
  /// but nothing is happening in it.
  final bool emphasised;

  @override
  Widget build(BuildContext context) {
    final dot = Container(
      width: _Stage._dot,
      height: _Stage._dot,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: filled ? colour : KhadraColors.surface,
        border: Border.all(color: colour, width: 2),
      ),
    );

    if (!emphasised) return Padding(padding: const EdgeInsets.all(4), child: dot);

    return Container(
      padding: const EdgeInsets.all(4),
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: colour.withValues(alpha: 0.16),
      ),
      child: dot,
    );
  }
}
