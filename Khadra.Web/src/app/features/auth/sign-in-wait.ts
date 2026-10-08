import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';

/**
 * The wait the server named, in the unit a person counts in. The address limit asks for seconds; the per-account
 * ceiling on failed sign-ins (pre-launch item 51) asks for up to fifteen minutes, which "900 seconds" words badly.
 */
export function rateLimitedMessage(
  t: (key: TranslationKey, params?: MessageParams) => string,
  retryAfterSeconds: number | null,
): string {
  if (retryAfterSeconds === null) return t('signIn.rateLimited');
  if (retryAfterSeconds < 60) return t('signIn.rateLimitedFor', { count: retryAfterSeconds });
  return t('signIn.rateLimitedForMinutes', { count: Math.ceil(retryAfterSeconds / 60) });
}
