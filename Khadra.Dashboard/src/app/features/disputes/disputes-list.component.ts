import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Tone } from '../../core/models/console.models';
import { DisputeListItem } from '../../core/models/disputes.api';
import { AdminDisputesService, DisputeQueue } from '../../core/services/admin-disputes.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence, snapshotProblem } from '../../core/i18n/problem';

/**
 * The queues, as the values the service filters on. `live` is the console's own view of everything
 * still waiting; the rest are the server's status names, so they are worded as statuses.
 */
const QUEUES: readonly DisputeQueue[] = ['live', 'Open', 'UnderReview', 'Resolved', 'Withdrawn'];

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
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly statusLabel = this.i18n.statusLabel;
  /** Server enums that are not statuses: which party raised the ticket. */
  protected readonly enumLabel = this.i18n.enumLabel;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminDisputesService);
  private readonly router = inject(Router);

  /**
   * The queue tabs in the reader's language. A `computed`, not a field: this was a field initialiser
   * once, which worded the tabs a single time and left them in that language after a switch.
   */
  protected readonly queues = computed(() =>
    QUEUES.map((key) => ({
      key,
      label: key === 'live' ? this.t('disputesList.liveQueue') : this.statusLabel(key),
    })),
  );

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

  /** A failed load, held as the resource's facts and worded here, so a language switch re-words it. */
  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    return describe(snapshotProblem(error), this.t, this.i18n.lang());
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
    return this.statusLabel(row.status);
  }

  /**
   * Time against the ticket's own deadline, as words rather than a raw timestamp: "7h remaining",
   * then "Overdue by 13h" by the server's flag OR the clock. A closed ticket says when it closed.
   */
  protected sla(row: DisputeListItem): string {
    if (row.closedAt) return this.t('disputesList.closedAt', { when: this.when(row.closedAt) });
    return this.formats.sla(row.slaDeadline, row.isOverdue).text;
  }

  protected when(iso: string): string {
    return this.formats.dayMonthTime(iso);
  }

  /** The dealership's name, or the fact that it has left the platform, in the reader's language. */
  protected dealerName(row: DisputeListItem): string {
    return row.dealerName ?? this.t('common.dealerNoLongerOnPlatform');
  }

  /** The customer's name, or the fact that the account was closed, in the reader's language. */
  protected customerName(row: DisputeListItem): string {
    return row.customerName ?? this.t('common.customerAccountClosed');
  }

  /**
   * Who holds the ticket. Whether it is held is the ID's answer, not the name's: a holder whose
   * account has since closed still holds it, and saying "Unassigned" would tell a second admin the
   * work is free while the queue's own count says otherwise.
   */
  protected holder(row: DisputeListItem): string {
    if (row.assignedAdminId === null) return this.t('common.unassigned');
    return row.assignedAdminName === null
      ? this.t('disputesList.heldByClosedAccount')
      : this.t('disputesList.withHolder', { name: row.assignedAdminName });
  }

  /**
   * Two letters for the avatar. A party that no longer resolves has no name to take them from, and
   * the server sends null rather than a sentence -- so the avatar shows a dash, not the initials of
   * "Dealer no longer on the platform", and never throws on the missing name.
   */
  protected initials(name: string | null): string {
    if (!name?.trim()) return '—';
    return name
      .trim()
      .split(/\s+/)
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }
}

/** Why the queue could not load, in the language on screen when it is shown. */
function describe(
  problem: ProblemSnapshot,
  t: (key: TranslationKey) => string,
  language: Language,
): string {
  if (problem.status === 403) return t('disputesList.theDisputeQueueIs');
  return serverSentence(problem, language, t) ?? t('disputesList.theDisputeQueueCould');
}
