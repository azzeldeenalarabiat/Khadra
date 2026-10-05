import { TranslationKey } from './en';
import { MessageParams } from './language';
import { spellEnumName } from './status-key';

/**
 * A legal document's name in the reader's language (Wave 2 G1), from the server's kind name: the legal screen, the
 * audit log and the dashboard's activity strip all name it, and all three name it this way. A kind this build does
 * not know is spelled out from its name, never guessed.
 */
export function legalKindLabel(kind: string, t: (key: TranslationKey, params?: MessageParams) => string): string {
  if (kind === 'Terms') return t('legal.terms');
  if (kind === 'Privacy') return t('legal.privacy');
  return spellEnumName(kind);
}
