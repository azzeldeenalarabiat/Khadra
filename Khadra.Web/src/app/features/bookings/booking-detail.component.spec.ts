import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppConfigService } from '../../core/config/app-config.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { BookingDetailComponent, changedRefundOf, refundStage } from './booking-detail.component';

const ID = '01a0d095-c204-7f23-b0b3-1be7852e4b23';
const BOOKING_URL = `/api/v1/bookings/${ID}`;
const CODE_URL = `${BOOKING_URL}/handover-code`;
const FINANCIALS_URL = `${BOOKING_URL}/financials`;
const money = (amount: number) => ({ amount, currency: 'JOD' });

/** One refund as the financial state lists it. */
const financialRefund = (reason: string, status: string, figure: number) => ({
  refundId: `fr-${reason}`, paymentId: 'p-1', reason, status, amount: money(figure), feePart: money(0),
  requestedAt: '2026-09-25T20:00:00+00:00', sentAt: status === 'Requested' ? null : '2026-09-25T20:01:00+00:00',
  settledAt: status === 'Settled' ? '2026-09-25T20:05:00+00:00' : null,
  failedAt: status === 'Failed' ? '2026-09-25T20:02:00+00:00' : null, disputeTicketId: null,
});

/**
 * The customer's projection of a booking's financial state (payments Phase 4): one payment of
 * `charged` for `purpose`, the given refunds under it, and the balance and deposit as the server
 * states them.
 */
function financials(options: {
  status: string;
  purpose: 'Deposit' | 'FullPayment';
  charged: number;
  balance: { state: string; amount: number };
  deposit: { state: string; amount: number; refund?: object | null };
  refunds?: object[];
  progress?: string;
  refunded?: number;
  inProgress?: number;
}) {
  return {
    bookingId: ID, bookingStatus: options.status, currency: 'JOD', generatedAt: '2026-09-26T08:00:00+00:00',
    calculatorVersion: 1, needsReview: false,
    summary: {
      rentalSubtotal: money(90), deliveryFee: money(12.75), bookingTotal: money(102.75), requiredDeposit: money(18),
      securityDeposit: money(500), paidOnline: money(options.charged), processingFees: money(0),
      chargedOnline: money(options.charged), refunded: money(options.refunded ?? 0),
      refundInProgress: money(options.inProgress ?? 0), refundDelayed: money(0),
    },
    balance: { state: options.balance.state, amount: money(options.balance.amount), cashRecorded: [] },
    deposit: {
      state: options.deposit.state, amount: money(options.deposit.amount), windowEndsAt: null,
      refund: options.deposit.refund ?? null, decision: null,
    },
    commission: null,
    payments: [{
      paymentId: 'p-1', purpose: options.purpose, status: 'Applied', refundProgress: options.progress ?? 'None',
      occurredAt: '2026-09-25T17:56:00+00:00', appliedToBooking: money(options.charged), amountCharged: money(options.charged),
      processingFee: money(0), feeRefundable: true, refunds: options.refunds ?? [],
      createdAt: null, isSandbox: null, providerReference: null, failureCode: null, orphanReason: null,
    }],
    issues: null,
  };
}

/** A Confirmed booking, shaped as the API answered for one on 2026-09-24 (names replaced). */
const CONFIRMED = {
  bookingId: ID,
  reference: 'KH-TEST0001',
  status: 'Confirmed',
  isTerminal: false,
  customerId: '01a0d079-fd84-7f08-8b59-83735471b016',
  dealerId: '01a07283-79d7-7d7f-b779-1a7dedc73d2a',
  vehicleId: '01a07d0d-b068-7d86-b0cb-dede8f3af32c',
  periodStart: '2026-10-15T07:00:00+00:00',
  periodEnd: '2026-10-17T07:00:00+00:00',
  pickupMethod: 'SelfPickup',
  deliveryLocation: null,
  paymentOption: 'DepositOnly',
  pricing: {
    dailyRate: money(100), pickupDate: '2026-10-15', returnDate: '2026-10-17', days: 2,
    rentalTotal: money(200), deliveryFee: money(0), totalPrice: money(200), depositPercent: 20,
    depositAmount: money(40), balanceDue: money(160), securityDeposit: money(500),
    mileageUnlimited: false, mileageDailyLimitKm: 200, mileageExcessFeePerKm: money(0), fuelPolicy: 'FullToFull',
  },
  terms: {
    depositPercent: 20, commissionPercent: 20, freeCancellationWindowHours: 1, noShowTimeoutHours: 8,
    paymentWindowHours: 2, answerWindowHours: 48, postReturnSettlementWindowHours: 48,
    customerCancellationPenaltyPercent: 100, dealerPenaltyMinPercent: 25, dealerPenaltyMaxPercent: 50, rulesVersion: 1,
  },
  penalty: null,
  cancelledBy: null,
  cancellationReasonCode: null,
  cancellationReason: null,
  createdAt: '2026-09-23T23:24:30+00:00',
  decisionDeadline: '2026-09-25T23:24:30+00:00',
  paymentDeadline: '2026-09-24T01:24:53+00:00',
  depositPaid: true,
  requestedAt: '2026-09-23T23:24:30+00:00',
  approvedAt: '2026-09-23T23:24:53+00:00',
  freeCancellationDeadline: '2026-09-24T00:25:18+00:00',
  pickedUpAt: null,
  returnedAt: null,
  finishedAt: null,
  canBeDisputed: false,
  isAwaitingDecision: false,
  isAwaitingPayment: false,
  cancellation: {
    canCancel: false, isFree: false,
    penalty: {
      attributedTo: 'Customer', minPercent: 100, maxPercent: 100, minAmount: money(40), maxAmount: money(40),
      isRange: false, isNothingOwed: false, requiresTicketToEnforce: true, reason: '', reasonCode: 'LateCancellation',
      assessedAt: '2026-09-23T23:54:11+00:00',
    },
  },
  liveDisputeId: null,
  payment: { canPay: false, unavailableReason: 'booking.not_awaiting_payment', amountDue: null, payBy: null, liveAttempt: null },
  canReportNonDelivery: false,
  nonDeliveryReportableFrom: '2026-10-15T07:15:00+00:00',
  canBeReviewed: false,
  myReviewId: null,
  vehicle: { vehicleId: '01a07d0d-b068-7d86-b0cb-dede8f3af32c', make: 'Toyota', model: 'Supra', year: 1999, color: 'Orange', plateNumber: '11111111', coverImageUrl: null },
  dealerName: 'Test Rentals',
  dealerRemoved: false,
  dealerCityId: '01a07280-8405-7250-a6e8-ea3587f27b6e',
  customerName: 'Test Customer',
  customerAccountClosed: false,
  handovers: [],
  history: [
    { fromStatus: null, toStatus: 'Requested', actorParty: 'Customer', actorUserId: null, reasonCode: null, reason: null, occurredAt: '2026-09-23T23:24:30+00:00' },
    { fromStatus: 'Requested', toStatus: 'Approved', actorParty: 'Dealer', actorUserId: null, reasonCode: null, reason: null, occurredAt: '2026-09-23T23:24:53+00:00' },
    { fromStatus: 'Approved', toStatus: 'Confirmed', actorParty: 'Customer', actorUserId: null, reasonCode: null, reason: null, occurredAt: '2026-09-23T23:25:18+00:00' },
  ],
};

describe('BookingDetailComponent, with the handover code on screen', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<BookingDetailComponent>;

  async function settle() {
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    for (let i = 0; i < 3; i++) await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const bookingReads = () => http.match((request) => request.method === 'GET' && request.url === BOOKING_URL);
  const codeRequests = () => http.match((request) => request.method === 'POST' && request.url === CODE_URL);
  const element = () => fixture.nativeElement as HTMLElement;

  beforeEach(async () => {
    // Only the page's refresh intervals are faked; promises and timeouts stay real.
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    TestBed.configureTestingModule({
      imports: [BookingDetailComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BookingDetailComponent);
    fixture.componentRef.setInput('bookingId', ID);
    await settle();
    bookingReads().forEach((read) => read.flush(CONFIRMED));
    await settle();

    // The customer opens the pickup code.
    element().querySelector<HTMLButtonElement>('section[aria-labelledby=actions-title] .btn--primary')!.click();
    await settle();
    const issued = codeRequests();
    expect(issued).toHaveLength(1);
    issued[0].flush({ type: 'Pickup', code: '123456', qrPayload: 'khadra-handover:v1:KH-TEST0001:123456', expiresAt: new Date(Date.now() + 120_000).toISOString() });
    await settle();
    expect(element().textContent).toContain('123 456');
  });

  afterEach(() => vi.useRealTimers());

  // Found reviewing the website before staging: one failed refresh replaced the page with the error
  // panel, destroying the code, and the next good refresh remounted the panel, which asked for a NEW
  // code while the office was typing the old one.
  it('keeps the code, and asks for no new one, when a refresh fails', async () => {
    vi.advanceTimersByTime(5_000);
    await settle();
    bookingReads().forEach((read) => read.flush({ code: 'server.error' }, { status: 503, statusText: 'Unavailable' }));
    await settle();

    expect(element().textContent).toContain('123 456');
    expect(codeRequests()).toHaveLength(0);

    vi.advanceTimersByTime(5_000);
    await settle();
    bookingReads().forEach((read) => read.flush(CONFIRMED));
    await settle();

    expect(element().textContent).toContain('123 456');
    expect(codeRequests()).toHaveLength(0);
  });

  it('closes the code and says the handover was recorded once the office records it', async () => {
    vi.advanceTimersByTime(5_000);
    await settle();
    bookingReads().forEach((read) => read.flush({ ...CONFIRMED, status: 'PickedUp', pickedUpAt: '2026-09-23T23:30:00+00:00' }));
    await settle();

    expect(element().textContent).not.toContain('123 456');
    expect(element().querySelector('.handover')).toBeNull();
  });
});

describe('BookingDetailComponent, a paid booking cancelled inside the free window', () => {
  let http: HttpTestingController;

  async function render(depositRefund: unknown) {
    TestBed.configureTestingModule({
      imports: [BookingDetailComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(BookingDetailComponent);
    fixture.componentRef.setInput('bookingId', ID);
    const settle = async () => {
      for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();
    };
    await settle();
    http
      .match((request) => request.url === BOOKING_URL)
      .forEach((read) => read.flush({ ...CONFIRMED, status: 'Cancelled', cancelledBy: 'Customer', finishedAt: '2026-09-23T23:40:00+00:00', depositRefund }));
    await settle();
    // The Payments section reads the server's financial state for the same booking (payments Phase 4).
    const status = (depositRefund as { status: string }).status;
    const refundLine = financialRefund('FreeCancellation', status, 40);
    http.match((request) => request.url === FINANCIALS_URL).forEach((read) => read.flush(financials({
      status: 'Cancelled', purpose: 'Deposit', charged: 40,
      balance: { state: 'NotDue', amount: 0 },
      deposit: { state: 'ReturnedWithPayment', amount: 40, refund: refundLine },
      refunds: [refundLine],
      progress: status === 'Settled' ? 'Complete' : status === 'Failed' ? 'Delayed' : 'InProgress',
      refunded: status === 'Settled' ? 40 : 0,
      inProgress: status === 'Settled' ? 0 : 40,
    })));
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  const refund = (status: string) => ({
    status,
    amount: { amount: 40, currency: 'JOD' },
    requestedAt: '2026-09-23T23:40:00+00:00',
    sentAt: status === 'Requested' ? null : '2026-09-23T23:41:00+00:00',
    settledAt: status === 'Settled' ? '2026-09-23T23:45:00+00:00' : null,
    failedAt: status === 'Failed' ? '2026-09-23T23:42:00+00:00' : null,
  });

  // The owner's rule: after a free paid cancellation the customer must never see only "Deposit Paid". Rendered
  // in Arabic, the site's default language.
  it('says the refund was initiated, never only that the deposit was paid', async () => {
    const page = await render(refund('Sent'));
    const text = page.textContent ?? '';

    expect(text).toContain('بدأ الاسترداد');
    expect(text).toContain('بالكامل إلى وسيلة الدفع الأصلية');
    // No status anywhere calls the refunded deposit simply "Paid": the payment reads as being refunded.
    const badges = [...page.querySelectorAll('.badge')].map((badge) => badge.textContent?.trim());
    expect(badges).not.toContain('مدفوع');
    expect(badges).toContain('قيد الاسترداد');
  });

  it('says it was refunded once the provider settles it', async () => {
    const text = (await render(refund('Settled'))).textContent ?? '';

    expect(text).toContain('تم الاسترداد');
    expect(text).toContain('تم استرداد عربونك');
  });

  it('says a refused refund is still owed and being retried', async () => {
    const text = (await render(refund('Failed'))).textContent ?? '';

    expect(text).toContain('تأخر الاسترداد');
    expect(text).toContain('مستحقًا لك');
  });
});

describe('refundStage', () => {
  it('reads requested and sent alike, settled as done, and failed as delayed', () => {
    const at = (status: string) => refundStage({ status, amount: { amount: 40, currency: 'JOD' }, requestedAt: '', sentAt: null, settledAt: null, failedAt: null });
    expect(at('Requested')).toBe('initiated');
    expect(at('Sent')).toBe('initiated');
    expect(at('Settled')).toBe('done');
    expect(at('Failed')).toBe('delayed');
  });
});

describe('BookingDetailComponent, an approved booking awaiting payment', () => {
  let http: HttpTestingController;
  const money = (amount: number) => ({ amount, currency: 'JOD' });

  // The owner's example: 250 JOD booking, 50 deposit. Figures as the server sends them.
  const OPTIONS = [
    { purpose: 'Deposit', selectedPaymentAmount: money(50), processingFee: money(0), totalChargedNow: money(50), remainingBalanceAfter: money(200) },
    { purpose: 'FullPayment', selectedPaymentAmount: money(250), processingFee: money(3.75), totalChargedNow: money(253.75), remainingBalanceAfter: money(0) },
  ];
  const APPROVED = {
    ...CONFIRMED,
    status: 'Approved',
    depositPaid: false,
    isAwaitingPayment: true,
    paymentDeadline: new Date(Date.now() + 90 * 60_000).toISOString(),
    pricing: { ...CONFIRMED.pricing, totalPrice: money(250), depositAmount: money(50), balanceDue: money(200) },
    payment: { canPay: true, unavailableReason: null, amountDue: money(50), payBy: null, liveAttempt: null, options: OPTIONS },
  };

  async function render() {
    TestBed.configureTestingModule({
      imports: [BookingDetailComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(BookingDetailComponent);
    fixture.componentRef.setInput('bookingId', ID);
    const settle = async () => {
      for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();
    };
    await settle();
    http.match((request) => request.url === BOOKING_URL).forEach((read) => read.flush(APPROVED));
    await settle();
    return { page: fixture.nativeElement as HTMLElement, settle };
  }

  const payButton = (page: HTMLElement) => page.querySelector<HTMLButtonElement>('.pay-box .btn--primary')!;

  it('offers both ways to pay, deposit chosen, with the server\'s own figures', async () => {
    const { page } = await render();
    const options = page.querySelectorAll('.pay-option');

    expect(options).toHaveLength(2);
    expect(options[0].textContent).toContain('دفع العربون فقط');
    expect(options[1].textContent).toContain('دفع المبلغ كاملًا');
    expect(options[0].classList).toContain('is-selected');
    // Without /app-config the test page prints money without its fixed decimals; the figure is what matters.
    expect(payButton(page).textContent).toMatch(/[^0-9.]50(.000)? JOD/);
    expect(page.querySelector('.pay-summary')!.textContent).toMatch(/200(.000)? JOD/);
  });

  it('follows the choice: the full amount, its fee shown, nothing left after', async () => {
    const { page, settle } = await render();

    page.querySelectorAll<HTMLInputElement>('.pay-option input')[1].dispatchEvent(new Event('change'));
    await settle();

    expect(page.querySelectorAll('.pay-option')[1].classList).toContain('is-selected');
    expect(payButton(page).textContent).toMatch(/253.750? JOD/);
    const summary = page.querySelector('.pay-summary')!.textContent ?? '';
    expect(summary).toMatch(/[^0-9.]3.750? JOD/);
    expect(summary).toMatch(/[^0-9.]0(.000)? JOD/);
  });

  it('asks the server for a checkout by PURPOSE, never by amount', async () => {
    const { page, settle } = await render();
    page.querySelectorAll<HTMLInputElement>('.pay-option input')[1].dispatchEvent(new Event('change'));
    await settle();

    payButton(page).click();
    await settle();

    const request = http.expectOne((r) => r.method === 'POST' && r.url === `${BOOKING_URL}/checkout`);
    expect(request.request.body).toEqual({ purpose: 'FullPayment' });
  });
});

/**
 * Paid by deposit or in full (owner, 2026-09-25). The figures were right after a full payment, but
 * the page still called it a deposit: "Your deposit is paid", a "Deposit (20%) … Paid" line while
 * 102.750 had been charged, and a "Deposit paid" step. Booking B of the browser E2E, as the API sends
 * it: 90.000 rental + 12.750 delivery = 102.750, an 18.000 deposit.
 */
describe('BookingDetailComponent, paid by deposit or in full', () => {
  const money = (amount: number) => ({ amount, currency: 'JOD' });
  const PRICING = {
    ...CONFIRMED.pricing,
    dailyRate: money(30), days: 3, rentalTotal: money(90), deliveryFee: money(12.75), totalPrice: money(102.75), depositAmount: money(18),
  };
  const paidBy = (purpose: 'Deposit' | 'FullPayment') => {
    const full = purpose === 'FullPayment';
    const charged = full ? 102.75 : 18;
    return {
      ...CONFIRMED,
      pickupMethod: 'Delivery',
      pricing: { ...PRICING, balanceDue: money(full ? 0 : 84.75) },
      onlinePaid: money(charged),
      isPaidInFull: full,
      confirmingPayment: {
        purpose, amountCharged: money(charged), processingFee: money(0), appliedToBooking: money(charged),
        paidAt: '2026-09-25T17:56:00+00:00', refundOnFreeCancellation: money(charged),
      },
    };
  };
  const DEPOSIT = paidBy('Deposit');
  const FULL = paidBy('FullPayment');
  /**
   * An amount as the page prints it: English puts the code first ("JOD 102.75"), Arabic after it,
   * and without /app-config the fixed decimals are not applied. The figure is what matters.
   */
  const amount = (figure: string) => {
    const digits = figure.replace('.', '[.]') + (figure.includes('.') ? '0?' : '([.]000)?');
    return new RegExp(`(JOD\\s*${digits}|${digits}\\s*JOD)`);
  };

  afterEach(() => {
    sessionStorage.removeItem(`kh.checkout.${ID}`);
    vi.useRealTimers();
  });

  /** The financial state served beside the two fixtures above. */
  const FINANCIALS_DEPOSIT = financials({
    status: 'Confirmed', purpose: 'Deposit', charged: 18,
    balance: { state: 'DueAtHandover', amount: 84.75 }, deposit: { state: 'Held', amount: 18 },
  });
  const FINANCIALS_FULL = financials({
    status: 'Confirmed', purpose: 'FullPayment', charged: 102.75,
    balance: { state: 'PaidInFull', amount: 0 }, deposit: { state: 'Held', amount: 18 },
  });

  /** Renders the booking in one language; `returning` is the customer coming back from checkout. */
  async function render(booking: object, language: 'ar' | 'en', returning = false, money?: object) {
    TestBed.configureTestingModule({
      imports: [BookingDetailComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(I18nService).use(language);
    if (returning) {
      // Coming back from checkout: the page's first look at the payment is three seconds in. Only
      // timeouts are faked, and they still advance with real time, so the settling below works.
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'], shouldAdvanceTime: true });
      sessionStorage.setItem(`kh.checkout.${ID}`, '01a0d9b6-f5e1-7f85-a4b7-fd81671e7d40');
    }
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(BookingDetailComponent);
    fixture.componentRef.setInput('bookingId', ID);
    const settle = async () => {
      for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();
    };
    await settle();
    http.match((request) => request.url === BOOKING_URL).forEach((read) => read.flush(booking));
    await settle();
    if (money) {
      http.match((request) => request.url === FINANCIALS_URL).forEach((read) => read.flush(money));
      await settle();
    }
    if (returning) {
      await vi.advanceTimersByTimeAsync(3_100);
      await settle();
      if (money) {
        http.match((request) => request.url === FINANCIALS_URL).forEach((read) => read.flush(money));
        await settle();
      }
    }
    const page = fixture.nativeElement as HTMLElement;
    const lineIn = (section: string) => (label: string) =>
      [...page.querySelectorAll(`section[aria-labelledby=${section}] dl.lines > div`)]
        .find((row) => row.querySelector('dt')?.textContent?.trim() === label)
        ?.querySelector('dd')?.textContent?.trim() ?? null;
    const payments = page.querySelector('section[aria-labelledby=payments-title]');
    return {
      text: page.textContent ?? '',
      banner: page.querySelector('.notice[role=status]')?.textContent?.trim() ?? '',
      stages: [...page.querySelectorAll('.timeline__label')].map((label) => label.textContent?.trim()),
      /** The price card's figure beside a label, or null when the card has no such line. */
      line: lineIn('payment-title'),
      /** The Payments section's figure beside a label (payments Phase 4). */
      paid: lineIn('payments-title'),
      /** The Payments section's words, history included. */
      payments: payments?.textContent ?? '',
    };
  }

  it('keeps the deposit wording when only the deposit was paid (English)', async () => {
    const page = await render(DEPOSIT, 'en', true, FINANCIALS_DEPOSIT);

    expect(page.banner).toBe('Your deposit is paid. The booking is confirmed.');
    expect(page.stages).toContain('Deposit paid');
    expect(page.stages).not.toContain('Paid in full');
    // The price card states the frozen deposit; what was paid is the Payments section's (Phase 4).
    expect(page.line('Deposit (20%)')).toMatch(amount('18'));
    expect(page.paid('Paid online')).toMatch(amount('18'));
    expect(page.paid('Paid to the office at pickup')).toMatch(amount('84.75'));
    expect(page.payments).toContain('Deposit payment');
    expect(page.payments).toContain('Paid');
    expect(page.text).not.toContain('Paid in full');
  });

  it('says a booking paid in full is paid in full, with what was charged and nothing left (English)', async () => {
    const page = await render(FULL, 'en', true, FINANCIALS_FULL);

    expect(page.banner).toBe('Paid in full. The booking is confirmed.');
    expect(page.text).not.toContain('Your deposit is paid');
    expect(page.stages).toContain('Paid in full');
    expect(page.stages).not.toContain('Deposit paid');
    expect(page.payments).toContain('Full payment');
    expect(page.paid('Paid online')).toMatch(amount('102.75'));
    expect(page.paid('Paid to the office at pickup')).toBeNull();
    expect(page.line('Deposit (20%)')).toBeNull();
    expect(page.payments).toContain('You paid the whole booking online, so there is nothing to pay the office.');
  });

  it('keeps the deposit wording in Arabic', async () => {
    const page = await render(DEPOSIT, 'ar', true, FINANCIALS_DEPOSIT);

    expect(page.banner).toBe('تم دفع العربون، والحجز مؤكد.');
    expect(page.stages).toContain('دُفع العربون');
    expect(page.stages).not.toContain('دُفع المبلغ كاملًا');
    expect(page.payments).toContain('دفعة العربون');
    expect(page.payments).not.toContain('الدفع الكامل');
  });

  it('says paid in full in Arabic, never the deposit', async () => {
    const page = await render(FULL, 'ar', true, FINANCIALS_FULL);

    expect(page.banner).toBe('تم دفع المبلغ كاملًا، والحجز مؤكد.');
    expect(page.stages).toContain('دُفع المبلغ كاملًا');
    expect(page.stages).not.toContain('دُفع العربون');
    expect(page.payments).toContain('الدفع الكامل');
    expect(page.paid('المدفوع عبر الإنترنت')).toMatch(amount('102.75'));
    expect(page.text).not.toContain('تم دفع العربون');
    expect(page.payments).toContain('فلا يتبقى عليك شيء للمكتب');
  });

  it("promises the whole payment back on a free cancellation, from the server's refund figure", async () => {
    const cancellable = { ...FULL, cancellation: { ...FULL.cancellation, canCancel: true, isFree: true, willRefundDeposit: true } };

    const english = await render(cancellable, 'en');

    expect(english.text).toMatch(new RegExp(`Free cancellation[.] ${amount('102.75').source} will be refunded to your original payment method`));
    expect(english.text).not.toContain('Your deposit will be refunded');
  });

  // ── Phase 3 (owner, 2026-09-26): what a booking paid in full gets back when it ends early ──
  const PAST_WINDOW_PENALTY = {
    attributedTo: 'Customer', minPercent: 100, maxPercent: 100, minAmount: money(18), maxAmount: money(18),
    isRange: false, isNothingOwed: false, requiresTicketToEnforce: true, reason: '', reasonCode: 'CustomerCancelledAfterFreeWindow',
    assessedAt: '2026-09-25T20:00:00+00:00',
  };
  const lateCancellable = {
    ...FULL,
    cancellation: { canCancel: true, isFree: false, willRefundDeposit: false, penalty: PAST_WINDOW_PENALTY, refundAmount: money(84.75) },
  };
  const refundRow = (reason: string, status: string, figure: number) => ({
    refundId: `r-${reason}`, paymentId: 'p-1', reason, amount: money(figure), status,
    requestedAt: '2026-09-25T20:00:00+00:00', sentAt: status === 'Requested' ? null : '2026-09-25T20:01:00+00:00',
    settledAt: status === 'Settled' ? '2026-09-25T20:05:00+00:00' : null, failedAt: null, disputeTicketId: null,
  });
  const lateCancelled = {
    ...FULL,
    status: 'Cancelled',
    isTerminal: true,
    cancelledBy: 'Customer',
    finishedAt: '2026-09-25T20:00:00+00:00',
    penalty: PAST_WINDOW_PENALTY,
    depositRefund: null,
    refunds: [refundRow('EndedBeforePickup', 'Settled', 84.75)],
    refundedAmount: money(84.75),
    refundOutstandingAmount: money(0),
  };

  /** The cancel sheet, opened, in one language; with the platform's reasons when a test confirms. */
  async function openSheet(booking: object, language: 'ar' | 'en', withReasons = false) {
    TestBed.configureTestingModule({
      imports: [BookingDetailComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(I18nService).use(language);
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(BookingDetailComponent);
    fixture.componentRef.setInput('bookingId', ID);
    const settle = async () => {
      for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();
    };
    await settle();
    http.match((request) => request.url === BOOKING_URL).forEach((read) => read.flush(booking));
    if (withReasons) {
      // The reasons are the platform's vocabulary, published on /app-config.
      void TestBed.inject(AppConfigService).load();
      http.expectOne('/api/v1/app-config').flush({
        currency: { code: 'JOD', minorUnits: 3 },
        vocabularies: { cancellationReasons: [{ name: 'PlansChanged', labelEn: 'Plans changed', labelAr: 'تغيّرت الخطط' }], rejectionReasons: [] },
      });
    }
    await settle();
    const page = fixture.nativeElement as HTMLElement;
    const dialog = page.querySelector('dialog') as HTMLDialogElement;
    // jsdom has no modal dialogs; the sheet's content is what is under test.
    dialog.showModal = () => dialog.setAttribute('open', '');
    (page.querySelector('button.btn--danger') as HTMLButtonElement).click();
    await settle();
    return { http, page, dialog, settle };
  }

  /** Pre-launch item 173: the penalty notice states where the penalty stands, from the server's state. */
  const withPenaltyState = (state: string | null) => ({ ...lateCancelled, penalty: { ...PAST_WINDOW_PENALTY, state } });
  /** What a reader sees: without the direction isolates the dictionary wraps each value in. */
  const visible = (text: string) => text.replace(/[\u2068\u2069]/g, '');

  it('says an assessed penalty has charged nothing yet, and a resolved one where its final amount is, in both languages', async () => {
    const assessed = await render(withPenaltyState('Assessed'), 'en');
    expect(assessed.text).toContain('A penalty has been assessed, but no amount has been charged yet.');
    expect(assessed.text).not.toContain('resolved through a dispute');
    TestBed.resetTestingModule();

    const resolved = await render(withPenaltyState('ResolvedByDispute'), 'en');
    expect(resolved.text).toContain('This penalty was resolved through a dispute. See Payments for the final amount.');
    expect(resolved.text).not.toContain('no amount has been charged yet');
    TestBed.resetTestingModule();

    const arabicAssessed = await render(withPenaltyState('Assessed'), 'ar');
    expect(arabicAssessed.text).toContain('تم تقدير جزاء، ولكن لم يتم خصم أي مبلغ بعد.');
    expect(arabicAssessed.text).not.toContain('تم حسم هذا الجزاء');
    TestBed.resetTestingModule();

    const arabicResolved = await render(withPenaltyState('ResolvedByDispute'), 'ar');
    expect(arabicResolved.text).toContain('تم حسم هذا الجزاء من خلال نزاع. راجع قسم المدفوعات لمعرفة المبلغ النهائي.');
    expect(arabicResolved.text).not.toContain('لم يتم خصم أي مبلغ بعد');
  });

  it("says a penalty the ledger kept from the deposit was kept, in the owner's approved words (payments Phase 8)", async () => {
    const kept = await render(withPenaltyState('KeptFromDeposit'), 'en');
    expect(kept.text).toContain('The dispute window ended without a dispute. The assessed deposit penalty has now been finalized and applied according to the booking’s cancellation terms.');
    expect(kept.text).not.toContain('no amount has been charged yet');
    TestBed.resetTestingModule();

    const arabicKept = await render(withPenaltyState('KeptFromDeposit'), 'ar');
    expect(arabicKept.text).toContain('انتهت مهلة النزاع دون فتح نزاع. تم تثبيت حسم العربون وتطبيقه وفق شروط إلغاء الحجز.');
    // «التأمين» is the security deposit, which this page names in its own right; the booking deposit is «العربون».
    expect(arabicKept.text).not.toContain('حسم مبلغ التأمين');
  });

  it('says a penalty on the customer is assessed against them in words their language allows', async () => {
    const english = await render(withPenaltyState('Assessed'), 'en');
    expect(english.text).toContain('An amount of');
    expect(english.text).toContain('has been assessed against you.');
    TestBed.resetTestingModule();

    // «عليك», never «على أنت»: the reader's own penalty is not the party word dropped into a sentence. Checked on the
    // visible words: every value sits in a direction isolate, which would hide the old phrase from a plain search.
    const arabic = await render(withPenaltyState('Assessed'), 'ar');
    expect(arabic.text).toContain('قُدِّر مبلغ');
    expect(arabic.text).toContain('عليك.');
    expect(visible(arabic.text)).not.toContain('على أنت');
  });

  it('says who made each change in the history in words their language allows (pre-launch item 218)', async () => {
    const english = await render(withPenaltyState('Assessed'), 'en');
    expect(visible(english.text)).toContain('· by you');
    expect(visible(english.text)).toContain('· by the rental office');
    TestBed.resetTestingModule();

    // «من قِبلك», never «بواسطة أنت»: the reader's own act has a phrase of its own; the office is named as before.
    const arabic = visible((await render(withPenaltyState('Assessed'), 'ar')).text);
    expect(arabic).toContain('· من قِبلك');
    expect(arabic).toContain('· بواسطة مكتب التأجير');
    expect(arabic).not.toContain('بواسطة أنت');
  });

  it('never again says nothing has been charged, and says nothing under a penalty state it does not know', async () => {
    const unknown = await render(withPenaltyState('SomethingNewer'), 'en');
    const missing = { ...lateCancelled, penalty: PAST_WINDOW_PENALTY };

    // The assessment itself is still stated; only the sentence about where it stands is left out.
    expect(unknown.text).toMatch(amount('18'));
    expect(unknown.text).not.toContain('Nothing has been charged');
    expect(unknown.text).not.toContain('no amount has been charged yet');
    expect(unknown.text).not.toContain('resolved through a dispute');
    TestBed.resetTestingModule();

    const olderApi = await render(missing, 'ar');
    expect(olderApi.text).not.toContain('لم يُخصم شيء');
    expect(olderApi.text).not.toContain('لم يتم خصم أي مبلغ بعد');
  });

  it('tells a customer cancelling a full payment late what comes back above the deposit, in both languages', async () => {
    const english = await openSheet(lateCancellable, 'en');
    expect(english.dialog.textContent).toMatch(new RegExp(`You will get ${amount('84.75').source} back to your original payment method: everything you paid above the deposit`));
    expect(english.dialog.textContent).toContain('Cancelling now assesses');
    expect(english.dialog.textContent).not.toContain('Free cancellation');
    TestBed.resetTestingModule();

    const arabic = await openSheet(lateCancellable, 'ar');
    expect(arabic.dialog.textContent).toContain('كل ما دفعته فوق العربون');
    expect(arabic.dialog.textContent).toMatch(amount('84.75'));
  });

  it('sends the refund it showed, and on a changed refund shows the new figure and cancels nothing', async () => {
    const sheet = await openSheet(lateCancellable, 'en', true);
    (sheet.dialog.querySelector('input[type=radio]') as HTMLInputElement).click();
    await sheet.settle();
    const confirm = [...sheet.dialog.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Cancel the booking')!;
    confirm.click();
    await sheet.settle();

    const post = sheet.http.expectOne((request) => request.method === 'POST' && request.url === `${BOOKING_URL}/cancel`);
    expect(post.request.body).toEqual({ reasonCode: 'PlansChanged', details: null, expectedRefund: 84.75 });
    post.flush(
      { status: 409, title: 'The refund for cancelling this booking has changed.', code: 'booking.refund_changed', currentRefund: { amount: 66.75, currency: 'JOD' } },
      { status: 409, statusText: 'Conflict' },
    );
    await sheet.settle();

    expect(sheet.dialog.textContent).toMatch(new RegExp(`What you would get back has changed to ${amount('66.75').source}`));
    expect(sheet.dialog.hasAttribute('open')).toBe(true);
    // Read again, so the sheet states the new figure before the customer confirms.
    expect(sheet.http.match((request) => request.url === BOOKING_URL).length).toBeGreaterThan(0);
  });

  it('reads the changed refund from the refusal, and nothing from any other refusal', () => {
    const refusal = { status: 409, error: { code: 'booking.refund_changed', currentRefund: { amount: 84.75, currency: 'JOD' } } };
    expect(changedRefundOf(refusal)).toEqual({ amount: 84.75, currency: 'JOD' });
    expect(changedRefundOf({ status: 409, error: { code: 'booking.not_cancellable' } })).toBeNull();
    expect(changedRefundOf({ status: 409, error: { code: 'booking.refund_changed', currentRefund: { amount: '84.75', currency: 'JOD' } } })).toBeNull();
    expect(changedRefundOf(null)).toBeNull();
  });

  /** The financial state of the late-cancelled booking above: the refund above the deposit, settled. */
  const lateCancelledMoney = financials({
    status: 'Cancelled', purpose: 'FullPayment', charged: 102.75,
    balance: { state: 'NotDue', amount: 0 }, deposit: { state: 'HeldForAssessedPenalty', amount: 18 },
    refunds: [financialRefund('EndedBeforePickup', 'Settled', 84.75)], progress: 'Partial', refunded: 84.75,
  });

  it('lists every refund with why, how much and where it is, and the totals the server sent (English)', async () => {
    const page = await render(lateCancelled, 'en', false, lateCancelledMoney);

    expect(page.text).toMatch(new RegExp(`Everything you paid above the deposit, ${amount('84.75').source}, was refunded to your original payment method`));
    expect(page.payments).toContain('Payments');
    expect(page.payments).toContain('Paid above the deposit');
    expect(page.paid('Refunded to you')).toMatch(amount('84.75'));
    expect(page.paid('Refund in progress')).toBeNull();
    // Part of it came back: the payment reads "Partly refunded", never "Paid in full".
    expect(page.payments).toContain('Partly refunded');
    expect(page.payments).not.toContain('Paid in full');
    expect(page.payments).toContain('Nothing further is due on this booking.');
  });

  it('lists the refunds in Arabic, the deposit release included', async () => {
    const released = {
      ...lateCancelled,
      cancelledBy: 'Dealer',
      penalty: { ...PAST_WINDOW_PENALTY, attributedTo: 'Dealer' },
      refunds: [refundRow('EndedBeforePickup', 'Settled', 84.75), refundRow('DisputeWindowClosed', 'Sent', 18)],
      depositRefund: { status: 'Sent', amount: money(18), requestedAt: '2026-09-27T20:00:00+00:00', sentAt: '2026-09-27T20:01:00+00:00', settledAt: null, failedAt: null },
      refundedAmount: money(84.75),
      refundOutstandingAmount: money(18),
    };

    const releaseLine = financialRefund('DisputeWindowClosed', 'Sent', 18);
    const page = await render(released, 'ar', false, financials({
      status: 'Cancelled', purpose: 'FullPayment', charged: 102.75,
      balance: { state: 'NotDue', amount: 0 }, deposit: { state: 'Released', amount: 18, refund: releaseLine },
      refunds: [financialRefund('EndedBeforePickup', 'Settled', 84.75), releaseLine], progress: 'InProgress',
      refunded: 84.75, inProgress: 18,
    }));

    expect(page.payments).toContain('المدفوعات');
    expect(page.payments).toContain('المدفوع فوق العربون');
    expect(page.payments).toContain('إعادة العربون');
    expect(page.text).toContain('يجري استرداد عربونك');
    expect(page.paid('قيد الاسترداد')).toMatch(amount('18'));
    expect(page.payments).toContain('بدأ الاسترداد');
    expect(page.payments).toContain('أُعيد إليك عربونك');
  });

  it('words the refund of a booking paid in full as a refund of the payment', async () => {
    const cancelled = {
      ...FULL,
      status: 'Cancelled',
      cancelledBy: 'Customer',
      finishedAt: '2026-09-25T18:00:00+00:00',
      depositRefund: {
        status: 'Sent', amount: money(102.75), requestedAt: '2026-09-25T18:00:00+00:00',
        sentAt: '2026-09-25T18:01:00+00:00', settledAt: null, failedAt: null,
      },
    };

    const wholeLine = financialRefund('FreeCancellation', 'Sent', 102.75);
    const arabic = await render(cancelled, 'ar', false, financials({
      status: 'Cancelled', purpose: 'FullPayment', charged: 102.75,
      balance: { state: 'NotDue', amount: 0 }, deposit: { state: 'ReturnedWithPayment', amount: 18, refund: wholeLine },
      refunds: [wholeLine], progress: 'InProgress', inProgress: 102.75,
    }));

    expect(arabic.text).toContain('من دفعتك');
    // The status notice words the whole payment's refund; the Payments section lists it.
    expect(arabic.payments).toContain('الدفع الكامل');
    expect(arabic.payments).toContain('بدأ الاسترداد');
    expect(arabic.paid('قيد الاسترداد')).toMatch(amount('102.75'));
  });
});
