import { PaymentOption, PaymentPurpose } from '../../core/api/bookings.api';

/**
 * Which of the server's options the pay box shows as chosen.
 *
 * The visitor's own pick wins; otherwise the option an open checkout was started with (so coming back
 * to a half-finished full payment shows it still chosen); otherwise the deposit, the minimum that
 * confirms. Only ever an option the server actually offered.
 */
export function chosenOption(
  options: readonly PaymentOption[] | undefined,
  picked: PaymentPurpose | null,
  openAttempt: PaymentPurpose | undefined,
): PaymentOption | null {
  if (!options?.length) return null;
  const find = (purpose: PaymentPurpose | null | undefined) =>
    purpose ? options.find((option) => option.purpose === purpose) : undefined;
  return find(picked) ?? find(openAttempt) ?? find('Deposit') ?? options[0];
}
