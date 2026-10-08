import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN } from '../../core/i18n/en';
import { resolveMessage } from '../../core/i18n/resolve';
import {
  CustomerPageView,
  LocalizedText,
  VisibleCustomerPage,
} from '../../core/models/dealer-console.api';
import { Translate } from '../dealer/customer-page.presenter';
import { customerPagePanel } from './customer-page-panel.presenter';

/**
 * Pre-launch item 113: an administrator reads what an office tells its customers. Everything the office wrote,
 * hidden sections included, each marked with where it stands — read from the same rows the office's own editor uses.
 */
const en: Translate = (key, params) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
const ar: Translate = (key, params) =>
  resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key;

const unwritten: LocalizedText = { ar: null, en: null };
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
    about: { ar: 'مكتب عائلي في العبدلي.', en: null },
    rentalConditions: { ar: 'ممنوع التدخين.', en: 'No smoking.' },
    insurance: { ar: null, en: 'Full cover.' },
    pickupInstructions: unwritten,
    deliveryNotes: { ar: null, en: 'We deliver to hotels.' },
    customerNotes: unwritten,
    hiddenSections: ['Insurance'],
    sections: [
      'About',
      'RentalConditions',
      'Insurance',
      'PickupInstructions',
      'DeliveryNotes',
      'CustomerNotes',
    ],
    maxTextLength: 2000,
    deliveryEnabled: false,
    visible: { ar: nothingVisible, en: nothingVisible },
    ...over,
  };
}

describe('the office customer page, as an administrator reads it', () => {
  it('lists every section the server sent, in its order, with the office’s raw words per language', () => {
    const panel = customerPagePanel(page(), en);

    expect(panel.rows.map((row) => row.name)).toEqual([
      'About',
      'RentalConditions',
      'Insurance',
      'PickupInstructions',
      'DeliveryNotes',
      'CustomerNotes',
    ]);
    // No fallback: the About was written in Arabic only, and that is what the administrator must see.
    expect(panel.rows[0].written).toEqual([{ language: 'ar', text: 'مكتب عائلي في العبدلي.' }]);
    expect(panel.rows[1].written.map((written) => written.language)).toEqual(['ar', 'en']);
  });

  it('shows a hidden section’s words and says it is hidden', () => {
    const insurance = customerPagePanel(page(), en).rows.find((row) => row.name === 'Insurance')!;

    expect(insurance.state).toBe('hidden');
    expect(insurance.stateLabel).toBe('Hidden from customers');
    expect(insurance.written).toEqual([{ language: 'en', text: 'Full cover.' }]);
  });

  it('marks delivery notes written while delivery is off, and empty sections, as not shown', () => {
    const rows = customerPagePanel(page(), en).rows;

    expect(rows.find((row) => row.name === 'DeliveryNotes')!.state).toBe('deliveryOff');
    expect(rows.find((row) => row.name === 'PickupInstructions')!.state).toBe('empty');
    expect(rows.find((row) => row.name === 'RentalConditions')!.state).toBe('shown');
    expect(
      customerPagePanel(page({ deliveryEnabled: true }), en).rows.find(
        (row) => row.name === 'DeliveryNotes',
      )!.state,
    ).toBe('shown');
  });

  it('counts a section this console has no label for rather than guessing at it', () => {
    const panel = customerPagePanel(page({ sections: ['About', 'LoyaltyScheme'] }), en);

    expect(panel.rows.map((row) => row.name)).toEqual(['About']);
    expect(panel.unknownCount).toBe(1);
  });

  it('is worded in Arabic too', () => {
    const rows = customerPagePanel(page(), ar).rows;

    expect(rows.find((row) => row.name === 'Insurance')!.stateLabel).toBe(
      AR['dealerReview.customerPage.hidden'],
    );
    expect(rows[0].label).toBe(AR['dealerCustomerPage.about']);
  });
});
