import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { Tone } from '../../core/models/console.models';
import { DealerDisputesService } from '../../core/services/dealer-disputes.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { SessionService } from '../../core/services/session.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { MoneyPipe } from '../../shared/money.pipe';

/**
 * A dispute from the dealer's side (spec 3.3).
 *
 * Both parties' statements are visible to both, by design: one ticket per booking. The dealer can
 * add to a live ticket and, if they opened it, withdraw it. The Admin's decision appears here when it
 * is made -- as a recorded decision, with the note that no money moves until Payments is live.
 */
@Component({
  selector: 'kh-dealer-dispute',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-dispute.component.html',
  imports: [RouterLink, IconComponent, MoneyPipe],
})
export class DealerDisputeComponent {
  protected readonly t = inject(I18nService).t;
  private readonly service = inject(DealerDisputesService);
  private readonly ui = inject(ConsoleUiService);
  private readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);

  private readonly ticketId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('ticketId'))),
    { initialValue: this.route.snapshot.paramMap.get('ticketId') },
  );

  constructor() {
    effect(() => this.service.viewing.set(this.ticketId()));
  }

  protected readonly resource = this.service.dispute;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly dispute = computed(() => this.data() ?? null);
  protected readonly body = signal('');
  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly evidenceKeys = signal<readonly string[]>([]);
  protected readonly evidenceNames = signal<readonly string[]>([]);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 404) return 'That dispute is not yours to see, or no longer exists.';
    return 'The dispute could not be loaded. Nothing has been changed.';
  });

  protected readonly tone = computed<Tone>(() => {
    const d = this.dispute();
    if (!d) return 'dim';
    if (d.status === 'Resolved') return 'ok';
    if (d.status === 'Withdrawn') return 'dim';
    return d.isOverdue ? 'bad' : 'warn';
  });

  protected readonly openedByMe = computed(
    () => this.dispute()?.openedByUserId === this.session.user()?.id,
  );

  protected readonly sla = computed(() => {
    const d = this.dispute();
    if (!d || !d.isLive) return null;
    const hours = Math.round((Date.parse(d.slaDeadline) - Date.now()) / 3_600_000);
    return hours <= 0
      ? `${-hours}h over the platform's SLA`
      : `${hours}h until the platform's deadline`;
  });

  protected async attachEvidence(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const d = this.dispute();
    if (!file || !d) return;
    this.busy.set(true);
    try {
      const key = await this.service.uploadEvidence(d.bookingId, file);
      this.evidenceKeys.update((keys) => [...keys, key]);
      this.evidenceNames.update((names) => [...names, file.name]);
    } catch (error) {
      this.problem.set(describe(error));
    } finally {
      this.busy.set(false);
      input.value = '';
    }
  }

  protected async addStatement(): Promise<void> {
    const d = this.dispute();
    const text = this.body().trim();
    if (!d || !text || this.busy()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await this.service.addStatement(d.ticketId, text, this.evidenceKeys());
      this.body.set('');
      this.evidenceKeys.set([]);
      this.evidenceNames.set([]);
      this.service.refresh();
      this.ui.showToast('Statement added', 'The platform and the customer can read it.');
    } catch (error) {
      this.problem.set(describe(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected withdraw(): void {
    const d = this.dispute();
    if (!d) return;
    this.ui.openAction(
      {
        icon: 'x-circle',
        tone: 'warn',
        title: this.t('dealerDispute.withdrawThisDispute'),
        body: this.t('dealerDispute.theAmicablePathThe'),
        confirm: this.t('dealerDispute.withdrawDispute'),
        result: { title: this.t('dealerDispute.disputeWithdrawn'), body: this.t('dealerDispute.nothingIsChargedTo'), tone: 'warn' },
      },
      async () => {
        await this.service.withdraw(d.ticketId);
        this.service.refresh();
      },
      { title: this.t('dealerDispute.disputeWithdrawn'), body: this.t('dealerDispute.nothingIsChargedTo'), tone: 'warn' },
    );
  }

  protected fieldValue(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  protected reload(): void {
    this.service.refresh();
  }

  protected dateTime(iso: string): string {
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

function describe(error: unknown): string {
  const problem = error as { error?: { code?: string; title?: string } };
  switch (problem.error?.code) {
    case 'dispute.not_open':
      return 'This dispute is closed; nothing more can be added to it.';
    case 'dispute.invalid_evidence_type':
      return 'Evidence must be a photo or a PDF.';
    default:
      return problem.error?.title ?? 'The service did not respond. Nothing has been changed.';
  }
}
