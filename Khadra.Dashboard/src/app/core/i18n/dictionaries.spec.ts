import { describe, expect, it } from 'vitest';
import { AR } from './ar';
import { EN, TranslationKey } from './en';
import { Message } from './language';

/**
 * The dictionaries, checked against each other and against Arabic's own grammar.
 *
 * `ar.ts` is typed `Record<TranslationKey, Message>`, so a MISSING key already fails the build. That
 * leaves the failures a type cannot see: a key in Arabic that no longer exists in English, a
 * placeholder that was dropped in translation — which silently prints a sentence with a hole in it —
 * and a plural that was translated as though Arabic had two forms like English.
 */

const placeholders = (message: Message): Set<string> => {
  const forms = typeof message === 'string' ? [message] : Object.values(message);
  const found = new Set<string>();
  for (const form of forms) {
    for (const match of (form ?? '').matchAll(/\{(\w+)\}/g)) found.add(match[1]);
  }
  return found;
};

/** Entries that are meant to read identically in both: the brand, and each language's own name. */
const UNTRANSLATED_BY_DESIGN = new Set<string>(['lang.en', 'lang.ar', 'app.name']);

describe('translation dictionaries', () => {
  it('carry exactly the same keys', () => {
    // The type catches Arabic missing an English key. This catches the other direction: a key
    // deleted from en.ts leaves dead Arabic behind, which nobody notices because nothing renders it.
    expect(Object.keys(AR).sort()).toEqual(Object.keys(EN).sort());
  });

  it('never leave an Arabic entry as the English text', () => {
    // A key whose Arabic is byte-identical to its English is almost always one that was pasted and
    // not yet translated. The genuine exceptions are the two language names, which are each written
    // in their own script on purpose.
    const untranslated = (Object.keys(EN) as (keyof typeof EN)[])
      .filter((key) => !UNTRANSLATED_BY_DESIGN.has(key))
      .filter((key) => typeof EN[key] === 'string' && typeof AR[key] === 'string')
      .filter((key) => EN[key] === AR[key]);

    expect(untranslated).toEqual([]);
  });

  it('keep every placeholder the English sentence promised', () => {
    // '{name} is suspended' translated without its {name} renders a sentence about nobody.
    for (const key of Object.keys(EN) as (keyof typeof EN)[]) {
      expect(
        [...placeholders(AR[key])].sort(),
        `placeholders differ for "${key}"`,
      ).toEqual([...placeholders(EN[key])].sort());
    }
  });

  it('give Arabic plurals all six forms', () => {
    // Arabic distinguishes zero, one, two, few, many and other. A translation that supplies only
    // `one` and `other`, mirroring English, is grammatical only by accident: three bookings and
    // eleven bookings take different forms of the noun, and both differ from two.
    const required: Intl.LDMLPluralRule[] = ['zero', 'one', 'two', 'few', 'many', 'other'];

    for (const key of Object.keys(EN) as (keyof typeof EN)[]) {
      const english = EN[key];
      if (typeof english === 'string') continue;

      const arabic = AR[key];
      expect(typeof arabic, `"${key}" is plural in English but not in Arabic`).not.toBe('string');
      // `zero` is only needed where English bothered to write one; the rest are always required.
      const forms = english as Partial<Record<Intl.LDMLPluralRule, string>>;
      const needed = required.filter((form) => form !== 'zero' || forms.zero !== undefined);
      const missing = needed.filter((form) => (arabic as Record<string, string>)[form] === undefined);
      expect(missing, `"${key}" is missing Arabic plural forms`).toEqual([]);
    }
  });

  it('use the Customer App words for a rental office and a car', () => {
    // The owner decided on 2026-09-13 that the console speaks the Customer App's Arabic
    // (Khadra.Mobile/lib/l10n/app_ar.arb): a dealership is "مكتب" / "مكتب التأجير" and a vehicle is
    // "سيارة", so a customer and the office they rent from use the same words. The console used to
    // say "معرض" and "مركبة"; this stops a later batch bringing either back, in any form (المعارض,
    // معرضك, مركبات, مركبتك). "معارضة", an objection, is a different word and stays allowed.
    const retired = /معرض|معارض(?!ة)|مركب[ةتا]/;
    const offending = (Object.keys(AR) as (keyof typeof AR)[]).filter((key) => {
      const message: Message = AR[key];
      const forms = typeof message === 'string' ? [message] : Object.values(message);
      return forms.some((form) => retired.test(form ?? ''));
    });

    expect(offending).toEqual([]);
  });

  it('never glue the Arabic article onto a placeholder', () => {
    // «معروض بالـ{language}» rendered «بالـالإنجليزية»: the names that fill these placeholders — a
    // language, a city, a status — arrive already carrying «ال», so an article typed in front of the
    // brace is always a second one. Checked across every key, because the defect is invisible in the
    // dictionary and only shows once a real name is dropped in.
    const glued = /الـ?\{/; // «ال», optionally with a tatweel, then «{»
    const offending = (Object.keys(AR) as (keyof typeof AR)[]).filter((key) => {
      const message: Message = AR[key];
      const forms = typeof message === 'string' ? [message] : Object.values(message);
      return forms.some((form) => glued.test(form ?? ''));
    });

    expect(offending).toEqual([]);
  });

  it('name the fallback language in a phrase that reads whole in both', () => {
    // The badge a customer-page preview shows when the office has not written a section in that
    // language. Resolved with the real language names, as the screen resolves it.
    const arabic = (AR['dealerCustomerPage.shownIn'] as string).replace(
      '{language}',
      AR['lookups.english'] as string,
    );
    const english = (EN['dealerCustomerPage.shownIn'] as string).replace(
      '{language}',
      EN['lookups.arabic'] as string,
    );

    expect(arabic).toBe('باللغة الإنجليزية');
    expect(english).toBe('shown in Arabic');
  });
});

/**
 * Office copy that states a rule (Wave 3: checklist 168 and 185, E2E F22 and F24). Each said something that stopped
 * being true when the rule moved, and went on saying it: the free-cancellation window "starts now" at approval, and a
 * request "expires when its rental date arrives". Neither sentence carries a number, so neither can go stale that way.
 */
describe('office copy that states a rule', () => {
  const text = (dictionary: typeof EN | typeof AR, key: TranslationKey) => dictionary[key] as string;
  const EXPIRY: TranslationKey[] = [
    'dealerDashboard.aRequestExpiresWhen',
    'dealerDash.oldestMadeExpiry',
    'notifications.aRequestExpiresWhen',
    'notifications.oldestAndExpiry',
    'dealerBookings.onceYourVehiclesAre',
  ];

  it('opens the free-cancellation window at payment, and holds the car to the payment deadline', () => {
    expect(text(EN, 'dealerDecide.approve.body')).toContain('free-cancellation window opens when they pay');
    expect(text(EN, 'dealerDecide.approve.body')).toContain('payment deadline');
    expect(text(EN, 'dealerDecide.approve.body')).not.toContain('starts now');
    expect(text(AR, 'dealerDecide.approve.body')).toContain('مهلة الإلغاء المجاني للعميل عند الدفع');
    expect(text(AR, 'dealerDecide.approve.body')).toContain('مهلة الدفع');
    expect(text(AR, 'dealerDecide.approve.body')).not.toContain('من الآن');
  });

  it('expires a request at its answer deadline, not at its rental date', () => {
    for (const key of EXPIRY) {
      expect(text(EN, key), key).toMatch(/answer deadline|expires \{deadline\}/);
      expect(text(EN, key), key).not.toMatch(/rental date|before pickup/);
      expect(text(AR, key), key).toMatch(/مهلة الرد|ينتهي \{deadline\}/);
      expect(text(AR, key), key).not.toMatch(/تاريخ (الإيجار|تأجيره|التأجير)/);
    }
  });

  it('writes no business number into either rule', () => {
    for (const key of ['dealerDecide.approve.body', ...EXPIRY] as TranslationKey[]) {
      expect(text(EN, key), key).not.toMatch(/[0-9]/);
      expect(text(AR, key), key).not.toMatch(/[0-9٠-٩]/);
    }
  });
});

/**
 * Who sees a dispute decision (owner decisions D5 and Q4, 2026-10-06): the customer sees it as Khadra's, with the
 * note but never the administrator's name; the office sees only its own share, with the note and the name. The form
 * and the confirmation dialog both state it, and the dialog once kept the older "both parties see your name" after
 * the form had changed (E2E F75).
 */
describe("the administrator's promise about who sees a dispute decision", () => {
  const PROMISES: TranslationKey[] = [
    'disputeDetail.theDecisionYourNote',
    'disputeDetail.resolveConfirmBody',
    'disputeDetail.resolveConfirmBodyWithCharge',
  ];

  it("names the decision Khadra's and keeps the administrator's name from the customer", () => {
    for (const key of PROMISES) {
      const en = EN[key] as string;
      const ar = AR[key] as string;
      expect(en, key).toContain("as Khadra's");
      expect(en, key).toContain('never your name');
      expect(en, key).not.toMatch(/both parties/i);
      expect(ar, key).toContain('قرار خضرا');
      expect(ar, key).toContain('دون اسمك');
      expect(ar, key).not.toContain('الطرفان');
    }
  });
});
