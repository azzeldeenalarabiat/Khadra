using Khadra.Application.AdminDashboard;
using Khadra.Application.Common;
using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Disputes;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The administrator's money screens against the real EF model (payments Phase 4b): the payments list,
/// the refunds queue, a payment's provider events, the finance panel's facts, the work queue's money
/// rows, and the deposits pre-launch item 164 is about. Each is a query that compiles and could quietly
/// mean something else, so each is pinned against rows that should, and rows that should not, be found.
/// </summary>
public sealed class PaymentAdminReaderTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public PaymentAdminReaderTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private KhadraDbContext NewContext() => new(_options);

    private static void Settle(Refund refund, DateTimeOffset at)
    {
        refund.MarkSent("rf_" + refund.Id.Value.ToString("N"), at);
        refund.MarkSettled(at);
    }

    private async Task SaveAsync(
        IEnumerable<Booking>? bookings = null,
        IEnumerable<Payment>? payments = null,
        IEnumerable<DisputeTicket>? tickets = null,
        IEnumerable<Dealer>? dealers = null,
        IEnumerable<User>? users = null,
        IEnumerable<ProviderEventReceipt>? receipts = null)
    {
        await using var context = NewContext();
        context.Bookings.AddRange(bookings ?? []);
        context.Payments.AddRange(payments ?? []);
        context.DisputeTickets.AddRange(tickets ?? []);
        context.Dealers.AddRange(dealers ?? []);
        context.Users.AddRange(users ?? []);
        context.ProviderEventReceipts.AddRange(receipts ?? []);
        await context.SaveChangesAsync();
    }

    private static AdminPaymentFilter NoFilter => new(null, null, null, null, null, null, null);

    // ── The payments list ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_reads_newest_first_with_its_labels_and_each_payments_own_refund_progress()
    {
        var dealer = Build.ApprovedDealer(businessName: "Petra Rentals");
        var customer = Build.Customer();
        var (older, olderPayment) = Build.PaidBooking(inFull: true, fee: 4.5m, now: Now, customerId: customer.Id, dealerId: dealer.Id);
        Assert.True(older.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now.AddHours(3)).IsSuccess);
        Settle(olderPayment.RefundAboveDeposit(Money.Jod(72m), Now.AddHours(3)).Value!, Now.AddHours(4));
        var (newer, newerPayment) = Build.PaidBooking(now: Now.AddDays(1), customerId: customer.Id, dealerId: dealer.Id);
        await SaveAsync([older, newer], [olderPayment, newerPayment], dealers: [dealer], users: [customer]);

        await using var read = NewContext();
        var page = await new PaymentAdminReader(read).ListPaymentsAsync(NoFilter, PageRequest.From(1, 20));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal([newerPayment.Id.Value, olderPayment.Id.Value], page.Items.Select(row => row.PaymentId));
        var row = page.Items[1];
        Assert.Equal(older.Reference.Value, row.BookingReference);
        Assert.Equal("Petra Rentals", row.DealerName);
        Assert.Equal(dealer.Id.Value, row.DealerId);
        Assert.Equal("Rana Sharif", row.CustomerName);
        Assert.Equal("FullPayment", row.Purpose);
        Assert.Equal("Applied", row.Status);
        // The payment's own verdict, never a second reading of its refunds.
        Assert.Equal(olderPayment.RefundProgress.Name, row.RefundProgress);
        Assert.Equal("Partial", row.RefundProgress);
        Assert.Equal(94.5m, row.AmountCharged.Amount);
        Assert.Equal(4.5m, row.ProcessingFee.Amount);
        Assert.Equal(90m, row.AppliedToBooking.Amount);
        // Everything above the deposit went back WITH the refundable fee: 72 of booking money and 4.5 of fee.
        Assert.Equal(76.5m, row.RefundSettled.Amount);
        Assert.False(row.IsSandbox);
        Assert.Equal("sess_" + older.Reference.Value, row.ProviderReference);
    }

    [Fact]
    public async Task The_list_filters_by_status_purpose_office_customer_reference_and_amman_days()
    {
        var dealer = Id.New();
        var customer = Id.New();
        var (mine, minePayment) = Build.PaidBooking(inFull: true, now: Now, customerId: customer, dealerId: dealer);
        var (other, otherPayment) = Build.PaidBooking(now: Now.AddDays(2));
        var unpaid = Build.ApprovedBooking(Now.AddDays(3));
        var failed = Payment.Open(unpaid.Id, unpaid.CustomerId, Money.Jod(18m), "TestProvider", Now.AddDays(3).AddMinutes(30), Now.AddDays(3));
        Assert.True(failed.AttachProviderSession("sess_failed", "https://provider.test/checkout").IsSuccess);
        Assert.True(failed.Fail("card_declined", Now.AddDays(3)).IsSuccess);
        await SaveAsync([mine, other, unpaid], [minePayment, otherPayment, failed]);

        await using var read = NewContext();
        var reader = new PaymentAdminReader(read);
        async Task<IReadOnlyList<Guid>> Ids(AdminPaymentFilter filter) =>
            [.. (await reader.ListPaymentsAsync(filter, PageRequest.From(1, 20))).Items.Select(row => row.PaymentId)];

        Assert.Equal([failed.Id.Value], await Ids(NoFilter with { Status = PaymentStatus.Failed }));
        Assert.Equal([minePayment.Id.Value], await Ids(NoFilter with { Purpose = PaymentPurpose.FullPayment }));
        Assert.Equal([minePayment.Id.Value], await Ids(NoFilter with { DealerId = dealer }));
        Assert.Equal([minePayment.Id.Value], await Ids(NoFilter with { CustomerId = customer }));
        Assert.Equal([otherPayment.Id.Value], await Ids(NoFilter with { Reference = other.Reference.Value.ToLowerInvariant() }));
        Assert.Empty(await Ids(NoFilter with { Reference = "not a reference" }));
        // A half-open range of instants: the second day's payment only.
        Assert.Equal([otherPayment.Id.Value], await Ids(NoFilter with { CreatedFrom = Now.AddDays(1), CreatedBefore = Now.AddDays(3) }));
    }

    [Fact]
    public async Task Pages_never_drop_or_repeat_a_payment_opened_in_the_same_instant()
    {
        var booked = Enumerable.Range(0, 5).Select(_ => Build.PaidBooking(now: Now)).ToList();
        await SaveAsync([.. booked.Select(pair => pair.Booking)], [.. booked.Select(pair => pair.Payment)]);

        await using var read = NewContext();
        var reader = new PaymentAdminReader(read);
        var seen = new List<Guid>();
        for (var page = 1; page <= 5; page++)
            seen.AddRange((await reader.ListPaymentsAsync(NoFilter, PageRequest.From(page, 1))).Items.Select(row => row.PaymentId));

        Assert.Equal(5, seen.Distinct().Count());
        Assert.Equal(booked.Select(pair => pair.Payment.Id.Value).OrderByDescending(id => id), seen);
    }

    // ── The refunds queue ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_queue_ranks_refused_then_recorded_then_sent_each_owed_longest_first_and_settled_apart()
    {
        var pairs = Enumerable.Range(0, 5).Select(_ => Build.PaidBooking(inFull: true)).ToList();
        Refund Above(int index, int hours) =>
            pairs[index].Payment.RefundAboveDeposit(Money.Jod(72m), Now.AddHours(hours)).Value!;
        var sentOld = Above(0, 1);
        sentOld.MarkSent("rf_sent_old", Now.AddHours(1));
        var requestedNew = Above(1, 5);
        var failedNew = Above(2, 4);
        failedNew.MarkFailed("card_closed", Now.AddHours(6));
        var failedOld = Above(3, 2);
        failedOld.MarkFailed("card_closed", Now.AddHours(9));
        var settled = Above(4, 0);
        Settle(settled, Now.AddHours(2));
        await SaveAsync([.. pairs.Select(pair => pair.Booking)], [.. pairs.Select(pair => pair.Payment)]);

        await using var read = NewContext();
        var reader = new PaymentAdminReader(read);
        var live = await reader.ListRefundsAsync(new AdminRefundFilter(null, null, null, null, null), PageRequest.From(1, 20));
        var done = await reader.ListRefundsAsync(new AdminRefundFilter(RefundStatus.Settled, null, null, null, null), PageRequest.From(1, 20));

        // Refused first, ordered by when they were first owed (never by FailedAt, which every refusal rewrites).
        Assert.Equal(
            [failedOld.Id.Value, failedNew.Id.Value, requestedNew.Id.Value, sentOld.Id.Value],
            live.Items.Select(row => row.RefundId));
        var first = live.Items[0];
        Assert.Equal("Failed", first.Status);
        Assert.Equal("EndedBeforePickup", first.Reason);
        Assert.Equal("card_closed", first.FailureCode);
        Assert.Equal(pairs[3].Booking.Reference.Value, first.BookingReference);
        Assert.Equal([settled.Id.Value], done.Items.Select(row => row.RefundId));
    }

    // ── A payment's provider events ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Provider_events_are_tied_by_the_payment_and_by_its_reference_alone()
    {
        var (booking, payment) = Build.PaidBooking();
        var reference = payment.ProviderReference!;
        var early = ProviderEventReceipt.Record("TestProvider", "evt_early", reference, "Captured", null, ProviderEventOutcome.Unknown, Money.Jod(18m), Now);
        var named = ProviderEventReceipt.Record("TestProvider", "evt_named", reference, "Captured", payment.Id, ProviderEventOutcome.Acted, Money.Jod(18m), Now.AddMinutes(1));
        var stranger = ProviderEventReceipt.Record("TestProvider", "evt_other", "sess_someone_else", "Captured", null, ProviderEventOutcome.Unknown, null, Now);
        var otherProvider = ProviderEventReceipt.Record("OtherProvider", "evt_elsewhere", reference, "Captured", null, ProviderEventOutcome.Unknown, null, Now);
        await SaveAsync([booking], [payment], receipts: [early, named, stranger, otherProvider]);

        await using var read = NewContext();
        var events = await new PaymentAdminReader(read).ProviderEventsAsync(payment.Id, payment.Provider, reference);

        Assert.Equal(["evt_early", "evt_named"], events.Select(item => item.ProviderEventId));
        Assert.Equal(["Reference", "Payment"], events.Select(item => item.TiedBy));
        Assert.Equal("Unknown", events[0].Outcome);
        Assert.Equal(18m, events[1].Amount!.Amount);
    }

    // ── The finance panel and the work queue ──────────────────────────────────────────────────────────

    [Fact]
    public async Task The_finance_facts_count_the_month_by_their_own_dates_and_another_currency_apart()
    {
        var monthStart = Now.AddDays(-1);
        var monthEnd = Now.AddDays(10);
        var (inside, insidePayment) = Build.PaidBooking(inFull: true, fee: 4.5m, now: Now);
        var (before, beforePayment) = Build.PaidBooking(now: monthStart.AddDays(-3));
        Settle(beforePayment.RefundHeldDeposit(Money.Jod(18m), Now).Value!, Now.AddHours(1));
        var expired = Build.ApprovedBooking(Now);
        var usdOrphan = Payment.Open(expired.Id, expired.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now);
        Assert.True(usdOrphan.AttachProviderSession("sess_usd", "https://provider.test/checkout").IsSuccess);
        Assert.True(usdOrphan.Orphan(Money.Create(25.4m, "USD"), Now, "booking.not_awaiting_payment", Now).IsSuccess);
        await SaveAsync([inside, before, expired], [insidePayment, beforePayment, usdOrphan]);

        await using var read = NewContext();
        var facts = await new PaymentDashboardReader(read).FinanceAsync(monthStart, monthEnd);
        var summary = FinanceSummaryBuilder.Build(facts, "JOD", "Sandbox", DateOnly.FromDateTime(monthStart.UtcDateTime), DateOnly.FromDateTime(monthEnd.UtcDateTime), Now);

        // Applied this month: the payment's OWN booking money and fee, never the one applied before it.
        Assert.Equal(insidePayment.AppliedToBooking.Amount, summary.ThisMonth.AppliedToBookings.Amount);
        Assert.Equal(1, summary.ThisMonth.PaymentsApplied);
        Assert.Equal(4.5m, summary.ThisMonth.ProcessingFeesCharged.Amount);
        // A refund settles in the month whatever month its payment applied in.
        Assert.Equal(18m, summary.ThisMonth.RefundsSettled.Amount);
        // The orphan went wrong in USD: listed apart, never in a dinar figure.
        Assert.Equal(0m, summary.RightNow.RefundsInProgress.Amount);
        Assert.Equal(25.4m, Assert.Single(summary.OtherCurrencies).RefundsOutstanding.Amount);
    }

    [Fact]
    public async Task Refused_refunds_and_owed_orphans_are_read_for_the_queue_and_a_refused_orphan_only_once()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true);
        var refused = payment.RefundAboveDeposit(Money.Jod(72m), Now).Value!;
        refused.MarkFailed("card_closed", Now.AddMinutes(1));
        var owedBooking = Build.ApprovedBooking(Now);
        var owed = Payment.Open(owedBooking.Id, owedBooking.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now);
        Assert.True(owed.AttachProviderSession("sess_owed", "https://provider.test/checkout").IsSuccess);
        Assert.True(owed.Orphan(Money.Jod(18m), Now, "booking.not_awaiting_payment", Now).IsSuccess);
        var refusedOrphanBooking = Build.ApprovedBooking(Now);
        var refusedOrphan = Payment.Open(refusedOrphanBooking.Id, refusedOrphanBooking.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now);
        Assert.True(refusedOrphan.AttachProviderSession("sess_refused_orphan", "https://provider.test/checkout").IsSuccess);
        Assert.True(refusedOrphan.Orphan(Money.Jod(18m), Now, "booking.not_awaiting_payment", Now).IsSuccess);
        Assert.Single(refusedOrphan.Refunds).MarkFailed("card_closed", Now.AddMinutes(2));
        await SaveAsync([booking, owedBooking, refusedOrphanBooking], [payment, owed, refusedOrphan]);

        await using var read = NewContext();
        var reader = new PaymentDashboardReader(read);
        var failed = await reader.FailedRefundsAsync(refusedAtLeast: 1);
        var orphans = await reader.OwedOrphansAsync();

        Assert.Equal(2, failed.Count);
        Assert.Contains(failed, item => item.RefundId == refused.Id.Value && item.BookingReference == booking.Reference.Value);
        // The refused orphan is a failed refund, and only that.
        Assert.Equal([owed.Id.Value], orphans.Select(item => item.PaymentId));
    }

    /// <summary>
    /// The work queue asks only for refunds refused often enough to need a person (Wave 4, B4); below that the
    /// back-off is handling them. The refunds queue shows each one's count and when it is sent again.
    /// </summary>
    [Fact]
    public async Task Refused_refunds_are_read_for_the_queue_from_the_alert_on_and_listed_with_their_count()
    {
        var (onceBooking, once) = Build.PaidBooking(inFull: true);
        var refusedOnce = once.RefundAboveDeposit(Money.Jod(72m), Now).Value!;
        refusedOnce.RecordRefusedSend("card_closed", Now, TestPayments.RetryPolicy);
        var (thriceBooking, thrice) = Build.PaidBooking(inFull: true);
        var refusedThrice = thrice.RefundAboveDeposit(Money.Jod(72m), Now).Value!;
        refusedThrice.RecordRefusedSend("card_closed", Now, TestPayments.RetryPolicy);
        refusedThrice.RecordRefusedSend("card_closed", Now.AddMinutes(1), TestPayments.RetryPolicy);
        refusedThrice.RecordRefusedSend("card_closed", Now.AddMinutes(3), TestPayments.RetryPolicy);
        await SaveAsync([onceBooking, thriceBooking], [once, thrice]);

        await using var read = NewContext();
        var queued = await new PaymentDashboardReader(read).FailedRefundsAsync(refusedAtLeast: TestPayments.RetryPolicy.RefusalsBeforeAlert);
        var listed = await new PaymentAdminReader(read).ListRefundsAsync(new AdminRefundFilter(null, null, null, null, null), PageRequest.From(1, 20));

        Assert.Equal([refusedThrice.Id.Value], queued.Select(item => item.RefundId));
        var row = Assert.Single(listed.Items, item => item.RefundId == refusedThrice.Id.Value);
        Assert.Equal(3, row.RefusalCount);
        Assert.Equal(Now.AddMinutes(7), row.NextAttemptAt);
        Assert.Equal(1, Assert.Single(listed.Items, item => item.RefundId == refusedOnce.Id.Value).RefusalCount);
    }

    // ── Capture incidents (Wave 4, B1) ────────────────────────────────────────────────────────────────

    private static ProviderEventReceipt Notice(Payment payment, string eventId, ProviderEventOutcome outcome, string? captureReference, DateTimeOffset at) =>
        ProviderEventReceipt.Record(
            "TestProvider", eventId, payment.ProviderReference!, "Captured", payment.Id, outcome, Money.Jod(18m), at, captureReference);

    private static PaymentIncident Raised(
        Payment payment, ProviderEventReceipt receipt, PaymentIncidentKind kind, DateTimeOffset at, Id? otherPaymentId = null) =>
        PaymentIncident.Raise(
            kind, payment.Id, receipt.Id, "TestProvider", receipt.CaptureReference, Money.Jod(18m), Money.Jod(18m), otherPaymentId, at);

    private async Task SaveIncidentsAsync(params PaymentIncident[] incidents)
    {
        await using var context = NewContext();
        context.PaymentIncidents.AddRange(incidents);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// A payment's page reads its own incidents, open ones first, each with the notice that raised it and — once
    /// handled — the administrator's name and note. Another payment's incident is not on it.
    /// </summary>
    [Fact]
    public async Task A_payments_incidents_are_read_open_first_with_who_handled_them()
    {
        var admin = User.CreateAdmin(
            EmailAddress.Create("omar@khadra.jo").Value,
            PhoneNumber.Create("0790000002").Value,
            PersonName.Create("Omar Deeb").Value,
            PasswordHash.FromHash("hash"),
            Now.AddYears(-1));
        var (booking, payment) = Build.PaidBooking();
        var (otherBooking, otherPayment) = Build.PaidBooking();
        var first = Notice(payment, "evt_a", ProviderEventOutcome.SecondCapture, "cap_2", Now.AddHours(1));
        var second = Notice(payment, "evt_b", ProviderEventOutcome.AmountMismatch, "cap_1", Now.AddHours(2));
        var third = Notice(payment, "evt_c", ProviderEventOutcome.OtherAttempt, "cap_9", Now.AddHours(3));
        var elsewhere = Notice(otherPayment, "evt_d", ProviderEventOutcome.SecondCapture, "cap_3", Now);
        await SaveAsync([booking, otherBooking], [payment, otherPayment], users: [admin], receipts: [first, second, third, elsewhere]);

        var handled = Raised(payment, first, PaymentIncidentKind.SecondCapture, Now.AddHours(1));
        Assert.True(handled.MarkHandled(admin.Id, "Refunded at the provider.", Now.AddHours(5)).IsSuccess);
        var mismatch = Raised(payment, second, PaymentIncidentKind.AmountMismatch, Now.AddHours(2));
        var onOther = Raised(payment, third, PaymentIncidentKind.CaptureOnAnotherAttempt, Now.AddHours(3), otherPayment.Id);
        await SaveIncidentsAsync(handled, mismatch, onOther, Raised(otherPayment, elsewhere, PaymentIncidentKind.SecondCapture, Now));

        await using var read = NewContext();
        var reader = new PaymentAdminReader(read);
        var incidents = await reader.IncidentsAsync(payment.Id);

        Assert.Equal([mismatch.Id.Value, onOther.Id.Value, handled.Id.Value], incidents.Select(item => item.IncidentId));
        var open = incidents[0];
        Assert.Equal("AmountMismatch", open.Kind);
        Assert.Equal(second.Id.Value, open.ReceiptId);
        Assert.Equal("cap_1", open.CaptureReference);
        Assert.Equal(18m, open.Reported.Amount);
        Assert.Equal("JOD", open.Expected.Currency);
        Assert.Null(open.HandledAt);
        Assert.Null(open.HandledBy);
        Assert.Equal(otherPayment.Id.Value, incidents[1].OtherPaymentId);
        var closed = incidents[2];
        Assert.Equal(Now.AddHours(5), closed.HandledAt);
        Assert.Equal("Omar Deeb", closed.HandledBy);
        Assert.Equal("Refunded at the provider.", closed.HandledNote);

        // The notices themselves carry the capture reference they were sent with.
        var events = await reader.ProviderEventsAsync(payment.Id, payment.Provider, payment.ProviderReference);
        Assert.Equal(["cap_2", "cap_1", "cap_9"], events.Select(item => item.CaptureReference));
        Assert.Equal(["SecondCapture", "AmountMismatch", "OtherAttempt"], events.Select(item => item.Outcome));

        Assert.Empty(await reader.IncidentsAsync(Id.New()));
    }

    /// <summary>
    /// The work queue reads every incident nobody has handled, oldest first, labelled by its booking's reference where
    /// one resolves; the finance panel counts the same set.
    /// </summary>
    [Fact]
    public async Task Open_incidents_are_read_for_the_queue_oldest_first_and_counted_for_the_finance_panel()
    {
        var (booking, payment) = Build.PaidBooking();
        var (_, orphanedFromItsBooking) = Build.PaidBooking();
        var older = Notice(payment, "evt_old", ProviderEventOutcome.SecondCapture, "cap_2", Now.AddHours(1));
        var newer = Notice(orphanedFromItsBooking, "evt_new", ProviderEventOutcome.AmountMismatch, "cap_1", Now.AddHours(2));
        var done = Notice(payment, "evt_done", ProviderEventOutcome.SecondCapture, "cap_3", Now);
        // The second payment's booking is never saved: its incident still reaches the queue, with no reference.
        await SaveAsync([booking], [payment, orphanedFromItsBooking], receipts: [older, newer, done]);
        var handled = Raised(payment, done, PaymentIncidentKind.SecondCapture, Now);
        Assert.True(handled.MarkHandled(Id.New(), "Dealt with.", Now.AddHours(3)).IsSuccess);
        var newest = Raised(orphanedFromItsBooking, newer, PaymentIncidentKind.AmountMismatch, Now.AddHours(2));
        var oldest = Raised(payment, older, PaymentIncidentKind.SecondCapture, Now.AddHours(1));
        await SaveIncidentsAsync(newest, handled, oldest);

        await using var read = NewContext();
        var reader = new PaymentDashboardReader(read);
        var open = await reader.OpenCaptureIncidentsAsync();
        var facts = await reader.FinanceAsync(Now.AddDays(-1), Now.AddDays(1));

        Assert.Equal([oldest.Id.Value, newest.Id.Value], open.Select(item => item.IncidentId));
        Assert.Equal(payment.Id.Value, open[0].PaymentId);
        Assert.Equal(booking.Reference.Value, open[0].BookingReference);
        Assert.Equal("SecondCapture", open[0].Kind);
        Assert.Equal(Now.AddHours(1), open[0].DetectedAt);
        Assert.Null(open[1].BookingReference);
        Assert.Equal(2, facts.OpenCaptureIncidents);
    }
}
