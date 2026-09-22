import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LiveRefreshService, SurfaceOptions } from './live-refresh.service';

/**
 * The refresh policy, counted rather than trusted. See `docs/refresh-policy.md`.
 *
 * Every number here is a decision somebody made for a reason, and the reasons are the sort that get
 * lost: the floor exists so clicking through eight booking tabs costs one re-read; the write
 * exemption exists so a dealer watches the row they just approved leave Pending; the idle gate exists
 * because the BFF's session ticket slides on every authenticated request, so a poll left running
 * would keep an abandoned console signed in past its thirty-minute timeout.
 *
 * A spec that only proved "it polls" would let any of those be deleted quietly.
 *
 * `performance` is faked along with the timers, because the service measures age on a MONOTONIC
 * clock — the wall clock is corrected at exactly the moment a machine wakes from sleep.
 */
describe('LiveRefreshService', () => {
  let live: LiveRefreshService;
  let reloads: number;
  let loading: boolean;

  const advance = (ms: number): void => {
    vi.advanceTimersByTime(ms);
  };

  function surface(overrides: Partial<SurfaceOptions> = {}): SurfaceOptions {
    return {
      id: 'queue',
      tier: 'live',
      reload: () => {
        if (loading) return false;
        reloads += 1;
        return true;
      },
      isLoading: () => loading,
      ...overrides,
    };
  }

  beforeEach(() => {
    vi.useFakeTimers();
    // The service measures age on `performance.now()`, and the fake clock does not move it. Tying
    // it to the faked `Date` gives a monotonic clock that advances with the timers, without the
    // production code having to know it is under test.
    vi.spyOn(performance, 'now').mockImplementation(() => Date.now());
    reloads = 0;
    loading = false;
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    live = TestBed.inject(LiveRefreshService);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('polls a visible live surface on its own cadence', () => {
    // One five-second heartbeat drives every surface, so a thirty-second poll lands somewhere in
    // thirty to thirty-five. That granularity is the point of a shared scheduler and these advances
    // are sized for it rather than against it.
    live.register(surface({ pollSeconds: 30 }));
    live.succeeded('queue');

    advance(35_000);
    expect(reloads).toBe(1);

    live.succeeded('queue');
    advance(35_000);
    expect(reloads).toBe(2);
  });

  it('does not poll a surface that is not in front of anybody', () => {
    // The dealer's tab-counts are eight COUNTs. Nothing may ask for them while somebody is looking
    // at the fleet.
    live.register(surface({ pollSeconds: 30, visible: () => false }));
    live.succeeded('queue');

    advance(120_000);
    expect(reloads).toBe(0);
  });

  it('holds a second refresh under the twenty-second floor', () => {
    live.register(surface());
    live.succeeded('queue');

    live.touch('queue', { force: false });
    expect(reloads).toBe(0);

    advance(21_000);
    live.touch('queue', { force: false });
    expect(reloads).toBe(1);
  });

  it('lets a write through the floor immediately', () => {
    // Trigger C. A dealer who approves five seconds after a poll has to see the row leave Pending —
    // flooring this would break the one part of the console that already worked.
    live.register(surface());
    live.succeeded('queue');

    live.touch('queue');
    expect(reloads).toBe(1);
  });

  it('never starts a second reload while one is in flight', () => {
    live.register(surface({ pollSeconds: 30 }));
    live.succeeded('queue');
    loading = true;

    advance(120_000);
    expect(reloads).toBe(0);

    loading = false;
    advance(6_000);
    expect(reloads).toBe(1);
  });

  it('backs off after failures and resets on the first success', () => {
    live.register(surface({ pollSeconds: 30 }));
    live.succeeded('queue');

    advance(35_000);
    expect(reloads).toBe(1);

    live.failed('queue'); // 30 x 2^1 = sixty seconds away, not thirty
    advance(35_000);
    expect(reloads).toBe(1);
    advance(35_000);
    expect(reloads).toBe(2);

    live.succeeded('queue');
    advance(35_000);
    expect(reloads).toBe(3);
  });

  it('says a surface is stale only after a SECOND failure running', () => {
    // One failure is a hiccup and interrupting anybody over it is noise. Two means the screen should
    // stop implying it is current — while still showing the rows the dealer can work from.
    live.register(surface({ pollSeconds: 30 }));
    live.succeeded('queue');

    live.failed('queue');
    expect(live.staleSince().has('queue')).toBe(false);

    live.failed('queue');
    expect(live.staleSince().has('queue')).toBe(true);

    live.succeeded('queue');
    expect(live.staleSince().has('queue')).toBe(false);
  });

  it('stops polling once nobody has touched the console for ten minutes', () => {
    // NOT a battery nicety. The BFF stores its session ticket in Redis with a sliding expiration and
    // every authenticated request slides it, so a poll left running would keep an abandoned console
    // alive to the eight-hour cap instead of letting it die at the thirty-minute idle timeout.
    live.register(surface({ pollSeconds: 30 }));
    live.succeeded('queue');

    advance(11 * 60_000);
    const whileIdle = reloads;

    advance(5 * 60_000);
    expect(reloads).toBe(whileIdle);
  });

  it('gives an on-demand surface a longer floor and no poll at all', () => {
    live.register(surface({ tier: 'on-demand' }));
    live.succeeded('queue');

    advance(120_000);
    expect(reloads).toBe(0); // never polls

    live.touch('queue', { force: false });
    expect(reloads).toBe(1); // but two minutes is past its sixty-second floor
  });
});
