import { Money } from '../../core/api/common.api';
import {
  Bilingual,
  FINANCIAL_DOCUMENT_TYPES,
  FinancialDocumentLink,
  FinancialDocumentPage,
  FinancialDocumentRow,
  PendingFinancialDocument,
} from '../../core/api/financial-documents.api';
import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { DocumentContent, DocumentValue, readDocumentContent } from './document-content';

/**
 * Issued financial documents in words (payments Phase 5b). Two kinds of text meet here, and they come
 * from different places on purpose:
 *
 * - what is INSIDE a document — every label, heading, figure and time — is the stored document itself,
 *   read in the page's language; nothing is worded or computed here;
 * - what SURROUNDS it — its standing, the void notice, its versions, "being prepared" — is a live fact
 *   the server sends beside the document, worded from the dictionaries like any other live screen.
 */

type Translate = (key: TranslationKey, params?: MessageParams) => string;

/** What these pages format: live figures as every page does, and a document's own as it stored them. */
export interface InvoiceFormat {
  money(value: Money | null | undefined): string;
  date(value: string | null | undefined): string;
  storedMoney(amount: string, currency: string): string;
  frozenTime(local: string): string;
}

export function pick(text: Bilingual, arabic: boolean): string {
  return arabic ? text.ar : text.en;
}

export interface StandingView {
  readonly label: string;
  /** A badge modifier class, or none. */
  readonly tone: string;
  /** A standing this site does not know: the server's own name, spelled out and isolated. */
  readonly unknown: boolean;
}

/**
 * The chip beside a document. None for a current one. A standing this site does not know is shown as the
 * server named it rather than left out: saying nothing about a document's standing would present it as
 * current (payments Phase 5b plan §3.3).
 */
export function standing(status: string, t: Translate): StandingView | null {
  switch (status) {
    case 'Current':
      return null;
    case 'Superseded':
      return { label: t('invoices.status.superseded'), tone: '', unknown: false };
    case 'Voided':
      return { label: t('invoices.status.voided'), tone: 'badge--bad', unknown: false };
    default:
      return { label: status, tone: '', unknown: true };
  }
}

// ── Lists ─────────────────────────────────────────────────────────────────────────────────────────

export interface InvoiceRowView {
  readonly id: string;
  readonly title: string;
  readonly number: string;
  /** "Version 2" above the first, else null. */
  readonly version: string | null;
  readonly standing: StandingView | null;
  readonly bookingReference: string;
  readonly issued: string;
  readonly headlineLabel: string;
  readonly headline: string;
}

/** A row is a live screen: its figure arrives as a plain number, so it goes through the live formatter. */
export function invoiceRow(row: FinancialDocumentRow, arabic: boolean, t: Translate, format: InvoiceFormat): InvoiceRowView {
  return {
    id: row.documentId,
    title: pick(row.title, arabic),
    number: row.number,
    version: row.version > 1 ? t('invoices.version', { n: row.version }) : null,
    standing: standing(row.status, t),
    bookingReference: row.bookingReference,
    issued: t('invoices.issued', { date: format.date(row.issuedAt) }),
    headlineLabel: pick(row.headline.label, arabic),
    headline: format.money(row.headline.amount),
  };
}

export interface PreparingView {
  readonly key: string;
  readonly label: string;
  /** The date of the money it will record, so two payments give two rows a reader can tell apart. */
  readonly date: string;
}

export function preparingRow(pending: PendingFinancialDocument, t: Translate, format: InvoiceFormat): PreparingView {
  const known = (FINANCIAL_DOCUMENT_TYPES as readonly string[]).includes(pending.type);
  return {
    key: `${pending.type}:${pending.subjectId}`,
    label: t(known ? (`invoices.preparing.${pending.type}` as TranslationKey) : 'invoices.preparing.other'),
    date: format.date(pending.occurredAt),
  };
}

// ── One document ──────────────────────────────────────────────────────────────────────────────────

/**
 * How a literal the document stored as it was registered (a `plain` value) is isolated from the text
 * around it — part of the reader's contract (docs/contracts/README.md), so the app does the same:
 *
 * - `ltr`: Latin — a number, a reference, a plate, an e-mail, a phone — kept left-to-right whatever
 *   surrounds it; "+962 6 000 0000" has no strong character, and left to find its own direction on an
 *   Arabic page its groups would be reordered;
 * - `auto`: anything written with an Arabic (or Hebrew) letter — a customer's or an office's name as
 *   registered — which takes its own direction from its first strong character, so a mixed name keeps
 *   its halves and its punctuation in order;
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

/** The stored document, in the page's language, exactly as it was issued. */
export function documentBody(content: DocumentContent, arabic: boolean, format: InvoiceFormat): DocumentBodyView {
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
 * Whether a customer's page shows a line of the stored document (owner, 2026-09-29). The commercial registrations
 * stay off it, as they stay off the PDF's body: Khadra's, the rental office's and — while there are no business
 * accounts — a customer's. Presentation only: the reader has read every line (a broken one refuses the document
 * whole), and the stored document keeps them all.
 */
export function shownToCustomer(sectionKey: string, lineKey: string): boolean {
  return !(sectionKey === 'parties' && ['issuerRegistration', 'officeRegistration', 'customerRegistration'].includes(lineKey));
}

function blocksOf(
  sectionKey: string,
  lines: DocumentContent['sections'][number]['lines'],
  arabic: boolean,
  format: InvoiceFormat,
): DocumentBlock[] {
  const blocks: DocumentBlock[] = [];
  lines.forEach((line, position) => {
    if (!shownToCustomer(sectionKey, line.key)) return;
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

function valueText(value: DocumentValue, arabic: boolean, format: InvoiceFormat): string {
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

export interface LinkView {
  readonly id: string;
  readonly number: string;
  readonly detail: string;
  readonly standing: StandingView | null;
  /** The document this page shows: listed, not linked. */
  readonly here: boolean;
}

/**
 * A link worded as a sentence that names a document number, with the sentence also parted around the
 * number so the page can keep the number in one piece. Left to wrap on its own, "Issued against payment
 * receipt TEST-PAY-2026-000005" broke at a hyphen inside the number on a phone ("TEST-PAY-" /
 * "2026-000005"). `text` stays the whole sentence; `before` keeps the space that precedes the number.
 */
export interface NumberedLink {
  readonly id: string;
  readonly text: string;
  readonly before: string;
  readonly number: string;
  readonly after: string;
}

const FSI = String.fromCharCode(0x2068);
const PDI = String.fromCharCode(0x2069);

function numberedLink(id: string, text: string, number: string): NumberedLink {
  const at = text.indexOf(number);
  if (at < 0) return { id, text, before: text, number: '', after: '' };
  // An Arabic sentence carries the number between direction isolates; the span the page puts the number
  // in isolates it itself, so the isolates stay with the sentence's words rather than doubling up.
  const before = text.slice(0, at);
  const after = text.slice(at + number.length);
  return {
    id,
    text,
    before: before.endsWith(FSI) ? before.slice(0, -1) : before,
    number,
    after: after.startsWith(PDI) ? after.slice(1) : after,
  };
}

export interface InvoicePageView {
  readonly title: string;
  readonly number: string;
  readonly standing: StandingView | null;
  readonly versionOf: string;
  readonly bookingId: string;
  readonly booking: string;
  /** "Voided on …" — and, as a link to the correction, "Replaced by …". */
  readonly voided: { readonly text: string; readonly replacement: NumberedLink | null } | null;
  /** On an earlier version: the newest version, linked. */
  readonly newer: NumberedLink | null;
  /** The document itself, or null when this site cannot show it whole. */
  readonly body: DocumentBodyView | null;
  /** What the page still states when it cannot show the document: its figure and when it was issued. */
  readonly headlineLabel: string;
  readonly headline: string;
  readonly issued: string;
  readonly versions: readonly LinkView[];
  /** On a refund receipt: the payment receipt it was issued against, linked. */
  readonly paymentReceipt: NumberedLink | null;
  readonly refundReceipts: readonly LinkView[];
  /** The PDFs to download (payments Phase 6), and whether one is still being drawn. */
  readonly pdf: PdfView;
}

/** One PDF download: the language it is in, its button, and the name the saved file gets. */
export interface PdfDownloadView {
  readonly language: string;
  readonly label: string;
  readonly aria: string;
  readonly fileName: string;
}

export interface PdfView {
  readonly downloads: readonly PdfDownloadView[];
  readonly preparing: boolean;
}

interface PdfWords {
  readonly label: TranslationKey;
  readonly aria: TranslationKey;
}

/**
 * The two languages a PDF is drawn in; one this site has never heard of is not offered. A voided document's PDFs are
 * its voided copies — stamped VOID and naming its replacement (owner, 2026-09-29) — and are named as such, so a saved
 * file says what it is before it is opened.
 */
const PDF_LANGUAGES: Readonly<Record<string, { readonly current: PdfWords; readonly voided: PdfWords }>> = {
  en: {
    current: { label: 'invoices.pdf.en', aria: 'invoices.pdf.downloadEn' },
    voided: { label: 'invoices.pdf.voidEn', aria: 'invoices.pdf.downloadVoidEn' },
  },
  ar: {
    current: { label: 'invoices.pdf.ar', aria: 'invoices.pdf.downloadAr' },
    voided: { label: 'invoices.pdf.voidAr', aria: 'invoices.pdf.downloadVoidAr' },
  },
};

export function pdfView(page: FinancialDocumentPage, t: Translate): PdfView {
  const pdf = page.pdf;
  // The server offers a voided document's voided copies and nothing else; the page only names them so.
  const voided = page.voided != null;
  return {
    downloads: (pdf?.languages ?? [])
      .filter((language) => Object.hasOwn(PDF_LANGUAGES, language))
      .map((language) => {
        const words = voided ? PDF_LANGUAGES[language].voided : PDF_LANGUAGES[language].current;
        return {
          language,
          label: t(words.label),
          aria: t(words.aria),
          fileName: voided ? `${page.number}-${language}-void.pdf` : `${page.number}-${language}.pdf`,
        };
      }),
    preparing: pdf?.preparing === true,
  };
}

export function invoicePage(page: FinancialDocumentPage, arabic: boolean, t: Translate, format: InvoiceFormat): InvoicePageView {
  const content = readDocumentContent(page.snapshotSchemaVersion, page.snapshot);
  const versions = [...page.links.versions].sort((a, b) => a.version - b.version);
  // The newest is the highest member of the family — always current, because a void always carries its
  // correction. Never `nextVersion`: that is only the next member, and it can be a voided one.
  const newest = versions.at(-1);
  const replacement = page.voided?.replacedBy ?? null;

  return {
    title: pick(page.title, arabic),
    number: page.number,
    standing: standing(page.status, t),
    versionOf: t('invoices.versionOf', { n: page.version, total: Math.max(versions.length, page.version) }),
    bookingId: page.bookingId,
    booking: t('invoices.booking', { reference: page.bookingReference }),
    voided: page.voided
      ? {
          text: t(replacement ? 'invoices.voidedOnAnd' : 'invoices.voidedOn', { date: format.date(page.voided.voidedAt) }),
          replacement: replacement
            ? numberedLink(replacement.documentId, t('invoices.replacedBy', { number: replacement.number }), replacement.number)
            : null,
        }
      : null,
    newer:
      page.status === 'Superseded' && newest && newest.documentId !== page.documentId
        ? numberedLink(newest.documentId, t('invoices.newerVersion', { number: newest.number }), newest.number)
        : null,
    body: content ? documentBody(content, arabic, format) : null,
    headlineLabel: pick(page.headline.label, arabic),
    headline: format.money(page.headline.amount),
    issued: t('invoices.issued', { date: format.date(page.issuedAt) }),
    versions: versions.length > 1 ? versions.map((link) => linkView(link, page.documentId, t, true)) : [],
    paymentReceipt: page.links.paymentReceipt
      ? numberedLink(
          page.links.paymentReceipt.documentId,
          t('invoices.issuedAgainst', { number: page.links.paymentReceipt.number }),
          page.links.paymentReceipt.number,
        )
      : null,
    refundReceipts: page.links.refundReceipts.map((link) => linkView(link, page.documentId, t, false)),
    pdf: pdfView(page, t),
  };
}

function linkView(link: FinancialDocumentLink, here: string, t: Translate, asVersion: boolean): LinkView {
  const chip = standing(link.status, t);
  return {
    id: link.documentId,
    number: link.number,
    detail: asVersion ? t('invoices.version', { n: link.version }) : '',
    standing: asVersion && link.status === 'Current' ? { label: t('invoices.status.current'), tone: 'badge--ok', unknown: false } : chip,
    here: link.documentId === here,
  };
}
