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
  FinancialDocumentEmail,
  FinancialDocumentEmailAttempt,
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
  emailDialogWords,
  emailQueuedToast,
  emailRefusalIsFinal,
  holdRow,
  preparingRow,
  refusalReport,
  shownInBody,
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

  it('words a hold by its kind, its reason and the issues it waits on, in both languages, and spells out a reason it does not know', () => {
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
      // The calculator's issue codes, worded as the booking's Money section words them (pre-launch item 219).
      issues: ["A refund this booking's ending owes was never recorded"],
      lastError: null,
    });
    expect(holdRow(hold, arabic, format)).toMatchObject({
      reason: 'سجلات الحجز تحتاج إلى مراجعة',
      issues: ['لم يُسجَّل استرداد يستحقه انتهاء هذا الحجز'],
    });
    // Any other failure keeps the server's own log line, as it is.
    expect(holdRow({ ...hold, reason: 'ProviderUnreachable', lastError: 'Timed out after 15 s.' }, english, format)).toMatchObject({
      reason: 'Provider unreachable',
      issues: [],
      lastError: 'Timed out after 15 s.',
    });
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
      { key: 'AsIssued:en', language: 'en', kind: 'AsIssued', label: 'Download PDF (English)', fileName: 'TEST-PAY-2026-000002-en.pdf' },
      { key: 'AsIssued:ar', language: 'ar', kind: 'AsIssued', label: 'Download PDF (Arabic)', fileName: 'TEST-PAY-2026-000002-ar.pdf' },
    ]);
    expect(view.voided).toBe(false);
    expect(view.preparing).toBe(false);
  });

  it('words it all in Arabic', () => {
    const view = documentPage(adminPage('payment-receipt-paid-in-full', { renditions: [rendition('ar', 1)] }), arabic, format).pdf;
    expect(view.renditions[0]!.title).toBe('العربية · القالب 1');
    expect(view.downloads[0]!.label).toBe('تنزيل PDF (بالعربية)');
    expect(view.renditions[0]!.rows.map((row) => row.k)).toEqual(['تاريخ الإنشاء', 'أداة الإنشاء', 'الحجم', 'بصمة الملف (SHA-256)', 'أُنشئ من بصمة المحتوى']);
  });

  it('offers a voided document both ways: the original as issued, unstamped, and the voided copy its customer is given', () => {
    const view = documentPage(
      adminPage('payment-receipt-deposit-voided', {
        renditions: [
          rendition('ar', 1, { kind: 'Voided' }),
          rendition('en', 1, { kind: 'AsIssued' }),
          rendition('en', 1, { kind: 'Voided' }),
          rendition('ar', 1, { kind: 'AsIssued' }),
        ],
      }),
      english,
      format,
    ).pdf;

    expect(view.downloads).toEqual([
      {
        key: 'AsIssued:en',
        language: 'en',
        kind: 'AsIssued',
        label: 'Download the original as issued, unstamped (English)',
        fileName: 'TEST-PAY-2026-000001-en.pdf',
      },
      {
        key: 'AsIssued:ar',
        language: 'ar',
        kind: 'AsIssued',
        label: 'Download the original as issued, unstamped (Arabic)',
        fileName: 'TEST-PAY-2026-000001-ar.pdf',
      },
      { key: 'Voided:en', language: 'en', kind: 'Voided', label: 'Download the voided copy (English)', fileName: 'TEST-PAY-2026-000001-en-void.pdf' },
      { key: 'Voided:ar', language: 'ar', kind: 'Voided', label: 'Download the voided copy (Arabic)', fileName: 'TEST-PAY-2026-000001-ar-void.pdf' },
    ]);
    expect(view.renditions.map((item) => item.title)).toEqual([
      'English · original as issued, unstamped · template 1',
      'Arabic · original as issued, unstamped · template 1',
      "English · voided copy, the customer's · template 1",
      "Arabic · voided copy, the customer's · template 1",
    ]);
    expect(new Set(view.renditions.map((item) => item.key)).size).toBe(4);
    expect(view.voided).toBe(true);
    expect(view.preparing).toBe(false);

    const arabicView = documentPage(
      adminPage('payment-receipt-deposit-voided', { renditions: [rendition('en', 1, { kind: 'AsIssued' }), rendition('en', 1, { kind: 'Voided' })] }),
      arabic,
      format,
    ).pdf;
    expect(arabicView.downloads.map((download) => download.label)).toEqual([
      'تنزيل الأصل كما صدر، بلا ختم (بالإنجليزية)',
      'تنزيل النسخة الملغاة (بالإنجليزية)',
    ]);
    expect(arabicView.renditions.map((item) => item.title)).toEqual([
      'الإنجليزية · الأصل كما صدر، بلا ختم · القالب 1',
      'الإنجليزية · النسخة الملغاة، نسخة العميل · القالب 1',
    ]);
  });

  it('says a voided copy is being drawn, and names the original before it is', () => {
    const page = adminPage('payment-receipt-deposit-voided', { renditions: [rendition('en', 1), rendition('ar', 1)] });
    const view = documentPage({ ...page, document: { ...page.document, pdf: { languages: [], preparing: true } } }, english, format).pdf;
    // A server that predates the kind drew only the document as issued.
    expect(view.downloads.map((download) => [download.kind, download.label])).toEqual([
      ['AsIssued', 'Download the original as issued, unstamped (English)'],
      ['AsIssued', 'Download the original as issued, unstamped (Arabic)'],
    ]);
    expect(view.voided).toBe(true);
    expect(view.preparing).toBe(true);
  });

  it('takes "being drawn" from the server, and shows nothing it does not know', () => {
    const none = documentPage(adminPage('booking-statement-receipt-corrected', { renditions: [] }), english, format).pdf;
    expect(none).toEqual({ downloads: [], renditions: [], preparing: true, voided: false, holds: [] });

    const strange = documentPage(
      adminPage('payment-receipt-paid-in-full', {
        // `constructor` is a property of every object: only the table's own keys are languages.
        renditions: [
          rendition('fr', 1),
          rendition('constructor', 1),
          rendition('en', 1, { format: 'Html' }),
          rendition('en', 1, { kind: 'Stamped' }),
        ],
      }),
      english,
      format,
    ).pdf;
    expect(strange.renditions).toEqual([]);
    expect(strange.downloads).toEqual([]);

    // A server older than Phase 6 sends neither field: nothing is offered and nothing is being drawn.
    const older = adminPage('payment-receipt-paid-in-full');
    const { pdf: _absent, ...page } = older.document;
    expect(documentPage({ ...older, document: page }, english, format).pdf).toEqual({
      downloads: [],
      renditions: [],
      preparing: false,
      voided: false,
      holds: [],
    });
  });

  // Pre-launch item 197: a PDF that could not be drawn says which, why and since when, in either language — and one
  // this console has no words for is left out rather than guessed at.
  it('says which PDF could not be drawn, why, and since when', () => {
    const held = adminPage('payment-receipt-paid-in-full', {
      renditions: [],
      pdfHolds: [
        { language: 'ar', kind: 'AsIssued', reason: 'SnapshotAltered', attempts: 2, firstFailedAt: '2026-10-03T06:00:00Z', lastFailedAt: '2026-10-04T06:00:00Z' },
        { language: 'fr', kind: 'AsIssued', reason: 'DrawingFailed', attempts: 1, firstFailedAt: '2026-10-03T06:00:00Z', lastFailedAt: '2026-10-03T06:00:00Z' },
      ],
    });

    const [only, ...rest] = documentPage(held, english, format).pdf.holds;
    expect(rest).toEqual([]);
    expect(only!.title).toBe('Arabic · as issued — could not be drawn: the stored record no longer matches what was issued');
    expect(only!.detail).toContain('2 attempts');

    const arabicHold = documentPage(held, arabic, format).pdf.holds[0]!;
    expect(arabicHold.title).toContain('السجل المخزَّن لم يعد يطابق ما صدر');
  });

  it('words a PDF that is not drawn yet, and a voided copy of a document that is not voided', () => {
    const problem = snapshotProblem({ status: 409, error: { code: 'financial_documents.pdf_not_ready', title: 'The PDF of this document is being prepared.' } });
    expect(problemMessage(problem, 'en', en)).toBe('This PDF is still being drawn. Try again shortly.');
    expect(problemMessage(problem, 'ar', ar)).toBe('ما زال ملف PDF هذا قيد الإنشاء. حاول مجددًا بعد قليل.');
    const notVoided = snapshotProblem({ status: 409, error: { code: 'financial_documents.not_voided', title: 'This document is not voided, so it has no voided copy.' } });
    expect(problemMessage(notVoided, 'en', en)).toBe('This document is not voided, so it has no voided copy.');
    expect(problemMessage(notVoided, 'ar', ar)).toBe('هذا المستند غير ملغى، لذا لا نسخة ملغاة له.');
  });
});

describe('the emails of a document, as the administrator reads them (payments Phase 7)', () => {
  const attempt = (number: number, over: Partial<FinancialDocumentEmailAttempt> = {}): FinancialDocumentEmailAttempt => ({
    number,
    outcome: 'Failed',
    attemptedAt: `2026-09-29T09:0${number}:00+00:00`,
    error: 'Brevo refused the message (401): key not found',
    provider: 'Brevo',
    providerMessageId: null,
    englishPdfSha256: 'e'.repeat(64),
    arabicPdfSha256: null,
    ...over,
  });

  const email = (over: Partial<FinancialDocumentEmail> = {}): FinancialDocumentEmail => ({
    deliveryId: 'mail-1',
    state: 'Sent',
    waitingFor: null,
    waitingSince: null,
    requestedByAdminId: null,
    requestedByName: null,
    queuedAt: '2026-09-29T09:00:00+00:00',
    completedAt: '2026-09-29T09:02:00+00:00',
    recipient: 'rana@example.jo',
    languages: ['en'],
    sendAttempts: 2,
    lastError: null,
    attempts: [attempt(1), attempt(2, { outcome: 'Accepted', error: null, providerMessageId: '<abc@smtp-relay.brevo.com>' })],
    ...over,
  });

  const emailsOf = (name: string, over: Partial<AdminFinancialDocument>, words = english) =>
    documentPage(adminPage(name, over), words, format).emails;

  it('lists each email with its state, who asked, where it went and every attempt, the machine values as code', () => {
    const view = emailsOf('payment-receipt-paid-in-full', { emails: [email()], canEmailAgain: true })!;

    const [sent] = view.emails;
    expect(sent!.standing).toEqual({ label: 'Sent', tone: 'ok' });
    expect(sent!.requested).toBe('Queued when the receipt was issued');
    expect(sent!.rows).toEqual([
      { k: 'Queued', v: '2026-09-29T09:00', code: false },
      { k: 'Finished', v: '2026-09-29T09:02', code: false },
      { k: 'To', v: 'rana@example.jo', code: true },
      { k: 'Written in', v: 'English', code: false },
      { k: 'Send attempts', v: '#2', code: false },
    ]);
    expect(sent!.attempts.map((item) => [item.title, item.tone])).toEqual([
      ['Attempt 1 · Failed', 'bad'],
      ['Attempt 2 · Accepted by the mail provider', 'ok'],
    ]);
    expect(sent!.attempts[0]!.rows).toEqual([
      { k: 'When', v: '2026-09-29T09:01', code: false },
      { k: 'Mail provider', v: 'Brevo', code: true },
      { k: 'Error', v: 'Brevo refused the message (401): key not found', code: true },
      { k: 'English PDF attached (SHA-256)', v: 'e'.repeat(64), code: true },
    ]);
    expect(sent!.attempts[1]!.rows.map((row) => row.k)).toEqual(['When', 'Mail provider', "Provider's message id", 'English PDF attached (SHA-256)']);
  });

  it('says who asked for an email again, and what a queued one is waiting for', () => {
    const waiting = email({
      deliveryId: 'mail-2',
      state: 'Queued',
      waitingFor: 'PdfNotReady',
      waitingSince: '2026-09-29T10:00:00+00:00',
      requestedByAdminId: 'admin-1',
      requestedByName: 'Azzeldeen Al-Arabiat',
      completedAt: null,
      recipient: null,
      languages: [],
      sendAttempts: 0,
      attempts: [],
    });
    const [view] = emailsOf('payment-receipt-paid-in-full', { emails: [waiting], canEmailAgain: false })!.emails;

    expect(view!.standing).toEqual({ label: 'Queued', tone: 'accent' });
    expect(view!.requested).toBe('Asked for by Azzeldeen Al-Arabiat');
    expect(view!.rows).toEqual([
      { k: 'Queued', v: '2026-09-29T09:00', code: false },
      { k: 'Waiting for', v: 'Its PDF to be drawn, since 2026-09-29T10:00', code: false },
    ]);
    // An administrator whose account no longer resolves is still named as one.
    const [gone] = emailsOf('payment-receipt-paid-in-full', { emails: [{ ...waiting, requestedByName: null }] })!.emails;
    expect(gone!.requested).toBe('Asked for by an administrator no longer on the platform');
  });

  it('names why an email was skipped as a reason, and a failure as an error', () => {
    const skipped = email({
      state: 'Skipped',
      recipient: null,
      languages: [],
      sendAttempts: 0,
      lastError: 'The customer has no verified email address.',
      attempts: [attempt(1, { outcome: 'Skipped', provider: null, englishPdfSha256: null, error: 'The customer has no verified email address.' })],
    });
    const [view] = emailsOf('payment-receipt-paid-in-full', { emails: [skipped], canEmailAgain: true })!.emails;
    expect(view!.standing).toEqual({ label: 'Skipped', tone: 'dim' });
    expect(view!.rows.at(-1)).toEqual({ k: 'Why it was not sent', v: 'The customer has no verified email address.', code: true });
    expect(view!.attempts[0]!.rows.at(-1)).toEqual({ k: 'Why', v: 'The customer has no verified email address.', code: true });

    const [failed] = emailsOf('payment-receipt-paid-in-full', { emails: [email({ state: 'Failed', lastError: 'TimeoutException' })], canEmailAgain: true })!.emails;
    expect(failed!.standing).toEqual({ label: 'Failed', tone: 'bad' });
    expect(failed!.rows.at(-1)).toEqual({ k: 'Last error', v: 'TimeoutException', code: true });
  });

  it('words it all in Arabic, the languages in the reader’s own words', () => {
    const view = emailsOf('payment-receipt-paid-in-full', { emails: [email({ languages: ['ar', 'en'] })], canEmailAgain: true }, arabic)!;
    const [sent] = view.emails;
    expect(sent!.standing.label).toBe('أُرسلت');
    expect(sent!.requested).toBe('جُدولت عند إصدار الإيصال');
    expect(sent!.rows.map((row) => row.k)).toEqual(['وقت الجدولة', 'وقت الانتهاء', 'إلى', 'اللغة', 'محاولات الإرسال']);
    expect(sent!.rows[3]!.v).toBe('العربية · الإنجليزية');
    expect(sent!.attempts[1]!.title).toBe('المحاولة 2 · قبِلها مزوّد البريد');
    expect(view.action!.label).toBe('إرساله بالبريد مجددًا');
  });

  it('offers emailing on the server’s word, labelled by whether it was ever emailed', () => {
    expect(emailsOf('payment-receipt-paid-in-full', { emails: [], canEmailAgain: true })).toEqual({
      emails: [],
      action: { label: 'Email it to the customer', waiting: null },
      note: null,
    });
    expect(emailsOf('refund-receipt-free-cancellation', { emails: [email()], canEmailAgain: true })!.action).toEqual({
      label: 'Email it again',
      waiting: null,
    });
  });

  it('waits out an email already on its way — the control stays, disabled, saying why', () => {
    const view = emailsOf('payment-receipt-paid-in-full', {
      emails: [email({ deliveryId: 'mail-2', state: 'Queued', completedAt: null }), email()],
      canEmailAgain: false,
    })!;
    expect(view.action).toEqual({ label: 'Email it again', waiting: 'An email of this receipt is already on its way.' });
    expect(view.note).toBeNull();
  });

  it('never offers it for a statement or a voided receipt, and says why each is not emailed', () => {
    const statement = emailsOf('booking-statement-cash-at-handover', { emails: [], canEmailAgain: false })!;
    expect(statement.action).toBeNull();
    expect(statement.note).toBe('Booking statements are not emailed to the customer. Only receipts are.');

    const voided = emailsOf('payment-receipt-deposit-voided', { emails: [email()], canEmailAgain: false })!;
    expect(voided.action).toBeNull();
    expect(voided.note).toBe('Voided, so it is not emailed again. Its correction is the receipt the customer is sent.');
    // Its history stays: what was sent before the void was sent.
    expect(voided.emails).toHaveLength(1);
  });

  it('shows nothing about emails for a server that does not report them', () => {
    expect(emailsOf('payment-receipt-paid-in-full', {})).toBeNull();
  });

  it('says a server whose delivery is switched off sends nothing, offers no way to send, and still lists what waits', () => {
    // Production on Brevo (owner, 2026-09-29): the email owed at issue waits in the queue; it is not "on its way".
    const held = email({ state: 'Queued', completedAt: null, recipient: null, languages: [], sendAttempts: 0, attempts: [] });
    const switchedOff = { emails: [held], canEmailAgain: false, emailDeliveryDisabled: true };

    const view = emailsOf('payment-receipt-paid-in-full', switchedOff)!;
    expect(view.action).toBeNull();
    expect(view.note).toBe(
      'Receipt emails are switched off on this server: its mail provider, Brevo, has not had its protection against duplicate sends verified. Queued emails wait here, and nothing is sent.',
    );
    expect(view.emails.map((row) => row.standing.label)).toEqual(['Queued']);
    expect(emailsOf('payment-receipt-paid-in-full', switchedOff, arabic)!.note).toBe(
      'إرسال الإيصالات بالبريد متوقف على هذا الخادم: لم يُتحقَّق بعد من حماية مزوّد البريد Brevo من الإرسال المكرر. تنتظر الرسائل المجدولة هنا، ولا يُرسَل شيء.',
    );

    // A statement and a voided receipt keep their own, more particular, reason.
    expect(emailsOf('booking-statement-cash-at-handover', { ...switchedOff, emails: [] })!.note).toBe(
      'Booking statements are not emailed to the customer. Only receipts are.',
    );
    expect(emailsOf('payment-receipt-deposit-voided', switchedOff)!.note).toBe(
      'Voided, so it is not emailed again. Its correction is the receipt the customer is sent.',
    );
  });

  it('says Sent means accepted by the mail provider, in both languages, and never claims it was delivered', () => {
    expect(en('financialDocuments.emailsHint')).toBe('Sent means the mail provider accepted it, not that it reached the inbox');
    expect(ar('financialDocuments.emailsHint')).toBe('«أُرسلت» تعني أن مزوّد البريد قبِلها، لا أنها وصلت إلى صندوق الوارد');
    // Every word this phase added about a document's emails.
    const emailWords = (Object.keys(EN) as TranslationKey[]).filter(
      (key) =>
        /^(financialDocuments\.email|financialDocumentEmail)/.test(key) ||
        /FinancialDocumentEmail/.test(key) ||
        [
          'queue.documentEmailsNotSent',
          'problem.documentNotEmailed',
          'problem.documentVoidedNotEmailed',
          'problem.emailAlreadyQueued',
          'problem.emailDeliveryDisabled',
        ].includes(key),
    );
    expect(emailWords.length).toBeGreaterThan(40);
    for (const key of emailWords) {
      const message = EN[key];
      const forms = typeof message === 'string' ? [message] : Object.values(message);
      expect(forms.join(' '), key).not.toMatch(/deliver/i);
    }
  });

  it('asks before queuing, states what is sent and to whom, and promises no time in the toast', () => {
    expect(emailDialogWords('TEST-PAY-2026-000013', en)).toEqual({
      title: 'Email TEST-PAY-2026-000013 to its customer?',
      body: "It goes to the customer's verified email address with its PDF attached: in the language they chose, or in Arabic and English if they never chose one.",
      note: "The request is recorded in the audit log under this receipt's number.",
      confirm: 'Queue the email',
    });
    expect(emailDialogWords('TEST-PAY-2026-000013', ar).title).toBe('إرسال TEST-PAY-2026-000013 إلى عميله بالبريد؟');
    const toast = emailQueuedToast('TEST-PAY-2026-000013', en);
    expect(toast).toEqual({
      title: 'Email queued',
      body: 'TEST-PAY-2026-000013 is queued for its customer. This page shows when the mail provider accepts it.',
    });
    expect(toast.body).not.toMatch(/\d+ (second|minute|hour)/);
  });

  it('closes the dialog only on a refusal that asking again cannot mend, and words each in both languages', () => {
    for (const code of [
      'financial_documents.not_emailed',
      'financial_documents.voided_not_emailed',
      'financial_documents.email_already_queued',
      'financial_documents.email_delivery_disabled',
    ]) {
      expect(emailRefusalIsFinal(code), code).toBe(true);
    }
    expect(emailRefusalIsFinal('financial_documents.not_found')).toBe(false);
    expect(emailRefusalIsFinal(null)).toBe(false);

    const refused = (code: string) => snapshotProblem({ status: 409, error: { code, title: 'server words' } });
    expect(problemMessage(refused('financial_documents.email_already_queued'), 'en', en)).toBe(
      'An email of this receipt is already queued. The page now shows it.',
    );
    expect(problemMessage(refused('financial_documents.voided_not_emailed'), 'ar', ar)).toBe(
      'أُلغي هذا الإيصال، فلا يُرسَل بالبريد مجددًا. تصحيحه هو الذي يُرسَل.',
    );
    expect(problemMessage(refused('financial_documents.not_emailed'), 'en', en)).toBe('Only receipts are emailed to the customer.');
    expect(problemMessage(refused('financial_documents.email_delivery_disabled'), 'en', en)).toBe(
      "Receipt emails are switched off on this server until Brevo's protection against duplicate sends is verified. Nothing was queued.",
    );
    expect(problemMessage(refused('financial_documents.email_delivery_disabled'), 'ar', ar)).toBe(
      'إرسال الإيصالات بالبريد متوقف على هذا الخادم إلى أن يُتحقَّق من حماية Brevo من الإرسال المكرر. لم يُجدوَل شيء.',
    );
  });
});

describe('the commercial registrations (owner, 2026-09-29)', () => {
  const registrations = ['Commercial registration', "Rental office's commercial registration", 'السجل التجاري', 'السجل التجاري لمكتب التأجير'];

  it('leave the document body, in both languages, as they leave the customer page and the PDF', () => {
    for (const entry of fixture.documents) {
      for (const words of [english, arabic]) {
        const view = documentPage(adminPage(entry.name), words, format);
        const lines = view.body!.sections.flatMap((section) => linesOf(section.blocks));
        expect(lines.map((line) => line.label).filter((label) => registrations.includes(label)), entry.name).toEqual([]);
        expect(lines.filter((line) => line.key.endsWith('Registration')), entry.name).toEqual([]);
      }
    }
  });

  it('stay in the proof of issue, exactly as the document stores them', () => {
    const view = documentPage(adminPage('payment-receipt-paid-in-full'), english, format);
    expect(view.registrations.map((line) => [line.label, line.value, line.direction])).toEqual([
      ['Commercial registration', 'TEST-0000', 'ltr'],
      ["Rental office's commercial registration", '123456', 'ltr'],
    ]);
    expect(documentPage(adminPage('payment-receipt-paid-in-full'), arabic, format).registrations.map((line) => line.label)).toEqual([
      'السجل التجاري',
      'السجل التجاري لمكتب التأجير',
    ]);
    // A document this console cannot read whole has no lines to move.
    const unknown = adminPage('payment-receipt-paid-in-full');
    expect(documentPage({ ...unknown, document: { ...unknown.document, snapshotSchemaVersion: 2 } }, english, format).registrations).toEqual([]);
  });

  it('is one rule, the same the website, the app and the PDF apply', () => {
    expect(shownInBody('parties', 'issuerRegistration')).toBe(false);
    expect(shownInBody('parties', 'officeRegistration')).toBe(false);
    expect(shownInBody('parties', 'customerRegistration')).toBe(false);
    expect(shownInBody('parties', 'customer')).toBe(true);
    expect(shownInBody('booking', 'officeRegistration')).toBe(true);
  });
});
