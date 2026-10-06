import { describe, expect, it } from 'vitest';

import { Booking } from '../../core/api/bookings.api';
import { handoverWindow, msUntilOpen } from './handover-window';

const pickupFrom = '2026-10-06T07:15:00Z';
const returnFrom = '2026-10-06T09:15:00Z';
const at = (iso: string) => Date.parse(iso);

function booking(status: string, extra: Partial<Booking> = {}): Booking {
  return { status, pickupAvailableFrom: pickupFrom, returnAvailableFrom: returnFrom, ...extra } as unknown as Booking;
}

describe('handoverWindow', () => {
  it('waits for the pickup moment on a confirmed booking, naming it', () => {
    expect(handoverWindow(booking('Confirmed'), at(pickupFrom) - 60_000)).toEqual({
      kind: 'Pickup',
      open: false,
      opensAt: pickupFrom,
    });
  });

  it('offers the pickup code from the moment itself', () => {
    expect(handoverWindow(booking('Confirmed'), at(pickupFrom))).toEqual({ kind: 'Pickup', open: true, opensAt: null });
  });

  it('waits for the rental start before offering the return code', () => {
    expect(handoverWindow(booking('PickedUp'), at(returnFrom) - 1)).toEqual({
      kind: 'Return',
      open: false,
      opensAt: returnFrom,
    });
    expect(handoverWindow(booking('PickedUp'), at(returnFrom)).open).toBe(true);
  });

  it('offers no code on a booking with no handover to prove', () => {
    expect(handoverWindow(booking('Approved'), at(pickupFrom))).toEqual({ kind: null, open: false, opensAt: null });
    expect(handoverWindow(null, at(pickupFrom)).kind).toBeNull();
  });

  it('keeps the old behaviour against an API that sends no moment', () => {
    expect(
      handoverWindow(booking('Confirmed', { pickupAvailableFrom: undefined }), at(pickupFrom) - 86_400_000),
    ).toEqual({ kind: 'Pickup', open: true, opensAt: null });
  });
});

describe('msUntilOpen', () => {
  it('waits until the moment for a window that is still closed', () => {
    const window = handoverWindow(booking('Confirmed'), at(pickupFrom) - 5_000);
    expect(msUntilOpen(window, at(pickupFrom) - 5_000)).toBe(5_000);
  });

  it('waits for nothing once open, or for a moment days away', () => {
    expect(msUntilOpen(handoverWindow(booking('Confirmed'), at(pickupFrom)), at(pickupFrom))).toBeNull();
    const far = handoverWindow(booking('Confirmed'), at(pickupFrom) - 3 * 86_400_000);
    expect(msUntilOpen(far, at(pickupFrom) - 3 * 86_400_000)).toBeNull();
  });
});
