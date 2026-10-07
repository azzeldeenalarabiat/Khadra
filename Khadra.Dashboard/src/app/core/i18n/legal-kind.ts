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

/** One piece of the consent sentence: words, or a text's name, linked when its page is known. */
export interface ConsentPart {
  readonly text: string;
  /** The text's public page; null for the joining words, and for a text whose page the API could not name. */
  readonly url: string | null;
}

/**
 * "I have read and accept the Terms of Service and the Privacy notice", in pieces, for the texts in force (Wave 4,
 * W4-8). Empty when nothing is in force: there is nothing to accept. The joining words carry their own spaces, so the
 * Arabic "و" joins the name that follows it.
 */
export function consentSentence(
  texts: readonly { readonly kind: string; readonly url: string | null }[],
  t: (key: TranslationKey, params?: MessageParams) => string,
): ConsentPart[] {
  if (texts.length === 0) return [];
  const parts: ConsentPart[] = [{ text: t('consent.agreeLead'), url: null }];
  texts.forEach((text, index) => {
    if (index > 0) parts.push({ text: t('consent.agreeAnd'), url: null });
    parts.push({ text: legalKindLabel(text.kind, t), url: text.url });
  });
  return parts;
}
