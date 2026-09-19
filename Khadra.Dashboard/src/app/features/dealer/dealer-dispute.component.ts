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
import { FormatService } from '../../core/i18n/format.service';
import { TranslationKey } from '../../core/i18n/en';
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { Dispute, DisputeResolution, DisputeStatement } from '../../core/models/disputes.api';
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
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  protected readonly statusLabel = this.i18n.statusLabel;
  private readonly formats = inject(FormatService);
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
  /** The last refusal, held as facts: its words are chosen below, so a language switch re-words it. */
  protected readonly problem = signal<ProblemSnapshot | null>(null);
  protected readonly problemText = computed(() => {
    const problem = this.problem();
    return problem ? describe(problem, this.t, this.i18n.lang()) : null;
  });
  protected readonly evidenceKeys = signal<readonly string[]>([]);
  protected readonly evidenceNames = signal<readonly string[]>([]);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 404) return this.t('dealerDispute.thatDisputeIsNot');
    return this.t('dealerDispute.theDisputeCouldNot');
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
    // The platform's promise on this ticket, by the server's flag OR the clock.
    return this.t('dealerDispute.platformDeadline', {
      clock: this.formats.sla(d.slaDeadline, d.isOverdue).text,
    });
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
      this.problem.set(snapshotProblem(error));
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
      this.ui.showToast(
        this.t('dealerDispute.statementAdded'),
        this.t('dealerDispute.thePlatformAndThe'),
      );
    } catch (error) {
      this.problem.set(snapshotProblem(error));
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
        result: {
          title: this.t('dealerDispute.disputeWithdrawn'),
          body: this.t('dealerDispute.nothingIsChargedTo'),
          tone: 'warn',
        },
      },
      async () => {
        await this.service.withdraw(d.ticketId);
        this.service.refresh();
      },
      {
        title: this.t('dealerDispute.disputeWithdrawn'),
        body: this.t('dealerDispute.nothingIsChargedTo'),
        tone: 'warn',
      },
    );
  }

  protected fieldValue(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  protected reload(): void {
    this.service.refresh();
  }

  protected dateTime(iso: string): string {
    return this.formats.dayMonthTime(iso);
  }

  protected initials(name: string): string {
    return name
      .trim()
      .split(/\s+/)
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  // Every name here arrives with a fact beside it. When the fact says the account is gone, the name
  // is an English stand-in kept for older customer apps, and this screen words it instead.

  protected openerName(d: Dispute): string {
    return d.openedByAccountClosed ? this.t('common.accountClosed') : d.openedByName;
  }

  protected authorName(s: DisputeStatement): string {
    return s.authorAccountClosed ? this.t('common.accountClosed') : s.authorName;
  }

  protected resolverName(r: DisputeResolution): string {
    return r.resolvedByAccountClosed ? this.t('common.accountClosed') : r.resolvedByName;
  }

  /** Who holds the ticket, or null while nobody does. A closed account still holds it. */
  protected holderName(d: Dispute): string | null {
    if (d.assignedAdminId === null) return null;
    return d.assignedAdminAccountClosed || d.assignedAdminName === null
      ? this.t('common.accountClosed')
      : d.assignedAdminName;
  }
}

/**
 * A server refusal, in the reader's own language, worded when it is shown: the codes this screen
 * knows by their own sentences, anything else by the server's English title while the console is
 * English and the console's own "refused" line while it is not.
 */
function describe(
  problem: ProblemSnapshot,
  t: (key: TranslationKey) => string,
  language: Language,
): string {
  switch (problem.code) {
    case 'dispute.not_open':
      return t('dealerDispute.thisDisputeIsClosed');
    case 'dispute.invalid_evidence_type':
      return t('dealerBooking.evidenceMustBeA');
    default:
      return serverSentence(problem, language, t) ?? t('dealerDelivery.serviceDidNotRespond');
  }
}
