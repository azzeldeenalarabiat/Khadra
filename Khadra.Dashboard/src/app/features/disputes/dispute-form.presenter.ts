import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence } from '../../core/i18n/problem';
import { Tone } from '../../core/models/console.models';
import { Dispute } from '../../core/models/disputes.api';
import { Money } from '../../core/models/fleet.api';
import { roundTo, scaleOf } from '../../core/services/money';
import { Translate } from '../dealer/renter-documents.presenter';

/**
 * The administrator's resolution form, as rules rather than markup (E2E F34, F35, F42; checklist 16).
 *
 * Each function restates a rule the SERVER enforces, so the form can say before the click what the
 * server would refuse after it. None of them is the judge: a resolution the form lets through can still
 * be refused, and the refusal is worded by `disputeRefusal` below.
 */

/**
 * The decimal places the split is entered and rounded at.
 *
 * The currency's minor units as the server states them (`/app-config`, `currency.minorUnits`). Before it
 * has answered, or from an API older than the field, the precision of the figures on screen, which is how
 * this form worked before it asked -- and why a whole-dinar deposit (50.000 arrives as 50) made every
 * input step by a whole dinar (F35).
 */
export function splitPlaces(minorUnits: number | undefined, ...figures: readonly number[]): number {
  return minorUnits ?? scaleOf(...figures);
}

/** What one press of an input's spinner is worth: one minor unit. */
export function stepFor(places: number): number {
  return 10 ** -places;
}

/**
 * Whether an amount was typed with more decimal places than the currency has.
 *
 * Only once the server has said how many it has: before that the console cannot know, and the server's
 * `dispute.amount_precision` refusal is the answer. Refused rather than rounded, as the server does --
 * a split typed as 1.2345 was once decided as 1.234 (F36).
 */
export function exceedsPlaces(value: number, minorUnits: number | undefined): boolean {
  return minorUnits !== undefined && scaleOf(value) > minorUnits;
}

/**
 * What the booking lets an administrator charge the office on this ticket (F34).
 *
 * `DisputeResolution.Create`'s rule: only a penalty the booking assessed against the office can be
 * charged, inside that penalty's range, and the range is the BOOKING's -- what earlier disputes on it
 * charged comes off the top, and the minimum binds only while nothing has been charged yet.
 */
export type ChargeAllowance =
  | {
      readonly kind: 'none';
      /** No penalty against the office; or one whose whole range earlier disputes already charged. */
      readonly reason: 'notAssessed' | 'alreadyCharged';
    }
  | {
      readonly kind: 'range';
      readonly min: number;
      readonly max: number;
      readonly currency: string;
      /** What earlier disputes on the booking charged, or null when they charged nothing. */
      readonly chargedEarlier: Money | null;
    };

export function chargeAllowance(d: Pick<Dispute, 'booking' | 'chargedToDealerEarlier'>): ChargeAllowance {
  const penalty = d.booking.penalty;
  if (!penalty || penalty.isNothingOwed || penalty.attributedTo !== 'Dealer') {
    return { kind: 'none', reason: 'notAssessed' };
  }
  // The server's figure. An API older than the field sends none, and the form then bounds the charge by
  // the whole range; the server still counts the earlier charges, and its refusal is worded.
  const earlier = d.chargedToDealerEarlier ?? null;
  const charged = earlier?.amount ?? 0;
  // At the precision of the two figures themselves, so the difference is exact in any currency.
  const max = roundTo(
    penalty.maxAmount.amount - charged,
    scaleOf(penalty.maxAmount.amount, charged),
  );
  if (max <= 0) return { kind: 'none', reason: 'alreadyCharged' };
  return {
    kind: 'range',
    min: charged > 0 ? 0 : penalty.minAmount.amount,
    max,
    currency: penalty.minAmount.currency,
    chargedEarlier: charged > 0 ? earlier : null,
  };
}

/**
 * What is wrong with the charge as typed, or null when nothing is. An empty field is no charge at all,
 * which is always allowed: the charge is optional even when the booking assessed one.
 */
export function chargeProblem(
  typed: string,
  allowance: ChargeAllowance,
  minorUnits: number | undefined,
): 'notAllowed' | 'notANumber' | 'tooPrecise' | 'outOfRange' | null {
  const text = typed.trim();
  if (text === '') return null;
  if (allowance.kind === 'none') return 'notAllowed';
  const value = Number(text);
  if (!Number.isFinite(value) || value < 0) return 'notANumber';
  if (exceedsPlaces(value, minorUnits)) return 'tooPrecise';
  return value < allowance.min || value > allowance.max ? 'outOfRange' : null;
}

/**
 * The tint of the SLA panel (F42), from the server's `slaState` -- the work queue's own rule, so the
 * dashboard and this panel cannot disagree. A closed ticket or one on time is neutral (null), one at risk
 * warns, one overdue alarms.
 *
 * `passed` is the browser clock's reading of the deadline. It can only ESCALATE a live ticket to overdue,
 * never calm one: the state was judged when the ticket loaded, and a screen left open must not keep a
 * deadline alive that has since passed. An API older than the field sends no state, and the clock --
 * which already takes the server's `isOverdue` -- decides alone.
 */
export function slaTone(
  d: Pick<Dispute, 'isLive' | 'slaState'>,
  passed: boolean,
): Extract<Tone, 'warn' | 'bad'> | null {
  if (!d.isLive) return null;
  if (passed || d.slaState === 'Overdue') return 'bad';
  return d.slaState === 'AtRisk' ? 'warn' : null;
}

/**
 * A refused resolution or assignment, in the reader's own language.
 *
 * Mapped from the server's stable error CODE, the only part of a refusal that can be translated:
 * `Error.Message` is English and stays English until the API grows request localisation (pre-launch
 * item 49). Every code the resolve path can return is worded here (F34); anything else shows the
 * server's sentence in English and the console's own "refused" line in Arabic.
 */
export function disputeRefusal(problem: ProblemSnapshot, t: Translate, language: Language): string {
  switch (problem.code) {
    case 'dispute.disposition_unbalanced':
      return t('disputeDetail.theThreeAmountsMust');
    case 'dispute.disposition_currency_mismatch':
      return t('disputeDetail.dispositionCurrencyMismatch');
    case 'dispute.amount_precision':
      return t('disputeDetail.amountPrecision');
    case 'dispute.dealer_charge_unassessed':
      return t('disputeDetail.chargeUnassessed');
    case 'dispute.dealer_charge_out_of_range':
      return t('disputeDetail.chargeOutsideRange');
    case 'dispute.dealer_charge_currency_mismatch':
      return t('disputeDetail.chargeCurrencyMismatch');
    case 'dispute.deposit_over_allocated':
      return t('disputeDetail.depositOverAllocated');
    case 'dispute.resolution_note_required':
      return t('disputeDetail.aNoteIsRequired');
    case 'dispute.already_resolved':
      return t('disputeDetail.thisTicketHasAlready');
    case 'dispute.already_withdrawn':
      return t('disputeDetail.thisTicketWasWithdrawn');
    case 'dispute.not_found':
      return t('disputeDetail.thatDisputeWasNot');
    case 'dispute.booking_missing':
      return t('disputeDetail.bookingMissing');
    case 'booking.not_returned':
      return t('disputeDetail.bookingNotReturned');
    case 'payments.not_live':
      return t('disputeDetail.paymentNotLive');
    case 'payments.refund_exceeds_capture':
      return t('disputeDetail.refundExceedsCapture');
    default:
      return serverSentence(problem, language, t) ?? t('dealerDelivery.serviceDidNotRespond');
  }
}
