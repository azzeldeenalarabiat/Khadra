import { TranslationKey } from '../../core/i18n/en';
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence } from '../../core/i18n/problem';
import { legalKindLabel as kindLabel } from '../../core/i18n/legal-kind';
import { spellEnumName } from '../../core/i18n/status-key';
import { Tone } from '../../core/models/console.models';
import { LegalKindName, LegalTextsRequest } from '../../core/models/legal.api';
import { Translate } from '../dealer/renter-documents.presenter';

/**
 * The legal texts' screen as rules rather than markup (Wave 2 G1). Every figure and every word about a version
 * comes from the server. This only chooses the sentence.
 */

/** A document's name in the reader's language; a kind this build does not know, spelled out. */
export function legalKindLabel(kind: string, t: Translate): string {
  return kindLabel(kind, t);
}

/** A version's state: the one in force is the good news, every replaced one is history. */
export function legalStateTone(state: string): Tone {
  return state === 'Current' ? 'ok' : 'dim';
}

export function legalStateLabel(state: string, t: Translate): string {
  if (state === 'Current') return t('legal.stateCurrent');
  if (state === 'Superseded') return t('legal.stateSuperseded');
  return spellEnumName(state);
}

/** Why a text could not be published, as the server's stable reason. */
const TEXT_REASONS: Readonly<Record<string, TranslationKey>> = {
  html: 'legal.reasonHtml',
  image: 'legal.reasonImage',
  link: 'legal.reasonLink',
  heading: 'legal.reasonHeading',
  code: 'legal.reasonCode',
  unsupported: 'legal.reasonUnsupported',
};

const WORDED: Readonly<Record<string, TranslationKey>> = {
  'legal.label_invalid': 'legal.labelInvalid',
  'legal.label_taken': 'legal.labelTaken',
  'legal.body_required': 'legal.bodyRequired',
  'legal.body_too_long': 'legal.bodyTooLong',
  'legal.body_invalid_characters': 'legal.bodyInvalidCharacters',
  'legal.publish_conflict': 'legal.publishConflict',
  'legal.kind_unknown': 'legal.kindUnknown',
  'legal.version_not_found': 'legal.versionNotFound',
};

/**
 * A refused preview or publish, in the reader's language. A text the platform cannot publish is answered with
 * WHERE: which language's text, which line, and why, so the administrator can find it.
 */
export function legalRefusal(problem: ProblemSnapshot, t: Translate, language: Language): string {
  if (problem.code === 'legal.text_unsupported') {
    const where = problem.textProblem;
    if (!where) return t('legal.textUnsupportedSomewhere');
    return t('legal.textUnsupported', {
      text: t(where.language === 'ar' ? 'legal.arabicText' : 'legal.englishText'),
      line: where.line,
      reason: t(TEXT_REASONS[where.reason] ?? 'legal.reasonUnsupported'),
    });
  }
  const key = problem.code ? WORDED[problem.code] : undefined;
  if (key) return t(key);
  return serverSentence(problem, language, t) ?? t('dealerDelivery.serviceDidNotRespond');
}

/**
 * What a preview was taken of, so the screen knows when it no longer matches the form. A preview is shown only
 * for the texts it rendered, and publishing needs one that matches the form exactly, because the version
 * published is the version previewed.
 */
export function previewKey(request: LegalTextsRequest): string {
  return JSON.stringify([request.kind, request.versionLabel.trim(), request.bodyEn, request.bodyAr]);
}

/** The kinds a new version may be published for, in the order the screen offers them. */
export function isLegalKind(kind: string): kind is LegalKindName {
  return kind === 'Terms' || kind === 'Privacy';
}
