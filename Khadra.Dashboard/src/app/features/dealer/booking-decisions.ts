import { Injectable, inject } from '@angular/core';
import { DealerBookingsService, HandoverInput } from '../../core/services/dealer-bookings.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';

/**
 * The dealer's decisions on a booking, each behind the console's confirmation dialog so the
 * consequence is stated before it happens and the outcome reported back (design: `modals()`).
 *
 * Shared by the list and the detail screen so the two never word the same decision differently.
 */
@Injectable({ providedIn: 'root' })
export class BookingDecisions {
  private readonly service = inject(DealerBookingsService);
  private readonly ui = inject(ConsoleUiService);

  /** Codes the API accepts (RejectionReasons.Labels), with the words the dealer picks from. */
  readonly rejectionReasons: readonly { readonly code: string; readonly label: string }[] = [
    { code: 'VehicleUnavailable', label: 'Vehicle no longer available' },
    { code: 'DatesConflict', label: 'Dates conflict with another booking' },
    { code: 'OutsideDeliveryRadius', label: 'Delivery location outside radius' },
    { code: 'CustomerVerificationIncomplete', label: 'Customer verification incomplete' },
    { code: 'Other', label: 'Other' },
  ];

  approve(bookingId: string, reference: string, customer: string, done: () => void): void {
    this.ui.openAction(
      {
        icon: 'check-circle',
        tone: 'ok',
        title: `Approve booking ${reference}?`,
        body: `${customer} is notified and the vehicle is held for these dates. The customer's free-cancellation window starts now.`,
        fields: [
          {
            label: 'Note to customer (optional)',
            type: 'text',
            placeholder: 'Pickup instructions, delivery window…',
            hint: 'Recorded on the booking with you as the actor; the customer can read it.',
          },
        ],
        note: 'Recorded against your account on the booking history.',
        confirm: 'Approve booking',
        result: { title: 'Booking approved', body: `${reference} · customer notified` },
      },
      async (values) => {
        await this.service.approve(
          bookingId,
          values['Note to customer (optional)']?.trim() || null,
        );
        done();
      },
      { title: 'Booking approved', body: `${reference} is held for its dates.` },
    );
  }

  reject(bookingId: string, reference: string, done: () => void): void {
    this.ui.openAction(
      {
        icon: 'x-circle',
        tone: 'bad',
        danger: true,
        title: `Reject booking ${reference}?`,
        body: 'The customer is notified immediately and the dates are released. Rejection never costs the customer anything. This cannot be undone.',
        fields: [
          { label: 'Reason', type: 'select', options: this.rejectionReasons.map((r) => r.label) },
          {
            label: 'Details for the customer',
            type: 'text',
            placeholder: 'Required — shown to the customer and kept on the booking.',
          },
        ],
        confirm: 'Reject booking',
        result: {
          title: 'Booking rejected',
          body: 'Dates released · reason sent to customer',
          tone: 'warn',
        },
      },
      async (values) => {
        const label = values['Reason'] ?? this.rejectionReasons[0].label;
        const code = this.rejectionReasons.find((r) => r.label === label)?.code ?? 'Other';
        const details = values['Details for the customer']?.trim();
        if (!details) throw { error: { title: 'Tell the customer why. The reason is required.' } };
        await this.service.reject(bookingId, code, details);
        done();
      },
      {
        title: 'Booking rejected',
        body: `${reference} · dates released, reason sent to the customer`,
        tone: 'warn',
      },
    );
  }

  recordPickup(bookingId: string, reference: string, vehicle: string, done: () => void): void {
    this.ui.openAction(
      {
        icon: 'key',
        tone: 'ok',
        title: `Hand over ${vehicle}?`,
        body: `Records that ${reference} started and the keys changed hands. The odometer and fuel level protect both sides if the return is disputed.`,
        fields: [
          { label: 'Odometer (km)', type: 'text', placeholder: 'e.g. 41200' },
          {
            label: 'Fuel level (0–1)',
            type: 'text',
            placeholder: 'e.g. 1 for a full tank, 0.5 for half',
          },
          {
            label: 'Cash collected (JOD)',
            type: 'text',
            placeholder: 'The balance paid in cash at handover, if any',
          },
          {
            label: 'Notes',
            type: 'text',
            placeholder: 'Condition, accessories, anything worth writing down',
          },
        ],
        note: 'Recorded with you as the person who handed over the car.',
        confirm: 'Record pickup',
        result: { title: 'Pickup recorded', body: `${reference} is now an active rental.` },
      },
      async (values) => {
        await this.service.recordPickup(bookingId, this.handover(values));
        done();
      },
      { title: 'Pickup recorded', body: `${reference} is now an active rental.` },
    );
  }

  recordReturn(bookingId: string, reference: string, vehicle: string, done: () => void): void {
    this.ui.openAction(
      {
        icon: 'arrow-square-in',
        tone: 'ok',
        title: `Take ${vehicle} back?`,
        body: `Records that ${reference} ended and the car is back with you. The settlement window starts from this moment; either side can open a dispute inside it.`,
        fields: [
          { label: 'Odometer (km)', type: 'text', placeholder: 'e.g. 41650' },
          { label: 'Fuel level (0–1)', type: 'text', placeholder: 'e.g. 0.75' },
          {
            label: 'Cash collected (JOD)',
            type: 'text',
            placeholder: 'Any balance settled in cash at return',
          },
          { label: 'Notes', type: 'text', placeholder: 'Damage, cleanliness, missing items' },
        ],
        note: 'Recorded with you as the person who took the car back.',
        confirm: 'Record return',
        result: {
          title: 'Return recorded',
          body: `${reference} is back; the settlement window has started.`,
        },
      },
      async (values) => {
        await this.service.recordReturn(bookingId, this.handover(values));
        done();
      },
      {
        title: 'Return recorded',
        body: `${reference} is back; the settlement window has started.`,
      },
    );
  }

  private handover(values: Record<string, string>): HandoverInput {
    const number = (key: string): number | null => {
      const raw = values[key]?.trim();
      if (!raw) return null;
      const parsed = Number(raw);
      if (!Number.isFinite(parsed)) throw { error: { title: `${key} must be a number.` } };
      return parsed;
    };
    return {
      odometerKm: number('Odometer (km)'),
      fuelLevel: number('Fuel level (0–1)'),
      cashCollected: number('Cash collected (JOD)'),
      notes: values['Notes']?.trim() || null,
    };
  }
}
