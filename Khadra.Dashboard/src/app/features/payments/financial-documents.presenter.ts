import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { EnumFamily, StatusScope } from '../../core/i18n/status-key';
import { Tone } from '../../core/models/console.models';
import {
  AdminFinancialDocument,
  AdminFinancialDocumentListItem,
  Bilingual,
  FinancialDocumentHold,
  FinancialDocumentLink,
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
  /** The server's own log line, machine English: shown as it is, left to right. */
  readonly lastError: string | null;
}

export function holdRow(hold: FinancialDocumentHold, words: DocumentWords, format: DocumentFormat): HoldRow {
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
    lastError: hold.lastError,
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

/** The stored document, in the console's language, exactly as it was issued. */
export function documentBody(content: DocumentContent, arabic: boolean, format: DocumentFormat): DocumentBodyView {
  return {
    title: pick(content.title, arabic),
    headlineLabel: pick(content.headline.label, arabic),
    headline: format.storedMoney(content.headline.amount, content.headline.currency),
    sections: content.sections.map((section, index) => ({
      key: `${index}:${section.key}`,
      heading: pick(section.heading, arabic),
      blocks: blocksOf(section.lines, arabic, format),
    })),
    timeNote: content.timeNote ? pick(content.timeNote, arabic) : null,
    notice: content.notice ? pick(content.notice, arabic) : null,
  };
}

function blocksOf(lines: DocumentContent['sections'][number]['lines'], arabic: boolean, format: DocumentFormat): DocumentBlock[] {
  const blocks: DocumentBlock[] = [];
  lines.forEach((line, position) => {
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

export function voidDialogWords(number: string, t: Translate): VoidDialogWords {
  return {
    title: t('financialDocuments.voidTitle', { number }),
    body: t('financialDocuments.voidBody'),
    note: t('financialDocuments.voidNote'),
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
