import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { resolveMessage } from '../../core/i18n/resolve';
import { disputeParty, openedByText } from './dispute-presentation';

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
