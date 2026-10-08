import { WrittenText, writtenIn } from '../../core/i18n/bilingual-content';
import { CustomerPageView } from '../../core/models/dealer-console.api';
import { Tone } from '../../core/models/console.models';
import {
  Translate,
  customerPageRows,
  unknownCustomerPageSections,
} from '../dealer/customer-page.presenter';

/** Where one section stands with customers, as the server's own projection says. */
export type CustomerPageSectionState = 'shown' | 'hidden' | 'deliveryOff' | 'empty';

export interface CustomerPagePanelRow {
  /** The section's wire name. */
  readonly name: string;
  readonly label: string;
  /** What the office wrote, per language, raw: an administrator must see which language is missing. */
  readonly written: readonly WrittenText[];
  readonly state: CustomerPageSectionState;
  readonly stateLabel: string;
  readonly tone: Tone;
}

export interface CustomerPagePanel {
  readonly rows: readonly CustomerPagePanelRow[];
  /** Sections the server has that this console has no label for: counted, never guessed at. */
  readonly unknownCount: number;
}

/**
 * What an office tells its customers, for an administrator to READ (pre-launch item 113).
 *
 * The rows are the office editor's own — the same server list, the same labels, the same "delivery is off" rule — so
 * the administrator and the owner cannot be shown two different pages. Hidden sections are shown here too, marked:
 * the point of the panel is to read everything the office has written, including what it has taken off the page.
 */
export function customerPagePanel(page: CustomerPageView, t: Translate): CustomerPagePanel {
  const hidden = new Set(page.hiddenSections);
  const rows = customerPageRows(page, t).map((row) => {
    const written = writtenIn(page[row.field]);
    const state: CustomerPageSectionState = hidden.has(row.name)
      ? 'hidden'
      : written.length === 0
        ? 'empty'
        : row.muted
          ? 'deliveryOff'
          : 'shown';
    return {
      name: row.name,
      label: row.label,
      written,
      state,
      stateLabel: t(STATE_LABELS[state]),
      tone: toneOf(state),
    };
  });
  return { rows, unknownCount: unknownCustomerPageSections(page).length };
}

const STATE_LABELS = {
  shown: 'dealerReview.customerPage.shown',
  hidden: 'dealerReview.customerPage.hidden',
  deliveryOff: 'dealerReview.customerPage.deliveryOff',
  empty: 'dealerReview.customerPage.empty',
} as const;

/** Shown is fine, hidden is worth a look, and the two kinds of "not shown" are quiet. */
function toneOf(state: CustomerPageSectionState): Tone {
  if (state === 'shown') return 'ok';
  return state === 'hidden' ? 'warn' : 'dim';
}
