import { describe, expect, it } from 'vitest';
import fixtureJson from '../../../../../docs/contracts/financial-documents-v1.json';
import { Money } from '../../core/api/common.api';
import { FinancialDocumentPage, FinancialDocumentRow } from '../../core/api/financial-documents.api';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { DocumentBlock, InvoiceFormat, invoicePage, invoiceRow, preparingRow, standing } from './invoice-presentation';

/**
 * Issued documents in words (payments Phase 5b), against the REAL dictionaries and the SHARED contract
 * fixture. Inside a document every word and figure is the stored one — the formatter below shows exactly
 * what it was handed, so these prove the presenter hands it the stored string and the frozen wall time,
 * never a float or a UTC instant. Around a document, the words are the approved ones (§11 of the plan).
 */
interface ContractFixture {
  readonly documents: readonly { readonly name: string; readonly page: FinancialDocumentPage }[];
}
const fixture = fixtureJson as unknown as ContractFixture;
const page = (name: string): FinancialDocumentPage => structuredClone(fixture.documents.find((entry) => entry.name === name)!.page);

const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[⁨⁩]/g, '');

const format: InvoiceFormat = {
  money: (value: Money | null | undefined) => (value ? `live ${value.amount} ${value.currency}` : '—'),
  date: (value) => (value ?? '').slice(0, 10),
  storedMoney: (amount, currency) => `${amount} ${currency}`,
  frozenTime: (local) => `[${local}]`,
};

const linesOf = (blocks: readonly DocumentBlock[]) => blocks.flatMap((block) => (block.kind === 'lines' ? block.lines : []));

describe('a document, rendered exactly as it was issued', () => {
  const view = invoicePage(page('payment-receipt-paid-in-full'), false, en, format);

  it('shows the stored title and headline, the figure as the stored string', () => {
    expect(view.body!.title).toBe('Payment receipt');
    expect(view.body!.headlineLabel).toBe('Amount paid');
    // "94.500", not "94.5": the stored digits, never a float.
    expect(view.body!.headline).toBe('94.500 JOD');
  });

  it('shows every time as its frozen Amman wall time, never the UTC instant', () => {
    const issued = linesOf(view.body!.sections[0]!.blocks).find((line) => line.key.endsWith(':issuedAt'))!;
    expect(issued.value).toBe('[2026-09-03 15:01]');
    expect(JSON.stringify(view.body)).not.toContain('T12:01:00Z');
  });

  it('keeps an unlabelled line as a value of its own, in its place', () => {
    const position = view.body!.sections.find((section) => section.key.endsWith(':bookingPosition'))!;
    expect(position.blocks.map((block) => block.kind)).toEqual(['lines', 'alone']);
    expect(position.blocks[1]).toMatchObject({ kind: 'alone', text: 'Paid in full online. Nothing is due to the rental office.' });
  });

  it('keeps Latin literals — numbers, references, plates, phones — left to right, and isolates nothing else', () => {
    const lines = view.body!.sections.flatMap((section) => linesOf(section.blocks));
    expect(lines.find((line) => line.key.endsWith(':number'))).toMatchObject({ value: 'TEST-PAY-2026-000002', direction: 'ltr' });
    expect(lines.find((line) => line.key.endsWith(':supportPhone'))).toMatchObject({ value: '+962 6 000 0000', direction: 'ltr' });
    expect(lines.find((line) => line.key.endsWith(':amountPaid'))).toMatchObject({ direction: null });
  });

  it('lets a name registered in Arabic take its own direction, so a mixed name keeps its halves in order', () => {
    const arabicOffice = page('payment-receipt-paid-in-full');
    const content = (arabicOffice.snapshot as { content: { sections: { key: string; lines: { key: string; plain?: string }[] }[] } }).content;
    content.sections.find((section) => section.key === 'parties')!.lines.find((line) => line.key === 'office')!.plain = 'أوتو رنت (Auto Rent)';
    const lines = invoicePage(arabicOffice, true, ar, format).body!.sections.flatMap((section) => linesOf(section.blocks));
    expect(lines.find((line) => line.key.endsWith(':office'))).toMatchObject({ value: 'أوتو رنت (Auto Rent)', direction: 'auto' });
  });

  it('says what the document says in Arabic, from the same answer', () => {
    const arabic = invoicePage(page('payment-receipt-paid-in-full'), true, ar, format);
    expect(arabic.body!.title).toBe('إيصال دفع');
    expect(arabic.body!.notice).toBe('هذا المستند ليس فاتورة ضريبية.');
    expect(arabic.body!.headline).toBe('94.500 JOD');
  });

  it('shows the notice and the time note the document stored', () => {
    expect(view.body!.notice).toBe('This document is not a tax invoice.');
    expect(view.body!.timeNote).toBe('All times are Amman time.');
  });

  it('cannot show a schema version it does not know, and keeps the facts the page carries', () => {
    const unknown = { ...page('payment-receipt-paid-in-full'), snapshotSchemaVersion: 2 };
    const fallback = invoicePage(unknown, false, en, format);
    expect(fallback.body).toBeNull();
    expect(fallback.headlineLabel).toBe('Amount paid');
    expect(fallback.number).toBe('TEST-PAY-2026-000002');
    expect(fallback.issued).toBe('Issued 2026-09-03');
  });
});

describe('what surrounds a document', () => {
  it('links an earlier version to the NEWEST one — the highest of the versions, never merely the next', () => {
    const superseded = invoicePage(page('booking-statement-superseded'), false, en, format);
    expect(superseded.standing).toEqual({ label: 'Earlier version', tone: '', unknown: false });
    expect(superseded.newer!.text).toBe('A newer version exists: TEST-STM-2026-000003');

    // v1 superseded, v2 voided, v3 its correction: v1's next member is the voided v2.
    const statement = page('booking-statement-superseded');
    const link = (version: number, status: string) => ({ ...statement.links.versions[0]!, documentId: `v${version}`, number: `STM-${version}`, version, status });
    const chain = {
      ...statement,
      links: { ...statement.links, versions: [link(1, 'Superseded'), link(3, 'Current'), link(2, 'Voided')], nextVersion: link(2, 'Voided') },
      documentId: 'v1',
    };
    expect(invoicePage(chain, false, en, format).newer).toEqual({ id: 'v3', text: 'A newer version exists: STM-3' });
  });

  it('says a voided document was voided and what replaced it — never why — in the approved words', () => {
    const voided = page('payment-receipt-deposit-voided');
    const english = invoicePage(voided, false, en, format).voided!;
    expect(english.text).toBe('Voided on 2026-09-04.');
    expect(english.replacement!.text).toBe('Replaced by TEST-PAY-2026-000005.');
    const arabic = invoicePage(voided, true, ar, format).voided!;
    expect(`${arabic.text} ${arabic.replacement!.text}`).toBe('أُلغي في 2026-09-04، وحلّ محلّه TEST-PAY-2026-000005.');
    expect(JSON.stringify(voided)).not.toContain('wrong office location');
  });

  it('says only when a void has no replacement to name', () => {
    const voided = page('payment-receipt-deposit-voided');
    const alone = { ...voided, voided: { ...voided.voided!, replacedBy: null } };
    expect(invoicePage(alone, false, en, format).voided).toEqual({ text: 'Voided on 2026-09-04.', replacement: null });
    expect(invoicePage(alone, true, ar, format).voided!.text).toBe('أُلغي في 2026-09-04.');
  });

  it('lists every version, marks the one on screen and the current one', () => {
    const view = invoicePage(page('payment-receipt-deposit-correction'), false, en, format);
    expect(view.versionOf).toBe('Version 2 of 2');
    expect(view.versions.map((link) => [link.number, link.here, link.standing?.label])).toEqual([
      ['TEST-PAY-2026-000001', false, 'Voided'],
      ['TEST-PAY-2026-000005', true, 'Current version'],
    ]);
  });

  it('links a refund receipt to the payment receipt it was issued against, and a payment receipt to its refunds', () => {
    expect(invoicePage(page('refund-receipt-free-cancellation'), false, en, format).paymentReceipt!.text).toBe(
      'Issued against payment receipt TEST-PAY-2026-000002',
    );
    expect(invoicePage(page('payment-receipt-paid-in-full'), false, en, format).refundReceipts.map((link) => link.number)).toEqual([
      'TEST-RFD-2026-000001',
    ]);
  });

  it('spells out a standing it does not know rather than presenting the document as current', () => {
    expect(standing('Current', en)).toBeNull();
    expect(standing('Voided', ar)).toEqual({ label: 'ملغى', tone: 'badge--bad', unknown: false });
    expect(standing('Withdrawn', en)).toEqual({ label: 'Withdrawn', tone: '', unknown: true });
  });
});

describe('lists', () => {
  const rows = (fixtureJson as unknown as { myDocuments: { items: FinancialDocumentRow[] } }).myDocuments.items;

  it('shows a row with its stored title and headline, the figure through the LIVE formatter', () => {
    const row = rows.find((item) => item.number === 'TEST-PAY-2026-000005')!;
    expect(invoiceRow(row, false, en, format)).toMatchObject({
      title: 'Payment receipt',
      version: 'Version 2',
      standing: null,
      headlineLabel: 'Amount paid',
      headline: 'live 19.5 JOD',
    });
    expect(invoiceRow(rows.find((item) => item.number === 'TEST-PAY-2026-000001')!, false, en, format).standing!.label).toBe('Voided');
    expect(invoiceRow(rows.find((item) => item.number === 'TEST-PAY-2026-000002')!, false, en, format).version).toBeNull();
  });

  it('words what is being prepared by kind, with the date of its money, and a kind it does not know plainly', () => {
    const pending = (type: string) => ({ type, subjectId: 's-1', occurredAt: '2026-09-03T12:32:00+00:00' });
    expect(preparingRow(pending('PaymentReceipt'), en, format)).toEqual({ key: 'PaymentReceipt:s-1', label: 'Payment receipt — being prepared', date: '2026-09-03' });
    expect(preparingRow(pending('RefundReceipt'), ar, format).label).toBe('إيصال استرداد — قيد الإعداد');
    expect(preparingRow(pending('BookingStatement'), ar, format).label).toBe('كشف حساب الحجز — قيد الإعداد');
    expect(preparingRow(pending('PayablesStatement'), en, format).label).toBe('A document — being prepared');
  });
});
