import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { AdminDealersService } from '../../core/services/admin-dealers.service';
import { isAtRisk } from '../../core/services/sla';
import { Cell, TableRow, Tone } from '../../core/models/console.models';
import { DealerListItem } from '../../core/models/dealers.api';
import { DataTableComponent } from '../../shared/data-table/data-table.component';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * The Admin's dealer queue, reading `GET /api/v1/admin/dealers`.
 *
 * Its own component because it pages and filters against the server, and the row a click opens is a
 * real application. Every figure in a row comes from that response, the "n of m" denominator
 * included: nothing on this screen is a number the console decided for itself.
 */
@Component({
  selector: 'kh-dealers-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealers-list.component.html',
  imports: [DataTableComponent, IconComponent],
})
export class DealersListComponent {
  private readonly service = inject(AdminDealersService);

  protected readonly resource = this.service.dealers;
  protected readonly status = this.service.status;
  protected readonly search = this.service.search;

  protected readonly statuses = [
    { label: 'All', value: null },
    { label: 'Pending review', value: 'PendingReview' },
    { label: 'Clarification needed', value: 'ClarificationNeeded' },
    { label: 'Approved', value: 'Approved' },
    { label: 'Rejected', value: 'Rejected' },
  ];

  // "Registration" used to have its own column while also sitting under the dealer's name; the
  // duplicate went so that fleet size and rating -- what an admin judges a trading dealer on -- fit
  // without crowding the queue columns a pending application needs.
  protected readonly columns = [
    'Dealer',
    'Status',
    'Cars',
    'Rating',
    'Documents',
    'Joined',
    'Review due',
    'Actions',
  ];

  protected readonly rows = computed<readonly TableRow[]>(() => {
    const page = this.resource.value();
    return page ? page.items.map((dealer) => this.toRow(dealer)) : [];
  });

  protected readonly summary = computed(() => {
    const page = this.resource.value();
    if (!page) return '';
    const from = page.totalCount === 0 ? 0 : (page.page - 1) * page.pageSize + 1;
    const to = Math.min(page.page * page.pageSize, page.totalCount);
    return `Showing ${from}–${to} of ${page.totalCount} dealers`;
  });

  protected readonly page = computed(() => this.resource.value()?.page ?? 1);
  protected readonly totalPages = computed(() => this.resource.value()?.totalPages ?? 0);
  protected readonly hasPrevious = computed(() => this.resource.value()?.hasPrevious ?? false);
  protected readonly hasNext = computed(() => this.resource.value()?.hasNext ?? false);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 401) return 'Your session has expired. Sign in again.';
    if (error.status === 403) return 'Only administrators can see the dealer queue.';
    return 'The dealer queue could not be loaded. Nothing has been changed.';
  });

  protected setStatus(value: string | null): void {
    this.status.set(value);
    this.service.page.set(1);
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.service.page.set(1);
  }

  protected goTo(page: number): void {
    if (page >= 1 && page <= this.totalPages()) this.service.page.set(page);
  }

  protected reload(): void {
    this.resource.reload();
  }

  private toRow(dealer: DealerListItem): TableRow {
    const cells: Cell[] = [
      {
        kind: 'entity',
        value: dealer.businessName,
        sub: `CR ${dealer.commercialRegistrationNumber}`,
      },
      { kind: 'badge', value: this.statusLabel(dealer), tone: this.statusTone(dealer) },
      {
        kind: 'text',
        value: String(dealer.carCount),
        align: 'right',
        // An approved dealer with an empty fleet has finished nothing: they can trade and have
        // nothing to trade. Worth the admin's eye, which a plain "0" would not catch.
        variant: dealer.carCount === 0 ? 'dim' : undefined,
      },
      this.ratingCell(dealer),
      // The count an admin actually cares about is whether they are all in (spec 3.1). Both the
      // total and the threshold come from the row: they were the literal 3 until it became clear
      // the console would keep printing "of 3" after a fourth document type was required.
      {
        kind: 'text',
        value: `${dealer.documentCount} of ${dealer.requiredDocumentCount}`,
        align: 'right',
        tone: dealer.documentCount < dealer.requiredDocumentCount ? 'warn' : undefined,
      },
      { kind: 'text', value: this.date(dealer.createdAt) },
      this.reviewDueCell(dealer),
      {
        kind: 'actions',
        // The button goes where the row goes. It used to be a placeholder that did nothing, which
        // is worse than no button: an admin clicks "Review" and the console answers with silence.
        actions: [
          {
            label: this.actionLabel(dealer),
            style: 'primary',
            action: `nav:/dealers/${dealer.dealerId}`,
          },
        ],
      },
    ];

    return { id: dealer.dealerId, link: ['/dealers', dealer.dealerId], cells };
  }

  /**
   * A rating, or an honest admission that there isn't one.
   *
   * Spec 4.1 computes a dealer's rating from customer reviews. The Reviews context has no
   * persistence yet, so there is nothing to average -- and "0.0" would read as a dealer rated
   * terribly by every customer, which is the opposite of the truth.
   */
  private ratingCell(dealer: DealerListItem): Cell {
    if (dealer.averageRating === null) {
      return { kind: 'text', value: 'No reviews yet', variant: 'dim' };
    }

    return {
      kind: 'text',
      value: dealer.averageRating.toFixed(1),
      sub: `${dealer.reviewCount} ${dealer.reviewCount === 1 ? 'review' : 'reviews'}`,
      align: 'right',
    };
  }

  /** Suspension is reported ahead of verification: it is what actually stops the business trading. */
  private statusLabel(dealer: DealerListItem): string {
    if (dealer.isSuspended) return 'Suspended';
    return dealer.verificationStatus.replace(/([a-z])([A-Z])/g, '$1 $2');
  }

  private statusTone(dealer: DealerListItem): Tone {
    if (dealer.isSuspended) return 'bad';
    switch (dealer.verificationStatus) {
      case 'Approved':
        return 'ok';
      case 'Rejected':
        return 'dim';
      default:
        return 'warn';
    }
  }

  /**
   * The review deadline, red once it is past. Only meaningful while an admin still owes a decision,
   * so a settled application shows a dash rather than a deadline nobody is waiting on.
   */
  private reviewDueCell(dealer: DealerListItem): Cell {
    const awaiting = dealer.verificationStatus === 'PendingReview';
    if (!awaiting) return { kind: 'text', value: '—', variant: 'dim' };

    const now = Date.now();
    const started = Date.parse(dealer.submittedAt);
    const due = Date.parse(dealer.reviewDueAt);
    const overdue = due <= now;
    const hours = Math.round(Math.abs(due - now) / 3_600_000);
    return {
      kind: 'text',
      value: overdue ? `${hours}h over` : `${hours}h left`,
      // At risk as a fraction of the window this application froze, not a literal 12 hours. The 12
      // was 0.75 of a 48-hour SLA and would have stopped meaning anything the moment the SLA moved.
      tone: overdue ? 'bad' : isAtRisk(started, due, now) ? 'warn' : undefined,
    };
  }

  private actionLabel(dealer: DealerListItem): string {
    return dealer.verificationStatus === 'PendingReview' ? 'Review' : 'Open';
  }

  private date(iso: string): string {
    return new Date(iso).toLocaleDateString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
  }
}
