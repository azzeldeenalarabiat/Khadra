import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Money } from '../../core/api/common.api';
import { snapshotProblem } from '../../core/http/problem';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { httpData } from '../../core/http/http-data';
import { DisputeRefund, disputeParty, disputeRefundText, openedByText } from './dispute-presentation';
import { decidedEarlier, earlierDecisionNotice, readsAsWaived } from './earlier-decisions';

/** `GET /api/v1/disputes/{id}` — only the fields this page shows. */
interface Dispute {
  readonly ticketId: string;
  readonly bookingId: string;
  readonly status: string;
  readonly isLive: boolean;
  readonly openedByParty: string;
  readonly reason: string;
  readonly openedAt: string;
  readonly slaDeadline: string;
  readonly closedAt: string | null;
  readonly statements: readonly {
    readonly statementId: string;
    readonly party: string;
    readonly body: string;
    readonly createdAt: string;
  }[];
  readonly resolution: {
    readonly refundToCustomer: Money;
    readonly dealerCharge: Money | null;
    readonly waivesEverything: boolean;
    readonly note: string;
    readonly resolvedAt: string;
  } | null;
  /** The customer's copy of the booking: its refunds say what became of this decision's refund (E2E F43). */
  readonly booking: { readonly reference: string; readonly refunds?: readonly DisputeRefund[] };
  /** What this ticket can split; on a later ticket, what earlier disputes left (item 169). */
  readonly depositHeld?: Money;
  /** Added 2026-09-26; absent from an older API. */
  readonly depositOnBooking?: Money;
  /** What the booking's earlier resolved disputes decided. Added 2026-09-26; absent from an older API. */
  readonly decidedByEarlierTickets?: Money;
}

const STATUSES = ['Open', 'UnderReview', 'Resolved', 'Withdrawn'];

/**
 * A dispute, read-only — where a "your dispute was updated" notification lands. What the customer
 * reads is the server's: its status, what was said and by whom, and how it was settled. Adding a
 * statement or withdrawing stays in the app for now (pre-launch item 146); the page says so.
 */
@Component({
  selector: 'kh-dispute',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, StatePanelComponent],
  templateUrl: './dispute.component.html',
})
export class DisputeComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly t = this.i18n.t.bind(this.i18n);

  readonly ticketId = input<string>('');
  protected readonly dispute = httpData<Dispute>(() => {
    const id = this.ticketId();
    return /^[0-9a-f-]{36}$/i.test(id) ? `/api/v1/disputes/${id}` : undefined;
  });
  protected readonly problem = computed(() => (this.dispute.error() ? snapshotProblem(this.dispute.error()) : null));
  protected readonly notFound = computed(
    () => !/^[0-9a-f-]{36}$/i.test(this.ticketId()) || this.problem()?.status === 404 || this.problem()?.status === 403,
  );

  /** On a live ticket, what earlier disputes on this booking already decided, in the server's figures. */
  protected readonly earlierNotice = computed(() => {
    const d = this.dispute.value();
    return d
      ? earlierDecisionNotice(d, (key, params) => this.i18n.t(key, params), (value) => this.format.money(value))
      : null;
  });

  /** What earlier disputes on this booking decided, shown beside a later ticket's settlement. */
  protected readonly earlierDecided = computed(() => {
    const d = this.dispute.value();
    return d ? decidedEarlier(d) : null;
  });

  /** What became of the refund this decision gave the customer — requested, on its way, refunded — or nothing. */
  protected readonly refundText = computed(() => {
    const d = this.dispute.value();
    return d?.resolution
      ? disputeRefundText(
          (key, params) => this.i18n.t(key, params),
          d.ticketId,
          d.booking.refunds,
          (value) => this.format.money(value),
          (iso) => this.format.dateTime(iso),
        )
      : null;
  });

  /** "Nothing is owed by either side" — only where no earlier dispute makes that untrue. */
  protected readonly waived = computed(() => {
    const d = this.dispute.value();
    return d ? readsAsWaived(d) : false;
  });

  constructor() {
    const seo = inject(SeoService);
    effect(() => seo.set({ title: this.i18n.t('seo.dispute.title'), noindex: true }));
  }

  protected status(status: string): string {
    return STATUSES.includes(status) ? this.i18n.t(`dispute.status.${status}` as TranslationKey) : status;
  }

  protected party(party: string): string {
    return disputeParty(this.t, party);
  }

  /** Who opened it and when: «فُتح من قِبلك» for the customer's own, never «فتحه أنت» (pre-launch item 218). */
  protected openedBy(party: string, openedAt: string): string {
    return openedByText(this.t, party, this.format.dateTime(openedAt));
  }

  protected tone(status: string): string {
    return status === 'Resolved' ? 'badge--ok' : status === 'Withdrawn' ? '' : 'badge--warn';
  }
}
