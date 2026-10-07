using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The payment tables against the real EF model.
/// </summary>
/// <remarks>
/// Two things are being proved here, and only one of them is "it saves". The other is that the
/// DATABASE, not the handler, refuses a second live attempt on one booking and a replayed provider
/// event — both are races a read-then-write loses, and both are the difference between charging a
/// customer once and charging them twice.
/// </remarks>
public sealed class PaymentPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public PaymentPersistenceTests()
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

    private static Payment Pending(Id bookingId, string reference = "sess_1", decimal amount = 18m)
    {
        var payment = Payment.Open(bookingId, Id.New(), Money.Jod(amount), "TestProvider", Now.AddMinutes(30), Now);
        payment.AttachProviderSession(reference, $"https://provider.test/{reference}");
        return payment;
    }

    /// <summary>Every field, because a column that silently does not persist is a fact nobody can recover.</summary>
    [Fact]
    public async Task A_payment_round_trips_with_every_field_and_its_refunds()
    {
        var bookingId = Id.New();
        var payment = Pending(bookingId);
        payment.Orphan(Money.Jod(18m), Now, "BookingExpired", Now.AddSeconds(3));
        var refund = payment.Refunds.Single();
        refund.MarkSent("rf_1", Now.AddMinutes(1));

        await using (var context = NewContext())
        {
            context.Payments.Add(payment);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await new PaymentRepository(reader).GetByIdAsync(payment.Id);

        Assert.NotNull(stored);
        Assert.Equal(bookingId, stored.BookingId);
        Assert.Equal(payment.CustomerId, stored.CustomerId);
        Assert.Equal(Money.Jod(18m), stored.Amount);
        Assert.Same(PaymentStatus.Orphaned, stored.Status);
        Assert.Equal("TestProvider", stored.Provider);
        Assert.Equal("sess_1", stored.ProviderReference);
        Assert.Equal("https://provider.test/sess_1", stored.CheckoutUrl);
        Assert.Equal(Now.AddMinutes(30), stored.ExpiresAt);
        Assert.Equal(Money.Jod(18m), stored.AmountCaptured);
        Assert.Equal(Now, stored.CapturedAt);
        Assert.Equal(Now.AddSeconds(3), stored.OrphanedAt);
        Assert.Null(stored.AppliedAt);
        Assert.Equal("BookingExpired", stored.OrphanReason);
        Assert.Equal(Now, stored.CreatedAt);

        var storedRefund = Assert.Single(stored.Refunds);
        Assert.Equal(refund.Id, storedRefund.Id);
        Assert.Equal(Money.Jod(18m), storedRefund.Amount);
        Assert.Same(RefundReason.OrphanedCapture, storedRefund.Reason);
        Assert.Same(RefundStatus.Sent, storedRefund.Status);
        Assert.Equal("rf_1", storedRefund.ProviderReference);
        // The refund is created at the moment the capture is ORPHANED, not at the moment it was taken.
        Assert.Equal(Now.AddSeconds(3), storedRefund.RequestedAt);
        Assert.Equal(Now.AddMinutes(1), storedRefund.SentAt);
        Assert.Null(storedRefund.DisputeTicketId);
        // Loaded WITH its refunds, so the ceiling on a second refund is computed against the truth.
        Assert.Equal(Money.Jod(18m), stored.RefundedOrOwed);
    }

    /// <summary>
    /// The guard that stops one booking having two card forms open at once.
    /// </summary>
    /// <remarks>
    /// A partial unique index rather than a check in the handler, because the handler's
    /// read-then-insert loses the race between two taps on a slow connection — and what it produces
    /// is a customer who can pay the same deposit twice, with the second capture landing on a booking
    /// that already carries another payment's id.
    /// </remarks>
    [Fact]
    public async Task A_full_payment_round_trips_its_purpose_and_its_fee()
    {
        var bookingId = Id.New();
        var payment = Payment.Open(
            bookingId, Id.New(), Money.Jod(253.75m), "TestProvider", Now.AddMinutes(30), Now,
            PaymentPurpose.FullPayment, Money.Jod(3.75m));

        await using (var context = NewContext())
        {
            context.Payments.Add(payment);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await reader.Payments.SingleAsync(row => row.Id == payment.Id);
        Assert.Same(PaymentPurpose.FullPayment, stored.Purpose);
        Assert.Equal(Money.Jod(3.75m), stored.ProcessingFee);
        Assert.Equal(Money.Jod(250m), stored.AppliedToBooking);
    }

    [Fact]
    public async Task An_attempt_opened_without_a_purpose_is_stored_as_a_deposit_with_no_fee()
    {
        var payment = Pending(Id.New());

        await using (var context = NewContext())
        {
            context.Payments.Add(payment);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await reader.Payments.SingleAsync(row => row.Id == payment.Id);
        Assert.Same(PaymentPurpose.Deposit, stored.Purpose);
        Assert.Equal(0m, stored.ProcessingFee.Amount);
    }

    [Fact]
    public async Task The_database_refuses_a_second_live_attempt_on_one_booking()
    {
        var bookingId = Id.New();

        await using (var context = NewContext())
        {
            context.Payments.Add(Pending(bookingId, "sess_1"));
            await context.SaveChangesAsync();
        }

        await using var second = NewContext();
        second.Payments.Add(Pending(bookingId, "sess_2"));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    /// <summary>
    /// The terminal rows must be allowed to pile up: a customer whose card is declined three times is
    /// entitled to a fourth attempt.
    /// </summary>
    [Fact]
    public async Task A_finished_attempt_does_not_block_the_next_one()
    {
        var bookingId = Id.New();
        var first = Pending(bookingId, "sess_1");
        first.Fail("card_declined", Now);

        await using (var context = NewContext())
        {
            context.Payments.Add(first);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            context.Payments.Add(Pending(bookingId, "sess_2"));
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new PaymentRepository(reader);
        Assert.Equal(2, (await repository.ListForBookingAsync(bookingId)).Count);
        // And exactly one of them is the one a customer is paying through.
        var live = await repository.GetLiveForBookingAsync(bookingId);
        Assert.Equal("sess_2", live!.ProviderReference);
    }

    /// <summary>
    /// The replay guard for the whole feature. It is the INDEX that refuses a duplicate, not a read:
    /// a check-then-write leaves a window in which two concurrent deliveries both pass the check.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_a_provider_event_it_has_already_recorded()
    {
        await using (var context = NewContext())
        {
            context.ProviderEventReceipts.Add(ProviderEventReceipt.Record(
                "TestProvider", "evt_1", "sess_1", "Captured", Id.New(), ProviderEventOutcome.Acted, Money.Jod(18m), Now));
            await context.SaveChangesAsync();
        }

        await using var replay = NewContext();
        replay.ProviderEventReceipts.Add(ProviderEventReceipt.Record(
            "TestProvider", "evt_1", "sess_1", "Captured", Id.New(), ProviderEventOutcome.Acted, Money.Jod(18m), Now));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => replay.SaveChangesAsync());
    }

    /// <summary>
    /// The same event id from a DIFFERENT provider is a different event. Providers do not share an
    /// id space, and treating them as if they did would silently drop real payments after a switch.
    /// </summary>
    [Fact]
    public async Task The_same_event_id_from_another_provider_is_a_different_event()
    {
        await using var context = NewContext();
        context.ProviderEventReceipts.Add(ProviderEventReceipt.Record(
            "ProviderA", "evt_1", "sess_1", "Captured", null, ProviderEventOutcome.Unknown, null, Now));
        context.ProviderEventReceipts.Add(ProviderEventReceipt.Record(
            "ProviderB", "evt_1", "sess_1", "Captured", null, ProviderEventOutcome.Unknown, null, Now));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.ProviderEventReceipts.CountAsync());
    }

    /// <summary>
    /// A reference is only unique WITHIN a provider, and a row that never got one must not collide
    /// with every other row that never got one.
    /// </summary>
    [Fact]
    public async Task Attempts_that_never_reached_a_provider_do_not_collide_on_a_null_reference()
    {
        await using var context = NewContext();
        context.Payments.Add(Payment.Open(Id.New(), Id.New(), Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now));
        context.Payments.Add(Payment.Open(Id.New(), Id.New(), Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Payments.CountAsync());
    }

    [Fact]
    public async Task The_sweep_finds_attempts_whose_session_should_be_dead()
    {
        var lapsed = Pending(Id.New(), "sess_old");
        var live = Pending(Id.New(), "sess_new");
        var finished = Pending(Id.New(), "sess_done");
        finished.Fail("card_declined", Now);

        await using (var context = NewContext())
        {
            context.Payments.AddRange(lapsed, live, finished);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        // Everything whose expiry is before this instant. `live` and `lapsed` share an expiry, so the
        // cutoff is what separates them; `finished` is excluded by status whatever its expiry says.
        var stale = await new PaymentRepository(reader).ListStaleLiveAsync(Now.AddMinutes(31));

        Assert.Equal(2, stale.Count);
        Assert.DoesNotContain(stale, payment => payment.Id == finished.Id);
    }

    [Fact]
    public async Task Outstanding_refunds_are_found_whether_they_are_new_or_previously_refused()
    {
        var owed = Pending(Id.New(), "sess_1");
        owed.Orphan(Money.Jod(18m), Now, "BookingExpired", Now);

        var refused = Pending(Id.New(), "sess_2");
        refused.Orphan(Money.Jod(18m), Now, "BookingCancelled", Now);
        refused.Refunds.Single().MarkFailed("insufficient_funds", Now);

        var settled = Pending(Id.New(), "sess_3");
        settled.Orphan(Money.Jod(18m), Now, "BookingExpired", Now);
        settled.Refunds.Single().MarkSettled(Now);

        await using (var context = NewContext())
        {
            context.Payments.AddRange(owed, refused, settled);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var outstanding = await new PaymentRepository(reader).ListWithOutstandingRefundsAsync();

        Assert.Equal(2, outstanding.Count);
        Assert.DoesNotContain(outstanding, payment => payment.Id == settled.Id);
    }

    [Fact]
    public async Task A_payment_is_found_by_its_provider_reference_only_for_the_provider_that_issued_it()
    {
        var payment = Pending(Id.New(), "sess_1");

        await using (var context = NewContext())
        {
            context.Payments.Add(payment);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new PaymentRepository(reader);

        Assert.NotNull(await repository.GetByProviderReferenceAsync("TestProvider", "sess_1"));
        // A reference means nothing outside the provider that issued it.
        Assert.Null(await repository.GetByProviderReferenceAsync("AnotherProvider", "sess_1"));
    }

    [Fact]
    public async Task A_free_cancellation_refund_round_trips_its_reason_and_status()
    {
        var payment = Pending(Id.New());
        Assert.True(payment.Apply(Money.Jod(18m), Now, Now).IsSuccess);
        var refund = payment.RefundForFreeCancellation(Now.AddMinutes(2)).Value;
        refund.MarkFailed("refund_declined", Now.AddMinutes(3));

        await using (var context = NewContext())
        {
            context.Payments.Add(payment);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await new PaymentRepository(reader).GetByIdAsync(payment.Id);

        var storedRefund = Assert.Single(stored!.Refunds);
        Assert.Same(RefundReason.FreeCancellation, storedRefund.Reason);
        Assert.Same(RefundStatus.Failed, storedRefund.Status);
        Assert.Equal("refund_declined", storedRefund.FailureCode);
        Assert.Equal(Money.Jod(18m), storedRefund.Amount);
        Assert.Equal(Now.AddMinutes(2), storedRefund.RequestedAt);
        // Still owed: the idempotent call returns this row rather than a second one.
        Assert.Same(storedRefund, stored.RefundForFreeCancellation(Now.AddMinutes(4)).Value);
    }

    /// <summary>
    /// The production order: the payment is LOADED (its captured amount tracked), refunded, and saved.
    /// The refund must own its own amount, not the payment's tracked instance.
    /// </summary>
    [Fact]
    public async Task A_free_cancellation_refund_on_a_loaded_payment_saves_and_reloads_whole()
    {
        var payment = Pending(Id.New());
        Assert.True(payment.Apply(Money.Jod(18m), Now, Now).IsSuccess);
        await using (var context = NewContext())
        {
            context.Payments.Add(payment);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var loaded = await new PaymentRepository(context).GetByIdAsync(payment.Id);
            Assert.True(loaded!.RefundForFreeCancellation(Now.AddMinutes(1)).IsSuccess);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await new PaymentRepository(reader).GetByIdAsync(payment.Id);
        Assert.Equal(Money.Jod(18m), stored!.AmountCaptured);
        var refund = Assert.Single(stored.Refunds);
        Assert.Equal(Money.Jod(18m), refund.Amount);
        Assert.Same(RefundReason.FreeCancellation, refund.Reason);
    }

    // ------------------------------------------------------------------ capture references and incidents (Wave 4, B1)

    private static Payment Applied(string reference, string? captureReference, string provider = "TestProvider")
    {
        var payment = Payment.Open(Id.New(), Id.New(), Money.Jod(18m), provider, Now.AddMinutes(30), Now);
        payment.AttachProviderSession(reference, $"https://provider.test/{reference}");
        Assert.True(payment.Apply(Money.Jod(18m), Now, Now, captureReference).IsSuccess);
        return payment;
    }

    private async Task<(Payment Payment, ProviderEventReceipt Receipt)> StoredWithReceiptAsync(string eventId = "evt_2")
    {
        var payment = Applied("sess_1", "cap_1");
        var receipt = ProviderEventReceipt.Record(
            "TestProvider", eventId, "sess_1", "Captured", payment.Id, ProviderEventOutcome.SecondCapture, Money.Jod(20m), Now, "cap_2");
        await using var context = NewContext();
        context.Payments.Add(payment);
        context.ProviderEventReceipts.Add(receipt);
        await context.SaveChangesAsync();
        return (payment, receipt);
    }

    /// <summary>The capture's own id persists on the payment and on the receipt, and finds the payment within its provider only.</summary>
    [Fact]
    public async Task A_capture_reference_round_trips_and_finds_its_payment_within_its_provider()
    {
        var payment = Applied("sess_1", "cap_1");
        await using (var context = NewContext())
        {
            context.Payments.Add(payment);
            context.ProviderEventReceipts.Add(ProviderEventReceipt.Record(
                "TestProvider", "evt_1", "sess_1", "Captured", payment.Id, ProviderEventOutcome.Acted, Money.Jod(18m), Now, " cap_1 "));
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new PaymentRepository(reader);
        var found = await repository.GetByCaptureReferenceAsync("TestProvider", "cap_1");
        Assert.Equal(payment.Id, found?.Id);
        Assert.Equal("cap_1", found!.ProviderCaptureReference);
        Assert.Null(await repository.GetByCaptureReferenceAsync("OtherProvider", "cap_1"));
        Assert.Null(await repository.GetByCaptureReferenceAsync("TestProvider", "cap_2"));
        Assert.Equal("cap_1", (await reader.ProviderEventReceipts.SingleAsync()).CaptureReference);
    }

    /// <summary>
    /// One capture on one attempt, refused by the DATABASE: the webhook's read can lose a race, and this index is what
    /// the losing save meets. Within a provider only, and payments that never named a capture never collide.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_one_capture_on_two_attempts()
    {
        await using (var context = NewContext())
        {
            context.Payments.Add(Applied("sess_1", "cap_1"));
            context.Payments.Add(Applied("sess_2", "cap_1", provider: "OtherProvider"));
            context.Payments.Add(Applied("sess_3", captureReference: null));
            context.Payments.Add(Applied("sess_4", captureReference: null));
            await context.SaveChangesAsync();
        }

        await using var racer = NewContext();
        racer.Payments.Add(Applied("sess_5", "cap_1"));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => racer.SaveChangesAsync());
    }

    /// <summary>Every field of an incident, open and then handled, because a column that does not persist is a fact lost.</summary>
    [Fact]
    public async Task An_incident_round_trips_with_every_field_open_and_handled()
    {
        var (payment, receipt) = await StoredWithReceiptAsync();
        var other = Id.New();
        var incident = PaymentIncident.Raise(
            PaymentIncidentKind.CaptureOnAnotherAttempt, payment.Id, receipt.Id, "TestProvider", "cap_2",
            Money.Jod(20m), Money.Jod(18m), other, Now.AddMinutes(1));
        await using (var context = NewContext())
        {
            new PaymentIncidentRepository(context).Add(incident);
            await context.SaveChangesAsync();
        }

        await using (var reader = NewContext())
        {
            var open = await new PaymentIncidentRepository(reader).GetByIdAsync(incident.Id);
            Assert.NotNull(open);
            Assert.Same(PaymentIncidentKind.CaptureOnAnotherAttempt, open.Kind);
            Assert.Equal(payment.Id, open.PaymentId);
            Assert.Equal(receipt.Id, open.ReceiptId);
            Assert.Equal("TestProvider", open.Provider);
            Assert.Equal("cap_2", open.CaptureReference);
            Assert.Equal(Money.Jod(20m), open.Reported);
            Assert.Equal(Money.Jod(18m), open.Expected);
            Assert.Equal(other, open.OtherPaymentId);
            Assert.Equal(Now.AddMinutes(1), open.DetectedAt);
            Assert.False(open.IsHandled);
            Assert.Null(open.HandledByAdminId);
            Assert.Null(open.HandledNote);

            var admin = Id.New();
            Assert.True(open.MarkHandled(admin, "Refunded at the provider.", Now.AddHours(1)).IsSuccess);
            await reader.SaveChangesAsync();
        }

        await using var again = NewContext();
        var handled = await new PaymentIncidentRepository(again).GetByIdAsync(incident.Id);
        Assert.True(handled!.IsHandled);
        Assert.Equal(Now.AddHours(1), handled.HandledAt);
        Assert.NotNull(handled.HandledByAdminId);
        Assert.Equal("Refunded at the provider.", handled.HandledNote);
        Assert.Equal(Money.Jod(20m), handled.Reported);
    }

    /// <summary>One notice raises one incident at most: the receipt is unique on the incident table.</summary>
    [Fact]
    public async Task The_database_refuses_a_second_incident_for_one_notice()
    {
        var (payment, receipt) = await StoredWithReceiptAsync();
        await using (var context = NewContext())
        {
            context.PaymentIncidents.Add(PaymentIncident.Raise(
                PaymentIncidentKind.SecondCapture, payment.Id, receipt.Id, "TestProvider", "cap_2",
                Money.Jod(20m), Money.Jod(18m), null, Now));
            await context.SaveChangesAsync();
        }

        await using var second = NewContext();
        second.PaymentIncidents.Add(PaymentIncident.Raise(
            PaymentIncidentKind.SecondCapture, payment.Id, receipt.Id, "TestProvider", "cap_2",
            Money.Jod(20m), Money.Jod(18m), null, Now));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    /// <summary>
    /// The production order: the payment is LOADED and tracked, and the incident is raised from its captured amount.
    /// The incident must own its own money, not the payment's tracked instance — or one of them saves null.
    /// </summary>
    [Fact]
    public async Task An_incident_raised_from_a_loaded_payments_money_saves_both_whole()
    {
        var (payment, receipt) = await StoredWithReceiptAsync();

        await using (var context = NewContext())
        {
            var loaded = await new PaymentRepository(context).GetByIdAsync(payment.Id);
            var taken = loaded!.AmountCaptured!;
            context.PaymentIncidents.Add(PaymentIncident.Raise(
                PaymentIncidentKind.SecondCapture, loaded.Id, receipt.Id, "TestProvider", "cap_2",
                taken, taken, null, Now));
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        Assert.Equal(Money.Jod(18m), (await new PaymentRepository(reader).GetByIdAsync(payment.Id))!.AmountCaptured);
        var stored = await reader.PaymentIncidents.SingleAsync();
        Assert.Equal(Money.Jod(18m), stored.Reported);
        Assert.Equal(Money.Jod(18m), stored.Expected);
    }

    // ------------------------------------------------------------------ a refused refund waits (Wave 4, B4)

    private static Payment WithOwedRefund(string reference, DateTimeOffset createdAt)
    {
        var payment = Payment.Open(Id.New(), Id.New(), Money.Jod(18m), "TestProvider", createdAt.AddMinutes(30), createdAt);
        payment.AttachProviderSession(reference, $"https://provider.test/{reference}");
        Assert.True(payment.Orphan(Money.Jod(18m), createdAt, "BookingExpired", createdAt).IsSuccess);
        return payment;
    }

    [Fact]
    public async Task A_refunds_refusals_and_its_next_attempt_round_trip()
    {
        var payment = WithOwedRefund("sess_1", Now);
        var refund = Assert.Single(payment.Refunds);
        refund.RecordRefusedSend("card_closed", Now, TestPayments.RetryPolicy);
        refund.RecordRefusedSend("card_closed", Now.AddMinutes(1), TestPayments.RetryPolicy);
        await using (var context = NewContext())
        {
            context.Payments.Add(payment);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = Assert.Single((await new PaymentRepository(reader).GetByIdAsync(payment.Id))!.Refunds);
        Assert.Equal(2, stored.RefusalCount);
        Assert.Equal(Now.AddMinutes(3), stored.NextAttemptAt);
        Assert.Equal(Now.AddMinutes(1), stored.FailedAt);
        Assert.Equal("card_closed", stored.FailureCode);
    }

    /// <summary>
    /// The sweep's list: a payment whose refund is owed and due, by its own schedule — never one still waiting, sent,
    /// settled, or with no provider reference to send against. Oldest payment first.
    /// </summary>
    [Fact]
    public async Task The_sweep_lists_only_payments_with_a_refund_due_now()
    {
        var requested = WithOwedRefund("sess_requested", Now.AddMinutes(-10));
        var waiting = WithOwedRefund("sess_waiting", Now.AddMinutes(-9));
        waiting.Refunds.Single().RecordRefusedSend("card_closed", Now, TestPayments.RetryPolicy);
        var waited = WithOwedRefund("sess_waited", Now.AddMinutes(-8));
        waited.Refunds.Single().RecordRefusedSend("card_closed", Now.AddMinutes(-1), TestPayments.RetryPolicy);
        var sent = WithOwedRefund("sess_sent", Now.AddMinutes(-7));
        sent.Refunds.Single().MarkSent("rf_sent", Now);
        var settled = WithOwedRefund("sess_settled", Now.AddMinutes(-6));
        settled.Refunds.Single().MarkSettled(Now);
        await using (var context = NewContext())
        {
            context.Payments.AddRange(waited, sent, settled, waiting, requested);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var due = await new PaymentRepository(reader).ListIdsWithRefundsDueAsync(Now);

        Assert.Equal([requested.Id, waited.Id], due);
        // The one still waiting becomes due the moment its wait is over, and not before.
        Assert.DoesNotContain(waiting.Id, await new PaymentRepository(reader).ListIdsWithRefundsDueAsync(Now.AddSeconds(59)));
        Assert.Contains(waiting.Id, await new PaymentRepository(reader).ListIdsWithRefundsDueAsync(Now.AddMinutes(1)));
    }
}
