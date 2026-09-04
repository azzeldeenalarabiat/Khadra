import { Signal, computed } from '@angular/core';
import { Resource } from '@angular/core';

/**
 * The value of a resource, or null while it has none.
 *
 * `Resource.value()` THROWS while the resource is in an error state — it does not return undefined.
 * So `resource.value() ?? null` is not a guard: the `??` never runs. Every read of it has to be
 * reached only when the resource succeeded, which is a rule a template can keep by accident and an
 * effect or a shell component cannot keep at all.
 *
 * What that cost: a failed `GET /admin/workload` made the sidebar's badge() throw during the shell's
 * change detection, and because the shell wraps every admin screen, the console froze on its loading
 * skeleton — no error, no Retry — while each screen's own "couldn't load this" block sat unrendered
 * behind it. A 404 on a dispute did the same to its workspace through the seeding effect.
 *
 * So nothing reads `value()` directly. Screens derive from this, and their `failure()` computeds go
 * on reading `error()`, which never throws.
 */
export function loaded<T>(resource: Resource<T>): Signal<T | null> {
  return computed(() => (resource.hasValue() ? resource.value() : null));
}
