import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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
  commissionAmount: money(40),
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
