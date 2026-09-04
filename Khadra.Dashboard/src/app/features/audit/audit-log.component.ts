import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AdminAuditService } from '../../core/services/admin-audit.service';
import { loaded } from '../../core/services/loaded';
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

  // Resource.value() throws while a request has failed, so nothing reads it directly; failure()
  // goes on reading error(), which does not throw.
  private readonly loadedPage = loaded(this.resource);
  protected readonly vocabulary = this.service.vocabulary;
  private readonly vocabularyData = loaded(this.vocabulary);

  protected readonly action = this.service.action;
  protected readonly entityType = this.service.entityType;
  protected readonly actorUserId = this.service.actorUserId;
  protected readonly from = this.service.from;
  protected readonly to = this.service.to;
  protected readonly search = this.service.search;
  protected readonly systemOnly = this.service.systemOnly;
  protected readonly entityId = this.service.entityId;
  protected readonly page = this.service.page;

  constructor() {
    // A record's own screen links here with its type and id, so "everything that has happened to
    // this dealer" is one click from the dealer. Read once on entry: the filters are the screen's
    // state after that, and re-reading would fight the admin's own changes.
    const params = inject(ActivatedRoute).snapshot.queryParamMap;
    const entityType = params.get('entityType');
    const entityId = params.get('entityId');
    // Who acted, as well as what was acted on: the Admin users screen links here to answer "what is
    // this account accountable for" before anyone deactivates it.
    const actorUserId = params.get('actorUserId');
    if (entityType) this.service.entityType.set(entityType);
    if (entityId) this.service.entityId.set(entityId);
    if (actorUserId) this.service.actorUserId.set(actorUserId);
  }

  /** Which row is open. One at a time: this is a reading screen, not a comparison one. */
  protected readonly expanded = signal<string | null>(null);

  protected readonly rows = computed(() => this.loadedPage()?.items ?? []);
  protected readonly total = computed(() => this.loadedPage()?.totalCount ?? 0);
  protected readonly totalPages = computed(() => this.loadedPage()?.totalPages ?? 0);
  protected readonly hasPrevious = computed(() => this.loadedPage()?.hasPrevious ?? false);
  protected readonly hasNext = computed(() => this.loadedPage()?.hasNext ?? false);

  protected readonly anyFilter = computed(
    () =>
      !!this.action() ||
      !!this.entityType() ||
      !!this.actorUserId() ||
      !!this.from() ||
      !!this.to() ||
      !!this.search().trim() ||
      this.systemOnly() ||
      !!this.entityId(),
  );

  protected readonly summary = computed(() => {
    const page = this.loadedPage();
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

  protected setFilter(key: 'action' | 'entityType', value: string): void {
    // The blank option means "no filter", not a filter whose value is empty.
    this[key].set(value || null);
    this.page.set(1);
  }

  /**
   * The Who dropdown carries one option that is not a person.
   *
   * "System" cannot be an actor id, because the System IS the absence of one — so it selects a
   * different filter entirely. Without this, sweeps and expiries are the single class of entry an
   * auditor cannot isolate, which is backwards: an action nobody was present for is the one most
   * worth being able to review.
   */
  protected setActor(value: string): void {
    const system = value === 'system';
    this.systemOnly.set(system);
    this.actorUserId.set(system || !value ? null : value);
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

  /**
   * An instant, stamped in the calendar this screen FILTERS by.
   *
   * Not the browser's. The date pickers resolve against the platform's reporting zone, so rendering
   * in the reader's own zone made an entry near midnight appear to vanish: an admin in London
   * filtering "to 3 September" would see a row labelled "03 Sep 22:00" disappear, correctly — it is
   * the 4th in Amman — and inexplicably, because nothing on the screen said which calendar it meant.
   *
   * Seconds are shown because entries are strictly ordered and two in the same minute are common;
   * without them the log looks simultaneous where it is sequential.
   */
  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      timeZone: this.timeZone(),
    });
  }

  /** The zone the server resolves this log in. Undefined until it answers — never assumed. */
  protected readonly timeZone = computed(() => this.vocabularyData()?.reportingTimeZone);

  /** Spelled out beside the table, so nobody has to guess whose midnight a day ends at. */
  protected readonly timeZoneNote = computed(() => {
    const zone = this.timeZone();
    return zone ? `Times and dates in ${zone.replace('_', ' ')}` : '';
  });

  /** True when the entry records a change of value, rather than just that something happened. */
  protected hasChange(entry: AuditLogEntry): boolean {
    return !!entry.previousValue || !!entry.newValue;
  }
}
