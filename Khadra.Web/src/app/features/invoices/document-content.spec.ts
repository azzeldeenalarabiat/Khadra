import { describe, expect, it } from 'vitest';
import fixtureJson from '../../../../../docs/contracts/financial-documents-v1.json';
import { FinancialDocumentPage } from '../../core/api/financial-documents.api';
import { DocumentContent, readDocumentContent } from './document-content';

/**
 * The version 1 reader against the SHARED contract fixture — customer pages of every shape the grammar
 * has, composed by the server's own composer (`docs/contracts/financial-documents-v1.json`, pinned byte
 * for byte by `FinancialDocumentFixtureTests`). The app and the console read the same file, so all three
 * are proven against the same documents (payments Phase 5b, decision D5).
 */
interface ContractFixture {
  readonly schemaVersion: number;
  readonly documents: readonly { readonly name: string; readonly page: FinancialDocumentPage }[];
}
const fixture = fixtureJson as unknown as ContractFixture;
const page = (name: string): FinancialDocumentPage => fixture.documents.find((entry) => entry.name === name)!.page;

type Json = Record<string, unknown>;
/** A copy of a fixture document's snapshot to break or extend. */
const snapshotOf = (name: string): Json => structuredClone(page(name).snapshot) as Json;
const contentOf = (snapshot: Json) => snapshot['content'] as Json;
const sectionsOf = (snapshot: Json) => contentOf(snapshot)['sections'] as Json[];
const linesOf = (section: Json) => section['lines'] as Json[];
const read = (snapshot: unknown, version = 1): DocumentContent | null => readDocumentContent(version, snapshot);

describe('the version 1 reader, over every document the server composes', () => {
  it('reads every document in the shared fixture whole, sections and lines in their stored order', () => {
    expect(fixture.documents.length).toBeGreaterThan(0);
    for (const { name, page: document } of fixture.documents) {
      const content = read(document.snapshot, document.snapshotSchemaVersion);
      expect(content, name).not.toBeNull();
      const stored = sectionsOf(document.snapshot as Json);
      expect(content!.sections.map((section) => section.key), name).toEqual(stored.map((section) => section['key']));
      content!.sections.forEach((section, index) =>
        expect(section.lines.map((line) => line.key), name).toEqual(linesOf(stored[index]!).map((line) => line['key'])),
      );
    }
  });

  it('meets every kind of value, and a line with no label, in the fixture', () => {
    const kinds = new Set<string>();
    let unlabelled = 0;
    for (const { page: document } of fixture.documents) {
      for (const section of read(document.snapshot)!.sections) {
        for (const line of section.lines) {
          kinds.add(line.value.kind);
          if (line.label === null) unlabelled++;
        }
      }
    }
    expect([...kinds].sort()).toEqual(['instant', 'money', 'plain', 'text']);
    expect(unlabelled).toBeGreaterThan(0);
  });

  it('keeps an amount as the string it was stored as', () => {
    const content = read(page('payment-receipt-paid-in-full').snapshot)!;
    expect(content.headline).toEqual({ label: { en: 'Amount paid', ar: 'المبلغ المدفوع' }, amount: '94.500', currency: 'JOD' });
  });
});

describe('what the reader tolerates (docs/contracts/README.md: what the server may do within version 1)', () => {
  it('ignores keys it does not know, anywhere', () => {
    const snapshot = snapshotOf('booking-statement-dispute-decided');
    const before = read(snapshot);
    snapshot['somethingNew'] = { any: 'thing' };
    contentOf(snapshot)['layoutHint'] = 'two-columns';
    sectionsOf(snapshot)[0]!['collapsed'] = true;
    linesOf(sectionsOf(snapshot)[0]!)[0]!['emphasis'] = 'strong';
    expect(read(snapshot)).toEqual(before);
  });

  it('renders sections and lines it has never seen, in the order given', () => {
    const snapshot = snapshotOf('payment-receipt-paid-in-full');
    const sections = sectionsOf(snapshot);
    sections[0]!['key'] = 'aSectionBornLater';
    sections.reverse();
    const content = read(snapshot)!;
    expect(content.sections.at(-1)!.key).toBe('aSectionBornLater');
    expect(content.sections.map((section) => section.key)).toEqual(sections.map((section) => section['key']));
  });

  it('reads an absent time note or notice as nothing to show — and never writes one itself', () => {
    const snapshot = snapshotOf('payment-receipt-not-applied');
    delete contentOf(snapshot)['timeNote'];
    delete contentOf(snapshot)['notice'];
    const content = read(snapshot)!;
    expect(content.timeNote).toBeNull();
    expect(content.notice).toBeNull();
  });

  it('still reads an amount or a time off its pattern, for the formatter to print as it is', () => {
    const snapshot = snapshotOf('payment-receipt-paid-in-full');
    const headline = contentOf(snapshot)['headline'] as Json;
    (headline['money'] as Json)['amount'] = '94,5';
    const instant = linesOf(sectionsOf(snapshot)[0]!).find((line) => line['instant'] !== undefined)!;
    (instant['instant'] as Json)['local'] = '2026-9-3 15:01';
    const content = read(snapshot)!;
    expect(content.headline.amount).toBe('94,5');
    expect(content.sections[0]!.lines.find((line) => line.value.kind === 'instant')!.value).toMatchObject({ local: '2026-9-3 15:01' });
  });
});

describe('what the reader refuses whole (a financial record is never shown with a line missing)', () => {
  it('refuses a schema version it does not know, gating on the page, never on the snapshot', () => {
    expect(read(page('payment-receipt-paid-in-full').snapshot, 2)).toBeNull();
    const snapshot = snapshotOf('payment-receipt-paid-in-full');
    snapshot['schemaVersion'] = 99;
    expect(read(snapshot, 1)).not.toBeNull();
  });

  it('refuses a line with no value, or with two', () => {
    const none = snapshotOf('refund-receipt-free-cancellation');
    const bare = linesOf(sectionsOf(none)[0]!)[0]!;
    for (const kind of ['money', 'instant', 'text', 'plain']) delete bare[kind];
    expect(read(none)).toBeNull();

    const two = snapshotOf('refund-receipt-free-cancellation');
    const doubled = linesOf(sectionsOf(two)[0]!)[0]!;
    doubled['plain'] = 'x';
    doubled['text'] = { en: 'x', ar: 'س' };
    expect(read(two)).toBeNull();
  });

  it('refuses a missing title, headline, heading, lines or line key', () => {
    const breaks: ((snapshot: Json) => void)[] = [
      (snapshot) => delete contentOf(snapshot)['title'],
      (snapshot) => delete (contentOf(snapshot)['headline'] as Json)['money'],
      (snapshot) => delete (contentOf(snapshot)['headline'] as Json)['label'],
      (snapshot) => delete sectionsOf(snapshot)[1]!['heading'],
      (snapshot) => delete sectionsOf(snapshot)[1]!['lines'],
      (snapshot) => delete linesOf(sectionsOf(snapshot)[1]!)[0]!['key'],
      (snapshot) => (contentOf(snapshot)['sections'] = 'not a list'),
    ];
    for (const breakIt of breaks) {
      const snapshot = snapshotOf('booking-statement-cash-at-handover');
      breakIt(snapshot);
      expect(read(snapshot)).toBeNull();
    }
  });

  it('refuses a text that is not two strings, and an amount that is not a string', () => {
    const text = snapshotOf('payment-receipt-deposit-correction');
    (contentOf(text)['title'] as Json)['ar'] = 7;
    expect(read(text)).toBeNull();

    const amount = snapshotOf('payment-receipt-deposit-correction');
    ((contentOf(amount)['headline'] as Json)['money'] as Json)['amount'] = 19.5;
    expect(read(amount)).toBeNull();
  });

  it('refuses what is not a document at all', () => {
    expect(read(null)).toBeNull();
    expect(read('a document')).toBeNull();
    expect(read({})).toBeNull();
  });
});
