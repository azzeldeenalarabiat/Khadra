import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import {
  CustomerPageText,
  CustomerPageView,
  UpdateCustomerPageRequest,
} from '../../core/models/dealer-console.api';

/** Passed in rather than injected: these are pure functions, and their spec calls them directly. */
export type Translate = (key: TranslationKey, params?: MessageParams) => string;

/** The draft an owner is typing, keyed by the wire name of each section. */
export type CustomerPageDraft = Readonly<Record<string, string>>;

/** One editable section, ready to render. */
export interface CustomerPageRow {
  /** The section's name as the API stores and sends it. PascalCase, and a contract. */
  readonly name: string;
  /** The field its text travels in. camelCase, and the same contract. */
  readonly field: keyof CustomerPageText;
  readonly label: string;
  readonly hint: string;
  readonly placeholder: string;
  /**
   * Whether this section is written but not shown for a reason that is not hiding.
   *
   * Delivery notes while delivery is off: the public page leaves them out, and letting somebody type
   * into a box whose text nobody will read, with no explanation, is worse than saying so.
   */
  readonly muted: boolean;
}

/** What the console knows how to render for one section of the page. */
interface SectionUi {
  readonly field: keyof CustomerPageText;
  readonly label: TranslationKey;
  readonly hint: TranslationKey;
  readonly placeholder: TranslationKey;
}

/**
 * The sections this build can edit, by the names the API uses.
 *
 * NOT the list of sections — that is the server's, and this only says which of them this console has
 * a box and a label for. The names are a contract: they are stored in `dealers.hidden_profile_sections`
 * and sent on the wire, so one renamed here silently un-hides whatever an office had hidden under the
 * old one.
 */
const SECTIONS: Readonly<Record<string, SectionUi>> = {
  About: {
    field: 'about',
    label: 'dealerCustomerPage.about',
    hint: 'dealerCustomerPage.aboutHint',
    placeholder: 'dealerCustomerPage.aboutPlaceholder',
  },
  RentalConditions: {
    field: 'rentalConditions',
    label: 'dealerCustomerPage.rentalConditions',
    hint: 'dealerCustomerPage.rentalConditionsHint',
    placeholder: 'dealerCustomerPage.rentalConditionsPlaceholder',
  },
  Insurance: {
    field: 'insurance',
    label: 'dealerCustomerPage.insurance',
    hint: 'dealerCustomerPage.insuranceHint',
    placeholder: 'dealerCustomerPage.insurancePlaceholder',
  },
  PickupInstructions: {
    field: 'pickupInstructions',
    label: 'dealerCustomerPage.pickupInstructions',
    hint: 'dealerCustomerPage.pickupInstructionsHint',
    placeholder: 'dealerCustomerPage.pickupInstructionsPlaceholder',
  },
  DeliveryNotes: {
    field: 'deliveryNotes',
    label: 'dealerCustomerPage.deliveryNotes',
    hint: 'dealerCustomerPage.deliveryNotesHint',
    placeholder: 'dealerCustomerPage.deliveryNotesPlaceholder',
  },
  CustomerNotes: {
    field: 'customerNotes',
    label: 'dealerCustomerPage.customerNotes',
    hint: 'dealerCustomerPage.customerNotesHint',
    placeholder: 'dealerCustomerPage.customerNotesPlaceholder',
  },
};

/**
 * The editor's rows, in the SERVER's order and from the server's own list.
 *
 * The list is the point. A console that wrote its own six would offer to hide whatever it happened
 * to believe was hideable, and the opening hours a pickup is held to, the delivery terms, the
 * address, the rating and the cars are not an office's to take off its page. They are not on the
 * server's list, so no switch for them can be rendered.
 *
 * A name this build has no box for is left out rather than guessed at: the release that teaches the
 * console a new section is the release that gives it a label and a field to write into, and a row
 * with neither would be a control that cannot save. Leaving it out is only safe because the page
 * also refuses to SAVE while one is present — see `unknownCustomerPageSections`.
 */
export function customerPageRows(page: CustomerPageView, t: Translate): readonly CustomerPageRow[] {
  return partitionSections(page).known.map(({ name, ui }) => ({
    name,
    field: ui.field,
    label: t(ui.label),
    hint: t(ui.hint),
    placeholder: t(ui.placeholder),
    muted: ui.field === 'deliveryNotes' && !page.deliveryEnabled,
  }));
}

/**
 * The sections the server has that this build has no box for — a console older than its server.
 *
 * A save replaces the whole page, and a section with no box has nothing to send, so saving from here
 * would erase what an office wrote in it. The page must not save while this is non-empty (owner,
 * 2026-09-19: "an older console must never silently erase data belonging to a newer contract").
 *
 * Exactly the names `customerPageRows` leaves out, because both come from one partition: a name
 * cannot be dropped from the form without being reported here.
 */
export function unknownCustomerPageSections(page: CustomerPageView): readonly string[] {
  return partitionSections(page).unknown;
}

/** The server's sections, split into those this build can render and those it cannot. */
function partitionSections(page: CustomerPageView): {
  readonly known: readonly { readonly name: string; readonly ui: SectionUi }[];
  readonly unknown: readonly string[];
} {
  const known: { name: string; ui: SectionUi }[] = [];
  const unknown: string[] = [];
  for (const name of page.sections) {
    const ui = sectionUi(name);
    if (ui) known.push({ name, ui });
    else unknown.push(name);
  }
  return { known, unknown };
}

/**
 * The console's box for a section, by its own name only.
 *
 * `SECTIONS[name]` on a plain object literal also answers for names it inherits: `constructor` or
 * `toString` would come back as a function, be taken for a known section, and render a row with no
 * field. Our server cannot send those, but the check that decides whether saving is safe must not
 * rest on that.
 */
function sectionUi(name: string): SectionUi | undefined {
  return Object.hasOwn(SECTIONS, name) ? SECTIONS[name] : undefined;
}

/** The draft as the server answered it, so the form is a draft of THAT answer. */
export function customerPageDraft(page: CustomerPageView): CustomerPageDraft {
  const draft: Record<string, string> = {};
  for (const row of customerPageRows(page, (key) => key)) {
    draft[row.field] = sectionText(page, row.field) ?? '';
  }
  return draft;
}

/**
 * The body of a save.
 *
 * Every section every time, including the empty ones: the API replaces rather than patches, which is
 * what stops text an owner has cleared from standing on a customer's screen. An empty box is null —
 * nothing written — which is a different answer from an empty string.
 */
export function customerPageRequest(
  draft: CustomerPageDraft,
  hidden: ReadonlySet<string>,
): UpdateCustomerPageRequest {
  const written = (field: string): string | null => {
    const value = (draft[field] ?? '').trim();
    return value === '' ? null : value;
  };

  return {
    about: written('about'),
    rentalConditions: written('rentalConditions'),
    insurance: written('insurance'),
    pickupInstructions: written('pickupInstructions'),
    deliveryNotes: written('deliveryNotes'),
    customerNotes: written('customerNotes'),
    hiddenSections: [...hidden],
  };
}

/** Whether the draft says anything the server's copy does not. */
export function customerPageDirty(
  page: CustomerPageView,
  draft: CustomerPageDraft,
  hidden: ReadonlySet<string>,
): boolean {
  const changed = customerPageRows(page, (key) => key).some(
    (row) => (draft[row.field] ?? '').trim() !== (sectionText(page, row.field) ?? ''),
  );
  if (changed) return true;

  return (
    page.hiddenSections.length !== hidden.size ||
    page.hiddenSections.some((name) => !hidden.has(name))
  );
}

/**
 * What a customer actually sees, as the SERVER says they see it.
 *
 * Never worked out here from "hidden or empty". The rule lives in one place on the server and the
 * public page runs it; a second copy in a browser would agree with it right up until one of them
 * changed, and the one that would be wrong is the preview an owner trusts.
 */
export function customerPagePreview(
  page: CustomerPageView,
  t: Translate,
): readonly { label: string; text: string }[] {
  return customerPageRows(page, t)
    .map((row) => ({ label: row.label, text: sectionText(page.visible, row.field) }))
    .filter((row): row is { label: string; text: string } => !!row.text);
}

/** One section's text out of a payload, by its wire name. */
export function sectionText(source: CustomerPageText, field: string): string | null {
  return (source as unknown as Record<string, string | null>)[field] ?? null;
}
