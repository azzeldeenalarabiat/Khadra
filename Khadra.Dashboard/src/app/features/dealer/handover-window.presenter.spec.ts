import { describe, expect, it } from 'vitest';

import { handoverWindow, msUntilNextOpening } from './handover-window.presenter';

const at = Date.parse('2026-10-06T07:15:00Z');

describe('handoverWindow', () => {
  it('stays closed, naming the moment, until the server-sent moment arrives', () => {
    expect(handoverWindow('2026-10-06T07:15:00Z', at - 60_000)).toEqual({
      open: false,
      opensAt: '2026-10-06T07:15:00Z',
    });
  });

  it('opens at the moment itself and after it', () => {
    expect(handoverWindow('2026-10-06T07:15:00Z', at)).toEqual({ open: true, opensAt: null });
    expect(handoverWindow('2026-10-06T07:15:00Z', at + 1)).toEqual({ open: true, opensAt: null });
  });

  it('is open when the server sent no moment, as an older API never did', () => {
    expect(handoverWindow(undefined, at)).toEqual({ open: true, opensAt: null });
    expect(handoverWindow(null, at)).toEqual({ open: true, opensAt: null });
  });

  it('never traps a button behind a moment it cannot read', () => {
    expect(handoverWindow('not a time', at)).toEqual({ open: true, opensAt: null });
  });
});

describe('msUntilNextOpening', () => {
  it('waits for the soonest window that is still ahead', () => {
    const pickup = handoverWindow('2026-10-06T07:15:00Z', at - 10_000);
    const ret = handoverWindow('2026-10-06T09:15:00Z', at - 10_000);
    expect(msUntilNextOpening([pickup, ret], at - 10_000)).toBe(10_000);
  });

  it('waits for nothing when every window is open', () => {
    expect(msUntilNextOpening([handoverWindow(null, at)], at)).toBeNull();
  });

  it('does not keep a timer for a moment days away', () => {
    const far = handoverWindow('2026-10-09T07:15:00Z', at);
    expect(msUntilNextOpening([far], at)).toBeNull();
  });
});
