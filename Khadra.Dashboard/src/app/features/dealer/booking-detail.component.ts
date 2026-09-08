import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { KeyValue, TimelineStep, Tone } from '../../core/models/console.models';
import { Booking } from '../../core/models/bookings.api';
import { DealerBookingsService } from '../../core/services/dealer-bookings.service';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { DealerDisputesService } from '../../core/services/dealer-disputes.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';
import { BookingDecisions } from './booking-decisions';
import { I18nService } from '../../core/i18n/i18n.service';
import { MoneyPipe } from '../../shared/money.pipe';

/**
 * One booking, from the dealer's side (design: Dealer Console, `isBooking`).
 *
 * Everything shown is the booking's own frozen record: its pricing, its terms, its history with the
 * actor named on every step. The design's "customer rating" and "verification" rows have no source
 * yet and are not shown; the money panel shows what the customer paid and what is due, never a payout
 * (Payments is not built, and the panel says so).
 */
@Component({
  selector: 'kh-dealer-booking-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './booking-detail.component.html',
  imports: [RouterLink, IconComponent, TimelineComponent, MoneyPipe],
})
export class DealerBookingDetailComponent {
  protected readonly t = inject(I18nService).t;
  private readonly service = inject(DealerBookingsService);
  private readonly console = inject(DealerConsoleService);
  private readonly disputes = inject(DealerDisputesService);
  private readonly decisions = inject(BookingDecisions);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly bookingId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('bookingId'))),
    { initialValue: this.route.snapshot.paramMap.get('bookingId') },
  );

  constructor() {
    effect(() => this.service.viewing.set(this.bookingId()));
  }

  protected readonly resource = this.service.booking;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly booking = computed(() => this.data() ?? null);
  protected readonly me = this.console.me;
  private readonly dealer = loaded(this.me);
  protected readonly disputeReason = signal('');
  protected readonly disputeBusy = signal(false);
  protected readonly evidenceKeys = signal<readonly string[]>([]);
  protected readonly evidenceNames = signal<readonly string[]>([]);
  protected readonly problem = signal<string | null>(null);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 404) return 'That booking is not one of yours, or no longer exists.';
    return 'The booking could not be loaded. Nothing has been changed.';
  });

  protected readonly tone = computed<Tone>(() => {
    const b = this.booking();
    if (!b) return 'dim';
    if (b.liveDisputeId) return 'bad';
    switch (b.status) {
      // Waiting on somebody: the dealer's answer, or the customer's deposit.
      case 'Requested':
      case 'Approved':
        return 'warn';
      case 'Confirmed':
        return 'accent';
      case 'PickedUp':
      case 'Returned':
        return 'ok';
      default:
        return 'dim';
    }
  });

  protected readonly label = computed(() => {
    const b = this.booking();
    if (!b) return '';
    if (b.liveDisputeId) return 'Disputed';
    const labels: Partial<Record<Booking['status'], string>> = {
      Requested: 'Pending',
      Approved: 'Awaiting deposit',
      PickedUp: 'Active',
      NoShow: 'No-show',
    };
    return labels[b.status] ?? b.status;
  });

  protected readonly meta = computed(() => {
    const b = this.booking();
    if (!b) return '';
    const method = b.pickupMethod === 'Delivery' ? 'delivery' : 'pickup at your location';
    return `Requested ${this.dateTime(b.requestedAt ?? b.createdAt)} · ${b.customerName} · ${b.pricing.days} ${b.pricing.days === 1 ? 'day' : 'days'} · ${method}`;
  });

  /**
   * Time left to answer, against the deadline THIS booking carries.
   *
   * It used to count down to the rental start, which was the rule while a deposit had to clear
   * before a request reached a dealer at all. A request costs the customer nothing now, so the
   * answer window is what gets the car back if nobody replies, and the server sends the moment.
   */
  protected readonly answerBy = computed(() => {
    const b = this.booking();
    if (!b || b.status !== 'Requested') return null;
    const hours = Math.round((Date.parse(b.decisionDeadline) - Date.now()) / 3_600_000);
    if (hours <= 0)
      return { figure: 'Expired', note: this.t('dealerBooking.theAnswerWindowHasClosed') };
    return {
      figure: hours >= 48 ? `${Math.round(hours / 24)}d left` : `${hours}h left`,
      note: `Expires ${this.dateTime(b.decisionDeadline)}.`,
    };
  });

  protected readonly customerRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    // Only what the API carries. NO CONTACT DETAILS: the platform has not decided whether a dealer
    // ever sees a customer's phone, and the console must not promise it. The history below is a
    // separate, deliberately narrower thing -- aggregates the platform itself counted.
    return [{ k: 'Name', v: b.customerName }];
  });

  /**
   * What the platform knows about this customer, or null.
   *
   * Null covers two different situations that look the same on screen and are both correct: the
   * booking is no longer live, so the gallery's access has ended (409 from the server), or the
   * request has not landed yet. Neither is an error to show.
   */
  protected readonly reputation = computed(() => this.service.reputation.value() ?? null);

  /** True when the access rule -- not a failure -- is why there is nothing to show. */
  protected readonly reputationClosed = computed(
    () => this.service.reputation.status() === 'error' && !this.reputation(),
  );

  protected readonly myCustomerRating = computed(() => this.service.customerRating.value() ?? null);

  /**
   * Whether the gallery may rate this customer now.
   *
   * The SERVER decides whether a rating is accepted; this only decides whether to offer the control,
   * from the same two facts the server judges on -- the booking is finished, and no rating exists yet.
   */
  protected readonly canRateCustomer = computed(() => {
    const b = this.booking();
    return !!b && b.status === 'Completed' && !this.myCustomerRating();
  });

  protected readonly ratingBusy = signal(false);

  /** The stars offered. A fixed 1-5 scale: it is the platform's, and it is not configurable. */
  protected readonly ratingChoices = [1, 2, 3, 4, 5] as const;

  async rateCustomer(rating: number): Promise<void> {
    const b = this.booking();
    if (!b || this.ratingBusy()) return;

    this.ratingBusy.set(true);
    this.problem.set(null);
    try {
      await this.service.rateCustomer(b.bookingId, rating);
      this.service.refresh();
      this.ui.showToast(this.t("dealerBooking.rateCustomer"), this.t("dealerBooking.rateSaved"));
    } catch (error: unknown) {
      this.problem.set(describe(error));
    } finally {
      this.ratingBusy.set(false);
    }
  }

  protected readonly vehicleRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    if (!b.vehicle) return [{ k: 'Vehicle', v: 'No longer listed', tone: 'dim' }];
    return [
      { k: 'Vehicle', v: `${b.vehicle.make} ${b.vehicle.model} ${b.vehicle.year}` },
      { k: 'Plate', v: b.vehicle.plateNumber },
      { k: 'Colour', v: b.vehicle.color ?? '—' },
      {
        k: 'Daily price on this booking',
        v: `${b.pricing.dailyRate.amount} ${b.pricing.dailyRate.currency}`,
      },
    ];
  });

  protected readonly rentalRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    return [
      { k: 'Start', v: this.dateTime(b.periodStart) },
      { k: 'End', v: this.dateTime(b.periodEnd) },
      { k: 'Duration', v: `${b.pricing.days} ${b.pricing.days === 1 ? 'day' : 'days'}` },
      {
        k: 'Mileage',
        v: b.pricing.mileageUnlimited
          ? 'Unlimited'
          : `${b.pricing.mileageDailyLimitKm} km/day, ${b.pricing.mileageExcessFeePerKm?.amount ?? 0} ${b.pricing.dailyRate.currency}/km over`,
      },
      { k: 'Fuel', v: b.pricing.fuelPolicy === 'FullToFull' ? 'Full to full' : 'Same to same' },
    ];
  });

  protected readonly pickupRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    if (b.pickupMethod !== 'Delivery' || !b.deliveryLocation) {
      return [{ k: 'Method', v: 'Collected from your location' }];
    }
    return [
      { k: 'Method', v: 'Delivery' },
      {
        k: 'Location',
        v: `${b.deliveryLocation.latitude.toFixed(4)}, ${b.deliveryLocation.longitude.toFixed(4)}`,
      },
      {
        k: 'Delivery fee',
        v: `${b.pricing.deliveryFee.amount} ${b.pricing.deliveryFee.currency} · frozen on this booking`,
      },
    ];
  });

  /** The money on THIS booking, as frozen when it was made. */
  protected readonly moneyRows = computed(() => {
    const b = this.booking();
    if (!b) return [];
    const cur = b.pricing.totalPrice.currency;
    // What the customer has paid and what is still due only mean something while a handover can
    // still happen. A rejected or expired request refunds its deposit (Payments will do that);
    // a cancelled or no-show booking is settled through the penalty panel, not this one.
    const live =
      b.status === 'Requested' ||
      b.status === 'Approved' ||
      b.status === 'Confirmed' ||
      b.status === 'PickedUp';
    const settling = b.status === 'Returned' || b.status === 'Completed';
    // From the server, not from the status: a booking that ended after being paid is still one the
    // customer paid, and reading that off a list of statuses is how a screen starts lying.
    const paidDeposit = b.depositPaid;
    return [
      {
        k: `Rental · ${b.pricing.days} × ${b.pricing.dailyRate.amount} ${cur}`,
        v: `${b.pricing.rentalTotal.amount}`,
      },
      { k: 'Delivery fee (yours)', v: `${b.pricing.deliveryFee.amount}` },
      { k: 'Security deposit (held per car)', v: `${b.pricing.securityDeposit.amount}` },
      {
        k: `Deposit paid by card (${b.pricing.depositPercent}%)`,
        v: paidDeposit ? `${b.pricing.depositAmount.amount}` : '0',
        hi: live,
      },
      ...(live
        ? [
            {
              k: 'Balance to collect in cash at handover',
              v: `${b.pricing.balanceDue.amount}`,
              hi: true,
            },
          ]
        : settling
          ? [{ k: 'Balance collected in cash at handover', v: `${b.pricing.balanceDue.amount}` }]
          : [{ k: 'Deposit', v: 'Held pending settlement — see the penalty panel', dim: true }]),
      {
        k: `Platform commission · ${b.terms.commissionPercent}% (frozen on this booking)`,
        // Computed by the API at the frozen rate; the console never multiplies money.
        v: `−${b.commissionAmount.amount}`,
      },
      { k: 'Net payout', v: 'Not available yet', dim: true },
    ];
  });

  protected readonly timeline = computed<readonly TimelineStep[]>(() => {
    const b = this.booking();
    if (!b) return [];
    const done = b.history.map((change) => ({
      label: this.stepLabel(change.toStatus),
      meta: `${this.dateTime(change.occurredAt)} · ${this.actor(change.actorParty, change.actorUserId)}${change.reason ? ` · “${change.reason}”` : ''}`,
      tone: (change.toStatus === 'Rejected' ||
      change.toStatus === 'Cancelled' ||
      change.toStatus === 'NoShow'
        ? 'bad'
        : change.toStatus === 'Requested'
          ? 'warn'
          : 'ok') as Tone,
    }));
    const future: TimelineStep[] = [];
    if (b.status === 'Requested')
      future.push({
        label: 'Approved / rejected',
        meta: `Your answer, before ${this.dateTime(b.decisionDeadline)}`,
        tone: 'dim',
        future: true,
      });
    // The step between the two that did not exist before: the customer's deposit, on their own
    // clock, which is what turns an approval into a rental.
    if (b.status === 'Approved' && b.paymentDeadline)
      future.push({
        label: 'Deposit paid',
        meta: `The customer pays by ${this.dateTime(b.paymentDeadline)}`,
        tone: 'dim',
        future: true,
      });
    if (b.status === 'Requested' || b.status === 'Approved' || b.status === 'Confirmed')
      future.push({
        label: 'Pickup',
        meta: `Scheduled ${this.dateTime(b.periodStart)}`,
        tone: 'dim',
        future: true,
      });
    if (['Requested', 'Approved', 'Confirmed', 'PickedUp'].includes(b.status))
      future.push({
        label: 'Return',
        meta: `Scheduled ${this.dateTime(b.periodEnd)}`,
        tone: 'dim',
        future: true,
      });
    if (!b.isTerminal)
      future.push({
        label: 'Completed',
        meta:
          b.status === 'Returned'
            ? `After the ${b.terms.postReturnSettlementWindowHours}h settlement window`
            : '—',
        tone: 'dim',
        future: true,
      });
    return [...done, ...future];
  });

  // `canDecideBookings` is `ApprovedDealerStaff` — an employee decides requests, that being their
  // default permission (spec 4.2). Read from the permissions table rather than re-deriving
  // `canTrade` here, so the one place the API's rule is written down stays the only place.
  protected readonly canDecide = computed(
    () =>
      this.booking()?.status === 'Requested' &&
      this.console.permissions()?.canDecideBookings === true,
  );
  // Not Approved: the deposit has to have cleared before a car leaves the lot.
  protected readonly canPickUp = computed(() => this.booking()?.status === 'Confirmed');
  protected readonly canReturn = computed(() => this.booking()?.status === 'PickedUp');
  protected readonly canDispute = computed(
    () => !!this.booking()?.canBeDisputed && !this.booking()?.liveDisputeId,
  );

  protected approve(): void {
    const b = this.booking();
    if (b)
      this.decisions.approve(b.bookingId, b.reference, b.customerName, () =>
        this.service.refresh(),
      );
  }

  protected reject(): void {
    const b = this.booking();
    if (b) this.decisions.reject(b.bookingId, b.reference, () => this.service.refresh());
  }

  protected pickUp(): void {
    const b = this.booking();
    if (b)
      this.decisions.recordPickup(b.bookingId, b.reference, this.carName(b), () =>
        this.service.refresh(),
      );
  }

  protected takeBack(): void {
    const b = this.booking();
    if (b)
      this.decisions.recordReturn(b.bookingId, b.reference, this.carName(b), () =>
        this.service.refresh(),
      );
  }

  protected async attachEvidence(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const b = this.booking();
    if (!file || !b) return;
    this.disputeBusy.set(true);
    try {
      const key = await this.disputes.uploadEvidence(b.bookingId, file);
      this.evidenceKeys.update((keys) => [...keys, key]);
      this.evidenceNames.update((names) => [...names, file.name]);
    } catch (error) {
      this.problem.set(describe(error));
    } finally {
      this.disputeBusy.set(false);
      input.value = '';
    }
  }

  protected async openDispute(): Promise<void> {
    const b = this.booking();
    const reason = this.disputeReason().trim();
    if (!b || !reason || this.disputeBusy()) return;
    this.disputeBusy.set(true);
    this.problem.set(null);
    try {
      const ticket = await this.disputes.open(b.bookingId, reason, this.evidenceKeys());
      this.ui.showToast(
        'Dispute opened',
        `The platform will answer within ${this.slaHours(ticket.openedAt, ticket.slaDeadline)} hours.`,
      );
      await this.router.navigate(['/dealer/disputes', ticket.ticketId]);
    } catch (error) {
      this.problem.set(describe(error));
    } finally {
      this.disputeBusy.set(false);
    }
  }

  protected fieldValue(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  protected reload(): void {
    this.service.refresh();
  }

  protected initials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  protected carName(b: Booking): string {
    return b.vehicle ? `${b.vehicle.make} ${b.vehicle.model} ${b.vehicle.year}` : 'the vehicle';
  }

  protected dateTime(iso: string): string {
    return new Date(iso).toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  private slaHours(from: string, to: string): number {
    return Math.round((Date.parse(to) - Date.parse(from)) / 3_600_000);
  }

  private stepLabel(status: string): string {
    return (
      {
        Requested: 'Requested · awaiting your answer',
        Approved: 'Approved · awaiting the deposit',
        Confirmed: 'Deposit paid · booking confirmed',
        Rejected: 'Rejected',
        PickedUp: 'Picked up',
        Returned: 'Returned',
        Completed: 'Completed',
        Cancelled: 'Cancelled',
        NoShow: 'No-show',
        Expired: 'Expired',
      }[status] ?? status
    );
  }

  private actor(party: string, userId: string | null): string {
    if (party === 'Dealer') return userId ? 'by your staff' : 'by your dealership';
    if (party === 'Customer') return 'by the customer';
    return 'by the platform';
  }
}

function describe(error: unknown): string {
  const problem = error as { status?: number; error?: { code?: string; title?: string } };
  switch (problem.error?.code) {
    case 'dispute.booking_not_disputable':
      return 'This booking cannot be disputed: it has not finished, or its dispute window has closed.';
    case 'dispute.already_open':
      return 'A dispute is already open on this booking.';
    case 'dispute.invalid_evidence_type':
      return 'Evidence must be a photo or a PDF.';
    default:
      return problem.error?.title ?? 'The service did not respond. Nothing has been changed.';
  }
}
