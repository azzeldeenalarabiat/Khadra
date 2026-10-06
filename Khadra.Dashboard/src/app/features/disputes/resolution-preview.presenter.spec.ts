import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { OfficeExpectedOutcome, ResolutionPreview } from '../../core/models/disputes.api';
import { Money } from '../../core/models/fleet.api';
import { PayoutFormat } from '../payouts/payouts.presenter';
import {
  PreviewWords,
  officeOutcomeView,
  previewConfirmSentence,
  previewView,
  previewWaitingKey,
} from './resolution-preview.presenter';

/**
 * What a dispute decision does to the money (Wave 2 C1; E2E F37), resolved against the REAL dictionaries. The words
 * never claim money moved: a refund is requested, a net is what the ledger will record, and a cancellation's figures
 * hold only if nothing else is decided before its window closes.
 */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[⁨⁩]/g, '');
const jod = (amount: number): Money => ({ amount, currency: 'JOD' });
const format: PayoutFormat = {
  money: (value) => `${value.amount.toFixed(3)} ${value.currency}`,
  dateTime: (iso) => iso.slice(0, 16).replace('T', ' '),
  day: (day) => day,
};
const words = (t: typeof en): PreviewWords => ({
  t,
  label: (_family, name) => name ?? '',
  status: (name) => name,
});
const TICKET = '01a1-ticket';

/** E3 on Staging: 1.5 back to the customer, 0.5 kept by Khadra, 1 to the office, which the commission took whole. */
const e3: ResolutionPreview = {
  statusAfter: 'Completed',
  officeState: 'Final',
  outcome: 'RentalAfterDispute',
  lines: [
    { kind: 'DisputeShare', amount: jod(1), ticketId: TICKET },
    { kind: 'Commission', amount: jod(1), ticketId: null },
  ],
  customer: { refundRequested: jod(1.5) },
  platform: { retainedShare: jod(0.5), commission: jod(1) },
  office: { share: jod(1), money: jod(1), frozenCommission: jod(3), commission: jod(1), charges: jod(0), net: jod(0) },
  earlierDecisions: null,
  recordedNotBefore: '2026-10-05T09:10:00+00:00',
  furtherDecisionsPossibleUntil: null,
  calculatorVersion: 2,
};

describe("the administrator's preview", () => {
  it('shows the office its share and the commission taken from it, which nothing said before (F37)', () => {
    const view = previewView(e3, words(en), format);

    expect(view.statusLine).toBe('The booking becomes Completed.');
    expect(view.rows).toEqual([
      { k: 'Refund requested to the customer', v: '1.500 JOD' },
      { k: 'Kept by Khadra from the deposit', v: '0.500 JOD' },
    ]);
    // The office's part as the payouts page lines it up: its share, less the commission capped at it.
    expect(view.lines.map((line) => [line.label, line.amount])).toEqual([
      ['DisputeShare', '1.000 JOD'],
      ['Less Commission', '1.000 JOD'],
    ]);
    expect(view.net).toEqual({ text: 'Nothing either way', tone: 'dim' });
    expect(view.notes).toContain(
      "The commission frozen on the booking is 3.000 JOD, and it is never more than the office's money on it, here 1.000 JOD.",
    );
    expect(view.notes).toContain('The payouts ledger records it no earlier than 2026-10-05 09:10.');
  });

  it('never says the customer was refunded or the office will receive anything', () => {
    const everything = [
      ...previewView(e3, words(en), format).rows.flatMap((row) => [row.k, row.v]),
      ...previewView(e3, words(en), format).notes,
      previewConfirmSentence(e3, words(en), format),
    ].join(' ');

    expect(everything).not.toMatch(/\bwas refunded\b|\bwill receive\b|\bhas been paid\b/i);
  });

  it("states a cancellation's figures only until its dispute window closes, with any charge on the office", () => {
    const cancelled: ResolutionPreview = {
      ...e3,
      statusAfter: 'Cancelled',
      outcome: 'DisputeDecided',
      lines: [{ kind: 'DisputeCharge', amount: jod(4.5), ticketId: TICKET }],
      customer: { refundRequested: jod(18) },
      platform: { retainedShare: jod(0), commission: jod(0) },
      office: { share: jod(0), money: jod(0), frozenCommission: jod(6), commission: jod(0), charges: jod(4.5), net: jod(-4.5) },
      furtherDecisionsPossibleUntil: '2026-10-07T10:00:00+00:00',
    };

    const view = previewView(cancelled, words(en), format);

    expect(view.lines.map((line) => [line.label, line.amount])).toEqual([['Less DisputeCharge', '4.500 JOD']]);
    expect(view.net).toEqual({ text: 'The office owes Khadra 4.500 JOD', tone: 'warn' });
    expect(view.notes).toContain(
      'If nothing else is decided on this booking before 2026-10-07 10:00: another dispute may still be opened until then.',
    );
    expect(previewConfirmSentence(cancelled, words(en), format)).toBe(
      'As the payouts ledger will record it: The office owes Khadra 4.500 JOD, if nothing else is decided on this booking before 2026-10-07 10:00.',
    );
  });

  it('says plainly when nothing was paid online, and shows no ledger figures', () => {
    const unpaid: ResolutionPreview = { ...e3, officeState: 'NotApplicable', outcome: null, lines: [] };

    const view = previewView(unpaid, words(en), format);

    expect(view.net).toBeNull();
    expect(view.lines).toEqual([]);
    expect(view.notes).toEqual(['Nothing was paid online for this booking, so the payouts ledger records nothing for it.']);
  });

  it('reads in Arabic too', () => {
    const view = previewView(e3, words(ar), format);

    expect(view.rows[0]).toEqual({ k: 'استرداد مطلوب للعميل', v: '1.500 JOD' });
    expect(view.statusLine).toBe('تصبح حالة الحجز Completed.');
  });

  it('counts the earlier decisions it includes', () => {
    const later: ResolutionPreview = {
      ...e3,
      earlierDecisions: { count: 2, toCustomer: jod(1), keptByPlatform: jod(0), toOffice: jod(0), chargedToOffice: jod(1) },
    };

    expect(previewView(later, words(en), format).notes).toContain('These figures include the 2 earlier decisions on this booking.');
  });

  // ── After the advisor's review of Wave 2 ─────────────────────────────────────────────────────────

  it('says a cancellation stays what it is, and only a returned booking becomes completed', () => {
    const cancelled: ResolutionPreview = { ...e3, statusAfter: 'Cancelled', outcome: 'DisputeDecided' };

    expect(previewView(cancelled, words(en), format, 'Cancelled').statusLine).toBe('The booking stays Cancelled.');
    expect(previewView(cancelled, words(ar), format, 'Cancelled').statusLine).toBe('تبقى حالة الحجز Cancelled.');
    expect(previewView(e3, words(en), format, 'Returned').statusLine).toBe('The booking becomes Completed.');
  });

  it('promises no recording when the ledger will hold the booking for review, and says why', () => {
    const held: ResolutionPreview = { ...e3, ledgerIssues: ['EndingRefundMissing', 'RefundsConflict'] };
    const label = (_family: string, name: string | null | undefined) => `issue:${name}`;

    const view = previewView(held, { ...words(en), label }, format);

    expect(view.notes).toContain(
      'The payouts ledger will hold this booking for review instead of recording it, because its records disagree:',
    );
    expect(view.notes).toContain('issue:EndingRefundMissing');
    expect(view.notes).toContain('issue:RefundsConflict');
    expect(view.notes.some((note) => note.includes('no earlier than'))).toBe(false);
    expect(previewConfirmSentence(held, words(en), format)).toBe(
      'The payouts ledger will hold this booking for review instead of recording it, because its records disagree.',
    );
  });

  it('says a booking already final is recorded at the next pass, on the server’s word, not at a time to come', () => {
    const final: ResolutionPreview = { ...e3, recordedAtNextPass: true, ledgerIssues: [] };

    const notes = previewView(final, words(en), format).notes;

    expect(notes).toContain('The booking is final already, so the payouts ledger records it at its next pass, within minutes.');
    expect(notes.some((note) => note.includes('no earlier than'))).toBe(false);
  });
});

describe("the office's reading of a decided dispute", () => {
  const outcome: OfficeExpectedOutcome = {
    source: 'Projected',
    payableId: null,
    outcome: 'DisputeDecided',
    officeMoney: jod(0),
    commission: jod(0),
    charges: jod(4.5),
    net: jod(-4.5),
    lines: [{ kind: 'DisputeCharge', amount: jod(4.5), ticketId: TICKET }],
    finalAt: '2026-10-07T10:00:00+00:00',
    furtherDecisionsPossibleUntil: '2026-10-07T10:00:00+00:00',
  };

  it('is worded to the office, with the window that may still change it', () => {
    const view = officeOutcomeView(outcome, words(en), format);

    expect(view.net).toEqual({ text: 'You owe Khadra 4.500 JOD', tone: 'warn' });
    expect(view.notes[0]).toBe(
      'Worked out from the decision. Your payouts will show these figures once the booking is recorded there.',
    );
    expect(view.notes[1]).toContain('2026-10-07 10:00');
  });

  it('says it is the recorded payable once the ledger has one, with no window left to mention', () => {
    const view = officeOutcomeView(
      { ...outcome, source: 'Recorded', payableId: 'p1', furtherDecisionsPossibleUntil: null },
      words(en),
      format,
    );

    expect(view.notes).toEqual(['As recorded in your payouts.']);
  });

  it('says another open dispute can still change the projection, however long ago the window closed', () => {
    const view = officeOutcomeView(
      { ...outcome, furtherDecisionsPossibleUntil: null, anotherDisputeOpen: true },
      words(en),
      format,
    );

    expect(view.notes).toEqual([
      'Worked out from the decision. Your payouts will show these figures once the booking is recorded there.',
      'Another dispute on this booking is still open, and its decision can change these figures.',
    ]);
    expect(officeOutcomeView({ ...outcome, anotherDisputeOpen: true }, words(ar), format).notes).toContain(
      'ما زال نزاع آخر على هذا الحجز مفتوحًا، وقد يغيّر قراره هذه الأرقام.',
    );
  });
});

describe('previewWaitingKey (Wave 3, F66)', () => {
  it('asks for the three amounts to add up only while they do not', () => {
    expect(previewWaitingKey(false, false)).toBe('disputePreview.waiting');
    expect(previewWaitingKey(false, true)).toBe('disputePreview.waiting');
  });

  it('names a refused figure once the amounts balance, in both languages', () => {
    expect(previewWaitingKey(true, true)).toBe('disputePreview.waitingForValid');
    expect(en('disputePreview.waitingForValid')).toBe('Shown once every amount is valid.');
    expect(ar('disputePreview.waitingForValid')).toBe('يظهر حين تصبح المبالغ كلها صحيحة.');
  });
});
