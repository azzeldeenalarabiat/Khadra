import { TranslationKey } from '../i18n/en';
import { IconName } from '../../shared/icon/icon-paths';

/**
 * Semantic status tone. Every coloured element in the console (pill, dot, rail,
 * meter, banner) resolves its colour from one of these five, applied as a CSS
 * class that sets `--c`. Nothing carries a literal colour.
 */
export type Tone = 'ok' | 'warn' | 'bad' | 'dim' | 'accent';

export const toneClass = (tone: Tone): string => `s-${tone}`;

/** A single table cell. The variant decides how the cell renders. */
export type Cell =
  | {
      kind: 'text';
      value: string;
      sub?: string;
      align?: 'right';
      variant?: CellVariant;
      tone?: Tone;
    }
  // Money always renders with its currency code: a bare figure in a table that
  // mixes booking values, deposits and commission is ambiguous, and the backend
  // Money value object carries the currency anyway.
  | { kind: 'money'; amount: number; currency: string }
  | { kind: 'badge'; value: string; tone: Tone }
  | { kind: 'entity'; value: string; sub: string }
  | { kind: 'actions'; actions: RowAction[] };

export type CellVariant = 'mono' | 'muted' | 'dim' | 'accent' | 'clip-280' | 'clip-320';

export interface RowAction {
  readonly label: string;
  /** Maps to .btn-primary / .btn-secondary / .btn-ghost. */
  readonly style: 'primary' | 'secondary' | 'ghost';
  readonly action: string;
  /** A restricted action stays visible but disabled, and says why. */
  readonly disabledReason?: string;
}

export interface TableRow {
  /** Stable identity for tracking, and the accessible name of the row link. */
  readonly id: string;
  readonly cells: readonly Cell[];
  /** Route to open when the row is activated, if the row is navigable. */
  readonly link?: readonly string[];
}

export interface TableConfig {
  readonly columns: readonly string[];
  readonly rows: readonly TableRow[];
}

/** Everything a list screen needs. Mirrors the design's own `lists()` shape. */
export interface ListConfig extends TableConfig {
  readonly title: string;
  readonly subtitle: string;
  readonly searchHint: string;
  readonly filters: readonly string[];
  readonly count: string;
  readonly primary?: { readonly label: string; readonly action: string };
  /** Minimum table width before it scrolls sideways. */
  readonly minWidth?: string;
}

/** The five states a list can be in, exposed as a switcher in the design. */
export type ViewState = 'data' | 'loading' | 'empty' | 'error' | 'denied';

export interface KeyValue {
  readonly k: string;
  readonly v: string;
  readonly tone?: Tone;
}

export interface TimelineStep {
  readonly label: string;
  readonly meta: string;
  readonly tone: Tone;
  /** A step that has not happened yet: hollow dot, muted label. */
  readonly future?: boolean;
}

export interface DocumentTile {
  readonly label: string;
  readonly status: string;
  readonly tone: Tone;
  /**
   * The one line of identity above the button: a format ("PDF") or a stored file name. Whatever it
   * is, the SERVER said it — never something the console worked out from the document's type, which
   * is how every dealer licence came to be labelled ".jpg" regardless of what was actually on file.
   * Never the URL, which is a credential.
   */
  readonly file: string;
  readonly meta: string;
  /** A short-lived signed link. Absent when the caller has no link to offer. */
  readonly href?: string;
}

/** Which live count a nav item carries, if any. The number itself is never written here. */
export type NavCount = 'dealers-pending' | 'disputes-live' | 'notifications-unread';

/**
 * Which permission a nav item needs, if any. It NAMES the permission; it never decides one.
 *
 * Same discipline as `NavCount`: the sidebar looks the answer up in `DealerPermissions`, which comes
 * from `GET /dealers/me`. Writing "employees are shown this, owners are shown that" into the
 * structure would be a claim about a person that only the server can make — and it could not express
 * Reports at all, where an employee WITH the owner's grant must see the link and one without it
 * must not.
 */
export type NavRequirement = 'manage-staff' | 'view-reports';

export interface NavItem {
  /**
   * A translation key, not words.
   *
   * The sidebar is structure, and structure has no language. Holding the English here would mean
   * one of the two sidebars is always wrong, and the labels would be the one part of the console a
   * switch could not reach.
   */
  readonly labelKey: TranslationKey;
  readonly icon: IconName;
  readonly route: string;
  /**
   * The badge names a count for the sidebar to look up in the workload response; it never holds a
   * figure. A hard-coded "12" beside Disputes is a number an administrator will act on, and it was
   * wrong from the moment it was typed.
   */
  readonly count?: NavCount;
  /** Absent means every member of staff may open it. */
  readonly requires?: NavRequirement;
}

export interface NavGroup {
  readonly groupKey?: TranslationKey;
  readonly items: readonly NavItem[];
}

/** A confirmation dialog. Every destructive admin action goes through one. */
export interface ModalConfig {
  readonly icon: IconName;
  readonly tone: Tone;
  readonly title: string;
  readonly body: string;
  readonly note?: string;
  readonly confirm: string;
  readonly danger?: boolean;
  readonly fields?: readonly ModalField[];
  /** Toast shown after confirming. */
  readonly result: { readonly title: string; readonly body: string; readonly tone?: Tone };
}

/** One choice in a `select` field: a stable value to send, and a label to show. */
export interface ModalOption {
  readonly value: string;
  readonly label: string;
}

export interface ModalField {
  /**
   * Stable identifier for the answer. NOT the label.
   *
   * The dialog returns what was typed keyed by this, and callers read it back by the same name. It
   * used to be the label -- the visible English text -- which meant translating a caption silently
   * broke the read: `values['Odometer (km)']` on an Arabic dialog is `undefined`, and a pickup was
   * recorded with no odometer, no fuel and no cash, with no error anywhere. On the one record that
   * settles a dispute. A name is invisible, so nothing a translator does can move it.
   */
  readonly name: string;
  readonly label: string;
  /**
   * `text` is a textarea — these are reasons and notes, which run to sentences.
   *
   * `password` is a single-line masked input, and has to be its own type rather than a `text` one:
   * the textarea would put the password on screen in clear, next to whoever is standing behind the
   * person typing it.
   */
  readonly type: 'select' | 'text' | 'password';
  /**
   * Choices for a `select`. Value and label are separate for the same reason `name` exists: the
   * rejection dialog used to map the chosen LABEL back to a reason code by string comparison, so an
   * Arabic label matched nothing and every rejection was filed as 'Other'.
   */
  readonly options?: readonly ModalOption[];
  readonly placeholder?: string;
  /** Pre-filled value. A select without one starts on its first option. */
  readonly value?: string;
  readonly hint?: string;
  /**
   * Fields are required by default: every one of these is a reason or a note attached to a decision
   * that both parties and the audit log will read, and an unexplained decision is the thing the
   * dialog exists to prevent.
   */
  readonly optional?: boolean;
}

export interface Toast {
  readonly title: string;
  readonly body: string;
  readonly tone: Tone;
}
