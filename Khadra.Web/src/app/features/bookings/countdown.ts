/**
 * How long is left until a deadline the SERVER set (`decisionDeadline`, `paymentDeadline`, a handover
 * code's `expiresAt`), for a reader to see. It only describes the wait: whether the thing is still
 * open is the server's own flag (`isAwaitingPayment`, `payment.canPay`), never this arithmetic.
 *
 * Two largest units, as the app shows it: "1 day 4 hours", "3 hours 12 minutes", "8 minutes".
 */
export interface CountdownParts {
  readonly days: number;
  readonly hours: number;
  readonly minutes: number;
  readonly seconds: number;
  readonly over: boolean;
}

export function countdownParts(deadline: string | Date | null | undefined, now: number): CountdownParts | null {
  if (!deadline) return null;
  const end = (deadline instanceof Date ? deadline : new Date(deadline)).getTime();
  if (Number.isNaN(end)) return null;
  const remaining = Math.max(0, end - now);
  const totalSeconds = Math.floor(remaining / 1000);
  return {
    days: Math.floor(totalSeconds / 86_400),
    hours: Math.floor((totalSeconds % 86_400) / 3_600),
    minutes: Math.floor((totalSeconds % 3_600) / 60),
    seconds: totalSeconds % 60,
    over: remaining === 0,
  };
}

/** Which two units to name. A wait under a minute still reads "1 minute", never "0 minutes". */
export function countdownUnits(parts: CountdownParts): readonly ['days' | 'hours' | 'minutes', number][] {
  if (parts.days > 0) return parts.hours > 0 ? [['days', parts.days], ['hours', parts.hours]] : [['days', parts.days]];
  if (parts.hours > 0) return parts.minutes > 0 ? [['hours', parts.hours], ['minutes', parts.minutes]] : [['hours', parts.hours]];
  return [['minutes', Math.max(1, parts.minutes + (parts.seconds > 0 && parts.minutes === 0 ? 1 : 0))]];
}

/** "04:59" for a short-lived code. */
export function clockCountdown(parts: CountdownParts): string {
  const minutes = parts.days * 1440 + parts.hours * 60 + parts.minutes;
  return `${String(minutes).padStart(2, '0')}:${String(parts.seconds).padStart(2, '0')}`;
}
