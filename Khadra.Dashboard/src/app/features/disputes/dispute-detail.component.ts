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
import { roundTo, scaleOf } from '../../core/services/money';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { MoneyPipe } from '../../shared/money.pipe';

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
  imports: [RouterLink, IconComponent, TimelineComponent, MoneyPipe],
})
export class DisputeDetailComponent {
  protected readonly t = inject(I18nService).t;
  private readonly service = inject(AdminDisputesService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);

  private readonly ticketId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('ticketId'))),
    { initialValue: this.route.snapshot.paramMap.get('ticketId') },
  );

  /**
   * The ticket the split on screen was seeded for.
   *
   * The router reuses this component between /disputes/A and /disputes/B, and the resource reloads
   * whenever the ticket is taken on, so "have I seeded yet" cannot be a boolean.
   */
  private seededFor: string | null = null;

  constructor() {
    effect(() => this.service.viewing.set(this.ticketId()));
    // The split is seeded ONCE per ticket, from the ticket, so it starts balanced against this
    // booking. It deliberately reads nothing but the loaded dispute: seeding used to call
    // apply('partial'), which reads allocated(), so the effect took a dependency on the three legs
    // -- every keystroke re-ran it and reset the admin's split back to a full refund, while the
    // boxes kept showing what had been typed and the total kept claiming it balanced.
    effect(() => {
      const d = this.dispute();
      if (!d || d.resolution || this.seededFor === d.ticketId) return;
      this.seededFor = d.ticketId;
      this.seed(d);
    });
  }

  protected readonly resource = this.service.dispute;

  /**
   * `httpResource.value()` THROWS while the resource is in an error state, so it cannot be read
   * unguarded from an effect: the throw escapes, change detection stops, and a ticket that 404s
   * renders a blank page instead of the "Couldn't load this dispute" block the template already has.
   */
  protected readonly dispute = computed(() =>
    this.resource.hasValue() ? this.resource.value() : null,
  );

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
      label: this.t('disputeDetail.refundTheCustomer'),
      desc: 'The whole deposit goes back. Nothing is kept and nothing reaches the dealer.',
    },
    {
      key: 'penalty',
      label: this.t('disputeDetail.applyThePenaltyIn'),
      desc: 'The deposit is split the way the booking assessed it, against the party at fault.',
    },
    {
      key: 'partial',
      label: 'Partial',
      desc: 'You set each leg. The three must add up to the deposit held.',
    },
    {
      key: 'waive',
      label: this.t('disputeDetail.waiveEverything'),
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

  /**
   * The currency the deposit is actually held in. Only ever read inside the loaded-dispute branch of
   * the template, so the empty fallback cannot reach the screen -- and an assumed 'JOD' would be a
   * currency code printed beside three amounts on the one screen where the platform moves money.
   */
  protected readonly currency = computed(() => this.dispute()?.depositHeld.currency ?? '');
  protected readonly held = computed(() => this.dispute()?.depositHeld.amount ?? 0);

  /** The precision this ticket's money is held at, read off the figures rather than assumed. */
  private readonly scale = computed(() =>
    scaleOf(this.held(), this.refund(), this.platform(), this.dealer()),
  );

  /** What one press of an input's spinner is worth, at the deposit's own precision. */
  protected readonly step = computed(() => 10 ** -scaleOf(this.held()));

  protected readonly allocated = computed(() =>
    roundTo(this.refund() + this.platform() + this.dealer(), this.scale()),
  );
  protected readonly remainder = computed(() =>
    roundTo(this.held() - this.allocated(), this.scale()),
  );
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
        title: this.t('disputeDetail.moneyOnThisBooking'),
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
    // The opening statement IS statement #1 -- Open() writes it from the reason -- so it is labelled
    // as the opening rather than added a second time above the list. Listing both put the same
    // sentence on the trail twice and made a one-statement ticket read as two.
    const steps: TimelineStep[] = d.statements.map((s, index) => ({
      label: index === 0 ? `Opened by ${s.authorName}` : `${s.authorName} answered`,
      meta: `${this.when(s.createdAt)} · ${s.party}${s.evidence.length ? ` · ${s.evidence.length} file(s)` : ''}`,
      tone: (index === 0 ? 'warn' : s.party === 'Dealer' ? 'accent' : 'dim') as Tone,
    }));
    if (steps.length === 0) {
      steps.push({
        label: `Opened by ${d.openedByName}`,
        meta: `${this.when(d.openedAt)} · ${d.openedByParty} · “${d.reason}”`,
        tone: 'warn',
      });
    }
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

  /**
   * The opening position for a ticket: the whole deposit back to the customer, which balances.
   *
   * Writes only. Nothing here reads a signal, so the effect that calls it cannot end up depending
   * on the very fields an admin is typing into.
   */
  private seed(d: Dispute): void {
    this.preset.set('partial');
    this.refund.set(d.depositHeld.amount);
    this.platform.set(0);
    this.dealer.set(0);
    this.dealerCharge.set('');
    this.note.set('');
    this.problem.set(null);
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
        const places = scaleOf(held, toDealer);
        this.dealer.set(roundTo(toDealer, places));
        this.refund.set(roundTo(held - toDealer, places));
        this.platform.set(0);
        break;
      }
      case 'partial':
        // Nothing. "Partial" means the admin sets each leg themselves, so choosing it must not
        // overwrite the legs they are already setting -- it used to reset an unbalanced split back
        // to a full refund, which is the one thing an admin part-way through a split has not asked
        // for. The opening position is seeded once when the ticket loads; see seed().
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
        title: this.t('disputeDetail.recordThisDecision'),
        body: `${this.refund()} ${cur} back to ${d.booking.customerName}, ${this.platform()} ${cur} kept by the platform, ${this.dealer()} ${cur} to ${d.booking.dealerName}${charge ? `, and ${charge} ${cur} charged to the dealer` : ''}. Both parties see the decision, your note and your name, and it is written to the audit log.`,
        note: this.t('disputeDetail.decisionRecordedNoFunds'),
        confirm: this.t('disputeDetail.resolveDispute'),
        result: { title: this.t('disputeDetail.disputeResolved'), body: this.t('disputeDetail.decisionRecordedNoFunds2') },
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
      { title: this.t('disputeDetail.disputeResolved'), body: this.t('disputeDetail.decisionRecordedNoFunds2') },
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
