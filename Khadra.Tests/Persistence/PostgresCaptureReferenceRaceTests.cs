using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Notifications;
using Khadra.Application.Payments.ReceiveProviderEvent;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// One capture on one attempt, proved on the engine that enforces it (Wave 4, B1; the advisor's review).
/// </summary>
/// <remarks>
/// <para>
/// The webhook reads first — does another attempt already hold this capture? — and records an incident when one
/// does. A read can lose a race: two attempts named under one capture, both read "nobody", both apply. The unique
/// index <c>ux_payments_provider_capture_reference</c> is the floor under that read, and the handler expects to lose
/// to it BY NAME. Three things only PostgreSQL can show: that the index really carries that name (the translation
/// keys on what the server reports), that discarding the tracker really forgets the refused save's mutations (the
/// second save must not write the Applied payment and Confirmed booking again), and that what commits afterwards is
/// a receipt and an incident and nothing else.
/// </para>
/// <para>
/// The race is made deterministic by the one seam that decides it: the loser's read of the capture reference answers
/// "nobody", as it would have a moment before the winner committed. Everything else — the repositories, the unit of
/// work, the translation, the database — is real. Opt-in: set <c>KHADRA_TEST_POSTGRES</c> to a scratch database;
/// it is reused, so every row here carries this run's own ids.
/// </para>
/// </remarks>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresCaptureReferenceRaceTests : IAsyncLifetime
{
    private const string Provider = "PGRACE";
    private static readonly DateTimeOffset Now = Build.Now;
    private DbContextOptions<KhadraDbContext>? _options;

    public async Task InitializeAsync()
    {
        var connectionString = PostgresTestDatabase.ConnectionString;
        if (connectionString is null) return;

        await EnsureDatabaseExistsAsync(connectionString);
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var context = new KhadraDbContext(_options);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private KhadraDbContext NewContext() => new(_options
        ?? throw new InvalidOperationException($"{PostgresTestDatabase.VariableName} is not set."));

    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", target.Database!);
        if (await exists.ExecuteScalarAsync() is not null) return;
        await using var create = new NpgsqlCommand(
            $"CREATE DATABASE \"{target.Database!.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", connection);
        await create.ExecuteNonQueryAsync();
    }

    private static (Booking Booking, Payment Payment) Awaiting(string sessionReference)
    {
        var booking = Build.ApprovedBooking(Now);
        // A copy: EF tracks owned values by reference, and the booking's own figure must not be the payment's too.
        var deposit = Money.Create(booking.Pricing.DepositAmount.Amount, booking.Pricing.DepositAmount.CurrencyCode);
        var payment = Payment.Open(booking.Id, booking.CustomerId, deposit, Provider, Now.AddMinutes(30), Now);
        Assert.True(payment.AttachProviderSession(sessionReference, $"https://provider.test/{sessionReference}").IsSuccess);
        return (booking, payment);
    }

    private static IPaymentProvider Delivering(ProviderEvent notification)
    {
        var provider = Substitute.For<IPaymentProvider>();
        provider.Name.Returns(Provider);
        provider.IsConfigured.Returns(true);
        provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(notification));
        return provider;
    }

    private static ProviderEvent Capture(string eventId, string sessionReference, Money amount, string captureReference) =>
        new(eventId, sessionReference, ProviderEventKind.Captured, Money.Create(amount.Amount, amount.CurrencyCode), null, Now, null, captureReference);

    /// <summary>The real handler over one context, with whatever repositories the race needs in place of the real ones.</summary>
    private static ReceiveProviderEventHandler Handler(
        KhadraDbContext context,
        IPaymentProvider provider,
        IDomainEventDispatcher dispatcher,
        IPaymentRepository? payments = null,
        IProviderEventReceiptRepository? receipts = null) =>
        new(
            provider,
            payments ?? new PaymentRepository(context),
            receipts ?? new ProviderEventReceiptRepository(context),
            new PaymentIncidentRepository(context),
            new BookingRepository(context),
            Substitute.For<IDealerRepository>(),
            new DealerTeamNotifier(Substitute.For<INotifier>(), Substitute.For<IUserRepository>()),
            new TestClock(Now),
            TestPayments.Settings(),
            new UnitOfWork(context, dispatcher),
            NullLogger<ReceiveProviderEventHandler>.Instance);

    private static ReceiveProviderEventCommand Delivery() => new("{}", new Dictionary<string, string>());

    [PostgresFact]
    public async Task The_loser_of_a_race_for_one_capture_answers_with_a_receipt_and_an_incident_and_changes_nothing_else()
    {
        var capture = PostgresTestDatabase.Unique("cap");
        var (winnerBooking, winner) = Awaiting(PostgresTestDatabase.Unique("sess_w"));
        var (loserBooking, loser) = Awaiting(PostgresTestDatabase.Unique("sess_l"));
        await using (var seed = NewContext())
        {
            seed.Bookings.AddRange(winnerBooking, loserBooking);
            seed.Payments.AddRange(winner, loser);
            await seed.SaveChangesAsync();
        }

        // The winner applies the capture and commits.
        var winnerEvent = PostgresTestDatabase.Unique("evt_w");
        await using (var first = NewContext())
        {
            var applied = await Handler(
                    first,
                    Delivering(Capture(winnerEvent, winner.ProviderReference!, winner.Amount, capture)),
                    Substitute.For<IDomainEventDispatcher>())
                .Handle(Delivery(), CancellationToken.None);
            Assert.True(applied.IsSuccess);
        }

        // The loser read "nobody holds it" a moment before that commit; every other read is the database's.
        var loserEvent = PostgresTestDatabase.Unique("evt_l");
        var loserDispatcher = Substitute.For<IDomainEventDispatcher>();
        await using (var second = NewContext())
        {
            var real = new PaymentRepository(second);
            var payments = Substitute.For<IPaymentRepository>();
            payments.GetByProviderReferenceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => real.GetByProviderReferenceAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<CancellationToken>(2)));
            var reads = 0;
            payments.GetByCaptureReferenceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => ++reads == 1
                    ? Task.FromResult<Payment?>(null)
                    : real.GetByCaptureReferenceAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<CancellationToken>(2)));

            var answered = await Handler(
                    second,
                    Delivering(Capture(loserEvent, loser.ProviderReference!, loser.Amount, capture)),
                    loserDispatcher,
                    payments)
                .Handle(Delivery(), CancellationToken.None);

            // 2xx: a 5xx here is a notice the provider redelivers for days.
            Assert.True(answered.IsSuccess);
            Assert.Equal(2, reads);
        }

        await using var read = NewContext();

        // The loser's attempt and booking are exactly as they were: the refused save's mutations were forgotten.
        var storedLoser = await read.Payments.AsNoTracking().SingleAsync(payment => payment.Id == loser.Id);
        Assert.Same(PaymentStatus.Pending, storedLoser.Status);
        Assert.Null(storedLoser.ProviderCaptureReference);
        Assert.Null(storedLoser.AmountCaptured);
        var storedLoserBooking = await read.Bookings.AsNoTracking().SingleAsync(booking => booking.Id == loserBooking.Id);
        Assert.Same(BookingStatus.Approved, storedLoserBooking.Status);
        Assert.Null(storedLoserBooking.DepositPaymentId);

        // What committed: the notice, recorded as a capture on another attempt, and its incident.
        var receipt = await read.ProviderEventReceipts.AsNoTracking()
            .SingleAsync(row => row.Provider == Provider && row.ProviderEventId == loserEvent);
        Assert.Same(ProviderEventOutcome.OtherAttempt, receipt.Outcome);
        Assert.Equal(loser.Id, receipt.PaymentId);
        Assert.Equal(capture, receipt.CaptureReference);
        var incident = await read.PaymentIncidents.AsNoTracking().SingleAsync(row => row.ReceiptId == receipt.Id);
        Assert.Same(PaymentIncidentKind.CaptureOnAnotherAttempt, incident.Kind);
        Assert.Equal(loser.Id, incident.PaymentId);
        Assert.Equal(winner.Id, incident.OtherPaymentId);
        Assert.Equal(capture, incident.CaptureReference);
        Assert.Equal(loser.Amount, incident.Expected);
        Assert.False(incident.IsHandled);
        Assert.Single(await read.PaymentIncidents.AsNoTracking().Where(row => row.PaymentId == loser.Id).ToListAsync());

        // Nothing announced a confirmation that never committed.
        await loserDispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<IReadOnlyCollection<IDomainEvent>>(), Arg.Any<CancellationToken>());

        // And the winner keeps what it took.
        var storedWinner = await read.Payments.AsNoTracking().SingleAsync(payment => payment.Id == winner.Id);
        Assert.Same(PaymentStatus.Applied, storedWinner.Status);
        Assert.Equal(capture, storedWinner.ProviderCaptureReference);
        Assert.Equal(winner.Id, (await read.Bookings.AsNoTracking().SingleAsync(booking => booking.Id == winnerBooking.Id)).DepositPaymentId);
    }

    /// <summary>
    /// One notice raises one incident, even when two deliveries of it race past the replay check: the receipt's index
    /// refuses the second, its incident goes with it, and the second is still answered 2xx.
    /// </summary>
    [PostgresFact]
    public async Task Two_deliveries_of_one_incident_notice_raise_one_incident()
    {
        var (booking, payment) = Awaiting(PostgresTestDatabase.Unique("sess_i"));
        Assert.True(payment.Apply(Money.Create(payment.Amount.Amount, payment.Amount.CurrencyCode), Now, Now, PostgresTestDatabase.Unique("cap_first")).IsSuccess);
        Assert.True(booking.ConfirmDepositPaid(payment.Id, Now).IsSuccess);
        await using (var seed = NewContext())
        {
            seed.Bookings.Add(booking);
            seed.Payments.Add(payment);
            await seed.SaveChangesAsync();
        }

        var notice = Capture(PostgresTestDatabase.Unique("evt_second"), payment.ProviderReference!, payment.Amount, PostgresTestDatabase.Unique("cap_second"));
        for (var delivery = 0; delivery < 2; delivery++)
        {
            await using var context = NewContext();
            // Both deliveries pass the replay check, as two concurrent ones would; only the index can refuse one.
            var receipts = Substitute.For<IProviderEventReceiptRepository>();
            var real = new ProviderEventReceiptRepository(context);
            receipts.HasSeenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
            receipts.When(repository => repository.Add(Arg.Any<ProviderEventReceipt>()))
                .Do(call => real.Add(call.Arg<ProviderEventReceipt>()));

            var answered = await Handler(context, Delivering(notice), Substitute.For<IDomainEventDispatcher>(), receipts: receipts)
                .Handle(Delivery(), CancellationToken.None);
            Assert.True(answered.IsSuccess);
        }

        await using var read = NewContext();
        var receipt = await read.ProviderEventReceipts.AsNoTracking()
            .SingleAsync(row => row.Provider == Provider && row.ProviderEventId == notice.ProviderEventId);
        Assert.Same(ProviderEventOutcome.SecondCapture, receipt.Outcome);
        var incident = Assert.Single(await read.PaymentIncidents.AsNoTracking().Where(row => row.PaymentId == payment.Id).ToListAsync());
        Assert.Same(PaymentIncidentKind.SecondCapture, incident.Kind);
        Assert.Equal(receipt.Id, incident.ReceiptId);
        Assert.Empty((await read.Payments.AsNoTracking().Include(row => row.Refunds).SingleAsync(row => row.Id == payment.Id)).Refunds);
    }
}
