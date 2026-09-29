import { describe, expect, it } from 'vitest';
import fixtureJson from '../../../../../docs/contracts/financial-documents-v1.json';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { problemMessage, snapshotProblem } from '../../core/i18n/problem';
import { resolveMessage } from '../../core/i18n/resolve';
import { EnumFamily, StatusScope, enumKey, spellEnumName, statusKey } from '../../core/i18n/status-key';
import {
  AdminFinancialDocument,
  AdminFinancialDocumentListItem,
  FinancialDocument,
  FinancialDocumentHold,
  FinancialDocumentRendition,
} from '../../core/models/financial-documents.api';
import { Money } from '../../core/models/fleet.api';
import { readDocumentContent } from './financial-document-content';
import {
  DocumentBlock,
  DocumentFormat,
  DocumentWords,
  directionOf,
  documentPage,
  documentRow,
  holdRow,
  preparingRow,
  refusalReport,
  voidDialogWords,
  voidRefusalIsFinal,
  voidedToast,
} from './financial-documents.presenter';

/**
 * The administrator's financial documents in words (payments Phase 5b), against the REAL dictionaries and
 * the SHARED contract fixture the server writes — the same file the website's and the app's tests read.
 * The formatter shows exactly what it was handed, so these prove the presenter hands it the stored string
 * and the frozen wall time, never a float or a UTC instant.
 */
interface ContractFixture {
  readonly documents: readonly { readonly name: string; readonly page: FinancialDocument }[];
  readonly myDocuments: { readonly items: readonly AdminFinancialDocumentListItem[] };
}
const fixture = fixtureJson as unknown as ContractFixture;
const page = (name: string): FinancialDocument => structuredClone(fixture.documents.find((entry) => entry.name === name)!.page);

type Tr = (key: TranslationKey, params?: MessageParams) => string;
const en: Tr = (key, params) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar: Tr = (key, params) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[⁨⁩]/g, '');

const wordsWith = (t: Tr, arabic: boolean): DocumentWords => ({
  t,
  arabic,
  enumLabel: (family: EnumFamily, name: string | null | undefined) => {
    const key = enumKey(family, name ?? '');
    return key ? t(key) : spellEnumName(name ?? '');
  },
  statusLabel: (name: string | null | undefined, scope?: StatusScope) => {
    const key = statusKey(name ?? '', scope);
    return key ? t(key) : spellEnumName(name ?? '');
  },
});
const english = wordsWith(en, false);
const arabic = wordsWith(ar, true);

const format: DocumentFormat = {
  money: (value: Money) => `live ${value.amount} ${value.currency}`,
  when: (iso) => iso.slice(0, 16),
  relative: (iso) => `relative ${iso.slice(0, 16)}`,
  storedMoney: (amount, currency) => `[${amount} ${currency}]`,
  frozenTime: (local) => `<${local}>`,
  count: (value) => `#${value}`,
};

/** The administrator's page around a customer page from the fixture. */
const adminPage = (name: string, over: Partial<AdminFinancialDocument> = {}): AdminFinancialDocument => ({
  document: page(name),
  customerId: 'c-1',
  dealerId: 'd-1',
  provider: 'SANDBOX',
  isTest: true,
  contentSha256: 'a3f1c2d4e5b6978800112233445566778899aabbccddeeff0011223344556677',
  coversThrough: null,
  checkpointFingerprint: null,
  void: null,
  ...over,
});

const linesOf = (blocks: readonly DocumentBlock[]) => blocks.flatMap((block) => (block.kind === 'lines' ? block.lines : []));

describe('the version 1 reader, on every document the server composes', () => {
  it('reads each whole, sections and lines in their stored order', () => {
    for (const entry of fixture.documents) {
      const content = readDocumentContent(entry.page.snapshotSchemaVersion, entry.page.snapshot);
      expect(content, entry.name).not.toBeNull();
      const stored = (entry.page.snapshot as { content: { sections: { key: string }[] } }).content.sections;
      expect(content!.sections.map((section) => section.key), entry.name).toEqual(stored.map((section) => section.key));
    }
  });

  it('refuses a schema version it does not know, gating on the page and never on the snapshot', () => {
    const document = page('payment-receipt-paid-in-full');
    expect(readDocumentContent(2, document.snapshot)).toBeNull();
    (document.snapshot as Record<string, unknown>)['schemaVersion'] = 99;
    expect(readDocumentContent(1, document.snapshot)).not.toBeNull();
  });

  it('refuses a document with a line missing its value, or holding two', () => {
    const none = page('refund-receipt-free-cancellation').snapshot as { content: { sections: { lines: Record<string, unknown>[] }[] } };
    const line = none.content.sections[0]!.lines[0]!;
    for (const kind of ['money', 'instant', 'text', 'plain']) delete line[kind];
    expect(readDocumentContent(1, none)).toBeNull();

    const two = page('refund-receipt-free-cancellation').snapshot as { content: { sections: { lines: Record<string, unknown>[] }[] } };
    two.content.sections[0]!.lines[0]!['plain'] = 'x';
    two.content.sections[0]!.lines[0]!['text'] = { en: 'x', ar: 'س' };
    expect(readDocumentContent(1, two)).toBeNull();
  });
});

describe('a document, rendered exactly as it was issued', () => {
  it('shows the stored title and figure, the figure as the stored string through storedMoney', () => {
    const view = documentPage(adminPage('payment-receipt-paid-in-full'), english, format).body!;
    expect(view.title).toBe('Payment receipt');
    expect(view.headlineLabel).toBe('Amount paid');
    expect(view.headline).toBe('[94.500 JOD]');
  });

  it('prints every time from its frozen Amman wall time, never the UTC instant', () => {
    const view = documentPage(adminPage('payment-receipt-paid-in-full'), english, format).body!;
    const issued = view.sections.flatMap((section) => linesOf(section.blocks)).find((line) => line.key.endsWith(':issuedAt'))!;
    expect(issued.value).toBe('<2026-09-03 15:01>');
  });

  it('keeps an unlabelled line as a value of its own, in its place', () => {
    const view = documentPage(adminPage('payment-receipt-paid-in-full'), english, format).body!;
    const position = view.sections.find((section) => section.key.endsWith(':bookingPosition'))!;
    expect(position.blocks.map((block) => block.kind)).toEqual(['lines', 'alone']);
  });

  it('keeps a Latin literal left to right and lets a name in Arabic take its own direction', () => {
    const view = documentPage(adminPage('payment-receipt-paid-in-full'), english, format).body!;
    const lines = view.sections.flatMap((section) => linesOf(section.blocks));
    expect(lines.find((line) => line.key.endsWith(':number'))!.direction).toBe('ltr');
    expect(lines.find((line) => line.key.endsWith(':amountPaid'))!.direction).toBeNull();
    expect(directionOf({ kind: 'plain', plain: 'أوتو رنت (Auto Rent)' })).toBe('auto');
  });

  it('says what the document says in Arabic, from the same answer', () => {
    const view = documentPage(adminPage('payment-receipt-paid-in-full'), arabic, format).body!;
    expect(view.title).toBe('إيصال دفع');
    expect(view.notice).toBe('هذا المستند ليس فاتورة ضريبية.');
  });

  it('cannot show a schema version it does not know, and keeps the facts the page carries', () => {
    const unknown = adminPage('payment-receipt-paid-in-full');
    const view = documentPage({ ...unknown, document: { ...unknown.document, snapshotSchemaVersion: 2 } }, english, format);
    expect(view.body).toBeNull();
    expect(view.headlineLabel).toBe('Amount paid');
    expect(view.headline).toBe('live 94.5 JOD');
    // The recorded facts are still the administrator's evidence.
    expect(view.facts).toContain('"purpose"');
  });

  it('reports a document it refused whole by its id and schema version only, and one it shows not at all', () => {
    const shown = page('payment-receipt-paid-in-full');
    expect(refusalReport(shown)).toBeNull();

    const unknown = { ...shown, snapshotSchemaVersion: 2 };
    expect(refusalReport(unknown)).toEqual({ documentId: shown.documentId, snapshotSchemaVersion: 2 });

    // A structural break in a version it knows is refused, and reported, the same way.
    const broken = { ...shown, snapshot: { content: {} } };
    const report = refusalReport(broken);
    expect(report).toEqual({ documentId: shown.documentId, snapshotSchemaVersion: 1 });
    // Never the snapshot: a customer's name and their money go nowhere near a log.
    expect(Object.keys(report!).sort()).toEqual(['documentId', 'snapshotSchemaVersion']);
  });
});

describe("a document's page", () => {
  it('names its standing always, the current one included, in both languages', () => {
    const current = documentPage(adminPage('payment-receipt-deposit-correction'), english, format);
    expect(current.standing).toEqual({ label: 'Current version', tone: 'ok' });
    expect(documentPage(adminPage('payment-receipt-deposit-voided'), arabic, format).standing).toEqual({ label: 'ملغى', tone: 'bad' });
    expect(documentPage(adminPage('booking-statement-superseded'), english, format).standing.label).toBe('Earlier version');
  });

  it('spells out a standing this build does not know rather than presenting the document as current', () => {
    const withdrawn = adminPage('payment-receipt-paid-in-full');
    const view = documentPage({ ...withdrawn, document: { ...withdrawn.document, status: 'Withdrawn' } }, english, format);
    expect(view.standing).toEqual({ label: 'Withdrawn', tone: 'dim' });
    expect(view.canVoid).toBe(false);
  });

  it('links an earlier version to the NEWEST — the highest of the versions, never merely the next', () => {
    const statement = adminPage('booking-statement-superseded');
    const member = statement.document.links.versions[0]!;
    const link = (version: number, status: string) => ({ ...member, documentId: `v${version}`, number: `STM-${version}`, version, status });
    const chain: AdminFinancialDocument = {
      ...statement,
      document: {
        ...statement.document,
        documentId: 'v1',
        links: {
          ...statement.document.links,
          versions: [link(1, 'Superseded'), link(3, 'Current'), link(2, 'Voided')],
          nextVersion: link(2, 'Voided'),
        },
      },
    };
    const view = documentPage(chain, english, format);
    expect(view.newer).toMatchObject({ id: 'v3', number: 'STM-3', detail: 'Version 3' });
    expect(view.versions.map((version) => [version.number, version.here, version.standing.label])).toEqual([
      ['STM-1', true, 'Earlier version'],
      ['STM-2', false, 'Voided'],
      ['STM-3', false, 'Current version'],
    ]);
    expect(view.versionOf).toBe('Version 1 of 3');
  });

  it('offers the void on a current document only', () => {
    expect(documentPage(adminPage('payment-receipt-deposit-correction'), english, format).canVoid).toBe(true);
    expect(documentPage(adminPage('payment-receipt-deposit-voided'), english, format).canVoid).toBe(false);
    expect(documentPage(adminPage('booking-statement-superseded'), english, format).canVoid).toBe(false);
  });

  it('shows a void with who, when, the reason as typed, and the correction', () => {
    const voided = adminPage('payment-receipt-deposit-voided');
    const replacedBy = voided.document.voided!.replacedBy!;
    const view = documentPage(
      { ...voided, void: { voidedAt: '2026-09-04T10:00:00Z', voidedByAdminId: 'a-1', voidedByName: 'Rania Haddad', reason: 'Wrong office location', replacedBy } },
      english,
      format,
    );
    expect(view.voided).toEqual({
      when: '2026-09-04T10:00',
      by: 'Rania Haddad',
      reason: 'Wrong office location',
      replacement: expect.objectContaining({ number: 'TEST-PAY-2026-000005', detail: 'Version 2' }),
    });
    const gone = documentPage(
      { ...voided, void: { voidedAt: '2026-09-04T10:00:00Z', voidedByAdminId: 'a-1', voidedByName: null, reason: 'Wrong office location', replacedBy } },
      arabic,
      format,
    );
    expect(gone.voided!.by).toBe('مشرف لم يعد على المنصّة');
  });

  it('states its proof, with the machine values marked as code', () => {
    const view = documentPage(
      adminPage('booking-statement-cash-at-handover', { coversThrough: '2026-09-25T17:57:00Z', checkpointFingerprint: 'f00d' }),
      english,
      format,
    );
    expect(view.proof).toEqual([
      { k: 'Payment provider', v: 'SANDBOX', code: true },
      { k: 'Content hash (SHA-256)', v: expect.stringMatching(/^[0-9a-f]{64}$/), code: true },
      { k: 'Covers money movements up to', v: '2026-09-25T17:57', code: false },
      { k: 'Checkpoint fingerprint', v: 'f00d', code: true },
    ]);
  });

  it('links a refund receipt to its payment receipt, and a payment receipt to its refunds', () => {
    expect(documentPage(adminPage('refund-receipt-free-cancellation'), english, format).paymentReceipt).toMatchObject({
      number: 'TEST-PAY-2026-000002',
      detail: 'Payment receipt',
    });
    expect(documentPage(adminPage('payment-receipt-paid-in-full'), arabic, format).refundReceipts.map((link) => [link.number, link.detail])).toEqual([
      ['TEST-RFD-2026-000001', 'إيصال استرداد'],
    ]);
  });

  it('names its type and cause in the words the document itself uses', () => {
    const view = documentPage(adminPage('booking-statement-dispute-decided'), english, format);
    expect(view.type).toBe('Booking statement');
    expect(view.cause).toBe('Dispute decided');
    expect(documentPage(adminPage('booking-statement-dispute-decided'), arabic, format).cause).toBe('حسم نزاع');
  });
});

describe('the lists', () => {
  const rows = fixture.myDocuments.items.map((item) => ({ ...item, customerId: 'c-1', dealerId: 'd-1', isTest: true }));

  it('shows a row by its stored title and number, the figure through the LIVE formatter', () => {
    const row = documentRow(rows.find((item) => item.number === 'TEST-PAY-2026-000005')!, english, format);
    expect(row).toMatchObject({
      number: 'TEST-PAY-2026-000005',
      title: 'Payment receipt',
      version: 2,
      isTest: true,
      headlineLabel: 'Amount paid',
      headline: 'live 19.5 JOD',
      standing: { label: 'Current version', tone: 'ok' },
    });
    expect(documentRow(rows.find((item) => item.number === 'TEST-PAY-2026-000001')!, arabic, format).standing.label).toBe('ملغى');
  });

  it('words a hold by its kind and reason, in both languages, and spells out a reason it does not know', () => {
    const hold: FinancialDocumentHold = {
      holdId: 'h-1',
      documentType: 'BookingStatement',
      subjectId: 'b-1',
      bookingId: 'b-1',
      bookingReference: 'KH-95JGHJQZ',
      reason: 'RecordsNeedReview',
      attempts: 7,
      firstFailedAt: '2026-09-24T00:00:00Z',
      lastFailedAt: '2026-09-27T08:00:00Z',
      nextAttemptAt: '2026-09-27T09:00:00Z',
      lastError: 'EndingRefundMissing',
    };
    expect(holdRow(hold, english, format)).toMatchObject({
      type: 'Booking statement',
      reason: "The booking's records need review",
      attempts: 7,
      nextAttempt: 'relative 2026-09-27T09:00',
      lastError: 'EndingRefundMissing',
    });
    expect(holdRow(hold, arabic, format).reason).toBe('سجلات الحجز تحتاج إلى مراجعة');
    expect(holdRow({ ...hold, reason: 'ProviderUnreachable' }, english, format).reason).toBe('Provider unreachable');
  });

  it('words what is being prepared by kind, and a kind this build does not know plainly', () => {
    const pending = (type: string) => ({ type, subjectId: 's-1', occurredAt: '2026-09-03T12:32:00Z' });
    expect(preparingRow(pending('PaymentReceipt'), english, format).label).toBe('Payment receipt — being prepared');
    expect(preparingRow(pending('BookingStatement'), arabic, format).label).toBe('كشف حساب الحجز — قيد الإعداد');
    expect(preparingRow(pending('PayablesStatement'), english, format).label).toBe('A document — being prepared');
  });
});

describe('voiding a document', () => {
  it('states the consequence first, in both languages', () => {
    const words = voidDialogWords('TEST-PAY-2026-000005', true, en);
    expect(words.title).toBe('Void TEST-PAY-2026-000005 and issue its correction?');
    expect(words.body).toContain('permanent');
    expect(words.body).toContain('never your reason');
    expect(voidDialogWords('TEST-PAY-2026-000005', true, ar).title).toBe('إلغاء TEST-PAY-2026-000005 وإصدار تصحيحه؟');
  });

  it("says a receipt's correction brings a new booking statement shortly after, and a statement's does not", () => {
    expect(voidDialogWords('TEST-PAY-2026-000005', true, en).note).toBe(
      'Your reason is kept with the void and in the audit log. A new booking statement will be issued shortly after the correction.',
    );
    expect(voidDialogWords('TEST-PAY-2026-000005', true, ar).note).toBe(
      'يُحفظ سببك مع الإلغاء وفي سجل التدقيق. سيصدر كشف حساب جديد للحجز بعد التصحيح بقليل.',
    );
    expect(voidDialogWords('TEST-STM-2026-000003', false, en).note).toBe('Your reason is kept with the void and in the audit log.');
  });

  it('knows which documents the statement follows: receipts, and never a statement', () => {
    expect(documentPage(adminPage('payment-receipt-deposit-correction'), english, format).statementFollows).toBe(true);
    expect(documentPage(adminPage('refund-receipt-free-cancellation'), english, format).statementFollows).toBe(true);
    expect(documentPage(adminPage('booking-statement-receipt-corrected'), english, format).statementFollows).toBe(false);
  });

  it("names a statement issued for a receipt's correction in both languages", () => {
    expect(documentPage(adminPage('booking-statement-receipt-corrected'), english, format).cause).toBe('Receipt corrected');
    expect(documentPage(adminPage('booking-statement-receipt-corrected'), arabic, format).cause).toBe('تصحيح إيصال');
  });

  it('reports what the void did as the server reported it', () => {
    expect(voidedToast('TEST-PAY-2026-000001', 'TEST-PAY-2026-000005', en)).toEqual({
      title: 'Document voided',
      body: 'Voided TEST-PAY-2026-000001. Issued TEST-PAY-2026-000005.',
    });
  });

  it('closes the dialog only on a refusal after which the document can never be current again', () => {
    expect(voidRefusalIsFinal('financial_documents.not_current')).toBe(true);
    expect(voidRefusalIsFinal('financial_documents.already_voided')).toBe(true);
    for (const code of [
      'financial_documents.correction_records_need_review',
      'financial_documents.correction_issuer_not_configured',
      'financial_documents.correction_failed',
      null,
    ]) {
      expect(voidRefusalIsFinal(code)).toBe(false);
    }
  });

  it('words each refusal itself, in both languages, and a reason refused without naming any limit', () => {
    const refused = (code: string, status = 409) => snapshotProblem({ status, error: { code, title: 'server words' } });
    expect(problemMessage(refused('financial_documents.correction_records_need_review'), 'en', en)).toContain('Nothing was voided');
    expect(problemMessage(refused('financial_documents.correction_issuer_not_configured'), 'ar', ar)).toContain('لم يُلغَ شيء');
    expect(problemMessage(refused('financial_documents.correction_failed', 422), 'en', en)).toContain('has been logged');
    expect(problemMessage(refused('financial_documents.not_current'), 'ar', ar)).toContain('النسخة الحالية');
    expect(problemMessage(refused('financial_documents.already_voided'), 'en', en)).toContain('already been voided');

    const reason = snapshotProblem({ status: 400, error: { title: 'One or more validation errors occurred.', errors: { reason: ['The field Reason must be ...'] } } });
    expect(problemMessage(reason, 'en', en)).toBe('Check the reason: it is missing, or longer than the platform allows.');
    expect(problemMessage(reason, 'en', en)).not.toMatch(/\d/);
  });
});

describe('the PDFs of a document, as the administrator reads them (payments Phase 6)', () => {
  const rendition = (language: string, templateVersion: number, over: Partial<FinancialDocumentRendition> = {}): FinancialDocumentRendition => ({
    language,
    format: 'Pdf',
    templateVersion,
    rendererVersion: 'QuestPDF 2026.9.1',
    contentSha256: `${language}${templateVersion}`.padEnd(64, '0'),
    sizeBytes: 84213,
    renderedAt: '2026-09-29T09:15:00+00:00',
    snapshotSha256: 'a3f1c2d4e5b6978800112233445566778899aabbccddeeff0011223344556677',
    ...over,
  });

  it('lists every PDF drawn with the proof of its bytes, English first and the newest template first', () => {
    const view = documentPage(
      adminPage('payment-receipt-paid-in-full', { renditions: [rendition('ar', 1), rendition('en', 1), rendition('en', 2)] }),
      english,
      format,
    ).pdf;

    expect(view.renditions.map((item) => item.title)).toEqual(['English · template 2', 'English · template 1', 'Arabic · template 1']);
    expect(view.renditions[2]!.rows).toEqual([
      { k: 'Drawn', v: '2026-09-29T09:15', code: false },
      { k: 'Drawn with', v: 'QuestPDF 2026.9.1', code: true },
      { k: 'Size', v: '#84213 bytes', code: false },
      { k: 'File hash (SHA-256)', v: 'ar1'.padEnd(64, '0'), code: true },
      { k: 'Drawn from content hash', v: 'a3f1c2d4e5b6978800112233445566778899aabbccddeeff0011223344556677', code: true },
    ]);
    // One download per language — the server serves its newest template — saved under the number and the language.
    expect(view.downloads).toEqual([
      { language: 'en', label: 'Download PDF (English)', fileName: 'TEST-PAY-2026-000002-en.pdf' },
      { language: 'ar', label: 'Download PDF (Arabic)', fileName: 'TEST-PAY-2026-000002-ar.pdf' },
    ]);
    expect(view.withheld).toBe(false);
    expect(view.preparing).toBe(false);
  });

  it('words it all in Arabic', () => {
    const view = documentPage(adminPage('payment-receipt-paid-in-full', { renditions: [rendition('ar', 1)] }), arabic, format).pdf;
    expect(view.renditions[0]!.title).toBe('العربية · القالب 1');
    expect(view.downloads[0]!.label).toBe('تنزيل PDF (بالعربية)');
    expect(view.renditions[0]!.rows.map((row) => row.k)).toEqual(['تاريخ الإنشاء', 'أداة الإنشاء', 'الحجم', 'بصمة الملف (SHA-256)', 'أُنشئ من بصمة المحتوى']);
  });

  it('still offers a voided document as issued, and says the customer is no longer handed it', () => {
    const view = documentPage(
      adminPage('payment-receipt-deposit-voided', { renditions: [rendition('en', 1), rendition('ar', 1)] }),
      english,
      format,
    ).pdf;
    expect(view.downloads.map((download) => download.language)).toEqual(['en', 'ar']);
    expect(view.withheld).toBe(true);
    expect(view.preparing).toBe(false);
  });

  it('takes "being drawn" from the server, and shows nothing it does not know', () => {
    const none = documentPage(adminPage('booking-statement-receipt-corrected', { renditions: [] }), english, format).pdf;
    expect(none).toEqual({ downloads: [], renditions: [], preparing: true, withheld: false });

    const strange = documentPage(
      adminPage('payment-receipt-paid-in-full', {
        // `constructor` is a property of every object: only the table's own keys are languages.
        renditions: [rendition('fr', 1), rendition('constructor', 1), rendition('en', 1, { format: 'Html' })],
      }),
      english,
      format,
    ).pdf;
    expect(strange.renditions).toEqual([]);
    expect(strange.downloads).toEqual([]);

    // A server older than Phase 6 sends neither field: nothing is offered and nothing is being drawn.
    const older = adminPage('payment-receipt-paid-in-full');
    const { pdf: _absent, ...page } = older.document;
    expect(documentPage({ ...older, document: page }, english, format).pdf).toEqual({ downloads: [], renditions: [], preparing: false, withheld: false });
  });

  it('words a PDF that is not drawn yet', () => {
    const problem = snapshotProblem({ status: 409, error: { code: 'financial_documents.pdf_not_ready', title: 'The PDF of this document is being prepared.' } });
    expect(problemMessage(problem, 'en', en)).toBe('This PDF is still being drawn. Try again shortly.');
    expect(problemMessage(problem, 'ar', ar)).toBe('ما زال ملف PDF هذا قيد الإنشاء. حاول مجددًا بعد قليل.');
  });
});
