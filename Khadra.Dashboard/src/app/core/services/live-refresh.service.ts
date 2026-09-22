import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs/operators';

/**
 * Anything this service can re-read. `httpResource` satisfies it; so does a hand-rolled loader.
 *
 * `reload()` returns false when the resource refuses — it is idle, or already loading. That answer
 * is the single-flight guard, which is why it is part of the shape rather than something this
 * service tries to work out for itself.
 */
export interface Reloadable {
  reload(): boolean;
  isLoading(): boolean;
}

export type RefreshTier = 'live' | 'on-demand';

export interface SurfaceOptions {
  /** Unique; also what `touch()` and the future pulse name. */
  readonly id: string;
  readonly tier: RefreshTier;
  /** Seconds between polls while the surface is visible. Omitted means no polling. */
  readonly pollSeconds?: number;
  /** True while this surface is actually in front of the user. Not registered means always. */
  readonly visible?: () => boolean;
  readonly reload: () => boolean;
  readonly isLoading: () => boolean;
  /** Called when a quiet refresh fails twice running, so the screen can say "last updated ...". */
  readonly onStale?: (since: number) => void;
}

/** The floors from `docs/refresh-policy.md`. Config never re-reads, so it is not a tier here. */
const FLOOR_SECONDS: Record<RefreshTier, number> = { live: 20, 'on-demand': 60 };

/** How often the one scheduler wakes to see what is due. */
const HEARTBEAT_MS = 5_000;

/**
 * Ten minutes without a keystroke, a click or a scroll and the console stops polling.
 *
 * NOT a battery nicety, and not something `visibilitychange` covers — an abandoned console left
 * visible on a second monitor is exactly the case. The BFF keeps its session ticket in Redis with a
 * SLIDING expiration, and every authenticated request slides it. A thirty-second poll would keep an
 * unattended console alive to the eight-hour cap instead of letting it die at the thirty-minute idle
 * timeout, which is a security property changed by accident. Ten is comfortably under thirty.
 */
const IDLE_MS = 10 * 60_000;

const BACKOFF_CAP_SECONDS = 600;

interface Surface extends SurfaceOptions {
  /** Monotonic. NULL means never successfully read — not zero, which is a real reading. */
  lastGood: number | null;
  /** Monotonic; when the next poll is due. */
  nextDue: number;
  failures: number;
}

/**
 * One place that decides when anything is re-read. See `docs/refresh-policy.md`.
 *
 * **It calls `reload()` and never bumps a request signal.** That is not a style choice. Angular's
 * resource keeps its previous value only while the request object is the SAME REFERENCE
 * (`_resource-chunk.mjs`: `if (previous.value.extRequest.request === request)`), and `reload()` is
 * the one path that holds the reference and bumps a counter. Re-running the params — which is what
 * the three `NavigationEnd` signals in this codebase used to do — builds a new request object, so
 * the resource drops what it was holding and every dependent screen blinks through its loading
 * state. On a real connection that is visible on every click.
 *
 * **A quiet refresh never takes data away.** A poll that fails leaves the last good answer on the
 * screen and backs off; only a refresh somebody ASKED for may replace a list with an error block.
 * Without that rule a flaky minute empties a dealer's queue and fills it again, which is worse than
 * not polling.
 */
@Injectable({ providedIn: 'root' })
export class LiveRefreshService {
  private readonly surfaces = new Map<string, Surface>();
  private timer: ReturnType<typeof setInterval> | null = null;

  /** Last input, monotonic. Starts "now" so a console just opened is not born idle. */
  private lastInput = now();

  /** Exposed so a screen can say when it last had a good answer. */
  readonly staleSince = signal<ReadonlyMap<string, number>>(new Map());

  constructor() {
    const destroyRef = inject(DestroyRef);

    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      // Route entry is the console's "became visible". Gated by the floor, so clicking through
      // eight booking tabs — each one a NavigationEnd — costs one re-read, not eight.
      .subscribe(() => this.wake('navigation'));

    if (typeof document !== 'undefined') {
      const onVisibility = (): void => {
        if (!document.hidden) this.wake('visible');
      };
      const onInput = (): void => {
        const wasIdle = this.isIdle();
        this.lastInput = now();
        // Coming back from idle is a became-visible: the console has been sitting there not asking.
        if (wasIdle) this.wake('input');
      };

      document.addEventListener('visibilitychange', onVisibility);
      for (const event of ['pointerdown', 'keydown', 'wheel'] as const)
        document.addEventListener(event, onInput, { passive: true });

      destroyRef.onDestroy(() => {
        document.removeEventListener('visibilitychange', onVisibility);
        for (const event of ['pointerdown', 'keydown', 'wheel'] as const)
          document.removeEventListener(event, onInput);
      });
    }

    destroyRef.onDestroy(() => this.stop());
  }

  register(options: SurfaceOptions): () => void {
    this.surfaces.set(options.id, { ...options, lastGood: null, nextDue: 0, failures: 0 });
    this.start();
    return () => {
      this.surfaces.delete(options.id);
      if (this.surfaces.size === 0) this.stop();
    };
  }

  /**
   * Something changed and we know it. Re-reads past the floor.
   *
   * This is trigger C, and the entry point the pulse service will call. `force` skips the floor
   * because a write by this user must be visible immediately — a dealer who approves five seconds
   * after a poll has to see the row leave Pending.
   */
  touch(id: string, { force = true }: { force?: boolean } = {}): void {
    const surface = this.surfaces.get(id);
    if (!surface) return;
    this.refresh(surface, force ? 'loud' : 'quiet', force);
  }

  /** Every live surface, as on a reconnect: the channel was down, so anything may have changed. */
  touchAll(): void {
    for (const surface of this.surfaces.values()) this.refresh(surface, 'quiet', true);
  }

  private isIdle(): boolean {
    return now() - this.lastInput > IDLE_MS;
  }

  private awake(): boolean {
    const hidden = typeof document !== 'undefined' && document.hidden;
    return !hidden && !this.isIdle();
  }

  private start(): void {
    // One heartbeat for every surface rather than a timer each, so N surfaces cost one wakeup.
    this.timer ??= setInterval(() => this.tick(), HEARTBEAT_MS);
  }

  private stop(): void {
    if (this.timer !== null) clearInterval(this.timer);
    this.timer = null;
  }

  /** Trigger A for everything due. */
  private wake(_reason: 'navigation' | 'visible' | 'input'): void {
    if (!this.awake()) return;
    for (const surface of this.surfaces.values()) {
      if (this.isVisible(surface)) this.refresh(surface, 'quiet', false);
    }
  }

  /** Trigger B. */
  private tick(): void {
    if (!this.awake()) return;
    const at = now();
    for (const surface of this.surfaces.values()) {
      if (!surface.pollSeconds || !this.isVisible(surface)) continue;
      if (at >= surface.nextDue) this.refresh(surface, 'quiet', false);
    }
  }

  private isVisible(surface: Surface): boolean {
    return surface.visible?.() ?? true;
  }

  private refresh(surface: Surface, volume: 'quiet' | 'loud', skipFloor: boolean): void {
    // Single flight. `reload()` would also refuse while loading, but asking first keeps the floor
    // and the backoff honest about what actually went out.
    if (surface.isLoading()) return;

    const at = now();
    const floor = FLOOR_SECONDS[surface.tier] * 1000;
    // A negative age means the wall clock moved under us. Treated as stale, which is why age is
    // measured on a monotonic clock in the first place.
    if (!skipFloor && surface.lastGood !== null) {
      const age = at - surface.lastGood;
      if (age >= 0 && age < floor) return;
    }

    if (!surface.reload()) return;

    this.schedule(surface, at);
    void volume;
  }

  /**
   * Books the next poll and, for now, treats a started reload as a success.
   *
   * The failure half is reported by the surface through {@link succeeded} / {@link failed}: this
   * service starts reloads, and only the caller can see how one ended.
   */
  private schedule(surface: Surface, at: number): void {
    const interval = (surface.pollSeconds ?? 0) * 1000;
    surface.nextDue = at + interval;
  }

  /** The surface's own answer landed. Clears any backoff and any staleness notice. */
  succeeded(id: string): void {
    const surface = this.surfaces.get(id);
    if (!surface) return;
    surface.lastGood = now();
    surface.failures = 0;
    surface.nextDue = surface.lastGood + (surface.pollSeconds ?? 0) * 1000;
    this.clearStale(id);
  }

  /**
   * The surface's own answer failed. The data it already had stays on screen.
   *
   * Backoff doubles to a cap, so a console against a dead API asks less and less rather than
   * hammering it — and a `429` counts, because the API's global limiter is keyed on remote IP and
   * every console behind one BFF can share a bucket.
   */
  failed(id: string): void {
    const surface = this.surfaces.get(id);
    if (!surface) return;

    surface.failures += 1;
    const base = surface.pollSeconds ?? FLOOR_SECONDS[surface.tier];
    const seconds = Math.min(base * 2 ** surface.failures, BACKOFF_CAP_SECONDS);
    surface.nextDue = now() + seconds * 1000;

    // One failure is a hiccup and says nothing worth interrupting anybody for. Two in a row means
    // the screen should stop implying it is current.
    if (surface.failures >= 2) {
      const since = surface.lastGood ?? 0;
      surface.onStale?.(since);
      const next = new Map(this.staleSince());
      next.set(id, since);
      this.staleSince.set(next);
    }
  }

  private clearStale(id: string): void {
    if (!this.staleSince().has(id)) return;
    const next = new Map(this.staleSince());
    next.delete(id);
    this.staleSince.set(next);
  }
}

/** Monotonic. The wall clock is corrected at exactly the moment a machine wakes from sleep. */
function now(): number {
  return typeof performance !== 'undefined' ? performance.now() : Date.now();
}
