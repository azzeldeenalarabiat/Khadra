import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AdminAuditService } from '../../core/services/admin-audit.service';
import { AuditLogEntry } from '../../core/models/audit.api';
import { Tone } from '../../core/models/console.models';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * The audit log (spec 7): every privileged action, who took it, and on what grounds.
 *
 * The record that settles a later argument, so the screen is built around the three fields the
 * dashboard's activity strip leaves out — the written reason, the change itself, and whether a person
 * or a background job acted. A row expands in place rather than opening a detail route: an entry is
 * immutable and small, and there is nothing on a separate page to do to it.
 *
 * Nothing here is a list this console keeps. The actions, the entity types and the people who appear
 * all come from `/audit-logs/vocabulary`, because a filter set typed into the front end goes stale
 * the moment an action is added — and a stale filter on this screen means an auditor believes they
 * have seen everything of a kind when they have not.
 */
@Component({
  selector: 'kh-audit-log',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './audit-log.component.html',
  imports: [IconComponent],
})
export class AuditLogComponent {
  private readonly service = inject(AdminAuditService);

  protected readonly resource = this.service.entries;
  protected readonly vocabulary = this.service.vocabulary;

  protected readonly action = this.service.action;
  protected readonly entityType = this.service.entityType;
  protected readonly actorUserId = this.service.actorUserId;
  protected readonly from = this.service.from;
  protected readonly to = this.service.to;
  protected readonly search = this.service.search;
  protected readonly page = this.service.page;

  /** Which row is open. One at a time: this is a reading screen, not a comparison one. */
  protected readonly expanded = signal<string | null>(null);

  protected readonly rows = computed(() => this.resource.value()?.items ?? []);
  protected readonly total = computed(() => this.resource.value()?.totalCount ?? 0);
  protected readonly totalPages = computed(() => this.resource.value()?.totalPages ?? 0);
  protected readonly hasPrevious = computed(() => this.resource.value()?.hasPrevious ?? false);
  protected readonly hasNext = computed(() => this.resource.value()?.hasNext ?? false);

  protected readonly anyFilter = computed(
    () =>
      !!this.action() ||
      !!this.entityType() ||
      !!this.actorUserId() ||
      !!this.from() ||
      !!this.to() ||
      !!this.search().trim(),
  );

  protected readonly summary = computed(() => {
    const page = this.resource.value();
    if (!page) return '';
    const from = page.totalCount === 0 ? 0 : (page.page - 1) * page.pageSize + 1;
    const to = Math.min(page.page * page.pageSize, page.totalCount);
    const noun = page.totalCount === 1 ? 'entry' : 'entries';
    return `Showing ${from}–${to} of ${page.totalCount} ${noun}`;
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 403) return 'The audit log is for administrators.';
    if (error.status === 400) return 'That combination of filters is not valid.';
    return 'The audit log could not be loaded. Nothing has been changed.';
  });

  protected setFilter(key: 'action' | 'entityType' | 'actorUserId', value: string): void {
    // The blank option means "no filter", not a filter whose value is empty.
    this[key].set(value || null);
    this.page.set(1);
  }

  protected setDate(key: 'from' | 'to', event: Event): void {
    this[key].set((event.target as HTMLInputElement).value || null);
    this.page.set(1);
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.page.set(1);
  }

  protected clear(): void {
    this.service.reset();
  }

  protected goTo(page: number): void {
    if (page >= 1 && page <= this.totalPages()) this.page.set(page);
  }

  protected reload(): void {
    this.service.reload();
  }

  protected toggle(entry: AuditLogEntry): void {
    this.expanded.update((open) => (open === entry.id ? null : entry.id));
  }

  /**
   * "DealerClarificationRequested" reads as "Dealer clarification requested".
   *
   * Split on the capitals rather than mapped through a dictionary: the vocabulary is the server's and
   * grows without asking this console, so a lookup table here would render a new action as a blank.
   */
  protected label(name: string): string {
    const spaced = name.replace(/([a-z])([A-Z])/g, '$1 $2');
    return spaced.charAt(0) + spaced.slice(1).toLowerCase();
  }

  /**
   * The colour of an action, by what it did.
   *
   * Grouped by consequence, not by context: anything that took something away reads bad, anything
   * that granted or restored reads ok, and everything else is neutral. An unknown action is neutral
   * rather than absent, so a new verb still renders.
   */
  protected tone(action: string): Tone {
    if (/Rejected|Suspended|Deactivated|Hidden|Cancelled|NoShow|Expired/.test(action)) return 'bad';
    if (/Approved|Reactivated|Restored|Resolved/.test(action)) return 'ok';
    if (/Clarification|Opened|Assigned/.test(action)) return 'warn';
    return 'dim';
  }

  /** A background job, not a person. Worth saying out loud on an accountability screen. */
  protected isSystem(entry: AuditLogEntry): boolean {
    return entry.actorUserId === null;
  }

  protected initials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  /** True when the entry records a change of value, rather than just that something happened. */
  protected hasChange(entry: AuditLogEntry): boolean {
    return !!entry.previousValue || !!entry.newValue;
  }
}
