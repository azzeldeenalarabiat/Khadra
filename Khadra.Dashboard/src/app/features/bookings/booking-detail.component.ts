import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import {
  Booking,
  BookingStatus,
  Handover,
  PenaltyAssessment,
  depositRefundKey,
} from '../../core/models/bookings.api';
import { enumKey } from '../../core/i18n/status-key';
import { KeyValue, TimelineStep, Tone } from '../../core/models/console.models';
import { AdminBookingsService } from '../../core/services/admin-bookings.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence, snapshotProblem } from '../../core/i18n/problem';

/**
 * One booking as the platform sees it.
 *
 * Every figure is the one the booking FROZE — its price, its deposit, its commission rate and the
 * whole rulebook it was made under — never today's settings. That is the point of the snapshot, and
 * a screen that recomputed any of it would re-judge a past booking by rules nobody agreed to.
 *
 * The three actions are interventions, not decisions about money: an admin cancellation assesses no
 * penalty against anyone, and the two deadline actions are refused by the server while the booking's
 * own window still has time in it. Money moves only through a dispute resolution.
 */
@Component({
  selector: 'kh-admin-booking-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './booking-detail.component.html',
  imports: [RouterLink, IconComponent, TimelineComponent],
})
export class AdminBookingDetailComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly status = this.i18n.statusLabel;
  /** Server enums that are not statuses: who recorded a handover, who a penalty is against. */
  protected readonly enumLabel = this.i18n.enumLabel;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminBookingsService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);

  private readonly bookingId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('bookingId'))),
    { initialValue: this.route.snapshot.paramMap.get('bookingId') },
  );

  constructor() {
    effect(() => this.service.viewing.set(this.bookingId()));
  }

  protected readonly resource = this.service.booking;
  protected readonly booking = loaded(this.resource);

  /** A failed load, held as the resource's facts and worded here, so a language switch re-words it. */
  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    return describe(snapshotProblem(error), this.t, this.i18n.lang());
  });

  protected readonly tone = computed<Tone>(() => {
    const booking = this.booking();
    if (!booking) return 'dim';
    if (booking.liveDisputeId) return 'bad';
    return STATUS_TONES[booking.status] ?? 'dim';
  });

  /** The server's own word for the state, in the customer's language. */
  protected readonly statusLabel = computed(() => this.status(this.booking()?.status, 'booking'));

  /** What the booking is worth, all of it frozen at the moment it was made. */
  protected readonly moneyRows = computed<readonly KeyValue[]>(() => {
    const booking = this.booking();
    if (!booking) return [];
    const pricing = booking.pricing;
    const rows: KeyValue[] = [
      // The days the booking froze, never a subtraction of its two instants.
      {
        k: this.t('adminBooking.dailyRateForDays', { count: pricing.days }),
        v: this.money(pricing.dailyRate),
      },
      { k: this.t('myBooking.rentalTotal'), v: this.money(pricing.rentalTotal) },
    ];
    // Keyed on the pickup method, not the amount: 0 is now a real answer a gallery can give, and
    // hiding the row would make free delivery indistinguishable from no delivery at all.
    if (booking.pickupMethod === 'Delivery')
      rows.push({ k: this.t('vehicleWizard.deliveryFee'), v: this.money(pricing.deliveryFee) });
    rows.push(
      { k: this.t('myBooking.totalPrice'), v: this.money(pricing.totalPrice) },
      // The percentages come from the booking's own terms, never from the settings in force today.
      {
        k: this.t('adminBooking.depositWithPercent', {
          percent: this.formats.percent(pricing.depositPercent),
        }),
        v: this.money(pricing.depositAmount),
      },
      // A free cancellation's refund, where one exists: the admin sees the same status the customer does.
      ...(booking.depositRefund
        ? [
            {
              k: this.t('adminBooking.depositRefund'),
              v: this.t(depositRefundKey(booking.depositRefund), { amount: this.money(booking.depositRefund.amount) }),
            },
          ]
        : []),
      { k: this.t('myBooking.balanceDue'), v: this.money(pricing.balanceDue) },
      { k: this.t('vehicleDetail.securityDeposit'), v: this.money(pricing.securityDeposit) },
      {
        k: this.t('adminBooking.platformCommissionWithPercent', {
          percent: this.formats.percent(booking.terms.commissionPercent),
        }),
        // Unsigned: the amount Khadra charges, computed by the API at the frozen rate. The label
        // carries the subtraction; a sign typed here once printed "−0" on a zero commission.
        v: this.money(booking.commissionAmount),
      },
    );
    return rows;
  });

  /** The rulebook this booking froze. Version included: it is what makes the rest reproducible. */
  protected readonly termsRows = computed<readonly KeyValue[]>(() => {
    const terms = this.booking()?.terms;
    if (!terms) return [];
    const hours = (count: number): string => this.t('adminBooking.hours', { count });
    return [
      {
        k: this.t('myBooking.freeCancellationWindow'),
        v: hours(terms.freeCancellationWindowHours),
      },
      { k: this.t('myBooking.paymentWindow'), v: hours(terms.paymentWindowHours) },
      { k: this.t('myBooking.noShowTimeout'), v: hours(terms.noShowTimeoutHours) },
      {
        k: this.t('myBooking.settlementWindowAfterReturn'),
        v: hours(terms.postReturnSettlementWindowHours),
      },
      {
        k: this.t('myBooking.customerCancellationPenalty'),
        v: this.formats.percent(terms.customerCancellationPenaltyPercent),
      },
      {
        k: this.t('myBooking.dealerNonDeliveryPenalty'),
        // One run, so Arabic cannot lay the two bounds out upper bound first.
        v: this.formats.percentRange(terms.dealerPenaltyMinPercent, terms.dealerPenaltyMaxPercent),
      },
      { k: this.t('dealerBooking.rulesVersion'), v: String(terms.rulesVersion) },
    ];
  });

  protected readonly partyRows = computed<readonly KeyValue[]>(() => {
    const booking = this.booking();
    if (!booking) return [];
    return [
      { k: this.t('dealersList.colDealer'), v: this.dealerName(booking) },
      { k: this.t('vehicleDetail.customer'), v: this.customerName(booking) },
      {
        k: this.t('dealerBooking.vehicle'),
        v: booking.vehicle
          ? `${booking.vehicle.make} ${booking.vehicle.model} ${booking.vehicle.year} · ${booking.vehicle.plateNumber}`
          : this.t('myBooking.delistedSinceThisBooking'),
      },
      {
        k: this.t('myBooking.handover'),
        v:
          booking.pickupMethod === 'Delivery'
            ? this.t('common.delivery')
            : this.t('myBooking.selfPickup'),
      },
    ];
  });

  /** The dealership's name, or the fact that it has left the platform. Never the English stand-in. */
  private dealerName(booking: Booking): string {
    return booking.dealerRemoved ? this.t('common.dealerNoLongerOnPlatform') : booking.dealerName;
  }

  /** The customer's name, or the fact that the account was closed. Never the English stand-in. */
  private customerName(booking: Booking): string {
    return booking.customerAccountClosed
      ? this.t('common.customerAccountClosed')
      : booking.customerName;
  }

  /**
   * Every status change, in the order it happened, naming who made it.
   *
   * The meta line is independent facts joined by " · ", each worded on its own: when, which party,
   * and the reason recorded with the change. The reason is quoted exactly as it was stored, never
   * translated — it is somebody's words, or the text the platform froze at the time.
   */
  protected readonly timeline = computed<readonly TimelineStep[]>(() => {
    const booking = this.booking();
    if (!booking) return [];
    return booking.history.map((change) => ({
      label: this.status(change.toStatus, 'booking'),
      meta: [
        this.when(change.occurredAt),
        this.enumLabel('party', change.actorParty),
        ...(change.reason ? [this.t('disputeDetail.quoted', { text: change.reason })] : []),
      ].join(' · '),
      tone: (STATUS_TONES[change.toStatus as BookingStatus] ?? 'dim') as Tone,
    }));
  });

  /**
   * What a handover recorded, as independent facts joined by " · ": who recorded it, the odometer,
   * the fuel level and how many photos — each only when it was recorded.
   */
  protected handoverFacts(handover: Handover): string {
    const facts = [
      this.t('adminBooking.recordedByParty', {
        party: this.enumLabel('party', handover.recordedBy),
      }),
    ];
    if (handover.odometerKm !== null)
      facts.push(
        this.t('adminBooking.odometerKm', { km: this.formats.number(handover.odometerKm) }),
      );
    if (handover.fuelLevel !== null)
      facts.push(
        this.t('adminBooking.fuelLevel', { level: this.formats.number(handover.fuelLevel) }),
      );
    if (handover.photoCount)
      facts.push(this.t('adminBooking.photoCount', { count: handover.photoCount }));
    return facts.join(' · ');
  }

  /**
   * The assessed amount: ONE run for a range, so Arabic cannot lay the bounds out upper bound first,
   * and the code from the value itself.
   */
  protected penaltyAmount(penalty: PenaltyAssessment): string {
    return penalty.isRange
      ? this.formats.moneyRange(
          penalty.minAmount.amount,
          penalty.maxAmount.amount,
          penalty.maxAmount.currency,
        )
      : this.money(penalty.maxAmount);
  }

  // ── What the platform may do to this booking, and why it may not.
  //
  // The server is the judge of every one of these: it refuses an expiry whose window has not run out
  // and a no-show before the timeout, against the booking's OWN frozen terms. These only decide
  // whether the button is worth offering, so an admin is not invited to click something that will
  // be refused.
  protected readonly canCancel = computed(() => {
    const status = this.booking()?.status;
    return status === 'Requested' || status === 'Approved' || status === 'Confirmed';
  });

  // The two expiries an admin can force, and the two states that have a clock running on them: a
  // request nobody answered, and an approval nobody paid for.
  protected readonly canExpire = computed(() => {
    const status = this.booking()?.status;
    return status === 'Requested' || status === 'Approved';
  });

  // A no-show needs a rental that was actually going ahead, which means the deposit cleared.
  protected readonly canMarkNoShow = computed(() => this.booking()?.status === 'Confirmed');

  protected readonly hasAnyAction = computed(
    () => this.canCancel() || this.canExpire() || this.canMarkNoShow(),
  );

  protected cancel(): void {
    const booking = this.booking();
    if (!booking) return;
    this.ui.openAction(
      {
        icon: 'x-circle',
        tone: 'warn',
        danger: true,
        title: this.t('adminBooking.cancelTitle', { reference: booking.reference }),
        body: this.t('adminBooking.cancelBody', {
          customer: this.customerName(booking),
          dealer: this.dealerName(booking),
        }),
        note: this.t('adminBooking.nothingIsRefundedHere'),
        fields: [
          {
            name: this.t('myBooking.reason'),
            label: this.t('dealerDecide.reject.reasonLabel'),
            type: 'text',
            placeholder: this.t('adminBooking.whyIsThePlatform'),
          },
        ],
        confirm: this.t('adminBooking.cancelBooking'),
        result: { title: this.t('adminBooking.bookingCancelled'), body: '', tone: 'warn' },
      },
      async (values) => {
        await this.service.cancel(booking.bookingId, values['reason'] ?? '');
        this.service.refresh();
      },
      {
        title: this.t('adminBooking.bookingCancelled'),
        body: this.t('adminBooking.recordedAgainstYourAccount'),
      },
    );
  }

  protected expire(): void {
    const booking = this.booking();
    if (!booking) return;
    const which =
      booking.status === 'Approved'
        ? this.t('myBooking.theDepositWasNever')
        : this.t('myBooking.theDealerNeverAnswered');
    this.ui.openAction(
      {
        icon: 'clock-counter-clockwise',
        tone: 'warn',
        title: this.t('adminBooking.expireTitle', { reference: booking.reference }),
        body: this.t('adminBooking.expireBody', { which }),
        note: this.t('adminBooking.refusedIfTheBookings'),
        confirm: this.t('adminBooking.expireBooking'),
        result: { title: this.t('adminBooking.bookingExpired'), body: '', tone: 'warn' },
      },
      async () => {
        await this.service.expire(booking.bookingId);
        this.service.refresh();
      },
      {
        title: this.t('adminBooking.bookingExpired'),
        body: this.t('adminBooking.recordedAgainstYourAccount'),
      },
    );
  }

  protected markNoShow(): void {
    const booking = this.booking();
    if (!booking) return;
    this.ui.openAction(
      {
        icon: 'user-minus',
        tone: 'bad',
        danger: true,
        title: this.t('adminBooking.noShowTitle', { reference: booking.reference }),
        body: this.t('adminBooking.noShowBody', { customer: this.customerName(booking) }),
        note: this.t('adminBooking.refusedUntilTheNo'),
        confirm: this.t('adminBooking.markNoShow'),
        result: { title: this.t('adminBooking.recordedAsANo'), body: '', tone: 'bad' },
      },
      async () => {
        await this.service.markNoShow(booking.bookingId);
        this.service.refresh();
      },
      {
        title: this.t('adminBooking.recordedAsANo'),
        body: this.t('adminBooking.aPenaltyIsAssessed'),
      },
    );
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
    this.resource.reload();
  }

  /** An amount at the currency's own scale, with the code the value carries. Never signed. */
  protected money(value: { readonly amount: number; readonly currency: string }): string {
    return this.formats.money(value.amount, value.currency);
  }

  protected when(iso: string): string {
    return this.formats.dateTime(iso);
  }
}

const STATUS_TONES: Readonly<Partial<Record<BookingStatus, Tone>>> = {
  Requested: 'warn',
  // Approved but unpaid is a car held against nothing, with a clock on it.
  Approved: 'warn',
  Confirmed: 'accent',
  PickedUp: 'accent',
  Returned: 'warn',
  Completed: 'ok',
  Rejected: 'dim',
  Cancelled: 'dim',
  Expired: 'dim',
  NoShow: 'bad',
};

/** Why the booking could not load, in the language on screen when it is shown. */
function describe(
  problem: ProblemSnapshot,
  t: (key: TranslationKey) => string,
  language: Language,
): string {
  if (problem.status === 404) return t('myBooking.thatBookingWasNot');
  if (problem.status === 403) return t('myBooking.thePlatformBookingRecord');
  return serverSentence(problem, language, t) ?? t('dealerBooking.theBookingCouldNot');
}
