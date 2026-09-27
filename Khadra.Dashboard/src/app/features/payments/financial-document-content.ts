import { Bilingual } from '../../core/models/financial-documents.api';

/**
 * The version 1 reader of an issued document's stored content (payments Phase 5b). The contract is in
 * `docs/contracts/README.md` ("Issued financial documents: the reader's contract"); the website and the
 * customer app read the same grammar the same way, and all three are proven against one shared fixture.
 *
 * A document renders WHOLE, or not at all: a financial record shown with a line quietly missing is worse
 * than none on screen. So this reader
 *
 * - IGNORES keys it does not know, anywhere, and keeps sections and lines in the order given — it never
 *   picks one out by its `key`;
 * - FAILS CLOSED (answers null) only on a structural break: a missing title, headline label or money, a
 *   section heading or its lines, a line's key; a line with no value or with more than one; a text that
 *   is not two strings; and a schema version it does not know;
 * - DEGRADES nothing itself: an amount or a time off its pattern is still read, and `FormatService`
 *   prints it as it is rather than hiding the record over a missing leading zero.
 */

/** The schema versions this console renders. A new one is added beside version 1, never in its place. */
export const SUPPORTED_SCHEMA_VERSIONS: readonly number[] = [1];

export type DocumentValue =
  | { readonly kind: 'money'; readonly amount: string; readonly currency: string }
  | { readonly kind: 'instant'; readonly utc: string; readonly local: string }
  | { readonly kind: 'text'; readonly text: Bilingual }
  | { readonly kind: 'plain'; readonly plain: string };

export interface DocumentLine {
  readonly key: string;
  /** Null for a line that is its value alone ("Paid in full online…"). */
  readonly label: Bilingual | null;
  readonly value: DocumentValue;
}

export interface DocumentSection {
  readonly key: string;
  readonly heading: Bilingual;
  readonly lines: readonly DocumentLine[];
}

export interface DocumentContent {
  readonly title: Bilingual;
  readonly headline: { readonly label: Bilingual; readonly amount: string; readonly currency: string };
  readonly sections: readonly DocumentSection[];
  /** Optional: absent, nothing is shown. */
  readonly timeNote: Bilingual | null;
  /** Optional: absent, nothing is shown — no client ever writes the tax-invoice sentence itself. */
  readonly notice: Bilingual | null;
}

/** A structural break: the document is refused whole. */
class Unreadable extends Error {}

/**
 * The content of a document of `schemaVersion` (the DTO's `snapshotSchemaVersion`, never the snapshot's
 * own), or null when this console cannot show it whole.
 */
export function readDocumentContent(schemaVersion: number, snapshot: unknown): DocumentContent | null {
  if (!SUPPORTED_SCHEMA_VERSIONS.includes(schemaVersion)) return null;
  try {
    const content = object(object(snapshot)['content']);
    const headline = object(content['headline']);
    const money = moneyOf(headline['money']);
    return {
      title: bilingual(content['title']),
      headline: { label: bilingual(headline['label']), amount: money.amount, currency: money.currency },
      sections: array(content['sections']).map(section),
      timeNote: optionalBilingual(content['timeNote']),
      notice: optionalBilingual(content['notice']),
    };
  } catch (error) {
    if (error instanceof Unreadable) return null;
    throw error;
  }
}

/**
 * The snapshot's recorded facts, for the administrator's evidence panel — the part of the stored
 * document the customer's page does not lay out. Null when the snapshot has no `facts` object.
 */
export function recordedFacts(snapshot: unknown): Record<string, unknown> | null {
  if (typeof snapshot !== 'object' || snapshot === null || Array.isArray(snapshot)) return null;
  const facts = (snapshot as Record<string, unknown>)['facts'];
  return typeof facts === 'object' && facts !== null && !Array.isArray(facts) ? (facts as Record<string, unknown>) : null;
}

function section(value: unknown): DocumentSection {
  const node = object(value);
  return { key: string(node['key']), heading: bilingual(node['heading']), lines: array(node['lines']).map(line) };
}

const VALUE_KINDS = ['money', 'instant', 'text', 'plain'] as const;

function line(value: unknown): DocumentLine {
  const node = object(value);
  const kinds = VALUE_KINDS.filter((kind) => node[kind] !== undefined && node[kind] !== null);
  if (kinds.length !== 1) throw new Unreadable();
  const label = node['label'] === undefined || node['label'] === null ? null : bilingual(node['label']);
  return { key: string(node['key']), label, value: valueOf(kinds[0]!, node[kinds[0]!]) };
}

function valueOf(kind: (typeof VALUE_KINDS)[number], value: unknown): DocumentValue {
  switch (kind) {
    case 'money':
      return { kind, ...moneyOf(value) };
    case 'instant': {
      const node = object(value);
      return { kind, utc: string(node['utc']), local: string(node['local']) };
    }
    case 'text':
      return { kind, text: bilingual(value) };
    case 'plain':
      return { kind, plain: string(value) };
  }
}

function moneyOf(value: unknown): { amount: string; currency: string } {
  const node = object(value);
  return { amount: string(node['amount']), currency: string(node['currency']) };
}

function optionalBilingual(value: unknown): Bilingual | null {
  return value === undefined || value === null ? null : bilingual(value);
}

function bilingual(value: unknown): Bilingual {
  const node = object(value);
  return { en: string(node['en']), ar: string(node['ar']) };
}

function object(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Unreadable();
  return value as Record<string, unknown>;
}

function array(value: unknown): readonly unknown[] {
  if (!Array.isArray(value)) throw new Unreadable();
  return value;
}

function string(value: unknown): string {
  if (typeof value !== 'string') throw new Unreadable();
  return value;
}
