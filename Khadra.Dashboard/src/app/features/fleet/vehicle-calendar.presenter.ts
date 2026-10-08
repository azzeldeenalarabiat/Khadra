import { TranslationKey } from '../../core/i18n/en';
import { Tone } from '../../core/models/console.models';
import { VehicleCalendarDay } from '../../core/models/fleet.api';

/** One cell of a car's month. */
export interface CalendarDay {
  readonly n: number;
  readonly tag: string;
  readonly tone: Tone;
  readonly bookingId: string | null;
}

/**
 * A car's month as cells (pre-launch item 54), from the days the SERVER cut in the platform's calendar.
 *
 * The screen used to cut bookings into days itself, in the browser's own zone and from every booking whose row still
 * read Requested or Approved after its window had closed, and it showed the car free during the turnaround before a
 * rental. Which day is which, which bookings still hold the car and when its claim starts are the server's answers; this
 * only words them. A day nothing holds reads by the car's own state: off the road, not listed, or free.
 */
export function calendarCells(
  days: readonly VehicleCalendarDay[],
  carStatus: string | null,
  t: (key: TranslationKey) => string,
): readonly CalendarDay[] {
  return days.map((day) => {
    const n = Number(day.date.slice(8, 10));
    if (day.status && day.turnaround)
      return { n, tag: t('vehicleDetail.turnaround'), tone: 'warn', bookingId: day.bookingId };
    switch (day.status) {
      case 'PickedUp':
        return { n, tag: t('vehicleDetail.onHire'), tone: 'accent', bookingId: day.bookingId };
      case 'Requested':
        return { n, tag: t('vehicleDetail.requested'), tone: 'bad', bookingId: day.bookingId };
      case 'Approved':
        return { n, tag: t('status.awaitingPayment'), tone: 'bad', bookingId: day.bookingId };
      case null:
        break;
      default:
        // Confirmed, or a holding status this build does not know: the booking, by its reference.
        return {
          n,
          tag: day.reference ?? t('vehicleDetail.booked'),
          tone: 'warn',
          bookingId: day.bookingId,
        };
    }
    if (carStatus === 'Maintenance')
      return { n, tag: t('status.offTheRoad'), tone: 'dim', bookingId: null };
    if (carStatus !== null && carStatus !== 'Active')
      return { n, tag: t('vehicleDetail.notListed'), tone: 'dim', bookingId: null };
    return { n, tag: t('vehicleDetail.free'), tone: 'ok', bookingId: null };
  });
}

/** The month `delta` months from `month`, as the platform's calendar numbers it. */
export function shiftCalendarMonth(
  month: { readonly year: number; readonly month: number },
  delta: number,
): { readonly year: number; readonly month: number } {
  const index = month.year * 12 + (month.month - 1) + delta;
  return { year: Math.floor(index / 12), month: (index % 12) + 1 };
}
