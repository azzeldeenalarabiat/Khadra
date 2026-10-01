import { TranslationKey } from '../../core/i18n/en';
import { issueLines } from '../../core/i18n/issue-words';
import { MessageParams } from '../../core/i18n/language';
import { EnumFamily, StatusScope } from '../../core/i18n/status-key';
import { Tone } from '../../core/models/console.models';
import {
  AdminFinancialDocument,
  AdminFinancialDocumentListItem,
  Bilingual,
  FinancialDocument,
  FinancialDocumentEmail,
  FinancialDocumentEmailAttempt,
  FinancialDocumentHold,
  FinancialDocumentLink,
  FinancialDocumentRendition,
  PendingFinancialDocument,
} from '../../core/models/financial-documents.api';
import { Money } from '../../core/models/fleet.api';
import { DocumentContent, DocumentValue, readDocumentContent, recordedFacts } from './financial-document-content';

/**
 * Issued financial documents in the administrator's words (payments Phase 5b). Two kinds of text meet
 * here, and they come from different places on purpose:
 *
 * - what is INSIDE a document — every label, heading, figure and time — is the stored document itself,
 *   read in the console's language; nothing is worded or computed here;
 * - what SURROUNDS it — its standing, its versions, its void, its proof, "being prepared", a hold — is a
 *   live fact the server sends beside it, worded from the dictionaries like any other screen.
 */

type Translate = (key: TranslationKey, params?: MessageParams) => string;
type EnumLabel = (family: EnumFamily, name: string | null | undefined) => string;
type StatusLabel = (name: string | null | undefined, scope?: StatusScope) => string;

/** The words a screen hands in: the dictionaries, the two label helpers, and the language on screen. */
export interface DocumentWords {
  readonly t: Translate;
  readonly enumLabel: EnumLabel;
  readonly statusLabel: StatusLabel;
  /** Which half of a stored bilingual text to show. */
  readonly arabic: boolean;
}

/** How these screens print: live figures as every screen does, and a document's own as it stored them. */
export interface DocumentFormat {
  money(value: Money): string;
  when(iso: string): string;
  /** "in 3 hr": when a hold is tried again. */
  relative(iso: string): string;
  storedMoney(amount: string, currency: string): string;
  frozenTime(local: string): string;
  /** A whole number, grouped in the console's language: a PDF's size in bytes. */
  count(value: number): string;
}

export function pick(text: Bilingual, arabic: boolean): string {
  return arabic ? text.ar : text.en;
}

export interface StandingView {
  readonly label: string;
  readonly tone: Tone;
}

const STANDING_TONES: Readonly<Record<string, Tone>> = { Current: 'ok', Superseded: 'dim', Voided: 'bad' };

/**
 * A document's standing, always shown on this console — a current one says so, because an auditor
 * comparing versions has to see which one counts. A standing this build does not know is spelled out
 * from the server's name by `statusLabel`, never left blank.
 */
export function standing(status: string, words: DocumentWords): StandingView {
  return { label: words.statusLabel(status, 'financialDocument'), tone: STANDING_TONES[status] ?? 'dim' };
}

// ── Lists ─────────────────────────────────────────────────────────────────────────────────────────────

export interface DocumentRow {
  readonly id: string;
  readonly number: string;
  readonly isTest: boolean;
  /** The stored title, in the console's language. */
  readonly title: string;
  readonly standing: StandingView;
  readonly version: number;
  readonly bookingId: string;
  readonly reference: string;
  readonly headlineLabel: string;
  /** A list row is a live screen: its figure is a plain number, through the live formatter. */
  readonly headline: string;
  readonly issued: string;
}

export function documentRow(row: AdminFinancialDocumentListItem, words: DocumentWords, format: DocumentFormat): DocumentRow {
  return {
    id: row.documentId,
    number: row.number,
    isTest: row.isTest,
    title: pick(row.title, words.arabic),
    standing: standing(row.status, words),
    version: row.version,
    bookingId: row.bookingId,
    reference: row.bookingReference,
    headlineLabel: pick(row.headline.label, words.arabic),
    headline: format.money(row.headline.amount),
    issued: format.when(row.issuedAt),
  };
}

export interface HoldRow {
  readonly id: string;
  readonly type: string;
  readonly bookingId: string;
  readonly reference: string | null;
  readonly reason: string;
  readonly attempts: number;
  readonly firstFailed: string;
  readonly lastFailed: string;
  readonly nextAttempt: string;
  /** The issues the booking's records need reviewing for, worded (pre-launch item 219); empty for any other reason. */
  readonly issues: readonly string[];
  /** Any other failure: the server's own log line, machine English, shown as it is, left to right. */
  readonly lastError: string | null;
}

export function holdRow(hold: FinancialDocumentHold, words: DocumentWords, format: DocumentFormat): HoldRow {
  // A document held because the booking's records need review carries the calculator's issue codes: each attempt
  // writes its reason and its error together, so this reason never sits beside some other failure's log line.
  const review = hold.reason === 'RecordsNeedReview';
  return {
    id: hold.holdId,
    type: words.enumLabel('financialDocumentType', hold.documentType),
    bookingId: hold.bookingId,
    reference: hold.bookingReference,
    reason: words.enumLabel('financialDocumentHoldReason', hold.reason),
    attempts: hold.attempts,
    firstFailed: format.when(hold.firstFailedAt),
    lastFailed: format.when(hold.lastFailedAt),
    nextAttempt: format.relative(hold.nextAttemptAt),
    issues: review ? issueLines(hold.lastError, words.enumLabel) : [],
    lastError: review ? null : hold.lastError,
  };
}

export interface PreparingRow {
  readonly key: string;
  /** "Payment receipt — being prepared", by kind; a kind this build does not know, plainly. */
  readonly label: string;
  /** When the money it will record moved, so two payments give two rows a reader can tell apart. */
  readonly date: string;
}

const PREPARING_KEYS: Readonly<Record<string, TranslationKey>> = {
  PaymentReceipt: 'financialDocuments.preparingPaymentReceipt',
  RefundReceipt: 'financialDocuments.preparingRefundReceipt',
  BookingStatement: 'financialDocuments.preparingBookingStatement',
};

export function preparingRow(pending: PendingFinancialDocument, words: DocumentWords, format: DocumentFormat): PreparingRow {
  return {
    key: `${pending.type}:${pending.subjectId}`,
    label: words.t(PREPARING_KEYS[pending.type] ?? 'financialDocuments.preparingOther'),
    date: format.when(pending.occurredAt),
  };
}

// ── One document, as it was issued ────────────────────────────────────────────────────────────────────

/**
 * How a literal the document stored as registered (a `plain` value) keeps its direction — part of the
 * reader's contract, so the website and the app do the same:
 *
 * - `ltr`: Latin — a number, a reference, a plate, an e-mail, a phone — kept left to right whatever
 *   surrounds it; "+962 6 000 0000" has no strong character, and left to itself on an Arabic screen its
 *   groups would be reordered;
 * - `auto`: anything written with an Arabic (or Hebrew) letter — a name as registered — which takes its
 *   own direction from its first strong character;
 * - null: every other kind of value, which the stored text or the formatter has already isolated.
 */
export type LiteralDirection = 'ltr' | 'auto' | null;

const RTL_LETTER = /[\u0590-\u08FF\uFB1D-\uFDFF\uFE70-\uFEFF]/;

export interface DocumentLineView {
  readonly key: string;
  readonly label: string;
  readonly value: string;
  readonly direction: LiteralDirection;
}

/**
 * A section's lines in their stored order: labelled lines together as a description list, and a line
 * with no label — a value that stands alone, such as "Paid in full online" — as a paragraph of its own.
 */
export type DocumentBlock =
  | { readonly kind: 'lines'; readonly key: string; readonly lines: readonly DocumentLineView[] }
  | { readonly kind: 'alone'; readonly key: string; readonly text: string; readonly direction: LiteralDirection };

export interface DocumentSectionView {
  readonly key: string;
  readonly heading: string;
  readonly blocks: readonly DocumentBlock[];
}

export interface DocumentBodyView {
  readonly title: string;
  readonly headlineLabel: string;
  readonly headline: string;
  readonly sections: readonly DocumentSectionView[];
  readonly timeNote: string | null;
  readonly notice: string | null;
}

/**
 * Whether the document's body shows a line (owner, 2026-09-29): every line but the commercial registrations —
 * Khadra's, the rental office's and a customer's — which the customer's page and the PDF leave out too. The console
 * keeps them, in its proof of issue ({@link registrationsOf}); the stored document keeps them all.
 */
export function shownInBody(sectionKey: string, lineKey: string): boolean {
  return !(sectionKey === 'parties' && ['issuerRegistration', 'officeRegistration', 'customerRegistration'].includes(lineKey));
}

/** The stored document, in the console's language, exactly as its customer reads it. */
export function documentBody(content: DocumentContent, arabic: boolean, format: DocumentFormat): DocumentBodyView {
  return {
    title: pick(content.title, arabic),
    headlineLabel: pick(content.headline.label, arabic),
    headline: format.storedMoney(content.headline.amount, content.headline.currency),
    sections: content.sections.map((section, index) => ({
      key: `${index}:${section.key}`,
      heading: pick(section.heading, arabic),
      blocks: blocksOf(section.key, section.lines, arabic, format),
    })),
    timeNote: content.timeNote ? pick(content.timeNote, arabic) : null,
    notice: content.notice ? pick(content.notice, arabic) : null,
  };
}

/**
 * The lines the body leaves out — the commercial registrations — exactly as the document stores them, label and
 * all, for the administrator's proof of issue. Not a customer's reading: internal, like the rest of that section.
 */
export function registrationsOf(content: DocumentContent, arabic: boolean, format: DocumentFormat): DocumentLineView[] {
  return content.sections.flatMap((section, index) =>
    section.lines
      .map((line, position) => ({ line, position }))
      .filter(({ line }) => line.label !== null && !shownInBody(section.key, line.key))
      .map(({ line, position }) => ({
        key: `${index}:${position}:${line.key}`,
        label: pick(line.label!, arabic),
        value: valueText(line.value, arabic, format),
        direction: directionOf(line.value),
      })),
  );
}

function blocksOf(
  sectionKey: string,
  lines: DocumentContent['sections'][number]['lines'],
  arabic: boolean,
  format: DocumentFormat,
): DocumentBlock[] {
  const blocks: DocumentBlock[] = [];
  lines.forEach((line, position) => {
    if (!shownInBody(sectionKey, line.key)) return;
    const key = `${position}:${line.key}`;
    const value = valueText(line.value, arabic, format);
    const direction = directionOf(line.value);
    if (!line.label) {
      blocks.push({ kind: 'alone', key, text: value, direction });
      return;
    }
    const last = blocks.at(-1);
    const view: DocumentLineView = { key, label: pick(line.label, arabic), value, direction };
    if (last?.kind === 'lines') blocks[blocks.length - 1] = { ...last, lines: [...last.lines, view] };
    else blocks.push({ kind: 'lines', key, lines: [view] });
  });
  return blocks;
}

export function directionOf(value: DocumentValue): LiteralDirection {
  if (value.kind !== 'plain') return null;
  return RTL_LETTER.test(value.plain) ? 'auto' : 'ltr';
}

function valueText(value: DocumentValue, arabic: boolean, format: DocumentFormat): string {
  switch (value.kind) {
    case 'money':
      return format.storedMoney(value.amount, value.currency);
    case 'instant':
      return format.frozenTime(value.local);
    case 'text':
      return pick(value.text, arabic);
    case 'plain':
      return value.plain;
  }
}

// ── A document's page ─────────────────────────────────────────────────────────────────────────────────

export interface LinkView {
  readonly id: string;
  readonly number: string;
  /** "Version 2", or the kind of document it is. */
  readonly detail: string;
  readonly standing: StandingView;
  /** The document this page shows: listed, not linked. */
  readonly here: boolean;
}

export interface ProofRow {
  readonly k: string;
  readonly v: string;
  /** A machine value — a provider name, a hash — shown as it is, left to right. */
  readonly code: boolean;
}

export interface DocumentPageView {
  readonly id: string;
  readonly number: string;
  readonly isTest: boolean;
  readonly title: string;
  readonly standing: StandingView;
  readonly versionOf: string;
  readonly type: string;
  readonly cause: string;
  readonly issued: string;
  readonly occurred: string;
  readonly bookingId: string;
  readonly reference: string;
  readonly dealerId: string;
  readonly customerId: string;
  /** The stored document, or null when this console cannot show it whole. */
  readonly body: DocumentBodyView | null;
  /** What the page still states when it cannot: the stored title's figure, live, and the issue time. */
  readonly headlineLabel: string;
  readonly headline: string;
  /** The snapshot's recorded facts, pretty-printed: evidence, not a customer's reading. */
  readonly facts: string | null;
  readonly proof: readonly ProofRow[];
  /** The commercial registrations the body leaves out, as stored: shown in the proof of issue, never to a customer. */
  readonly registrations: readonly DocumentLineView[];
  /** On an earlier version: the NEWEST version, never merely the next one. */
  readonly newer: LinkView | null;
  readonly versions: readonly LinkView[];
  readonly paymentReceipt: LinkView | null;
  readonly refundReceipts: readonly LinkView[];
  readonly voided: {
    readonly when: string;
    readonly by: string;
    readonly reason: string;
    readonly replacement: LinkView | null;
  } | null;
  /** Only a CURRENT document can be voided; a superseded or voided one can never become current. */
  readonly canVoid: boolean;
  /**
   * A receipt: its correction brings the booking's statement a new version, issued by the next settlement
   * pass rather than with the void (owner, 2026-09-28), so the void dialog says so.
   */
  readonly statementFollows: boolean;
  /** Its PDFs (payments Phase 6): each one drawn, with its proof, and a download per kind and language. */
  readonly pdf: PdfSectionView;
  /** Its emails to the customer (payments Phase 7); null from a server that does not report them. */
  readonly emails: EmailSectionView | null;
}

/** One PDF download: which PDF it is, its button, and the name the saved file gets. */
export interface PdfDownloadView {
  /** `kind:language`, unique on the page. */
  readonly key: string;
  readonly language: string;
  /** `AsIssued`, or `Voided` for the copy a voided document's customer is given. */
  readonly kind: string;
  readonly label: string;
  readonly fileName: string;
}

/** One PDF drawn of the document: which kind, language and template, and the proof of what was stored. */
export interface PdfRenditionView {
  readonly key: string;
  readonly title: string;
  readonly rows: readonly ProofRow[];
}

export interface PdfSectionView {
  /**
   * One per kind and language drawn — the document as issued first, English first: what a download serves is that
   * PDF's newest template.
   */
  readonly downloads: readonly PdfDownloadView[];
  /** Every PDF drawn, as issued first, English first, newest template first. */
  readonly renditions: readonly PdfRenditionView[];
  /** The server's word that a PDF the customer will be offered is still being drawn. */
  readonly preparing: boolean;
  /**
   * Voided (owner, 2026-09-29): its customer is given the voided copies, stamped VOID and naming the correction;
   * the original as issued, unstamped, stays the administrators'.
   */
  readonly voided: boolean;
}

interface PdfLanguageWords {
  readonly name: TranslationKey;
  /** A current document's PDF. */
  readonly download: TranslationKey;
  /** A voided document's original, as issued and unstamped. */
  readonly original: TranslationKey;
  /** A voided document's voided copy. */
  readonly voidedCopy: TranslationKey;
}

/** The two languages a PDF is drawn in; one this console has never heard of is not shown. */
const PDF_LANGUAGES: Readonly<Record<string, PdfLanguageWords>> = {
  en: {
    name: 'financialDocuments.pdfEnglish',
    download: 'financialDocuments.pdfDownloadEn',
    original: 'financialDocuments.pdfDownloadOriginalEn',
    voidedCopy: 'financialDocuments.pdfDownloadVoidedEn',
  },
  ar: {
    name: 'financialDocuments.pdfArabic',
    download: 'financialDocuments.pdfDownloadAr',
    original: 'financialDocuments.pdfDownloadOriginalAr',
    voidedCopy: 'financialDocuments.pdfDownloadVoidedAr',
  },
};

/** The two kinds of PDF, in the order they are listed; a kind this console has never heard of is not shown. */
const PDF_KINDS: readonly string[] = ['AsIssued', 'Voided'];

export function pdfSection(page: AdminFinancialDocument, words: DocumentWords, format: DocumentFormat): PdfSectionView {
  const { t } = words;
  const voided = page.document.status === 'Voided';
  const order = (language: string) => (language === 'en' ? 0 : 1);
  // A server that predates the kind drew nothing but the document as issued.
  const kindOf = (rendition: FinancialDocumentRendition) => rendition.kind ?? 'AsIssued';
  const drawn = (page.renditions ?? [])
    .filter(
      (rendition) =>
        rendition.format === 'Pdf' && Object.hasOwn(PDF_LANGUAGES, rendition.language) && PDF_KINDS.includes(kindOf(rendition)),
    )
    .sort(
      (a, b) =>
        PDF_KINDS.indexOf(kindOf(a)) - PDF_KINDS.indexOf(kindOf(b)) ||
        order(a.language) - order(b.language) ||
        b.templateVersion - a.templateVersion,
    );
  const pairs = [...new Map(drawn.map((rendition) => [`${kindOf(rendition)}:${rendition.language}`, rendition])).values()];
  const label = (kind: string, language: string): TranslationKey =>
    kind === 'Voided' ? PDF_LANGUAGES[language].voidedCopy : voided ? PDF_LANGUAGES[language].original : PDF_LANGUAGES[language].download;
  const title = (kind: string): TranslationKey =>
    kind === 'Voided' ? 'financialDocuments.pdfTitleVoided' : voided ? 'financialDocuments.pdfTitleOriginal' : 'financialDocuments.pdfTitle';

  return {
    downloads: pairs.map((rendition) => {
      const kind = kindOf(rendition);
      return {
        key: `${kind}:${rendition.language}`,
        language: rendition.language,
        kind,
        label: t(label(kind, rendition.language)),
        fileName:
          kind === 'Voided'
            ? `${page.document.number}-${rendition.language}-void.pdf`
            : `${page.document.number}-${rendition.language}.pdf`,
      };
    }),
    renditions: drawn.map((rendition) => ({
      key: `${kindOf(rendition)}:${rendition.language}:${rendition.templateVersion}`,
      title: t(title(kindOf(rendition)), {
        language: t(PDF_LANGUAGES[rendition.language].name),
        n: rendition.templateVersion,
      }),
      rows: [
        { k: t('financialDocuments.pdfRendered'), v: format.when(rendition.renderedAt), code: false },
        { k: t('financialDocuments.pdfRenderer'), v: rendition.rendererVersion, code: true },
        { k: t('financialDocuments.pdfSize'), v: t('financialDocuments.pdfBytes', { n: format.count(rendition.sizeBytes) }), code: false },
        { k: t('financialDocuments.pdfHash'), v: rendition.contentSha256, code: true },
        { k: t('financialDocuments.pdfDrawnFrom'), v: rendition.snapshotSha256, code: true },
      ],
    })),
    preparing: page.document.pdf?.preparing === true,
    voided,
  };
}

// ── Its emails (payments Phase 7) ─────────────────────────────────────────────────────────────────────

/** One attempt at an email: what it came to, and the proof of it. */
export interface EmailAttemptView {
  readonly key: string;
  /** "Attempt 2 · Accepted by the mail provider". */
  readonly title: string;
  readonly tone: Tone;
  readonly rows: readonly ProofRow[];
}

/** One email of the document: where it stands, who asked for it, and every attempt. */
export interface EmailView {
  readonly key: string;
  readonly standing: StandingView;
  /** "Queued when the receipt was issued", or the administrator who asked. */
  readonly requested: string;
  readonly rows: readonly ProofRow[];
  readonly attempts: readonly EmailAttemptView[];
}

export interface EmailSectionView {
  /** Newest first, as the server sends them. */
  readonly emails: readonly EmailView[];
  /**
   * Emailing it (again): absent for what is never emailed again — a statement, a voided receipt — and otherwise its
   * label, with `waiting` saying why it cannot be taken yet while an email is already on its way.
   */
  readonly action: { readonly label: string; readonly waiting: string | null } | null;
  /** Why it is not emailed at all, when that is the case: a statement, or a voided receipt. */
  readonly note: string | null;
}

/**
 * What each state reads as. `Sent` is ACCEPTED by the mail provider — not delivered, not read — and the section says
 * so rather than the pill; a queued email is work in progress, not yet a problem.
 */
const EMAIL_TONES: Readonly<Record<string, Tone>> = { Queued: 'accent', Sent: 'ok', Skipped: 'dim', Failed: 'bad' };
const ATTEMPT_TONES: Readonly<Record<string, Tone>> = { Accepted: 'ok', Failed: 'bad', Skipped: 'dim' };

/**
 * The emails of a document to its customer, as the administrator reads them: the history the server keeps, worded;
 * and whether it can be emailed again, which is the SERVER's word (`canEmailAgain`) — the console only says why not.
 */
export function emailSection(page: AdminFinancialDocument, words: DocumentWords, format: DocumentFormat): EmailSectionView | null {
  if (page.emails === undefined) return null;
  const { t } = words;
  const document = page.document;
  const onItsWay = page.emails.some((email) => email.state === 'Queued');
  // Only a receipt is emailed, and never once it is voided (owner, 2026-09-29); the server refuses both as well. A
  // server whose delivery is switched off sends none at all: its queued emails wait, and none is on its way.
  const note =
    document.type === 'BookingStatement'
      ? t('financialDocuments.emailsNotForStatements')
      : document.status === 'Voided'
        ? t('financialDocuments.emailsVoided')
        : page.emailDeliveryDisabled === true
          ? t('financialDocuments.emailsSwitchedOff')
          : null;

  return {
    emails: page.emails.map((email) => emailView(email, words, format)),
    action:
      page.canEmailAgain === true || (note === null && onItsWay)
        ? {
            label: t(page.emails.length ? 'financialDocuments.emailAgain' : 'financialDocuments.emailSend'),
            waiting: page.canEmailAgain === true ? null : t('financialDocuments.emailOnItsWay'),
          }
        : null,
    note,
  };
}

function emailView(email: FinancialDocumentEmail, words: DocumentWords, format: DocumentFormat): EmailView {
  const { t } = words;
  const rows: ProofRow[] = [{ k: t('financialDocuments.emailQueuedAt'), v: format.when(email.queuedAt), code: false }];
  if (email.waitingFor) {
    const reason = words.enumLabel('financialDocumentEmailWait', email.waitingFor);
    rows.push({
      k: t('financialDocuments.emailWaiting'),
      v: email.waitingSince ? t('financialDocuments.emailWaitingSince', { reason, since: format.when(email.waitingSince) }) : reason,
      code: false,
    });
  }
  if (email.completedAt) rows.push({ k: t('financialDocuments.emailFinished'), v: format.when(email.completedAt), code: false });
  if (email.recipient) rows.push({ k: t('financialDocuments.emailTo'), v: email.recipient, code: true });
  if (email.languages.length) {
    rows.push({
      k: t('financialDocuments.emailLanguages'),
      v: email.languages.map((language) => (Object.hasOwn(PDF_LANGUAGES, language) ? t(PDF_LANGUAGES[language].name) : language)).join(' · '),
      code: false,
    });
  }
  if (email.sendAttempts > 0) rows.push({ k: t('financialDocuments.emailSendAttempts'), v: format.count(email.sendAttempts), code: false });
  if (email.lastError) {
    rows.push({
      k: t(email.state === 'Skipped' ? 'financialDocuments.emailWhyNotSent' : 'financialDocuments.emailLastError'),
      v: email.lastError,
      code: true,
    });
  }

  return {
    key: email.deliveryId,
    standing: { label: words.statusLabel(email.state, 'financialDocumentEmail'), tone: EMAIL_TONES[email.state] ?? 'dim' },
    requested:
      email.requestedByAdminId === null
        ? t('financialDocuments.emailAskedAtIssue')
        : t('financialDocuments.emailAskedBy', { name: email.requestedByName ?? t('financialDocuments.emailByUnknown') }),
    rows,
    attempts: email.attempts.map((attempt) => attemptView(email.deliveryId, attempt, words, format)),
  };
}

function attemptView(deliveryId: string, attempt: FinancialDocumentEmailAttempt, words: DocumentWords, format: DocumentFormat): EmailAttemptView {
  const { t } = words;
  const rows: ProofRow[] = [{ k: t('financialDocuments.emailAttemptAt'), v: format.when(attempt.attemptedAt), code: false }];
  if (attempt.provider) rows.push({ k: t('financialDocuments.emailProvider'), v: attempt.provider, code: true });
  if (attempt.providerMessageId) rows.push({ k: t('financialDocuments.emailMessageId'), v: attempt.providerMessageId, code: true });
  if (attempt.error) {
    rows.push({
      k: t(attempt.outcome === 'Skipped' ? 'financialDocuments.emailWhy' : 'financialDocuments.emailError'),
      v: attempt.error,
      code: true,
    });
  }
  if (attempt.englishPdfSha256) rows.push({ k: t('financialDocuments.emailPdfEn'), v: attempt.englishPdfSha256, code: true });
  if (attempt.arabicPdfSha256) rows.push({ k: t('financialDocuments.emailPdfAr'), v: attempt.arabicPdfSha256, code: true });
  return {
    key: `${deliveryId}:${attempt.number}`,
    title: t('financialDocuments.emailAttempt', {
      n: attempt.number,
      outcome: words.enumLabel('financialDocumentEmailOutcome', attempt.outcome),
    }),
    tone: ATTEMPT_TONES[attempt.outcome] ?? 'dim',
    rows,
  };
}

/** The dialog's words: what is sent, to whom, and that it is recorded. */
export function emailDialogWords(number: string, t: Translate): { title: string; body: string; note: string; confirm: string } {
  return {
    title: t('financialDocuments.emailTitle', { number }),
    body: t('financialDocuments.emailBody'),
    note: t('financialDocuments.emailNote'),
    confirm: t('financialDocuments.emailConfirm'),
  };
}

/** "Email queued": what asking did — never "delivered", and no promise of when. */
export function emailQueuedToast(number: string, t: Translate): { title: string; body: string } {
  return { title: t('financialDocuments.emailQueuedTitle'), body: t('financialDocuments.emailQueuedBody', { number }) };
}

/**
 * The refusals after which asking again cannot help — the document is a statement, it was voided, an email is
 * already on its way, or this server sends none at all: the dialog closes, the refusal is a toast, and the page
 * reloads to show why. Anything else leaves the dialog open with the words, and nothing was queued.
 */
export function emailRefusalIsFinal(code: string | null): boolean {
  return (
    code === 'financial_documents.not_emailed' ||
    code === 'financial_documents.voided_not_emailed' ||
    code === 'financial_documents.email_already_queued' ||
    code === 'financial_documents.email_delivery_disabled'
  );
}

/** What the console reports about a document its reader refused whole. */
export interface RefusalReport {
  readonly documentId: string;
  readonly snapshotSchemaVersion: number;
}

/**
 * A document the reader refused whole is reported with its id and its schema version only — never the
 * snapshot, which holds a customer's name and their money (the reader's contract, docs/contracts/README.md).
 * Null for a document it shows.
 */
export function refusalReport(document: FinancialDocument): RefusalReport | null {
  return readDocumentContent(document.snapshotSchemaVersion, document.snapshot)
    ? null
    : { documentId: document.documentId, snapshotSchemaVersion: document.snapshotSchemaVersion };
}

export function documentPage(page: AdminFinancialDocument, words: DocumentWords, format: DocumentFormat): DocumentPageView {
  const { t } = words;
  const document = page.document;
  const content = readDocumentContent(document.snapshotSchemaVersion, document.snapshot);
  const versions = [...document.links.versions].sort((a, b) => a.version - b.version);
  // The newest is the highest member of the family — always current, because a void always carries its
  // correction. Never `nextVersion`: that is only the next member, and it can be a voided one.
  const newest = versions.at(-1);
  const facts = recordedFacts(document.snapshot);
  const link = (target: FinancialDocumentLink, detail: string): LinkView => ({
    id: target.documentId,
    number: target.number,
    detail,
    standing: standing(target.status, words),
    here: target.documentId === document.documentId,
  });

  const proof: ProofRow[] = [
    { k: t('financialDocuments.provider'), v: page.provider, code: true },
    { k: t('financialDocuments.contentHash'), v: page.contentSha256, code: true },
  ];
  if (page.coversThrough) {
    proof.push({ k: t('financialDocuments.coversThrough'), v: format.when(page.coversThrough), code: false });
  }
  if (page.checkpointFingerprint) {
    proof.push({ k: t('financialDocuments.checkpoint'), v: page.checkpointFingerprint, code: true });
  }

  return {
    id: document.documentId,
    number: document.number,
    isTest: page.isTest,
    title: pick(document.title, words.arabic),
    standing: standing(document.status, words),
    versionOf: t('financialDocuments.versionOf', {
      n: document.version,
      total: Math.max(versions.length, document.version),
    }),
    type: words.enumLabel('financialDocumentType', document.type),
    cause: words.enumLabel('financialDocumentCause', document.cause),
    issued: format.when(document.issuedAt),
    occurred: format.when(document.occurredAt),
    bookingId: document.bookingId,
    reference: document.bookingReference,
    dealerId: page.dealerId,
    customerId: page.customerId,
    body: content ? documentBody(content, words.arabic, format) : null,
    headlineLabel: pick(document.headline.label, words.arabic),
    headline: format.money(document.headline.amount),
    facts: facts ? JSON.stringify(facts, null, 2) : null,
    proof,
    registrations: content ? registrationsOf(content, words.arabic, format) : [],
    newer:
      document.status === 'Superseded' && newest && newest.documentId !== document.documentId
        ? link(newest, t('financialDocuments.version', { n: newest.version }))
        : null,
    versions:
      versions.length > 1
        ? versions.map((member) => link(member, t('financialDocuments.version', { n: member.version })))
        : [],
    paymentReceipt: document.links.paymentReceipt
      ? link(document.links.paymentReceipt, words.enumLabel('financialDocumentType', document.links.paymentReceipt.type))
      : null,
    refundReceipts: document.links.refundReceipts.map((member) =>
      link(member, words.enumLabel('financialDocumentType', member.type)),
    ),
    voided: page.void
      ? {
          when: format.when(page.void.voidedAt),
          by: page.void.voidedByName ?? t('financialDocuments.voidedByUnknown'),
          reason: page.void.reason,
          replacement: page.void.replacedBy
            ? link(page.void.replacedBy, t('financialDocuments.version', { n: page.void.replacedBy.version }))
            : null,
        }
      : null,
    canVoid: document.status === 'Current',
    statementFollows: document.type === 'PaymentReceipt' || document.type === 'RefundReceipt',
    pdf: pdfSection(page, words, format),
    emails: emailSection(page, words, format),
  };
}

// ── Voiding ───────────────────────────────────────────────────────────────────────────────────────────

/** The dialog's words: the consequence first, as every destructive action in this console states it. */
export interface VoidDialogWords {
  readonly title: string;
  readonly body: string;
  readonly note: string;
  readonly reasonLabel: string;
  readonly placeholder: string;
  readonly confirm: string;
}

/** @param statementFollows a receipt, whose correction the booking's statement follows shortly after. */
export function voidDialogWords(number: string, statementFollows: boolean, t: Translate): VoidDialogWords {
  const note = t('financialDocuments.voidNote');
  return {
    title: t('financialDocuments.voidTitle', { number }),
    body: t('financialDocuments.voidBody'),
    note: statementFollows ? `${note} ${t('financialDocuments.voidStatementNote')}` : note,
    reasonLabel: t('financialDocuments.voidReasonLabel'),
    placeholder: t('financialDocuments.voidReasonPlaceholder'),
    confirm: t('financialDocuments.voidConfirm'),
  };
}

/** "Voided TEST-PAY-2026-000001. Issued TEST-PAY-2026-000005." — what the void did, as the server reported it. */
export function voidedToast(voidedNumber: string, replacementNumber: string, t: Translate): { title: string; body: string } {
  return {
    title: t('financialDocuments.voidedTitle'),
    body: t('financialDocuments.voidedBody', { voided: voidedNumber, replacement: replacementNumber }),
  };
}

/**
 * The refusals after which the document can never be current again: a retry cannot succeed, so the
 * dialog closes, the refusal is a toast, and the page reloads to show the void or the newer version.
 * Every other refusal — the records need review, no issuer, the correction could not be composed, the
 * reason — leaves the dialog open with the words, and nothing was voided.
 */
export function voidRefusalIsFinal(code: string | null): boolean {
  return code === 'financial_documents.not_current' || code === 'financial_documents.already_voided';
}
