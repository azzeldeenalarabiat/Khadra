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
    });
  });
}
