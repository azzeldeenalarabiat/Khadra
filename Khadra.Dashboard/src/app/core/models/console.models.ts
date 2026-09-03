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
  /** The file name as an admin would recognise it, never the URL. */
  readonly file: string;
  readonly meta: string;
  /** A short-lived signed link, when one exists. Absent on sample tiles. */
  readonly href?: string;
}

/** Which live count a nav item carries, if any. The number itself is never written here. */
export type NavCount = 'dealers-pending' | 'disputes-live';

export interface NavItem {
  readonly label: string;
  readonly icon: IconName;
  readonly route: string;
  /**
   * The badge names a count for the sidebar to look up in the dashboard snapshot; it never holds a
   * figure. A hard-coded "12" beside Disputes is a number an administrator will act on, and it was
   * wrong from the moment it was typed.
   */
  readonly count?: NavCount;
}

export interface NavGroup {
  readonly group?: string;
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

export interface ModalField {
  readonly label: string;
  readonly type: 'select' | 'text';
  readonly options?: readonly string[];
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
