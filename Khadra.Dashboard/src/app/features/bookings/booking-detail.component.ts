import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { Booking, BookingStatus } from '../../core/models/bookings.api';
import { KeyValue, TimelineStep, Tone } from '../../core/models/console.models';
import { AdminBookingsService } from '../../core/services/admin-bookings.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { MoneyPipe } from '../../shared/money.pipe';

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
  imports: [RouterLink, IconComponent, TimelineComponent, MoneyPipe],
})
export class AdminBookingDetailComponent {
  protected readonly t = inject(I18nService).t;
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

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 404) return 'That booking was not found.';
    if (error.status === 403) return 'The platform booking record is for administrators.';
    return 'The booking could not be loaded. Nothing has been changed.';
  });

  protected readonly tone = computed<Tone>(() => {
    const booking = this.booking();
    if (!booking) return 'dim';
    if (booking.liveDisputeId) return 'bad';
    return STATUS_TONES[booking.status] ?? 'dim';
  });

  protected readonly statusLabel = computed(() =>
    (this.booking()?.status ?? '').replace(/([a-z])([A-Z])/g, '$1 $2'),
  );

  /** What the booking is worth, all of it frozen at the moment it was made. */
  protected readonly moneyRows = computed<readonly KeyValue[]>(() => {
    const booking = this.booking();
    if (!booking) return [];
    const pricing = booking.pricing;
    const rows: KeyValue[] = [
      { k: `Daily rate × ${pricing.days} days`, v: this.money(pricing.dailyRate) },
      { k: 'Rental total', v: this.money(pricing.rentalTotal) },
    ];
    // Keyed on the pickup method, not the amount: 0 is now a real answer a gallery can give, and
    // hiding the row would make free delivery indistinguishable from no delivery at all.
    if (booking.pickupMethod === 'Delivery')
      rows.push({ k: 'Delivery fee', v: this.money(pricing.deliveryFee) });
    rows.push(
      { k: 'Total price', v: this.money(pricing.totalPrice) },
      // The percentages come from the booking's own terms, never from the settings in force today.
      { k: `Deposit (${pricing.depositPercent}%)`, v: this.money(pricing.depositAmount) },
      { k: 'Balance due', v: this.money(pricing.balanceDue) },
      { k: 'Security deposit', v: this.money(pricing.securityDeposit) },
      {
        k: `Platform commission (${booking.terms.commissionPercent}%)`,
        v: this.money(booking.commissionAmount),
      },
    );
    return rows;
  });

  /** The rulebook this booking froze. Version included: it is what makes the rest reproducible. */
  protected readonly termsRows = computed<readonly KeyValue[]>(() => {
    const terms = this.booking()?.terms;
    if (!terms) return [];
    return [
      { k: 'Free cancellation window', v: `${terms.freeCancellationWindowHours} hours` },
      { k: 'Payment window', v: `${terms.paymentWindowHours} hours` },
      { k: 'No-show timeout', v: `${terms.noShowTimeoutHours} hours` },
      { k: 'Settlement window after return', v: `${terms.postReturnSettlementWindowHours} hours` },
      { k: 'Customer cancellation penalty', v: `${terms.customerCancellationPenaltyPercent}%` },
      {
        k: 'Dealer non-delivery penalty',
        v: `${terms.dealerPenaltyMinPercent}–${terms.dealerPenaltyMaxPercent}%`,
      },
      { k: 'Rules version', v: String(terms.rulesVersion) },
    ];
  });

  protected readonly partyRows = computed<readonly KeyValue[]>(() => {
    const booking = this.booking();
    if (!booking) return [];
    return [
      { k: 'Dealer', v: booking.dealerName },
      { k: 'Customer', v: booking.customerName },
      {
        k: 'Vehicle',
        v: booking.vehicle
          ? `${booking.vehicle.make} ${booking.vehicle.model} ${booking.vehicle.year} · ${booking.vehicle.plateNumber}`
          : 'Delisted since this booking was made',
      },
      { k: 'Handover', v: booking.pickupMethod === 'Delivery' ? 'Delivery' : 'Self pickup' },
    ];
  });

  /** Every status change, in the order it happened, naming who made it. */
  protected readonly timeline = computed<readonly TimelineStep[]>(() => {
    const booking = this.booking();
    if (!booking) return [];
    return booking.history.map((change) => ({
      label: change.toStatus.replace(/([a-z])([A-Z])/g, '$1 $2'),
      meta: `${this.when(change.occurredAt)} · ${change.actorParty}${change.reason ? ` · “${change.reason}”` : ''}`,
      tone: (STATUS_TONES[change.toStatus as BookingStatus] ?? 'dim') as Tone,
    }));
  });

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
        title: `Cancel ${booking.reference}?`,
        body: `The booking ends now and the car is released. No penalty is assessed against ${booking.customerName} or ${booking.dealerName} — the platform is cancelling, not either party.`,
        note: this.t('adminBooking.nothingIsRefundedHere'),
        fields: [
          {
            name: 'reason',
            label: 'Reason',
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
      { title: this.t('adminBooking.bookingCancelled'), body: this.t('adminBooking.recordedAgainstYourAccount') },
    );
  }

  protected expire(): void {
    const booking = this.booking();
    if (!booking) return;
    const which =
      booking.status === 'Approved'
        ? 'The deposit was never paid inside the payment window.'
        : 'The dealer never answered inside their window.';
    this.ui.openAction(
      {
        icon: 'clock-counter-clockwise',
        tone: 'warn',
        title: `Expire ${booking.reference}?`,
        body: `${which} Expiring releases the car. No penalty is assessed against anyone.`,
        note: this.t('adminBooking.refusedIfTheBookings'),
        confirm: this.t('adminBooking.expireBooking'),
        result: { title: this.t('adminBooking.bookingExpired'), body: '', tone: 'warn' },
      },
      async () => {
        await this.service.expire(booking.bookingId);
        this.service.refresh();
      },
      { title: this.t('adminBooking.bookingExpired'), body: this.t('adminBooking.recordedAgainstYourAccount') },
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
        title: `Mark ${booking.reference} as a no-show?`,
        body: `${booking.customerName} never collected the car. This assesses whatever this booking's own terms say is owed — nothing is charged.`,
        note: this.t('adminBooking.refusedUntilTheNo'),
        confirm: this.t('adminBooking.markNoShow'),
        result: { title: this.t('adminBooking.recordedAsANo'), body: '', tone: 'bad' },
      },
      async () => {
        await this.service.markNoShow(booking.bookingId);
        this.service.refresh();
      },
      { title: this.t('adminBooking.recordedAsANo'), body: this.t('adminBooking.aPenaltyIsAssessed') },
    );
  }

  protected reload(): void {
    this.resource.reload();
  }

  protected money(value: { amount: number; currency: string }): string {
    return `${value.amount} ${value.currency}`;
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
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
