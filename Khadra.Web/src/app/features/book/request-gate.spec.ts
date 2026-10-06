import { describe, expect, it } from 'vitest';

import { RequestState, canSendRequest, waitsOnTheCustomer } from './request-gate';

const ready: RequestState = {
  busy: false,
  available: true,
  delivery: false,
  deliveryPointChosen: false,
  emailUnverified: false,
  documentsIncomplete: false,
};

describe('canSendRequest (E2E F18)', () => {
  it('sends a request the server could accept', () => {
    expect(canSendRequest(ready)).toBe(true);
  });

  it('holds it back while the documents are incomplete, or the email unverified', () => {
    expect(canSendRequest({ ...ready, documentsIncomplete: true })).toBe(false);
    expect(canSendRequest({ ...ready, emailUnverified: true })).toBe(false);
    expect(waitsOnTheCustomer({ ...ready, documentsIncomplete: true })).toBe(true);
  });

  it('keeps the rules it had: a quote that answered yes, a delivery point when delivering, one request at a time', () => {
    expect(canSendRequest({ ...ready, available: null })).toBe(false);
    expect(canSendRequest({ ...ready, available: false })).toBe(false);
    expect(canSendRequest({ ...ready, delivery: true })).toBe(false);
    expect(canSendRequest({ ...ready, delivery: true, deliveryPointChosen: true })).toBe(true);
    expect(canSendRequest({ ...ready, busy: true })).toBe(false);
    expect(waitsOnTheCustomer(ready)).toBe(false);
  });
});
