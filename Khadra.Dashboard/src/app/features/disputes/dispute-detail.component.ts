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
import { KeyValue, TimelineStep, Tone } from '../../core/models/console.models';
import { Dispute } from '../../core/models/disputes.api';
import { AdminDisputesService } from '../../core/services/admin-disputes.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';

/** The four shapes spec 3.3 names, each one a preset split of the deposit the booking holds. */
type Preset = 'refund' | 'penalty' | 'partial' | 'waive';

/**
 * Dispute resolution (spec 3.3): the one screen on the platform where a decision about money is
 * made, and the only way a penalty is ever applied.
 *
 * A resolution is three legs of the held deposit plus an optional charge to the dealer. The legs
 * must add up to exactly what the booking froze, and the server is the judge of that — this screen
 * shows the running total so an admin sees the imbalance before submitting, but it never decides
 * the basis itself. Until Payments ships, resolving records the decision and moves nothing, which
 * the panel says next to the button rather than in a footnote.
 */
@Component({
  selector: 'kh-dispute-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dispute-detail.component.html',
  imports: [RouterLink, IconComponent, TimelineComponent],
})
export class DisputeDetailComponent {
  private readonly service = inject(AdminDisputesService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);

  private readonly ticketId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('ticketId'))),
    { initialValue: this.route.snapshot.paramMap.get('ticketId') },
  );

  constructor() {
    effect(() => this.service.viewing.set(this.ticketId()));
    // The split is seeded from the ticket, so it always starts balanced against this booking.
    effect(() => {
      const d = this.dispute();
      if (d && !d.resolution) this.apply('partial', d);
    });
  }

  protected readonly resource = this.service.dispute;
  protected readonly dispute = computed(() => this.resource.value() ?? null);

  protected readonly preset = signal<Preset>('partial');
  protected readonly refund = signal(0);
  protected readonly platform = signal(0);
  protected readonly dealer = signal(0);
  protected readonly dealerCharge = signal('');
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);

  protected readonly presets: readonly { key: Preset; label: string; desc: string }[] = [
    {
      key: 'refund',
      label: 'Refund the customer',
      desc: 'The whole deposit goes back. Nothing is kept and nothing reaches the dealer.',
    },
    {
      key: 'penalty',
      label: 'Apply the penalty in full',
      desc: 'The deposit is split the way the booking assessed it, against the party at fault.',
    },
    {
      key: 'partial',
      label: 'Partial',
      desc: 'You set each leg. The three must add up to the deposit held.',
    },
    {
      key: 'waive',
      label: 'Waive everything',
      desc: 'No penalty. The deposit returns to the customer and the booking closes clean.',
    },
  ];

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 404) return 'That dispute was not found.';
    if (error.status === 403) return 'The dispute workspace is for administrators.';
    return 'The dispute could not be loaded. Nothing has been changed.';
  });

  protected readonly currency = computed(() => this.dispute()?.depositHeld.currency ?? 'JOD');
  protected readonly held = computed(() => this.dispute()?.depositHeld.amount ?? 0);
  protected readonly allocated = computed(() =>
    round(this.refund() + this.platform() + this.dealer()),
  );
  protected readonly remainder = computed(() => round(this.held() - this.allocated()));
  protected readonly balanced = computed(() => this.remainder() === 0);
  protected readonly canResolve = computed(
    () =>
      !!this.dispute()?.isLive && this.balanced() && this.note().trim().length > 0 && !this.busy(),
  );

  protected readonly tone = computed<Tone>(() => {
    const d = this.dispute();
    if (!d) return 'dim';
    if (d.status === 'Resolved') return 'ok';
    if (d.status === 'Withdrawn') return 'dim';
    return d.isOverdue ? 'bad' : 'warn';
  });

  protected readonly sla = computed(() => {
    const d = this.dispute();
    if (!d) return { figure: '', over: false };
    if (!d.isLive) return { figure: 'Closed', over: false };
    const hours = Math.round((Date.parse(d.slaDeadline) - Date.now()) / 3_600_000);
    return hours <= 0
      ? { figure: `${-hours}h over`, over: true }
      : { figure: `${hours}h left`, over: false };
  });

  protected readonly age = computed(() => {
    const d = this.dispute();
    if (!d) return '';
    const hours = Math.round((Date.now() - Date.parse(d.openedAt)) / 3_600_000);
    return hours >= 48 ? `${Math.round(hours / 24)}d` : `${hours}h`;
  });

  /** The three panels the design puts across the top: the booking, the parties, the money. */
  protected readonly cards = computed<
    readonly {
      title: string;
      icon: 'car-simple' | 'user' | 'currency-circle-dollar';
      rows: readonly KeyValue[];
    }[]
  >(() => {
    const d = this.dispute();
    if (!d) return [];
    const b = d.booking;
    const cur = b.pricing.totalPrice.currency;
    return [
      {
        title: 'Booking',
        icon: 'car-simple',
        rows: [
          { k: 'Reference', v: b.reference },
          {
            k: 'Vehicle',
            v: b.vehicle
              ? `${b.vehicle.make} ${b.vehicle.model} ${b.vehicle.year}`
              : 'No longer listed',
          },
          { k: 'Rental', v: `${this.date(b.periodStart)} – ${this.date(b.periodEnd)}` },
          { k: 'Status', v: b.status },
        ],
      },
      {
        title: 'Parties',
        icon: 'user',
        rows: [
          { k: 'Dealer', v: b.dealerName },
          { k: 'Customer', v: b.customerName },
          { k: 'Raised by', v: `${d.openedByName} (${d.openedByParty})` },
          { k: 'Handled by', v: d.assignedAdminName ?? 'Unassigned' },
        ],
      },
      {
        title: 'Money on this booking',
        icon: 'currency-circle-dollar',
        rows: [
          { k: 'Rental total', v: `${b.pricing.rentalTotal.amount} ${cur}` },
          { k: 'Deposit held', v: `${d.depositHeld.amount} ${d.depositHeld.currency}` },
          { k: 'Security deposit', v: `${b.pricing.securityDeposit.amount} ${cur}` },
          {
            k: 'Penalty assessed',
            v:
              b.penalty && !b.penalty.isNothingOwed
                ? `${b.penalty.isRange ? b.penalty.minAmount.amount + '–' + b.penalty.maxAmount.amount : b.penalty.minAmount.amount} ${b.penalty.minAmount.currency} · ${b.penalty.attributedTo}`
                : 'None',
          },
        ],
      },
    ];
  });

  /** Every statement and every decision, oldest first: the case as it actually unfolded. */
  protected readonly timeline = computed<readonly TimelineStep[]>(() => {
    const d = this.dispute();
    if (!d) return [];
    const steps: TimelineStep[] = [
      {
        label: `Opened by ${d.openedByName}`,
        meta: `${this.when(d.openedAt)} · ${d.openedByParty} · “${d.reason}”`,
        tone: 'warn',
      },
      ...d.statements.map((s) => ({
        label: `${s.authorName} answered`,
        meta: `${this.when(s.createdAt)} · ${s.party}${s.evidence.length ? ` · ${s.evidence.length} file(s)` : ''}`,
        tone: (s.party === 'Dealer' ? 'accent' : 'dim') as Tone,
      })),
    ];
    if (d.assignedAdminName && !d.resolution) {
      steps.push({
        label: `Taken on by ${d.assignedAdminName}`,
        meta: 'Under review',
        tone: 'accent',
      });
    }
    if (d.resolution) {
      steps.push({
        label: `Resolved by ${d.resolution.resolvedByName}`,
        meta: `${this.when(d.resolution.resolvedAt)} · “${d.resolution.note}”`,
        tone: 'ok',
      });
    } else {
      steps.push({
        label: 'Decision',
        meta: `Due ${this.when(d.slaDeadline)}`,
        tone: 'dim',
        future: true,
      });
    }
    return steps;
  });

  /** Every file both parties attached, newest first, with the party that sent it. */
  protected readonly evidence = computed(() => {
    const d = this.dispute();
    if (!d) return [];
    return d.statements.flatMap((s) =>
      s.evidence.map((file) => ({
        fileName: file.fileName,
        url: file.url,
        party: s.party,
        author: s.authorName,
        when: this.when(s.createdAt),
      })),
    );
  });

  protected select(preset: Preset): void {
    const d = this.dispute();
    if (d) this.apply(preset, d);
  }

  /** Seeds the three legs from a preset. Partial keeps whatever is already there to edit. */
  private apply(preset: Preset, d: Dispute): void {
    this.preset.set(preset);
    const held = d.depositHeld.amount;
    switch (preset) {
      case 'refund':
      case 'waive':
        this.refund.set(held);
        this.platform.set(0);
        this.dealer.set(0);
        this.dealerCharge.set('');
        break;
      case 'penalty': {
        // The booking's own assessment decides who the deposit follows; a customer at fault means
        // the dealer is made whole from it, a dealer at fault means the customer is.
        const owed =
          d.booking.penalty && !d.booking.penalty.isNothingOwed ? d.booking.penalty : null;
        const toDealer =
          owed?.attributedTo === 'Customer' ? Math.min(held, owed.minAmount.amount) : 0;
        this.dealer.set(round(toDealer));
        this.refund.set(round(held - toDealer));
        this.platform.set(0);
        break;
      }
      case 'partial':
        if (this.allocated() !== held) {
          this.refund.set(held);
          this.platform.set(0);
          this.dealer.set(0);
        }
        break;
    }
  }

  protected setLeg(leg: 'refund' | 'platform' | 'dealer', event: Event): void {
    const value = Math.max(0, Number((event.target as HTMLInputElement).value) || 0);
    if (leg === 'refund') this.refund.set(value);
    if (leg === 'platform') this.platform.set(value);
    if (leg === 'dealer') this.dealer.set(value);
    this.preset.set('partial');
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  protected assign(): void {
    const d = this.dispute();
    if (!d || this.busy()) return;
    this.busy.set(true);
    this.service
      .assign(d.ticketId)
      .then(() => {
        this.service.refresh();
        this.ui.showToast(
          'Assigned to you',
          'Recorded on the ticket; any admin can still resolve it.',
        );
      })
      .catch((error: unknown) => this.problem.set(describe(error)))
      .finally(() => this.busy.set(false));
  }

  protected resolve(): void {
    const d = this.dispute();
    if (!d || !this.canResolve()) return;
    const charge = this.dealerCharge().trim();
    const cur = this.currency();

    this.ui.openAction(
      {
        icon: 'scales',
        tone: 'warn',
        danger: true,
        title: 'Record this decision?',
        body: `${this.refund()} ${cur} back to ${d.booking.customerName}, ${this.platform()} ${cur} kept by the platform, ${this.dealer()} ${cur} to ${d.booking.dealerName}${charge ? `, and ${charge} ${cur} charged to the dealer` : ''}. Both parties see the decision, your note and your name, and it is written to the audit log.`,
        note: 'Decision recorded — no funds moved. Payments is not live, so nothing is transferred yet.',
        confirm: 'Resolve dispute',
        result: { title: 'Dispute resolved', body: 'Decision recorded — no funds moved.' },
      },
      async () => {
        await this.service.resolve(d.ticketId, {
          refundToCustomer: this.refund(),
          retainedByPlatform: this.platform(),
          transferredToDealer: this.dealer(),
          dealerCharge: charge === '' ? null : Number(charge),
          note: this.note().trim(),
        });
        this.service.refresh();
        this.service.refreshList();
      },
      { title: 'Dispute resolved', body: 'Decision recorded — no funds moved.' },
    );
  }

  protected reload(): void {
    this.service.refresh();
  }

  protected date(iso: string): string {
    return new Date(iso).toLocaleDateString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
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

/** Money here is only ever compared, never accumulated across bookings; 2dp keeps the sum honest. */
function round(value: number): number {
  return Math.round(value * 100) / 100;
}

function describe(error: unknown): string {
  const problem = error as { error?: { code?: string; title?: string } };
  switch (problem.error?.code) {
    case 'dispute.disposition_unbalanced':
      return 'The three amounts must add up to exactly the deposit held.';
    case 'dispute.resolution_note_required':
      return 'A note is required so both parties can see the reasoning.';
    case 'dispute.already_resolved':
      return 'This ticket has already been resolved.';
    case 'dispute.already_withdrawn':
      return 'This ticket was withdrawn by the party who opened it.';
    default:
      return problem.error?.title ?? 'The service did not respond. Nothing has been changed.';
  }
}
