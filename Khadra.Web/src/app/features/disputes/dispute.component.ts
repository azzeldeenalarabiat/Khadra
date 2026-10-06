import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Money } from '../../core/api/common.api';
import { AppConfigService } from '../../core/config/app-config.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { httpData } from '../../core/http/http-data';
import { DisputeRefund, disputeParty, disputeRefundText, openedByText } from './dispute-presentation';
import { decidedEarlier, earlierDecisionNotice, readsAsWaived } from './earlier-decisions';
import { DisputeApi } from './dispute-api';
import { EvidenceDraft, evidenceRefusalText } from './evidence-draft';

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
    /** Signed, short-lived links to the files attached to it. Absent from an older API. */
    readonly evidence?: readonly { readonly fileName: string; readonly url: string }[];
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
 * A dispute — where a "your dispute was updated" notification lands. What the customer reads is the server's: its
 * status, what was said and by which side, and how it was settled. While it is live the customer can add to it and,
 * if they opened it, withdraw it, as in the app (Wave 3 C4; pre-launch item 146).
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

  private readonly api = inject(DisputeApi);
  private readonly appConfig = inject(AppConfigService);

  /** Arrived here straight from opening it: say so once (Wave 3 C4). */
  protected readonly justOpened = signal(inject(DOCUMENT).defaultView?.history.state?.opened === true);

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

  // ── Adding to a live dispute, and withdrawing one the customer opened (Wave 3 C4) ──────────────────────────────

  protected readonly limits = computed(() => this.appConfig.config()?.documents ?? null);
  protected readonly accept = computed(() => (this.limits()?.allowedContentTypes ?? []).join(','));
  protected readonly addBody = signal('');
  protected readonly evidence = new EvidenceDraft();
  protected readonly adding = signal(false);
  protected readonly added = signal(false);
  private readonly bodyMissing = signal(false);
  private readonly addProblem = signal<ProblemSnapshot | null>(null);
  protected readonly addProblemText = computed(() => {
    if (this.bodyMissing()) return this.i18n.t('dispute.add.required');
    const refused = evidenceRefusalText(this.evidence.refused(), this.limits(), this.t, (bytes) =>
      `${this.format.number(bytes / (1024 * 1024), 0)} MB`,
    );
    return refused ?? this.word(this.addProblem());
  });

  protected readonly askingToWithdraw = signal(false);
  protected readonly withdrawing = signal(false);
  private readonly withdrawProblem = signal<ProblemSnapshot | null>(null);
  protected readonly withdrawProblemText = computed(() => this.word(this.withdrawProblem()));

  protected setBody(event: Event): void {
    this.addBody.set((event.target as HTMLTextAreaElement).value);
    this.bodyMissing.set(false);
    this.added.set(false);
  }

  protected choose(event: Event): void {
    const input = event.target as HTMLInputElement;
    const chosen = [...(input.files ?? [])];
    input.value = '';
    this.evidence.choose(chosen, this.limits());
  }

  protected async addStatement(d: Dispute): Promise<void> {
    if (this.adding()) return;
    const body = this.addBody().trim();
    this.addProblem.set(null);
    if (!body) {
      this.bodyMissing.set(true);
      return;
    }
    this.adding.set(true);
    try {
      const keys: string[] = [];
      for (const file of this.evidence.files()) keys.push(await this.api.attach(d.bookingId, file));
      await this.api.addStatement(d.ticketId, body, keys);
      this.addBody.set('');
      this.evidence.clear();
      this.added.set(true);
      this.dispute.reload();
    } catch (error) {
      this.addProblem.set(snapshotProblem(error));
    } finally {
      this.adding.set(false);
    }
  }

  protected async withdraw(d: Dispute): Promise<void> {
    if (this.withdrawing()) return;
    this.withdrawProblem.set(null);
    this.withdrawing.set(true);
    try {
      await this.api.withdraw(d.ticketId);
      this.askingToWithdraw.set(false);
      this.dispute.reload();
    } catch (error) {
      this.withdrawProblem.set(snapshotProblem(error));
    } finally {
      this.withdrawing.set(false);
    }
  }

  private word(problem: ProblemSnapshot | null): string | null {
    return problem ? problemText(problem, this.t, this.i18n.language(), this.appConfig.config()) : null;
  }

  protected tone(status: string): string {
    return status === 'Resolved' ? 'badge--ok' : status === 'Withdrawn' ? '' : 'badge--warn';
  }
}
