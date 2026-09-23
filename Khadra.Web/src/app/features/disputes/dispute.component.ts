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
  readonly booking: { readonly reference: string };
}

const STATUSES = ['Open', 'UnderReview', 'Resolved', 'Withdrawn'];
const PARTIES = ['Customer', 'Dealer', 'Admin'];

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

  readonly ticketId = input<string>('');
  protected readonly dispute = httpData<Dispute>(() => {
    const id = this.ticketId();
    return /^[0-9a-f-]{36}$/i.test(id) ? `/api/v1/disputes/${id}` : undefined;
  });
  protected readonly problem = computed(() => (this.dispute.error() ? snapshotProblem(this.dispute.error()) : null));
  protected readonly notFound = computed(
    () => !/^[0-9a-f-]{36}$/i.test(this.ticketId()) || this.problem()?.status === 404 || this.problem()?.status === 403,
  );

  constructor() {
    const seo = inject(SeoService);
    effect(() => seo.set({ title: this.i18n.t('seo.dispute.title'), noindex: true }));
  }

  protected status(status: string): string {
    return STATUSES.includes(status) ? this.i18n.t(`dispute.status.${status}` as TranslationKey) : status;
  }

  protected party(party: string): string {
    return PARTIES.includes(party) ? this.i18n.t(`dispute.party.${party}` as TranslationKey) : party;
  }

  protected tone(status: string): string {
    return status === 'Resolved' ? 'badge--ok' : status === 'Withdrawn' ? '' : 'badge--warn';
  }
}
