import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/push/push_coordinator.dart';

/// The deposit a paid free cancellation returns (owner, 2026-09-24), as the app
/// reads it. Both fields are ADDITIVE: an older API sends neither, and the app
/// must read that as "no refund" and "no promise", never as a failure.
void main() {
  group('the cancellation preview', () {
    test('promises the refund only when the server says so', () {
      final promised = CancellationPreview.fromJson(
          {'canCancel': true, 'isFree': true, 'willRefundDeposit': true});
      final older = CancellationPreview.fromJson({'canCancel': true, 'isFree': true});

      expect(promised.willRefundDeposit, isTrue);
      expect(older.willRefundDeposit, isFalse);
      expect(older.isFree, isTrue);
    });
  });

  group('a deposit refund', () {
    Map<String, dynamic> refund(String status, {String? settledAt}) => {
          'status': status,
          'amount': {'amount': 40, 'currency': 'JOD'},
          'requestedAt': '2026-09-24T00:00:00Z',
          'sentAt': null,
          'settledAt': settledAt,
          'failedAt': null,
        };

    test('reads each stage the way the customer is told it', () {
      final initiated = DepositRefund.maybe(refund('Sent'))!;
      final done = DepositRefund.maybe(refund('Settled', settledAt: '2026-09-24T00:05:00Z'))!;
      final delayed = DepositRefund.maybe(refund('Failed'))!;

      expect(initiated.isRefunded, isFalse);
      expect(initiated.isDelayed, isFalse);
      expect(done.isRefunded, isTrue);
      expect(done.settledAt, DateTime.parse('2026-09-24T00:05:00Z'));
      expect(delayed.isDelayed, isTrue);
      expect(initiated.amount.amount, 40);
      expect(initiated.amount.currencyCode, 'JOD');
    });

    test('is absent rather than invented when the server sends none', () {
      expect(DepositRefund.maybe(null), isNull);
      expect(DepositRefund.maybe({'status': 'Sent'}), isNull);
    });
  });

  group('a refund notification', () {
    test('opens the booking it is about, like every booking notification', () {
      expect(pushRoute({'kind': 'YourDepositRefunded', 'subjectId': 'b1'}), '/bookings/b1');
      expect(pushRoute({'kind': 'YourPartialRefundSettled', 'subjectId': 'b1'}), '/bookings/b1');
    });
  });

  // Phase 3 (owner, 2026-09-26). Every field is ADDITIVE: an older API sends none
  // of them, and the app reads that as "nothing listed", never as a failure.
  group('the refunds a booking lists', () {
    Map<String, dynamic> row(String reason, String status) => {
          'refundId': 'r-$reason',
          'paymentId': 'p-1',
          'reason': reason,
          'amount': {'amount': 132, 'currency': 'JOD'},
          'status': status,
          'requestedAt': '2026-09-26T10:00:00Z',
          'sentAt': null,
          'settledAt': status == 'Settled' ? '2026-09-26T10:05:00Z' : null,
          'failedAt': status == 'Failed' ? '2026-09-26T10:02:00Z' : null,
          'disputeTicketId': null,
        };

    test('reads each refund with its reason, amount and status', () {
      final refunds = Refund.listOrNull([row('EndedBeforePickup', 'Settled'), row('DisputeWindowClosed', 'Failed'), 'junk'])!;

      expect(refunds, hasLength(2));
      expect(refunds.first.reason, 'EndedBeforePickup');
      expect(refunds.first.isRefunded, isTrue);
      expect(refunds.first.amount.amount, 132);
      expect(refunds.first.settledAt, DateTime.parse('2026-09-26T10:05:00Z'));
      expect(refunds.last.isDelayed, isTrue);
    });

    test('an older API lists nothing, which is not the same as an empty list', () {
      expect(Refund.listOrNull(null), isNull);
      expect(Refund.listOrNull(const <dynamic>[]), isEmpty);
    });

    test('the sheet knows the refund it promises, and whether the server published one', () {
      final published = CancellationPreview.fromJson({
        'canCancel': true,
        'isFree': false,
        'refundAmount': {'amount': 132, 'currency': 'JOD'},
      });
      final nothing = CancellationPreview.fromJson({'canCancel': true, 'isFree': false, 'refundAmount': null});
      final older = CancellationPreview.fromJson({'canCancel': true, 'isFree': false});

      expect(published.refundAmount!.amount, 132);
      expect(published.publishesRefundAmount, isTrue);
      expect(nothing.refundAmount, isNull);
      expect(nothing.publishesRefundAmount, isTrue);
      expect(older.publishesRefundAmount, isFalse);
    });
  });
}
