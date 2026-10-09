import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { snapshotProblem } from './problem';

/** What a page keeps of a refusal: only the fields it may word, read defensively from whatever came back. */
describe('snapshotProblem', () => {
  const refused = (body: unknown, status = 409) =>
    snapshotProblem(new HttpErrorResponse({ status, error: body }));

  // Pre-launch item 225: a handover code asked for before its window names the moment it opens.
  it('keeps the moment a too-early refusal names', () => {
    const problem = refused({
      code: 'booking.pickup_too_early',
      traceId: 't-1',
      availableFrom: '2026-10-12T07:00:00+00:00',
    });

    expect(problem).toMatchObject({
      status: 409,
      code: 'booking.pickup_too_early',
      traceId: 't-1',
      availableFrom: '2026-10-12T07:00:00+00:00',
    });
  });

  it('has no moment when the server sent none, or sent something that is not one', () => {
    expect(refused({ code: 'booking.pickup_too_early' }).availableFrom).toBeNull();
    expect(
      refused({ code: 'booking.pickup_too_early', availableFrom: 1760252400 }).availableFrom,
    ).toBeNull();
    expect(snapshotProblem(new Error('offline')).availableFrom).toBeNull();
  });
});
