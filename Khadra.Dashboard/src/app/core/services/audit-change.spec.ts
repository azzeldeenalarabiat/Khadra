import { describe, expect, it } from 'vitest';
import { AR } from '../i18n/ar';
import { EN } from '../i18n/en';
import { resolveMessage } from '../i18n/resolve';
import { AuditChangeFacts, AuditValueWords, auditValueText } from './audit-change';
import { Translate } from './dashboard.presenter';

/**
 * The audit log's Change column (pre-launch items 50 and 174): names and parts in the reader's language, anything else
 * exactly as it was recorded.
 */
const t: Translate = (key, params) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
const tAr: Translate = (key, params) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[⁨⁩]/g, '');

const english: AuditValueWords = {
  t,
  arabic: false,
  storedMoney: (amount, currency) => `${amount} ${currency}`,
};
const arabic: AuditValueWords = {
  t: tAr,
  arabic: true,
  storedMoney: (amount, currency) => `${amount} ${currency}`,
};

const entry = (over: Partial<AuditChangeFacts>): AuditChangeFacts => ({
  action: 'DealerApproved',
  entityType: 'Dealer',
  previousValue: 'PendingReview',
  newValue: 'Approved',
  ...over,
});

describe('auditValueText', () => {
  it('words a status by the record it belongs to', () => {
    expect(auditValueText(entry({}), 'previous', english)).toEqual({
      kind: 'worded',
      text: 'Pending review',
    });
    const suspended = auditValueText(entry({ newValue: 'Suspended' }), 'new', arabic);
    expect(suspended?.kind).toBe('worded');
    expect(suspended?.text).not.toContain('Suspended');

    const booking = entry({
      action: 'BookingCancelledByAdmin',
      entityType: 'Booking',
      previousValue: 'Confirmed',
      newValue: 'Cancelled',
    });
    expect(auditValueText(booking, 'new', english)?.kind).toBe('worded');
    expect(
      auditValueText(
        entry({
          action: 'AdminInvited',
          entityType: 'AdminUser',
          previousValue: null,
          newValue: 'Admin',
        }),
        'new',
        english,
      ),
    ).toEqual({ kind: 'worded', text: 'Administrator' });
    expect(
      auditValueText(
        entry({
          action: 'ReviewHidden',
          entityType: 'Review',
          previousValue: null,
          newValue: 'AbusiveLanguage',
        }),
        'new',
        english,
      ),
    ).toEqual({ kind: 'worded', text: 'Abusive language' });
    expect(
      auditValueText(
        entry({
          action: 'OfficePayableHeld',
          entityType: 'OfficePayable',
          previousValue: null,
          newValue: 'Manual',
        }),
        'new',
        english,
      ),
    ).toEqual({ kind: 'worded', text: 'Held by an administrator' });
  });

  it("words a customer document's slot and standing, and keeps anything else as it was", () => {
    const rejected = entry({
      action: 'CustomerDocumentRejected',
      entityType: 'Customer',
      previousValue: 'Passport:PendingReview',
      newValue: 'Passport:Rejected',
    });
    expect(auditValueText(rejected, 'new', english)?.text).toBe('Passport: Rejected');
    expect(auditValueText({ ...rejected, newValue: 'Passport' }, 'new', english)).toEqual({
      kind: 'recorded',
      text: 'Passport',
    });
  });

  it('composes a dispute decision from its figures, in both languages, with a charge when there was one (item 50)', () => {
    const resolved = entry({
      action: 'DisputeResolved',
      entityType: 'Dispute',
      previousValue: 'UnderReview',
      newValue:
        '{"currency":"JOD","held":"18.000","refund":"10.000","platform":"4.000","dealer":"4.000"}',
    });
    expect(auditValueText(resolved, 'new', english)?.text).toBe(
      'Of 18.000 JOD held: 10.000 JOD back to the customer, 4.000 JOD to Khadra, 4.000 JOD to the office',
    );
    const inArabic = auditValueText(resolved, 'new', arabic);
    expect(inArabic?.kind).toBe('worded');
    expect(inArabic?.text).toContain('10.000 JOD');
    expect(inArabic?.text).not.toMatch(/refund|held|platform/);
    expect(auditValueText(resolved, 'previous', english)).toEqual({
      kind: 'worded',
      text: 'Under review',
    });

    const charged = {
      ...resolved,
      newValue: resolved.newValue!.replace('}', ',"charge":"30.000","chargeCurrency":"JOD"}'),
    };
    expect(auditValueText(charged, 'new', english)?.text).toContain(
      'the office charged 30.000 JOD',
    );
  });

  it('shows a decision recorded before 2026-10-08 exactly as it was written', () => {
    const old = entry({
      action: 'DisputeResolved',
      entityType: 'Dispute',
      newValue: 'Resolved: of 18.000 JOD held, refund 18.000, platform 0.000, dealer 0.000',
    });
    expect(auditValueText(old, 'new', arabic)).toEqual({ kind: 'recorded', text: old.newValue });
  });

  it("composes a handover's outcome and a locked code (item 174)", () => {
    const verified = entry({
      action: 'HandoverVerified',
      entityType: 'Booking',
      previousValue: 'Confirmed',
      newValue: '{"status":"PickedUp","handover":"Pickup","method":"Code"}',
    });
    const text = auditValueText(verified, 'new', english)?.text;
    expect(text).toMatch(/^.+ \(Pickup, With the customer's code\)$/);

    const locked = entry({
      action: 'HandoverCodeLocked',
      entityType: 'Booking',
      previousValue: 'Confirmed',
      newValue: '{"handover":"Pickup","wrongTries":5}',
    });
    expect(auditValueText(locked, 'new', english)?.text).toBe(
      'Pickup code locked after 5 wrong tries',
    );
    expect(auditValueText(locked, 'new', arabic)?.text).toBe(
      'قُفل رمز التسليم بعد 5 محاولات خاطئة',
    );
    // The old English line stays as written.
    expect(
      auditValueText(
        { ...locked, newValue: 'Pickup code locked after 5 wrong tries' },
        'new',
        arabic,
      )?.kind,
    ).toBe('recorded');
  });

  it("names a city in the reader's language, with whether it is offered (items 174, 176)", () => {
    const retired = entry({
      action: 'LookupRetired',
      entityType: 'City',
      previousValue: '{"en":"Madaba","ar":"مادبا","offered":true}',
      newValue: '{"en":"Madaba","ar":"مادبا","offered":false}',
    });
    expect(auditValueText(retired, 'previous', english)?.text).toBe('Madaba · Offered');
    expect(auditValueText(retired, 'new', english)?.text).toBe('Madaba · Retired');
    const inArabic = auditValueText(retired, 'new', arabic)?.text ?? '';
    expect(inArabic.startsWith('مادبا · ')).toBe(true);
    expect(inArabic).not.toContain('Madaba');
    // "Amman / عمّان · Offered", from before the parts, is history.
    expect(
      auditValueText({ ...retired, newValue: 'Madaba / مادبا · Retired' }, 'new', arabic)?.kind,
    ).toBe('recorded');
  });

  it('keeps numbers, amounts and anything it does not know as recorded, and has nothing for an empty side', () => {
    expect(
      auditValueText(
        entry({
          action: 'FinancialDocumentVoided',
          entityType: 'FinancialDocument',
          previousValue: 'R-2026-000123',
        }),
        'previous',
        english,
      ),
    ).toEqual({ kind: 'recorded', text: 'R-2026-000123' });
    expect(
      auditValueText(
        entry({
          action: 'OfficeSettlementRecorded',
          entityType: 'OfficeSettlement',
          newValue: '12.500 JOD',
        }),
        'new',
        arabic,
      ),
    ).toEqual({ kind: 'recorded', text: '12.500 JOD' });
    expect(auditValueText(entry({ newValue: 'SomethingNewer' }), 'new', english)).toEqual({
      kind: 'recorded',
      text: 'SomethingNewer',
    });
    expect(
      auditValueText(
        entry({ action: 'DisputeResolved', entityType: 'Dispute', newValue: '{not json' }),
        'new',
        english,
      ),
    ).toEqual({ kind: 'recorded', text: '{not json' });
    expect(auditValueText(entry({ previousValue: null }), 'previous', english)).toBeNull();
  });
});
