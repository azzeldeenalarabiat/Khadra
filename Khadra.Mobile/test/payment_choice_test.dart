import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/features/bookings/payment_choice.dart';

Money jod(num amount) => Money(amount, 'JOD');

// The owner's example: a 250 JOD booking, a 50 JOD deposit.
const deposit = PaymentOption(
  purpose: 'Deposit',
  selectedPaymentAmount: Money(50, 'JOD'),
  processingFee: Money(0, 'JOD'),
  totalChargedNow: Money(50, 'JOD'),
  remainingBalanceAfter: Money(200, 'JOD'),
);
const full = PaymentOption(
  purpose: 'FullPayment',
  selectedPaymentAmount: Money(250, 'JOD'),
  processingFee: Money(0, 'JOD'),
  totalChargedNow: Money(250, 'JOD'),
  remainingBalanceAfter: Money(0, 'JOD'),
);

void main() {
  group('the chosen way to pay', () {
    test('starts on the deposit, the minimum that confirms', () {
      expect(chosenOption([deposit, full]), same(deposit));
    });

    test("follows the customer's pick, with the server's own figures", () {
      final chosen = chosenOption([deposit, full], picked: 'FullPayment');
      expect(chosen, same(full));
      expect(chosen!.totalChargedNow.amount, 250);
      expect(chosen.remainingBalanceAfter.amount, 0);
    });

    test("shows a half-finished checkout's choice on the way back", () {
      expect(chosenOption([deposit, full], openAttempt: 'FullPayment'), same(full));
    });

    test('never invents an option the server did not offer', () {
      expect(chosenOption([deposit], picked: 'FullPayment'), same(deposit));
      expect(chosenOption(const []), isNull);
    });
  });

  group('reading the options', () {
    test('parses every figure and keeps the server order', () {
      final availability = PaymentAvailability.maybe({
        'canPay': true,
        'unavailableReason': null,
        'amountDue': {'amount': 50, 'currency': 'JOD'},
        'payBy': null,
        'liveAttempt': null,
        'options': [
          {
            'purpose': 'Deposit',
            'selectedPaymentAmount': {'amount': 50, 'currency': 'JOD'},
            'processingFee': {'amount': 0, 'currency': 'JOD'},
            'totalChargedNow': {'amount': 50, 'currency': 'JOD'},
            'remainingBalanceAfter': {'amount': 200, 'currency': 'JOD'},
          },
          {
            'purpose': 'FullPayment',
            'selectedPaymentAmount': {'amount': 250, 'currency': 'JOD'},
            'processingFee': {'amount': 3.75, 'currency': 'JOD'},
            'totalChargedNow': {'amount': 253.75, 'currency': 'JOD'},
            'remainingBalanceAfter': {'amount': 0, 'currency': 'JOD'},
          },
        ],
      })!;

      expect(availability.options.map((option) => option.purpose), ['Deposit', 'FullPayment']);
      expect(availability.options[1].processingFee.amount, 3.75);
      expect(availability.options[1].totalChargedNow.amount, 253.75);
    });

    test('an older server that sends no options leaves the list empty', () {
      final availability = PaymentAvailability.maybe({'canPay': true, 'liveAttempt': null})!;
      expect(availability.options, isEmpty);
    });
  });
}
