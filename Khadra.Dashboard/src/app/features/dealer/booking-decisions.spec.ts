// Loaded before anything Angular: the services below are DI tokens with partially compiled decorators.
import '@angular/compiler';
import { Injector } from '@angular/core';
import { Router } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { I18nService } from '../../core/i18n/i18n.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { DealerBookingsService, HandoverInput } from '../../core/services/dealer-bookings.service';
import { BookingDecisions } from './booking-decisions';

/**
 * Recording a handover from the office console (E2E F52, F53).
 *
 * A pickup was once stored without the notes typed into its dialog, and a reason typed beside a code was
 * always dropped: the server proves a handover by the code and never reads the reason. These pin what the
 * dialog sends — or refuses to send.
 */
class RecordingBookings {
  readonly pickups: HandoverInput[] = [];
  readonly returns: HandoverInput[] = [];

  recordPickup(_bookingId: string, handover: HandoverInput): Promise<unknown> {
    this.pickups.push(handover);
    return Promise.resolve({});
  }

  recordReturn(_bookingId: string, handover: HandoverInput): Promise<unknown> {
    this.returns.push(handover);
    return Promise.resolve({});
  }
}

function setUp() {
  const bookings = new RecordingBookings();
  const injector = Injector.create({
    providers: [
      { provide: Router, useValue: { navigateByUrl: () => Promise.resolve(true) } },
      { provide: I18nService, useValue: { t: (key: string) => key, lang: () => 'en' } },
      { provide: DealerBookingsService, useValue: bookings },
      { provide: ConsoleUiService, useClass: ConsoleUiService },
      { provide: BookingDecisions, useClass: BookingDecisions },
    ],
  });
  return { bookings, ui: injector.get(ConsoleUiService), decisions: injector.get(BookingDecisions) };
}

describe('BookingDecisions — proving a handover', () => {
  it('refuses a code and a reason together, before anything is sent', async () => {
    const { bookings, ui, decisions } = setUp();
    decisions.recordPickup('b1', 'KH-TEST', 'BMW', 'JOD', () => undefined);

    await ui.confirmModal({ handoverCode: '123456', unverifiedReason: 'Phone battery dead', notes: 'n' });

    expect(bookings.pickups).toEqual([]);
    expect(ui.modal()).not.toBeNull();
    expect(ui.modalRefusal()).toEqual({
      kind: 'local',
      refusal: expect.objectContaining({ key: 'dealerDecide.codeAndReason' }),
    });
  });

  it('sends the notes with the code, so what was typed is what is stored', async () => {
    const { bookings, ui, decisions } = setUp();
    decisions.recordPickup('b1', 'KH-TEST', 'BMW', 'JOD', () => undefined);

    await ui.confirmModal({ handoverCode: '123 456', notes: 'Scratch on the rear bumper', odometerKm: '88200', fuelLevel: '1' });

    expect(bookings.pickups).toEqual([
      {
        odometerKm: 88200,
        fuelLevel: 1,
        cashCollected: null,
        notes: 'Scratch on the rear bumper',
        handoverCode: '123 456',
        unverifiedReason: null,
      },
    ]);
  });

  it('sends a reason without a code as an unverified return', async () => {
    const { bookings, ui, decisions } = setUp();
    decisions.recordReturn('b1', 'KH-TEST', 'BMW', 'JOD', () => undefined);

    await ui.confirmModal({ unverifiedReason: 'Customer phone off; licence and ID checked', notes: 'Returned clean' });

    expect(bookings.returns).toEqual([
      expect.objectContaining({ handoverCode: null, unverifiedReason: 'Customer phone off; licence and ID checked', notes: 'Returned clean' }),
    ]);
  });

  it('refuses a figure that is not a number as a refusal of its own, not as a silent server error', async () => {
    const { bookings, ui, decisions } = setUp();
    decisions.recordPickup('b1', 'KH-TEST', 'BMW', 'JOD', () => undefined);

    await ui.confirmModal({ handoverCode: '123456', odometerKm: 'eighty' });

    expect(bookings.pickups).toEqual([]);
    expect(ui.modalRefusal()).toEqual({
      kind: 'local',
      refusal: expect.objectContaining({ key: 'dealerDecide.mustBeANumber' }),
    });
  });

  it('offers the code as one line with a number pad', () => {
    const { ui, decisions } = setUp();
    decisions.recordPickup('b1', 'KH-TEST', 'BMW', 'JOD', () => undefined);

    expect(ui.modal()?.fields?.[0]).toEqual(
      expect.objectContaining({ name: 'handoverCode', type: 'line', inputMode: 'numeric', optional: true }),
    );
  });
});
