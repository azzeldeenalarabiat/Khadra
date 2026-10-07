import { LegalSlug } from '../../core/api/legal.api';
import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';

type Translate = (key: TranslationKey, params?: MessageParams) => string;

/** One piece of the consent sentence: words, or a text's name, linked to its page on this website. */
export interface ConsentPart {
  readonly text: string;
  /** The text's page here; null for the joining words. */
  readonly slug: LegalSlug | null;
}

/** The website's legal pages, in the order it lists them. */
const ORDER: readonly LegalSlug[] = ['terms', 'privacy'];

/** The texts in force that this website has a page for, in its order: what `/app-config` names, and nothing else. */
export function slugsInForce(documents: readonly { readonly slug: string }[]): LegalSlug[] {
  return ORDER.filter((slug) => documents.some((document) => document.slug === slug));
}

/** A text's name in the reader's language. */
export function legalName(slug: LegalSlug, t: Translate): string {
  return t(slug === 'privacy' ? 'legal.privacy' : 'legal.terms');
}

/**
 * "I have read and accept the Terms of Service and the Privacy notice", in pieces, for the texts in force (Wave 4,
 * W4-8). Empty when nothing is in force: there is nothing to accept, and the server asks for nothing. The joining words
 * carry their own spaces, so the Arabic "و" joins the name that follows it.
 */
export function consentSentence(slugs: readonly LegalSlug[], t: Translate): ConsentPart[] {
  if (slugs.length === 0) return [];
  const parts: ConsentPart[] = [{ text: t('consent.agreeLead'), slug: null }];
  slugs.forEach((slug, index) => {
    if (index > 0) parts.push({ text: t('consent.agreeAnd'), slug: null });
    parts.push({ text: legalName(slug, t), slug });
  });
  return parts;
}
