import { EN, TranslationKey } from './en';

/**
 * Which reader a status is being named for, when the plain word is not the right one.
 *
 * - `booking`: `Approved` on a booking is a gallery saying yes; on a dealership it is a licence check
 *   that passed, and Arabic does not use one word for both.
 * - `dealerBooking`: the rental office's own wording for its queue — a `Requested` booking is
 *   "Pending" to the office that has to answer it, and `PickedUp` is an "Active" rental.
 * - `vehicle`: a car in a fleet — `Active` is "Published" (customers can find it), `Maintenance` is
 *   "Off the road"; an `Active` dealership or account is a different word.
 */
export type StatusScope = 'booking' | 'dealerBooking' | 'vehicle';

/**
 * The dictionary key for a server status name, most specific scope first.
 *
 * A chain rather than one scoped key: the dealer scope only overrides the handful of statuses it
 * words differently, and everything else falls through to the booking wording and then the plain
 * one — so `Rejected` on a dealer's booking still gets the booking sense of the word.
 */
export function statusKey(name: string, scope?: StatusScope): TranslationKey | null {
  const camel = name.charAt(0).toLowerCase() + name.slice(1);
  const chain =
    scope === 'dealerBooking'
      ? [`status.${camel}DealerBooking`, `status.${camel}Booking`, `status.${camel}`]
      : scope === 'booking'
        ? [`status.${camel}Booking`, `status.${camel}`]
        : scope === 'vehicle'
          ? [`status.${camel}Vehicle`, `status.${camel}`]
          : [`status.${camel}`];
  const key = chain.find((candidate) => candidate in EN);
  return key === undefined ? null : (key as TranslationKey);
}

/**
 * Server enums that are not statuses, each worded under its own dictionary family.
 *
 * - `party`: `BookingParty` — who acted, who a penalty is attributed to (Customer, Dealer, System,
 *   Unattributed, Admin).
 * - `handoverType`: `HandoverType` — Pickup, Return.
 * - `penaltyReason`: `PenaltyReason` — the stable code for a system-written penalty sentence.
 */
export type EnumFamily = 'party' | 'handoverType' | 'penaltyReason';

/** The dictionary key for a server enum name within its family, or null when this build has none. */
export function enumKey(family: EnumFamily, name: string): TranslationKey | null {
  const key = `${family}.${name.charAt(0).toLowerCase()}${name.slice(1)}`;
  return key in EN ? (key as TranslationKey) : null;
}

/**
 * A status this build has no word for, spelled out from its name: "PartiallyRefunded" becomes
 * "Partially refunded".
 *
 * A server enum can grow a member without asking the console, and a blank pill tells the person
 * deciding whether to approve a booking nothing at all. English words are wrong under Arabic, but
 * they are readable and they are true; `I18nService` isolates them so they do not reorder the
 * sentence around them.
 */
export function spellEnumName(name: string): string {
  const spaced = name.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}
