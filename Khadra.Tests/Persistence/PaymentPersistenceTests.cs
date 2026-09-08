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
        Assert.Equal(Money.Jod(18m), stored.RefundedTotal);
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
}
