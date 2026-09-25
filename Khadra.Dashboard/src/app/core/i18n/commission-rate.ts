import { TranslationKey } from './en';
import { MessageParams } from './language';

/** Passed in rather than injected: a pure function, called directly by its spec. */
type Translate = (key: TranslationKey, params?: MessageParams) => string;

/**
 * A commission percent together with what it is a percent OF (owner, 2026-09-25).
 *
 * Since 2026-09-24 a new booking's commission is a percent of ONE day's rate, not of the rental. A
 * bare "Platform commission · 20%" beside 20.000 on a 200.000 rental therefore read as 20% of the
 * whole rental, a figure nobody could reconcile with the amount printed next to it. Every place the
 * console prints the rate says its basis, taken from the basis the server froze on the booking, or
 * the one in force on the settings screen.
 *
 * A basis this build does not know prints the percent alone: naming a basis the server did not state
 * would be a worse claim than naming none.
 *
 * @param percent the percent, already formatted by `FormatService.percent`
 * @param basis `BookingTerms.commissionBasis` or `BusinessRules.commissionBasis`
 */
export function commissionRate(t: Translate, percent: string, basis: string | null | undefined): string {
  switch (basis) {
    case 'OneDay':
      return t('commission.percentOfOneDailyRate', { percent });
    case 'RentalTotal':
      return t('commission.percentOfRentalTotal', { percent });
    default:
      return percent;
  }
}
