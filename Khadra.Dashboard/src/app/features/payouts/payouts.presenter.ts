import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { issueLines } from '../../core/i18n/issue-words';
import { EnumFamily } from '../../core/i18n/status-key';
import { Tone } from '../../core/models/console.models';
import { FinancialOffice } from '../../core/models/financials.api';
import { Money } from '../../core/models/fleet.api';
import {
  FinanceTotals,
  OfficeBalance,
  OfficePayable,
  OfficeSettlement,
  OfficeSettlementLine,
  PayableHold,
  PayableLine,
} from '../../core/models/payables.api';

type Translate = (key: TranslationKey, params?: MessageParams) => string;
/** `I18nService.enumLabel`: a server enum in the reader's language, spelled out when this build has no word. */
type EnumLabel = (family: EnumFamily, name: string | null | undefined) => string;

/**
 * The office payables ledger in the consoles' words (payments Phase 8). Every figure is the server's; these only
 * choose a sentence and a tone. A net's SIGN is the server's answer to who owes whom, and the words say it — the
 * minus is never left for a reader to interpret.
 */
export interface PayoutWords {
  readonly t: Translate;
  readonly label: EnumLabel;
}

export interface PayoutFormat {
  money(value: Money): string;
  dateTime(iso: string): string;
  /** A calendar day, `yyyy-MM-dd`, as the console prints dates. */
  day(isoDay: string): string;
}

/** Whose side a sentence is written from: an administrator reading about an office, or the office itself. */
export type Audience = 'admin' | 'office';

const positive = (money: Money): Money => ({ amount: Math.abs(money.amount), currency: money.currency });

/** Who owes whom, and how much: the sign as words. */
export function netText(net: Money, audience: Audience, words: PayoutWords, format: PayoutFormat): { readonly text: string; readonly tone: Tone } {
  const amount = format.money(positive(net));
  if (net.amount > 0) return { text: words.t(audience === 'admin' ? 'payouts.net.toOffice' : 'payouts.net.toYou', { amount }), tone: 'ok' };
  if (net.amount < 0) return { text: words.t(audience === 'admin' ? 'payouts.net.byOffice' : 'payouts.net.byYou', { amount }), tone: 'warn' };
  return { text: words.t('payouts.net.even'), tone: 'dim' };
}

/**
 * A net a settlement has closed, in the past tense: the money has moved, so nobody "owes" it any more. What a settled
 * payable and a settlement's lines say.
 */
export function settledText(net: Money, audience: Audience, words: PayoutWords, format: PayoutFormat): { readonly text: string; readonly tone: Tone } {
  const amount = format.money(positive(net));
  if (net.amount > 0) return { text: words.t(audience === 'admin' ? 'payouts.net.owedToOffice' : 'payouts.net.owedToYou', { amount }), tone: 'ok' };
  if (net.amount < 0) return { text: words.t(audience === 'admin' ? 'payouts.net.owedByOffice' : 'payouts.net.owedByYou', { amount }), tone: 'warn' };
  return { text: words.t('payouts.net.even'), tone: 'dim' };
}

/**
 * What a settlement moved: the positive figure — its direction ("Paid to the office", "Received from the office") says
 * which way — or nothing, when it netted to zero.
 */
export function movedText(amount: Money, words: PayoutWords, format: PayoutFormat): { readonly text: string; readonly tone: Tone } {
  if (amount.amount === 0) return { text: words.t('payouts.net.even'), tone: 'dim' };
  return { text: format.money(positive(amount)), tone: amount.amount > 0 ? 'ok' : 'warn' };
}

/** The money that went towards the office carries +; what is taken from it carries −. */
const TOWARDS_OFFICE: ReadonlySet<string> = new Set(['RentalRevenue', 'DisputeShare', 'PenaltyKept']);

export interface PayableLineRow {
  readonly label: string;
  readonly amount: string;
  readonly towardsOffice: boolean;
}

/**
 * One line, its amount as the positive figure the server sent. The LABEL carries the subtraction — "Less Khadra's
 * commission" — never a sign typed before an amount, which prints "−0" and splits from its figure in Arabic.
 */
export function lineRow(line: PayableLine, words: PayoutWords, format: PayoutFormat): PayableLineRow {
  const towardsOffice = TOWARDS_OFFICE.has(line.kind);
  const kind = words.label('payableLineKind', line.kind);
  return {
    label: towardsOffice ? kind : words.t('payouts.line.less', { line: kind }),
    amount: format.money(line.amount),
    towardsOffice,
  };
}

const STATE_TONES: Readonly<Record<string, Tone>> = {
  Due: 'accent',
  NothingDue: 'dim',
  Blocked: 'warn',
  OnHold: 'warn',
  Settled: 'ok',
  AwaitingRecord: 'dim',
  Open: 'dim',
  NotApplicable: 'dim',
};

export const stateTone = (state: string): Tone => STATE_TONES[state] ?? 'dim';

// ── Balances ────────────────────────────────────────────────────────────────────────────────────

export interface BalanceRow {
  readonly key: string;
  readonly dealerId: string;
  readonly office: string;
  readonly currency: string;
  readonly isTest: boolean;
  readonly provider: string | null;
  readonly due: string;
  readonly dueTone: Tone;
  /** The balance due as a number, sent back when it is settled: the figure the administrator was shown. */
  readonly dueAmount: number;
  readonly dueCount: number;
  readonly notYetDue: string | null;
  readonly lastSettlement: string | null;
  readonly lastSettlementId: string | null;
  readonly canSettle: boolean;
}

export function balanceRow(balance: OfficeBalance, audience: Audience, words: PayoutWords, format: PayoutFormat): BalanceRow {
  const due = netText(balance.due, audience, words, format);
  const last = balance.lastSettlement;
  return {
    key: `${balance.dealerId}:${balance.currency}:${balance.provider ?? ''}`,
    dealerId: balance.dealerId,
    office: balance.dealerName,
    currency: balance.currency,
    isTest: balance.isTest === true,
    provider: balance.provider,
    due: balance.dueCount === 0 ? words.t('payouts.nothingDue') : due.text,
    dueTone: balance.dueCount === 0 ? 'dim' : due.tone,
    dueAmount: balance.due.amount,
    dueCount: balance.dueCount,
    notYetDue: balance.notYetDueCount === 0
      ? null
      : words.t('payouts.notYetDue', { count: balance.notYetDueCount, amount: netText(balance.notYetDue, audience, words, format).text }),
    lastSettlement: last
      ? words.t('payouts.lastSettlement', {
          number: last.number,
          direction: words.label('settlementDirection', last.direction),
          day: format.day(last.paidOn),
        })
      : null,
    lastSettlementId: last?.settlementId ?? null,
    // Something is due, one way or the other, or the payables cancel out: all three close by one settlement.
    canSettle: balance.dueCount > 0 && balance.provider !== null,
  };
}

// ── Payables ────────────────────────────────────────────────────────────────────────────────────

/** What a hold says beyond its reason, and how a screen shows it (pre-launch item 219). */
export interface HoldDetail {
  /**
   * `worded`: in this console's words — the issues a booking's records need reviewing for. `server`: as the server
   * composed it, English and figures, shown left to right. `typed`: as the administrator who held it typed it.
   */
  readonly kind: 'worded' | 'server' | 'typed';
  readonly lines: readonly string[];
}

export interface HoldRow {
  readonly id: string;
  readonly reason: string;
  readonly detail: HoldDetail | null;
  readonly opened: string;
  readonly by: string | null;
  readonly manual: boolean;
  readonly bookingId: string;
  readonly reference: string | null;
}

export function holdRow(hold: PayableHold, words: PayoutWords, format: PayoutFormat): HoldRow {
  return {
    id: hold.holdId,
    reason: words.label('payableHoldReason', hold.reason),
    detail: holdDetail(hold, words),
    opened: format.dateTime(hold.openedAt),
    by: hold.openedBy,
    manual: hold.reason === 'Manual',
    bookingId: hold.bookingId,
    reference: hold.bookingReference,
  };
}

/**
 * A hold's detail. The pass holds a booking whose records need review with the calculator's issue codes, which this
 * console words, one per line; an administrator's reason is shown as typed; anything else the server composed — what
 * a payable no longer matches, a penalty that is not the whole deposit — is shown as it is.
 */
function holdDetail(hold: PayableHold, words: PayoutWords): HoldDetail | null {
  const detail = hold.detail?.trim();
  if (!detail) return null;
  if (hold.reason === 'Manual') return { kind: 'typed', lines: [detail] };
  if (hold.reason === 'NeedsReview') return { kind: 'worded', lines: issueLines(detail, words.label) };
  return { kind: 'server', lines: [detail] };
}

export interface PayableRow {
  readonly id: string;
  readonly bookingId: string;
  readonly reference: string;
  readonly office: string;
  readonly dealerId: string;
  readonly outcome: string;
  readonly state: string;
  readonly stateTone: Tone;
  readonly officeMoney: string;
  readonly commission: string;
  readonly charges: string | null;
  readonly net: string;
  readonly netTone: Tone;
  readonly final: string;
  readonly lines: readonly PayableLineRow[];
  readonly settlement: { readonly id: string; readonly number: string; readonly day: string } | null;
  readonly holds: readonly HoldRow[];
  readonly blocks: readonly string[];
  readonly isTest: boolean;
  /** An administrator may hold an open payable nobody else holds, and release the one they held. */
  readonly canHold: boolean;
  readonly canRelease: boolean;
}

export function payableRow(payable: OfficePayable, audience: Audience, words: PayoutWords, format: PayoutFormat): PayableRow {
  const net = payable.state === 'Settled'
    ? settledText(payable.net, audience, words, format)
    : netText(payable.net, audience, words, format);
  const holds = (payable.holds ?? []).map((hold) => holdRow(hold, words, format));
  const open = payable.state !== 'Settled';
  return {
    id: payable.payableId,
    bookingId: payable.bookingId,
    reference: payable.bookingReference,
    office: payable.dealerName,
    dealerId: payable.dealerId,
    outcome: words.label('payableOutcome', payable.outcome),
    state: words.label('payableState', payable.state),
    stateTone: stateTone(payable.state),
    officeMoney: format.money(payable.officeMoney),
    commission: format.money(payable.commission),
    charges: payable.officeCharges.amount > 0 ? format.money(payable.officeCharges) : null,
    net: net.text,
    netTone: net.tone,
    final: format.dateTime(payable.finalAt),
    lines: payable.lines.map((line) => lineRow(line, words, format)),
    settlement: payable.settlement
      ? { id: payable.settlement.settlementId, number: payable.settlement.number, day: format.day(payable.settlement.paidOn) }
      : null,
    holds,
    blocks: (payable.blocks ?? []).map((block) => words.label('payableBlock', block.kind)),
    isTest: payable.isTest === true,
    canHold: audience === 'admin' && open && !holds.some((hold) => hold.manual),
    canRelease: audience === 'admin' && open && holds.some((hold) => hold.manual),
  };
}

// ── Settlements ─────────────────────────────────────────────────────────────────────────────────

export interface SettlementRow {
  readonly id: string;
  readonly number: string;
  readonly office: string;
  readonly dealerId: string;
  readonly direction: string;
  readonly amount: string;
  readonly amountTone: Tone;
  readonly day: string;
  readonly reference: string | null;
  readonly note: string | null;
  readonly recorded: string;
  readonly recordedBy: string | null;
  readonly count: number;
  readonly isTest: boolean;
  readonly voided: { readonly when: string; readonly by: string | null; readonly reason: string | null } | null;
}

export function settlementRow(settlement: OfficeSettlement, audience: Audience, words: PayoutWords, format: PayoutFormat): SettlementRow {
  const amount = netText(settlement.amount, audience, words, format);
  return {
    id: settlement.settlementId,
    number: settlement.number,
    office: settlement.dealerName,
    dealerId: settlement.dealerId,
    direction: words.label('settlementDirection', settlement.direction),
    amount: format.money(positive(settlement.amount)),
    amountTone: settlement.void ? 'dim' : amount.tone,
    day: format.day(settlement.paidOn),
    reference: settlement.reference,
    note: settlement.note,
    recorded: format.dateTime(settlement.recordedAt),
    recordedBy: settlement.recordedBy,
    count: settlement.payableCount,
    isTest: settlement.isTest === true,
    voided: settlement.void
      ? { when: format.dateTime(settlement.void.voidedAt), by: settlement.void.voidedBy, reason: settlement.void.reason }
      : null,
  };
}

export interface SettlementLineRow {
  readonly payableId: string;
  readonly bookingId: string;
  readonly reference: string;
  readonly outcome: string;
  readonly net: string;
  readonly netTone: Tone;
}

export function settlementLineRow(line: OfficeSettlementLine, audience: Audience, words: PayoutWords, format: PayoutFormat): SettlementLineRow {
  const net = settledText(line.net, audience, words, format);
  return {
    payableId: line.payableId,
    bookingId: line.bookingId,
    reference: line.bookingReference,
    outcome: words.label('payableOutcome', line.outcome),
    net: net.text,
    netTone: net.tone,
  };
}

// ── Finance ─────────────────────────────────────────────────────────────────────────────────────

export interface FinanceCard {
  readonly label: string;
  readonly value: string;
  readonly hint: string | null;
}

export interface FinanceGroup {
  readonly key: string;
  readonly title: string;
  readonly isTest: boolean;
  readonly cards: readonly FinanceCard[];
}

/** Khadra's own money in one currency and kind of money: six cards, the design's Finance KPIs, all the server's sums. */
export function financeGroup(totals: FinanceTotals, words: PayoutWords, format: PayoutFormat): FinanceGroup {
  const { t } = words;
  return {
    key: `${totals.currency}:${totals.isTest}`,
    title: t(totals.isTest ? 'finance.groupTest' : 'finance.group', { currency: totals.currency }),
    isTest: totals.isTest,
    cards: [
      { label: t('finance.commissionEarned'), value: format.money(totals.commissionEarned), hint: t('finance.payablesRecorded', { count: totals.payablesRecorded }) },
      { label: t('finance.keptFromDisputes'), value: format.money(totals.keptFromDisputes), hint: t('finance.keptFromDisputesHint') },
      { label: t('finance.paidToOffices'), value: format.money(totals.paidToOffices), hint: null },
      { label: t('finance.receivedFromOffices'), value: format.money(totals.receivedFromOffices), hint: null },
      { label: t('finance.owedToOffices'), value: format.money(totals.owedToOffices), hint: t('finance.owedNowHint') },
      { label: t('finance.owedByOffices'), value: format.money(totals.owedByOffices), hint: t('finance.owedNowHint') },
    ],
  };
}

// ── A booking's office card ─────────────────────────────────────────────────────────────────────

export interface OfficeCard {
  readonly state: string;
  readonly stateTone: Tone;
  readonly outcome: string | null;
  readonly lines: readonly PayableLineRow[];
  readonly net: string | null;
  readonly netTone: Tone;
  readonly note: string;
  readonly settlement: { readonly id: string; readonly number: string; readonly day: string } | null;
  readonly holds: readonly HoldRow[];
  readonly blocks: readonly string[];
}

/**
 * What the booking comes to for the office, in its Money section (payments Phase 8): the ledger's state, and the
 * lines and net — the ledger's once recorded, the calculator's before that. Null when nothing was paid online.
 */
export function officeCard(office: FinancialOffice | null | undefined, audience: Audience, words: PayoutWords, format: PayoutFormat): OfficeCard | null {
  if (!office || office.state === 'NotApplicable') return null;
  const net = office.net
    ? office.state === 'Settled'
      ? settledText(office.net, audience, words, format)
      : netText(office.net, audience, words, format)
    : null;
  return {
    state: words.label('payableState', office.state),
    stateTone: stateTone(office.state),
    outcome: office.outcome ? words.label('payableOutcome', office.outcome) : null,
    lines: office.lines.map((line) => lineRow(line, words, format)),
    net: net?.text ?? null,
    netTone: net?.tone ?? 'dim',
    note: OFFICE_NOTE_STATES.includes(office.state)
      ? words.t(`payouts.officeNote.${office.state.charAt(0).toLowerCase()}${office.state.slice(1)}` as TranslationKey)
      : '',
    settlement: office.settlement
      ? { id: office.settlement.settlementId, number: office.settlement.number, day: format.day(office.settlement.paidOn) }
      : null,
    holds: (office.holds ?? []).map((hold) => holdRow(hold, words, format)),
    blocks: (office.blocks ?? []).map((block) => words.label('payableBlock', block.kind)),
  };
}

/** The office-card note keys: one per state the server may send. A state this build does not know gets none. */
export const OFFICE_NOTE_STATES: readonly string[] = [
  'Open', 'AwaitingRecord', 'OnHold', 'Due', 'NothingDue', 'Blocked', 'Settled',
];

// ── Refusals ────────────────────────────────────────────────────────────────────────────────────

/** The balance due now, when a settlement was refused because it changed: the figure the dialog asks about next. */
export function currentAmountOf(error: unknown): Money | null {
  const body = (typeof error === 'object' && error !== null ? (error as { error?: unknown }).error : null) as
    | { currentAmount?: { amount?: unknown; currency?: unknown } }
    | null;
  const current = body?.currentAmount;
  return current && typeof current.amount === 'number' && typeof current.currency === 'string'
    ? { amount: current.amount, currency: current.currency }
    : null;
}
