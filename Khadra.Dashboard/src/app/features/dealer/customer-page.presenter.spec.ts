import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN } from '../../core/i18n/en';
import { resolveMessage } from '../../core/i18n/resolve';
import { CustomerPageView } from '../../core/models/dealer-console.api';
import {
  Translate,
  customerPageDirty,
  customerPageDraft,
  customerPagePreview,
  customerPageRequest,
  customerPageRows,
  unknownCustomerPageSections,
} from './customer-page.presenter';

/**
 * The customer page editor (spec 4.1).
 *
 * Two rules carry this screen and both are the server's, deliberately. WHICH sections an office may
 * hide is the server's list — so a console cannot offer to take the opening hours, the delivery
 * terms, the address, the rating or the cars off a page, because those are not on it. And what a
 * customer SEES is the server's answer, from the code the public page runs — so an owner's preview
 * cannot drift from their page.
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

function page(over: Partial<CustomerPageView> = {}): CustomerPageView {
  return {
    about: 'A family office in Abdali since 1998.',
    rentalConditions: 'No smoking in any car.',
    insurance: null,
    pickupInstructions: null,
    deliveryNotes: null,
    customerNotes: null,
    hiddenSections: [],
    sections: ALL,
    maxTextLength: 2000,
    deliveryEnabled: true,
    visible: {
      about: 'A family office in Abdali since 1998.',
      rentalConditions: 'No smoking in any car.',
      insurance: null,
      pickupInstructions: null,
      deliveryNotes: null,
      customerNotes: null,
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
    const shown = customerPageRows(page({ sections }), en).map((row) => row.name);
    const unknown = unknownCustomerPageSections(page({ sections }));

    expect(shown).toEqual(['About', 'Insurance']);
    expect(unknown).toEqual(['LoyaltyScheme', 'Accessibility']);
    expect([...shown, ...unknown].sort()).toEqual([...sections].sort());
  });

  it('does not take a name an object inherits for a section it knows', () => {
    // `constructor` and `toString` answer on any plain object. Read as known, they would render a
    // row with no field and — worse — leave the refusal to save with nothing to report.
    const sections = ['About', 'constructor', 'toString'];

    expect(customerPageRows(page({ sections }), en).map((row) => row.name)).toEqual(['About']);
    expect(unknownCustomerPageSections(page({ sections }))).toEqual(['constructor', 'toString']);
    expect(Object.keys(customerPageDraft(page({ sections })))).toEqual(['about']);
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

  it('sends every section on a save, so one cleared is one cleared', () => {
    // A patch would leave text on a customer's screen that its owner can no longer see.
    const request = customerPageRequest({ about: 'Still here.', insurance: '   ' }, new Set());

    expect(request).toEqual({
      about: 'Still here.',
      rentalConditions: null,
      insurance: null,
      pickupInstructions: null,
      deliveryNotes: null,
      customerNotes: null,
      hiddenSections: [],
    });
  });

  it('sends the switches that are off as the hidden set', () => {
    const request = customerPageRequest(
      { about: 'Written.' },
      new Set(['About', 'CustomerNotes']),
    );

    expect([...request.hiddenSections].sort()).toEqual(['About', 'CustomerNotes']);
    // Hiding does not clear: the text is still saved, and switching it back on shows it again.
    expect(request.about).toBe('Written.');
  });

  it('knows an untouched form from an edited one', () => {
    const saved = page();
    const draft = customerPageDraft(saved);

    expect(customerPageDirty(saved, draft, new Set(saved.hiddenSections))).toBe(false);
    expect(customerPageDirty(saved, { ...draft, insurance: 'Comprehensive.' }, new Set())).toBe(
      true,
    );
    // A switch flipped is a change, even with every box untouched.
    expect(customerPageDirty(saved, draft, new Set(['About']))).toBe(true);
  });

  it('does not call trailing whitespace a change', () => {
    const saved = page();
    const draft = { ...customerPageDraft(saved), about: `${saved.about}   ` };

    expect(customerPageDirty(saved, draft, new Set())).toBe(false);
  });

  it('previews what the SERVER says a customer sees, not what is typed', () => {
    // The office has written insurance and hidden its About. The server has already applied both,
    // and the preview repeats its answer rather than working the rule out a second time.
    const saved = page({
      about: 'A family office in Abdali since 1998.',
      insurance: 'Comprehensive cover.',
      hiddenSections: ['About'],
      visible: {
        about: null,
        rentalConditions: 'No smoking in any car.',
        insurance: 'Comprehensive cover.',
        pickupInstructions: null,
        deliveryNotes: null,
        customerNotes: null,
      },
    });

    const preview = customerPagePreview(saved, en);

    expect(preview.map((row) => row.text)).toEqual([
      'No smoking in any car.',
      'Comprehensive cover.',
    ]);
    expect(preview.map((row) => row.label)).toEqual([
      EN['dealerCustomerPage.rentalConditions'],
      EN['dealerCustomerPage.insurance'],
    ]);
  });

  it('shows nothing at all for a page whose sections are all hidden or empty', () => {
    const saved = page({
      hiddenSections: ALL,
      visible: {
        about: null,
        rentalConditions: null,
        insurance: null,
        pickupInstructions: null,
        deliveryNotes: null,
        customerNotes: null,
      },
    });

    expect(customerPagePreview(saved, en)).toEqual([]);
  });
});
