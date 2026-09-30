import { FinancialBalance, FinancialCommission, FinancialDeposit } from '../models/financials.api';
import { Money } from '../models/fleet.api';
import { TranslationKey } from './en';
import { MessageParams } from './language';
import { spellEnumName } from './status-key';

type Translate = (key: TranslationKey, params?: MessageParams) => string;

/**
 * A booking's money in the words both consoles use (payments Phase 4b). Every input is a STATE the
 * server's financial calculator gave, with the amount and date it gave; these only choose a sentence,
 * and never add, subtract or compare two amounts to decide what happened.
 */

/** How the consoles print a figure and an instant: in its own currency, and in Amman. */
export interface MoneyFormat {
  money(value: Money): string;
  percent(value: number): string;
  dateTime(iso: string): string;
  /** A calendar day, `YYYY-MM-DD` — the day a settlement's money moved. */
  day?(isoDay: string): string;
}

const DEPOSIT_STATES: ReadonlySet<string> = new Set([
  'NotPaid',
  'Held',
  'AppliedToRental',
  'InSettlementWindow',
  'UnderDispute',
  'SettledWithRental',
  'ReturnedWithPayment',
  'HeldUntilWindowCloses',
  'HeldForAssessedPenalty',
  'HeldUnresolved',
  'KeptAsPenalty',
  'Released',
  'DecidedByDispute',
]);

const camel = (name: string): string => name.charAt(0).toLowerCase() + name.slice(1);

/**
 * Where the deposit is, as one sentence. A state this build does not know is spelled out from its name:
 * the consoles' convention for a server enum that grew, readable and true where a blank would say nothing.
 */
export function depositText(t: Translate, deposit: Pick<FinancialDeposit, 'state' | 'windowEndsAt'>, format: MoneyFormat): string {
  if (!DEPOSIT_STATES.has(deposit.state)) return spellEnumName(deposit.state);
  const date = deposit.windowEndsAt ? format.dateTime(deposit.windowEndsAt) : '';
  return t(`money.deposit.${camel(deposit.state)}` as TranslationKey, { date });
}

/** One line of a money section: a label, a value, and how loudly to show it. */
export interface MoneyLine {
  readonly k: string;
  readonly v: string;
  readonly hi?: boolean;
  readonly dim?: boolean;
}

/**
 * What is still to be paid, as lines. Never "paid" without a record: at a handover it is what WAS due,
 * and beside it whatever cash the office recorded — a figure that may include a cash security deposit,
 * so it is shown and never compared.
 */
export function balanceLines(t: Translate, balance: FinancialBalance, format: MoneyFormat): MoneyLine[] {
  const due = t('money.balance.dueAtHandover');
  switch (balance.state) {
    case 'NotYetDue':
      return [{ k: due, v: t('money.balance.notYetDue'), dim: true }];
    case 'DueAtHandover':
      return [{ k: due, v: format.money(balance.amount), hi: true }];
    case 'CashAtHandover':
      return [
        { k: t('money.balance.wasDueAtHandover'), v: format.money(balance.amount) },
        ...balance.cashRecorded.map((cash) => ({
          k: t(cash.handover === 'Return' ? 'money.cashRecordedReturn' : 'money.cashRecordedPickup'),
          v: format.money(cash.amount),
        })),
      ];
    case 'PaidInFull':
      return [{ k: due, v: t('money.balance.paidInFull') }];
    case 'NotDue':
      return [{ k: due, v: t('money.balance.notDue'), dim: true }];
    default:
      return [];
  }
}

/**
 * Khadra's commission, with its state (owner, 2026-09-26). A booking that earned nothing says so
 * instead of printing a frozen figure beside it as though it were owed (pre-launch item 158).
 */
export function commissionText(t: Translate, commission: FinancialCommission, format: MoneyFormat): string {
  const amount = format.money(commission.amount);
  switch (commission.state) {
    case 'Projected':
      return t('money.commission.projected', { amount });
    case 'Expected':
      return t('money.commission.expected', { amount });
    case 'Earned':
      // What was EARNED, when the ledger capped it at the office's money (payments Phase 8); the frozen figure
      // otherwise, from an API that does not send it.
      return t('money.commission.earned', { amount: commission.earned ? format.money(commission.earned) : amount });
    case 'Undecided':
      return t('money.commission.undecided', { amount });
    case 'NotEarned':
      return t('money.commission.notEarned');
    case 'NotApplicable':
      return t('money.commission.notApplicable');
    default:
      return `${amount} · ${spellEnumName(commission.state)}`;
  }
}

/** Whether the booking will ever pay anything out: not when its commission is not earned or never applied. */
/**
 * Whether a checkout attempt took money: applied to its booking, or captured and going back whole
 * (Orphaned). A failed, initiated or pending attempt took nothing, so the amount the server sends for
 * it is what the checkout ASKED for, and is worded as requested — never as charged.
 */
export function tookMoney(status: string): boolean {
  return status === 'Applied' || status === 'Orphaned';
}

/**
 * Where an assessed penalty stands, as the key of the sentence under it (payments Phase 8): kept from the deposit, resolved
 * by a dispute, to be kept unless a dispute decides otherwise — a customer's penalty of the whole deposit — or, for every
 * other assessment, money only through a dispute. Read from the server's state and flag, never from the clock.
 */
export function penaltyStandingKey(
  penalty: { readonly state?: string | null; readonly requiresTicketToEnforce?: boolean },
  audience: 'admin' | 'office',
): TranslationKey {
  if (penalty.state === 'KeptFromDeposit') return 'penaltyStanding.keptFromDeposit';
  if (penalty.state === 'ResolvedByDispute') return 'penaltyStanding.resolvedByDispute';
  if (penalty.requiresTicketToEnforce === false) return 'penaltyStanding.keptUnlessDisputed';
  return audience === 'admin' ? 'adminBooking.assessedNotChargedMoney' : 'dealerBooking.assessedNotChargedMoney';
}

export function paysOut(commission: FinancialCommission | null): boolean {
  return commission !== null && commission.state !== 'NotEarned' && commission.state !== 'NotApplicable';
}
