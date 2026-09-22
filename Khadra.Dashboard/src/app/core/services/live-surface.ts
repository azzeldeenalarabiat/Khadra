import { Injector, ResourceRef, Signal, effect, linkedSignal, untracked } from '@angular/core';
import { LiveRefreshService, SurfaceOptions } from './live-refresh.service';

/**
 * The last good value a resource held, kept through a failed reload.
 *
 * `loaded()` answers null the moment a resource errors, which is right for a screen somebody is
 * waiting on and wrong for one that is quietly re-reading in the background. A thirty-second poll
 * against a flaky connection would empty a dealer's queue and fill it again a poll later — data
 * disappearing and reappearing under somebody working through it.
 *
 * So this holds the last answer. A screen shows it and, after two failures running, says when it was
 * last true (see `LiveRefreshService.staleSince`). Loud refreshes — Retry, and after a write — still
 * surface the error through `resource.error()`, which is untouched.
 */
export function retained<T>(resource: ResourceRef<T>): Signal<T | null> {
  return linkedSignal<T | null, T | null>({
    source: () => (resource.hasValue() ? resource.value() : null),
    computation: (fresh, previous) => fresh ?? previous?.value ?? null,
  });
}

/**
 * Registers an `httpResource` with the refresh policy and reports how each read ended.
 *
 * The service starts reloads; only the resource knows whether one landed. This wires that back, so
 * the floor, the backoff and the staleness notice are measured against answers rather than attempts.
 */
export function liveResource<T>(
  live: LiveRefreshService,
  resource: ResourceRef<T>,
  options: Omit<SurfaceOptions, 'reload' | 'isLoading'>,
  injector?: Injector,
): () => void {
  const unregister = live.register({
    ...options,
    reload: () => resource.reload(),
    isLoading: () => resource.isLoading(),
  });

  effect(
    () => {
      const status = resource.status();
      untracked(() => {
        if (status === 'resolved' || status === 'local') live.succeeded(options.id);
        else if (status === 'error') live.failed(options.id);
      });
    },
    injector ? { injector } : undefined,
  );

  return unregister;
}
