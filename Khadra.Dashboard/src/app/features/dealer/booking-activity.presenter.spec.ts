import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { StatusScope, enumKey, spellEnumName, statusKey } from '../../core/i18n/status-key';
import { DealerActivityEntry } from '../../core/models/dealer-console.api';
import {
  ActivityWords,
  activityActor,
  activityEvent,
  activityIcon,
  activityReason,
  activitySentence,
} from './booking-activity.presenter';

/**
 * A change on an office's bookings, worded for the office (Wave 3: E2E F23 and F27), resolved against the REAL
 * dictionaries and the real status and party keys, so these read as the words the office sees.
 */
function wordsIn(dictionary: typeof EN | typeof AR, locale: string, rtl: boolean): ActivityWords {
  const t = (key: TranslationKey, params?: MessageParams) =>
    (resolveMessage(dictionary[key], params, locale, rtl) ?? key).replace(/[⁨⁩]/g, '');
  return {
    t,
    status: (name: string, scope?: StatusScope) => {
      const key = statusKey(name, scope);
      return key ? t(key) : spellEnumName(name);
    },
    party: (name: string) => {
      const key = enumKey('party', name);
      return key ? t(key) : spellEnumName(name);
    },
  };
}
const en = wordsIn(EN, 'en-GB', false);
const ar = wordsIn(AR, 'ar-JO-u-nu-latn', true);

const entry = (over: Partial<DealerActivityEntry> = {}): DealerActivityEntry => ({
  bookingId: 'b1',
  reference: 'KH-AAA111',
  toStatus: 'Approved',
  fromStatus: 'Requested',
  actorParty: 'Dealer',
  actorUserId: 'u1',
  actorName: 'Rana Haddad',
  reason: null,
  occurredAt: '2026-10-06T09:00:00Z',
  ...over,
});

describe('activityEvent: what happened, in the past tense (F23)', () => {
  it('names a request and an approval as history, never as what the booking waits for', () => {
    expect(activityEvent(entry({ toStatus: 'Requested', fromStatus: null }), en)).toBe('Requested');
    expect(activityEvent(entry({ toStatus: 'Approved' }), en)).toBe('Approved');
    expect(activityEvent(entry({ toStatus: 'Requested', fromStatus: null }), en)).not.toContain(
      'awaiting',
    );
    expect(activityEvent(entry({ toStatus: 'Approved' }), ar)).toBe('تمت الموافقة');
  });

  it('tells a request nobody answered from an approval nobody paid for', () => {
    expect(activityEvent(entry({ toStatus: 'Expired', fromStatus: 'Requested' }), en)).toBe(
      'Expired unanswered',
    );
    expect(activityEvent(entry({ toStatus: 'Expired', fromStatus: 'Approved' }), en)).toBe(
      'Expired unpaid',
    );
    expect(activityEvent(entry({ toStatus: 'Expired', fromStatus: 'Approved' }), ar)).toBe(
      'انتهت المدة دون دفع',
    );
  });

  it("words the handover as the handover, not as the queue's word for the rental", () => {
    expect(activityEvent(entry({ toStatus: 'PickedUp', fromStatus: 'Confirmed' }), en)).toBe(
      'Picked up',
    );
    expect(activityEvent(entry({ toStatus: 'Confirmed' }), en)).toBe(
      'Payment received — booking confirmed',
    );
    expect(activityEvent(entry({ toStatus: 'NoShow', fromStatus: 'Confirmed' }), en)).toBe(
      'Marked no-show',
    );
  });

  it('still words a status this build has no sentence for', () => {
    expect(activityEvent(entry({ toStatus: 'Cancelled' }), en)).toBe('Cancelled');
    expect(activityEvent(entry({ toStatus: 'SomethingNew' }), en)).toBe('Something new');
  });
});

describe('activityActor: who made the change (F27)', () => {
  it('names the member of staff on an office change, or the office when nobody signed it', () => {
    expect(activityActor(entry(), en)).toBe('Rana Haddad');
    expect(activityActor(entry({ actorUserId: null, actorName: null }), en)).toBe(
      'The rental office',
    );
    expect(activityActor(entry({ actorName: null }), en)).toBe('Former staff member');
  });

  it('calls the customer the customer, and the platform and an administrator Khadra', () => {
    const customer = entry({ actorParty: 'Customer', actorUserId: null, actorName: null });
    expect(activityActor(customer, en)).toBe('The customer');
    expect(activityActor(customer, ar)).toBe('العميل');
    expect(
      activityActor(entry({ actorParty: 'System', actorUserId: null, actorName: null }), en),
    ).toBe('Khadra');
    expect(
      activityActor(entry({ actorParty: 'Admin', actorUserId: null, actorName: null }), ar),
    ).toBe('خضرا');
  });

  it('never names a person the office did not have on its staff, whatever the entry carries', () => {
    // The server sends no id or name for these; the words do not depend on that.
    expect(activityActor(entry({ actorParty: 'Admin', actorName: 'An Administrator' }), en)).toBe(
      'Khadra',
    );
    expect(activityActor(entry({ actorParty: 'Customer', actorName: 'Sami Kamal' }), en)).toBe(
      'The customer',
    );
  });
});

describe('activityReason: only what somebody typed', () => {
  it("shows the office's and the customer's own words", () => {
    expect(activityReason(entry({ toStatus: 'Rejected', reason: 'Car in the garage.' }))).toBe(
      'Car in the garage.',
    );
    expect(
      activityReason(
        entry({ toStatus: 'Cancelled', actorParty: 'Customer', reason: 'Plans changed.' }),
      ),
    ).toBe('Plans changed.');
  });

  it("shows Khadra's reason for a cancellation, which both parties are told", () => {
    expect(
      activityReason(entry({ toStatus: 'Cancelled', actorParty: 'Admin', reason: 'Withdrawn.' })),
    ).toBe('Withdrawn.');
  });

  it("does not show the platform's own English on an expiry, a no-show or a completion", () => {
    expect(
      activityReason(
        entry({ toStatus: 'Expired', actorParty: 'System', reason: 'Dealer did not respond.' }),
      ),
    ).toBeNull();
    expect(
      activityReason(
        entry({ toStatus: 'Expired', actorParty: 'Admin', reason: 'Payment window elapsed.' }),
      ),
    ).toBeNull();
    expect(
      activityReason(
        entry({ toStatus: 'Completed', actorParty: 'System', reason: 'Dispute resolved.' }),
      ),
    ).toBeNull();
  });
});

describe("activitySentence: the dashboard's recent activity (F27)", () => {
  it("words the customer's changes with the customer as the actor", () => {
    const requested = entry({
      toStatus: 'Requested',
      fromStatus: null,
      actorParty: 'Customer',
      actorUserId: null,
      actorName: null,
    });
    expect(activitySentence(requested, en)).toBe('The customer requested booking KH-AAA111');
    expect(activitySentence(requested, ar)).toBe('طُلب الحجز KH-AAA111 من قِبل العميل');
    expect(
      activitySentence({ ...requested, toStatus: 'Confirmed', fromStatus: 'Approved' }, en),
    ).toBe('The customer paid for booking KH-AAA111');
    expect(
      activitySentence({ ...requested, toStatus: 'Cancelled', fromStatus: 'Confirmed' }, en),
    ).toBe('The customer cancelled booking KH-AAA111');
  });

  it("names no actor for what the platform's clock did", () => {
    const system = { actorParty: 'System', actorUserId: null, actorName: null };
    expect(
      activitySentence(entry({ ...system, toStatus: 'Expired', fromStatus: 'Requested' }), en),
    ).toBe('Booking KH-AAA111 expired unanswered');
    expect(
      activitySentence(entry({ ...system, toStatus: 'Expired', fromStatus: 'Approved' }), en),
    ).toBe('Booking KH-AAA111 expired unpaid');
    expect(
      activitySentence(entry({ ...system, toStatus: 'Completed', fromStatus: 'Returned' }), ar),
    ).toBe('اكتمل الحجز KH-AAA111');
  });

  it('keeps the office’s own sentences', () => {
    expect(activitySentence(entry(), en)).toBe('Rana Haddad approved booking KH-AAA111');
    expect(
      activitySentence(
        entry({ toStatus: 'Cancelled', actorParty: 'Admin', actorUserId: null, actorName: null }),
        en,
      ),
    ).toBe('Khadra cancelled booking KH-AAA111');
  });
});

describe('activityIcon', () => {
  it('marks an ending that went wrong, one that went right, and a handover', () => {
    expect(activityIcon({ toStatus: 'Expired' })).toBe('x-circle');
    expect(activityIcon({ toStatus: 'Confirmed' })).toBe('check-circle');
    expect(activityIcon({ toStatus: 'Returned' })).toBe('key');
  });
});
