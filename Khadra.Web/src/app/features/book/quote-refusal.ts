import { AppConfig } from '../../core/api/app-config.api';
import { ProblemSnapshot } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { TranslationKey } from '../../core/i18n/en';
import { Language, MessageParams } from '../../core/i18n/language';

type Translate = (key: TranslationKey, params?: MessageParams) => string;

/** The refusals a quote words by the office's hours, which a customer moves by changing a time or choosing delivery. */
const HOURS_CODES = ['booking.pickup_outside_opening_hours', 'booking.return_outside_opening_hours'];

/**
 * Why a quote for these dates was refused, in the reader's words (Wave 3 E7; E2E F1): the one wording the car page and
 * the booking page share, so a customer reads the same sentence on both. The office's hours by name; dates the platform
 * will not take with the limit they broke, from `/app-config`; anything else the price could not be worked out for, or
 * the server's own refusal.
 */
export function quoteRefusalText(
  problem: ProblemSnapshot,
  t: Translate,
  language: Language,
  config: AppConfig | null,
  dateTime: (value: Date) => string,
): string {
  if (problem.code && HOURS_CODES.includes(problem.code))
    return t(`book.refusal.${problem.code}` as TranslationKey);
  switch (problem.code) {
    case 'booking.too_soon':
    case 'booking.period_in_past':
      return config
        ? t('search.tooSoon', { when: dateTime(new Date(Date.now() + config.minimumBookingLeadTimeMinutes * 60_000)) })
        : t('quote.failed');
    case 'booking.beyond_horizon':
      return config ? t('search.tooFar', { days: config.maxAdvanceBookingDays }) : t('quote.failed');
    case 'booking.rental_too_long':
      return config ? t('search.tooLong', { days: config.maxRentalDays }) : t('quote.failed');
    case 'period.end_before_start':
      return t('search.returnBeforePickup');
    default:
      break;
  }
  return problem.status === 400 ? t('quote.failed') : problemText(problem, t, language, config);
}

/** Whether a refusal is about the office's counter hours, so delivery would answer it where the car can be delivered. */
export function refusedForOpeningHours(problem: ProblemSnapshot | null): boolean {
  return !!problem?.code && HOURS_CODES.includes(problem.code);
}
