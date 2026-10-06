import { AppConfig } from '../api/app-config.api';
import { TranslationKey } from '../i18n/en';
import { Language, MessageParams } from '../i18n/language';
import { ProblemSnapshot } from './problem';

type Translate = (key: TranslationKey, params?: MessageParams) => string;

/**
 * Refusals the website words itself, from the server's stable `code` — the only part of a
 * ProblemDetails a client may translate. Anything unmapped falls back to the server's own English
 * sentence on an English page, and to a plain "could not be done" on an Arabic one, so an Arabic page
 * never shows English prose.
 */
const WORDED: Readonly<Record<string, TranslationKey>> = {
  'auth.email_taken': 'problem.emailTaken',
  'auth.phone_taken': 'problem.phoneTaken',
  'auth.invalid_email': 'problem.invalidEmail',
  'auth.invalid_phone': 'problem.invalidPhone',
  'auth.invalid_name': 'problem.invalidName',
  'auth.password_policy': 'problem.passwordPolicy',
  'auth.password_unchanged': 'problem.passwordUnchanged',
  'auth.invalid_credentials': 'problem.invalidCredentials',
  'auth.date_of_birth_required': 'problem.dateOfBirthRequired',
  'auth.invalid_date_of_birth': 'problem.invalidDateOfBirth',
  'auth.invalid_token': 'problem.invalidToken',
  'auth.account_suspended': 'signIn.suspended',
  'auth.email_not_verified': 'signIn.unverified',
  rate_limited: 'problem.rateLimited',
  // Payments Phase 6: a PDF not drawn yet. A voided document's is its voided copy, which is handed out like any other.
  'financial_documents.pdf_not_ready': 'invoices.pdf.preparing',
  // Opening and answering a dispute on the website (Wave 3 C4).
  'dispute.booking_not_disputable': 'dispute.open.notNow',
  'dispute.already_open': 'dispute.open.alreadyOpen',
  'dispute.reason_required': 'dispute.open.reasonRequired',
  'dispute.statement_required': 'dispute.add.required',
  'dispute.invalid_evidence_type': 'dispute.evidence.refused',
  'dispute.evidence_not_uploaded': 'dispute.evidence.refused',
  'dispute.evidence_outside_booking': 'dispute.evidence.refused',
  'upload.already_stored': 'dispute.evidence.refused',
  'dispute.not_open': 'dispute.closedNow',
  'dispute.already_resolved': 'dispute.closedNow',
  'dispute.already_withdrawn': 'dispute.closedNow',
  'dispute.only_opener_can_withdraw': 'dispute.onlyOpener',
};

export function problemText(
  problem: ProblemSnapshot,
  t: Translate,
  language: Language,
  config: AppConfig | null,
): string {
  if (problem.code === 'auth.under_minimum_age') {
    // The age is the owner's setting, published in /app-config; never a figure typed here.
    return config?.minimumRenterAge != null
      ? t('problem.underMinimumAge', { age: config.minimumRenterAge })
      : t('problem.underMinimumAgeNoFigure');
  }
  if (problem.status === 429) return t('problem.rateLimited');
  const key = problem.code ? WORDED[problem.code] : undefined;
  if (key) return t(key);
  if (problem.status === 0 || problem.status >= 500) return t('state.unavailable.title');
  if (language === 'en' && problem.title) return problem.title;
  return t('problem.unknown');
}
