import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { DisputeRefund, disputeParty, disputeRefundText, openedByText } from './dispute-presentation';

/** Against the REAL dictionaries, so these read as the words a customer sees on a dispute. */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
// Arabic wraps every value in zero-width direction isolates (resolve.ts); compare the visible words.
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[\u2068\u2069]/g, '');

describe('who opened a dispute (pre-launch item 218)', () => {
  it("says the customer's own was opened by them in words Arabic allows: «فُتح من قِبلك», never «فتحه أنت»", () => {
    expect(openedByText(en, 'Customer', '28 Sept 2026')).toBe('Opened by you on 28 Sept 2026');
    expect(openedByText(ar, 'Customer', '28 Sept 2026')).toBe('فُتح من قِبلك في 28 Sept 2026');
    expect(openedByText(ar, 'Customer', '28 Sept 2026')).not.toContain('أنت');
  });

  it('names the rental office and Khadra as before, and a party this build does not know as it came', () => {
    expect(openedByText(en, 'Dealer', '28 Sept 2026')).toBe('Opened by the rental office on 28 Sept 2026');
    expect(openedByText(ar, 'Dealer', '28 Sept 2026')).toBe('فتحه مكتب التأجير في 28 Sept 2026');
    expect(openedByText(ar, 'Admin', '28 Sept 2026')).toBe('فتحه خضرا في 28 Sept 2026');
    expect(disputeParty(en, 'SomethingNewer')).toBe('SomethingNewer');
  });
});

/**
 * What became of the customer's refund from a decided dispute (E2E F43). The page used to say the amounts were
 * "not a payment that has already been made to you" — after the refund had settled.
 */
describe('the refund a dispute decision gave the customer', () => {
  const TICKET = '01a10668-c224-7754-9629-7be4cb3ddf2a';
  const money = (value: DisputeRefund['amount']) => `${value.amount.toFixed(3)} ${value.currency}`;
  const date = (iso: string) => iso.slice(0, 10);
  const refund = (status: string, overrides: Partial<DisputeRefund> = {}): DisputeRefund => ({
    reason: 'DisputeResolution',
    amount: { amount: 5, currency: 'JOD' },
    status,
    requestedAt: '2026-10-04T10:16:49Z',
    sentAt: status === 'Requested' ? null : '2026-10-04T10:17:40Z',
    settledAt: status === 'Settled' ? '2026-10-04T10:18:05Z' : null,
    disputeTicketId: TICKET,
    ...overrides,
  });

  it('says it was refunded, and when, once it settled — in English and in Arabic', () => {
    expect(disputeRefundText(en, TICKET, [refund('Settled')], money, date)).toBe(
      '5.000 JOD was refunded to your original payment method on 2026-10-04. Your bank may take additional time to show it.',
    );
    expect(disputeRefundText(ar, TICKET, [refund('Settled')], money, date)).toBe(
      'تم استرداد 5.000 JOD إلى وسيلة الدفع الأصلية في 2026-10-04. قد يحتاج البنك بعض الوقت لإظهار المبلغ في حسابك.',
    );
  });

  it('says it is on its way, requested, or being sent again — never that it was not paid', () => {
    expect(disputeRefundText(en, TICKET, [refund('Sent')], money, date)).toContain('is on its way');
    expect(disputeRefundText(en, TICKET, [refund('Requested')], money, date)).toContain('has been requested');
    expect(disputeRefundText(en, TICKET, [refund('Failed')], money, date)).toContain('Khadra is sending it again');
    for (const status of ['Settled', 'Sent', 'Requested', 'Failed']) {
      expect(disputeRefundText(en, TICKET, [refund(status)], money, date)).not.toContain('not a payment');
    }
  });

  it("reads only this ticket's refund, and says nothing when there is none or its status is newer than this build", () => {
    const otherTicket = refund('Settled', { disputeTicketId: '01a00000-0000-0000-0000-000000000000' });
    const freeCancellation = refund('Settled', { reason: 'FreeCancellation', disputeTicketId: null });
    expect(disputeRefundText(en, TICKET, [otherTicket, freeCancellation], money, date)).toBeNull();
    expect(disputeRefundText(en, TICKET, undefined, money, date)).toBeNull();
    expect(disputeRefundText(en, TICKET, [refund('SomethingNewer')], money, date)).toBeNull();
  });
});
