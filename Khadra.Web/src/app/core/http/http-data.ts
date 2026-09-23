import { HttpResourceRequest, httpResource } from '@angular/common/http';
import { Signal, computed } from '@angular/core';

/**
 * `httpResource`, except that reading the value of a failed request answers `undefined` instead of
 * throwing.
 *
 * Angular's `value()` throws once a resource is in its error state. Every page here reads values in
 * computed signals, effects and template expressions that sit beside — not only under — the check for
 * an error, and a throw from any of them aborts the render. In the browser that is a console error;
 * on the SERVER it was worse: the render stopped half-way and the half-drawn page, loading skeleton
 * and all, was sent with a 200. With this, a failure is only ever read through `error()`, which is
 * what every page's error panel already does.
 */
export interface HttpData<T> {
  readonly value: Signal<T | undefined>;
  readonly error: Signal<unknown>;
  readonly isLoading: Signal<boolean>;
  readonly status: Signal<string>;
  hasValue(): boolean;
  reload(): boolean;
  set(value: T): void;
}

export function httpData<T>(request: () => string | HttpResourceRequest | undefined): HttpData<T> {
  const ref = httpResource<T>((): HttpResourceRequest | undefined => {
    const r = request();
    return typeof r === 'string' ? { url: r } : r;
  });
  return {
    value: computed(() => (ref.hasValue() ? ref.value() : undefined)),
    error: ref.error,
    isLoading: ref.isLoading,
    status: ref.status,
    hasValue: () => ref.hasValue(),
    reload: () => ref.reload(),
    set: (value: T) => ref.set(value),
  };
}
