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
import { KeyValue, TimelineStep, Tone, toneClass } from '../../core/models/console.models';
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
import { FormatService } from '../../core/i18n/format.service';
import { TranslationKey } from '../../core/i18n/en';
import { MoneyPipe } from '../../shared/money.pipe';
import { toRenterDocumentsPanel } from './renter-documents.presenter';

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
  protected readonly statusLabel = inject(I18nService).statusLabel;
  private readonly format = inject(FormatService);
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
    // Moving to another booking puts every open document away. A passport left on screen from the
    // previous customer is the kind of leak nobody notices until it is in front of the wrong person.
    effect(() => {
      this.bookingId();
      this.revealed.set(new Set());
      this.brokenPreviews.set(new Set());
    });
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
    if (error.status === 404) return this.t('dealerBooking.thatBookingIsNot');
    return this.t('dealerBooking.theBookingCouldNot');
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

  /**
   * What this booking's state is called ON THE DEALER'S SCREEN.
   *
   * Four of them are deliberately not the server's own word, because the server names a state and a
   * gallery wants to know what is being asked OF THEM: `Approved` means "waiting for their money",
   * `PickedUp` means "the car is out". Everything else falls through to the shared helper, so a
   * status this console has never heard of still reads as words rather than as a raw enum name.
   */
  protected readonly label = computed(() => {
    const b = this.booking();
    if (!b) return '';
    if (b.liveDisputeId) return this.statusLabel('Disputed');
    const labels: Partial<Record<Booking['status'], string>> = {
      Requested: this.t('status.pendingDealer'),
      Approved: this.t('status.awaitingDeposit'),
      PickedUp: this.t('status.activeRental'),
      NoShow: this.t('status.noShow'),
    };
    return labels[b.status] ?? this.statusLabel(b.status, 'booking');
  });

  protected readonly meta = computed(() => {
    const b = this.booking();
    if (!b) return '';
    const method = b.pickupMethod === 'Delivery' ? 'delivery' : this.t('dealerBooking.pickupAtYourLocation');
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
    return [{ k: this.t('dealerSettings.name'), v: b.customerName }];
  });

  /**
   * What the platform knows about this customer, or null.
   *
   * Null covers two different situations that look the same on screen and are both correct: the
   * booking is no longer live, so the gallery's access has ended (409 from the server), or the
   * request has not landed yet. Neither is an error to show.
   */
  // Through `loaded`, not `value() ?? null`. The `??` never runs: `Resource.value()` THROWS in the
  // error state rather than returning undefined, and the error state is this panel's normal answer
  // once the booking stops being live. Read raw, `reputationClosed` threw during change detection
  // and took the whole screen with it — the failure `loaded()` was written for.
  protected readonly reputation = loaded(this.service.reputation);

  /** True when the access rule -- not a failure -- is why there is nothing to show. */
  protected readonly reputationClosed = computed(
    () => this.service.reputation.status() === 'error' && !this.reputation(),
  );

  protected readonly myCustomerRating = loaded(this.service.customerRating);

  // ── The renter's identity papers (spec 5.1, pre-launch item 63) ───────────────────────────────

  /**
   * What the documents panel is showing.
   *
   * Derived from the resource's OWN status and status code, never from "did the request fail". The
   * server has two failures here that mean opposite things: 409 is the access rule working — the
   * booking is no longer live and the window has closed — and anything else is a fault the gallery
   * should retry. Rendering both as "the service did not respond" is the mistake the frontend rules
   * describe, and here it would tell a gallery the platform is broken when it is behaving exactly as
   * designed.
   */
  private readonly renterDocumentsValue = loaded(this.service.renterDocuments);

  protected readonly renterDocuments = computed(() =>
    toRenterDocumentsPanel(
      this.service.renterDocuments.status(),
      // Through `loaded`, NEVER `value()` directly. `Resource.value()` throws in the error state, and
      // 409 — a booking that is no longer live — is this panel's most ordinary answer. Read raw, the
      // computed would throw during change detection on every Returned, Completed, Cancelled,
      // Expired, Rejected and NoShow booking, taking the whole detail screen down with it rather
      // than showing the one sentence it was written to show.
      this.renterDocumentsValue() ?? undefined,
      (this.service.renterDocuments.error() as { error?: { code?: string } } | undefined)?.error
        ?.code,
      this.bookingId() ?? '',
      (bookingId, documentId) => this.service.renterDocumentUrl(bookingId, documentId),
      this.t,
      (iso) => this.format.dateTime(iso),
    ),
  );

  /**
   * Which documents are on screen right now.
   *
   * Nothing is fetched until the gallery asks. These are photographs of a named private
   * individual's passport and licence, and a booking screen that loaded them unbidden would put
   * them in front of everyone who opens the page and everyone standing behind them — including on
   * bookings nobody is handing over today.
   */
  private readonly revealed = signal<ReadonlySet<string>>(new Set());

  /** Ids whose image the browser could not load, so the tile says so instead of showing a gap. */
  protected readonly brokenPreviews = signal<ReadonlySet<string>>(new Set());

  /** The tiles, or none. Read separately so the template never narrows the union itself. */
  protected readonly renterTiles = computed(() => {
    const panel = this.renterDocuments();
    return panel.kind === 'ready' ? panel.tiles : [];
  });

  /** The sentence naming what the renter has not filed, or null when nothing is outstanding. */
  protected readonly renterMissing = computed(() => {
    const panel = this.renterDocuments();
    return panel.kind === 'ready' ? panel.missing : null;
  });

  protected readonly closedNote = computed(() => {
    const panel = this.renterDocuments();
    return panel.kind === 'closed' ? panel.note : '';
  });

  protected readonly failedNote = computed(() => {
    const panel = this.renterDocuments();
    return panel.kind === 'failed' ? panel.note : '';
  });

  protected readonly toneClass = toneClass;

  protected isRevealed(documentId: string): boolean {
    return this.revealed().has(documentId);
  }

  protected isBroken(documentId: string): boolean {
    return this.brokenPreviews().has(documentId);
  }

  /** True once anything is showing, so the section-level control can offer to put it away again. */
  protected readonly anyRevealed = computed(() => this.revealed().size > 0);

  protected toggleDocument(documentId: string): void {
    this.revealed.update((shown) => {
      const next = new Set(shown);
      if (!next.delete(documentId)) next.add(documentId);
      return next;
    });
    this.brokenPreviews.update((broken) => {
      const next = new Set(broken);
      next.delete(documentId);
      return next;
    });
  }

  /**
   * The licence check itself: both sides at once, which is what spec 5.1 asks the gallery to do.
   *
   * Pressed again, it puts EVERYTHING away rather than only the licence — a gallery that has finished
   * with the counter wants the screen clear, and leaving a passport open because it was revealed by a
   * different button is not a distinction worth defending in front of a customer.
   */
  protected toggleLicence(): void {
    if (this.anyRevealed()) {
      this.revealed.set(new Set());
      this.brokenPreviews.set(new Set());
      return;
    }

    const panel = this.renterDocuments();
    if (panel.kind !== 'ready') return;
    this.revealed.set(
      new Set(panel.tiles.filter((tile) => tile.isLicence).map((tile) => tile.documentId)),
    );
  }

  protected previewFailed(documentId: string): void {
    this.brokenPreviews.update((broken) => new Set(broken).add(documentId));
  }

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
      this.problem.set(describe(error, this.t));
    } finally {
      this.ratingBusy.set(false);
    }
  }

  protected readonly vehicleRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    if (!b.vehicle) return [{ k: this.t('dealerBooking.vehicle'), v: this.t('dealerBooking.noLongerListed'), tone: 'dim' }];
    return [
      { k: this.t('dealerBooking.vehicle'), v: `${b.vehicle.make} ${b.vehicle.model} ${b.vehicle.year}` },
      { k: this.t('dealerBooking.plate'), v: b.vehicle.plateNumber },
      { k: this.t('common.colour'), v: b.vehicle.color ?? '—' },
      {
        k: this.t('dealerBooking.dailyPriceOnThis'),
        v: `${b.pricing.dailyRate.amount} ${b.pricing.dailyRate.currency}`,
      },
    ];
  });

  protected readonly rentalRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    return [
      { k: this.t('dealerBooking.start'), v: this.dateTime(b.periodStart) },
      { k: this.t('dealerBooking.end'), v: this.dateTime(b.periodEnd) },
      { k: this.t('dealerBooking.duration'), v: `${b.pricing.days} ${b.pricing.days === 1 ? 'day' : 'days'}` },
      {
        k: this.t('vehicleWizard.mileage'),
        v: b.pricing.mileageUnlimited
          ? 'Unlimited'
          : `${b.pricing.mileageDailyLimitKm} km/day, ${b.pricing.mileageExcessFeePerKm?.amount ?? 0} ${b.pricing.dailyRate.currency}/km over`,
      },
      { k: this.t('common.fuel'), v: b.pricing.fuelPolicy === 'FullToFull' ? this.t('vehicleWizard.fullToFull') : this.t('vehicleWizard.sameToSame') },
    ];
  });

  protected readonly pickupRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    if (b.pickupMethod !== 'Delivery' || !b.deliveryLocation) {
      return [{ k: this.t('dealerBooking.method'), v: this.t('dealerBooking.collectedFromYourLocation') }];
    }
    return [
      { k: this.t('dealerBooking.method'), v: 'Delivery' },
      {
        k: this.t('dealerProfile.location'),
        v: `${b.deliveryLocation.latitude.toFixed(4)}, ${b.deliveryLocation.longitude.toFixed(4)}`,
      },
      {
        k: this.t('vehicleWizard.deliveryFee'),
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
      { k: this.t('dealerBooking.deliveryFeeYours'), v: `${b.pricing.deliveryFee.amount}` },
      { k: this.t('dealerBooking.securityDepositHeldPer'), v: `${b.pricing.securityDeposit.amount}` },
      {
        k: `Deposit paid by card (${b.pricing.depositPercent}%)`,
        v: paidDeposit ? `${b.pricing.depositAmount.amount}` : '0',
        hi: live,
      },
      ...(live
        ? [
            {
              k: this.t('dealerBooking.balanceToCollectIn'),
              v: `${b.pricing.balanceDue.amount}`,
              hi: true,
            },
          ]
        : settling
          ? [{ k: this.t('dealerBooking.balanceCollectedInCash'), v: `${b.pricing.balanceDue.amount}` }]
          : [{ k: this.t('common.deposit'), v: this.t('dealerBooking.heldPendingSettlementSee'), dim: true }]),
      {
        k: `Platform commission · ${b.terms.commissionPercent}% (frozen on this booking)`,
        // Computed by the API at the frozen rate; the console never multiplies money.
        v: `−${b.commissionAmount.amount}`,
      },
      { k: this.t('dealerReports.netPayout'), v: this.t('dealerReports.notAvailableYet'), dim: true },
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
        label: this.t('dealerBooking.depositPaid'),
        meta: `The customer pays by ${this.dateTime(b.paymentDeadline)}`,
        tone: 'dim',
        future: true,
      });
    if (b.status === 'Requested' || b.status === 'Approved' || b.status === 'Confirmed')
      future.push({
        label: this.t('dealerBooking.pickup'),
        meta: `Scheduled ${this.dateTime(b.periodStart)}`,
        tone: 'dim',
        future: true,
      });
    if (['Requested', 'Approved', 'Confirmed', 'PickedUp'].includes(b.status))
      future.push({
        label: this.t('dealerBooking.return'),
        meta: `Scheduled ${this.dateTime(b.periodEnd)}`,
        tone: 'dim',
        future: true,
      });
    if (!b.isTerminal)
      future.push({
        label: this.t('status.completed'),
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
      this.problem.set(describe(error, this.t));
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
        this.t('dealerBooking.disputeOpened'),
        `The platform will answer within ${this.slaHours(ticket.openedAt, ticket.slaDeadline)} hours.`,
      );
      await this.router.navigate(['/dealer/disputes', ticket.ticketId]);
    } catch (error) {
      this.problem.set(describe(error, this.t));
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
    return b.vehicle ? `${b.vehicle.make} ${b.vehicle.model} ${b.vehicle.year}` : this.t('dealerBooking.theVehicle');
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
        Requested: this.t('dealerBooking.requestedAwaitingYourAnswer'),
        Approved: this.t('dealerBooking.approvedAwaitingTheDeposit'),
        Confirmed: this.t('dealerBooking.depositPaidBookingConfirmed'),
        Rejected: 'Rejected',
        PickedUp: this.t('status.pickedUp'),
        Returned: 'Returned',
        Completed: 'Completed',
        Cancelled: 'Cancelled',
        NoShow: 'No-show',
        Expired: 'Expired',
      }[status] ?? status
    );
  }

  private actor(party: string, userId: string | null): string {
    if (party === 'Dealer') return userId ? this.t('dealerBooking.byYourStaff') : this.t('dealerBooking.byYourDealership');
    if (party === 'Customer') return this.t('dealerBooking.byTheCustomer');
    return this.t('dealerBooking.byThePlatform');
  }
}

/**
 * A server refusal, in the reader's own language.
 *
 * Takes `t` rather than reaching for one: this is a module function, outside the class, so it has no
 * `this` and no injector. Passing it in also keeps the mapping honest about what it is -- a lookup
 * from the server's stable error CODE to a sentence, which is the only shape that can be translated
 * at all. The server's own `title` is English and is the last resort.
 */
function describe(error: unknown, t: (key: TranslationKey) => string): string {
  const problem = error as { status?: number; error?: { code?: string; title?: string } };
  switch (problem.error?.code) {
    case 'dispute.booking_not_disputable':
      return t('dealerBooking.thisBookingCannotBe');
    case 'dispute.already_open':
      return t('dealerBooking.aDisputeIsAlready');
    case 'dispute.invalid_evidence_type':
      return t('dealerBooking.evidenceMustBeA');
    default:
      return problem.error?.title ?? t('dealerDelivery.serviceDidNotRespond');
  }
}
