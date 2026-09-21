import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/features/bookings/booking_timeline.dart';

/// The lifecycle timeline, built from a booking's OWN recorded transitions.
///
/// Every date it shows is one the server wrote, and a stage that has not
/// happened carries none. The cases below are the ones where an invented step or
/// an invented date would be easiest to slip in: a booking that ended early, a
/// deadline that has passed without the sweep having run, and a status this
/// build has never heard of.
void main() {
  final now = DateTime.utc(2026, 9, 20, 9);

  BookingStatusChange change(String to, DateTime at, {String? from}) =>
      BookingStatusChange.fromJson({
        'fromStatus': from,
        'toStatus': to,
        'actorParty': 'Customer',
        'reasonCode': null,
        'reason': null,
        'occurredAt': at.toIso8601String(),
      });

  Booking booking({
    required String status,
    required List<BookingStatusChange> history,
    bool isAwaitingDecision = false,
    bool isAwaitingPayment = false,
  }) =>
      Booking.fromJson({
        'bookingId': 'b1',
        'reference': 'KH-0001',
        'status': status,
        'isTerminal': const {'Rejected', 'Cancelled', 'NoShow', 'Expired', 'Completed'}
            .contains(status),
        'dealerId': 'd1',
        'vehicleId': 'v1',
        'periodStart': now.add(const Duration(days: 3)).toIso8601String(),
        'periodEnd': now.add(const Duration(days: 6)).toIso8601String(),
        'pickupMethod': 'SelfPickup',
        'pricing': const <String, dynamic>{},
        'terms': const <String, dynamic>{},
        'createdAt': now.toIso8601String(),
        'decisionDeadline': now.add(const Duration(hours: 4)).toIso8601String(),
        'isAwaitingDecision': isAwaitingDecision,
        'isAwaitingPayment': isAwaitingPayment,
        'nonDeliveryReportableFrom': now.toIso8601String(),
        'cancellation': const <String, dynamic>{},
        'dealerName': 'Petra Rentals',
        'handovers': const <dynamic>[],
        'history': [for (final c in history) _json(c)],
      });

  List<String> namesOf(List<BookingStage> stages) =>
      [for (final stage in stages) stage.status];

  test('a fresh request shows the whole path, with only the first date', () {
    final stages = bookingStages(booking(
      status: 'Requested',
      isAwaitingDecision: true,
      history: [change('Requested', now)],
    ));

    expect(namesOf(stages),
        ['Requested', 'Approved', 'Confirmed', 'PickedUp', 'Returned', 'Completed']);
    expect(stages.first.state, StageState.current);
    expect(stages.first.reachedAt, now);
    expect(stages.first.isStalled, isFalse);

    // Nothing ahead carries a date. A time under a stage that has not happened
    // would be a schedule the platform never promised.
    for (final stage in stages.skip(1)) {
      expect(stage.state, StageState.upcoming);
      expect(stage.reachedAt, isNull, reason: '${stage.status} has a date it cannot have');
    }
  });

  test('a rental out now marks the stages behind it done, from their own dates', () {
    final approved = now.add(const Duration(hours: 1));
    final confirmed = now.add(const Duration(hours: 2));
    final pickedUp = now.add(const Duration(days: 3));

    final stages = bookingStages(booking(
      status: 'PickedUp',
      history: [
        change('Requested', now),
        change('Approved', approved, from: 'Requested'),
        change('Confirmed', confirmed, from: 'Approved'),
        change('PickedUp', pickedUp, from: 'Confirmed'),
      ],
    ));

    expect(stages[1].reachedAt, approved);
    expect(stages[2].reachedAt, confirmed);
    expect(stages[3].state, StageState.current);
    expect(stages[3].reachedAt, pickedUp);
    expect(stages[4].state, StageState.upcoming);
    expect(stages[5].state, StageState.upcoming);
  });

  test('a booking that ended shows no future it will never have', () {
    // Cancelled before the office ever answered. Drawing "Picked up" and
    // "Finished" in grey under this would promise days that cannot come.
    final cancelled = now.add(const Duration(hours: 2));
    final stages = bookingStages(booking(
      status: 'Cancelled',
      history: [
        change('Requested', now),
        change('Cancelled', cancelled, from: 'Requested'),
      ],
    ));

    expect(namesOf(stages), ['Requested', 'Cancelled']);
    expect(stages.last.state, StageState.current);
    expect(stages.last.reachedAt, cancelled);
  });

  test('an ending keeps the stages that really were reached', () {
    final approved = now.add(const Duration(hours: 1));
    final expired = now.add(const Duration(hours: 3));

    final stages = bookingStages(booking(
      status: 'Expired',
      history: [
        change('Requested', now),
        change('Approved', approved, from: 'Requested'),
        change('Expired', expired, from: 'Approved'),
      ],
    ));

    // Confirmed was never reached: the deposit was never paid.
    expect(namesOf(stages), ['Requested', 'Approved', 'Expired']);
  });

  test('a window that has closed is not drawn as still running', () {
    // The server says the request is no longer awaiting a decision while the row
    // still reads Requested — the minute or two before the settlement sweep. The
    // stage is where the booking IS, but nothing is happening in it.
    final stages = bookingStages(booking(
      status: 'Requested',
      isAwaitingDecision: false,
      history: [change('Requested', now)],
    ));

    expect(stages.first.state, StageState.current);
    expect(stages.first.isStalled, isTrue);
  });

  test('an approval still inside its payment window is running', () {
    final stages = bookingStages(booking(
      status: 'Approved',
      isAwaitingPayment: true,
      history: [
        change('Requested', now),
        change('Approved', now.add(const Duration(hours: 1)), from: 'Requested'),
      ],
    ));

    expect(stages[1].state, StageState.current);
    expect(stages[1].isStalled, isFalse);
  });

  test('a status this build has never heard of degrades rather than guessing', () {
    // The platform can add a status without asking the app. An old build in a
    // shop should show what it knows happened and stop, not place the unknown
    // one somewhere on the path by guessing.
    final stages = bookingStages(booking(
      status: 'SomethingNew',
      history: [
        change('Requested', now),
        change('SomethingNew', now.add(const Duration(hours: 1)), from: 'Requested'),
      ],
    ));

    expect(namesOf(stages), ['Requested', 'SomethingNew']);
    expect(stages.last.state, StageState.current);
  });
}

/// `BookingStatusChange` back to the shape it was parsed from, so the fixtures
/// above can be written once as domain objects.
Map<String, dynamic> _json(BookingStatusChange change) => {
      'fromStatus': change.fromStatus,
      'toStatus': change.toStatus,
      'actorParty': change.actorParty,
      'reasonCode': change.reasonCode,
      'reason': change.reason,
      'occurredAt': change.occurredAt.toIso8601String(),
    };
