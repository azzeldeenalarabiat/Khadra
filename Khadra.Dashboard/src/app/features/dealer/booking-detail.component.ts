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
import { Booking, PenaltyAssessment, depositRefundKey } from '../../core/models/bookings.api';
import { enumKey } from '../../core/i18n/status-key';
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
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { MoneyPipe } from '../../shared/money.pipe';
import { toRenterDocumentsPanel } from './renter-documents.presenter';

/**
 * The history steps this screen words as more than a status's name: who is waiting on whom, the
 * same descriptions the activity screen gives them. Every other status goes through `statusLabel` in
 * the rental office's own wording, so a status the domain adds later still reads as words.
 */
const STEP_DESCRIPTIONS: Readonly<Record<string, TranslationKey>> = {
  Requested: 'dealerBooking.requestedAwaitingYourAnswer',
  Approved: 'dealerBooking.approvedAwaitingTheDeposit',
  Confirmed: 'dealerBooking.depositPaidBookingConfirmed',
  // The handover itself, not the queue's word for the rental it starts ("Active").
  PickedUp: 'status.pickedUp',
};

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
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  protected readonly statusLabel = this.i18n.statusLabel;
  protected readonly enumLabel = this.i18n.enumLabel;
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
  /** The last refusal, held as facts: its words are chosen below, so a language switch re-words it. */
  protected readonly problem = signal<ProblemSnapshot | null>(null);
  protected readonly problemText = computed(() => {
    const problem = this.problem();
    return problem ? describe(problem, this.t, this.i18n.lang()) : null;
  });

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
   * Some are deliberately not the server's own word, because the server names a state and a gallery
   * wants to know what is being asked OF THEM: `Approved` means "waiting for their money", `PickedUp`
   * means "the car is out". Those live in the dictionary under the `dealerBooking` scope, the same
   * wording the bookings list uses; everything else falls through to the booking and then the plain
   * word, so a status this console has never heard of still reads as words.
   */
  protected readonly label = computed(() => {
    const b = this.booking();
    if (!b) return '';
    // A live dispute is a flag on the booking, not one of its statuses, so it is worded here.
    if (b.liveDisputeId) return this.t('status.disputed');
    return this.statusLabel(b.status, 'dealerBooking');
  });

  protected readonly meta = computed(() => {
    const b = this.booking();
    if (!b) return '';
    const method =
      b.pickupMethod === 'Delivery'
        ? this.t('common.delivery')
        : this.t('dealerBooking.pickupAtYourLocation');
    // A list of whole facts, each worded on its own; the days are the server's frozen count.
    return [
      this.t('dealerBooking.requestedAt', { when: this.dateTime(b.requestedAt ?? b.createdAt) }),
      this.customerName(b),
      this.t('booking.days', { count: b.pricing.days }),
      method,
    ].join(' · ');
  });

  /** The customer's name, or the fact that the account was closed. Never the English stand-in. */
  protected customerName(b: Booking): string {
    return b.customerAccountClosed ? this.t('common.customerAccountClosed') : b.customerName;
  }

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
    // A request the office can still answer is the SERVER's call (isAwaitingDecision); this clock can
    // only close it early, never keep a dead one open. "2h remaining", then "Expired".
    const reading = this.format.deadline(b.decisionDeadline, !b.isAwaitingDecision);
    if (reading.passed)
      return { figure: reading.text, note: this.t('dealerBooking.theAnswerWindowHasClosed') };
    return {
      figure: reading.text,
      note: this.t('dealerBooking.expiresAt', { when: this.dateTime(b.decisionDeadline) }),
    };
  });

  protected readonly customerRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    // Only what the API carries. NO CONTACT DETAILS: the platform has not decided whether a dealer
    // ever sees a customer's phone, and the console must not promise it. The history below is a
    // separate, deliberately narrower thing -- aggregates the platform itself counted.
    return [{ k: this.t('dealerSettings.name'), v: this.customerName(b) }];
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

  /** Which document is being recorded right now, so its own button can say so and be disabled. */
  protected readonly reviewingDocumentId = signal<string | null>(null);

  /**
   * Records that this dealership checked one document.
   *
   * Nothing about the reviewer or the time is sent: the server takes both from the validated token
   * and its own clock. The guard here is only against a double click producing two requests — the
   * server is idempotent anyway, and answers the second with the first review, timestamp intact.
   */
  protected async markReviewed(documentId: string): Promise<void> {
    const b = this.booking();
    if (!b || this.reviewingDocumentId()) return;

    this.reviewingDocumentId.set(documentId);
    this.problem.set(null);
    try {
      await this.service.reviewRenterDocument(b.bookingId, documentId);
      // Re-read rather than patching a local copy: the review that now stands is the SERVER's, and
      // on a repeat that is the original one with its original timestamp.
      this.service.renterDocuments.reload();
      this.ui.showToast(this.t('renterDocs.reviewedByDealer'), this.t('renterDocs.reviewSaved'));
    } catch (error: unknown) {
      // A 409 here means the window closed between the listing and the click. The panel's own state
      // will say so on the next load; this line is for everything else.
      this.problem.set(snapshotProblem(error));
    } finally {
      this.reviewingDocumentId.set(null);
    }
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
      this.ui.showToast(this.t('dealerBooking.rateCustomer'), this.t('dealerBooking.rateSaved'));
    } catch (error: unknown) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.ratingBusy.set(false);
    }
  }

  protected readonly vehicleRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    if (!b.vehicle)
      return [
        {
          k: this.t('dealerBooking.vehicle'),
          v: this.t('dealerBooking.noLongerListed'),
          tone: 'dim',
        },
      ];
    return [
      {
        k: this.t('dealerBooking.vehicle'),
        v: `${b.vehicle.make} ${b.vehicle.model} ${b.vehicle.year}`,
      },
      { k: this.t('dealerBooking.plate'), v: b.vehicle.plateNumber },
      { k: this.t('common.colour'), v: b.vehicle.color ?? '—' },
      {
        k: this.t('dealerBooking.dailyPriceOnThis'),
        v: this.format.money(b.pricing.dailyRate.amount, b.pricing.dailyRate.currency),
      },
    ];
  });

  protected readonly rentalRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    const excessFee = b.pricing.mileageExcessFeePerKm;
    return [
      { k: this.t('dealerBooking.start'), v: this.dateTime(b.periodStart) },
      { k: this.t('dealerBooking.end'), v: this.dateTime(b.periodEnd) },
      // The server's frozen day count, as a plural message: Arabic has six forms of "day".
      {
        k: this.t('dealerBooking.duration'),
        v: this.t('booking.days', { count: b.pricing.days }),
      },
      {
        k: this.t('vehicleWizard.mileage'),
        v: b.pricing.mileageUnlimited
          ? this.t('vehicleWizard.unlimited')
          : this.t('dealerBooking.mileageAllowance', {
              limit: this.format.number(b.pricing.mileageDailyLimitKm),
              fee: excessFee
                ? this.format.money(excessFee.amount, excessFee.currency)
                : this.format.money(0, b.pricing.dailyRate.currency),
            }),
      },
      {
        k: this.t('common.fuel'),
        v:
          b.pricing.fuelPolicy === 'FullToFull'
            ? this.t('vehicleWizard.fullToFull')
            : this.t('vehicleWizard.sameToSame'),
      },
    ];
  });

  protected readonly pickupRows = computed<readonly KeyValue[]>(() => {
    const b = this.booking();
    if (!b) return [];
    if (b.pickupMethod !== 'Delivery' || !b.deliveryLocation) {
      return [
        { k: this.t('dealerBooking.method'), v: this.t('dealerBooking.collectedFromYourLocation') },
      ];
    }
    return [
      { k: this.t('dealerBooking.method'), v: this.t('common.delivery') },
      {
        k: this.t('dealerProfile.location'),
        v: this.format.coordinates(b.deliveryLocation.latitude, b.deliveryLocation.longitude),
      },
      {
        k: this.t('vehicleWizard.deliveryFee'),
        v: this.t('dealerBooking.amountFrozen', {
          amount: this.format.money(b.pricing.deliveryFee.amount, b.pricing.deliveryFee.currency),
        }),
      },
    ];
  });

  /**
   * The money on THIS booking, as frozen when it was made.
   *
   * Every amount carries its own currency code and goes through `FormatService`, so it prints at the
   * currency's scale and never as "−0". The commission is the amount Khadra charges, unsigned: a sign
   * typed in front of it printed "−0" when the frozen rate was zero, and Arabic bidi then carried that
   * sign to the far end of the figure.
   */
  protected readonly moneyRows = computed(() => {
    const b = this.booking();
    if (!b) return [];
    const money = (value: { readonly amount: number; readonly currency: string }): string =>
      this.format.money(value.amount, value.currency);
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
        k: this.t('dealerBooking.rentalLine', {
          count: b.pricing.days,
          rate: money(b.pricing.dailyRate),
        }),
        v: money(b.pricing.rentalTotal),
      },
      { k: this.t('dealerBooking.deliveryFeeYours'), v: money(b.pricing.deliveryFee) },
      {
        k: this.t('dealerBooking.securityDepositHeldPer'),
        v: money(b.pricing.securityDeposit),
      },
      {
        k: this.t('dealerBooking.depositPaidByCard', {
          percent: this.format.percent(b.pricing.depositPercent),
        }),
        // Nothing is paid until the server says the deposit cleared: zero, in the deposit's currency.
        v: paidDeposit
          ? money(b.pricing.depositAmount)
          : this.format.money(0, b.pricing.depositAmount.currency),
        hi: live,
      },
      ...(live
        ? [
            {
              k: this.t('dealerBooking.balanceToCollectIn'),
              v: money(b.pricing.balanceDue),
              hi: true,
            },
          ]
        : settling
          ? [
              {
                k: this.t('dealerBooking.balanceCollectedInCash'),
                v: money(b.pricing.balanceDue),
              },
            ]
          : b.depositRefund
            ? [
                {
                  // The customer cancelled inside the free window after paying: the whole deposit
                  // went back to them, so nothing of it is held for anyone to settle.
                  k: this.t('common.deposit'),
                  v: this.t(depositRefundKey(b.depositRefund), { amount: money(b.depositRefund.amount) }),
                },
              ]
            : [
                {
                  k: this.t('common.deposit'),
                  v: this.t('dealerBooking.heldPendingSettlementSee'),
                  dim: true,
                },
              ]),
      {
        k: this.t('dealerBooking.platformCommissionFrozen', {
          percent: this.format.percent(b.terms.commissionPercent),
        }),
        // Unsigned, and computed by the API at the frozen rate; the console never multiplies money.
        v: money(b.commissionAmount),
      },
      {
        k: this.t('dealerReports.netPayout'),
        v: this.t('dealerReports.notAvailableYet'),
        dim: true,
      },
    ];
  });

  protected readonly timeline = computed<readonly TimelineStep[]>(() => {
    const b = this.booking();
    if (!b) return [];
    const done = b.history.map((change) => ({
      label: this.stepLabel(change.toStatus),
      // Independent facts, each whole: when, who, and the reason exactly as somebody typed it —
      // quoted, never translated.
      meta: [
        this.dateTime(change.occurredAt),
        this.actor(change.actorParty, change.actorUserId),
        ...(change.reason ? [this.t('disputeDetail.quoted', { text: change.reason })] : []),
      ].join(' · '),
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
        label: this.t('dealerBooking.approvedOrRejected'),
        meta: this.t('dealerBooking.yourAnswerBefore', { when: this.dateTime(b.decisionDeadline) }),
        tone: 'dim',
        future: true,
      });
    // The step between the two that did not exist before: the customer's deposit, on their own
    // clock, which is what turns an approval into a rental.
    if (b.status === 'Approved' && b.paymentDeadline)
      future.push({
        label: this.t('dealerBooking.depositPaid'),
        meta: this.t('dealerBooking.customerPaysBy', { when: this.dateTime(b.paymentDeadline) }),
        tone: 'dim',
        future: true,
      });
    if (b.status === 'Requested' || b.status === 'Approved' || b.status === 'Confirmed')
      future.push({
        label: this.t('dealerBooking.pickup'),
        meta: this.t('dealerBooking.scheduledFor', { when: this.dateTime(b.periodStart) }),
        tone: 'dim',
        future: true,
      });
    if (['Requested', 'Approved', 'Confirmed', 'PickedUp'].includes(b.status))
      future.push({
        label: this.t('dealerBooking.return'),
        meta: this.t('dealerBooking.scheduledFor', { when: this.dateTime(b.periodEnd) }),
        tone: 'dim',
        future: true,
      });
    if (!b.isTerminal)
      future.push({
        label: this.t('status.completed'),
        // The window frozen on this booking, as a plural message: Arabic words "hours" six ways.
        meta:
          b.status === 'Returned'
            ? this.t('dealerBooking.afterSettlementWindow', {
                count: b.terms.postReturnSettlementWindowHours,
              })
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
      this.decisions.approve(b.bookingId, b.reference, this.customerName(b), () =>
        this.service.refresh(),
      );
  }

  protected reject(): void {
    const b = this.booking();
    if (b) this.decisions.reject(b.bookingId, b.reference, () => this.service.refresh());
  }

  // The cash taken at a handover is recorded in the booking's own currency, so that is the code the
  // dialog names beside the figure.
  protected pickUp(): void {
    const b = this.booking();
    if (b)
      this.decisions.recordPickup(
        b.bookingId,
        b.reference,
        this.carName(b),
        b.pricing.totalPrice.currency,
        () => this.service.refresh(),
      );
  }

  protected takeBack(): void {
    const b = this.booking();
    if (b)
      this.decisions.recordReturn(
        b.bookingId,
        b.reference,
        this.carName(b),
        b.pricing.totalPrice.currency,
        () => this.service.refresh(),
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
      this.problem.set(snapshotProblem(error));
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
        // The window the platform promised on THIS ticket, from the deadline the server froze on it
        // when it was opened. The booking carries no such figure, so nothing earlier can name it.
        this.t('dealerBooking.platformWillAnswerWithin', {
          count: this.slaHours(ticket.openedAt, ticket.slaDeadline),
        }),
      );
      await this.router.navigate(['/dealer/disputes', ticket.ticketId]);
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.disputeBusy.set(false);
    }
  }

  protected fieldValue(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  /**
   * Why the platform assessed this penalty, in the reader's language.
   *
   * The stable code is what gets worded. A booking assessed before codes existed carries only the
   * frozen English sentence: that is shown exactly as it was written, never guessed at from the text,
   * and marked as a Latin run so Arabic does not reorder it.
   */
  protected penaltyReasonText(penalty: PenaltyAssessment): string {
    const key = penalty.reasonCode ? enumKey('penaltyReason', penalty.reasonCode) : null;
    return key ? this.t(key) : penalty.reason;
  }

  /** True when the sentence on screen is the frozen English one rather than a worded code. */
  protected penaltyReasonIsFrozen(penalty: PenaltyAssessment): boolean {
    return !(penalty.reasonCode && enumKey('penaltyReason', penalty.reasonCode));
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
    return b.vehicle
      ? `${b.vehicle.make} ${b.vehicle.model} ${b.vehicle.year}`
      : this.t('dealerBooking.theVehicle');
  }

  /** "06 Sept 2026, 14:32", in the reader's language. */
  protected dateTime(iso: string): string {
    return this.format.dateTime(iso);
  }

  /** A plain figure (an odometer reading, a fuel level), or "—" when none was recorded. */
  protected number(value: number | null): string {
    return this.format.number(value);
  }

  /** "20%", in the reader's language. */
  protected percent(value: number): string {
    return this.format.percent(value);
  }

  /**
   * What the penalty would come to: its range, or its single amount, as one isolated run in the
   * assessment's own currency. Two amounts around a dash would be laid out upper bound first under
   * Arabic.
   */
  protected readonly penaltyAmount = computed(() => {
    const penalty = this.booking()?.penalty;
    if (!penalty) return '';
    return penalty.isRange
      ? this.format.moneyRange(
          penalty.minAmount.amount,
          penalty.maxAmount.amount,
          penalty.minAmount.currency,
        )
      : this.format.money(penalty.minAmount.amount, penalty.minAmount.currency);
  });

  private slaHours(from: string, to: string): number {
    return Math.round((Date.parse(to) - Date.parse(from)) / 3_600_000);
  }

  private stepLabel(status: string): string {
    const key = STEP_DESCRIPTIONS[status];
    // The office's own wording for everything else, the same the bookings list uses.
    return key ? this.t(key) : this.statusLabel(status, 'dealerBooking');
  }

  private actor(party: string, userId: string | null): string {
    switch (party) {
      case 'Dealer':
        return userId
          ? this.t('dealerBooking.byYourStaff')
          : this.t('dealerBooking.byYourDealership');
      case 'Customer':
        return this.t('dealerBooking.byTheCustomer');
      case 'System':
      case 'Admin':
        return this.t('dealerBooking.byThePlatform');
      default:
        // A party this screen has no sentence for is named, never passed off as the platform.
        return this.enumLabel('party', party);
    }
  }
}

/**
 * A server refusal, in the reader's own language, worded when it is shown.
 *
 * Takes `t` and the language rather than reaching for them: this is a module function, outside the
 * class, so it has no `this` and no injector. The mapping is from the server's stable error CODE to
 * a sentence, which is the only shape that can be translated at all. The server's own `title` is
 * English: the last resort, and only while the console is English.
 */
function describe(
  problem: ProblemSnapshot,
  t: (key: TranslationKey) => string,
  language: Language,
): string {
  switch (problem.code) {
    case 'dispute.booking_not_disputable':
      return t('dealerBooking.thisBookingCannotBe');
    case 'dispute.already_open':
      return t('dealerBooking.aDisputeIsAlready');
    case 'dispute.invalid_evidence_type':
      return t('dealerBooking.evidenceMustBeA');
    // The window closed between the listing and the click. The panel says the same thing on its next
    // load; this is what the gallery reads in the meantime.
    case 'booking.renter_documents_not_available':
      return t('renterDocs.closed');
    case 'documents.not_found':
    case 'booking.not_found':
      return t('renterDocs.reviewFailed');
    default:
      return serverSentence(problem, language, t) ?? t('dealerDelivery.serviceDidNotRespond');
  }
}
