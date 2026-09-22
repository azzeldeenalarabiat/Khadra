import { TranslationKey } from '../../core/i18n/en';
import { Language, MessageParams } from '../../core/i18n/language';
import { CONTENT_LANGUAGES, boxKey } from '../../core/i18n/bilingual-content';
import {
  CustomerPageText,
  CustomerPageView,
  LocalizedText,
  ResolvedText,
  UpdateCustomerPageRequest,
  VisibleCustomerPage,
} from '../../core/models/dealer-console.api';

/** Re-exported so a screen editing this content has ONE import site for all of it. */
export { CONTENT_LANGUAGES, boxKey };

/** Passed in rather than injected: these are pure functions, and their spec calls them directly. */
export type Translate = (key: TranslationKey, params?: MessageParams) => string;

/** The draft an owner is typing, keyed by the wire name of each section. */
export type CustomerPageDraft = Readonly<Record<string, string>>;

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
    for (const language of CONTENT_LANGUAGES) {
      // The office's own words, NOT resolved. An office that has written only Arabic must find an
      // empty English box, because that empty box is the work still to do — the server sends the
      // raw pair for exactly this reason and the preview below is where the fallback shows up.
      draft[boxKey(row.field, language)] = sectionText(page, row.field, language) ?? '';
    }
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
  const box = (field: string, language: Language): string | null => {
    const value = (draft[boxKey(field, language)] ?? '').trim();
    // Null, not an empty string: "nothing written" and "written as nothing" are different answers,
    // and the server treats them the same way only because this sends null.
    return value === '' ? null : value;
  };

  const section = (field: string): LocalizedText => ({
    ar: box(field, 'ar'),
    en: box(field, 'en'),
  });

  return {
    about: section('about'),
    rentalConditions: section('rentalConditions'),
    insurance: section('insurance'),
    pickupInstructions: section('pickupInstructions'),
    deliveryNotes: section('deliveryNotes'),
    customerNotes: section('customerNotes'),
    hiddenSections: [...hidden],
  };
}

/** Whether the draft says anything the server's copy does not. */
export function customerPageDirty(
  page: CustomerPageView,
  draft: CustomerPageDraft,
  hidden: ReadonlySet<string>,
): boolean {
  const changed = customerPageRows(page, (key) => key).some((row) =>
    CONTENT_LANGUAGES.some(
      (language) =>
        (draft[boxKey(row.field, language)] ?? '').trim() !==
        (sectionText(page, row.field, language) ?? ''),
    ),
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
/**
 * One preview per audience, so an owner sees BOTH and neither depends on their own browser.
 *
 * The server sends two, resolved by its own rule, and the BFF forwards whatever `Accept-Language`
 * the owner's machine happens to send — so a single preview would have flipped between Arabic and
 * English depending on which laptop they signed in from, silently, with no way to see the other one.
 *
 * Each row carries the language the text ACTUALLY came back in, which is how a fallback shows up
 * here: an Arabic preview row tagged `en` is an office that has not written that section in Arabic
 * yet, and the row says so rather than looking finished.
 */
export function customerPagePreview(
  page: CustomerPageView,
  language: Language,
  t: Translate,
): readonly CustomerPagePreviewRow[] {
  const shown = language === 'ar' ? page.visible.ar : page.visible.en;

  return customerPageRows(page, t)
    .map((row) => ({ label: row.label, shown: resolvedText(shown, row.field) }))
    .filter((row) => !!row.shown)
    .map((row) => ({
      label: row.label,
      text: row.shown!.text,
      language: row.shown!.language as Language,
      isFallback: row.shown!.language !== language,
    }));
}

export interface CustomerPagePreviewRow {
  readonly label: string;
  readonly text: string;
  /** The language the text is in, which is not always the one being previewed. */
  readonly language: Language;
  /** True when this is the other language standing in, so the row can say so. */
  readonly isFallback: boolean;
}

/**
 * One preview block: the audience it is for, and what that audience sees.
 *
 * Named here rather than written inline in the template, because a template's array literal infers
 * `language` as `string` and the language is the one thing every row of the block is keyed by.
 */
export interface CustomerPageAudiencePreview {
  readonly language: Language;
  readonly rows: readonly CustomerPagePreviewRow[];
}

/** Both audiences, in the order the console draws them. */
export function customerPagePreviews(
  page: CustomerPageView | null,
  t: Translate,
): readonly CustomerPageAudiencePreview[] {
  return CONTENT_LANGUAGES.map((language) => ({
    language,
    rows: page ? customerPagePreview(page, language, t) : [],
  }));
}

/** One language of one section out of the owner's own copy, by wire name. */
export function sectionText(
  source: CustomerPageText,
  field: string,
  language: Language,
): string | null {
  const pair = (source as unknown as Record<string, LocalizedText | undefined>)[field];
  return (language === 'ar' ? pair?.ar : pair?.en) ?? null;
}

/** One section out of a resolved preview, by wire name. */
function resolvedText(source: VisibleCustomerPage, field: string): ResolvedText | null {
  return (source as unknown as Record<string, ResolvedText | null>)[field] ?? null;
}
