import '../../api/dtos.dart';

/// Which of the server's options the payment step shows as chosen.
///
/// The customer's own pick wins; otherwise the option an open checkout was
/// started with (coming back to a half-finished full payment shows it still
/// chosen); otherwise the deposit, the minimum that confirms. Only ever an
/// option the server actually offered.
PaymentOption? chosenOption(
  List<PaymentOption> options, {
  String? picked,
  String? openAttempt,
}) {
  if (options.isEmpty) return null;
  PaymentOption? find(String? purpose) {
    if (purpose == null) return null;
    for (final option in options) {
      if (option.purpose == purpose) return option;
    }
    return null;
  }

  return find(picked) ?? find(openAttempt) ?? find('Deposit') ?? options.first;
}
