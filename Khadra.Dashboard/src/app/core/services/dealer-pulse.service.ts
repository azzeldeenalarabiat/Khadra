import { httpResource } from '@angular/common/http';
import { Injectable, computed, effect, inject, untracked } from '@angular/core';
import { LiveRefreshService } from './live-refresh.service';
import { liveResource } from './live-surface';
import { SessionService } from './session.service';

interface DealerPulse {
  /** Opaque. Compared, never read. */
  readonly bookings: string;
}

/**
 * Asks the server whether this dealer's queue has changed, and re-reads the real endpoints when it
 * has. See `docs/refresh-policy.md`.
 *
 * This is the whole answer to "a new customer booking must reach the dealer without a browser
 * refresh". It polls ONE cheap endpoint — two indexed counts — every thirty seconds, and touches
 * nothing at all until the token moves. A dealer with a quiet afternoon costs the API two counts a
 * minute; a dealer whose queue is busy pays for the re-read only when there is something to re-read.
 *
 * **The token is never rendered and never stored as data.** It exists to be compared with the last
 * one. Everything a screen shows still comes from the endpoint that owns it, because a count arriving
 * from two places is a count that can disagree with itself — and a dealer looking at two numbers has
 * no way to tell which is true.
 *
 * **This is the seam for the push channel.** When it lands it calls `live.touch('dealer.bookings')`
 * with the same scope names and no payload, and fires trigger A for every scope on reconnect. A
 * socket is then a faster way to learn the same fact, not a second way to learn a different one —
 * and this poller becomes the fallback for when the socket is down.
 */
@Injectable({ providedIn: 'root' })
export class DealerPulseService {
  private readonly live = inject(LiveRefreshService);
  private readonly session = inject(SessionService);

  private readonly isDealer = computed(() => {
    const role = this.session.user()?.role;
    return role === 'DealerOwner' || role === 'DealerEmployee';
  });

  /** Undefined keeps the resource idle, so an admin never asks a dealer question. */
  private readonly pulse = httpResource<DealerPulse>(() =>
    this.isDealer() ? '/api/v1/dealers/me/pulse' : undefined,
  );

  private seen: string | null = null;

  constructor() {
    liveResource(this.live, this.pulse, {
      id: 'dealer.pulse',
      tier: 'live',
      pollSeconds: 30,
      visible: () => this.isDealer(),
    });

    effect(() => {
      const token = this.pulse.hasValue() ? this.pulse.value().bookings : null;
      if (token === null) return;

      untracked(() => {
        const first = this.seen === null;
        const moved = this.seen !== token;
        this.seen = token;

        // The FIRST token is not news. The screens that care have just loaded their own data; a
        // re-read here would double every console's opening cost to prove nothing.
        if (first || !moved) return;

        // Something moved. Which, the pulse does not say and does not need to — the readers below
        // are the ones that know. `force: false` so this still respects the twenty-second floor:
        // the pulse cannot make the console ask faster than the policy allows, whatever the server
        // says.
        this.live.touch('dealer.bookings', { force: false });
        this.live.touch('dealer.counts', { force: false });
        this.live.touch('dealer.dashboard', { force: false });
      });
    });
  }
}
