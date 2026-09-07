import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Tone } from '../../core/models/console.models';
import { DisputeListItem } from '../../core/models/disputes.api';
import { AdminDisputesService, DisputeQueue } from '../../core/services/admin-disputes.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * The Admin's dispute queue (spec 3.3).
 *
 * Sorted by the deadline frozen on each ticket, soonest first, because the queue exists to answer
 * "what runs out next" rather than "what arrived last". Overdue is the ticket's own judgement from
 * the API, not a comparison this screen makes: raising the SLA later must never retroactively
 * breach a promise already made.
 */
@Component({
  selector: 'kh-disputes-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './disputes-list.component.html',
  imports: [RouterLink, IconComponent],
})
export class DisputesListComponent {
  protected readonly t = inject(I18nService).t;
  private readonly service = inject(AdminDisputesService);
  private readonly router = inject(Router);

  protected readonly queues: readonly { key: DisputeQueue; label: string }[] = [
    { key: 'live', label: this.t('disputesList.liveQueue') },
    { key: 'Open', label: 'Open' },
    { key: 'UnderReview', label: this.t('disputesList.underReview') },
    { key: 'Resolved', label: 'Resolved' },
    { key: 'Withdrawn', label: 'Withdrawn' },
  ];

  protected readonly queue = this.service.queue;
  protected readonly overdueOnly = this.service.overdueOnly;
  protected readonly page = this.service.page;
  protected readonly resource = this.service.list;

  // Resource.value() throws while a request has failed, so nothing reads it directly; failure()
  // goes on reading error(), which does not throw.
  private readonly loadedPage = loaded(this.resource);

  protected readonly rows = computed(() => this.loadedPage()?.items ?? []);
  protected readonly total = computed(() => this.loadedPage()?.totalCount ?? 0);
  protected readonly totalPages = computed(() => this.loadedPage()?.totalPages ?? 1);
  // The server's, for the whole filtered queue. These were counted from the rows on screen and
  // printed beside a platform total, which is right only while everything fits on one page.
  private readonly loadedCounts = loaded(this.service.counts);
  protected readonly overdue = computed(() => this.loadedCounts()?.overdue ?? null);
  protected readonly unassigned = computed(() => this.loadedCounts()?.unassigned ?? null);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 403) return 'The dispute queue is for administrators.';
    return 'The dispute queue could not be loaded. Nothing has been changed.';
  });

  protected select(queue: DisputeQueue): void {
    this.queue.set(queue);
    this.page.set(1);
  }

  protected toggleOverdue(): void {
    this.overdueOnly.update((only) => !only);
    this.page.set(1);
  }

  protected goTo(page: number): void {
    this.page.set(Math.max(1, page));
  }

  protected open(row: DisputeListItem): void {
    void this.router.navigate(['/disputes', row.ticketId]);
  }

  protected reload(): void {
    this.service.refreshList();
  }

  protected tone(row: DisputeListItem): Tone {
    if (row.status === 'Resolved') return 'ok';
    if (row.status === 'Withdrawn') return 'dim';
    return row.isOverdue ? 'bad' : 'warn';
  }

  protected label(row: DisputeListItem): string {
    return row.status === 'UnderReview' ? 'Under review' : row.status;
  }

  /** Time against the ticket's own deadline, as words rather than a raw timestamp. */
  protected sla(row: DisputeListItem): string {
    if (row.closedAt) return `Closed ${this.when(row.closedAt)}`;
    const hours = Math.round((Date.parse(row.slaDeadline) - Date.now()) / 3_600_000);
    if (hours <= 0) return `${-hours}h over`;
    return hours >= 48 ? `${Math.round(hours / 24)}d left` : `${hours}h left`;
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected initials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }
}
