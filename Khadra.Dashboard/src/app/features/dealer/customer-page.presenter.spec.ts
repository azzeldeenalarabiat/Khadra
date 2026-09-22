import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN } from '../../core/i18n/en';
import { resolveMessage } from '../../core/i18n/resolve';
import {
  CustomerPageView,
  LocalizedText,
  ResolvedText,
  VisibleCustomerPage,
} from '../../core/models/dealer-console.api';
import {
  Translate,
  boxKey,
  customerPageDirty,
  customerPageDraft,
  customerPagePreview,
  customerPagePreviews,
  customerPageRequest,
  customerPageRows,
  sectionText,
  unknownCustomerPageSections,
} from './customer-page.presenter';

/**
 * The customer page editor (spec 4.1), now in two languages.
 *
 * Three rules carry this screen and all three are the server's, deliberately. WHICH sections an
 * office may hide is the server's list — so a console cannot offer to take the opening hours, the
 * delivery terms, the address, the rating or the cars off a page, because those are not on it. What a
 * customer SEES is the server's answer, from the code the public page runs — so an owner's preview
 * cannot drift from their page. And WHICH LANGUAGE a customer gets is the server's too: it resolves
 * the fallback once, and both previews here are that answer read back.
 *
 * The one thing this file is strict about on its own side is the difference between an owner's copy
 * and a customer's. An owner edits what they WROTE — an empty English box is the work still to do, so
 * the form must never be seeded with resolved text. A customer reads what there IS.
 *
 * The real dictionaries are resolved, so these assertions read as the words an owner sees.
 */
const en: Translate = (key, params) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
const ar: Translate = (key, params) =>
  resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key;

/** Every section the platform has today, in the order the API sends them. */
const ALL = [
  'About',
  'RentalConditions',
  'Insurance',
  'PickupInstructions',
  'DeliveryNotes',
  'CustomerNotes',
];

const both = (arabic: string, english: string): LocalizedText => ({ ar: arabic, en: english });
const arabicOnly = (arabic: string): LocalizedText => ({ ar: arabic, en: null });
const englishOnly = (english: string): LocalizedText => ({ ar: null, en: english });
const unwritten: LocalizedText = { ar: null, en: null };

const shown = (text: string, language: 'ar' | 'en'): ResolvedText => ({ text, language });

const nothingVisible: VisibleCustomerPage = {
  about: null,
  rentalConditions: null,
  insurance: null,
  pickupInstructions: null,
  deliveryNotes: null,
  customerNotes: null,
};

function page(over: Partial<CustomerPageView> = {}): CustomerPageView {
  return {
    about: both('مكتب عائلي في العبدلي منذ 1998.', 'A family office in Abdali since 1998.'),
    rentalConditions: both('ممنوع التدخين في أي سيارة.', 'No smoking in any car.'),
    insurance: unwritten,
    pickupInstructions: unwritten,
    deliveryNotes: unwritten,
    customerNotes: unwritten,
    hiddenSections: [],
    sections: ALL,
    maxTextLength: 2000,
    deliveryEnabled: true,
    visible: {
      ar: {
        ...nothingVisible,
        about: shown('مكتب عائلي في العبدلي منذ 1998.', 'ar'),
        rentalConditions: shown('ممنوع التدخين في أي سيارة.', 'ar'),
      },
      en: {
        ...nothingVisible,
        about: shown('A family office in Abdali since 1998.', 'en'),
        rentalConditions: shown('No smoking in any car.', 'en'),
      },
    },
    ...over,
  };
}

describe('the customer page editor', () => {
  it('builds its switches from the sections the server sent, and only those', () => {
    const rows = customerPageRows(page({ sections: ['About', 'Insurance'] }), en);

    expect(rows.map((row) => row.name)).toEqual(['About', 'Insurance']);
    expect(rows.map((row) => row.field)).toEqual(['about', 'insurance']);
  });

  it('leaves out a section this build has no box for', () => {
    // A section the platform adds before the console learns it. A row with no label and no field to
    // write into would be a switch that cannot save.
    const rows = customerPageRows(page({ sections: ['About', 'LoyaltyScheme'] }), en);

    expect(rows.map((row) => row.name)).toEqual(['About']);
  });

  it('reports the section it left out, so the page can refuse to save over it', () => {
    // Leaving a section out of the form is only safe if the page cannot save: a save replaces the
    // whole page, so a section with no box would be sent as nothing and erased.
    expect(unknownCustomerPageSections(page({ sections: ['About', 'LoyaltyScheme'] }))).toEqual([
      'LoyaltyScheme',
    ]);
  });

  it('reports nothing when this build has a box for every section', () => {
    expect(unknownCustomerPageSections(page())).toEqual([]);
  });

  it('drops from the form exactly what it reports, and nothing else', () => {
    const sections = ['LoyaltyScheme', 'About', 'Accessibility', 'Insurance'];
    const rows = customerPageRows(page({ sections }), en).map((row) => row.name);
    const unknown = unknownCustomerPageSections(page({ sections }));

    expect(rows).toEqual(['About', 'Insurance']);
    expect(unknown).toEqual(['LoyaltyScheme', 'Accessibility']);
    expect([...rows, ...unknown].sort()).toEqual([...sections].sort());
  });

  it('does not take a name an object inherits for a section it knows', () => {
    // `constructor` and `toString` answer on any plain object. Read as known, they would render a
    // row with no field and — worse — leave the refusal to save with nothing to report.
    const sections = ['About', 'constructor', 'toString'];

    expect(customerPageRows(page({ sections }), en).map((row) => row.name)).toEqual(['About']);
    expect(unknownCustomerPageSections(page({ sections }))).toEqual(['constructor', 'toString']);
    expect(Object.keys(customerPageDraft(page({ sections })))).toEqual(['aboutAr', 'aboutEn']);
  });

  it('labels every section in the language the reader chose', () => {
    const rows = customerPageRows(page(), ar);

    expect(rows[0].label).toBe(AR['dealerCustomerPage.about']);
    expect(rows[0].label).not.toBe(EN['dealerCustomerPage.about']);
    expect(rows.every((row) => row.placeholder.trim() !== '')).toBe(true);
  });

  it('marks the delivery notes while delivery is off', () => {
    const off = customerPageRows(page({ deliveryEnabled: false }), en);
    const on = customerPageRows(page({ deliveryEnabled: true }), en);

    expect(off.find((row) => row.field === 'deliveryNotes')?.muted).toBe(true);
    expect(on.find((row) => row.field === 'deliveryNotes')?.muted).toBe(false);
    // And nothing else is muted by it: delivery is the only section with a second reason to be off.
    expect(off.filter((row) => row.muted)).toHaveLength(1);
  });
});

describe('the two boxes a section is edited through', () => {
  it('names each box the way the SERVER names it, so a refusal lands under the right one', () => {
    // Not a convenience. The API rejects `rentalConditionsAr` by that name, and the console shows
    // the message under the box keyed by it without translating anything in between.
    expect(boxKey('rentalConditions', 'ar')).toBe('rentalConditionsAr');
    expect(boxKey('rentalConditions', 'en')).toBe('rentalConditionsEn');
    expect(boxKey('about', 'ar')).toBe('aboutAr');
    expect(boxKey('pickupInstructions', 'en')).toBe('pickupInstructionsEn');
  });

  it('seeds both boxes from what the office WROTE, never from what a customer would see', () => {
    // The whole point of two boxes. An office that has written only Arabic must find an empty
    // English box: that empty box is the work still to do, and seeding it with the Arabic — which is
    // what a customer correctly gets — would make the page look finished and then save the Arabic
    // text into the English column on the next save.
    const draft = customerPageDraft(
      page({
        about: arabicOnly('مكتب عائلي في العبدلي.'),
        visible: {
          ar: { ...nothingVisible, about: shown('مكتب عائلي في العبدلي.', 'ar') },
          // The customer asking in English gets the Arabic, because it is all there is.
          en: { ...nothingVisible, about: shown('مكتب عائلي في العبدلي.', 'ar') },
        },
      }),
    );

    expect(draft['aboutAr']).toBe('مكتب عائلي في العبدلي.');
    expect(draft['aboutEn']).toBe('');
  });

  it('reads one language of one section out of the owner’s own copy', () => {
    const saved = page({ insurance: englishOnly('Comprehensive, 200 JOD excess.') });

    expect(sectionText(saved, 'insurance', 'en')).toBe('Comprehensive, 200 JOD excess.');
    expect(sectionText(saved, 'insurance', 'ar')).toBeNull();
    expect(sectionText(saved, 'deliveryNotes', 'ar')).toBeNull();
  });
});

describe('what a save sends', () => {
  it('sends every section in both languages, so one cleared is one cleared', () => {
    // A patch would leave text on a customer's screen that its owner can no longer see. And a
    // language left out of a pair would be the same defect one level down.
    const request = customerPageRequest(
      { aboutAr: 'ما زلنا هنا.', aboutEn: 'Still here.', insuranceEn: '   ' },
      new Set(),
    );

    expect(request).toEqual({
      about: { ar: 'ما زلنا هنا.', en: 'Still here.' },
      rentalConditions: unwritten,
      insurance: unwritten,
      pickupInstructions: unwritten,
      deliveryNotes: unwritten,
      customerNotes: unwritten,
      hiddenSections: [],
    });
  });

  it('sends one written language and a null beside it, not an empty string', () => {
    // "Nothing written" and "written as nothing" are different answers, and only the first one is
    // eligible for the other language to stand in for.
    const request = customerPageRequest({ rentalConditionsAr: 'ممنوع التدخين.' }, new Set());

    expect(request.rentalConditions).toEqual({ ar: 'ممنوع التدخين.', en: null });
    expect(request.rentalConditions.en).toBeNull();
  });

  it('sends the switches that are off as the hidden set', () => {
    const request = customerPageRequest(
      { aboutAr: 'مكتوب.', aboutEn: 'Written.' },
      new Set(['About', 'CustomerNotes']),
    );

    expect([...request.hiddenSections].sort()).toEqual(['About', 'CustomerNotes']);
    // Hiding does not clear: the text is still saved, and switching it back on shows it again.
    expect(request.about).toEqual({ ar: 'مكتوب.', en: 'Written.' });
  });
});

describe('knowing an untouched form from an edited one', () => {
  it('calls a form seeded from the server clean', () => {
    const saved = page();
    const draft = customerPageDraft(saved);

    expect(customerPageDirty(saved, draft, new Set(saved.hiddenSections))).toBe(false);
  });

  it('notices a change in EITHER language on its own', () => {
    const saved = page();
    const draft = customerPageDraft(saved);

    expect(customerPageDirty(saved, { ...draft, insuranceAr: 'تأمين شامل.' }, new Set())).toBe(
      true,
    );
    expect(customerPageDirty(saved, { ...draft, insuranceEn: 'Comprehensive.' }, new Set())).toBe(
      true,
    );
    // And the other language being untouched is not a reason to miss the one that moved.
    expect(customerPageDirty(saved, { ...draft, aboutEn: 'Rewritten.' }, new Set())).toBe(true);
  });

  it('calls a switch flipped a change, with every box untouched', () => {
    const saved = page();

    expect(customerPageDirty(saved, customerPageDraft(saved), new Set(['About']))).toBe(true);
  });

  it('does not call trailing whitespace a change', () => {
    const saved = page();
    const draft = customerPageDraft(saved);

    expect(
      customerPageDirty(saved, { ...draft, aboutEn: `${draft['aboutEn']}   ` }, new Set()),
    ).toBe(false);
    expect(
      customerPageDirty(saved, { ...draft, aboutAr: `${draft['aboutAr']}   ` }, new Set()),
    ).toBe(false);
  });
});

describe('the two previews', () => {
  it('previews what the SERVER says each audience sees, not what is typed', () => {
    // The office has written insurance and hidden its About. The server has already applied both,
    // and the preview repeats its answer rather than working the rule out a second time.
    const saved = page({
      insurance: both('تأمين شامل.', 'Comprehensive cover.'),
      hiddenSections: ['About'],
      visible: {
        ar: {
          ...nothingVisible,
          rentalConditions: shown('ممنوع التدخين في أي سيارة.', 'ar'),
          insurance: shown('تأمين شامل.', 'ar'),
        },
        en: {
          ...nothingVisible,
          rentalConditions: shown('No smoking in any car.', 'en'),
          insurance: shown('Comprehensive cover.', 'en'),
        },
      },
    });

    const arabic = customerPagePreview(saved, 'ar', en);
    const english = customerPagePreview(saved, 'en', en);

    expect(arabic.map((row) => row.text)).toEqual(['ممنوع التدخين في أي سيارة.', 'تأمين شامل.']);
    expect(english.map((row) => row.text)).toEqual([
      'No smoking in any car.',
      'Comprehensive cover.',
    ]);
    // Both keep the server's order and the reader's labels, and neither invents a heading for a
    // section the server left out.
    expect(english.map((row) => row.label)).toEqual([
      EN['dealerCustomerPage.rentalConditions'],
      EN['dealerCustomerPage.insurance'],
    ]);
    expect(arabic.every((row) => !row.isFallback)).toBe(true);
    expect(english.every((row) => !row.isFallback)).toBe(true);
  });

  it('marks a row the other language is standing in for, in either direction', () => {
    // The fallback made visible to the owner. Neither row is wrong for the customer — it is what
    // there is — but each is a section not yet written in that language, and the badge says so.
    const saved = page({
      insurance: englishOnly('Comprehensive, 200 JOD excess.'),
      pickupInstructions: arabicOnly('أحضر الرخصة الأصلية.'),
      visible: {
        ar: {
          ...nothingVisible,
          insurance: shown('Comprehensive, 200 JOD excess.', 'en'),
          pickupInstructions: shown('أحضر الرخصة الأصلية.', 'ar'),
        },
        en: {
          ...nothingVisible,
          insurance: shown('Comprehensive, 200 JOD excess.', 'en'),
          pickupInstructions: shown('أحضر الرخصة الأصلية.', 'ar'),
        },
      },
    });

    const arabic = customerPagePreview(saved, 'ar', en);
    const english = customerPagePreview(saved, 'en', en);

    // English text under an Arabic preview: a fallback, tagged with the language it is actually in
    // so the row can be given the right direction as well as the right badge.
    expect(arabic.find((row) => row.label === EN['dealerCustomerPage.insurance'])).toMatchObject({
      language: 'en',
      isFallback: true,
    });
    // And the same thing the other way round, which is the case a single-direction fallback misses.
    expect(
      english.find((row) => row.label === EN['dealerCustomerPage.pickupInstructions']),
    ).toMatchObject({ language: 'ar', isFallback: true });

    // A row in the language asked for is not a fallback in either preview.
    expect(
      arabic.find((row) => row.label === EN['dealerCustomerPage.pickupInstructions'])?.isFallback,
    ).toBe(false);
    expect(
      english.find((row) => row.label === EN['dealerCustomerPage.insurance'])?.isFallback,
    ).toBe(false);
  });

  it('shows a section written in both languages to each audience in its own', () => {
    const saved = page();

    expect(customerPagePreview(saved, 'ar', en)[0]).toMatchObject({
      text: 'مكتب عائلي في العبدلي منذ 1998.',
      language: 'ar',
      isFallback: false,
    });
    expect(customerPagePreview(saved, 'en', en)[0]).toMatchObject({
      text: 'A family office in Abdali since 1998.',
      language: 'en',
      isFallback: false,
    });
  });

  it('shows nothing at all for a section written in neither language', () => {
    const saved = page({
      about: unwritten,
      rentalConditions: unwritten,
      visible: {
        ar: nothingVisible,
        en: nothingVisible,
      },
    });

    expect(customerPagePreview(saved, 'ar', en)).toEqual([]);
    expect(customerPagePreview(saved, 'en', en)).toEqual([]);
  });

  it('shows nothing for a hidden section, in BOTH languages, however much is written', () => {
    // Hidden beats fallback, and it has to beat it in each language separately: a section hidden by
    // its owner must not reappear in the other audience's preview because that language had text.
    const saved = page({
      hiddenSections: ALL,
      visible: { ar: nothingVisible, en: nothingVisible },
    });

    expect(customerPagePreview(saved, 'ar', en)).toEqual([]);
    expect(customerPagePreview(saved, 'en', en)).toEqual([]);
    // The text is still the office's — hiding is not clearing — which is what makes the empty
    // preview the server's answer rather than an empty record.
    expect(saved.about.ar).not.toBeNull();
    expect(saved.about.en).not.toBeNull();
  });

  it('gives the screen both audiences, in the order it draws them', () => {
    const previews = customerPagePreviews(page(), en);

    expect(previews.map((preview) => preview.language)).toEqual(['ar', 'en']);
    expect(previews[0].rows.map((row) => row.text)).toEqual([
      'مكتب عائلي في العبدلي منذ 1998.',
      'ممنوع التدخين في أي سيارة.',
    ]);
    expect(previews[1].rows.map((row) => row.text)).toEqual([
      'A family office in Abdali since 1998.',
      'No smoking in any car.',
    ]);
  });

  it('gives the screen two empty previews rather than nothing while the page is loading', () => {
    // The screen renders the blocks from this list, so a null page must still answer with both
    // audiences — otherwise "nothing written yet" and "not loaded yet" look identical.
    const previews = customerPagePreviews(null, en);

    expect(previews.map((preview) => preview.language)).toEqual(['ar', 'en']);
    expect(previews.every((preview) => preview.rows.length === 0)).toBe(true);
  });
});
