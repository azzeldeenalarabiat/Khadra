import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { I18nService } from '../../core/i18n/i18n.service';
import { BookingDetailComponent, refundStage } from './booking-detail.component';

const ID = '01a0d095-c204-7f23-b0b3-1be7852e4b23';
const BOOKING_URL = `/api/v1/bookings/${ID}`;
const CODE_URL = `${BOOKING_URL}/handover-code`;
const money = (amount: number) => ({ amount, currency: 'JOD' });

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
    expect(text).not.toContain('مدفوع');
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

  /** Renders the booking in one language; `returning` is the customer coming back from checkout. */
  async function render(booking: object, language: 'ar' | 'en', returning = false) {
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
    if (returning) {
      await vi.advanceTimersByTimeAsync(3_100);
      await settle();
    }
    const page = fixture.nativeElement as HTMLElement;
    return {
      text: page.textContent ?? '',
      banner: page.querySelector('.notice[role=status]')?.textContent?.trim() ?? '',
      stages: [...page.querySelectorAll('.timeline__label')].map((label) => label.textContent?.trim()),
      /** The payment card's figure beside a label, or null when the card has no such line. */
      line: (label: string) =>
        [...page.querySelectorAll('section[aria-labelledby=payment-title] dl.lines > div')]
          .find((row) => row.querySelector('dt')?.textContent?.trim() === label)
          ?.querySelector('dd')?.textContent?.trim() ?? null,
    };
  }

  it('keeps the deposit wording when only the deposit was paid (English)', async () => {
    const page = await render(DEPOSIT, 'en', true);

    expect(page.banner).toBe('Your deposit is paid. The booking is confirmed.');
    expect(page.stages).toContain('Deposit paid');
    expect(page.stages).not.toContain('Paid in full');
    expect(page.line('Deposit (20%)')).toMatch(amount('18'));
    expect(page.line('Deposit (20%)')).toMatch(/Paid$/);
    expect(page.line('Paid to the office at pickup')).toMatch(amount('84.75'));
    expect(page.line('Payment type')).toBeNull();
    expect(page.text).not.toContain('Paid in full');
  });

  it('says a booking paid in full is paid in full, with what was charged and nothing left (English)', async () => {
    const page = await render(FULL, 'en', true);

    expect(page.banner).toBe('Paid in full. The booking is confirmed.');
    expect(page.text).not.toContain('Your deposit is paid');
    expect(page.stages).toContain('Paid in full');
    expect(page.stages).not.toContain('Deposit paid');
    expect(page.line('Payment type')).toMatch(/^Full payment\s*Paid in full$/);
    expect(page.line('Amount charged')).toMatch(amount('102.75'));
    expect(page.line('Remaining balance')).toMatch(amount('0'));
    expect(page.line('Remaining balance')).not.toMatch(/[1-9]/);
    expect(page.line('Deposit (20%)')).toBeNull();
    expect(page.line('Paid to the office at pickup')).toBeNull();
    expect(page.text).toContain('You paid the whole booking online, so there is nothing to pay the office.');
  });

  it('keeps the deposit wording in Arabic', async () => {
    const page = await render(DEPOSIT, 'ar', true);

    expect(page.banner).toBe('تم دفع العربون، والحجز مؤكد.');
    expect(page.stages).toContain('دُفع العربون');
    expect(page.stages).not.toContain('دُفع المبلغ كاملًا');
    expect(page.line('نوع الدفع')).toBeNull();
  });

  it('says paid in full in Arabic, never the deposit', async () => {
    const page = await render(FULL, 'ar', true);

    expect(page.banner).toBe('تم دفع المبلغ كاملًا، والحجز مؤكد.');
    expect(page.stages).toContain('دُفع المبلغ كاملًا');
    expect(page.stages).not.toContain('دُفع العربون');
    expect(page.line('نوع الدفع')).toContain('دفع كامل');
    expect(page.line('المبلغ المدفوع')).toMatch(amount('102.75'));
    expect(page.line('المبلغ المتبقي')).toMatch(amount('0'));
    expect(page.line('المبلغ المتبقي')).not.toMatch(/[1-9]/);
    expect(page.text).not.toContain('تم دفع العربون');
    expect(page.text).toContain('فلا يتبقى عليك شيء للمكتب');
  });

  it("promises the whole payment back on a free cancellation, from the server's refund figure", async () => {
    const cancellable = { ...FULL, cancellation: { ...FULL.cancellation, canCancel: true, isFree: true, willRefundDeposit: true } };

    const english = await render(cancellable, 'en');

    expect(english.text).toMatch(new RegExp(`Free cancellation[.] ${amount('102.75').source} will be refunded to your original payment method`));
    expect(english.text).not.toContain('Your deposit will be refunded');
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

    const arabic = await render(cancelled, 'ar');

    expect(arabic.text).toContain('من دفعتك');
    expect(arabic.text).not.toContain('عربونك');
    expect(arabic.line('المبلغ المدفوع')).toContain('بدأ الاسترداد');
  });
});
