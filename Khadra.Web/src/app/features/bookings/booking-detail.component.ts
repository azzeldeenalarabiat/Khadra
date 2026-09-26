import { HttpClient } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  signal,
  linkedSignal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Booking, DepositRefund, PaymentAttempt, PaymentOption, PaymentPurpose, PenaltyAssessment, Refund } from '../../core/api/bookings.api';
import { Money } from '../../core/api/common.api';
import { vocabularyLabel } from '../../core/api/app-config.api';
import { AppConfigService } from '../../core/config/app-config.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { LIFECYCLE, countdownText, partyLabel, stageLabel, statusLabel, statusTone } from './booking-presentation';
import { countdownParts } from './countdown';
import { HandoverCodeComponent } from './handover-code.component';
import { chosenOption } from './payment-choice';
import { BookingPaymentsComponent } from './booking-payments.component';
import { httpData } from '../../core/http/http-data';

/** How often an open booking is re-read while the page is visible (docs/refresh-policy.md: 60s). */
const LIVE_REFRESH_MS = 60_000;
/** While a handover code is on screen the office may record the handover at any moment (the app: 5s). */
const HANDOVER_WATCH_MS = 5_000;
/** After returning from checkout: quickly for a minute, then every ten seconds, as the app does. */
const CHECKOUT_FAST_MS = 3_000;
const CHECKOUT_FAST_FOR_MS = 60_000;
const CHECKOUT_SLOW_MS = 10_000;
const CHECKOUT_KEY = 'kh.checkout.';

/**
 * One booking, the way the app shows it: a notice about where it stands, what the customer can do
 * now, the timeline, the frozen price, and the handovers. Every action appears only when the server's
 * own flag allows it. Nothing here decides whether a deadline has passed; the countdown only tells
 * the reader how long is left.
 *
 * Paying opens the provider's checkout page (a URL the server generated) in this tab. Coming back,
 * the page never trusts the redirect: it re-reads the booking until the server says the payment
 * settled one way or the other.
 */
/** Requested and Sent read alike to a customer (the server has started it); Failed is still owed. */
export function refundStage(refund: DepositRefund): 'initiated' | 'done' | 'delayed' {
  return refund.status === 'Settled' ? 'done' : refund.status === 'Failed' ? 'delayed' : 'initiated';
}

/**
 * The refund a `booking.refund_changed` refusal says cancelling would now return (owner, 2026-09-26),
 * read from the ProblemDetails beside its code. Null when the body carries no usable figure.
 */
export function changedRefundOf(error: unknown): Money | null {
  const body = (typeof error === 'object' && error !== null ? (error as { error?: unknown }).error : null) as
    | { code?: unknown; currentRefund?: { amount?: unknown; currency?: unknown } }
    | null;
  const current = body?.code === 'booking.refund_changed' ? body.currentRefund : undefined;
  return current && typeof current.amount === 'number' && typeof current.currency === 'string'
    ? { amount: current.amount, currency: current.currency }
    : null;
}

@Component({
  selector: 'kh-booking-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, StatePanelComponent, HandoverCodeComponent, BookingPaymentsComponent],
  templateUrl: './booking-detail.component.html',
})
export class BookingDetailComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly appConfig = inject(AppConfigService);
  private readonly document = inject(DOCUMENT);
  private readonly t = this.i18n.t.bind(this.i18n);

  readonly bookingId = input<string>('');
  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);
  protected readonly justCreated = computed(() => this.query()?.get('created') === '1');

  protected readonly booking = httpData<Booking>(() => {
    const id = this.bookingId();
    return /^[0-9a-f-]{36}$/i.test(id) ? `/api/v1/bookings/${id}` : undefined;
  });
  protected readonly problem = computed(() => (this.booking.error() ? snapshotProblem(this.booking.error()) : null));
  protected readonly notFound = computed(
    () => !/^[0-9a-f-]{36}$/i.test(this.bookingId()) || this.problem()?.status === 404 || this.problem()?.status === 403,
  );

  /**
   * The booking as last read. A refresh that fails keeps the page on what it last knew instead of
   * replacing it with the error panel: with a handover code on screen, that replacement destroyed the
   * code panel, and the next good refresh remounted it — issuing a NEW code while the office was typing
   * the old one. Only a booking that is gone (404/403) or a different booking clears it.
   */
  protected readonly view = linkedSignal<{ id: string; value: Booking | undefined; gone: boolean }, Booking | undefined>({
    source: () => ({ id: this.bookingId(), value: this.booking.value(), gone: this.notFound() }),
    computation: (source, previous) =>
      source.value ?? (source.gone || previous?.source.id !== source.id ? undefined : previous?.value),
  });
  /** The whole-page error: only when there is nothing to show. */
  protected readonly pageProblem = computed(() => (this.view() ? null : this.problem()));
  /** A refresh failed while the page still shows the booking as last read. */
  protected readonly refreshFailed = computed(() => !!this.view() && this.problem() !== null && !this.notFound());

  private readonly now = signal(Date.now());
  protected readonly decisionLeft = computed(() => countdownText(this.t, countdownParts(this.view()?.decisionDeadline, this.now())));
  protected readonly paymentLeft = computed(() => countdownText(this.t, countdownParts(this.view()?.paymentDeadline, this.now())));

  /**
   * The lifecycle as far as this booking has gone. A live booking shows every stage, done up to its
   * current one; a booking that ended early (cancelled, declined, expired, not collected) shows only
   * the stages it reached and then the one it ended at.
   */
  protected readonly timeline = computed(() => {
    const booking = this.view();
    if (!booking) return [];
    const reachedAt = new Map(booking.history.map((change) => [change.toStatus, change.occurredAt] as const));
    const at = (stage: string) => (stage === 'Requested' ? (booking.requestedAt ?? booking.createdAt) : (reachedAt.get(stage) ?? null));
    type Row = { readonly stage: string; readonly label: string; readonly at: string | null; readonly state: 'done' | 'current' | 'todo' | 'ended' };

    const confirmedBy = booking.confirmingPayment?.purpose;
    const current = LIFECYCLE.indexOf(booking.status as (typeof LIFECYCLE)[number]);
    if (current >= 0) {
      return LIFECYCLE.map((stage, index): Row => ({
        stage,
        label: stageLabel(this.t, stage, confirmedBy),
        at: index <= current ? at(stage) : null,
        state: index < current || booking.status === 'Completed' ? 'done' : index === current ? 'current' : 'todo',
      }));
    }

    const reached = LIFECYCLE.filter((stage) => at(stage) !== null).map(
      (stage): Row => ({ stage, label: stageLabel(this.t, stage, confirmedBy), at: at(stage), state: 'done' }),
    );
    return [...reached, { stage: booking.status, label: stageLabel(this.t, booking.status, confirmedBy), at: reachedAt.get(booking.status) ?? booking.finishedAt, state: 'ended' } as Row];
  });

  /** Why it ended, when it ended early: the reason recorded on the change into its final status. */
  protected readonly endReason = computed(() => {
    const booking = this.view();
    if (!booking) return '';
    const change = [...booking.history].reverse().find((entry) => entry.toStatus === booking.status);
    return this.reasonLabel(change?.reasonCode ?? booking.cancellationReasonCode);
  });

  /**
   * What the Payments section re-reads on: the booking's money-relevant facts as last read — its status,
   * whether it is paid, each refund's status, the handovers. The section's own figures come from the
   * server's financial state; this only says when that may have moved.
   */
  protected readonly paymentsVersion = computed(() => {
    const b = this.view();
    if (!b) return '';
    const refunds = (b.refunds ?? []).map((refund) => `${refund.refundId}:${refund.status}`).join(',');
    return [b.status, b.depositPaid, b.isPaidInFull ?? false, refunds, b.handovers.length, b.liveDisputeId ?? ''].join('|');
  });

  protected readonly canShowHandover = computed(() => ['Confirmed', 'PickedUp'].includes(this.view()?.status ?? ''));
  protected readonly handoverOpen = signal(false);
  protected readonly handoverRecorded = signal(false);

  // ── Payment ──────────────────────────────────────────────────────────────
  protected readonly paying = signal(false);
  protected readonly payProblem = signal<ProblemSnapshot | null>(null);
  /** The way of paying the visitor picked; null until they touch the choice. */
  protected readonly pickedPurpose = signal<PaymentPurpose | null>(null);

  protected option(b: Booking): PaymentOption | null {
    return chosenOption(b.payment?.options, this.pickedPurpose(), b.payment?.liveAttempt?.purpose);
  }
  protected readonly checkout = signal<'checking' | 'paid' | 'open' | 'ended' | null>(null);
  private checkoutStartedAt = 0;
  private checkoutPaymentId: string | null = null;
  /** The pending re-read after checkout; cancelled when the page goes away. */
  private checkoutTimer: ReturnType<typeof setTimeout> | null = null;

  // ── Cancellation ─────────────────────────────────────────────────────────
  private readonly cancelDialog = viewChild<ElementRef<HTMLDialogElement>>('cancelDialog');
  protected readonly cancelReasons = computed(() => this.appConfig.config()?.vocabularies.cancellationReasons ?? []);
  protected readonly cancelReason = signal('');
  protected readonly cancelDetails = signal('');
  protected readonly cancelling = signal(false);
  protected readonly cancelProblem = signal<ProblemSnapshot | null>(null);
  protected readonly cancelOutcome = signal<'cancelled' | 'expired' | null>(null);
  protected readonly cancelMissingReason = signal(false);
  /** The refund changed while the sheet was open: the server's new figure, until the customer confirms again. */
  protected readonly refundChanged = signal<Money | null>(null);

  protected readonly problemMessage = (problem: ProblemSnapshot | null) =>
    problem ? problemText(problem, this.t, this.i18n.language(), this.appConfig.config()) : null;

  constructor() {
    const destroy = inject(DestroyRef);
    const clock = setInterval(() => this.now.set(Date.now()), 30_000);
    const live = setInterval(() => {
      const booking = this.view();
      if (booking && !booking.isTerminal && this.document.visibilityState === 'visible' && !this.checkout() && !this.handoverOpen()) this.booking.reload();
    }, LIVE_REFRESH_MS);
    const handoverWatch = setInterval(() => {
      if (this.handoverOpen() && !this.booking.isLoading()) this.booking.reload();
    }, HANDOVER_WATCH_MS);
    destroy.onDestroy(() => {
      clearInterval(clock);
      clearInterval(live);
      clearInterval(handoverWatch);
      if (this.checkoutTimer) clearTimeout(this.checkoutTimer);
    });

    effect(() => {
      const booking = this.view();
      if (booking) this.seo.set({ title: this.t('seo.booking.title', { reference: booking.reference }), noindex: true });
    });

    // Back from the provider's checkout: the address says nothing reliable, so ask the server.
    effect(() => {
      const booking = this.view();
      if (!booking || this.checkout() !== null) return;
      const started = this.readCheckoutMarker(booking.bookingId);
      if (started) this.watchCheckout(booking.bookingId, started);
    });

    // The handover panel closes itself once the office has recorded the handover.
    let lastStatus: string | null = null;
    effect(() => {
      const status = this.view()?.status ?? null;
      if (lastStatus && status && status !== lastStatus && this.handoverOpen()) {
        // Any change ends the code's purpose; only the two handover transitions mean one was recorded.
        // A booking cancelled while the code was on screen must not read "Handover recorded."
        this.handoverOpen.set(false);
        this.handoverRecorded.set(
          (lastStatus === 'Confirmed' && status === 'PickedUp') || (lastStatus === 'PickedUp' && status === 'Returned'),
        );
      }
      lastStatus = status;
    });
  }

  private readonly seo = inject(SeoService);

  /** Deposit wording for a deposit, payment wording for a booking paid in full (owner, 2026-09-25). */
  protected refundText(refund: DepositRefund, booking: Booking): string {
    return this.paidWithFullPayment(booking) ? this.paymentRefundText(refund) : this.depositRefundText(refund);
  }

  /** The deposit, in full, on its way back. */
  private depositRefundText(refund: DepositRefund): string {
    const amount = this.format.money(refund.amount);
    switch (refundStage(refund)) {
      case 'done':
        return this.t('booking.refundedText', { amount, date: this.format.dateTime(refund.settledAt ?? refund.requestedAt) });
      case 'delayed':
        return this.t('booking.refundDelayedText', { amount });
      default:
        return this.t('booking.refundInitiatedText', { amount, date: this.format.dateTime(refund.requestedAt) });
    }
  }

  /** An amount of the payment on its way back: the whole of it, or a part a dispute decided. */
  private paymentRefundText(refund: DepositRefund): string {
    const amount = this.format.money(refund.amount);
    switch (refundStage(refund)) {
      case 'done':
        return this.t('booking.refundedTextPayment', { amount, date: this.format.dateTime(refund.settledAt ?? refund.requestedAt) });
      case 'delayed':
        return this.t('booking.refundDelayedTextPayment', { amount });
      default:
        return this.t('booking.refundInitiatedTextPayment', { amount, date: this.format.dateTime(refund.requestedAt) });
    }
  }

  /**
   * One refund in a sentence, by what it returns: the money paid above the deposit, the deposit
   * itself (a free cancellation's on a deposit, the clean-close release), or part of the payment.
   */
  protected refundSentence(refund: Refund, booking: Booking): string {
    if (refund.reason === 'EndedBeforePickup') {
      const amount = this.format.money(refund.amount);
      switch (refundStage(refund)) {
        case 'done':
          return this.t('booking.refundAboveDepositRefundedText', { amount, date: this.format.dateTime(refund.settledAt ?? refund.requestedAt) });
        case 'delayed':
          return this.t('booking.refundAboveDepositDelayedText', { amount });
        default:
          return this.t('booking.refundAboveDepositInitiatedText', { amount, date: this.format.dateTime(refund.requestedAt) });
      }
    }
    if (refund.reason === 'DisputeWindowClosed') return this.depositRefundText(refund);
    if (refund.reason === 'FreeCancellation' || refund.reason === 'PlatformCancellation') return this.refundText(refund, booking);
    return this.paymentRefundText(refund);
  }

  /** Whether the payment that confirmed this booking was the whole amount, from the server's record of it. */
  protected paidWithFullPayment(booking: Booking): boolean {
    return booking.confirmingPayment?.purpose === 'FullPayment';
  }

  /**
   * What a free cancellation promises. A full payment names the server's refund figure, which is the
   * whole payment unless a processing fee was taken as non-refundable; a deposit keeps its sentence.
   */
  protected freeRefundText(booking: Booking): string {
    const payment = booking.confirmingPayment;
    return payment?.purpose === 'FullPayment'
      ? this.t('cancel.freeRefundPayment', { amount: this.format.money(payment.refundOnFreeCancellation) })
      : this.t('cancel.freeRefund');
  }

  protected status(status: string): string {
    return statusLabel(this.t, status);
  }

  protected tone(status: string): string {
    return statusTone(status);
  }

  protected party(party: string | null): string {
    return partyLabel(this.t, party);
  }

  /**
   * Where an assessed penalty stands, in the owner's words (pre-launch item 173): the server's state,
   * never one worked out here. A state this site does not know is left unsaid.
   */
  protected penaltyNote(penalty: PenaltyAssessment): string | null {
    return penalty.state === 'Assessed' || penalty.state === 'ResolvedByDispute'
      ? this.t(`booking.penaltyState.${penalty.state}` as TranslationKey)
      : null;
  }

  /** How a recorded handover was proved (Code, Unverified, NotRequired); nothing for older records. */
  protected verificationLabel(verification: string | null | undefined): string {
    return verification === 'Code' || verification === 'Unverified' || verification === 'NotRequired'
      ? this.t(`handover.verified.${verification}` as TranslationKey)
      : '';
  }

  protected methodLabel(method: string): string {
    return method === 'Delivery' || method === 'SelfPickup' ? this.t(`booking.method.${method}` as TranslationKey) : method;
  }

  protected percent(value: number): string {
    return `${this.format.number(value)}%`;
  }

  protected reasonLabel(code: string | null): string {
    if (!code) return '';
    const vocabularies = this.appConfig.config()?.vocabularies;
    const all = [...(vocabularies?.cancellationReasons ?? []), ...(vocabularies?.rejectionReasons ?? [])];
    return vocabularyLabel(all, code, this.i18n.isArabic());
  }

  // ── Payment ──────────────────────────────────────────────────────────────
  protected async pay(booking: Booking): Promise<void> {
    if (this.paying() || !booking.payment?.canPay) return;
    this.paying.set(true);
    this.payProblem.set(null);
    try {
      // The purpose, never an amount: the server works out what that choice costs. An API from before
      // the two options offers none, and the deposit endpoint is still the way to pay it.
      const chosen = this.option(booking);
      const attempt = await firstValueFrom(
        chosen
          ? this.http.post<PaymentAttempt>(`/api/v1/bookings/${booking.bookingId}/checkout`, { purpose: chosen.purpose })
          : this.http.post<PaymentAttempt>(`/api/v1/bookings/${booking.bookingId}/deposit-checkout`, {}),
      );
      const url = attempt.checkoutUrl ?? '';
      // Only ever follow an http(s) address the server generated; anything else is refused here.
      if (!/^https?:\/\//i.test(url)) throw { status: 0 };
      this.writeCheckoutMarker(booking.bookingId, attempt.paymentId);
      this.document.location.assign(url);
    } catch (error) {
      this.payProblem.set(snapshotProblem(error));
      this.paying.set(false);
      this.booking.reload();
    }
  }

  private watchCheckout(bookingId: string, paymentId: string): void {
    this.checkout.set('checking');
    this.checkoutStartedAt = Date.now();
    this.checkoutPaymentId = paymentId;
    const tick = () => {
      const booking = this.view();
      const settled = this.checkoutSettled(booking);
      if (settled) {
        this.checkout.set(settled);
        this.clearCheckoutMarker(bookingId);
        return;
      }
      this.booking.reload();
      const elapsed = Date.now() - this.checkoutStartedAt;
      this.checkoutTimer = setTimeout(tick, elapsed < CHECKOUT_FAST_FOR_MS ? CHECKOUT_FAST_MS : CHECKOUT_SLOW_MS);
    };
    this.checkoutTimer = setTimeout(tick, CHECKOUT_FAST_MS);
  }

  /** The app's rule: settled once the booking no longer awaits payment, or the attempt changed or failed. */
  private checkoutSettled(booking: Booking | undefined): 'paid' | 'open' | 'ended' | null {
    if (!booking) return null;
    if (booking.depositPaid || booking.status === 'Confirmed') return 'paid';
    if (!booking.isAwaitingPayment) return 'ended';
    const live = booking.payment?.liveAttempt ?? null;
    if (!live || live.paymentId !== this.checkoutPaymentId || live.status === 'Failed') return 'ended';
    // Still open after the fast minute: say so, and let the customer carry on paying.
    return Date.now() - this.checkoutStartedAt > CHECKOUT_FAST_FOR_MS ? 'open' : null;
  }

  private readCheckoutMarker(bookingId: string): string | null {
    try {
      return this.document.defaultView?.sessionStorage.getItem(CHECKOUT_KEY + bookingId) ?? null;
    } catch {
      return null;
    }
  }

  private writeCheckoutMarker(bookingId: string, paymentId: string): void {
    try {
      this.document.defaultView?.sessionStorage.setItem(CHECKOUT_KEY + bookingId, paymentId);
    } catch {
      /* private mode: the page still re-reads the booking on its own schedule */
    }
  }

  private clearCheckoutMarker(bookingId: string): void {
    try {
      this.document.defaultView?.sessionStorage.removeItem(CHECKOUT_KEY + bookingId);
    } catch {
      /* nothing to clear */
    }
  }

  // ── Cancellation ─────────────────────────────────────────────────────────
  protected openCancel(): void {
    this.cancelReason.set('');
    this.cancelDetails.set('');
    this.cancelProblem.set(null);
    this.cancelMissingReason.set(false);
    this.refundChanged.set(null);
    this.cancelDialog()?.nativeElement.showModal();
  }

  /**
   * The money cancelling now gives back ABOVE the deposit — the case the free-cancellation sentence
   * does not cover. Null inside the free window (the whole payment is promised there) and when nothing
   * comes back.
   */
  protected aboveDepositRefund(booking: Booking): Money | null {
    const refund = booking.cancellation.refundAmount;
    return !booking.cancellation.willRefundDeposit && refund && refund.amount > 0 ? refund : null;
  }

  protected closeCancel(): void {
    this.cancelDialog()?.nativeElement.close();
  }

  protected async confirmCancel(booking: Booking): Promise<void> {
    if (this.cancelling()) return;
    if (!this.cancelReason()) {
      this.cancelMissingReason.set(true);
      return;
    }
    this.cancelling.set(true);
    this.cancelProblem.set(null);
    try {
      // The figure the sheet showed, so a refund that changed while it was open (the free window
      // closing) is refused with the new figure rather than cancelled for less. Not sent to a server
      // that published no figure: it would not know what the sheet showed.
      const shown = booking.cancellation.refundAmount;
      const updated = await firstValueFrom(
        this.http.post<Booking>(`/api/v1/bookings/${booking.bookingId}/cancel`, {
          reasonCode: this.cancelReason(),
          details: this.cancelDetails().trim() || null,
          ...(shown === undefined ? {} : { expectedRefund: shown?.amount ?? 0 }),
        }),
      );
      this.booking.set(updated);
      this.cancelOutcome.set(updated.status === 'Expired' ? 'expired' : 'cancelled');
      this.refundChanged.set(null);
      this.closeCancel();
    } catch (error) {
      // The refund moved on: say by how much, re-read the booking so the sheet states it, and let the
      // customer decide again. Nothing was cancelled.
      const changed = changedRefundOf(error);
      this.refundChanged.set(changed);
      this.cancelProblem.set(changed ? null : snapshotProblem(error));
      this.booking.reload();
    } finally {
      this.cancelling.set(false);
    }
  }

  protected dismissCreated(): void {
    void this.router.navigate([], { queryParams: { created: null }, queryParamsHandling: 'merge', replaceUrl: true });
  }
}
